// PORT-ONLY: OD-0114 / ED-404. Account metadata in settings; credentials remain CLI-owned or in APIKeychain.
using System.Globalization;
using System.Text.Json;
using Marrow.Core.Models;

namespace Marrow.Core.Port.Accounts;

public sealed class AccountStore
{
    public const string SettingsKey = "multiAccounts";
    public event Action? Changed;
    private readonly ISettingsStore settings;
    private volatile AccountDocument document;
    private readonly object gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new AccountServiceConverter() } };

    public AccountStore(ISettingsStore settings, Func<ProviderKind, AccountDefaults>? legacyDefaults = null)
    {
        this.settings = settings;
        if (settings.Object(SettingsKey) is { } saved)
        {
            try
            {
                if (saved is not byte[] bytes) throw new InvalidDataException("Account settings have an unsupported type.");
                using var json = JsonDocument.Parse(bytes);
                if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Version", out var version) ||
                    version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                    throw new InvalidDataException("Unsupported account document version.");
                document = JsonSerializer.Deserialize<AccountDocument>(bytes, JsonOptions) ?? throw new InvalidDataException("Account settings are empty.");
                Validate(document);
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidDataException or NullReferenceException)
            {
                throw new InvalidDataException("Account settings could not be read; the original value was preserved.", e);
            }
        }
        else
        {
            document = new AccountDocument
            {
                Accounts = SwiftEnum<ProviderKind>.AllCases.Select(p => new Account
                {
                    Id = Guid.NewGuid(), Service = p, UsesLegacyCredentials = true,
                    Defaults = legacyDefaults?.Invoke(p) ?? new(),
                }).ToArray(),
            };
            Save(document);
        }
    }

    // ED-403: a corrupt/newer document keeps single-account launches available, but cannot be replaced by editing a recovered row.
    public bool SaveBlocked { get; private init; }

    public static AccountStore LegacyReadOnly(ISettingsStore settings, Func<ProviderKind, AccountDefaults>? defaults = null) => new(settings,
        new AccountDocument { Accounts = SwiftEnum<ProviderKind>.AllCases.Select(provider => new Account
        {
            Id = Guid.NewGuid(), Service = provider, UsesLegacyCredentials = true, Defaults = defaults?.Invoke(provider) ?? new(),
        }).ToArray() });

    private AccountStore(ISettingsStore settings, AccountDocument recovered)
    {
        this.settings = settings;
        document = recovered;
        SaveBlocked = true;
    }

    /// <summary>Creation order is separate from priority, so reordering does not rename identities.</summary>
    public IReadOnlyList<Account> Accounts => Array.AsReadOnly(document.Accounts);
    public IReadOnlyList<Account> ForService(ProviderKind service) => Array.AsReadOnly(document.Accounts
        .Where(a => a.Service == service).OrderBy(a => a.Priority).ToArray());
    public Account Get(Guid id) => document.Accounts.FirstOrDefault(a => a.Id == id) ?? throw new KeyNotFoundException("Unknown account.");

    public HandshakePoint GeneralHandshakePoint
    {
        get => document.GeneralHandshakePoint;
        set
        {
            lock (gate) Save(document with { GeneralHandshakePoint = value ?? throw new ArgumentNullException(nameof(value)) });
            Changed?.Invoke();
        }
    }

    public HandshakePoint EffectiveHandshakePoint(Guid id) => Get(id).Defaults.HandshakePoint ?? GeneralHandshakePoint;

    public Account Add(ProviderKind service, string? plan = null)
    {
        Account account;
        lock (gate)
        {
            if (!SwiftEnum<ProviderKind>.AllCases.Contains(service)) throw new ArgumentOutOfRangeException(nameof(service));
            var existing = ForService(service);
            if (existing.Count != 0 && !AccountCapabilities.SupportsMultipleAccounts(service))
                throw new InvalidOperationException("This service supports one account.");
            account = new Account { Id = Guid.NewGuid(), Service = service, Plan = NormalizePlan(plan), Priority = existing.Count };
            Save(document with { Accounts = [.. document.Accounts, account] });
        }
        Changed?.Invoke();
        return account;
    }

    public void Rename(Guid id, string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (name is not null && new StringInfo(name).LengthInTextElements > 24)
            throw new ArgumentOutOfRangeException(nameof(name), "Account names can contain at most 24 characters.");
        Update(id, a => a with { CustomName = name });
    }

    public void SetPlan(Guid id, string? plan) => Update(id, a => a with { Plan = NormalizePlan(plan) });
    public void SetEnabled(Guid id, bool enabled) => Update(id, a => a with { Enabled = enabled });
    public void SetDefaults(Guid id, AccountDefaults defaults) => Update(id, a => a with { Defaults = defaults ?? throw new ArgumentNullException(nameof(defaults)) });
    public void MarkSignedIn(Guid id, bool signedIn = true) => Update(id, a => a with { HasSignedIn = signedIn });

    public void Remove(Guid id)
    {
        lock (gate)
        {
            var account = Get(id);
            var remaining = document.Accounts.Where(a => a.Id != id).ToArray();
            var order = remaining.Where(a => a.Service == account.Service).OrderBy(a => a.Priority).Select(a => a.Id).ToArray();
            Save(document with { Accounts = remaining.Select(a => a.Service == account.Service ? a with { Priority = Array.IndexOf(order, a.Id) } : a).ToArray() });
        }
        Changed?.Invoke();
    }

    public void Reorder(ProviderKind service, IReadOnlyList<Guid> ids)
    {
        lock (gate)
        {
            var current = ForService(service);
            if (ids.Count != current.Count || ids.Distinct().Count() != ids.Count || !current.Select(a => a.Id).ToHashSet().SetEquals(ids))
                throw new ArgumentException("The order must contain every account for this service exactly once.", nameof(ids));
            var ranks = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            Save(document with { Accounts = document.Accounts.Select(a => a.Service == service ? a with { Priority = ranks[a.Id] } : a).ToArray() });
        }
        Changed?.Invoke();
    }

    /// <summary>A disabled pin is refused, never silently substituted. Automatic order skips disabled rows.</summary>
    public Account? Choose(ProviderKind service, Guid? pinned = null) => pinned is { } id
        ? document.Accounts.FirstOrDefault(a => a.Id == id && a.Service == service && a.Enabled)
        : ForService(service).FirstOrDefault(a => a.Enabled);

    public string DefaultName(Guid id)
    {
        var account = Get(id);
        var name = BaseName(account);
        var matches = document.Accounts.Where(a => a.Service == account.Service && string.Equals(BaseName(a), name, StringComparison.Ordinal)).ToArray();
        return matches.Length > 1 ? name + " " + (Array.FindIndex(matches, a => a.Id == id) + 1).ToString(CultureInfo.InvariantCulture) : name;
    }

    public string? DisplayName(Guid id)
    {
        var account = Get(id);
        return ForService(account.Service).Count < 2 ? null : account.CustomName ?? DefaultName(id);
    }

    private static string BaseName(Account account) => account.Service == ProviderKind.Claude && account.Plan is { Length: > 0 } plan
        ? "Claude " + plan : account.Service.DisplayName;
    private static string? NormalizePlan(string? plan) => string.IsNullOrWhiteSpace(plan) ? null : plan.Trim();

    private void Update(Guid id, Func<Account, Account> update)
    {
        lock (gate)
        {
            _ = Get(id);
            Save(document with { Accounts = document.Accounts.Select(a => a.Id == id ? update(a) : a).ToArray() });
        }
        Changed?.Invoke();
    }

    private void Save(AccountDocument next)
    {
        if (SaveBlocked) throw new InvalidOperationException("Account settings could not be read; the original value was preserved.");
        Validate(next);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions);
        settings.Set(SettingsKey, bytes);
        document = next;
    }

    private static void Validate(AccountDocument value)
    {
        if (value.Version != 1 || value.Accounts is null || value.GeneralHandshakePoint is null)
            throw new InvalidDataException("Unsupported account document.");
        var ids = new HashSet<Guid>();
        foreach (var a in value.Accounts)
        {
            if (a is null || a.Id == Guid.Empty || !ids.Add(a.Id) || !SwiftEnum<ProviderKind>.AllCases.Contains(a.Service) || a.Defaults is null || a.Priority < 0 ||
                (a.CustomName is not null && new StringInfo(a.CustomName).LengthInTextElements > 24))
                throw new InvalidDataException("Invalid account metadata.");
        }
        foreach (var group in value.Accounts.GroupBy(a => a.Service))
        {
            if ((!AccountCapabilities.SupportsMultipleAccounts(group.Key) && group.Count() > 1) || group.Count(a => a.UsesLegacyCredentials) > 1 ||
                !group.Select(a => a.Priority).Order().SequenceEqual(Enumerable.Range(0, group.Count())))
                throw new InvalidDataException("Invalid account order or service capability.");
        }
    }
}

internal sealed record AccountDocument
{
    public int Version { get; init; } = 1;
    public required Account[] Accounts { get; init; }
    public HandshakePoint GeneralHandshakePoint { get; init; } = HandshakePoint.Smart;
}
