using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4; ED-NEW-dextra-as10-13): what closing the main window does. With "Keep working when the window is
// closed" on, the close parks the window and the app goes on; off, the close asks the shell to quit as it always did. The switch is read
// when the window is closed, not when it is made, so the owner's tap takes effect at once. Settings close with their own window.
[Collection("Headless")]
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-13")]
public sealed class WindowKeepWorkingCloseTests(HeadlessSession headless)
{
    private static void Pump(LaunchHarness launch) => launch.Harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs());

    private static WindowManager Shown(LaunchHarness launch)
    {
        launch.Manager.MainContent = () => new TextBlock { Text = "nth" };
        launch.Harness.Main.OnMain(launch.Manager.PrepareMain);
        launch.Harness.Main.OnMain(launch.Manager.ShowMain);
        Pump(launch);
        return launch.Manager;
    }

    private static void CloseMain(LaunchHarness launch)
    {
        launch.Harness.Main.OnMain(() => launch.Manager.MainWindow!.Close());
        for (var pass = 0; pass < 3; pass++) Pump(launch);
    }

    [Fact]
    public void With_keep_working_on_closing_the_window_parks_it_and_does_not_quit()
    {
        WindowedThreadRig.WithLaunch(headless, null, launch =>
        {
            var quits = 0;
            var manager = Shown(launch);
            manager.MainWindowClose = () => quits++;
            manager.KeepWorkingOnClose = () => true;
            var window = manager.MainWindow!;
            CloseMain(launch);
            Assert.Equal(0, quits);
            Assert.True(manager.MainParked);
            Assert.False(window.IsVisible);
            Assert.Null(manager.MainWindow); // closed for real: a new one is made at the next show
        });
    }

    [Fact]
    public void With_keep_working_off_closing_the_window_asks_the_shell_to_quit_and_leaves_the_window_up()
    {
        WindowedThreadRig.WithLaunch(headless, null, launch =>
        {
            var quits = 0;
            var manager = Shown(launch);
            manager.MainWindowClose = () => quits++;
            manager.KeepWorkingOnClose = () => false;
            CloseMain(launch);
            Assert.Equal(1, quits);
            Assert.False(manager.MainParked);
            Assert.True(manager.MainWindow!.IsVisible);
        });
    }

    [Fact]
    public void The_switch_is_read_at_the_close_so_the_owners_tap_takes_effect_at_once()
    {
        WindowedThreadRig.WithLaunch(headless, null, launch =>
        {
            var quits = 0;
            var on = false;
            var manager = Shown(launch);
            manager.MainWindowClose = () => quits++;
            manager.KeepWorkingOnClose = () => on;
            CloseMain(launch);
            Assert.Equal(1, quits);
            on = true; // the owner turns it on; the window is still the one made before
            CloseMain(launch);
            Assert.Equal(1, quits);
            Assert.True(manager.MainParked);
        });
    }

    [Fact]
    public void A_parked_window_comes_back_as_a_new_window_that_the_shell_is_told_about()
    {
        WindowedThreadRig.WithLaunch(headless, null, launch =>
        {
            var manager = Shown(launch);
            manager.KeepWorkingOnClose = () => true;
            var made = new List<Avalonia.Controls.Window>();
            manager.MainWindowMade = made.Add;
            var first = manager.MainWindow!;
            Assert.True(launch.Harness.Main.OnMain(manager.ParkMain));
            Assert.Null(manager.MainWindow);
            launch.Harness.Main.OnMain(manager.PrepareMain);
            launch.Harness.Main.OnMain(manager.ShowMain);
            Pump(launch);
            Assert.NotSame(first, manager.MainWindow);
            Assert.True(manager.MainWindow!.IsVisible);
            Assert.False(manager.MainParked);
            Assert.Same(manager.MainWindow, Assert.Single(made));
            Assert.True(launch.Harness.Main.OnMain(manager.ParkMain));
            launch.Harness.Main.OnMain(() => manager.MainWindow?.Hide());
        });
    }
}
