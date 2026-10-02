using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Sidebar;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.AppCommandsRig;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppCommands.swift (B3 52bc792). The finishing pass over the records AppCommandsTests left on
// partial evidence: PR-WIN-092, PR-WIN-101, PR-WIN-113, PR-WIN-114 and the palette half of the "no palette row" clauses.
// DEVIATION OD-0008 C3: the port draws no menu bar, so each test presses chords through MenuKeyEquivalents or
// AppCommands.PerformKeyEquivalent. Chords are the defaults of Core/Models/Shortcuts.swift:216-234 in Windows notation
// (ED-050 (a), ED-099); ShortcutStore.shared is replaced by a store read from the test's own empty defaults.
[Collection("Headless")]
public sealed class AppCommandsFinishTests(HeadlessSession headless)
{
    private static readonly KeyboardShortcut CtrlW = Chord("w", ChordModifiers.Control); // archiveThread, 13 + command
    private static readonly KeyboardShortcut CtrlS = Chord("s", ChordModifiers.Control); // settleThread, 1 + command
    private static readonly KeyboardShortcut CtrlAltU = Chord("u", ChordModifiers.Control | ChordModifiers.Alt); // toggleActivityView, 32 + option, command

    private static MenuCommand On(AppCommands commands, KeyboardShortcut shortcut) => commands.Body.Single(command => command.Shortcut == shortcut);

    // ===== "no palette row": the source gives these commands none (CommandPalette.swift:153-178) =====

    [Fact]
    [Trait("Req", "PR-WIN-092")] // App/AppCommands.swift:21-25 (the source gives the command no palette row); Views/Palette/CommandPalette.swift:153-178
    [Trait("Req", "PR-WIN-101")] // App/AppCommands.swift:48-51; Views/Palette/CommandPalette.swift:153-178
    [Trait("Req", "PR-WIN-113")] // App/AppCommands.swift:103-105; Views/Palette/CommandPalette.swift:153-178
    [Trait("Req", "PR-WIN-222")] // App/AppCommands.swift:53-54
    [Trait("Req", "PR-WIN-223")] // App/AppCommands.swift:55-56
    [Trait("Req", "PR-WIN-224")] // App/AppCommands.swift:57-58
    [Trait("Req", "UI-WIN-007")] // App/AppCommands.swift:21-25
    [Trait("Req", "UI-WIN-015")] // App/AppCommands.swift:48-51
    [Trait("Req", "UI-WIN-025")] // App/AppCommands.swift:103-105
    [Trait("Req", "UI-WIN-074")] // App/AppCommands.swift:53-54
    [Trait("Req", "UI-WIN-077")] // App/AppCommands.swift:59-62
    public void The_palette_holds_only_the_source_actions_so_the_show_main_zoom_activity_and_archive_prompt_commands_have_no_row() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h =>
        {
            var project = AppModelSamples.Project("Alpha", h.NewFolder("alpha"));
            h.WriteLibrary([project], [AppModelSamples.Thread(project)]);
        });
        using var store = ShortcutStore.Shared.Override(FreshStore());
        CommandPalette palette = null!;
        AvaloniaWindow main = null!;
        launch.Harness.Main.OnMain(() =>
        {
            var model = launch.Manager.Model;
            model.SelectedThreadID = model.Threads[0].Id; // CommandPalette.swift:164: the selection adds the thread actions
            palette = new CommandPalette();
            launch.MainContent.Child = palette;
            model.IsCommandPalettePresented = true;
            launch.Manager.ShowMain();
            main = LaunchHarness.WindowOf(launch.MainContent)!;
        });

        string[] Rows(string query)
        {
            launch.Harness.Main.OnMain(() =>
            {
                palette.QueryField.Text = query;
                Dispatcher.UIThread.RunJobs();
            });
            return launch.Harness.Main.OnMain(() => RowNames(palette));
        }

        try
        {
            // :151-178 with a thread selected and an empty query: the thread, then New thread, New thread in worktree,
            // Add project, Toggle terminal, Toggle changes, the finish action (Settle by default) and Settings, then the
            // Welcome tour row OD-0012 G5 adds (placed by OD-0053).
            var all = Rows("");
            Assert.Equal(9, all.Length);
            Assert.Equal(["New thread", "New thread in worktree", "Add project", "Toggle terminal", "Toggle changes", "Settle thread", "Settings", "Welcome tour"], all.Skip(1));
            // A query is a plain title substring test (:180): nothing carries these commands' titles.
            foreach (var query in new[] { "zoom", "actual size", "activity", "archive", AppInfo.Current.Name.ToLowerInvariant() })
                Assert.Empty(Rows(query));
        }
        finally { main.Hide(); Dispatcher.UIThread.RunJobs(); }
    });

    // ===== PR-WIN-101: Toggle activity view, through a real key press =====

    [Fact]
    [Trait("Req", "PR-WIN-101")] // App/AppCommands.swift:48-51; App/AppSettings.swift (sidebarActivityView persisted)
    [Trait("Decision", "ED-099")]
    public void Ctrl_Alt_U_pressed_in_a_window_flips_the_activity_list_is_consumed_and_the_setting_survives_a_restart() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        h.WriteLibrary([], []);
        var model = new AppModel();
        var commands = new AppCommands(model);
        var window = Show(new AvaloniaWindow());
        var passed = Passed(window);
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        Assert.False(model.Settings.SidebarActivityView);
        Press(window, Key.U, PhysicalKey.U, RawInputModifiers.Control | RawInputModifiers.Alt); // Option-Command-U, Shortcuts.swift:232
        Assert.True(model.Settings.SidebarActivityView); // :49-50 the list switches to the activity list
        Assert.Empty(passed); // taken by the command: no control in the window sees the chord
        Assert.True(h.Defaults.Object("sidebarActivityView") is true); // written through the settings store
        Assert.True(new AppModel().Settings.SidebarActivityView); // so it survives a restart

        Press(window, Key.U, PhysicalKey.U, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.False(model.Settings.SidebarActivityView); // the same chord flips it back
        Assert.True(h.Defaults.Object("sidebarActivityView") is false);
        Assert.Empty(passed);
        window.Close();
    });

    // ===== PR-WIN-113 and UI-WIN-025: Archive thread... asks, and the question is carried out by whoever takes it =====

    [Fact]
    [Trait("Req", "PR-WIN-113")] // App/AppCommands.swift:103-105; App/AppModel.swift:902-921
    [Trait("Req", "UI-WIN-025")] // App/AppCommands.swift:103-105; App/AppModel.swift:911-920
    public void With_no_row_to_ask_on_the_archive_chord_archives_the_thread_80_milliseconds_later() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var asked = AppModelSamples.Thread(project);
        var other = AppModelSamples.Thread(project);
        var model = Model(h, [project], [asked, other]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            model.SelectedThreadID = asked.Id;
            // Something typed for it keeps it from being the empty thread that goes with no question (AppModel.swift:906-910).
            model.Runtime(asked.Id).Draft = new ComposerDraft { Text = "keep" };
            Assert.True(commands.PerformKeyEquivalent(CtrlW));
            Assert.Equal(asked.Id, model.AppModelArchiveRequest?.ThreadID); // :911-912 the question is raised
            Assert.False(model.Thread(asked.Id)!.IsArchived); // and nothing is archived yet
        });

        // :913-920 "No row to ask on (the sidebar is hidden, say): the thread is archived, which is what Return would have done."
        h.Main.Advance(TimeSpan.FromMilliseconds(100));
        h.Main.OnMain(() =>
        {
            Assert.True(model.Thread(asked.Id)!.IsArchived);
            Assert.Null(model.AppModelArchiveRequest);
            Assert.False(model.Thread(other.Id)!.IsArchived);
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-113")] // App/AppCommands.swift:103-105; App/Views/Sidebar/SidebarView.swift:198-202
    [Trait("Req", "UI-WIN-025")] // App/AppCommands.swift:103-105; App/Views/Sidebar/SidebarView.swift:198-202
    public void A_sidebar_row_takes_the_archive_question_so_the_thread_stays_until_the_user_answers() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var asked = AppModelSamples.Thread(project);
        var model = Model(h, [project], [asked]);
        SidebarView view = null!;
        h.Main.OnMain(() => view = MountedSidebar(model));
        try
        {
            h.Main.OnMain(() =>
            {
                var commands = new AppCommands(model);
                model.SelectedThreadID = asked.Id;
                model.Runtime(asked.Id).Draft = new ComposerDraft { Text = "keep" };
                Assert.True(commands.PerformKeyEquivalent(CtrlW));
            });
            h.Main.OnMain(() =>
            {
                Assert.Null(model.AppModelArchiveRequest); // SidebarView.swift:200: the row took it up
                Assert.Equal(asked.Id, PendingArchive(view)); // :201 and it is asking on the thread's own row
            });
            h.Main.Advance(TimeSpan.FromMilliseconds(100)); // AppModel.swift:917: no request left, so no fallback archive
            h.Main.OnMain(() =>
            {
                Assert.False(model.Thread(asked.Id)!.IsArchived);
                Assert.Equal(asked.Id, PendingArchive(view));
            });
        }
        finally { h.Main.OnMain(() => UnmountSidebar(view)); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-113")] // App/AppCommands.swift:103-105; App/AppModel.swift:906-910
    public void The_archive_chord_on_a_loaded_thread_with_nothing_in_it_deletes_it_with_no_question() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var empty = AppModelSamples.Thread(project);
        var other = AppModelSamples.Thread(project);
        var model = Model(h, [project], [empty, other]);
        var runtime = h.Main.OnMain(() => model.Runtime(empty.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            model.SelectedThreadID = empty.Id;
            Assert.True(commands.PerformKeyEquivalent(CtrlW));
            Assert.Null(model.AppModelArchiveRequest); // :909 returns before the question is raised
            Assert.Null(model.Thread(empty.Id)); // :908 delete(id), the way an empty tab goes
            Assert.NotNull(model.Thread(other.Id));
        });
    });

    // ===== PR-WIN-114: Settle thread / Reopen thread, carried out =====

    [Fact]
    [Trait("Req", "PR-WIN-114")] // App/AppCommands.swift:106-108; App/AppModel.swift:887-898
    public void The_settle_chord_settles_then_reopens_the_selected_thread_after_80_milliseconds_and_never_archives() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        AppCommands commands = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.SettleSound = false;
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; // the item settles whatever Settings chose (:106)
            model.SelectedThreadID = thread.Id;
            commands = new AppCommands(model);
            Assert.Equal("Settle thread", On(commands, CtrlS).Title); // :106 not settled
            Assert.True(commands.PerformKeyEquivalent(CtrlS));
            Assert.Equal((thread.Id, false), (model.AppModelFinishRequest!.ThreadID, model.AppModelFinishRequest.Reopens)); // :889-890
            Assert.False(model.Thread(thread.Id)!.IsSettled);
        });

        // :891-897 with no row to take the request, the thread is settled 80 ms later.
        h.Main.Advance(TimeSpan.FromMilliseconds(100));
        h.Main.OnMain(() =>
        {
            Assert.True(model.Thread(thread.Id)!.IsSettled);
            Assert.False(model.Thread(thread.Id)!.IsArchived);
            Assert.Null(model.AppModelFinishRequest);
            Assert.Equal("Reopen thread", On(commands, CtrlS).Title); // :106 now settled
            Assert.True(commands.PerformKeyEquivalent(CtrlS));
            Assert.Equal((thread.Id, true), (model.AppModelFinishRequest!.ThreadID, model.AppModelFinishRequest.Reopens));
        });

        h.Main.Advance(TimeSpan.FromMilliseconds(100));
        h.Main.OnMain(() =>
        {
            Assert.False(model.Thread(thread.Id)!.IsSettled); // reopened
            Assert.False(model.Thread(thread.Id)!.IsArchived);
            Assert.Equal("Settle thread", On(commands, CtrlS).Title);
        });
    });

    // ===== PR-WIN-092 and UI-WIN-007: the main window item =====

    [Fact]
    [Trait("Req", "PR-WIN-092")] // App/AppCommands.swift:21-25; Core/Support/Text.swift:4; App/Window/WindowManager.swift:26-30, :57-60, :63
    [Trait("Req", "UI-WIN-007")] // App/AppCommands.swift:21-25; OD-0012 F1, G6
    public void The_main_window_item_is_titled_with_the_display_name_and_a_recorded_chord_fronts_the_cached_main_window() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var name = RunningAs("Zed Test");
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var commands = new AppCommands(launch.Manager.Model);
            var body = commands.Body;
            // :12-15, :21-25, :27: the window item is second, after Settings and before New thread (the divider :24 draws nothing).
            Assert.Equal("Settings\U00002026", body[0].Title);
            Assert.Equal("Zed Test", body[1].Title); // :22 AppInfo.name, the bundle's display name (Text.swift:4)
            Assert.Equal("New thread", body[2].Title);
            Assert.Null(body[1].Shortcut); // OD-0012 G6: no default chord
            Assert.False(body[1].IsDisabled); // :21-25 has no .disabled
            Assert.Empty(launch.Events);

            // Ctrl+0 is Actual Size alone (OD-0012 G6): it does not show the main window.
            Assert.True(commands.PerformKeyEquivalent(Chord("0", ChordModifiers.Control)));
            Assert.Empty(launch.Events);

            launch.Manager.ShowMain();
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            Assert.Equal("Zed Test", main.Title); // WindowManager.swift:63: the window carries the same name as the item
            main.Hide();
            var input = new TextBox();
            launch.SettingsContent.Child = input;
            launch.Manager.ShowSettings();
            var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
            try
            {
                Assert.False(main.IsVisible);
                var rebound = new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Alt); // US Y position, ED-099
                ShortcutStore.Shared.Current.Set(rebound, AppShortcut.ShowMainWindow);
                Assert.Equal("Zed Test", new AppCommands(launch.Manager.Model).Body[1].Title); // the title stays when a chord is recorded
                Assert.Equal(rebound.KeyboardShortcut, new AppCommands(launch.Manager.Model).Body[1].Shortcut);

                // The Settings window, which the production path gave the dispatch, is the one key.
                Press(settings, Key.Y, PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Alt);
                Assert.True(main.IsVisible); // :22 showMain(): shown ...
                Assert.True(main.IsActive); // ... and brought to the front (WindowManager.swift:58-59)
                Assert.Same(main, LaunchHarness.WindowOf(launch.MainContent)); // the cached instance (:27)

                // Visible but behind another window: the chord fronts it again, still the same instance.
                Deactivate(main);
                Assert.False(main.IsActive);
                Press(settings, Key.Y, PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Alt);
                Assert.True(main.IsActive);
                Assert.Single(launch.Events, value => value == "main window built");
            }
            finally { settings.Hide(); main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    // ===== PR-WIN-089: the whole command table, with no menu bar =====

    [Fact]
    [Trait("Req", "PR-WIN-089")] // App/AppCommands.swift:11-114 every command of the seven placements, no menu bar (OD-0008 C3); the palette rows, the Welcome tour row included (OD-0053, STOP #4 resolved), are CommandPaletteTests
    public void The_whole_command_table_keeps_the_source_order_chords_and_enablement_and_no_window_draws_a_menu_bar() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var name = RunningAs("Zed Test");
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var commands = new AppCommands(launch.Manager.Model);
            var ctrl = ChordModifiers.Control;
            var ctrlShift = ChordModifiers.Control | ChordModifiers.Shift;
            // Nothing selected, no running turn, no top script: the enablement rules of :44, :47, :68, :77, :83, :94, :102, :105, :108.
            (string Title, KeyboardShortcut? Shortcut, bool Disabled)[] expected =
            [
                ("Settings…", Chord(",", ctrl), false), // :13-14 CommandGroup(replacing: .appSettings)
                ("Zed Test", null, false), // :22-23 CommandGroup(before: .windowArrangement); OD-0012 G6: no default chord
                ("New thread", Chord("n", ctrl), false), // :28-29 CommandGroup(replacing: .newItem)
                ("New thread in worktree", Chord("n", ctrlShift), false), // :30-31
                ("Add project…", Chord("o", ctrl), false), // :33-34
                ("Toggle sidebar", Chord("b", ctrl), false), // :38-39 CommandGroup(after: .sidebar)
                ("Command palette", Chord("k", ctrl), false), // :40-41
                ("Toggle terminal", Chord("j", ctrl), true), // :42-44
                ("Toggle changes", Chord("d", ctrl), true), // :45-47
                ("Toggle activity view", Chord("u", ctrl | ChordModifiers.Alt), false), // :48-51
                ("Zoom In", Chord("+", ctrl), false), // :53-54
                ("Zoom Out", Chord("-", ctrl), false), // :55-56
                ("Actual Size", Chord("0", ctrl), false), // :57-58
                ("Zoom In", Chord("=", ctrl), false), // :60-61
                ("Stop", Chord(".", ctrl), true), // :66-68 CommandMenu("Thread")
                ("Queue a chat", new KeyboardShortcut(KeyEquivalent.Return, ctrl), true), // :74-77
                ("Toggle plan mode", Chord("p", ctrlShift), true), // :78-83
                ("Previous thread", new KeyboardShortcut(KeyEquivalent.Tab, ctrlShift), false), // :85-86
                ("Next thread", new KeyboardShortcut(KeyEquivalent.Tab, ctrl), false), // :87-88
                ("Run top script", Chord("r", ctrl), true), // :89-94
                .. Enumerable.Range(1, 9).Select(number => ("Thread " + number, (KeyboardShortcut?)Chord(number.ToString(System.Globalization.CultureInfo.InvariantCulture), ctrl), false)), // :95-98
                ("Settle thread", new KeyboardShortcut(KeyEquivalent.Delete, ctrlShift), true), // :100-102 finishActionTitle, Settle chosen
                ("Archive thread…", Chord("w", ctrl), true), // :103-105
                ("Settle thread", Chord("s", ctrl), true), // :106-108
                ("Welcome tour", null, false), // :111-113 CommandGroup(replacing: .help)
            ];
            Assert.Equal(expected, commands.Body.Select(command => (command.Title, command.Shortcut, command.IsDisabled)));
        });

        // OD-0008 C3: no window the app builds carries a menu bar, and a chord still runs its command in each of them.
        launch.Harness.Main.OnMain(() =>
        {
            launch.Manager.ShowMain();
            launch.Manager.ShowSettings();
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
            try
            {
                foreach (var window in new[] { main, settings })
                {
                    Assert.Null(NativeMenu.GetMenu(window));
                    Assert.Empty(window.GetVisualDescendants().OfType<Menu>());
                }
                var model = launch.Manager.Model;
                var before = model.Settings.SidebarActivityView;
                Press(main, Key.U, PhysicalKey.U, RawInputModifiers.Control | RawInputModifiers.Alt); // :48-51
                Assert.Equal(!before, model.Settings.SidebarActivityView);
                Press(settings, Key.OemMinus, PhysicalKey.Minus, RawInputModifiers.Control); // :55-56
                Assert.Equal(90, model.Settings.AppZoom);
            }
            finally { settings.Hide(); main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    // ===== ED-230: the "+" key equivalent, as Windows types it =====

    [Fact]
    [Trait("Req", "PR-WIN-222")] // App/AppCommands.swift:53-54 (Zoom In on "+")
    [Trait("Decision", "ED-230")]
    public void Ctrl_Shift_equals_and_Ctrl_keypad_plus_zoom_in_unless_a_shortcut_holds_that_chord() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        var store = FreshStore();
        using var shared = ShortcutStore.Shared.Override(store);
        AvaloniaWindow main = null!;
        launch.Harness.Main.OnMain(() => { launch.Manager.ShowMain(); main = LaunchHarness.WindowOf(launch.MainContent)!; });
        long Zoom() => launch.Harness.Main.OnMain(() => launch.Manager.Model.Settings.AppZoom);
        void PressIn(Key key, PhysicalKey physical, RawInputModifiers modifiers) =>
            launch.Harness.Main.OnMain(() => { Press(main, key, physical, modifiers); Dispatcher.UIThread.RunJobs(); });
        try
        {
            Assert.Equal(100, Zoom());
            PressIn(Key.OemPlus, PhysicalKey.Equal, RawInputModifiers.Control | RawInputModifiers.Shift); // typing "+" with Ctrl
            Assert.Equal(110, Zoom());
            PressIn(Key.Add, PhysicalKey.NumPadAdd, RawInputModifiers.Control); // the keypad's +
            Assert.Equal(125, Zoom()); // the next step after 110 (AppZoom.swift)
            PressIn(Key.Add, PhysicalKey.NumPadAdd, RawInputModifiers.None); // no Ctrl: no command holds a bare "+"
            Assert.Equal(125, Zoom()); // the next step after 110 (AppZoom.swift)
            // a shortcut set on Ctrl+Shift+= keeps the chord: the "+" reading is only a fallback
            var activity = launch.Harness.Main.OnMain(() => launch.Manager.Model.Settings.SidebarActivityView);
            launch.Harness.Main.OnMain(() => store.Set(new KeyChord(0x0D, ChordModifiers.Control | ChordModifiers.Shift), AppShortcut.ToggleActivityView));
            PressIn(Key.OemPlus, PhysicalKey.Equal, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(125, Zoom()); // the next step after 110 (AppZoom.swift)
            Assert.NotEqual(activity, launch.Harness.Main.OnMain(() => launch.Manager.Model.Settings.SidebarActivityView));
        }
        finally { launch.Harness.Main.OnMain(() => { main.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    // ===== PR-WIN-222 to PR-WIN-225: the zoom chords rescale every hosted tree =====

    [Fact]
    [Trait("Req", "PR-WIN-222")] // App/AppCommands.swift:53-54; App/Window/WindowManager.swift:173-175
    [Trait("Req", "PR-WIN-223")] // App/AppCommands.swift:55-56; App/Window/WindowManager.swift:173-175
    [Trait("Req", "PR-WIN-224")] // App/AppCommands.swift:57-58; App/Window/WindowManager.swift:173-175
    [Trait("Req", "PR-WIN-225")] // App/AppCommands.swift:59-62; App/Window/WindowManager.swift:173-175
    [Trait("Decision", "ED-099")]
    public void The_zoom_chords_pressed_in_either_window_rescale_the_hosted_tree_of_both_live() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var store = ShortcutStore.Shared.Override(FreshStore());
        AvaloniaWindow main = null!;
        AvaloniaWindow settings = null!;
        launch.Harness.Main.OnMain(() =>
        {
            launch.Manager.ShowMain();
            launch.Manager.ShowSettings();
            main = LaunchHarness.WindowOf(launch.MainContent)!;
            settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
        });
        double[] Scales() => launch.Harness.Main.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return new[] { launch.MainContent, launch.SettingsContent }
                .Select(content => Assert.IsType<ScaleTransform>(content.RenderTransform).ScaleX).ToArray();
        });
        void PressIn(AvaloniaWindow window, Key key, PhysicalKey physical) =>
            launch.Harness.Main.OnMain(() => Press(window, key, physical, RawInputModifiers.Control));
        try
        {
            Assert.Equal([1d, 1d], Scales());
            PressIn(main, Key.OemPlus, PhysicalKey.Equal); // Ctrl with the "=" key: the second Zoom In (:60-61), 100 -> 110
            Assert.Equal(110, launch.Harness.Main.OnMain(() => launch.Manager.Model.Settings.AppZoom));
            Assert.Equal([1.1, 1.1], Scales()); // WindowManager.swift:173-175 AppZoomModifier(percent:), AppZoom.swift:19-23 scale = percent / 100
            PressIn(settings, Key.OemMinus, PhysicalKey.Minus); // Zoom Out (:55-56), 110 -> 100
            PressIn(settings, Key.OemMinus, PhysicalKey.Minus); // 100 -> 90
            Assert.Equal([0.9, 0.9], Scales());
            PressIn(main, Key.D0, PhysicalKey.Digit0); // Actual Size (:57-58)
            Assert.Equal([1d, 1d], Scales());
        }
        finally { launch.Harness.Main.OnMain(() => { settings.Hide(); main.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });
}
