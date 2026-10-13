using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Install;
using Marrow.Setup.Support;
using Marrow.Setup.Text;

namespace Marrow.Setup.Screens;

/// <summary>What the gate says once the progress may show: the first step's words (null keeps "Checking your PC"), whether it waits on nth, and the task that lets Setup.exe start.</summary>
/// <param name="FirstStep">"Saving and closing nth", "Stopping 2 threads and closing nth", or null.</param>
/// <param name="Waiting">The first step waits on nth: the bar neither creeps nor stalls (PLAN.md 4).</param>
/// <param name="Engine">True: Setup.exe may start. False: it may not (the run failed, or the screen shows nth's answer instead).</param>
public sealed record RunGo(string? FirstStep, bool Waiting, Task<bool> Engine);

/// <summary>
/// What <see cref="InstallFlow"/> asks before the progress shows (unit U4's window side, <see cref="NthCheck"/>). Called on the
/// window's thread.
/// </summary>
public interface IRunGate
{
    /// <summary>
    /// The run's space is enough. The answer: a <see cref="RunGo"/> (the progress shows), or null when nothing runs; the run's
    /// <see cref="InstallRun.Failure"/> is set when that is a problem. The answer may come later (the Update screen's "waits for nth").
    /// </summary>
    Task<RunGo?> OpenAsync(InstallRun run);

    /// <summary>The run is over (Ready, Problem, or stopped) and the flow is free again: the hold is let go, and what the check left to show is shown.</summary>
    void Ended(InstallRun run);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 step 7 and 3a; units U4 and U2 bound together, ED-NEW-inst-fix-1). The window's side of
/// "a running nth is asked, never surprised". On Install, Update and Retry (one handshake per attempt, PLAN.md 3a step 4): the
/// install hold on the root is taken first (<see cref="InstallHold"/>: another setup on the root is Problem, and an nth opened
/// during the install waits instead of being ended by Velopack), then nth is asked to quit when idle.
/// <list type="bullet">
/// <item>None runs, or it agrees and is gone: the engine starts. While it saves and closes, the first step reads "Saving and closing nth".</item>
/// <item>Busy (Update): "nth is still working" with its threads; "Update when they finish" waits ("Update waits for nth", the list's
/// first row) and the update starts by itself; "Stop them and update" stops them ("Stopping 2 threads and closing nth").</item>
/// <item>Agreed but still there after 15 s, or never answered (Update): "nth isn't responding. Close it anyway?" with Update (yes:
/// ended, then the update) and Not now (no: nothing is forced; whatever nth was told is called off).</item>
/// <item>Refused, or any of these on a fresh install (whose screen has no question): Problem, nothing changed; Retry asks again.</item>
/// </list>
/// On Update the screen keeps Ask while nth answers (usually a moment); if no answer has come in 0.4 s the progress shows and
/// waits. Stop waiting, Not now and Close call off what nth was told. Everything here runs on the window's thread; the
/// handshake's reports are posted to it.
/// </summary>
public sealed class NthCheck : IRunGate
{
    /// <summary>How long Update keeps Ask while nth answers before the progress shows and waits.</summary>
    public static readonly TimeSpan AskPatience = TimeSpan.FromMilliseconds(400);

    private readonly SetupSession session;
    private readonly Func<string, IRunningNth> create;
    private readonly Func<string, IDisposable?> takeHold;
    private readonly Action<Action> post;
    private readonly TimeProvider time;
    private IRunningNth? nth;
    private IDisposable? hold;
    private Attempt? current;
    private Choice next = Choice.Ask;
    private int threads;
    private bool closed;

    /// <param name="session">The window's session.</param>
    /// <param name="create">A new handshake for an install root (production: <c>ProductionRunningNth.Create</c>).</param>
    /// <param name="takeHold">Takes the install hold on a root, or null when another setup holds it.</param>
    /// <param name="post">Runs an action on the window's thread.</param>
    /// <param name="time">The clock of <see cref="AskPatience"/>.</param>
    public NthCheck(SetupSession session, Func<string, IRunningNth> create, Func<string, IDisposable?> takeHold, Action<Action> post, TimeProvider? time = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        this.takeHold = takeHold ?? throw new ArgumentNullException(nameof(takeHold));
        this.post = post ?? throw new ArgumentNullException(nameof(post));
        this.time = time ?? TimeProvider.System;
        session.Window.Closed += (_, _) => closed = true;
    }

    private enum Choice
    {
        Ask,
        Wait,
        Stop,
        End,
    }

    /// <summary>The Update screen whose buttons answer nth's questions (wired by <see cref="AttachUpdate"/>).</summary>
    public UpdateScreen? Update { get; private set; }

    /// <summary>
    /// Wires the Update screen: its Busy, Waiting and not-responding buttons, and the footnote that says nth is open (asked once,
    /// off the window's thread, changing nothing).
    /// </summary>
    public void AttachUpdate(UpdateScreen update, string root)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Update = update;
        update.WaitRequested += () => Choose(Choice.Wait);
        update.StopRequested += () => Choose(Choice.Stop);
        update.CloseAnywayRequested += () => Choose(Choice.End);
        update.StopWaitingRequested += CallOff;
        update.NotNowRequested += CallOff;
        _ = Task.Run(async () =>
        {
            try
            {
                var open = await create(root).IsNthOpenAsync().ConfigureAwait(false);
                post(() => { if (!closed) update.SetNthOpen(open); });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or TimeoutException)
            {
                // unknown: the footnote keeps its plain words, and pressing Update still asks nth
            }
        });
    }

    public Task<RunGo?> OpenAsync(InstallRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var choice = next;
        next = Choice.Ask;
        Release();
        var root = run.Request.Root;
        if (choice == Choice.Ask || nth is null)
        {
            choice = Choice.Ask;
            nth = create(root);
        }
        hold = takeHold(root);
        if (hold is null)
        {
            run.FailBeforeStart(FailureReason.Other, ["Another nth Setup is installing to this folder."]);
            return Task.FromResult<RunGo?>(null);
        }
        var update = run.Request.Update ? Update : null;
        var attempt = new Attempt(run, nth, choice, update);
        current = attempt;
        attempt.Listen = status => post(() => OnStatus(attempt, status));
        nth.StatusChanged += attempt.Listen;
        if (choice == Choice.Wait) update?.ShowWaiting(threads);
        Task<RunningNthStatus> operation;
        try
        {
            operation = choice switch
            {
                Choice.Wait => nth.WaitForWorkToFinishAsync(),
                Choice.Stop => nth.StopWorkAndQuitAsync(),
                Choice.End => nth.EndItAsync(),
                _ => nth.AskAsync(),
            };
        }
        catch (InvalidOperationException error)
        {
            operation = Task.FromException<RunningNthStatus>(error);
        }
        _ = operation.ContinueWith(done => post(() => OnSettled(attempt, done)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (!attempt.Opened.Task.IsCompleted)
        {
            // Update keeps Ask for a moment while nth answers; "waits for nth" keeps its own screen; the rest show the progress at once
            if (choice == Choice.Wait) { }
            else if (choice == Choice.Ask && update is not null)
                attempt.Patience = time.CreateTimer(_ => post(() => { if (ReferenceEquals(current, attempt) && !attempt.Opened.Task.IsCompleted) Open(attempt, null); }), null, AskPatience, Timeout.InfiniteTimeSpan);
            else Open(attempt, FirstStep(choice, open: false));
        }
        return attempt.Opened.Task;
    }

    public void Ended(InstallRun run)
    {
        var attempt = current;
        if (attempt is null || !ReferenceEquals(attempt.Run, run)) return;
        Release();
        current = null;
        if (closed || attempt.Show is not { } show) return;
        if (attempt.Update is { } update && session.Screen != SetupScreen.Update)
        {
            // a Retry from Problem: the Update screen comes back already showing nth's answer
            session.Go(SetupScreen.Update);
            update.Reveal.Now(show);
            return;
        }
        show();
    }

    /// <summary>
    /// The window closed (Program, once its loop has ended): whatever nth was told is called off and the hold let go. The task
    /// ends when the handshake has withdrawn its request (best effort; the handshake bounds it).
    /// </summary>
    public Task CloseAsync()
    {
        closed = true;
        var asked = nth;
        Release();
        current = null;
        return asked?.CancelAsync() ?? Task.CompletedTask;
    }

    /// <summary>A button of the Busy or not-responding question: the next run does that (the session's own Install gate decides whether it may run).</summary>
    private void Choose(Choice choice)
    {
        if (closed || current is not null) return;
        next = choice;
        session.ChooseInstall();
        if (current is null) next = Choice.Ask; // the session refused (paused, signature): nothing was started
    }

    /// <summary>Stop waiting, or Not now on the not-responding question: nothing is forced, whatever nth was told is called off.</summary>
    private void CallOff()
    {
        var asked = nth;
        next = Choice.Ask;
        if (current is { } attempt) attempt.Show = null; // the screen goes back to Ask by itself
        _ = asked?.CancelAsync(); // settles the attempt as Cancelled, so the flow is free again (no hold left)
        if (current is null) Release();
    }

    private void OnStatus(Attempt attempt, RunningNthStatus status)
    {
        if (!ReferenceEquals(current, attempt)) return;
        switch (status.State)
        {
            case RunningNthState.Closing or RunningNthState.Ending:
                attempt.SawOpen = true;
                if (!attempt.Opened.Task.IsCompleted) Open(attempt, FirstStep(attempt.Choice, open: true));
                else attempt.Run.SetWaiting(true, FirstStep(attempt.Choice, open: true));
                break;
            case RunningNthState.Waiting when attempt.Choice == Choice.Wait:
                threads = status.WorkCount;
                attempt.Update?.SetWaitingThreads(status.WorkCount);
                break;
        }
    }

    private void OnSettled(Attempt attempt, Task<RunningNthStatus> done)
    {
        if (!ReferenceEquals(current, attempt)) return;
        attempt.Patience?.Dispose();
        var status = done.IsCompletedSuccessfully
            ? done.Result
            : new RunningNthStatus(RunningNthState.Refused, [], 0, Reason: done.Exception?.InnerException?.Message ?? "the handshake was cancelled");
        switch (status.State)
        {
            case RunningNthState.Clear or RunningNthState.Closed:
                if (!attempt.Opened.Task.IsCompleted) Open(attempt, FirstStep(attempt.Choice, open: attempt.SawOpen || status.State == RunningNthState.Closed));
                attempt.Engine.TrySetResult(true);
                return;
            case RunningNthState.Cancelled:
                break;
            case RunningNthState.Busy when attempt.Update is { } update:
                threads = status.WorkCount;
                var running = status.Work.Select(item => (item.Name, Minutes(item.Seconds))).ToArray();
                attempt.Show = () => update.ShowBusy(running);
                break;
            case RunningNthState.NotResponding when attempt.Update is { } update:
                attempt.Show = update.ShowNotResponding;
                break;
            case RunningNthState.Busy or RunningNthState.NotResponding:
                // a fresh install's screen asks nothing: nth stays as it is, and Problem says what to do
                _ = attempt.Nth.CancelAsync();
                attempt.Run.FailBeforeStart(FailureReason.InUse, [Detail(status)]);
                break;
            default:
                attempt.Run.FailBeforeStart(FailureReason.InUse, [Detail(status)]);
                break;
        }
        if (!attempt.Opened.Task.IsCompleted) attempt.Opened.TrySetResult(null);
        attempt.Engine.TrySetResult(false);
    }

    private void Open(Attempt attempt, string? firstStep) =>
        attempt.Opened.TrySetResult(new RunGo(firstStep, Waiting: true, attempt.Engine.Task));

    private string? FirstStep(Choice choice, bool open) => choice switch
    {
        Choice.Stop => SetupStrings.StepStopping(Math.Max(1, threads)),
        Choice.Wait or Choice.End => SetupStrings.StepClosing,
        _ => open ? SetupStrings.StepClosing : null,
    };

    private static int Minutes(int seconds) => Math.Max(1, (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero));

    private static string Detail(RunningNthStatus status) =>
        "nth: " + status.State + (string.IsNullOrWhiteSpace(status.Reason) ? "" : " (" + status.Reason + ")");

    private void Release()
    {
        if (current is { } attempt)
        {
            attempt.Patience?.Dispose();
            if (attempt.Listen is { } listen) attempt.Nth.StatusChanged -= listen;
        }
        hold?.Dispose();
        hold = null;
    }

    private sealed class Attempt(InstallRun run, IRunningNth nth, Choice choice, UpdateScreen? update)
    {
        public InstallRun Run { get; } = run;

        public IRunningNth Nth { get; } = nth;

        public Choice Choice { get; } = choice;

        public UpdateScreen? Update { get; } = update;

        public TaskCompletionSource<RunGo?> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Engine { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Action<RunningNthStatus>? Listen { get; set; }

        public ITimer? Patience { get; set; }

        public bool SawOpen { get; set; }

        /// <summary>What the screen shows once the flow is free again (nth's question), or null.</summary>
        public Action? Show { get; set; }
    }
}
