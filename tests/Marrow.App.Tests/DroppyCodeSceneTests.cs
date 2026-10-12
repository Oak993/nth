using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using static Marrow.App.Tests.Fixtures.AppCommandsRig;
using static Marrow.App.Tests.Fixtures.LaunchSurfaceRig;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/DroppyCodeApp.swift (B3 52bc792), :21-25 body:
//   Settings { EmptyView() }.commands { AppCommands(model: WindowManager.shared.model) }
//   "The windows themselves are AppKit windows owned by WindowManager. This scene only carries the menus."
// The scene declares no window; evaluating it is the first access to WindowManager.shared (WindowManager.swift:10-13), whose
// `let model = AppModel()` is the one model every window and every menu item reads. WindowManager.swift:26-38 makes the main
// and the Settings window only when showMain() and showSettings() ask; DEVIATION OD-0151: Settings is a panel made inside the
// main window, not a window of its own.
// DEVIATION OD-0008 C3 (PR-LAUNCH-004, PR-WIN-001 deviation): the port builds no menu bar; the command set stays on its chords,
// reached through the key dispatch each window carries (Port/MenuKeyEquivalents.cs).
// DEVIATION OD-0008 C2 (PR-LAUNCH-073): the stock Quit menu item of the Mac has no counterpart; the window close control and
// Alt+F4 are the quit affordances, and no quit or exit item, label or shortcut is invented.
[Collection("Headless")]
public sealed class DroppyCodeSceneTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-004")] // App/DroppyCodeApp.swift:21-25; App/Window/WindowManager.swift:10-13, :26-38
    [Trait("Req", "PR-WIN-001")] // App/DroppyCodeApp.swift:21-25
    [Trait("Decision", "OD-0151")] // Settings is a panel inside the main window
    public void The_scene_makes_no_window_the_main_window_comes_with_showMain_and_the_Settings_panel_in_it_only_when_asked()
    {
        WithLaunch(headless, null, launch =>
        {
            // Evaluating the scene reads WindowManager.shared: its model exists, and no window does.
            Assert.Same(launch.Manager, WindowManager.Shared);
            Assert.NotNull(launch.Manager.Model);
            Assert.Same(launch.Manager.Model, WindowManager.Shared.Model); // one model, however often it is read
            Assert.Null(launch.Manager.MainWindow);
            Assert.Empty(launch.Events); // no window content has been built
            Assert.Null(LaunchHarness.WindowOf(launch.MainContent));
            Assert.Null(LaunchHarness.WindowOf(launch.SettingsContent));

            launch.Harness.Main.OnMain(launch.Manager.ShowMain);

            Assert.Equal(["main window built"], launch.Events); // :26-30 the main window, and no other
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            Assert.True(main.IsVisible);
            Assert.Null(LaunchHarness.WindowOf(launch.SettingsContent)); // :32-38 not before showSettings()
            Assert.Null(launch.Manager.SettingsOverlay);
            Assert.False(launch.Manager.IsSettingsOpen);

            launch.Harness.Main.OnMain(() => launch.Manager.ShowSettings());
            Assert.Equal(["main window built", "settings window built"], launch.Events);
            Assert.Same(main, LaunchHarness.WindowOf(launch.SettingsContent)); // OD-0151: the panel is in the main window, no window of its own
            Assert.Same(main, launch.Manager.SettingsOverlay!.Window);
            Assert.True(launch.Manager.IsSettingsOpen);
            launch.Harness.Main.OnMain(launch.Manager.ShowMain);
            Assert.Equal(["main window built", "settings window built"], launch.Events); // each is made once
            Assert.Same(main, launch.Manager.MainWindow);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-004")] // App/DroppyCodeApp.swift:23-24 AppCommands(model: WindowManager.shared.model)
    [Trait("Req", "PR-WIN-001")] // App/DroppyCodeApp.swift:21-25; App/Window/WindowManager.swift:13 the single model
    public void The_launch_delegate_the_main_window_and_its_command_chords_all_work_on_the_one_model()
    {
        WithLaunch(headless, h => ProjectWithUnusedThread(h), launch =>
        {
            using var shortcuts = ShortcutStore.Shared.Override(FreshStore());
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            var main = LaunchHarness.WindowOf(launch.MainContent)!;

            Assert.Same(launch.Manager.Model, launch.Delegate.Model); // AppDelegate.swift:16-17 reads WindowManager.shared.model
            launch.Harness.Main.OnMain(() =>
            {
                Assert.False(launch.Manager.Model.IsCommandPalettePresented);

                Press(main, Key.K, PhysicalKey.K, RawInputModifiers.Control); // AppCommands.swift:40-41: the palette's chord

                // The command table the scene installs acts on the very model the manager owns, not on one of its own.
                Assert.True(launch.Manager.Model.IsCommandPalettePresented);
                Assert.True(launch.Delegate.Model!.IsCommandPalettePresented);
            });
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-004")] // App/DroppyCodeApp.swift:21-25; owner_decisions.md OD-0008 C3
    [Trait("Req", "PR-WIN-001")] // App/DroppyCodeApp.swift:21-25; owner_decisions.md OD-0008 C3
    [Trait("Decision", "OD-0151")] // Settings is a panel inside the main window
    public void No_menu_bar_is_built_for_the_main_window_with_the_Settings_panel_open_or_for_the_application()
    {
        WithLaunch(headless, TourSeen, launch =>
        {
            var recorder = new ShellRecorder();
            var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
            launch.Harness.Main.OnMain(shell.FinishLaunching); // the whole launch, as the app runs it
            launch.Harness.Main.OnMain(() => launch.Manager.ShowSettings());
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
            Assert.Same(main, settings); // OD-0151: the panel is in the main window
            Assert.True(launch.Manager.IsSettingsOpen);

            Assert.Null(NativeMenu.GetMenu(Avalonia.Application.Current!));
            Assert.Null(NativeMenu.GetMenu(main));
            Assert.Empty(main.GetVisualDescendants().OfType<Menu>()); // the Settings panel included
            Assert.Empty(main.GetVisualDescendants().OfType<NativeMenuBar>());
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-073")] // App/AppCommands.swift:11-114 no CommandGroup for .appTermination; owner_decisions.md OD-0008 C2
    public void The_command_table_and_the_shortcut_store_have_no_quit_or_exit_command()
    {
        WithLaunch(headless, null, launch =>
        {
            using var shortcuts = ShortcutStore.Shared.Override(FreshStore());
            launch.Harness.Main.OnMain(() =>
            {
                var body = new AppCommands(launch.Manager.Model).Body;

                Assert.NotEmpty(body);
                Assert.DoesNotContain(body, command => HasQuitWording(command.Title));
                Assert.DoesNotContain(body, command => command.Shortcut == Chord("q", ChordModifiers.Control)); // the Mac's Command-Q, as Ctrl
            });
            Assert.DoesNotContain(Enum.GetNames<AppShortcut>(), name => HasQuitWording(name)); // no rebindable quit chord either
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-073")] // owner_decisions.md OD-0008 C2; App/AppDelegate.swift:44-45; Port/AppShell.cs terminate(_:)
    public void The_window_close_is_the_quit_and_no_key_chord_of_the_app_quits()
    {
        WithLaunch(headless, TourSeen, launch =>
        {
            using var shortcuts = ShortcutStore.Shared.Override(FreshStore());
            var recorder = new ShellRecorder();
            var shell = new AppShell(launch.Delegate, recorder.Exit, recorder.AfterWillTerminate);
            launch.Harness.Main.OnMain(shell.FinishLaunching);
            var main = LaunchHarness.WindowOf(launch.MainContent)!;

            Press(main, Key.Q, PhysicalKey.Q, RawInputModifiers.Control); // the Mac's Command-Q
            Press(main, Key.F4, PhysicalKey.F4, RawInputModifiers.Alt); // Alt+F4 is the system's: the window is asked to close, not a chord
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(recorder.Calls);
            Assert.True(main.IsVisible);

            launch.Harness.Main.OnMain(main.Close); // what the close control and the system's Alt+F4 both do to the window

            Assert.Equal(["exit"], recorder.Calls); // the termination handshake of AppDelegate.swift:44-54, asked once
        });
    }

    private static bool HasQuitWording(string text) =>
        text.Contains("quit", StringComparison.OrdinalIgnoreCase) || text.Contains("exit", StringComparison.OrdinalIgnoreCase);
}
