using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Xunit;

namespace Marrow.App.Tests.Port;

// AppDelegate.swift:28 starts UpdateChecker.swift:141-149's didBecomeActive observer.
// This covers a later window activation; the initial launch activation still needs a ruling.
[Collection("Headless")]
public sealed class AppShellActivationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-046")]
    public void Reactivating_the_main_window_after_launch_checks_when_the_update_observer_is_stale() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);

        launch.Harness.Main.OnMain(shell.FinishLaunching);
        var main = LaunchHarness.WindowOf(launch.MainContent)!;
        // The launch activation order is unresolved. Force a later activation while the
        // 8-second scheduled check has not fired, so only the observer can run Check.
        launch.Harness.Main.OnMain(main.Hide);
        Dispatcher.UIThread.RunJobs();
        launch.Harness.Main.OnMain(main.Show);
        Dispatcher.UIThread.RunJobs();
        launch.Harness.Main.OnMain(main.Activate);
        Dispatcher.UIThread.RunJobs();
        launch.Harness.Main.Loop.RunUntil(() => launch.Checker.LastCheckedAt is not null, TimeSpan.FromSeconds(3));

        Assert.NotNull(launch.Checker.LastCheckedAt);
    });
}
