using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using System.Text.Json;

namespace Marrow.Core.Port.Accounts;

// PORT-ONLY: OD-0114, OD-0116; ED-403. Metadata shares MA-1 identities; secrets stay in the vault.
public sealed class AccountKeyStore : IApiKeyStore, IApiKeyReservedSpend
{
    public const string SettingsKey = "multiAccountApiKeys";
    private readonly AccountStore accounts;
    private readonly ISettingsStore settings;
    private readonly Func<Guid, string?> secret;
    private readonly Lock gate = new();
    private AccountKeyDocument document = new();
    public bool SaveBlocked { get; }
    public event Action? Changed;

    public AccountKeyStore(AccountStore accounts, ISettingsStore settings, Func<Guid, string?> secret)
    {
        this.accounts = accounts;
        this.settings = settings;
        this.secret = secret;
        SaveBlocked = accounts.SaveBlocked;
        if (settings.Object(SettingsKey) is not { } saved) return;
        try
        {
            if (saved is not byte[] bytes) throw new InvalidDataException("Unsupported API key settings.");
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Version", out var version)
                || !version.TryGetInt32(out var number) || number != 1
                || !json.RootElement.TryGetProperty("Orders", out _) || !json.RootElement.TryGetProperty("Caps", out _)
                || !json.RootElement.TryGetProperty("Spend", out _)) throw new InvalidDataException("Unsupported API key settings.");
            var loaded = JsonSerializer.Deserialize<AccountKeyDocument>(bytes) ?? throw new InvalidDataException("Empty API key settings.");
            Validate(loaded);
            document = loaded;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            SaveBlocked = true; // Preserve the original bytes. Unknown caps cannot grant rotation permission.
        }
    }

    public IReadOnlyList<ApiKeyEntry> Keys(string service)
    {
        if (SaveBlocked || SwiftEnum<ProviderKind>.FromRawValue(service) is not { IsAPIKeyBased: true } provider) return [];
        lock (gate) return accounts.ForService(provider).Select(account => new ApiKeyEntry(account.Id.ToString("D"), service,
            account.CustomName ?? accounts.DefaultName(account.Id), account.Enabled,
            document.Caps.TryGetValue(account.Id, out var cap) ? cap : null)).ToArray();
    }

    public KeyOrderMode Order(string service) { lock (gate) return document.Orders.GetValueOrDefault(service); }
    public string? Secret(string keyId) => !SaveBlocked && Guid.TryParse(keyId, out var id)
        && accounts.Accounts.Any(account => account.Id == id && account.Service.IsAPIKeyBased) ? secret(id) : null;

    public void SetOrder(string service, KeyOrderMode order)
    {
        if (SwiftEnum<ProviderKind>.FromRawValue(service) is not { IsAPIKeyBased: true } || !Enum.IsDefined(order))
            throw new ArgumentException("Invalid API key order.", nameof(order));
        lock (gate)
        {
            var orders = new Dictionary<string, KeyOrderMode>(document.Orders, StringComparer.Ordinal) { [service] = order };
            Save(document with { Orders = orders });
        }
        Changed?.Invoke();
    }

    public void SetCap(Guid id, double? cap)
    {
        if (cap is { } value && (!double.IsFinite(value) || value < 0)) throw new ArgumentOutOfRangeException(nameof(cap));
        if (!accounts.Get(id).Service.IsAPIKeyBased) throw new ArgumentException("The account is not an API key.", nameof(id));
        lock (gate)
        {
            var caps = new Dictionary<Guid, double>(document.Caps);
            if (cap is { } amount) caps[id] = amount; else caps.Remove(id);
            Save(document with { Caps = caps });
        }
        Changed?.Invoke();
    }

    public double SpendSince(string keyId, DateTimeOffset since)
    {
        if (!Guid.TryParse(keyId, out var id)) return 0;
        lock (gate) return document.Spend.Where(item => item.Key == id && item.At >= since).Sum(item => item.Usd)
                + (document.Reservations ?? []).Where(item => item.Key == id).Sum(item => item.Usd ?? 0);
    }

    public double BudgetSince(string keyId, DateTimeOffset since)
    {
        if (!Guid.TryParse(keyId, out var id)) return 0;
        lock (gate) return (document.Reservations ?? []).Any(item => item.Key == id && item.Usd is null) ? double.PositiveInfinity : SpendSince(keyId, since);
    }

    public bool HasUnsettled(string keyId)
    {
        if (!Guid.TryParse(keyId, out var id)) return false;
        lock (gate) return (document.Reservations ?? []).Any(item => item.Key == id);
    }

    public void Hold(Guid request, string keyId, double? maximumUsd, DateTimeOffset at)
    {
        if (request == Guid.Empty || !Guid.TryParse(keyId, out var key) || key == Guid.Empty || maximumUsd is { } amount && (!double.IsFinite(amount) || amount < 0))
            throw new ArgumentException("Invalid API budget reservation.");
        lock (gate) Save(document with { Reservations = [.. document.Reservations ?? [], new(request, key, maximumUsd, at)] });
        Changed?.Invoke();
    }
    public void Cancel(Guid request)
    {
        lock (gate)
        {
            var remaining = (document.Reservations ?? []).Where(item => item.Request != request).ToArray();
            Save(document with { Reservations = remaining.Length == 0 ? null : remaining });
        }
        Changed?.Invoke();
    }
    public void Settle(Guid request, double actualUsd)
    {
        if (!double.IsFinite(actualUsd) || actualUsd < 0) throw new ArgumentOutOfRangeException(nameof(actualUsd));
        lock (gate)
        {
            if ((document.Reservations ?? []).FirstOrDefault(item => item.Request == request) is not { } held) return;
            var remaining = (document.Reservations ?? []).Where(item => item.Request != request).ToArray();
            Save(document with { Reservations = remaining.Length == 0 ? null : remaining, Spend = [.. document.Spend, new(held.Key, actualUsd, held.At)] });
        }
        Changed?.Invoke();
    }

    public void Add(string keyId, double usd, DateTimeOffset at)
    {
        if (!double.IsFinite(usd) || usd < 0) throw new ArgumentOutOfRangeException(nameof(usd));
        if (!Guid.TryParse(keyId, out var id) || id == Guid.Empty) throw new ArgumentException("A key identity is required.", nameof(keyId));
        lock (gate) Save(document with { Spend = [.. document.Spend, new(id, usd, at)] });
        Changed?.Invoke();
    }

    private void Save(AccountKeyDocument next)
    {
        if (SaveBlocked) throw new InvalidOperationException("API key settings could not be read; the original value was preserved.");
        Validate(next);
        settings.Set(SettingsKey, JsonSerializer.SerializeToUtf8Bytes(next));
        document = next;
    }

    private static void Validate(AccountKeyDocument value)
    {
        if (value.Version != 1 || value.Orders is null || value.Caps is null || value.Spend is null)
            throw new InvalidDataException("Unsupported API key settings.");
        foreach (var (service, order) in value.Orders)
            if (SwiftEnum<ProviderKind>.FromRawValue(service) is not { IsAPIKeyBased: true } || !Enum.IsDefined(order))
                throw new InvalidDataException("Invalid API key order.");
        foreach (var (id, cap) in value.Caps)
            if (id == Guid.Empty || !double.IsFinite(cap) || cap < 0) throw new InvalidDataException("Invalid API key cap.");
        foreach (var item in value.Spend)
            if (item is null || item.Key == Guid.Empty || !double.IsFinite(item.Usd) || item.Usd < 0)
                throw new InvalidDataException("Invalid API key spend.");
        var requests = new HashSet<Guid>();
        foreach (var item in value.Reservations ?? [])
            if (item is null || item.Request == Guid.Empty || !requests.Add(item.Request) || item.Key == Guid.Empty || item.Usd is { } amount && (!double.IsFinite(amount) || amount < 0))
                throw new InvalidDataException("Invalid API key reservation.");
    }
}
