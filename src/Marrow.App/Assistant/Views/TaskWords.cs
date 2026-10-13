// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 12.6b "One model, one set of words", "Layout, controls and access", "Heads"; the canvas's
// detail(), CT, setK, askHtml and paintHeads, work/design/assistant/index.html lines 1867-2025; ED-NEW-dextra-as8-3). No Swift twin.
// Every word a task row, a head line, a mission card, an inline ask and the live region say, in one place, so the Board, the home's
// Result list and the sidebar row read the same words.
using System.Globalization;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant.Views;

/// <summary>The control a slot shows now (the canvas's data-k).</summary>
public enum SlotKind { None, Pause, Resume, Restart, Stop, Revert }

/// <summary>Which inline ask a task or head row shows.</summary>
public enum AskKind { None, Stop, Revert, StillPausing }

/// <summary>One button of an inline ask.</summary>
public enum AskAnswer { StopNow, PauseInstead, KeepRunning, KeepPausing, KeepIt, Revert, KeepChanges, StopInstead }

public static class TaskWords
{
    private const string Dot = " · ";

    // ---- slots ----

    public static SlotKind KindOf(SlotA slot) => slot switch
    {
        SlotA.Pause => SlotKind.Pause,
        SlotA.Resume => SlotKind.Resume,
        SlotA.Restart => SlotKind.Restart,
        _ => SlotKind.None,
    };

    public static SlotKind KindOf(SlotB slot) => slot switch
    {
        SlotB.Stop => SlotKind.Stop,
        SlotB.RevertChanges => SlotKind.Revert,
        _ => SlotKind.None,
    };

    /// <summary>The hover tip: the short word only (r4 B19).</summary>
    public static string Tip(SlotKind kind) => kind switch
    {
        SlotKind.Pause => "Pause",
        SlotKind.Resume => "Resume",
        SlotKind.Restart => "Restart",
        SlotKind.Stop => "Stop",
        SlotKind.Revert => "Revert changes",
        _ => "",
    };

    /// <summary>The accessible name, which names the task or the head (12.6b): "Pause Next video: Why lenses fog up".</summary>
    public static string Name(SlotKind kind, string subject) => kind switch
    {
        SlotKind.Pause => "Pause " + subject,
        SlotKind.Resume => "Resume " + subject,
        SlotKind.Restart => "Restart from the brief: " + subject,
        SlotKind.Stop => "Stop " + subject,
        SlotKind.Revert => "Revert the changes of " + subject,
        _ => "",
    };

    /// <summary>A head's subject in its controls' names: "head Writer in Espresso machines under $800".</summary>
    public static string HeadSubject(string head, string task) => $"head {head} in {task}";

    /// <summary>The row expander's name (r4 B8).</summary>
    public static string ShowHeads(string task) => "Show heads of " + task;

    /// <summary>The Board card's heads disclosure name (r4 B8).</summary>
    public static string CardHeads(int heads, string task) => $"{ControlWords.Plural(heads, "head")} in {task}: show each head's controls";

    // ---- the detail line ----

    /// <summary>
    /// The line under a task's title: what it does now, and what the owner's controls did to it (the canvas's detail()). Pausing and
    /// stopping heads are counted as what they are, never as paused or stopped before they get there (r4 G11).
    /// </summary>
    public static string Detail(MissionJob job, Mission mission, string? waiting = null, string? reason = null, bool agentsReleased = false, bool interrupted = false)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(mission);
        var live = mission.Heads.Where(h => h.State != HeadState.Done && !h.HeldByReserve).ToList();
        var n = live.Count;
        int C(HeadState s) => live.Count(h => h.State == s);
        string Tally(params HeadState[] states) => n == 0 ? "" : string.Concat(states.Where(s => C(s) > 0).Select(s => $"{Dot}{C(s)} of {n} {Word(s)}"));
        var wait = WaitLine(mission);
        var kept = job.AnswerKept ? Dot + "your answer runs on resume" : "";
        var released = agentsReleased ? Dot + "agents released" : "";
        if (!string.IsNullOrWhiteSpace(waiting) && mission.State is not (MissionState.Stopping or MissionState.Stopped or MissionState.Done)) return waiting.Trim() + Tally(HeadState.Paused, HeadState.Stopped);
        switch (mission.State)
        {
            case MissionState.Pausing:
            {
                var pausing = live.Where(h => h.State == HeadState.Pausing).Select(h => h.Name).ToList();
                var lead = pausing.Count > 0
                    ? $"{ControlWords.AndList(pausing)} {(pausing.Count == 1 ? "finishes its" : "finish their")} turn, then it pauses"
                    : "Finishing the current turn, then pausing";
                return lead + Tally(HeadState.Paused, HeadState.Stopped) + wait + kept;
            }
            case MissionState.Paused:
                if (mission.PausedBy == PauseCause.Reserve) return "Paused before a usage reserve" + Dot + "answer its card to go on" + kept;
                if (interrupted) return "Paused when nth closed" + Dot + "state kept" + Dot + "resume to go on" + kept;
                return "Paused by you at a turn boundary" + Tally(HeadState.Paused, HeadState.Stopped) + wait + kept + released + Dot + "resumes where it paused";
            case MissionState.Stopping:
            {
                var stopped = C(HeadState.Stopped);
                return n > 0 ? $"Stopping{Dot}{stopped} of {stopped + C(HeadState.Stopping)} confirmed" : "Stopping" + Dot + "confirming the agent";
            }
            case MissionState.Stopped:
                if (mission.Reverted) return "Stopped" + Dot + "changes reverted" + Dot + "Restart begins again from the brief";
                return $"{(n > 0 ? C(HeadState.Stopped) : 1)} of {Math.Max(n, 1)} stopped" + Tally(HeadState.Paused) + wait + Dot + "state kept" + Files(mission);
            case MissionState.Blocked:
                return "Blocked" + (wait.Length > 0 ? wait : Dot + (string.IsNullOrWhiteSpace(reason) ? "it cannot go on" : reason.Trim()));
            case MissionState.Failed:
                return "Failed after its retries" + Dot + (string.IsNullOrWhiteSpace(reason) ? "the reason and evidence are attached" : reason.Trim());
            case MissionState.Done:
                return job.ReviewedBy is { Length: > 0 } reviewer ? "Done" + Dot + "reviewed by " + reviewer : "Done";
            case MissionState.NeedsYou:
                return (job.Activity is { } a ? Capital(a) : "Waiting on your answer") + Tally(HeadState.Pausing, HeadState.Paused, HeadState.Stopping, HeadState.Stopped) + wait;
            default:
                return (job.Activity is { } act ? Capital(act) : Doing(mission.State)) + Tally(HeadState.Pausing, HeadState.Paused, HeadState.Stopping, HeadState.Stopped) + wait;
        }
    }

    private static string Doing(MissionState state) => state switch
    {
        MissionState.Planning => "Estimating and choosing accounts",
        MissionState.Reviewing => "The reviewer is checking the result",
        _ => "Working on it",
    };

    private static string Word(HeadState state) => state switch
    {
        HeadState.Pausing => "pausing",
        HeadState.Paused => "paused",
        HeadState.Stopping => "stopping",
        HeadState.Stopped => "stopped",
        _ => state.ToString().ToLowerInvariant(),
    };

    private static string Files(Mission mission) => mission.ChangedFiles <= 0 ? "" : Dot + ControlWords.Plural(mission.ChangedFiles, "changed file") + " kept";

    /// <summary>" · Thumbnail waits on Subtitles": the queued heads that wait on a head that is pausing, paused or stopped.</summary>
    public static string WaitLine(Mission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var byId = mission.Heads.ToDictionary(h => h.Id, StringComparer.Ordinal);
        MissionHead? HeldBy(MissionHead h) => h.State != HeadState.Queued ? null
            : h.DependsOn.Select(d => byId.GetValueOrDefault(d)).FirstOrDefault(d => d is { State: HeadState.Pausing or HeadState.Paused or HeadState.Stopping or HeadState.Stopped });
        var waiting = mission.Heads.Where(h => HeldBy(h) is not null).ToList();
        if (waiting.Count == 0) return "";
        return $"{Dot}{ControlWords.AndList([.. waiting.Select(h => h.Name)])} {(waiting.Count == 1 ? "waits" : "wait")} on {HeldBy(waiting[0])!.Name}";
    }

    /// <summary>A head's note (the canvas's paintHeads).</summary>
    public static string HeadNote(HeadState state, bool heldByReserve, string? waitsOn, string? activity, bool pausingLong)
    {
        if (heldByReserve && state == HeadState.Paused) return "held by your reserve; answer its card";
        if (state == HeadState.Pausing) return pausingLong && activity is not null ? ControlWords.HeadStillFinishing(activity) : "finishing its turn";
        if (state == HeadState.Stopped) return "transcript kept";
        return waitsOn is { Length: > 0 } ? "waits on " + waitsOn : "";
    }

    /// <summary>
    /// What a Stop now cuts, from the work in flight (12.6b "Stop asks only when stopping would waste something"): a turn that says
    /// what it makes is cut and made again on resume. Empty when nothing is in flight.
    /// </summary>
    public static string Risk(string? activity) =>
        string.IsNullOrWhiteSpace(activity) ? "" : $"{Capital(activity.Trim())} is cut and made again on resume.";

    /// <summary>The share done for the progress track: finished heads of all heads, else where the state sits in the run.</summary>
    public static double Progress(Mission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (mission.State == MissionState.Done) return 1;
        if (mission.Heads.Count > 0)
        {
            var done = mission.Heads.Count(h => h.State == HeadState.Done);
            return Math.Clamp((done + .5) / (mission.Heads.Count + 1), .04, .96);
        }
        var at = mission.State is MissionState.Pausing or MissionState.Paused or MissionState.Stopping or MissionState.Stopped ? mission.ResumeTo ?? MissionState.Working : mission.State;
        return at switch
        {
            MissionState.Planning => .08,
            MissionState.Reviewing => .85,
            MissionState.Failed => .9,
            _ => .45,
        };
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];

    private static string Lower(string text) => text.Length == 0 ? text : char.ToLower(text[0], CultureInfo.CurrentCulture) + text[1..];

    // ---- the inline ask (the canvas's askKind and askHtml) ----

    /// <summary>Whether a Stop on this task asks first: only when it would cut a turn that makes something.</summary>
    public static bool StopAsks(MissionJob job) => job.State is not (MissionState.Done or MissionState.Stopping or MissionState.Stopped) && job.Risk.Length > 0;

    /// <summary>Whether a head's Stop asks first: when it would cut such a turn, or when other heads wait on it (r4 B3).</summary>
    public static bool StopAsks(HeadJob head) => head.State is not (HeadState.Done or HeadState.Stopping or HeadState.Stopped)
        && (head.Risk.Length > 0 || head.Waiters.Count > 0);

    /// <summary>An ask the owner opened, checked against the state now: one that no longer applies is never shown (r4 B1).</summary>
    public static AskKind Shown(AskKind asked, MissionJob job)
    {
        if (asked == AskKind.Revert) return job.State == MissionState.Stopped && job.ChangedFiles > 0 && !job.Reverted ? AskKind.Revert : AskKind.None;
        if (asked == AskKind.Stop) return job.State is MissionState.Done or MissionState.Stopping or MissionState.Stopped ? AskKind.None : AskKind.Stop;
        return job.State == MissionState.Pausing && job.StillPausing is not null ? AskKind.StillPausing : AskKind.None;
    }

    public static AskKind Shown(AskKind asked, HeadJob head) =>
        asked == AskKind.Stop && head.State is not (HeadState.Done or HeadState.Stopping or HeadState.Stopped) ? AskKind.Stop : AskKind.None;

    /// <summary>The ask's bold lead, its sentence and its buttons, first button first; the group's accessible name.</summary>
    public sealed record AskText(string Group, string Lead, string Body, IReadOnlyList<(AskAnswer Answer, string Label, bool Primary)> Buttons);

    public static AskText Ask(AskKind kind, MissionJob job, HeadJob? head = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        switch (kind)
        {
            case AskKind.Revert:
                return new AskText("Confirm revert", $"Revert {ControlWords.Plural(job.ChangedFiles, "changed file")}?",
                    $"They are on the task's own branch {job.Branch ?? "of its workspace"}, never in your files. The Activity log can bring them back for 7 days.",
                    [(AskAnswer.Revert, "Revert changes", true), (AskAnswer.KeepChanges, "Keep changes", false)]);
            case AskKind.StillPausing:
                return new AskText("Still pausing", "Still pausing.",
                    (job.StillPausing ?? ControlWords.StillPausing(job.PausingWaits, job.Activity)).Replace("Still pausing. ", "", StringComparison.Ordinal),
                    [(AskAnswer.StopInstead, "Stop instead", false)]);
            default:
            {
                var pausing = head is null ? job.State == MissionState.Pausing : head.State == HeadState.Pausing;
                var runs = head is null ? job.Runs || job.State is MissionState.NeedsYou or MissionState.Blocked : head.State == HeadState.Working;
                var risk = head is null ? job.Risk : head.Risk;
                var waiters = head?.Waiters ?? ValueList<string>.Empty;
                var waits = waiters.Count > 0 ? $" {ControlWords.AndList(waiters)} {(waiters.Count == 1 ? "waits" : "wait")} on it." : "";
                var tail = pausing ? " It is already pausing and finishes the turn first. Stop now cuts it."
                    : runs ? $" Pause finishes {(head is null ? "this" : "its")} turn first and loses nothing." : "";
                IReadOnlyList<(AskAnswer, string, bool)> buttons = pausing
                    ? [(AskAnswer.StopNow, "Stop now", true), (AskAnswer.KeepPausing, "Keep pausing", false)]
                    : runs
                        ? [(AskAnswer.StopNow, "Stop now", true), (AskAnswer.PauseInstead, "Pause instead", false), (AskAnswer.KeepRunning, "Keep running", false)]
                        : [(AskAnswer.StopNow, "Stop now", true), (AskAnswer.KeepIt, "Keep it", false)];
                return new AskText(head is null ? "Confirm stop" : "Confirm stop of " + head.Name, head is null ? "Stop now?" : $"Stop {head.Name} now?",
                    (risk.Length > 0 ? risk : "") + waits + tail, buttons);
            }
        }
    }

    // ---- the heads panel's footer, the live region ----

    public static string Footer(MissionJob job) => job.ChangedFiles <= 0
        ? "No file changes."
        : job.Reverted
            ? "Changes reverted. Restart begins again from the brief on a fresh branch."
            : $"{ControlWords.Plural(job.ChangedFiles, "changed file")} on {job.Branch ?? "the task's branch"}. Your own files are never touched.";

    /// <summary>What the polite live region says when a task's state changes; null for a change it does not announce.</summary>
    public static string? Announce(MissionState state, string title) => state switch
    {
        MissionState.Pausing => "Pausing: " + title,
        MissionState.Paused => "Paused: " + title,
        MissionState.Stopping => "Stopping: " + title,
        MissionState.Stopped => $"Stopped: {title}. State kept.",
        MissionState.Done => "Done: " + title,
        MissionState.NeedsYou => "Needs you: " + title,
        MissionState.Failed => "Failed: " + title,
        MissionState.Blocked => "Blocked: " + title,
        MissionState.Working => "Working: " + title,
        _ => null,
    };

    /// <summary>The card's meta line pieces: elapsed (tabular), agents, spend per account.</summary>
    public static string Elapsed(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes:00}:{span.Seconds:00}");
    }

    public static string AgentsWord(MissionJob job) => job.State == MissionState.Done ? "finished" : job.Agents == 0 ? "no agent running" : ControlWords.Plural(job.Agents, "agent");

    public static string SpendWord(IReadOnlyList<MissionSpend> spend) =>
        string.Join(Dot, spend.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Account} {s.Percent:0.0}%")));

    /// <summary>The group headings of the Missions tab (3.2), with what an empty one says.</summary>
    public static string GroupTitle(BoardGroup group) => group switch
    {
        BoardGroup.NeedsYou => "Needs you",
        BoardGroup.Working => "Working",
        _ => "Done today",
    };

    public static string GroupEmpty(BoardGroup group) => group == BoardGroup.NeedsYou ? "Nothing waiting on you." : "Nothing here yet.";

    internal static string LowerFirst(string text) => Lower(text);

    /// <summary>A moment in the wall clock's own time zone (the PC's; a test's clock may fix it), so every "today" agrees.</summary>
    public static DateTimeOffset Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Marrow.Core.Port.SwiftClock.Provider.Current.LocalTimeZone);
}
