// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6, 12.6b; unit AS-6; ED-NEW-dextra-as6-1). No Swift twin.
// The words of the stop and pause controls, in one place: the strip after Stop all, the line after Pause all and Resume all,
// the unavailable-key line, and the rule that flips the header, tray and voice toggle between Pause all and Resume all.
// Pure: no I/O, no clock. The sentences follow the approved page (index.html), so the app and the page read alike.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>What the global toggle (header, tray, voice) offers now (12.6b "One toggle in the header").</summary>
public enum GlobalToggleKind { PauseAll, ResumeAll }

/// <summary>The toggle and its two buttons: the Result list shows both, each disabled when it has nothing to do.</summary>
public sealed record GlobalToggle(GlobalToggleKind Kind, bool CanPauseAll, bool CanResumeAll);

/// <summary>Why a task is named in a Resume all line.</summary>
public enum ResumeAllLeft { StoppedByHand, HeadStoppedByHand, Reverted }

/// <summary>What Resume all did, for its line (12.6b "Global controls").</summary>
public sealed record ResumeAllResult(bool AfterStop)
{
    /// <summary>How many tasks picked up where they left off.</summary>
    public int PickedUp { get; init; }

    /// <summary>The kinds of irreversible card that sent a task back to Needs you ("send", "pay"), one entry per task.</summary>
    public ValueList<string> BackToNeedsYou { get; init; } = ValueList<string>.Empty;

    /// <summary>Tasks the owner stopped by hand: they stay stopped.</summary>
    public ValueList<string> StoppedByHand { get; init; } = ValueList<string>.Empty;

    /// <summary>Heads the owner stopped on their own, as "Head in “Task”".</summary>
    public ValueList<string> HeadsStoppedByHand { get; init; } = ValueList<string>.Empty;

    /// <summary>Reverted tasks: they wait for Restart.</summary>
    public int Reverted { get; init; }

    /// <summary>Tasks a reserve holds: their card resumes them.</summary>
    public ValueList<string> HeldByReserve { get; init; } = ValueList<string>.Empty;

    /// <summary>Tasks whose agents are still confirming a Stop all: they cannot resume until they have stopped.</summary>
    public ValueList<string> StillStopping { get; init; } = ValueList<string>.Empty;
}

public static class ControlWords
{
    // U+201C and U+201D, the quotes of the approved page; U+2026 HORIZONTAL ELLIPSIS.
    private const string Open = "\U0000201C";
    private const string Close = "\U0000201D";
    private const string Ellipsis = "\U00002026";

    public const string StopAllLabel = "Stop all";
    public const string PauseAllLabel = "Pause all";
    public const string ResumeAllLabel = "Resume all";
    public const string StillStopping = "Still stopping";
    public const string ForceEnd = "Force end";

    /// <summary>The line under Stop all when the stop key cannot be registered (12.6 "Fail closed for missions too").</summary>
    public static string StopKeyUnavailable(string chord) => $"Stop key unavailable: {chord} is taken. Change it in Settings";

    /// <summary>
    /// The line a task shows while it starts its agents again after they were let go (12.6b "Paused tasks"): the first turn reads
    /// the whole context again at the uncached price. <paramref name="tokens"/> is the context the lead held, when it is known.
    /// </summary>
    public static string ColdCache(long? tokens) => tokens is { } n and > 0
        ? $"Resuming with a cold cache: about {(n < 1000 ? n.ToString(CultureInfo.InvariantCulture) : ((n + 500) / 1000).ToString(CultureInfo.InvariantCulture) + "k")} tokens read again."
        : "Resuming with a cold cache: the context is read again at the uncached price.";

    /// <summary>The line a Resume that cannot take the stop key reports.</summary>
    public static string ResumeNeedsStopKey(string chord) => StopKeyUnavailable(chord) + ", then resume.";

    /// <summary>
    /// The strip after Stop all (12.6 "Afterwards"): the two times kept apart. <paramref name="agents"/> is how many agents
    /// were stopping, <paramref name="confirmed"/> how many have confirmed.
    /// </summary>
    public static string StopStrip(double sentMs, string source, int agents, int confirmed, TimeSpan? confirmedIn)
    {
        var sent = Math.Max(1, (int)Math.Round(sentMs, MidpointRounding.AwayFromZero));
        var middle = confirmed >= agents && confirmedIn is { } took
            ? $"{Count(agents)} of {Count(agents)} agents confirmed stopped in {took.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s; process trees closed."
            : $"Confirming agents: {Count(confirmed)} of {Count(agents)} stopped{Ellipsis}";
        return $"Stop sent in {Count(sent)} ms ({source}). {middle} Nothing was undone. Open decisions are held. "
            + "Each mission's changes wait in its own branch. Duties are held until Resume all.";
    }

    /// <summary>The line after Pause all (the page's feed line).</summary>
    public static string PauseAllLine(string source, int tasks) =>
        $"Pause all ({source}): {Plural(tasks, "task")} pause at their next turn boundary; nothing is cut; new duties start paused";

    /// <summary>The line after Resume all: what it picked up and what it left alone, named (12.6b).</summary>
    public static string ResumeAllLine(ResumeAllResult r)
    {
        var picked = r.PickedUp;
        var text = $"Resumed all{(r.AfterStop ? " after the stop" : "")}: {Plural(picked, "task")} picked up where {(picked == 1 ? "it" : "they")} left off.";
        if (r.BackToNeedsYou.Count > 0)
        {
            var kinds = string.Join(" or ", r.BackToNeedsYou.Distinct());
            text += $" {Count(r.BackToNeedsYou.Count)} went back to Needs you: a {kinds} card never resumes on its own.";
        }
        text += Left(r.StoppedByHand.Select(Quote).ToList(), "");
        text += Left(r.HeadsStoppedByHand, " on your own");
        if (r.HeldByReserve.Count > 0)
        {
            var held = r.HeldByReserve.Select(Quote).ToList();
            text += $" {AndList(held)} {(held.Count == 1 ? "is" : "are")} held by your reserve; {(held.Count == 1 ? "its" : "their")} card resumes {(held.Count == 1 ? "it" : "them")}.";
        }
        if (r.StillStopping.Count > 0)
        {
            var slow = r.StillStopping.Select(Quote).ToList();
            text += $" {AndList(slow)} {(slow.Count == 1 ? "is" : "are")} still stopping; Resume all again once {(slow.Count == 1 ? "it has" : "they have")} stopped.";
        }
        if (r.Reverted > 0) text += $" {Plural(r.Reverted, "reverted task")} {(r.Reverted == 1 ? "waits" : "wait")} for Restart.";
        return text;
    }

    private static string Left(IReadOnlyList<string> names, string where) =>
        names.Count == 0
            ? ""
            : $" {AndList(names)} {(names.Count == 1 ? "stays" : "stay")} stopped, because you stopped {(names.Count == 1 ? "it" : "them")}{where}.";

    /// <summary>Quotes a title the way the page does.</summary>
    public static string Quote(string title) => Open + title + Close;

    /// <summary>"1 task", "2 tasks".</summary>
    public static string Plural(int n, string noun) => $"{Count(n)} {noun}{(n == 1 ? "" : "s")}";

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>"A", "A and B", "A, B and C".</summary>
    public static string AndList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => items[0] + " and " + items[1],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    /// <summary>
    /// The global toggle (12.6b): Pause all while any task can still pause (Planning, Working, Reviewing, Needs you or
    /// Blocked); Resume all as soon as every live task is Pausing, Paused or stopped, or while only a latch or a Stop all
    /// hold remains. It never waits for turns to finish. The Result list's two buttons are each disabled when they have
    /// nothing to do.
    /// </summary>
    public static GlobalToggle Toggle(IEnumerable<Mission> missions, bool pauseLatched, bool stopHeld)
    {
        ArgumentNullException.ThrowIfNull(missions);
        var all = missions.ToList();
        var canPause = all.Any(m => MissionStates.CanPause(m.State));
        var canResume = all.Any(m => m.State is MissionState.Pausing or MissionState.Paused or MissionState.Stopping or MissionState.Stopped
            && !m.Reverted && m.PausedBy != PauseCause.Reserve) || pauseLatched || stopHeld;
        return new GlobalToggle(!canPause && canResume ? GlobalToggleKind.ResumeAll : GlobalToggleKind.PauseAll, canPause, canResume);
    }

    /// <summary>The word for the card kind an irreversible hold names ("send", "pay"): the page's wording for Resume all.</summary>
    public static string HeldCardWord(ActionClass? actionClass, DecisionKind kind) => kind == DecisionKind.Merge
        ? "merge"
        : actionClass switch
        {
            ActionClass.Money => "pay",
            ActionClass.Destructive => "delete",
            ActionClass.Install => "install",
            ActionClass.Commitment => "commit",
            _ => "send",
        };
}
