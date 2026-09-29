using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;

namespace Marrow.App.Tests.Window;

// WindowManager.swift:98, OD-0008 C1/C2; AppDelegate.swift:44-54.
// The real FinishLaunching binding and main.Close route must preserve the merge wait.
// Process exit itself is independently observed by WindowManagerProcessTests.
[Collection("Headless")]
public sealed class WindowManagerTerminationWaitTests(HeadlessSession headless)
{
    private void WithMergingMain(Action<LaunchHarness, AppShell, Action, Func<int>> check) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h =>
        {
            h.Defaults.Set("hasSeenTour", true);
            var project = AppModelSamples.Project("Window wait", h.NewFolder("project"));
            h.WriteLibrary([project], [AppModelSamples.Thread(project)]);
        });
        using var toast = ToastNotifications.Center.Override(new RecordingAppToastCenter());
        var exits = 0;
        var shell = new AppShell(launch.Delegate, () => exits++, () => { });
        launch.Harness.Main.OnMain(shell.FinishLaunching);
        var runtime = launch.Harness.Main.OnMain(() => launch.Manager.Model.Runtime(launch.Manager.Model.Threads[0].Id));
        launch.Harness.Main.OnMain(() => runtime.IsHydraMerging = true);
        try { check(launch, shell, () => launch.Harness.Main.OnMain(() => runtime.IsHydraMerging = false), () => exits); }
        finally
        {
            launch.Harness.Main.OnMain(() => runtime.IsHydraMerging = false);
            launch.Harness.Main.AdvanceMilliseconds(250);
            launch.Harness.Main.OnMain(() =>
            {
                UpdateCheckerLoop.Stop(launch.Checker);
                if (launch.Manager.MainWindow is { } main) { main.Content = null; main.Hide(); }
                Dispatcher.UIThread.RunJobs();
            });
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-003")] // OD-0008 C1; AppDelegate.swift:45-53.
    [Trait("Req", "PR-WIN-008")]
    public void The_bound_main_close_stays_visible_and_waits_until_the_next_250_millisecond_merge_poll()
    {
        WithMergingMain((launch, _, finishMerge, exits) =>
        {
            var main = Assert.IsType<ThreadWindow>(launch.Manager.MainWindow);
            launch.Harness.Main.OnMain(main.Close);
            Assert.True(main.IsVisible);
            Assert.Equal(0, exits());
            launch.Harness.Main.AdvanceMilliseconds(1000);
            Assert.Equal(0, exits());
            launch.Harness.Main.OnMain(main.Close);
            finishMerge();
            launch.Harness.Main.AdvanceMilliseconds(249);
            Assert.True(main.IsVisible);
            Assert.Equal(0, exits());
            launch.Harness.Main.AdvanceMilliseconds(1);
            Assert.Equal(1, exits());
            launch.Harness.Main.AdvanceMilliseconds(1000);
            Assert.Equal(1, exits());
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-003")] // OD-0008 C1; AppDelegate.swift:47-51.
    [Trait("Req", "PR-WIN-008")]
    public void The_bound_main_close_waits_exactly_four_minutes_for_a_merge_that_does_not_finish()
    {
        WithMergingMain((launch, _, _, exits) =>
        {
            var main = Assert.IsType<ThreadWindow>(launch.Manager.MainWindow);
            launch.Harness.Main.OnMain(main.Close);
            launch.Harness.Main.AdvanceMilliseconds(239999);
            Assert.True(main.IsVisible);
            Assert.Equal(0, exits());
            launch.Harness.Main.AdvanceMilliseconds(1);
            Assert.Equal(1, exits());
        });
    }
}
