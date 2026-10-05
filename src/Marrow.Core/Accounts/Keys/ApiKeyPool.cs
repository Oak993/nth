namespace Marrow.Core.Accounts.Keys;

/// <summary>How long a key stays out when the service gives no time. ENGINEERING DECISION (MA-6 ED): rate limit 60 s, out of credits re-checked after 30 min.</summary>
public sealed record ApiKeyPoolOptions(TimeSpan RateLimitCooldown, TimeSpan CreditsRecheck)
{
    public ApiKeyPoolOptions() : this(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(30)) { }
}

public sealed record PoolState(bool AllOut, DateTimeOffset? ReturnsAt, int Available);

/// <summary>One service's keys (NOTES "API providers"): order modes, Allowed switch, monthly cap, silent continuation on 402 and 429, per-thread pin.</summary>
public sealed class ApiKeyPool(
    string service, IApiKeyStore store, IApiKeyCredits credits, IApiKeySpend spend, TimeProvider clock, ApiKeyPoolOptions? options = null)
{
    public event EventHandler<ApiKeyEvent>? Changed;

    public IApiKeyRotation ForThread(Guid threadId) => throw new NotImplementedException();

    public void Pin(Guid threadId, string? keyId) => throw new NotImplementedException();

    public PoolState State() => throw new NotImplementedException();

    internal void Unused() => Changed?.Invoke(this, null!);
}
