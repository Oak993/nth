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

public sealed class InMemoryApiKeySpend : IApiKeySpend
{
    private readonly List<(string Key, double Usd, DateTimeOffset At)> entries = [];

    public double SpendSince(string keyId, DateTimeOffset since) =>
        entries.Where(e => e.Key == keyId && e.At >= since).Sum(e => e.Usd);

    public void Add(string keyId, double usd, DateTimeOffset at) => entries.Add((keyId, usd, at));
}

public sealed class InMemoryKeyMovePolicy : IKeyMovePolicy
{
    private readonly Dictionary<Guid, KeyMoveSetting> threads = [];

    /// <summary>The all-threads value.</summary>
    public KeyMoveSetting Default { get; set; }

    public void Set(Guid threadId, KeyMoveSetting setting) => threads[threadId] = setting;

    public KeyMoveSetting Setting(Guid threadId) => threads.TryGetValue(threadId, out var v) ? v : Default;
}
