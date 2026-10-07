namespace Marrow.Core.Accounts;

/// <summary>MA-1's accounts store, as the Handshake engine needs it. <see cref="Accounts"/> is the global priority order (row 1 first), enabled or not.</summary>
public interface IAccountStore
{
    IReadOnlyList<AccountInfo> Accounts { get; }
}

/// <summary>MA-2's per-account limits, polling and usage-rate estimator.</summary>
public interface IAccountLimits
{
    /// <summary>The latest reading of the account's binding window, or null when unknown.</summary>
    AccountUsage? Usage(string accountId);

    /// <summary>Real account health when no quota window exists; null means unknown.</summary>
    bool? IsAvailable(string accountId) => null;

    /// <summary>The measured usage percent this thread spends per minute on the account, or null when there is no data yet.</summary>
    double? SpendPercentPerMinute(string accountId, Guid threadId);

    /// <summary>The observed conservative Handshake cost in percent of the account's window (may include concurrent activity), or null when unmeasured.</summary>
    double? HandshakeCostPercent(string accountId);
}

/// <summary>What the incoming account gets to read (NOTES "Bring has three options").</summary>
public enum Bring
{
    Full,
    MarrowSummary,
    AiSummary,
}

/// <summary>Marrow's own summary of a thread. <see cref="KeyFacts"/> are what a read-back must show (files, open tasks, the user's latest instructions).</summary>
public sealed record HandoffSummary(string Text, string? FilePath, IReadOnlyList<string> KeyFacts);

/// <summary>What the incoming account is given.</summary>
public sealed record HandoffPackage(Bring Bring, HandoffSummary Summary, string? Briefing);

/// <summary>MA-4's Marrow-written handoff summary.</summary>
public interface IHandoffSummary
{
    Task<HandoffSummary> BuildAsync(Guid threadId, CancellationToken ct);

    /// <summary>Handshake step 4: the key facts the read-back does not show. Empty when the incoming account is in sync.</summary>
    IReadOnlyList<string> FindGaps(HandoffSummary summary, string readBack);
}

/// <summary>The Settings and thread stores for decision choices (NOTES "Remember scope"). Values are <see cref="DecisionValues"/> strings.</summary>
public interface IDecisionStore
{
    string? GetSetting(DecisionKind kind);

    void SetSetting(DecisionKind kind, string? value);

    string? GetThread(Guid threadId, DecisionKind kind);

    void SetThread(Guid threadId, DecisionKind kind, string? value);

    /// <summary>"Use Settings": clears every choice this thread holds.</summary>
    void ClearThread(Guid threadId);
}

/// <summary>
/// PORT-ONLY: OD-0116, OD-0122, ED-439. How a prepared destination holds the context before the switch.
/// Primed: a text-only model turn that reads it back (Claude, API, Local). Injected: the history is loaded without any model turn (Codex, Pi, OpenCode when the CLI proves it).
/// Deferred: nothing is sent early; the context rides in front of the first real turn (a warm-started or never-started destination).
/// </summary>
public enum StandbyKind
{
    Primed,
    Injected,
    Deferred,
}

/// <summary>The incoming account's session, started in the background (hot standby).</summary>
public interface IStandbySession : IAsyncDisposable
{
    /// <summary>How this standby holds the context. It can drop to <see cref="StandbyKind.Deferred"/> when a loaded history is discarded for safety.</summary>
    StandbyKind Kind => StandbyKind.Primed;

    /// <summary>Gives the incoming account the package. With <paramref name="readBack"/> it answers with a few lines of what it understood; otherwise null.</summary>
    Task<string?> ReceiveAsync(HandoffPackage package, bool readBack, CancellationToken ct);

    /// <summary>Tells the incoming account the facts its read-back missed.</summary>
    Task FillGapsAsync(IReadOnlyList<string> gaps, CancellationToken ct);
}

/// <summary>What the Handshake engine needs from one thread's runtime and its sessions.</summary>
public interface IThreadAccountHost
{
    string ActiveAccountId { get; }

    Pill? Pill { get; }

    /// <summary>The pill step the thread is on, or null.</summary>
    int? StepIndex { get; }

    /// <summary>The thread was pinned to its account by hand; Marrow never offers to go back to an earlier one.</summary>
    bool Pinned { get; }

    /// <summary>Between steps (after a tool finishes), never mid-edit. A turn that died on a limit is at a safe point.</summary>
    bool AtSafePoint { get; }

    /// <summary>A reply was being written when the limit hit.</summary>
    bool ReplyInFlight { get; }

    Task<IStandbySession> StartStandbyAsync(SwitchTarget target, CancellationToken ct);

    /// <summary>Handshake step 2: the outgoing account, while it still has usage, writes a briefing for its successor.</summary>
    Task<string?> RequestBriefingAsync(HandoffSummary summary, CancellationToken ct);

    /// <summary>The switch itself. <paramref name="resumeReply"/> continues the reply from the last finished step on the new account.</summary>
    Task SwitchAsync(IStandbySession standby, SwitchTarget target, bool resumeReply, CancellationToken ct);
}
