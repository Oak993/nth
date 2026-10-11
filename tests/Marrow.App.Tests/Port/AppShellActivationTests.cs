using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Port;

// AppDelegate.swift:28 starts UpdateChecker.swift:141-149's didBecomeActive observer.
// These tests cover the main window's hook (the seam AppShell.AddWindowProcedureHook records it), the message
// dispatch and headless window activation. Windows' own delivery of WM_ACTIVATEAPP to a window of the app (winuser.h,
// documented) and Avalonia's call of a window's hooks are the platform's and the framework's; a headless window has no
// Win32 procedure to send through.
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
        var hooks = new RecordedHooks();
        using var seam = AppShell.AddWindowProcedureHook.Override(hooks.Add);

        launch.Harness.Main.OnMain(shell.FinishLaunching);
        // AppDelegate.swift:28 observes NSApplication.didBecomeActiveNotification, not
        // NSWindow.didBecomeKey. WM_ACTIVATEAPP is the Windows application boundary, delivered to the main window's
        // own procedure: the hook FinishLaunching put on the main window, not a direct call of the shell.
        var handled = false;
        var hook = Assert.Single(hooks.Added).Hook;
        hook(0, 0x001C, 1, 0, ref handled);
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
        Assert.Same(main, settings); // OD-0140: Settings is a panel inside the main window, no window of its own
        Assert.True(launch.Manager.IsSettingsOpen);
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

    [Fact]
    [Trait("Req", "PR-LAUNCH-046")] // AppDelegate.swift:22, :28; UpdateChecker.swift:141-149; owner_decisions.md OD-0040
    [Trait("Decision", "ED-103")]
    public void The_launch_activation_reaches_the_update_observer_because_the_main_window_is_hooked_before_the_boot_shows_it() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        // The production boot (AppDelegate.StartBoot's default), not the harness's plain show.
        var production = new Marrow.App.AppDelegate().StartBoot;
        BootStageWindow? boot = null;
        launch.Delegate.StartBoot = (windows, show) => boot = production(windows, show);
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
        var hooks = new RecordedHooks();
        using var seam = AppShell.AddWindowProcedureHook.Override(hooks.Add);
        try
        {
            launch.Harness.Main.OnMain(shell.FinishLaunching);
            Dispatcher.UIThread.RunJobs();
            // The stage is up; the main window is made (and hooked) but not shown yet, and :28's observer is installed.
            Assert.NotNull(boot);
            var main = launch.Manager.MainWindow;
            Assert.NotNull(main);
            Assert.False(main.IsVisible);
            Assert.Null(launch.Checker.LastCheckedAt);
            // The hook is on the main window itself, and was put there while that window was not yet shown (so the first
            // activation the boot's show causes cannot be missed).
            var (hooked, hook, visibleWhenHooked) = Assert.Single(hooks.Added);
            Assert.Same(main, hooked);
            Assert.False(visibleWhenHooked);

            // OD-0040: the main window's first activation (WM_ACTIVATEAPP when the boot shows it) is an activation like
            // any other, and nothing has been checked yet, so the catch-up check runs (:146-147).
            var handled = false;
            hook(0, 0x001C, 1, 0, ref handled);
            launch.Harness.Main.Loop.RunUntil(() => launch.Checker.LastCheckedAt is not null, TimeSpan.FromSeconds(3));
            Assert.NotNull(launch.Checker.LastCheckedAt);
        }
        finally
        {
            boot?.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-046")] // AppDelegate.swift:28; UpdateChecker.swift:141-149: the observer is installed at launch, on the main window
    public void FinishLaunching_puts_exactly_one_procedure_hook_on_the_main_window_and_that_hook_is_the_shells() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
        var hooks = new RecordedHooks();
        using var seam = AppShell.AddWindowProcedureHook.Override(hooks.Add);

        launch.Harness.Main.OnMain(shell.FinishLaunching);

        var (window, hook, _) = Assert.Single(hooks.Added);
        Assert.Same(launch.Manager.MainWindow, window);
        // The hook does the shell's work: WM_ACTIVATEAPP TRUE reaches the update checker, WM_ACTIVATEAPP FALSE does not.
        var handled = false;
        hook(0, 0x001C, 0, 0, ref handled);
        launch.Harness.Main.Loop.RunUntilIdle();
        Assert.Null(launch.Checker.LastCheckedAt);
        hook(0, 0x001C, 1, 0, ref handled);
        launch.Harness.Main.Loop.RunUntil(() => launch.Checker.LastCheckedAt is not null, TimeSpan.FromSeconds(3));
        Assert.NotNull(launch.Checker.LastCheckedAt);
    });
}
