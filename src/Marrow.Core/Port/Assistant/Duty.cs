// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.1; unit AS-10; ED-NEW-dextra-as10-1, -4, -5). No Swift twin.
// A standing duty: a name, an owner assistant, a trigger, what to do, a per-run budget and reserve rule, the grants it narrows,
// a report style, active hours, a missed-run policy, on/off, and the last 50 runs. Pure data and the rules that need no clock
// of their own; AssistantScheduler (Marrow.Services) runs it, DutyJson keeps it.
using System.Text.RegularExpressions;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>How a duty reports (7.1). Silent unless something changed is the default.</summary>
public enum ReportStyle { SilentUnlessChanged, Always, DigestOnly, NotifyMe }

/// <summary>What happens to a run that came due while nth was closed, paused or held (7.1). Catch up once is the default.</summary>
public enum MissedRunPolicy { CatchUpOnce, Skip }

/// <summary>Why a run began.</summary>
public enum DutyCause { Time, Folder, Page, AppEvent, Manual, CatchUp, Wake }

/// <summary>How a run went. Started becomes one of the others when its mission ends.</summary>
public enum DutyRunOutcome { Started, NothingChanged, Done, Failed, Skipped, Stopped, Refused }

/// <summary>One line of a duty's run history: a link to its mission (7.1).</summary>
public sealed record DutyRun(DateTimeOffset At, DutyCause Cause, DutyRunOutcome Outcome)
{
    public Guid? MissionId { get; init; }

    public string? Note { get; init; }

    /// <summary>How many identical skipped times this line stands for (a run that is still going skips every time that comes).</summary>
    public int Count { get; init; } = 1;
}

public sealed record Duty(string Id, string Name, string AssistantId, DutyTrigger Trigger, string Instructions)
{
    /// <summary>The runs a duty keeps (7.1).</summary>
    public const int HistoryLimit = 50;

    public const int NameLimit = 120;

    public const int InstructionsLimit = 8_000;

    /// <summary>What the owner's duty should do, beside the instruction: a skill or a saved workflow by name (7.1).</summary>
    public string? Skill { get; init; }

    public string? Workflow { get; init; }

    /// <summary>The project its missions work in. Null: the only project, or the one the Assistant picks by name.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>The most one run may spend, in dollars; null: the plan's own limits.</summary>
    public decimal? BudgetUsd { get; init; }

    /// <summary>The reserve rule for each run, in the owner's words ("keep 30%"); read as a task rule when a run starts.</summary>
    public string? ReserveText { get; init; }

    /// <summary>What a run may do: it inherits its assistant's grants and can only narrow them (12.1). Null: all of them.</summary>
    public DutyScope? Scope { get; init; }

    public ReportStyle Report { get; init; } = ReportStyle.SilentUnlessChanged;

    public ActiveHours? Hours { get; init; }

    public MissedRunPolicy Missed { get; init; } = MissedRunPolicy.CatchUpOnce;

    public bool Enabled { get; init; } = true;

    /// <summary>The run needs the desktop (Tactus): on a locked PC it does what needs none and waits for the unlock (7.4).</summary>
    public bool NeedsDesktop { get; init; }

    /// <summary>Wake the PC for it with a Task Scheduler entry (7.4). Off by default; never for a duty that needs the desktop.</summary>
    public bool WakePc { get; init; }

    /// <summary>Who made it: "owner", or the assistant that proposed it and the owner approved.</summary>
    public string CreatedBy { get; init; } = "owner";

    /// <summary>When the current trigger began. An Every schedule counts from here, and a run owed before it is never caught up.</summary>
    public DateTimeOffset Since { get; init; }

    /// <summary>Until when the schedule was dealt with (a run, a skip or a catch-up). The next owed run is the first after it.</summary>
    public DateTimeOffset? Handled { get; init; }

    public DateTimeOffset? LastRun { get; init; }

    public ValueList<DutyRun> History { get; init; } = ValueList<DutyRun>.Empty;

    /// <summary>Fields a newer build wrote that this one does not know, as JSON text by name; they survive a save (15.3).</summary>
    public ValueMap<string, string> Unknown { get; init; } = ValueMap<string, string>.Empty;

    public static readonly Regex IdShape = new("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static string NewId() => "d" + Guid.NewGuid().ToString("N")[..12];

    public bool IsTimed => Trigger is TimeTrigger;

    /// <summary>Whether a Task Scheduler wake can be offered for it, and if not why (7.4: never for a duty that needs the desktop).</summary>
    public WakeOffer WakeOffer() => NeedsDesktop
        ? new WakeOffer(false, "This duty needs the desktop, and a locked PC cannot use it. It waits for you to unlock instead.")
        : Trigger is not TimeTrigger ? new WakeOffer(false, "Only a duty that runs at a time can wake the PC.") : new WakeOffer(true, null);

    /// <summary>Null when the duty can be saved; else the first thing wrong, in the owner's words.</summary>
    public string? Problem()
    {
        if (!IdShape.IsMatch(Id)) return "That id is not one nth can use.";
        if (string.IsNullOrWhiteSpace(Name)) return "Give the duty a name.";
        if (Name.Length > NameLimit) return $"A name holds at most {NameLimit} characters.";
        if (string.IsNullOrWhiteSpace(AssistantId)) return "Say which assistant owns it.";
        if (string.IsNullOrWhiteSpace(Instructions) && string.IsNullOrWhiteSpace(Skill) && string.IsNullOrWhiteSpace(Workflow))
            return "Say what the duty does.";
        if (Instructions.Length > InstructionsLimit) return $"The instructions hold at most {InstructionsLimit} characters.";
        if (BudgetUsd is < 0) return "A budget cannot be negative.";
        if (WakePc && !WakeOffer().Offered) return WakeOffer().Reason;
        return Trigger.Problem();
    }

    /// <summary>The duty with this run recorded (newest first), the history held to its limit, and it marked as the last that ran.</summary>
    public Duty WithRun(DutyRun run) => WithHistory(run) with { LastRun = run.At };

    /// <summary>A line in the history for something that is not a run (a skipped time, a refused start, a dry run): LastRun stays.</summary>
    public Duty WithHistory(DutyRun run) => this with
    {
        History = new ValueList<DutyRun>(History.Prepend(run).Take(HistoryLimit)),
    };

    /// <summary>The history with one run replaced (its mission ended).</summary>
    public Duty WithRunUpdated(Guid mission, Func<DutyRun, DutyRun> change) => this with
    {
        History = new ValueList<DutyRun>(History.Select(r => r.MissionId == mission ? change(r) : r)),
    };
}

/// <summary>Whether a duty may wake the PC, and if it may not, the reason the card shows greyed out (7.4).</summary>
public sealed record WakeOffer(bool Offered, string? Reason);
