using Marrow.Setup.Text;

namespace Marrow.Setup.Install;

/// <summary>The steps the Installing and Update screens list (PLAN.md 4's table; the canvas's INSTALL and UPDATE lists).</summary>
public enum InstallStep
{
    /// <summary>"Checking your PC" (or "Preparing files", or the running-nth step on Update): free space, a running nth, staging.</summary>
    Checking,

    /// <summary>"Copying nth" / "Copying the new version": the bytes in <c>packages\</c> and <c>current\</c>.</summary>
    Copying,

    /// <summary>"Adding nth to Start and your desktop" / "Adding nth to Start": the shortcuts and the install hook (fresh install only).</summary>
    Adding,

    /// <summary>"Finishing up": the hook ends, Setup.exe exits 0, the uninstall entry is re-pointed and Start re-stamped.</summary>
    Finishing,
}

/// <summary>A step row's state: the approved getting-ready rows' ring, spinner and tick.</summary>
public enum StepState
{
    Waiting,
    Working,
    Done,
}

/// <summary>Where the whole run is.</summary>
public enum InstallPhase
{
    /// <summary>Checking: nothing of Velopack's has started; Close still works.</summary>
    Checking,

    /// <summary>Setup.exe is running or has exited and the setup is finishing; Close is paused.</summary>
    Running,

    /// <summary>Everything is done, the uninstall entry included: the bar may show 100.</summary>
    Succeeded,

    /// <summary>The install failed (Problem).</summary>
    Failed,
}

/// <summary>A step's place on the bar, in percent.</summary>
public sealed record StepRange(InstallStep Step, double From, double To);

/// <summary>One step row as drawn: its words, state and the byte count beside Copying.</summary>
/// <param name="Detail">"210 of 420 MB" beside Copying (its longest form is <see cref="DetailLongest"/>), else null.</param>
/// <param name="DetailLongest">The byte count's longest form ("420 of 420 MB"), whose width the count keeps.</param>
public sealed record StepView(InstallStep Step, string Label, StepState State, string? Detail, string? DetailLongest)
{
    /// <summary>The byte count shows once its step is working (the canvas's <c>.d</c>).</summary>
    public bool DetailShown => Detail is not null && State != StepState.Waiting;
}

/// <summary>What the Installing (or Update) screen draws now.</summary>
/// <param name="Steps">The rows, in order.</param>
/// <param name="Target">Where the bar is heading, 0 to 100; it never goes back, never passes the next step's start before that step is seen, and is 100 only once everything is done.</param>
/// <param name="Slow">Nothing has changed for 20 s: the line under the title says so.</param>
/// <param name="Phase">Where the run is.</param>
public sealed record ProgressFrame(IReadOnlyList<StepView> Steps, double Target, bool Slow, InstallPhase Phase)
{
    /// <summary>The line under the title: "This usually takes less than a minute." or the stall line.</summary>
    public string Subtitle => Slow ? SetupStrings.InstallingSlow : SetupStrings.InstallingSubtitle;

    /// <summary>The step that is working (the last one once all are done).</summary>
    public int WorkingIndex
    {
        get
        {
            for (var i = 0; i < Steps.Count; i++)
                if (Steps[i].State != StepState.Done) return i;
            return Steps.Count - 1;
        }
    }
}

/// <summary>What has been seen of a run so far. Every flag only ever turns on.</summary>
public sealed class ProgressEvidence
{
    /// <summary>The first step's own progress before Velopack starts, 0 to 1 (the checks and the staging).</summary>
    public double CheckingFraction { get; set; }

    /// <summary>The payload is still being staged: the first step reads "Preparing files".</summary>
    public bool Staging { get; set; }

    /// <summary>Waiting for something outside the setup (a busy nth, unit U4): the bar does not creep and the stall line does not show.</summary>
    public bool Waiting { get; set; }

    /// <summary>The first step's words when unit U4 replaces "Checking your PC" on Update (null keeps the default).</summary>
    public string? FirstStepLabel { get; set; }

    public bool SetupStarted { get; set; }

    public long CopiedBytes { get; set; }

    public bool CreatingShortcuts { get; set; }

    public bool HookStarted { get; set; }

    public bool ResultWritten { get; set; }

    public bool HookEnded { get; set; }

    public bool Completed { get; set; }

    public bool ExitedZero { get; set; }

    /// <summary>The setup's own work after exit 0 is done (the uninstall entry re-pointed, Start re-stamped, the result read).</summary>
    public bool Finished { get; set; }

    public bool Failed { get; set; }
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4, "Rules"; the canvas's progress runner; unit U3, ED-NEW-inst-u3-1). Turns what has been
/// seen into the bar's target and the step rows. A fresh install has four steps: Checking 0-4 %, Copying 4-86 % (bytes ÷
/// (T_pkg + T_app)), Adding 86-94 %, Finishing 94-100 %; an update three: the first step 0-6 %, Copying the new version
/// 6-90 %, Finishing 90-100 % (the canvas's UPDATE lists). A step is working once its own signal is seen, and every step
/// before it is then done, so rows go waiting, working, done in order and never back. The target is the working step's
/// start plus the share its signals show, never past the next step's start; it never goes back; it is 100 only once the
/// setup's own work after exit 0 is done. If nothing changes for 2 s, it creeps toward the next step's start at a slowing
/// rate (90 % of the gap at most, an e-fold every 10 s), never passing it; if nothing changes for 20 s the frame is slow
/// (the stall line), until something moves again. While the first step waits on something outside the setup it neither
/// creeps nor stalls.
/// </summary>
public sealed class InstallProgress
{
    public static readonly TimeSpan CreepAfter = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(20);

    /// <summary>The creep's time constant: the gap to the next step shrinks by a factor e every this many seconds.</summary>
    public const double CreepSeconds = 10;

    /// <summary>The creep covers at most this share of the gap to the next step's start.</summary>
    public const double CreepShare = 0.9;

    private readonly InstallSizes sizes;
    private readonly bool desktopShortcut;
    private double target;
    private double creepBase;
    private TimeSpan lastChange;
    private string? lastKey;
    private bool started;

    public InstallProgress(bool update, InstallSizes sizes, bool desktopShortcut)
    {
        Update = update;
        this.sizes = sizes ?? throw new ArgumentNullException(nameof(sizes));
        this.desktopShortcut = desktopShortcut;
        Ranges = RangesFor(update);
    }

    public bool Update { get; }

    public IReadOnlyList<StepRange> Ranges { get; }

    /// <summary>The steps of a fresh install or an update.</summary>
    public static IReadOnlyList<StepRange> RangesFor(bool update) => update
        ? [new(InstallStep.Checking, 0, 6), new(InstallStep.Copying, 6, 90), new(InstallStep.Finishing, 90, 100)]
        : [new(InstallStep.Checking, 0, 4), new(InstallStep.Copying, 4, 86), new(InstallStep.Adding, 86, 94), new(InstallStep.Finishing, 94, 100)];

    /// <summary>The frame for <paramref name="evidence"/> at <paramref name="now"/> (a monotonic clock).</summary>
    public ProgressFrame Advance(ProgressEvidence evidence, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var index = WorkingIndex(evidence);
        var fraction = Math.Clamp(Fraction(evidence, Ranges[index].Step), 0, 1);
        var range = Ranges[index];
        var measured = evidence.Finished ? 100 : range.From + (range.To - range.From) * fraction;
        if (!evidence.Finished) measured = Math.Min(measured, range.To);

        var key = $"{index}|{fraction:R}|{evidence.CopiedBytes}|{evidence.Staging}|{evidence.Waiting}";
        if (!started || key != lastKey)
        {
            started = true;
            lastKey = key;
            lastChange = now;
            creepBase = Math.Max(target, measured);
        }
        var idle = now - lastChange;
        var next = Math.Max(target, measured);
        if (!evidence.Finished && !evidence.Failed && !evidence.Waiting && idle > CreepAfter && creepBase < range.To)
        {
            var share = CreepShare * (1 - Math.Exp(-(idle - CreepAfter).TotalSeconds / CreepSeconds));
            next = Math.Max(next, creepBase + (range.To - creepBase) * share);
        }
        if (!evidence.Finished) next = Math.Min(next, range.To);
        target = Math.Max(target, Math.Min(next, 100));

        var phase = evidence.Failed ? InstallPhase.Failed : evidence.Finished ? InstallPhase.Succeeded : evidence.SetupStarted ? InstallPhase.Running : InstallPhase.Checking;
        var slow = phase is InstallPhase.Checking or InstallPhase.Running && !evidence.Waiting && idle >= SlowAfter;
        return new ProgressFrame(Rows(evidence, index), target, slow, phase);
    }

    /// <summary>The highest step whose own signal has been seen (all of them once finished).</summary>
    public int WorkingIndex(ProgressEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Finished || evidence.ExitedZero) return Ranges.Count - 1;
        var hooked = evidence.CreatingShortcuts || evidence.HookStarted || evidence.ResultWritten;
        if (Update)
        {
            if (hooked || evidence.HookEnded || evidence.Completed) return 2;
            return evidence.SetupStarted ? 1 : 0;
        }
        if (evidence.HookEnded || evidence.Completed) return 3;
        if (hooked) return 2;
        return evidence.SetupStarted ? 1 : 0;
    }

    /// <summary>The share of <paramref name="step"/> its own signals show.</summary>
    private double Fraction(ProgressEvidence evidence, InstallStep step) => step switch
    {
        InstallStep.Checking => evidence.CheckingFraction,
        InstallStep.Copying => sizes.CopyBytes <= 0 ? 0 : (double)evidence.CopiedBytes / sizes.CopyBytes,
        InstallStep.Adding => evidence.ResultWritten ? 0.75 : evidence.HookStarted ? 0.5 : evidence.CreatingShortcuts ? 0.25 : 0,
        InstallStep.Finishing when Update => evidence.Finished ? 1
            : evidence.ExitedZero ? 0.7
            : evidence.Completed ? 0.6
            : evidence.HookEnded ? 0.5
            : evidence.ResultWritten ? 0.35
            : evidence.HookStarted ? 0.2
            : 0,
        InstallStep.Finishing => evidence.Finished ? 1 : evidence.ExitedZero ? 0.5 : evidence.Completed ? 0.35 : 0,
        _ => 0,
    };

    private IReadOnlyList<StepView> Rows(ProgressEvidence evidence, int index)
    {
        var rows = new StepView[Ranges.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var step = Ranges[i].Step;
            var state = evidence.Finished || i < index ? StepState.Done : i == index ? StepState.Working : StepState.Waiting;
            string? detail = null, longest = null;
            if (step == InstallStep.Copying)
            {
                var total = sizes.DiskMegabytes;
                var share = state == StepState.Done ? 1 : sizes.CopyBytes <= 0 ? 0 : Math.Clamp((double)evidence.CopiedBytes / sizes.CopyBytes, 0, 1);
                detail = SetupStrings.ProgressBytes((long)Math.Round(total * share, MidpointRounding.AwayFromZero), total);
                longest = SetupStrings.ProgressBytes(total, total);
            }
            rows[i] = new StepView(step, Label(step, evidence), state, detail, longest);
        }
        return rows;
    }

    private string Label(InstallStep step, ProgressEvidence evidence) => step switch
    {
        InstallStep.Checking when evidence.Staging => SetupStrings.StepPreparing,
        InstallStep.Checking => evidence.FirstStepLabel ?? SetupStrings.StepChecking,
        InstallStep.Copying => Update ? SetupStrings.StepCopyingNew : SetupStrings.StepCopying,
        InstallStep.Adding => desktopShortcut ? SetupStrings.StepAddingBoth : SetupStrings.StepAddingStart,
        _ => SetupStrings.StepFinishing,
    };
}
