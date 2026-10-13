using Avalonia.Threading;
using Marrow.Setup.Flow;
using Marrow.Setup.Install;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// The in-window install on a headless window (nth installer PLAN.md 4; unit U3): a session on the route's screen, an
/// <see cref="InstallFlow"/> whose run is an <see cref="InstallScenario"/>'s (a fake Setup.exe acting out a timeline in a
/// temp folder), a payload in memory, a poll loop turned by hand and posts that run at once on the window's thread. Built on the UI thread.
/// </summary>
public sealed class InstallFlowRig : IDisposable
{
    private readonly VelopackTimeline timeline;

    public InstallFlowRig(VelopackTimeline timeline, bool update = false, long? free = 100_000_000_000, bool payload = true, Func<InstallRun, Task<bool>>? beforeEngine = null, SystemLook? look = null, Func<SetupSession, IRunGate>? gate = null)
    {
        this.timeline = timeline;
        Window = new WindowRig(look);
        Scenario = new InstallScenario(timeline, update, free);
        Session = Window.Session(update ? SetupScreen.Update : SetupScreen.Welcome, installed: update ? InstallScenario.OldVersion : null);
        Staging = new EngineStaging(Scenario.Staging, payload ? () => new MemoryStream(new byte[64 * 1024]) : () => null, Scenario.Scope.Ensure);
        Gate = gate?.Invoke(Session);
        Flow = new InstallFlow(Session, NextRun, Staging, Poll, Post, beforeEngine, Gate);
        Session.InstallRequested = Flow.Begin;
        Session.Start();
        // ED-NEW-inst-merge-3: the window's signature check has answered (Install waits for it, ED-NEW-inst-cx-1)
        Session.SignatureChecked(new SignatureResult(SignatureState.Unsigned, null, 0));
        Window.Window.Show();
    }

    public WindowRig Window { get; }

    public InstallScenario Scenario { get; }

    public SetupSession Session { get; }

    public EngineStaging Staging { get; }

    public ManualPollLoop Poll { get; } = new();

    public InstallFlow Flow { get; }

    /// <summary>The check before the progress (unit U4's window side), when the test gave one.</summary>
    public IRunGate? Gate { get; }

    /// <summary>Every run the flow asked for, the first being the scenario's.</summary>
    public List<InstallRun> Runs { get; } = [];

    /// <summary>The timeline a retry's run acts out (the first run's when unset).</summary>
    public VelopackTimeline? RetryTimeline { get; set; }

    /// <summary>The fake Setup.exe of the latest run.</summary>
    public FakeVelopackRunner Runner { get; private set; } = null!;

    public bool Paused => Window.Window.Guard.IsPaused;

    /// <summary>The canvas's rows and bar the flow draws on (Installing's, or Update's on the update route).</summary>
    public CanvasProgress Panel => (CanvasProgress)((IProgressScreen)Session.Get(InstallFlow.ScreenFor(Scenario.Request.Update))).Progress;

    /// <summary>Waits for the payload to be written (staging runs on its own task).</summary>
    public void Staged() => Staging.StageAsync().Wait(TimeSpan.FromSeconds(20));

    /// <summary>100 ms: the fake Setup.exe moves on, the window draws six frames, then the loop turns once.</summary>
    public void Step()
    {
        Runner.Advance(0.1);
        Scenario.Time.Advance(TimeSpan.FromMilliseconds(100));
        Window.Run(0.1);
        Poll.Turn();
    }

    /// <summary>Steps until <paramref name="done"/> (or <paramref name="seconds"/> pass), calling <paramref name="each"/> after every step.</summary>
    public void Until(Func<bool> done, double seconds = 120, Action? each = null)
    {
        for (var t = 0.0; t < seconds && !done(); t += 0.1)
        {
            Step();
            each?.Invoke();
        }
    }

    public void Dispose()
    {
        if (Window.Window.IsVisible) Window.Window.CloseNow();
        Scenario.Dispose();
    }

    /// <summary>
    /// On the window's thread at once (everything the rig itself does); from elsewhere (a gate's answer, which completes on
    /// the thread pool) through the dispatcher, as the window does, so a test pumps it (<c>Dispatcher.UIThread.RunJobs</c>).
    /// </summary>
    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private InstallRun NextRun()
    {
        if (Runs.Count == 0)
        {
            Runner = Scenario.Runner;
            Runs.Add(Scenario.Run);
            return Scenario.Run;
        }
        Runner = new FakeVelopackRunner(RetryTimeline ?? timeline, Scenario.Hive, Path.Combine(Scenario.Base, "Start Menu", "Programs"));
        var run = new InstallRun(Scenario.Request, Runner, Scenario.Disk, Scenario.After, Scenario.Time, releaseStaging: Scenario.Scope.Release);
        Runs.Add(run);
        return run;
    }
}
