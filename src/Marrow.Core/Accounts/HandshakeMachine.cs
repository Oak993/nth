namespace Marrow.Core.Accounts;

public enum HandshakePhase
{
    Idle,
    Standby,
    Linking,
    InSync,
    Switch,

    /// <summary>PORT-ONLY phases: waiting for a reset, and waiting for the user's card answer.</summary>
    Waiting,
    Asking,
}

public enum HandshakeReason
{
    NearLimit,
    HardLimit,
    EarlierAccountBack,
    ResetReached,
}

public enum HandshakeEventKind
{
    PhaseChanged,
    DecisionRequested,
    InSyncReady,
    Degraded,
}

/// <summary>What the first-time or Ask-me card shows: one card for every unresolved kind, one Remember line.</summary>
public sealed record DecisionRequest(
    Guid ThreadId,
    IReadOnlyList<DecisionKind> Kinds,
    bool FirstTime,
    string OutgoingAccountId,
    string? IncomingAccountId,
    DateTimeOffset? ResetsAt);

/// <summary>What the UI binds to: phase changes, which accounts, why.</summary>
public sealed record HandshakeEvent(
    Guid ThreadId,
    HandshakeEventKind Kind,
    HandshakePhase From,
    HandshakePhase To,
    string? OutgoingAccountId,
    string? IncomingAccountId,
    HandshakeReason Reason,
    DateTimeOffset At,
    DateTimeOffset? ResumesAt = null,
    DecisionRequest? Request = null,
    string? Detail = null);

public sealed class HandshakeMachine(
    Guid threadId,
    IAccountStore store,
    IAccountLimits limits,
    IHandoffSummary summaries,
    DecisionPolicy decisions,
    IThreadAccountHost host,
    TimeProvider clock,
    HandshakeOptions options) : IAsyncDisposable
{
    public HandshakePhase Phase => throw new NotImplementedException();

    public event EventHandler<HandshakeEvent>? Changed;

    /// <summary>The poller's beat: starts the Handshake at the start point, switches in sync at a safe point, ends a wait.</summary>
    public Task TickAsync(CancellationToken ct) => throw new NotImplementedException();

    /// <summary>The limit hit, with or without warning.</summary>
    public Task LimitHitAsync(string accountId, DateTimeOffset? resetsAt, CancellationToken ct) => throw new NotImplementedException();

    /// <summary>The user's answer to a <see cref="HandshakeEventKind.DecisionRequested"/> card.</summary>
    public Task AnswerAsync(IReadOnlyList<Choice> choices, RememberScope scope, CancellationToken ct) => throw new NotImplementedException();

    public ValueTask DisposeAsync() => throw new NotImplementedException();

    internal void Unused() => Changed?.Invoke(this, null!);
}
