namespace Marrow.Core.Accounts.Keys;

// PORT-ONLY (OD-0114, MA-6): several API keys per service. Source for every rule here is
// porting/first-release/multi-account-NOTES.md (owner, 2026-10-05, "API providers"); no Swift twin exists.

/// <summary>NOTES: "order (in order / most credits first / spread evenly)".</summary>
public enum KeyOrderMode
{
    InOrder,
    MostCreditsFirst,
    SpreadEvenly,
}

/// <summary>
/// One stored key. <see cref="Allowed"/> is the Allowed switch (and the enabled/disabled rule: an off key stays stored and is never used).
/// <see cref="MonthlyCapUsd"/> is the optional cap per key (NOTES "optional cap per key", the owner's monthly cap for MA-6).
/// </summary>
public sealed record ApiKeyEntry(string Id, string Service, string Nickname, bool Allowed = true, double? MonthlyCapUsd = null);

/// <summary>The key a request goes out on. <see cref="Secret"/> is the bearer value; it never appears in an event.</summary>
public sealed record KeyChoice(string Id, string Nickname, string Secret);

/// <summary>What MA-1's accounts store gives the pool: a service's keys in the user's order, the order mode, and the stored secrets.</summary>
public interface IApiKeyStore
{
    IReadOnlyList<ApiKeyEntry> Keys(string service);

    KeyOrderMode Order(string service);

    /// <summary>The stored secret (APIKeychain-backed in production), or null when none is stored.</summary>
    string? Secret(string keyId);
}

/// <summary>Credits left in USD where the service reports them (OpenRouter does); null when it does not.</summary>
public interface IApiKeyCredits
{
    double? CreditsLeft(string keyId);
}

/// <summary>Spend tracking behind the monthly cap. The month is the user's LOCAL calendar month (lead decision, 2026-10-05); the pool passes its start.</summary>
public interface IApiKeySpend
{
    /// <summary>Money spent on the key at or after <paramref name="since"/>.</summary>
    double SpendSince(string keyId, DateTimeOffset since);

    void Add(string keyId, double usd, DateTimeOffset at);
}

public enum ApiKeyEventKind
{
    /// <summary>"Continued on &lt;name&gt;": a request went out again, unchanged, on the next allowed key.</summary>
    Continued,

    /// <summary>No allowed key can take the request now.</summary>
    AllKeysOut,

    /// <summary>A thread pinned to a key found it out; a pinned thread never falls to another key.</summary>
    PinnedKeyOut,

    /// <summary>A key reached its monthly cap.</summary>
    CapReached,

    /// <summary>A thread pinned to a key found it at its monthly cap: it stops before sending and offers to unpin or raise the cap.</summary>
    PinnedKeyCapped,

    /// <summary>OD-0116: moving to another key is not allowed yet; the first time it would happen the user is asked.</summary>
    MoveNeedsAnswer,

    /// <summary>The user chose that this thread never moves to another key by itself.</summary>
    MoveDeclined,
}

/// <summary>What the UI binds to for keys. The text is the unit's wording.</summary>
public sealed record ApiKeyEvent(
    ApiKeyEventKind Kind,
    string Service,
    Guid ThreadId,
    string? FromKeyId,
    string? FromName,
    string? ToKeyId,
    string? ToName,
    int? Status,
    DateTimeOffset At,
    DateTimeOffset? ReturnsAt,
    string Message);

/// <summary>One thread's view of a service's pool: what the session calls around each request.</summary>
public interface IApiKeyRotation
{
    /// <summary>Why <see cref="Current"/> or <see cref="OnFailure"/> last left the thread without a key to send on (the notice text), or null.</summary>
    string? StopReason { get; }

    /// <summary>The nickname of the key the last <see cref="Current"/> moved the thread onto because its key was out or capped (a "Continued on" the session still has to say), else null.</summary>
    string? ContinuedOn { get; }

    /// <summary>"Yes, just this time": the next forced move for this thread is allowed once. A remembered answer lives in <see cref="IKeyMovePolicy"/>.</summary>
    void AllowMoveOnce();

    /// <summary>The key the next request goes out on (the pin, else the order), or null when none can take it.</summary>
    KeyChoice? Current();

    /// <summary>
    /// The request on <see cref="Current"/> came back with <paramref name="status"/>. A 402 (out of credits) or 429 (rate limited) marks that key out and
    /// returns the next allowed key for the SAME request, but only when moving is allowed (OD-0116); anything else, a pinned thread, moving not allowed, or no key left returns null.
    /// </summary>
    KeyChoice? OnFailure(int status, TimeSpan? retryAfter);

    /// <summary>Money spent on the request that just finished, against <see cref="Current"/>'s monthly cap.</summary>
    void RecordSpend(double usd);
}

/// <summary>OD-0116: the user's answer to "may Marrow move this thread to another key by itself?". Unset is the default and means off, and ask the first time.</summary>
public enum KeyMoveSetting
{
    Unset,
    Allow,
    Deny,
}

/// <summary>Where the answer is kept: thread, then Settings (the join maps this onto MA-5's remember scopes).</summary>
public interface IKeyMovePolicy
{
    KeyMoveSetting Setting(Guid threadId);
}
