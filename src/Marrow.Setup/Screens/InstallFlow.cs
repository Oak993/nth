using Marrow.Setup.Flow;
using Marrow.Setup.Install;
using Marrow.Setup.Motion;

namespace Marrow.Setup.Screens;

/// <summary>Calls a poll every 100 ms off the window's thread until it returns false.</summary>
public interface IPollLoop
{
    void Start(Func<bool> poll);
}

/// <summary>The real loop: a pool thread on a 100 ms <see cref="PeriodicTimer"/>; the walk and the log never touch the window's thread.</summary>
public sealed class BackgroundPollLoop(TimeProvider? time = null) : IPollLoop
{
    public void Start(Func<bool> poll)
    {
        ArgumentNullException.ThrowIfNull(poll);
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(InstallFlow.Interval, time ?? TimeProvider.System);
            while (poll()) await timer.WaitForNextTickAsync().ConfigureAwait(false);
        });
    }
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, steps 6 to 9, and 4; unit U3, ED-NEW-inst-u3-1). Install or Update, from the click to
/// Ready or Problem. On the window's thread: the free-space check first (not enough: Problem before anything runs, nothing
/// changed); the progress screen's rows laid out; the staging and unit U4's running-nth check (<c>beforeEngine</c>) drive
/// the first step while Close still works; then Setup.exe is started and Close is paused in the same moment. Off the
/// window's thread, every 100 ms, the run looks at the root and the log; each frame is posted to the screen. When Setup.exe
/// has exited 0, the setup's own work runs on the window's thread (<see cref="InstallRun.Finish"/>), Close comes back, the
/// bar reaches 100, and 0.65 s later (Calm 0.25 s, the canvas's) Ready shows. Any failure resumes Close and shows Problem
/// with its reason. A window closed while still checking starts nothing.
/// </summary>
public sealed class InstallFlow
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>The canvas's pause between 100 % and Ready.</summary>
    public const double ReadyAfter = 0.65, ReadyAfterCalm = 0.25;

    private readonly SetupSession session;
    private readonly Func<InstallRun> createRun;
    private readonly EngineStaging staging;
    private readonly IPollLoop poll;
    private readonly Action<Action> post;
    private readonly Func<InstallRun, Task<bool>>? beforeEngine;
    private Task<bool>? checks;
    private IProgressView? view;
    private bool starting;
    private volatile bool closed;

    /// <param name="session">The window's session.</param>
    /// <param name="createRun">A new run for the session's route (a Retry makes another).</param>
    /// <param name="staging">The engine's staging, started when Welcome or Update showed.</param>
    /// <param name="poll">The 100 ms loop.</param>
    /// <param name="post">Runs an action on the window's thread.</param>
    /// <param name="beforeEngine">Unit U4's running-nth check (true: go on; false: the person stopped it), or null.</param>
    public InstallFlow(SetupSession session, Func<InstallRun> createRun, EngineStaging staging, IPollLoop poll, Action<Action> post, Func<InstallRun, Task<bool>>? beforeEngine = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.createRun = createRun ?? throw new ArgumentNullException(nameof(createRun));
        this.staging = staging ?? throw new ArgumentNullException(nameof(staging));
        this.poll = poll ?? throw new ArgumentNullException(nameof(poll));
        this.post = post ?? throw new ArgumentNullException(nameof(post));
        this.beforeEngine = beforeEngine;
        session.Window.Closed += (_, _) => closed = true;
    }

    /// <summary>The run in progress or the last one.</summary>
    public InstallRun? Run { get; private set; }

    /// <summary>A run is under way (from the click until Ready or Problem).</summary>
    public bool Busy { get; private set; }

    /// <summary>The screen the run draws on: Update for an update, else Installing.</summary>
    public static SetupScreen ScreenFor(bool update) => update ? SetupScreen.Update : SetupScreen.Installing;

    /// <summary>Install or Update was chosen (or Retry): a new run starts.</summary>
    public void Begin()
    {
        if (Busy || closed) return;
        var run = createRun();
        Run = run;
        Busy = true;
        starting = false;
        if (!run.CheckSpace().Enough)
        {
            Failed(run);
            return;
        }
        var screen = ScreenFor(run.Request.Update);
        if (session.Screen != screen) session.Go(screen);
        view = (session.Get(screen) as IProgressScreen)?.Progress;
        if (view is not null)
        {
            view.Attach(session.Window.Animator);
            view.Begin(run.Tick());
        }
        _ = staging.StageAsync();
        checks = beforeEngine?.Invoke(run) ?? Task.FromResult(true);
        poll.Start(() => Poll(run));
    }

    /// <summary>One look, off the window's thread. False once the run needs nothing more from the loop.</summary>
    private bool Poll(InstallRun run)
    {
        if (closed && !run.Evidence.SetupStarted) return false;
        if (!run.Evidence.SetupStarted)
        {
            if (staging.Error is not null)
            {
                run.FailBeforeStart(FailureReason.Other);
                post(() => Failed(run));
                return false;
            }
            if (checks is { IsCompleted: true } done && (!done.IsCompletedSuccessfully || !done.Result))
            {
                post(Stopped);
                return false;
            }
            run.SetChecking(0.2 + 0.8 * staging.Fraction, staging: !staging.IsComplete);
            if (staging.IsComplete && checks is { IsCompletedSuccessfully: true, Result: true } && !starting)
            {
                starting = true;
                post(() => StartEngine(run));
            }
        }
        var frame = run.Tick();
        post(() => view?.Render(frame));
        if (run.Failure is not null)
        {
            post(() => Failed(run));
            return false;
        }
        if (run.NeedsFinish)
        {
            post(() => Finish(run));
            return false;
        }
        return true;
    }

    /// <summary>Setup.exe starts and Close is paused in the same moment (PLAN.md 3, "Close").</summary>
    private void StartEngine(InstallRun run)
    {
        if (closed || run.Failure is not null) return;
        run.Start();
        if (run.Failure is not null) return; // the next poll shows Problem
        session.PauseClose();
    }

    private void Finish(InstallRun run)
    {
        var frame = run.Finish();
        if (run.Failure is not null)
        {
            Failed(run);
            return;
        }
        session.Result = run.Result;
        session.ResumeClose();
        if (view is null)
        {
            Done();
            return;
        }
        view.Render(frame);
        session.Window.Animator.Start(new ReadyWhenFull(view.Bar, session.Window.Settings.Current.Calm ? ReadyAfterCalm : ReadyAfter, Done));
    }

    private void Done()
    {
        Busy = false;
        if (!closed) session.Go(SetupScreen.Ready);
    }

    private void Failed(InstallRun run)
    {
        Busy = false;
        if (session.Window.Guard.IsPaused) session.ResumeClose();
        if (run.Failure is { } failure && !closed) session.Fail(failure);
    }

    /// <summary>Unit U4's check said no (the person stopped waiting): nothing started; back to where the run began.</summary>
    private void Stopped()
    {
        Busy = false;
        if (!closed) session.Go(session.Route.Screen);
    }

    /// <summary>Waits for the bar to be drawn full, then for the canvas's pause, then calls Ready (on the frame loop).</summary>
    private sealed class ReadyWhenFull(Ui.ProgressMotion bar, double pause, Action ready) : IMotion
    {
        private double left = pause;

        public bool Advance(double seconds)
        {
            if (bar.Value < 100) return true;
            left -= seconds;
            if (left > 0) return true;
            ready();
            return false;
        }
    }
}
