using Avalonia.Controls;
using Marrow.App.Views.Composer;

namespace Marrow.App.Port.FirstRun;

// GR-1 (BUILD-PLAN 2.2; OD-0096 and the OD-0098 amendments: the getting-ready pick "Look I, Words W1, Download D3"; ED-252):
// the first-run preparation service. On the first run after install, and again only when the prepared state is invalid (a new
// build, a new graphics adapter or driver: GpuWarmup.IsWarm, ED-252's marker), the app prepares everything that would otherwise
// cost frames, where a stall cannot be seen: W1's steps, in order, each named as W1 names it, with REAL progress only. This file
// is the service: the plan of steps, the run that reports each as its work completes, the prepared-state check, the entry for
// startup. GpuWarmup holds the work (its registry: programs drawn on the render thread and steps of other work, each naming its
// step); the screen that shows it is GR-2's, behind IFirstRunScreen. ENGINEERING DECISIONS (ED-NEW-f-gr-1, porting/engineering_
// decisions.md): how an unregistered step shows (it does not), what a step's fraction is, what the run does when the warm-up
// fails or has nothing to compile, and that nothing reads a clock to move progress.

/// <summary>One step of the first run as W1 names it: its id, its name on the screen and its place in the order.</summary>
public sealed record FirstRunStep(string Id, string Name, int Order);

/// <summary>W1's steps (decisions-2026-10-03/getting-ready/src/gr_lib.py:28-29, the ids as gr_lib.py:20-23 gives them). Voice
/// model and Voice speed test, which W1 also words, are not here: by the owner's pick D3 with consent the model downloads and is
/// timed behind Welcome, not on this screen (OD-0098).</summary>
public static class FirstRunSteps
{
    public static FirstRunStep Graphics { get; } = new("gpu", "Graphics card", 10);
    public static FirstRunStep Knob { get; } = new("knob", "Effort slider knob", 20);
    public static FirstRunStep Lights { get; } = new("lights", "Composer light", 30);
    public static FirstRunStep Liquid { get; } = new("liquid", "Slider liquid", 40);
    public static FirstRunStep Strike { get; } = new("strike", "Fast mode lightning", 50);
    public static FirstRunStep Voice { get; } = new("hw", "Voice hardware", 60);

    /// <summary>All six, in W1's order.</summary>
    public static IReadOnlyList<FirstRunStep> W1 { get; } = [Graphics, Knob, Lights, Liquid, Strike, Voice];
}

/// <summary>A step of a run's plan: the step and how many parts of work it has (the graphics card's own read, and each program
/// or work step registered under it).</summary>
public sealed record FirstRunStepInfo(FirstRunStep Step, int Parts);

public enum FirstRunStepState { Waiting, Running, Done }

/// <summary>
/// One event of a run, reported only when work has begun or completed: <paramref name="PartsDone"/> of <paramref name="Parts"/>
/// of the step are done. <paramref name="Elapsed"/> is how long the run has taken by the service's clock, for the record; no
/// event waits for it and none is caused by it.
/// </summary>
public sealed record FirstRunProgress(FirstRunStep Step, int Index, int Count, FirstRunStepState State, int PartsDone, int Parts, TimeSpan Elapsed)
{
    /// <summary>The share of the step's parts done: real, never a guess.</summary>
    public double Fraction => Parts == 0 ? (State == FirstRunStepState.Done ? 1 : 0) : (double)PartsDone / Parts;
}

/// <summary>How a run ended. NotShown: no screen was given (startup is unchanged). Skipped: the machine is prepared. Prepared:
/// the work completed. Failed and Cancelled: it did not (the next launch tries again: the marker was not written).</summary>
public enum FirstRunOutcomeKind { NotShown, Skipped, Prepared, Failed, Cancelled }

public sealed record FirstRunOutcome(FirstRunOutcomeKind Kind, string? Error = null);

/// <summary>What GR-2's Getting Ready sheet implements. <see cref="Begin"/> gives the plan (the sheet lists the steps),
/// <see cref="Report"/> each event, <see cref="EndAsync"/> the outcome once the work is over (the sheet says Ready and lifts away,
/// or just lifts away when it did not finish: it never holds the app longer than the work); the run waits for it.</summary>
public interface IFirstRunScreen
{
    void Begin(IReadOnlyList<FirstRunStepInfo> steps);
    void Report(FirstRunProgress progress);
    Task EndAsync(FirstRunOutcome outcome);
}

/// <summary>The first-run preparation (see the file note).</summary>
public sealed class FirstRunPreparation
{
    /// <summary>What the service reaches the machine through, so a test scripts it and nothing touches a GPU, a cache folder or a
    /// real warm-up unless the test is the one measuring that.</summary>
    internal sealed record Seams(Func<bool> IsPrepared, Func<IReadOnlyList<GpuProgram>> Registered, Func<CancellationToken, Task> PrepareGraphics,
        Func<TopLevel, IProgress<GpuWarmupStep>, CancellationToken, Task> Warm);

    private static Seams Real { get; } = new(() => GpuWarmup.IsWarm, () => GpuWarmup.Registered, GpuWarmup.PrepareGraphicsAsync,
        (window, progress, cancellation) => GpuWarmup.WarmAsync(window, progress, cancellation));

    private readonly Seams seams;
    private readonly TimeProvider clock;

    public FirstRunPreparation() : this(Real, TimeProvider.System) { }

    internal FirstRunPreparation(Seams seams, TimeProvider clock)
    {
        this.seams = seams;
        this.clock = clock;
    }

    /// <summary>The preparation startup uses.</summary>
    public static FirstRunPreparation Default { get; } = new();

    /// <summary>
    /// True when the programs of this build are kept for the graphics adapter and driver the app draws with now (OD-0096,
    /// ED-252: GpuWarmup.IsWarm, its marker naming the build, the adapter and the driver, and an entry for each program that
    /// draws). A new build, a driver update or another adapter makes it false, and the run happens again. Everything that joins
    /// the registry must have joined before this is asked (at startup, before the window is made).
    /// </summary>
    public bool IsPrepared => seams.IsPrepared();

    /// <summary>The steps a run would show, in W1's order, each with its parts: the graphics card's own read first, then every
    /// step that has a program or a step registered under it. A W1 step with nothing registered (Voice hardware, until the voice
    /// preparation registers it) is not in the plan: it has no row and no tick, because no work is done for it.</summary>
    public IReadOnlyList<FirstRunStepInfo> Plan() => PlanOf(seams.Registered());

    private static List<FirstRunStepInfo> PlanOf(IReadOnlyList<GpuProgram> programs)
    {
        var parts = new Dictionary<string, (FirstRunStep Step, int Parts)> { [FirstRunSteps.Graphics.Id] = (FirstRunSteps.Graphics, 1) };
        foreach (var program in programs)
        {
            var step = program.Step.Id == FirstRunSteps.Graphics.Id ? FirstRunSteps.Graphics : program.Step; // the graphics card is W1's, whatever a program says
            parts[step.Id] = (step, parts.GetValueOrDefault(step.Id).Parts + 1);
        }
        return parts.Values.OrderBy(p => p.Step.Order).ThenBy(p => p.Step.Id, StringComparer.Ordinal).Select(p => new FirstRunStepInfo(p.Step, p.Parts)).ToList();
    }

    /// <summary>
    /// Runs the preparation behind <paramref name="screen"/>. No screen: nothing runs (<see cref="FirstRunOutcomeKind.NotShown"/>:
    /// startup is as it was, the idle fallback of ED-252 still prepares the knob). Prepared: nothing runs and the screen is
    /// never told (<see cref="FirstRunOutcomeKind.Skipped"/>). Otherwise the screen gets the plan, then each event as the work it
    /// reports completes, then the outcome (and its EndAsync is awaited); the steps run in the plan's order, the graphics card
    /// first, then GpuWarmup's registry in one run (one program a frame on the render thread; a registered work step on the UI
    /// thread, in its place). Call it on the UI thread with the window the app shows.
    /// </summary>
    public async Task<FirstRunOutcome> RunAsync(TopLevel window, IFirstRunScreen? screen, CancellationToken cancellation = default)
    {
        if (screen is null) return new FirstRunOutcome(FirstRunOutcomeKind.NotShown);
        if (seams.IsPrepared()) return new FirstRunOutcome(FirstRunOutcomeKind.Skipped);
        var programs = seams.Registered();
        var plan = PlanOf(programs);
        var run = new Run(plan, programs, screen, clock);
        FirstRunOutcome outcome;
        screen.Begin(plan);
        try
        {
            run.Begun();
            await seams.PrepareGraphics(cancellation);
            run.Completed(FirstRunSteps.Graphics.Id);
            await seams.Warm(window, new Direct<GpuWarmupStep>(run.Completed), cancellation);
            run.Finished();
            outcome = new FirstRunOutcome(FirstRunOutcomeKind.Prepared);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = new FirstRunOutcome(FirstRunOutcomeKind.Cancelled);
        }
        catch (Exception error)
        {
            outcome = new FirstRunOutcome(FirstRunOutcomeKind.Failed, error.Message);
        }
        await screen.EndAsync(outcome);
        return outcome;
    }

    // the events of one run: a step is Running from when the step before it completes (the first from the start), and its parts are
    // counted as the work reports them; nothing here reads the clock but to stamp the event
    private sealed class Run
    {
        private readonly List<FirstRunStepInfo> plan;
        private readonly IFirstRunScreen screen;
        private readonly TimeProvider clock;
        private readonly long start;
        private readonly int[] done;
        private readonly FirstRunStepState[] state;
        private readonly Dictionary<string, int> stepOf = [];    // a step id: its place in the plan
        private readonly Dictionary<string, int> programOf = []; // a program's name: its step's place in the plan

        public Run(List<FirstRunStepInfo> plan, IReadOnlyList<GpuProgram> programs, IFirstRunScreen screen, TimeProvider clock)
        {
            this.plan = plan;
            this.screen = screen;
            this.clock = clock;
            start = clock.GetTimestamp();
            done = new int[plan.Count];
            state = new FirstRunStepState[plan.Count];
            for (var i = 0; i < plan.Count; i++) stepOf[plan[i].Step.Id] = i;
            foreach (var program in programs)
                programOf[program.Name] = stepOf[program.Step.Id];
        }

        public void Begun()
        {
            if (plan.Count == 0) return;
            state[0] = FirstRunStepState.Running;
            Emit(0);
        }

        // a part of the step is done: the graphics card's read
        public void Completed(string stepId) => Part(stepOf[stepId]);

        // a program or work step reported done by the warm-up; one the plan does not name (registered after the plan was made) is
        // not this run's to count
        public void Completed(GpuWarmupStep report)
        {
            if (programOf.TryGetValue(report.Program, out var i)) Part(i);
        }

        // the warm-up completed: a step it had no work for (the CPU renderer has nothing to compile ahead) is done
        public void Finished()
        {
            for (var i = 0; i < plan.Count; i++)
                if (state[i] != FirstRunStepState.Done) Complete(i);
        }

        private void Part(int i)
        {
            if (state[i] == FirstRunStepState.Done) return;
            done[i]++;
            if (done[i] >= plan[i].Parts) { Complete(i); return; }
            state[i] = FirstRunStepState.Running;
            Emit(i);
        }

        private void Complete(int i)
        {
            done[i] = plan[i].Parts;
            state[i] = FirstRunStepState.Done;
            Emit(i);
            if (i + 1 < plan.Count && state[i + 1] == FirstRunStepState.Waiting)
            {
                state[i + 1] = FirstRunStepState.Running;
                Emit(i + 1);
            }
        }

        private void Emit(int i) =>
            screen.Report(new FirstRunProgress(plan[i].Step, i, plan.Count, state[i], done[i], plan[i].Parts, clock.GetElapsedTime(start)));
    }

    // reports on the thread that reports (the warm-up's loop, on the UI thread), not posted
    private sealed class Direct<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

/// <summary>
/// The startup entry: runs <see cref="FirstRunPreparation.Default"/> behind <see cref="Screen"/>. GR-2's Getting Ready sheet sets
/// the screen and calls this from the startup hook; until then nothing sets it and nothing calls it, so startup is unchanged
/// (tracked in porting/STATE.md). One run at a time: asking while one is going gives that run.
/// </summary>
public static class FirstRunStartup
{
    private static readonly object gate = new();
    private static Task<FirstRunOutcome>? running;

    /// <summary>The screen a run is shown behind (null until GR-2 supplies it).</summary>
    public static IFirstRunScreen? Screen { get; set; }

    public static Task<FirstRunOutcome> RunAsync(TopLevel window, CancellationToken cancellation = default) =>
        RunAsync(window, FirstRunPreparation.Default, Screen, cancellation);

    internal static Task<FirstRunOutcome> RunAsync(TopLevel window, FirstRunPreparation preparation, IFirstRunScreen? screen, CancellationToken cancellation = default)
    {
        TaskCompletionSource<FirstRunOutcome> source;
        lock (gate)
        {
            if (running is { IsCompleted: false } current) return current;
            source = new TaskCompletionSource<FirstRunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            running = source.Task;
        }
        _ = Forward(preparation.RunAsync(window, screen, cancellation), source); // outside the lock: the screen's callbacks are not run under it
        return source.Task;
    }

    private static async Task Forward(Task<FirstRunOutcome> run, TaskCompletionSource<FirstRunOutcome> source)
    {
        try { source.SetResult(await run); }
        catch (Exception error) { source.SetException(error); }
    }
}
