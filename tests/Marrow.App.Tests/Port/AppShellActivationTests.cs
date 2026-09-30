using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Xunit;

namespace Marrow.App.Tests.Port;

// AppDelegate.swift:28 starts UpdateChecker.swift:141-149's didBecomeActive observer.
// These tests cover message dispatch and headless window activation. Native delivery and
// the initial macOS launch activation order remain separate STOPs in REPORT.md.
[Collection("Headless")]
public sealed class AppShellActivationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-046")] // AppDelegate.swift:28; UpdateChecker.swift:141-149
    public void Reactivating_the_application_after_launch_checks_when_the_update_observer_is_stale() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);

        launch.Harness.Main.OnMain(shell.FinishLaunching);
        // AppDelegate.swift:28 observes NSApplication.didBecomeActiveNotification, not
        // NSWindow.didBecomeKey. WM_ACTIVATEAPP is the Windows application boundary.
        var handled = false;
        shell.WndProc(0, 0x001C, 1, 0, ref handled);
        launch.Harness.Main.Loop.RunUntil(() => launch.Checker.LastCheckedAt is not null, TimeSpan.FromSeconds(3));

        Assert.False(handled);
        Assert.NotNull(launch.Checker.LastCheckedAt);
    });

    [Theory]
    [Trait("Req", "PR-LAUNCH-046")] // UpdateChecker.swift:141-149 observes didBecomeActive only
    [InlineData(0x001C, 0)] // WM_ACTIVATEAPP deactivation
    [InlineData(0x0006, 1)] // WM_ACTIVATE window focus
    [InlineData(0x0000, 1)] // WM_NULL
    public void Other_messages_and_app_deactivation_do_not_run_the_activation_observer(uint message, int wParam) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
        launch.Harness.Main.OnMain(shell.FinishLaunching);
        var handled = true;

        var result = shell.WndProc(0, message, wParam, 0, ref handled);
        launch.Harness.Main.Loop.RunUntilIdle();

        Assert.Equal((nint)0, result);
        Assert.True(handled);
        Assert.Null(launch.Checker.LastCheckedAt);
        Assert.Empty(recorder.Calls);
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-046")]
    public void A_main_window_activation_event_does_not_run_the_application_activation_observer() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
        launch.Harness.Main.OnMain(shell.FinishLaunching);
        var main = LaunchHarness.WindowOf(launch.MainContent)!;
        launch.Harness.Main.OnMain(() => launch.Manager.ShowSettings());
        Dispatcher.UIThread.RunJobs();
        var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
        Assert.True(settings.IsActive);
        // Avalonia 12.1.2 HeadlessWindowImpl.Activate posts Activated without deactivating
        // other windows. Observe that real headless event, without claiming native focus.
        var activations = 0;
        main.Activated += (_, _) => activations++;

        launch.Harness.Main.OnMain(main.Activate);
        Dispatcher.UIThread.RunJobs();
        launch.Harness.Main.Loop.RunUntilIdle();

        Assert.Equal(1, activations);
        Assert.True(main.IsActive);
        Assert.Null(launch.Checker.LastCheckedAt);
    });
}
