namespace Marrow.Core.Accounts.Keys;

/// <summary>The in-memory <see cref="IApiKeyStore"/> until MA-1's store is joined.</summary>
public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly List<ApiKeyEntry> keys = [];
    private readonly Dictionary<string, string> secrets = [];
    private readonly Dictionary<string, KeyOrderMode> modes = [];

    public IReadOnlyList<ApiKeyEntry> Keys(string service) => [.. keys.Where(k => k.Service == service)];

    public KeyOrderMode Order(string service) => modes.GetValueOrDefault(service);

    public string? Secret(string keyId) => secrets.GetValueOrDefault(keyId);

    public InMemoryApiKeyStore Add(ApiKeyEntry entry, string? secret)
    {
        keys.Add(entry);
        if (secret is not null) secrets[entry.Id] = secret;
        return this;
    }

    public void Update(string id, Func<ApiKeyEntry, ApiKeyEntry> change)
    {
        var at = keys.FindIndex(k => k.Id == id);
        keys[at] = change(keys[at]);
    }

    public void SetOrder(string service, KeyOrderMode mode) => modes[service] = mode;

    public void SetSecret(string keyId, string? secret)
    {
        if (secret is null) secrets.Remove(keyId);
        else secrets[keyId] = secret;
    }
}

public sealed class InMemoryApiKeyCredits : IApiKeyCredits
{
    private readonly Dictionary<string, double> credits = [];

    public void Set(string keyId, double? left)
    {
        if (left is { } v) credits[keyId] = v;
        else credits.Remove(keyId);
    }

    public double? CreditsLeft(string keyId) => credits.TryGetValue(keyId, out var v) ? v : null;
}

public sealed class InMemoryApiKeySpend : IApiKeyReservedSpend
{
    private readonly List<(string Key, double Usd, DateTimeOffset At)> entries = [];
    private readonly Dictionary<Guid, (string Key, double? Usd, DateTimeOffset At)> holds = [];

    public double SpendSince(string keyId, DateTimeOffset since) =>
        entries.Where(e => e.Key == keyId && e.At >= since).Sum(e => e.Usd) + holds.Values.Where(e => e.Key == keyId).Sum(e => e.Usd ?? 0);
    public double BudgetSince(string keyId, DateTimeOffset since) =>
        holds.Values.Any(e => e.Key == keyId && e.Usd is null) ? double.PositiveInfinity : SpendSince(keyId, since);

    public void Add(string keyId, double usd, DateTimeOffset at) => entries.Add((keyId, usd, at));
    public void Hold(Guid request, string keyId, double? maximumUsd, DateTimeOffset at) => holds.Add(request, (keyId, maximumUsd, at));
    public bool HasUnsettled(string keyId) => holds.Values.Any(item => item.Key == keyId);
    public void Cancel(Guid request) => holds.Remove(request);
    public void Settle(Guid request, double actualUsd)
    {
        if (holds.Remove(request, out var hold)) Add(hold.Key, actualUsd, hold.At);
    }
}

public sealed class InMemoryKeyMovePolicy : IKeyMovePolicy
{
    private readonly Dictionary<Guid, KeyMoveSetting> threads = [];

    /// <summary>The all-threads value.</summary>
    public KeyMoveSetting Default { get; set; }

    public void Set(Guid threadId, KeyMoveSetting setting) => threads[threadId] = setting;

    public KeyMoveSetting Setting(Guid threadId) => threads.TryGetValue(threadId, out var v) ? v : Default;
}
