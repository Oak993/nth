using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// PORT-ONLY unit under test: src/Marrow.App/Port/AppShell.cs, over real headless windows (R-TEST-7).
//   Launch: the shell hands NSApplication's finishLaunching to AppDelegate.swift:9-34 and from then on a close asked of
//     the main window itself (its close control, Alt+F4: CloseReason WindowClosing) is the terminate handshake
//     (OD-0008 C1, C2; PR-LAUNCH-074), where WindowManager.swift:98 alone would only order it out.
//   Reopen (ED-002; PR-LAUNCH-048 and PR-LAUNCH-072 windows_path): a second launch runs AppDelegate.swift:36-39 with
//     hasVisibleWindows as AppKit computes it (a miniaturized window is not visible), and the app is activated in both
//     cases: with a window up, the main window comes to the front.
[Collection("Headless")]
public sealed class AppShellSecondTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-074")]
    public void Closing_the_boot_shell_before_initialization_quits_without_constructing_the_model() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var frames = Marrow.UI.Port.FrameClock.Source.Override(new Marrow.UI.Port.ManualFrameSource());
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
        var prepare = typeof(AppShell).GetMethod("PrepareBoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(prepare);
        launch.Harness.Main.OnMain(() => prepare.Invoke(shell, null));
        var main = launch.Manager.MainWindow!;
        try
        {
            Assert.False(launch.Manager.HasCreatedModel);
            launch.Harness.Main.OnMain(main.Close);
            Assert.Equal(new[] { "exit" }, recorder.Calls);
            Assert.False(launch.Manager.HasCreatedModel);
        }
        finally
        {
            var boot = (BootStageWindow?)typeof(AppDelegate).GetField("preparedBoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(launch.Delegate);
            boot?.Window.Close();
            main.Hide();
        }
    });

    private void WithLaunch(Action<LaunchHarness, AppShell, ShellRecorder> body) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
        try
        {
            body(launch, shell, recorder);
        }
        finally
        {
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-074")] // App/Window/WindowManager.swift:98; owner_decisions.md OD-0008 C1, C2
    public void After_launch_closing_the_main_window_quits_through_the_handshake_and_does_not_hide_it_first()
    {
        WithLaunch((launch, shell, recorder) =>
        {
            launch.Harness.Main.OnMain(shell.FinishLaunching);
            var main = LaunchHarness.WindowOf(launch.MainContent);
            Assert.NotNull(main);
            Assert.True(main.IsVisible);

            launch.Harness.Main.OnMain(main.Close); // the close control or Alt+F4

            Assert.Equal(["exit"], recorder.Calls); // no merge running: .terminateNow (AppDelegate.swift:45)
            Assert.True(main.IsVisible); // left up for the lifetime's shutdown to close
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-048")] // App/AppDelegate.swift:36-39
    [Trait("Req", "PR-LAUNCH-072")] // engineering_decisions.md ED-002; App/AppDelegate.swift:36-39
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-WIN-072: a second launch with every window hidden shows the same cached main window
    [Trait("Req", "PR-WIN-072")] // App/AppDelegate.swift:36-39; engineering_decisions.md ED-002
    public void A_second_launch_with_the_main_window_hidden_shows_the_same_window_again()
    {
        WithLaunch((launch, shell, _) =>
        {
            launch.Harness.Main.OnMain(shell.FinishLaunching);
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            launch.Harness.Main.OnMain(main.Hide);
            Dispatcher.UIThread.RunJobs(); // the headless platform posts activation changes
            Assert.False(main.IsActive);

            launch.Harness.Main.OnMain(() => shell.Reopen([main]));
            Dispatcher.UIThread.RunJobs();

            Assert.True(main.IsVisible);
            Assert.True(main.IsActive);
            Assert.Single(launch.Events, e => e == "main window built");
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-048")] // App/AppDelegate.swift:36-39
    [Trait("Req", "PR-LAUNCH-072")] // engineering_decisions.md ED-002
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-WIN-072: a second launch with a window up builds nothing and brings the main window forward
    [Trait("Req", "PR-WIN-072")] // App/AppDelegate.swift:36-39; engineering_decisions.md ED-002
    public void A_second_launch_with_a_window_up_creates_nothing_and_brings_the_main_window_to_the_front()
    {
        WithLaunch((launch, shell, _) =>
        {
            launch.Harness.Main.OnMain(shell.FinishLaunching);
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            var other = new AvaloniaWindow();
            try
            {
                other.Show();
                Dispatcher.UIThread.RunJobs();
                // The headless platform never deactivates one window for another, so activation is counted.
                var mainActivations = 0;
                var otherActivations = 0;
                main.Activated += (_, _) => mainActivations++;
                other.Activated += (_, _) => otherActivations++;

                launch.Harness.Main.OnMain(() => shell.Reopen([other, main]));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, mainActivations); // the main window comes to the front, not merely the first one up
                Assert.Equal(0, otherActivations);
                Assert.True(other.IsVisible);
                Assert.Single(launch.Events, e => e == "main window built");
            }
            finally
            {
                other.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-048")] // App/AppDelegate.swift:36 hasVisibleWindows
    [Trait("Decision", "ED-002")] // engineering_decisions.md ED-002
    public void A_minimised_or_hidden_window_is_not_a_visible_window_and_a_shown_one_is()
    {
        headless.OnUiThread(() =>
        {
            var window = new AvaloniaWindow { ShowActivated = false };
            try
            {
                Assert.False(AppShell.HasVisibleWindows([window])); // never shown
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(AppShell.HasVisibleWindows([window]));
                window.WindowState = WindowState.Minimized; // NSWindow.isVisible is false while miniaturized
                Dispatcher.UIThread.RunJobs();
                Assert.False(AppShell.HasVisibleWindows([window]));
                Assert.False(AppShell.HasVisibleWindows([]));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
