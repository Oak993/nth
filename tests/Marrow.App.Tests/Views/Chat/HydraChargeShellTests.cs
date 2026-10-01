using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// HydraButton.swift:475-476, :518-528: the charge views observe NSApplication.didBecomeActiveNotification and
// NSWindow.didChangeOcclusionStateNotification. The port's application shell (Port/AppShell.cs, WM_ACTIVATEAPP) and window
// manager (Window/WindowManager.cs, shown/hidden and restored/minimised as the ENGINEERING DECISION there) post them.
[Collection("Headless")]
public sealed class HydraChargeShellTests(HeadlessSession headless)
{
    private static HydraChargeLayerView Charge()
    {
        var view = new HydraChargeLayerView();
        view.Configure(HydraCharge.LayerColors, new SwiftColor(0.2, 0.4, 0.9));
        return view;
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-241")] // HydraButton.swift:475, :518-521 the app becoming active reinstalls the animations
    public void The_application_shells_activation_reaches_the_charge_views() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var recorder = new ShellRecorder();
        var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
        launch.Harness.Main.OnMain(shell.FinishLaunching);
        var view = Charge();
        launch.MainContent.Child = view;
        Dispatcher.UIThread.RunJobs();
        var installs = view.InstallCount;

        var handled = false;
        shell.WndProc(0, 0x001C, 0, 0, ref handled); // WM_ACTIVATEAPP deactivation: nothing
        Assert.Equal(installs, view.InstallCount);
        shell.WndProc(0, 0x001C, 1, 0, ref handled); // activation
        Assert.Equal(installs + 1, view.InstallCount);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-242")] // HydraButton.swift:476, :523-528 its own window coming back into view reinstalls
    public void Restoring_the_main_window_reinstalls_its_charge_and_minimising_it_does_not() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var launch = new LaunchHarness();
        var view = Charge();
        launch.MainContent.Child = view;
        launch.Harness.Main.OnMain(() => launch.Manager.ShowMain());
        Dispatcher.UIThread.RunJobs();
        var main = LaunchHarness.WindowOf(launch.MainContent)!;
        var installs = view.InstallCount;

        launch.Harness.Main.OnMain(() => main.WindowState = WindowState.Minimized);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(installs, view.InstallCount); // going out of view
        launch.Harness.Main.OnMain(() => main.WindowState = WindowState.Normal);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(installs + 1, view.InstallCount); // back in view
        launch.Harness.Main.OnMain(() => main.WindowState = WindowState.Maximized);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(installs + 1, view.InstallCount); // still in view: no change posted
    });
}
