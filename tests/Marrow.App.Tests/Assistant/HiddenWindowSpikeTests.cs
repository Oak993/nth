using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10, THE SPIKE (DESIGN.md rev 5, 7.4 "Hidden window", 16 row AS-10; ED-NEW-dextra-as10-13): close the main window, force a full
// collection, and prove that nothing the app needs lived only in the window: the model still has its thread, its runtime and its timers, a
// session still takes a turn and ends it, the scheduler's timer still fires and a duty completes, and showing the window again makes a new
// one whose view is built from the model with the same project and thread on screen. Run both ways ParkMain can keep the window out of the
// way: closed for real (the default) and hidden (the fallback). WHAT THIS CANNOT PROVE is that the old views' memory comes back: Avalonia's
// headless platform keeps a closed window alive whatever the app does, so the old window and its tree were reachable in every run here
// (the spike followed the references: the touch selection handles of a text box listen on the window and pin the tree through it, and a
// hidden window never redraws, so the compositor's queue of changed views stays too). That is a Native measurement, HiddenWindowNativeTests.
[Collection("Headless")]
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-13")]
public sealed class HiddenWindowSpikeTests(HeadlessSession headless)
{
    private const string First = "Replaced the parser. All 14 tests pass.";
    private const string Second = "The docs are updated.";

    private static string Show(LaunchHarness launch)
    {
        // A window made again needs the overlay layer a thread view's popover opens in, as the first did.
        if (launch.Manager.MainWindow is null) SettingsProviderPopupLayers.Install(launch.Harness.Main.OnMain(launch.Manager.PrepareMain));
        launch.Harness.Main.OnMain(launch.Manager.ShowMain);
        Pump(launch);
        var window = launch.Manager.MainWindow!;
        return RootViewRig.AllText(window.GetVisualDescendants().OfType<RootView>().Single());
    }

    // A posted job (a text change in the composer) sets the model, which only the main actor may do.
    private static void Pump(LaunchHarness launch) => launch.Harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs());

    private async Task Run(Marrow.App.Window.WindowManager.ParkKind kind)
    {
        // The login environment is read once for the whole process; it is read here, on the test's own thread, so that nothing blocks on it later.
        await Marrow.Core.Support.LoginEnvironment.Load().WaitAsync(AppModelHarness.Bound);
        Guid thread = default;
        WindowedThreadRig.WithLaunch(headless, h => thread = LaunchSurfaceRig.ProjectWithUnusedThread(h), launch =>
        {
            using var cursor = Marrow.App.Port.WindowCentering.Cursor.Override(new FixedCursorPlatform(null));
            using var dragCursor = Marrow.UI.Chrome.DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            var frameSource = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(frameSource);
            launch.Manager.MainContent = () => new RootView();
            launch.Manager.Park = kind;
            // The thread view opens a popover when it is built, which a window needs an overlay layer for.
            SettingsProviderPopupLayers.Install(launch.Harness.Main.OnMain(launch.Manager.PrepareMain));
            var rig = new WindowedThreadRig(launch, thread);
            var app = rig.App;
            var clock = launch.Harness.Main.Time;
            launch.Harness.Main.OnMain(() => app.SelectedThreadID = thread);
            rig.Turn("Fix it.", First);

            // The scheduler: a duty every 10 minutes, on the clock the app's own timers run on.
            var store = new MemoryDutyStore();
            store.Put("dextra", SchedulerRig.Every("d1", 10) with { Since = clock.GetUtcNow() });
            var launcher = new FakeDutyLauncher();
            using var scheduler = new AssistantScheduler(new AssistantSchedulerOptions { Store = store, Launcher = launcher, AssistantIds = () => ["dextra"], Time = clock });
            scheduler.Start();

            try
            {
                var shown = Show(launch);
                Assert.Contains("Project", shown, StringComparison.Ordinal); // the sidebar: the project and the thread this model holds
                Assert.Contains("Fix it.", shown, StringComparison.Ordinal);
                var timersBefore = clock.ActiveTimers;
                var runtimeBefore = rig.Runtime;
                var window = launch.Manager.MainWindow!;

                // The close.
                Assert.True(launch.Harness.Main.OnMain(launch.Manager.ParkMain)); // on the main actor, as the window's close is
                Assert.True(launch.Manager.MainParked);
                Assert.False(window.IsVisible);
                if (kind == Marrow.App.Window.WindowManager.ParkKind.Close)
                {
                    Assert.Null(launch.Manager.MainWindow); // the window is gone ...
                    Assert.Null(window.Content); // ... and holds nothing
                }
                else
                {
                    Assert.Same(window, launch.Manager.MainWindow); // the fallback keeps the window and its tree
                    Assert.NotNull(window.Content);
                }
                for (var frame = 0; frame < 150; frame++)
                {
                    launch.Harness.Main.Advance(TimeSpan.FromSeconds(1.0 / 60));
                    frameSource.Advance(TimeSpan.FromSeconds(1.0 / 60));
                    Pump(launch);
                }
                for (var pass = 0; pass < 3; pass++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    Pump(launch);
                }

                // 1. The model kept its thread, its runtime, and the timers it had.
                Assert.Same(runtimeBefore, launch.Harness.Main.OnMain(() => app.ExistingRuntime(thread)));
                Assert.Equal(thread, app.SelectedThreadID);
                Assert.True(timersBefore > 0);
                Assert.Contains(clock.TimerDeadlines, deadline => deadline <= clock.Elapsed + TimeSpan.FromSeconds(61)); // the scheduler's timer, which sleeps a minute at most

                // 2. A session still takes a turn and ends it with no window.
                rig.Turn("And the docs?", Second);
                Assert.Equal([First, Second], rig.Said());

                // 3. The scheduler's timer fires and a duty completes.
                clock.Advance(TimeSpan.FromMinutes(10));
                Assert.Equal("d1", Assert.Single(launcher.Fires).Duty.Id);
                scheduler.MissionEnded(launcher.LastMission!.Value, DutyRunOutcome.Done, "RESULT: nothing_changed");
                Assert.Equal(DutyRunOutcome.NothingChanged, scheduler.Find("d1")!.History[0].Outcome);

                // 4. Showing the window again builds the view from the model: the same project and thread are on screen.
                var again = Show(launch);
                Assert.Contains("Project", again, StringComparison.Ordinal);
                Assert.Contains("Fix it.", again, StringComparison.Ordinal);
                Assert.True(launch.Manager.MainWindow!.IsVisible);
                Assert.False(launch.Manager.MainParked);
                if (kind == Marrow.App.Window.WindowManager.ParkKind.Close) Assert.NotSame(window, launch.Manager.MainWindow);
                else Assert.Same(window, launch.Manager.MainWindow);
            }
            finally
            {
                launch.Harness.Main.OnMain(() =>
                {
                    if (launch.Manager.MainWindow is { } main)
                    {
                        main.Content = null;
                        main.Hide();
                    }
                });
                for (var pass = 0; pass < 4; pass++) Pump(launch); // what the teardown posted runs here, on the main actor, not in the rig's own clean-up
            }
        });
    }

    [Fact]
    public Task Closing_the_main_window_for_real_leaves_the_sessions_the_timers_and_the_scheduler_running_and_showing_it_again_builds_a_new_one_from_the_model() =>
        Run(Marrow.App.Window.WindowManager.ParkKind.Close);

    [Fact]
    public Task The_fallback_of_hiding_the_window_does_the_same_and_keeps_the_window_and_its_tree() =>
        Run(Marrow.App.Window.WindowManager.ParkKind.Hide);
}
