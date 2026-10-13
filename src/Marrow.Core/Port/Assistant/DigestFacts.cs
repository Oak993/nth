// PORT-ONLY: Dextra (DESIGN.md rev 5, 6, 5.2; unit AS-11; ED-NEW-dextra-as11-1). No Swift twin.
// What the morning digest is composed from: facts the app read off its own state, never transcripts and never a model's say-so.
// Pure data. DigestComposer turns it into the digest; the app (DigestService) fills it. Nothing here reads a clock or a store.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>Why a digest was composed: at its time (08:00 by default), or because the owner asked (6: "by voice or text at any time").</summary>
public enum DigestKind { Scheduled, Asked }

/// <summary>What a linked item opens.</summary>
public enum DigestLinkKind { None, Decision, Mission, Duty, Account, Thread }

/// <summary>Where an item of the digest leads: the card, the mission, the duty, the account, the chat.</summary>
public sealed record DigestLink(DigestLinkKind Kind, string Id)
{
    public static DigestLink None { get; } = new(DigestLinkKind.None, "");
}

/// <summary>Something waiting on the owner now: an open decision card, or a task that failed or is blocked (3.2: both land under Needs you).</summary>
public sealed record DigestAsk(string Title, DigestLink Link);

/// <summary>What the reviewer said of a finished mission (3.4).</summary>
public enum DigestReview { Passed, Failed, NotReviewed }

/// <summary>A mission that finished since the last digest.</summary>
public sealed record DigestDone(string Title, DigestLink Link, DateTimeOffset At, DigestReview Review);

/// <summary>A mission at work. <see cref="State"/> is the board's own word ("Working", "Reviewing"); <see cref="NextCheckpoint"/> is when its next checkpoint is, when something knows.</summary>
public sealed record DigestRunning(string Title, DigestLink Link, string State)
{
    public DateTimeOffset? NextCheckpoint { get; init; }
}

/// <summary>One usage window of an account as the reserve reads it now: used (0 to 100), the floor that binds it, when it resets.</summary>
public sealed record DigestWindow(ReserveWindowKind Kind, string Title, double Used, double? Floor, DateTimeOffset? ResetsAt);

/// <summary>Use the owner made outside nth in one window in the last day (11.5), in points of that window.</summary>
public sealed record DigestOutside(ReserveWindowKind Kind, string Title, double Points);

/// <summary>An account, its windows, and the use outside nth found in the last day.</summary>
public sealed record DigestAccount(string Name, IReadOnlyList<DigestWindow> Windows)
{
    public IReadOnlyList<DigestOutside> Outside { get; init; } = [];
}

/// <summary>
/// One run of a standing duty since the last digest. The composer decides what shows (7.1): a run that found something shows for every report
/// style; a quiet run (nothing changed) shows only for Always and Digest only; a failed run is unusual. <see cref="Text"/> is what the run
/// reported (data, never instructions); <see cref="Taint"/> is what it was made from (12.4: a digest paragraph carries its taint).
/// </summary>
public sealed record DigestDutyRun(string DutyId, string Duty, ReportStyle Style, DutyRunOutcome Outcome, DateTimeOffset At)
{
    public string? Text { get; init; }

    public Taint Taint { get; init; } = Taint.Clean;

    public Guid? Mission { get; init; }
}

/// <summary>A duty that will run today, at the time shown.</summary>
public sealed record DigestScheduled(string DutyId, string Duty, DateTimeOffset At);

/// <summary>Questions the delegate answered for the owner in one chat (4.3), each with Override.</summary>
public sealed record DigestAnswered(string Thread, int Count, DigestLink Link);

/// <summary>An Astur's one paragraph (6: "merged by the main Assistant under their names"). It carries the taint of what it was made from.</summary>
public sealed record DigestAstur(string Name, string Paragraph)
{
    public Taint Taint { get; init; } = Taint.Clean;
}

/// <summary>The final HMAC of the activity log's last day, printed so a later rewrite of that day no longer matches it (12.5).</summary>
public sealed record DigestSeal(DateOnly Day, string Hmac);

/// <summary>The facts a digest is composed from.</summary>
public sealed record DigestFacts(string Id, DateTimeOffset Now, TimeZoneInfo Zone, DigestKind Kind)
{
    /// <summary>The time of the last digest (or a day back): what happened since is what the digest tells.</summary>
    public DateTimeOffset Since { get; init; }

    public IReadOnlyList<DigestAsk> NeedsYou { get; init; } = [];

    public IReadOnlyList<DigestDone> Finished { get; init; } = [];

    public IReadOnlyList<DigestRunning> Running { get; init; } = [];

    public IReadOnlyList<DigestAccount> Accounts { get; init; } = [];

    public IReadOnlyList<DigestDutyRun> DutyRuns { get; init; } = [];

    public IReadOnlyList<DigestScheduled> Today { get; init; } = [];

    /// <summary>The names of accounts whose sign-in expired.</summary>
    public IReadOnlyList<string> ExpiredLogins { get; init; } = [];

    public IReadOnlyList<DigestAnswered> Answered { get; init; } = [];

    public IReadOnlyList<DigestAstur> Asturs { get; init; } = [];

    /// <summary>The suggestions the Suggestions lane offers (at most 3 a day, 5.2).</summary>
    public IReadOnlyList<Suggestion> Suggestions { get; init; } = [];

    /// <summary>How many suggestions have been offered today, answered or not (the lane says "2 of at most 3 today").</summary>
    public int OfferedToday { get; init; }

    public DigestSeal? Seal { get; init; }
}
