using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Tour;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using TourPages = global::Marrow.App.Views.Tour.Tour;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppCommands.swift (B3 52bc792), :1-115. Part 1 of 3: the settings, save, window-arrangement,
// new-item and sidebar groups (:12-51), the Thread menu's first sixteen items (:65-98) and the help group (:111-113).
// Part 2 of 3: the zoom items (:52-62), the Thread menu's last three items (:99-108), and the menu-item records of the
// UI contract for the commands of part 1 (their order, chords and enablement, with no menu drawn).
// Part 3 of 3: the menu-item records of the UI contract for the Thread menu (:65-109), the activity toggle (:48-51),
// the zoom items (:53-62) and the help group (:111-113): place, title, chord, enablement and rebinding.
// DEVIATION OD-0008 C3: the port draws no menu bar. The body is a table of the same commands, each with its action,
// its enablement rule and its chord, and the port dispatches the chords itself (Port/MenuKeyEquivalents.cs), so these
// tests press chords rather than open menus. Chords are the defaults of Core/Models/Shortcuts.swift:216-234 in Windows
// notation (ED-050 (a), ED-099: Command and Control -> Ctrl, Option -> Alt), key equivalents in SwiftUI spelling.
// Every expectation is transcribed from the cited Swift and the PR records; none comes from running the port.
// ShortcutStore.shared is replaced by a store read from the test's own empty defaults, so every chord is its default.
[Collection("Headless")]
public sealed class AppCommandsTests(HeadlessSession headless)
{
    // Shortcuts.swift:216-230 through ED-050 (a) and ED-099 (2), (6): the menu key equivalent of each default chord.
    private static readonly KeyboardShortcut CtrlN = Chord("n", ChordModifiers.Control); // newThread, 45 + command
    private static readonly KeyboardShortcut CtrlShiftN = Chord("n", ChordModifiers.Control | ChordModifiers.Shift); // 45 + shift, command
    private static readonly KeyboardShortcut CtrlO = Chord("o", ChordModifiers.Control); // addProject, 31
    private static readonly KeyboardShortcut CtrlPeriod = Chord(".", ChordModifiers.Control); // stopTurn, 47
    private static readonly KeyboardShortcut CtrlReturn = new(KeyEquivalent.Return, ChordModifiers.Control); // queueChat, 36
    private static readonly KeyboardShortcut CtrlShiftP = Chord("p", ChordModifiers.Control | ChordModifiers.Shift); // togglePlanMode, 35
    private static readonly KeyboardShortcut CtrlShiftTab = new(KeyEquivalent.Tab, ChordModifiers.Control | ChordModifiers.Shift); // previousThread, 48
    private static readonly KeyboardShortcut CtrlTab = new(KeyEquivalent.Tab, ChordModifiers.Control); // nextThread, 48 + control
    private static readonly KeyboardShortcut CtrlR = Chord("r", ChordModifiers.Control); // runScript, 15
    private static readonly KeyboardShortcut CtrlK = Chord("k", ChordModifiers.Control); // commandPalette, 40
    private static readonly KeyboardShortcut CtrlB = Chord("b", ChordModifiers.Control); // toggleSidebar, 11
    private static readonly KeyboardShortcut CtrlJ = Chord("j", ChordModifiers.Control); // toggleTerminal, 38
    private static readonly KeyboardShortcut CtrlD = Chord("d", ChordModifiers.Control); // toggleChanges, 2
    private static readonly KeyboardShortcut CtrlAltU = Chord("u", ChordModifiers.Control | ChordModifiers.Alt); // toggleActivityView, 32 + option

    private static KeyboardShortcut Chord(string key, ChordModifiers modifiers) => new(new KeyEquivalent(key), modifiers);

    // AppCommands.swift:97 KeyEquivalent(Character(String(number))) with .command.
    private static KeyboardShortcut CtrlDigit(int number) => Chord(number.ToString(System.Globalization.CultureInfo.InvariantCulture), ChordModifiers.Control);

    // Precedent: Views/Settings/ShortcutsSettingsPageTests.cs:28 - ShortcutStore's constructor is private. It reads the
    // defaults of the calling flow once (Shortcuts.swift:267-274); the harness's are empty, so every chord is the default.
    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;

    private static MenuCommand Named(AppCommands commands, string title) => commands.Body.Single(command => command.Title == title);

    [Fact]
    [Trait("Req", "PR-WIN-090")] // App/AppCommands.swift:12-15
    [Trait("Req", "UI-LAUNCH-007")] // App/AppCommands.swift:12-15
    [Trait("Req", "UI-WIN-006")] // App/AppCommands.swift:12-15
    [Trait("Decision", "OD-0151")] // Settings opens as the panel inside the main window; the Settings command toggles it
    public void Settings_is_first_never_disabled_and_its_default_and_rebound_chords_open_the_cached_Settings_panel_in_the_main_window() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var commands = new AppCommands(launch.Manager.Model);
            var settings = commands.Body[0];
            Assert.Equal("Settings\U00002026", settings.Title); // :13
            Assert.Equal(Chord(",", ChordModifiers.Control), settings.Shortcut); // :14; Shortcuts.swift:234, ED-050
            Assert.False(settings.IsDisabled); // :12-15 has no .disabled
            Assert.False(launch.Manager.IsSettingsOpen);
            Assert.True(commands.PerformKeyEquivalent(Chord(",", ChordModifiers.Control)));
            var window = LaunchHarness.WindowOf(launch.SettingsContent);
            Assert.NotNull(window);
            Assert.Same(launch.Manager.MainWindow, window); // OD-0151: the Settings content sits in the main window
            var overlay = Assert.IsType<SettingsOverlay>(launch.Manager.SettingsOverlay);
            Assert.Same(window, overlay.Window);
            Assert.True(launch.Manager.IsSettingsOpen);
            Assert.True(overlay.IsVisible);
            Assert.True(window.IsVisible);
            launch.Manager.CloseSettings();
            window.Hide();
            Assert.False(launch.Manager.IsSettingsOpen);

            var rebound = new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Alt); // Windows US Y position, ED-050
            ShortcutStore.Shared.Current.Set(rebound, AppShortcut.OpenSettings);
            Assert.False(commands.PerformKeyEquivalent(Chord(",", ChordModifiers.Control)));
            Assert.False(launch.Manager.IsSettingsOpen);
            Assert.True(commands.PerformKeyEquivalent(rebound.KeyboardShortcut!.Value));
            Assert.Same(window, LaunchHarness.WindowOf(launch.SettingsContent));
            Assert.Same(overlay, launch.Manager.SettingsOverlay); // the cached panel (:34-35)
            Assert.True(launch.Manager.IsSettingsOpen);
            Assert.True(window.IsVisible);
            Assert.Single(launch.Events, value => value == "settings window built");
            // OD-0151: the Settings command closes the open panel, and the main window stays.
            Assert.True(commands.PerformKeyEquivalent(rebound.KeyboardShortcut!.Value));
            Assert.False(launch.Manager.IsSettingsOpen);
            Assert.True(window.IsVisible);
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-092")] // App/AppCommands.swift:21-25
    [Trait("Req", "UI-WIN-007")] // App/AppCommands.swift:21-25; OD-0012 F1,G6
    public void The_app_name_command_follows_Settings_has_no_default_chord_and_a_recorded_chord_shows_the_cached_main_window() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var commands = new AppCommands(launch.Manager.Model);
            var main = commands.Body[1];
            Assert.Equal(AppInfo.Current.Name, main.Title); // :22 AppInfo.name, OD-0012 F1
            Assert.Null(main.Shortcut); // OD-0012 G6
            Assert.False(main.IsDisabled); // :21-25 has no .disabled
            Assert.Equal("New thread", commands.Body[2].Title); // :28 follows the app-name group
            commands.PerformKeyEquivalent(Chord("0", ChordModifiers.Control)); // :57-58 Actual Size, not showMain
            Assert.Empty(launch.Events);

            var rebound = new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Alt);
            ShortcutStore.Shared.Current.Set(rebound, AppShortcut.ShowMainWindow);
            Assert.True(commands.PerformKeyEquivalent(rebound.KeyboardShortcut!.Value));
            var window = LaunchHarness.WindowOf(launch.MainContent);
            Assert.NotNull(window);
            Assert.True(window.IsVisible);
            window.Hide();
            Assert.True(commands.PerformKeyEquivalent(rebound.KeyboardShortcut!.Value));
            Assert.Same(window, LaunchHarness.WindowOf(launch.MainContent));
            Assert.True(window.IsVisible);
            Assert.Single(launch.Events, value => value == "main window built");
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-090")] // App/Views/Palette/CommandPalette.swift:176-178,201-206
    [Trait("Decision", "OD-0151")] // Settings opens as the panel inside the main window
    public void The_palettes_Settings_row_opens_the_Settings_panel_in_the_main_window() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var store = ShortcutStore.Shared.Override(FreshStore());
        CommandPalette palette = null!;
        AvaloniaWindow main = null!;
        launch.Harness.Main.OnMain(() =>
        {
            palette = new CommandPalette();
            launch.MainContent.Child = palette;
            launch.Manager.Model.IsCommandPalettePresented = true;
            launch.Manager.ShowMain();
            main = LaunchHarness.WindowOf(launch.MainContent)!;
            palette.QueryField.Text = "Settings";
        });
        launch.Harness.Main.OnMain(() =>
        {
            try
            {
                Assert.Equal(1, palette.ResultCount);
                Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(palette.RowsPanel), block => block.Text == "Settings");
                Assert.False(launch.Manager.IsSettingsOpen);
                palette.QueryField.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                var settings = LaunchHarness.WindowOf(launch.SettingsContent);
                Assert.NotNull(settings);
                Assert.Same(main, settings); // OD-0151: the panel opens in the main window, no second window
                Assert.Same(main, launch.Manager.SettingsOverlay!.Window);
                Assert.True(launch.Manager.IsSettingsOpen);
                Assert.True(settings.IsVisible);
                Assert.False(launch.Manager.Model.IsCommandPalettePresented); // :204 close before performing
                launch.Manager.CloseSettings();
            }
            finally { main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-092")] // App/AppCommands.swift:21-25; App/Window/WindowManager.swift:26-31
    [Trait("Decision", "OD-0151")] // the Settings content is a panel inside the main window
    public void A_recorded_show_main_chord_dispatched_from_the_focused_Settings_panel_reuses_main() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            launch.Manager.ShowMain();
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            main.Hide();
            var input = new TextBox();
            launch.SettingsContent.Child = input;
            launch.Manager.ShowSettings();
            var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
            try
            {
                // OD-0151: Settings asked for while the main window is hidden brings that same window back with the panel in it.
                Assert.Same(main, settings);
                Assert.Same(main, launch.Manager.SettingsOverlay!.Window);
                Assert.True(launch.Manager.IsSettingsOpen);
                Assert.True(main.IsVisible);
                Assert.True(input.Focus());
                Assert.True(input.IsFocused);
                var rebound = new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Alt);
                ShortcutStore.Shared.Current.Set(rebound, AppShortcut.ShowMainWindow);
                var commands = new AppCommands(launch.Manager.Model);
                // Test-owned attachment on top of the one WindowManager.MakeWindow installs (STOP #6, resolved); the
                // later registration runs first in the tunnel, and either one runs the same command.
                using var attached = MenuKeyEquivalents.Attach(settings, commands.PerformKeyEquivalent);
                Press(settings, Key.Y, PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Alt);
                Assert.Same(main, LaunchHarness.WindowOf(launch.MainContent));
                Assert.True(main.IsVisible);
                Assert.Single(launch.Events, value => value == "main window built");
                Assert.True(launch.Manager.IsSettingsOpen); // showing main leaves the panel as it was
            }
            finally { launch.Manager.CloseSettings(); main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-1")] // baseline evidence only; deliberately no Req trait
    // UNRESOLVED (STOP App.AppCommands #8; PR-WIN-224/226 depends_on_unknown UNK-0003): documents current
    // table precedence, not an approved parity rule.
    public void Recorded_show_main_overlaps_currently_win_over_fixed_zoom_and_thread_chords() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var model = launch.Manager.Model;
            var commands = new AppCommands(model);
            var overlaps = new[] { new KeyChord(0x0B, ChordModifiers.Control), new KeyChord(0x0C, ChordModifiers.Control), new KeyChord(0x0D, ChordModifiers.Control) }
                .Concat(Enumerable.Range(0x02, 9).Select(code => new KeyChord(code, ChordModifiers.Control)));
            foreach (var chord in overlaps)
            {
                ShortcutStore.Shared.Current.Set(chord, AppShortcut.ShowMainWindow);
                var matching = commands.Body.Where(command => command.Shortcut == chord.KeyboardShortcut).ToArray();
                Assert.Equal(2, matching.Length); // app-name plus fixed zoom or Thread N
                Assert.Equal(AppInfo.Current.Name, matching[0].Title);
                model.Settings.AppZoom = 150;
                var selection = model.SelectedThreadID;
                Assert.True(commands.PerformKeyEquivalent(chord.KeyboardShortcut!.Value));
                var main = LaunchHarness.WindowOf(launch.MainContent)!;
                Assert.True(main.IsVisible);
                Assert.Equal(150, model.Settings.AppZoom);
                Assert.Equal(selection, model.SelectedThreadID);
                main.Hide();
            }
            Dispatcher.UIThread.RunJobs();
        });
    });

    private static AppModel Model(AppModelHarness h, IEnumerable<Project> projects, IEnumerable<ChatThread> threads)
    {
        h.WriteLibrary(projects, threads);
        foreach (var thread in threads) AppModelHarness.WriteDocument(thread.Id);
        return h.MakeModel();
    }

    // ---- headless helpers (ThreadWindowTests.cs precedent) -------------------------------------------------------

    private static T Show<T>(T window) where T : AvaloniaWindow
    {
        window.Width = 400;
        window.Height = 300;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // A tunnel probe on the window, registered before the command set is attached. Avalonia runs a window's tunnelling
    // instance handlers latest-registered first (after the window class's own handlers), so the probe runs after the
    // command set and sees a key only when nothing before it consumed the event.
    private static List<Key> Passed(AvaloniaWindow window)
    {
        var passed = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => passed.Add(e.Key), RoutingStrategies.Tunnel);
        return passed;
    }

    private static void Press(AvaloniaWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physical, null);
        if (window.IsVisible) window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    // ===== PR-WIN-089, PR-WIN-095, PR-WIN-102, PR-WIN-103: the command set as a table =====

    [Fact]
    // Partial evidence for PR-WIN-089 (the whole record: AppCommandsFinishTests). // App/AppCommands.swift:11-114
    [Trait("Req", "PR-WIN-095")] // App/AppCommands.swift:27-35
    [Trait("Req", "PR-WIN-102")] // App/AppCommands.swift:37-52
    [Trait("Req", "PR-WIN-103")] // App/AppCommands.swift:65-98
    public void The_commands_keep_the_source_order_and_each_its_own_chord_with_no_divider_entries()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // :28, :30, :33, :38, :40, :42, :45, :48, :66, :74, :78, :85, :87, :89, :95-98, :112. U+2026 HORIZONTAL ELLIPSIS.
            (string Title, KeyboardShortcut? Shortcut)[] expected =
            [
                ("New thread", CtrlN), ("New thread in worktree", CtrlShiftN), ("Add project\U00002026", CtrlO),
                ("Toggle sidebar", CtrlB), ("Command palette", CtrlK), ("Toggle terminal", CtrlJ), ("Toggle changes", CtrlD),
                ("Toggle activity view", CtrlAltU),
                ("Stop", CtrlPeriod), ("Queue a chat", CtrlReturn), ("Toggle plan mode", CtrlShiftP),
                ("Previous thread", CtrlShiftTab), ("Next thread", CtrlTab), ("Run top script", CtrlR),
                ("Thread 1", CtrlDigit(1)), ("Thread 2", CtrlDigit(2)), ("Thread 3", CtrlDigit(3)), ("Thread 4", CtrlDigit(4)),
                ("Thread 5", CtrlDigit(5)), ("Thread 6", CtrlDigit(6)), ("Thread 7", CtrlDigit(7)), ("Thread 8", CtrlDigit(8)),
                ("Thread 9", CtrlDigit(9)),
                ("Welcome tour", null),
            ];
            var titles = expected.Select(item => item.Title).ToHashSet(StringComparer.Ordinal);
            var body = commands.Body;
            Assert.Equal(expected, body.Where(command => titles.Contains(command.Title)).Select(command => (command.Title, command.Shortcut)));
            // The dividers (:24, :32, :52, :84, :99) are drawn by no menu, so nothing stands between these neighbours.
            var order = body.Select(command => command.Title).ToList();
            Assert.Equal(order.IndexOf("New thread in worktree") + 1, order.IndexOf("Add project\U00002026"));
            Assert.Equal(order.IndexOf("Toggle plan mode") + 1, order.IndexOf("Previous thread"));
            Assert.Equal(order.IndexOf("Run top script") + 1, order.IndexOf("Thread 1"));
        });
    }

    [Fact]
    // Partial evidence for PR-WIN-089 (the whole record: AppCommandsFinishTests). // App/AppCommands.swift:11-114
    [Trait("Decision", "ED-099")]
    public void Attached_to_a_window_a_chord_works_with_no_menu_present_and_is_consumed() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        h.WriteLibrary([], []);
        var model = new AppModel();
        var commands = new AppCommands(model);
        var window = Show(new AvaloniaWindow());
        var passed = Passed(window);
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        Press(window, Key.K, PhysicalKey.K, RawInputModifiers.Control); // :40-41

        Assert.True(model.IsCommandPalettePresented);
        Assert.Empty(passed);
        Assert.Null(NativeMenu.GetMenu(window));
        window.Close();
    });

    // ===== PR-WIN-091: no stock Close =====

    [Fact]
    [Trait("Req", "PR-WIN-091")] // App/AppCommands.swift:17-19; App/Window/ThreadWindow.swift:58-64
    [Trait("Decision", "ED-099")]
    public void A_secondary_window_closes_itself_on_ctrl_w_and_no_command_but_archive_claims_that_chord() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        h.WriteLibrary([], []);
        var model = new AppModel();
        var commands = new AppCommands(model);
        var ctrlW = Chord("w", ChordModifiers.Control);
        // :103-104 "Archive thread" plus U+2026 is the only item the source gives the Command-W chord. U+2026 HORIZONTAL ELLIPSIS.
        Assert.All(commands.Body.Where(command => command.Shortcut == ctrlW), command => Assert.Equal("Archive thread\U00002026", command.Title));

        var window = Show(new SecondaryWindow());
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);

        Assert.False(window.IsVisible);
    });

    // ===== PR-WIN-093, PR-WIN-094: new threads =====

    [Fact]
    [Trait("Req", "PR-WIN-093")] // App/AppCommands.swift:28-29; App/AppModel.swift:614-630
    public void New_thread_chord_starts_a_thread_in_the_current_project_and_selects_it()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var existing = AppModelSamples.Thread(project);
        var model = Model(h, [project], [existing]);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = existing.Id;
            var commands = new AppCommands(model);

            Assert.False(Named(commands, "New thread").IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlN));

            Assert.Equal(2, model.Threads.Count);
            var created = model.Threads.Single(thread => thread.Id != existing.Id);
            Assert.Equal(created.Id, model.SelectedThreadID);
            Assert.Equal(project.Id, created.ProjectID);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-093")] // App/AppCommands.swift:28-29 (no .disabled modifier)
    public void New_thread_is_never_disabled_even_with_no_project()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);

            Assert.False(Named(commands, "New thread").IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlN));
            Assert.Empty(model.Threads);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-094")] // App/AppCommands.swift:30-31; App/AppModel.swift:625-626, :1125-1131
    public void New_thread_in_worktree_chord_asks_for_a_worktree_whatever_the_default_workspace()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("plain"));
        var existing = AppModelSamples.Thread(project);
        var model = Model(h, [project], [existing]);

        h.Main.OnMain(() =>
        {
            model.Settings.DefaultWorkspaceMode = WorkspaceMode.Local;
            model.SelectedThreadID = existing.Id;
            var commands = new AppCommands(model);

            Assert.False(Named(commands, "New thread in worktree").IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlShiftN));
            Assert.Equal(2, model.Threads.Count);
            Assert.NotEqual(existing.Id, model.SelectedThreadID);
        });
        // Not a git repository: the worktree path fails later with its own alert (AppModel.swift:1128-1131).
        h.Main.Loop.RunUntil(() => model.Alert is not null, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.Equal("Worktree unavailable", model.Alert!.Title);
            Assert.Equal("New worktrees need a git repository with at least one commit.", model.Alert!.Message);
        });
    }

    // ===== PR-WIN-096: Add project (with U+2026) =====

    [Fact]
    [Trait("Req", "PR-WIN-096")] // App/AppCommands.swift:33-34
    public void Add_project_is_never_disabled_and_takes_its_chord()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var add = Named(commands, "Add project\U00002026"); // U+2026 HORIZONTAL ELLIPSIS

            Assert.False(add.IsDisabled);
            Assert.Equal(CtrlO, add.Shortcut);
            Assert.True(commands.PerformKeyEquivalent(CtrlO));
        });
    }

    // ===== PR-WIN-097, PR-WIN-098: sidebar and palette =====

    [Fact]
    [Trait("Req", "PR-WIN-097")] // App/AppCommands.swift:38-39; UI/Chrome/WindowChrome.swift:358-370
    public void Toggle_sidebar_chord_opens_and_closes_the_sidebar_rewriting_both_sidebar_keys_each_time()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var before = model.Sidebar.IsVisible;
            Assert.False(Named(commands, "Toggle sidebar").IsDisabled);

            h.Defaults.Writes.Clear();
            Assert.True(commands.PerformKeyEquivalent(CtrlB));
            Assert.Equal(!before, model.Sidebar.IsVisible);
            Assert.Contains("sidebarWidth", h.Defaults.Writes);
            Assert.Contains("sidebarStartsOpen", h.Defaults.Writes);

            h.Defaults.Writes.Clear();
            Assert.True(commands.PerformKeyEquivalent(CtrlB));
            Assert.Equal(before, model.Sidebar.IsVisible);
            Assert.Contains("sidebarWidth", h.Defaults.Writes);
            Assert.Contains("sidebarStartsOpen", h.Defaults.Writes);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-098")] // App/AppCommands.swift:40-41
    public void Command_palette_chord_toggles_the_palette_rather_than_only_opening_it()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            Assert.False(Named(commands, "Command palette").IsDisabled);

            Assert.True(commands.PerformKeyEquivalent(CtrlK));
            Assert.True(model.IsCommandPalettePresented);
            Assert.True(commands.PerformKeyEquivalent(CtrlK));
            Assert.False(model.IsCommandPalettePresented);
        });
    }

    // ===== PR-WIN-099, PR-TERM-070: Toggle terminal =====

    [Fact]
    [Trait("Req", "PR-WIN-099")] // App/AppCommands.swift:7-9, :42-44
    [Trait("Req", "PR-TERM-070")] // App/AppCommands.swift:42-44
    public void Toggle_terminal_chord_flips_the_selected_threads_terminal_creating_its_runtime_on_first_use()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = thread.Id;
            Assert.Null(model.ExistingRuntime(thread.Id));
            var commands = new AppCommands(model);

            Assert.True(commands.PerformKeyEquivalent(CtrlJ));
            Assert.True(model.ExistingRuntime(thread.Id)!.IsTerminalVisible);
            Assert.True(commands.PerformKeyEquivalent(CtrlJ));
            Assert.False(model.Runtime(thread.Id).IsTerminalVisible);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-099")] // App/AppCommands.swift:44
    [Trait("Req", "PR-TERM-070")] // App/AppCommands.swift:44
    public void Toggle_terminal_is_disabled_with_no_selection_and_leaves_the_chord_unclaimed()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);

            Assert.True(Named(commands, "Toggle terminal").IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlJ));
            Assert.Null(model.ExistingRuntime(thread.Id));
        });
    }

    // ===== PR-WIN-100: Toggle changes =====

    [Fact]
    [Trait("Req", "PR-WIN-100")] // App/AppCommands.swift:45-47; App/Runtime/ThreadRuntime.swift:403-407
    public void Toggle_changes_chord_flips_the_changes_panel_and_is_disabled_with_no_selection()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            Assert.True(Named(commands, "Toggle changes").IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlD));

            model.SelectedThreadID = thread.Id;
            Assert.False(Named(commands, "Toggle changes").IsDisabled);
            // Web surfaces W4 (ED-NEW-web-w4-1, DESIGN 3.7 "Changes | Ctrl+D (existing ToggleChanges, now the pane)"): the chord opens and
            // closes the Changes pane; the popover (IsDiffVisible) is the Alt+click on the toolbar button's, and stays shut.
            Assert.True(commands.PerformKeyEquivalent(CtrlD));
            Assert.True(model.Runtime(thread.Id).IsChangesPaneOpen);
            Assert.True(model.Runtime(thread.Id).Workspace.IsOpen(PaneKind.Changes));
            Assert.False(model.Runtime(thread.Id).IsDiffVisible);
            Assert.True(commands.PerformKeyEquivalent(CtrlD));
            Assert.False(model.Runtime(thread.Id).IsChangesPaneOpen);
            Assert.False(model.Runtime(thread.Id).IsDiffVisible);
        });
    }

    // ===== PR-WIN-101: Toggle activity view =====

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-WIN-101")] // App/AppCommands.swift:48-51; UI/Chrome/Chrome.swift:100-104
    public void Toggle_activity_view_chord_flips_the_persisted_setting_inside_the_panel_slide_animation(bool reduceMotion)
    {
        using var h = new AppModelHarness();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);
        // Chrome.swift:101-103: a 0.18-second ease-out with Reduce Motion, otherwise the spring 0.32 / 0.9.
        var panelSlide = reduceMotion ? Animation.EaseOut(duration: 0.18) : Animation.Spring(response: 0.32, dampingFraction: 0.9);
        var applied = new List<(bool Value, Animation? Animation)>();
        var commands = h.Main.OnMain(() => new AppCommands(model));
        using var tracked = h.Main.OnMain(() => new Tracked<bool>(() => model.Settings.SidebarActivityView, value => applied.Add((value, Transaction.Current))));

        h.Main.OnMain(() =>
        {
            Assert.False(Named(commands, "Toggle activity view").IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlAltU));
        });

        Assert.Equal(new (bool Value, Animation? Animation)[] { (false, null), (true, panelSlide) }, applied);
        Assert.True(h.Defaults.Object("sidebarActivityView") is true);
    }

    // ===== PR-WIN-104: Stop =====

    [Fact]
    [Trait("Req", "PR-WIN-104")] // App/AppCommands.swift:66-68
    public void Stop_chord_interrupts_the_selected_threads_running_turn()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = thread.Id;
            runtime.RehearseTurn("working");
            Assert.True(runtime.IsRunning);
            var commands = new AppCommands(model);

            Assert.False(Named(commands, "Stop").IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlPeriod));
        });

        // ThreadRuntime.interrupt() with no provider session finishes the turn as interrupted; the finish awaits work
        // off the main actor (the turn's diff), so the loop is pumped until the runtime reports idle.
        h.Main.Loop.RunUntil(() => !runtime.IsRunning, AppModelHarness.Bound);
        Assert.Equal(TurnStatus.Interrupted, h.Main.OnMain(() => runtime.Turns[^1].Status));
    }

    [Fact]
    [Trait("Req", "PR-WIN-104")] // App/AppCommands.swift:68
    public void Stop_is_disabled_with_no_selection_or_no_running_turn_and_leaves_the_chord_unclaimed()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            Assert.True(Named(commands, "Stop").IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlPeriod));

            model.SelectedThreadID = thread.Id;
            Assert.False(model.Runtime(thread.Id).IsRunning);
            Assert.True(Named(commands, "Stop").IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlPeriod));
        });
    }

    // ===== PR-WIN-105: Queue a chat =====

    [Fact]
    // Partial evidence for PR-WIN-105; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:74-77
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void Queue_a_chat_chord_lines_the_draft_up_behind_the_running_turn()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        // A stored follow-up, not a Hydra head dispatched from the queue (ThreadRuntime.swift:999-1013).
        h.Main.OnMain(() =>
        {
            model.Settings.HydraQueueHeads = false;
            model.Settings.HydraAlwaysHeads = false;
        });
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = thread.Id;
            runtime.RehearseTurn("working");
            runtime.Draft = new ComposerDraft { Text = "next step" };
            var commands = new AppCommands(model);

            Assert.False(Named(commands, "Queue a chat").IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlReturn));

            Assert.Equal("next step", Assert.Single(runtime.FollowUps).Text);
            Assert.True(runtime.DraftIsEmpty);
        });
    }

    [Fact]
    // Partial evidence for PR-WIN-105; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:69-77
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void Queue_a_chat_is_disabled_while_an_approval_waits_so_the_chord_is_left_for_the_card()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = thread.Id;
            runtime.RehearseTurn("working");
            runtime.Draft = new ComposerDraft { Text = "next step" };
            runtime.Rehearse(new ProviderEvent.Approval(new ApprovalRequest("approval", ApprovalRequest.Kind.Command, "Command", ValueList<ApprovalRequest.Option>.Empty)));
            var commands = new AppCommands(model);

            Assert.True(Named(commands, "Queue a chat").IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlReturn));
            Assert.Empty(runtime.FollowUps);
            Assert.Equal("next step", runtime.Draft.Text);
        });
    }

    [Fact]
    // Partial evidence for PR-WIN-105; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:76-77
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void Queue_a_chat_is_disabled_with_an_empty_draft_or_no_selection()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            runtime.Draft = new ComposerDraft { Text = "next step" };
            Assert.True(Named(commands, "Queue a chat").IsDisabled); // no selection: runtime?.draftIsEmpty ?? true
            Assert.False(commands.PerformKeyEquivalent(CtrlReturn));

            model.SelectedThreadID = thread.Id;
            runtime.Draft = new ComposerDraft { Text = "   " };
            Assert.True(Named(commands, "Queue a chat").IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlReturn));
        });
    }

    [Fact]
    // Partial evidence for PR-WIN-105; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:69-77
    [Trait("Decision", "ED-099")]
    public void In_a_window_a_disabled_queue_leaves_ctrl_enter_on_its_way_to_the_rest_of_the_window() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = new AppModel();
        var runtime = model.Runtime(thread.Id);
        var loaded = runtime.EnsureLoaded();
        Assert.True(UiPump.Until(() => loaded.IsCompleted, AppModelHarness.Bound));
        model.SelectedThreadID = thread.Id;
        runtime.RehearseTurn("working");
        runtime.Draft = new ComposerDraft { Text = "next step" };
        runtime.Rehearse(new ProviderEvent.Approval(new ApprovalRequest("approval", ApprovalRequest.Kind.Command, "Command", ValueList<ApprovalRequest.Option>.Empty)));
        var commands = new AppCommands(model);
        var window = Show(new AvaloniaWindow());
        var passed = Passed(window);
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        Press(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);

        Assert.Equal([Key.Enter], passed);
        Assert.Equal("next step", runtime.Draft.Text);
        window.Close();
    });

    // The case STOP #5 left unwritten: the chord on an idle thread with no heads at work. canQueue is
    // `isRunning || hasWorkingHeads` (ThreadRuntime.swift:771-773), so queueDraftAsFollowUp sends the draft
    // (:782-785), and send() handles "/plan" locally (:746-749, :1159-1162).
    [Fact]
    [Trait("Req", "PR-WIN-105")] // App/AppCommands.swift:69-77; App/Runtime/ThreadRuntime.swift:743-749, :771-785, :1157-1162
    [Trait("Req", "UI-WIN-018")] // App/AppCommands.swift:71-74; App/Runtime/ThreadRuntime.swift:778-785
    public void Queue_a_chat_with_nothing_to_queue_behind_sends_the_draft_so_a_plan_command_flips_the_thread_to_plan()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        h.PrepareBootstrap(model); // R-TEST-10: no real CLI, should anything start a turn
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = thread.Id;
            runtime.Draft = new ComposerDraft { Text = "/plan" };
            var commands = new AppCommands(model);
            Assert.False(runtime.IsRunning);
            Assert.False(runtime.HasWorkingHeads);
            Assert.Equal(InteractionMode.Build, model.Thread(thread.Id)!.InteractionMode);
            Assert.False(Named(commands, "Queue a chat").IsDisabled); // :76-77

            Assert.True(commands.PerformKeyEquivalent(CtrlReturn));

            Assert.Equal(InteractionMode.Plan, model.Thread(thread.Id)!.InteractionMode); // ThreadRuntime.swift:1161
            Assert.True(runtime.DraftIsEmpty); // ThreadRuntime.swift:748
            Assert.Empty(runtime.FollowUps);
            Assert.Empty(runtime.SentPrompts); // a local command starts no turn
        });
    }

    // ===== PR-WIN-106: Toggle plan mode =====

    [Fact]
    [Trait("Req", "PR-WIN-106")] // App/AppCommands.swift:78-83
    public void Toggle_plan_mode_chord_flips_build_to_plan_and_plan_to_build_through_the_thread_store()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = thread.Id;
            var commands = new AppCommands(model);
            Assert.Equal(InteractionMode.Build, model.Thread(thread.Id)!.InteractionMode);
            Assert.False(Named(commands, "Toggle plan mode").IsDisabled);

            Assert.True(commands.PerformKeyEquivalent(CtrlShiftP));
            Assert.Equal(InteractionMode.Plan, model.Thread(thread.Id)!.InteractionMode);
            Assert.True(commands.PerformKeyEquivalent(CtrlShiftP));
            Assert.Equal(InteractionMode.Build, model.Thread(thread.Id)!.InteractionMode);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-106")] // App/AppCommands.swift:79, :83
    public void Toggle_plan_mode_is_disabled_with_no_selection_and_leaves_the_chord_unclaimed()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            Assert.True(Named(commands, "Toggle plan mode").IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlShiftP));
            Assert.Equal(InteractionMode.Build, model.Thread(thread.Id)!.InteractionMode);
        });
    }

    // ===== PR-WIN-107, PR-WIN-108: previous and next thread =====

    [Fact]
    [Trait("Req", "PR-WIN-107")] // App/AppCommands.swift:85-86; App/AppModel.swift:1109-1117
    [Trait("Req", "PR-WIN-108")] // App/AppCommands.swift:87-88; App/AppModel.swift:1109-1117
    public void Next_and_previous_thread_chords_step_through_the_sidebar_order_and_wrap_around()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var model = Model(h, [project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);

        h.Main.OnMain(() =>
        {
            var order = model.SidebarThreads.Select(thread => thread.Id).ToList();
            Assert.Equal(3, order.Count);
            var commands = new AppCommands(model);
            Assert.False(Named(commands, "Previous thread").IsDisabled);
            Assert.False(Named(commands, "Next thread").IsDisabled);

            model.SelectedThreadID = order[1];
            Assert.True(commands.PerformKeyEquivalent(CtrlTab));
            Assert.Equal(order[2], model.SelectedThreadID);
            Assert.True(commands.PerformKeyEquivalent(CtrlTab));
            Assert.Equal(order[0], model.SelectedThreadID);
            Assert.True(commands.PerformKeyEquivalent(CtrlShiftTab));
            Assert.Equal(order[2], model.SelectedThreadID);
            Assert.True(commands.PerformKeyEquivalent(CtrlShiftTab));
            Assert.Equal(order[1], model.SelectedThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-107")] // App/AppCommands.swift:85-86; App/AppModel.swift:1112-1114
    [Trait("Req", "PR-WIN-108")] // App/AppCommands.swift:87-88; App/AppModel.swift:1112-1114
    public void With_nothing_selected_both_directions_select_the_first_sidebar_thread()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var model = Model(h, [project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);

        h.Main.OnMain(() =>
        {
            var first = model.SidebarThreads[0].Id;
            var commands = new AppCommands(model);
            Assert.True(commands.PerformKeyEquivalent(CtrlShiftTab));
            Assert.Equal(first, model.SelectedThreadID);
            model.SelectedThreadID = null;
            Assert.True(commands.PerformKeyEquivalent(CtrlTab));
            Assert.Equal(first, model.SelectedThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-107")] // App/AppCommands.swift:85-86 (no .disabled); App/AppModel.swift:1111
    [Trait("Req", "PR-WIN-108")] // App/AppCommands.swift:87-88 (no .disabled); App/AppModel.swift:1111
    public void With_no_threads_both_directions_stay_enabled_take_the_chord_and_select_nothing()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            Assert.False(Named(commands, "Previous thread").IsDisabled);
            Assert.False(Named(commands, "Next thread").IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlTab));
            Assert.True(commands.PerformKeyEquivalent(CtrlShiftTab));
            Assert.Null(model.SelectedThreadID);
        });
    }

    // ===== PR-WIN-109: the run-script item =====

    [Fact]
    [Trait("Req", "PR-WIN-109")] // App/AppCommands.swift:89, :94; App/AppModel.swift:1098-1101
    public void Without_a_top_script_the_item_reads_Run_top_script_and_is_disabled()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var scripted = AppModelSamples.Project("Scripted", h.NewFolder("scripted")) with
        {
            Scripts = new ValueList<ProjectScript>([new ProjectScript("build", "Write-Output built")]),
        };
        var plain = AppModelSamples.Project("Plain", h.NewFolder("plain"));
        var scriptedThread = AppModelSamples.Thread(scripted);
        var plainThread = AppModelSamples.Thread(plain);
        var model = Model(h, [scripted, plain], [scriptedThread, plainThread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // No selection: topScript is nil (AppModel.swift:1099).
            var item = Assert.Single(commands.Body, command => command.Shortcut == CtrlR);
            Assert.Equal("Run top script", item.Title);
            Assert.True(item.IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlR));

            model.SelectedThreadID = plainThread.Id;
            item = Assert.Single(commands.Body, command => command.Shortcut == CtrlR);
            Assert.Equal("Run top script", item.Title);
            Assert.True(item.IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlR));
            Assert.Empty(model.Terminals.Sessions(plainThread.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-109")] // App/AppCommands.swift:89, :94
    public void With_a_top_script_the_item_reads_Run_and_the_scripts_own_name_and_is_enabled()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("Scripted", h.NewFolder("scripted")) with
        {
            Scripts = new ValueList<ProjectScript>([
                new ProjectScript("Build all", "Write-Output built"),
                new ProjectScript("test", "Write-Output tested"),
            ]),
        };
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = thread.Id;
            var commands = new AppCommands(model);
            var item = Assert.Single(commands.Body, command => command.Shortcut == CtrlR);
            Assert.Equal("Run Build all", item.Title);
            Assert.False(item.IsDisabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-109")] // App/AppCommands.swift:89-94; App/AppModel.swift:1103-1107
    public void Run_script_chord_runs_the_top_script_in_the_selected_threads_terminal() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var folder = h.NewFolder("run script");
        var script = new ProjectScript("build", "Set-Content -Path 'ran.txt' -Value 'ran' -Encoding utf8");
        var project = AppModelSamples.Project("Scripted", folder) with { Scripts = new ValueList<ProjectScript>([script]) };
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = new AppModel();
        try
        {
            model.SelectedThreadID = thread.Id;
            var commands = new AppCommands(model);

            Assert.True(commands.PerformKeyEquivalent(CtrlR));

            var session = Assert.Single(model.Terminals.Sessions(thread.Id));
            Assert.Equal("build", session.Title);
            Assert.Equal(folder, session.Directory);
            Assert.True(model.Runtime(thread.Id).IsTerminalVisible);
            Assert.True(UiPump.Until(() => File.Exists(Path.Combine(folder, "ran.txt")), AppModelHarness.Bound));
        }
        finally
        {
            model.Terminals.TerminateAll();
        }
    });

    // ===== PR-WIN-110: Thread 1 to Thread 9 =====

    [Fact]
    [Trait("Req", "PR-WIN-110")] // App/AppCommands.swift:95-98; App/AppModel.swift:1119-1123
    public void Ctrl_and_a_digit_select_that_sidebar_thread_counting_from_one_and_do_nothing_past_the_end()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var model = Model(h, [project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);

        h.Main.OnMain(() =>
        {
            var order = model.SidebarThreads.Select(thread => thread.Id).ToList();
            var commands = new AppCommands(model);
            for (var number = 1; number <= 9; number++)
                Assert.False(Named(commands, "Thread " + number.ToString(System.Globalization.CultureInfo.InvariantCulture)).IsDisabled);

            Assert.True(commands.PerformKeyEquivalent(CtrlDigit(2)));
            Assert.Equal(order[1], model.SelectedThreadID);
            Assert.True(commands.PerformKeyEquivalent(CtrlDigit(1)));
            Assert.Equal(order[0], model.SelectedThreadID);
            Assert.True(commands.PerformKeyEquivalent(CtrlDigit(9)));
            Assert.Equal(order[0], model.SelectedThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-110")] // App/AppCommands.swift:95-98
    [Trait("Decision", "ED-099")]
    public void In_another_window_ctrl_and_a_digit_reach_the_thread_command_through_the_ports_own_dispatch() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);
        var model = new AppModel();
        var order = model.SidebarThreads.Select(thread => thread.Id).ToList();
        var commands = new AppCommands(model);
        var window = Show(new SecondaryWindow());
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        Press(window, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Control);

        Assert.Equal(order[1], model.SelectedThreadID);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-110")] // App/AppCommands.swift:95-98; App/Window/ThreadWindow.swift:5-23
    [Trait("Decision", "ED-099")]
    public void In_the_main_window_its_digit_monitor_takes_ctrl_and_a_digit_before_the_command_set() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);
        var model = new AppModel();
        var commands = new AppCommands(model);
        var window = Show(new ThreadWindow());
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        Press(window, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Control);

        Assert.Equal([2L], numbers);
        Assert.Null(model.SelectedThreadID);
        window.Close();
    });

    // ===== STOP #6: the chords on the app's own windows, as built by the app =====
    // DroppyCode/App/DroppyCodeApp.swift:21-25 (B3 52bc792) installs `AppCommands(model: WindowManager.shared.model)` as
    // the menu bar, which answers the key equivalents of the window that is key. These tests attach nothing themselves:
    // each window is made by the production path (WindowManager.showMain/showSettings, TourWindowController.present)
    // and a real key-down goes through Avalonia's keyboard device to that window.

    [Fact]
    // Partial evidence for PR-WIN-089 (the whole record: AppCommandsFinishTests). // App/DroppyCodeApp.swift:21-25; App/AppCommands.swift:40-41; App/Window/WindowManager.swift:26-30
    [Trait("Decision", "ED-099")]
    public void A_chord_pressed_in_the_main_window_the_app_shows_runs_its_command_with_no_menu_present() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            launch.Manager.ShowMain();
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            try
            {
                Assert.False(launch.Manager.Model.IsCommandPalettePresented);
                // commandPalette's default, 40 (K) with command (Shortcuts.swift:228), is Ctrl+K (ED-050 (a), ED-099).
                Press(main, Key.K, PhysicalKey.K, RawInputModifiers.Control);
                Assert.True(launch.Manager.Model.IsCommandPalettePresented); // :40 model.isCommandPalettePresented.toggle()
                Assert.Null(NativeMenu.GetMenu(main)); // OD-0008 C3: no menu is drawn
            }
            finally { main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-110")] // App/DroppyCodeApp.swift:21-25; App/AppCommands.swift:95-98; App/Window/WindowManager.swift:32-38
    [Trait("Decision", "ED-099")]
    [Trait("Decision", "OD-0151")] // Settings is a panel inside the main window
    public void While_the_Settings_panel_the_app_shows_is_open_ctrl_and_a_digit_select_that_sidebar_thread() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h =>
        {
            var project = AppModelSamples.Project("P", h.NewFolder("p"));
            h.WriteLibrary([project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);
        });
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var model = launch.Manager.Model;
            var order = model.SidebarThreads.Select(thread => thread.Id).ToList();
            launch.Manager.ShowMain();
            launch.Manager.ShowSettings();
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
            try
            {
                Assert.Same(main, settings); // OD-0151: the panel is in the main window
                Assert.True(launch.Manager.IsSettingsOpen);
                Assert.Equal(2, order.Count);
                Assert.Null(model.SelectedThreadID);
                // :97 KeyEquivalent("2") with .command: Ctrl+2 (ED-050 (a), ED-099).
                Press(settings, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Control);
                Assert.Equal(order[1], model.SelectedThreadID); // :96; AppModel.swift:1119-1123 order[number - 1]
            }
            finally { launch.Manager.CloseSettings(); main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Fact]
    // Partial evidence for PR-WIN-089 (the whole record: AppCommandsFinishTests). // App/DroppyCodeApp.swift:21-25; App/AppCommands.swift:40-41; App/Views/Tour/TourWindowController.swift:8-11,110
    [Trait("Decision", "ED-099")]
    public void While_the_tour_window_is_key_a_chord_pressed_in_it_runs_its_command() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            // TourWindowController.swift:8-11 HostWindow can become key; :110 makeKeyAndOrderFront(nil).
            var tour = TourWindowController.Shared.Present(TourPages.Pages);
            try
            {
                Assert.False(launch.Manager.Model.IsCommandPalettePresented);
                Press(tour, Key.K, PhysicalKey.K, RawInputModifiers.Control); // Shortcuts.swift:228 through ED-050 (a), ED-099
                Assert.True(launch.Manager.Model.IsCommandPalettePresented); // AppCommands.swift:40
            }
            finally { TourWindowController.Shared.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    // ===== PR-UPDATE-092: Welcome tour =====

    [Fact]
    // Partial evidence for PR-UPDATE-092; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:111-113; App/Views/Tour/TourPages.swift:26-28
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void Welcome_tour_has_no_chord_is_never_disabled_and_presents_the_tour_even_after_it_was_seen() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = h.MakeModel();
        var tour = h.Main.OnMain(() => Named(new AppCommands(model), "Welcome tour"));

        Assert.Null(tour.Shortcut);
        Assert.False(tour.IsDisabled);

        h.Main.OnMain(() => model.Settings.HasSeenTour = true);
        h.Main.OnMain(tour.Action);
        // Only a tour presented for this model marks it seen again when it closes (TourPages.swift:26-28).
        h.Main.OnMain(() => model.Settings.HasSeenTour = false);
        h.Main.OnMain(() =>
        {
            // TourWindowController.swift:52-63 runs the caller's onClose only through TourView's onClose (the checkmark control); :139-143 close() only clears the retained window/delegate, so controller Close() is not the user-close path.
            var window = TourWindowController.Shared.Present(TourPages.Pages);
            var view = Assert.IsType<TourView>(window.Content);
            var checkmark = view.GetVisualDescendants().OfType<Button>().Single(button =>
                button.GetVisualDescendants().OfType<SwiftImage>().Any(image => image.SymbolName == "checkmark"));
            checkmark.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        Assert.True(h.Main.OnMain(() => model.Settings.HasSeenTour));
    });

    // ================================================================================================================
    // Part 2 of 3
    // ================================================================================================================

    // Shortcuts.swift:225-227 through ED-050 (a) and ED-099 (2), (6): finishThread 51 (Delete, the backspace key 0x0E)
    // with shift and command; archiveThread 13 (W) and settleThread 1 (S) with command.
    private static readonly KeyboardShortcut CtrlShiftDelete = new(KeyEquivalent.Delete, ChordModifiers.Control | ChordModifiers.Shift);
    private static readonly KeyboardShortcut CtrlW = Chord("w", ChordModifiers.Control);
    private static readonly KeyboardShortcut CtrlS = Chord("s", ChordModifiers.Control);

    // AppCommands.swift:54, :56, :58, :61: literal key equivalents with .command, which ED-050 (a) reads as Ctrl.
    private static readonly KeyboardShortcut CtrlPlus = Chord("+", ChordModifiers.Control);
    private static readonly KeyboardShortcut CtrlMinus = Chord("-", ChordModifiers.Control);
    private static readonly KeyboardShortcut Ctrl0 = Chord("0", ChordModifiers.Control);
    private static readonly KeyboardShortcut CtrlEquals = Chord("=", ChordModifiers.Control);

    private static MenuCommand On(AppCommands commands, KeyboardShortcut shortcut) => commands.Body.Single(command => command.Shortcut == shortcut);

    // ===== PR-WIN-111: the divider before the finish items =====

    [Fact]
    [Trait("Req", "PR-WIN-111")] // App/AppCommands.swift:95-109
    public void Thread_9_is_followed_directly_by_the_finish_archive_and_settle_items_with_no_divider_entry()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var body = new AppCommands(model).Body;
            var nine = body.Select(command => command.Title).ToList().IndexOf("Thread 9");
            Assert.True(nine >= 0);
            // :99 Divider() is drawn by no menu, so it has no entry: :100, :103 and :106 follow :96's last item directly.
            Assert.Equal(new KeyboardShortcut?[] { CtrlShiftDelete, CtrlW, CtrlS }, body.Skip(nine + 1).Take(3).Select(command => command.Shortcut));
            // :111-113 the help group comes after the Thread menu.
            Assert.Equal("Welcome tour", body[nine + 4].Title);
            Assert.All(body, command => Assert.False(string.IsNullOrEmpty(command.Title)));
        });
    }

    // ===== PR-WIN-112: the finish item =====

    [Fact]
    // Partial evidence for PR-WIN-112; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:100-102; App/AppModel.swift:855-858; App/AppSettings.swift:30-34
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void The_finish_item_reads_Settle_thread_Archive_thread_or_Reopen_thread_from_the_setting_and_the_selection()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var open = AppModelSamples.Thread(project);
        var settled = AppModelSamples.Thread(project) with { IsSettled = true };
        var model = Model(h, [project], [open, settled]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            (ThreadFinishAction Action, Guid? Selected, string Title)[] cases =
            [
                (ThreadFinishAction.Settle, null, "Settle thread"),
                (ThreadFinishAction.Settle, open.Id, "Settle thread"),
                (ThreadFinishAction.Settle, settled.Id, "Reopen thread"),
                (ThreadFinishAction.Archive, null, "Archive thread"),
                (ThreadFinishAction.Archive, open.Id, "Archive thread"),
                (ThreadFinishAction.Archive, settled.Id, "Archive thread"),
            ];
            foreach (var (action, selected, title) in cases)
            {
                model.Settings.ThreadFinishAction = action;
                model.SelectedThreadID = selected;
                Assert.Equal(title, On(commands, CtrlShiftDelete).Title);
            }
            // Shortcuts.swift:177: the same command in the shortcut settings list.
            Assert.Equal("Finish thread", AppShortcut.FinishThread.Title());
        });
    }

    [Fact]
    // Partial evidence for PR-WIN-112; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:102
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void The_finish_item_is_disabled_with_no_selection_and_leaves_its_chord_unclaimed()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            var commands = new AppCommands(model);

            Assert.True(On(commands, CtrlShiftDelete).IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlShiftDelete));
            Assert.False(model.Thread(thread.Id)!.IsArchived);

            model.SelectedThreadID = thread.Id;
            Assert.False(On(commands, CtrlShiftDelete).IsDisabled);
        });
    }

    [Fact]
    // Partial evidence for PR-WIN-112; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:100-101; App/AppModel.swift:865-870
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void With_Archive_chosen_the_finish_chord_archives_the_selected_thread()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            model.SelectedThreadID = thread.Id;
            var commands = new AppCommands(model);

            Assert.True(commands.PerformKeyEquivalent(CtrlShiftDelete));
            Assert.True(model.Thread(thread.Id)!.IsArchived);
        });
    }

    [Fact]
    // Partial evidence for PR-WIN-112; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:100-101; App/AppModel.swift:865-881
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void With_Settle_chosen_the_finish_chord_settles_the_selected_thread_once_no_row_has_taken_the_request()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            model.Settings.SettleSound = false; // no chime from a test (SettleChime plays through the speakers)
            model.SelectedThreadID = thread.Id;
            Assert.True(new AppCommands(model).PerformKeyEquivalent(CtrlShiftDelete));
        });
        // AppModel.swift:875-880: with no row to take the request up, the thread settles after 80 milliseconds.
        h.Main.Advance(TimeSpan.FromMilliseconds(80));

        Assert.True(h.Main.OnMain(() => model.Thread(thread.Id)!.IsSettled));
        Assert.False(h.Main.OnMain(() => model.Thread(thread.Id)!.IsArchived));
    }

    [Fact]
    // Partial evidence for PR-WIN-112; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:100-101; App/AppModel.swift:868-870
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void With_Settle_chosen_the_finish_chord_archives_a_helper_thread()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var parent = AppModelSamples.Thread(project);
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id };
        var model = Model(h, [project], [parent, helper]);

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            model.SelectedThreadID = helper.Id;
            var commands = new AppCommands(model);

            Assert.False(On(commands, CtrlShiftDelete).IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlShiftDelete));
            Assert.True(model.Thread(helper.Id)!.IsArchived);
        });
    }

    // ===== PR-WIN-113: Archive thread (with U+2026) =====

    [Fact]
    [Trait("Req", "PR-WIN-113")] // App/AppCommands.swift:103-105; App/AppModel.swift:902-912
    public void Archive_chord_asks_to_archive_the_selected_thread_and_is_disabled_with_no_selection()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var item = On(commands, CtrlW);
            Assert.Equal("Archive thread\U00002026", item.Title); // U+2026 HORIZONTAL ELLIPSIS
            Assert.Equal("Archive thread", AppShortcut.ArchiveThread.Title()); // Shortcuts.swift:178
            Assert.True(item.IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlW));
            Assert.Null(model.AppModelArchiveRequest);

            model.SelectedThreadID = thread.Id;
            Assert.False(On(commands, CtrlW).IsDisabled);
            // Part 3 correction: reading the body with a selection makes the thread's runtime (:7-9, :68; AppModel.swift
            // :434-439), so an empty thread would be deleted with no question (AppModel.swift:906-910, STOP #3). Something
            // typed for it keeps it from being that empty thread, so it is asked about.
            model.Runtime(thread.Id).Draft = new ComposerDraft { Text = "keep" };
            Assert.True(commands.PerformKeyEquivalent(CtrlW));
            Assert.Equal(thread.Id, model.AppModelArchiveRequest?.ThreadID);
            Assert.False(model.Thread(thread.Id)!.IsArchived);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-113")] // App/AppCommands.swift:17-19, :103-105; App/Window/ThreadWindow.swift:25-38
    [Trait("Decision", "ED-099")]
    public void In_the_main_window_ctrl_w_reaches_the_archive_command_and_leaves_the_window_open() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = new AppModel();
        model.SelectedThreadID = thread.Id;
        // Part 3 correction: the press reads the body, which makes the thread's runtime (:7-9), and an empty thread with
        // a runtime is deleted with no question (AppModel.swift:906-910, STOP #3); a draft keeps this one to be asked about.
        model.Runtime(thread.Id).Draft = new ComposerDraft { Text = "keep" };
        var commands = new AppCommands(model);
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);

        Assert.Equal(thread.Id, model.AppModelArchiveRequest?.ThreadID);
        Assert.Empty(passed);
        Assert.True(window.IsVisible);
        window.Close();
    });

    // ===== PR-WIN-114: Settle thread / Reopen thread =====

    [Fact]
    [Trait("Req", "PR-WIN-114")] // App/AppCommands.swift:106-108
    public void The_settle_item_reads_Settle_thread_and_is_disabled_with_no_selection_or_a_helper_selected()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var parent = AppModelSamples.Thread(project);
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id };
        var model = Model(h, [project], [parent, helper]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // :108 model.selectedThread.map { $0.isHelper } ?? true: nothing selected reads as disabled.
            Assert.Equal("Settle thread", On(commands, CtrlS).Title);
            Assert.True(On(commands, CtrlS).IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlS));

            model.SelectedThreadID = helper.Id;
            Assert.Equal("Settle thread", On(commands, CtrlS).Title);
            Assert.True(On(commands, CtrlS).IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlS));
            Assert.Null(model.AppModelFinishRequest);

            model.SelectedThreadID = parent.Id;
            Assert.Equal("Settle thread", On(commands, CtrlS).Title);
            Assert.False(On(commands, CtrlS).IsDisabled);
            Assert.Equal("Settle thread", AppShortcut.SettleThread.Title()); // Shortcuts.swift:179
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-114")] // App/AppCommands.swift:106-108; App/AppModel.swift:887-891
    public void Settle_chord_asks_to_settle_or_reopen_and_never_archives_even_with_Archive_chosen()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var open = AppModelSamples.Thread(project);
        var settled = AppModelSamples.Thread(project) with { IsSettled = true };
        var model = Model(h, [project], [open, settled]);

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            var commands = new AppCommands(model);

            model.SelectedThreadID = open.Id;
            Assert.True(commands.PerformKeyEquivalent(CtrlS));
            Assert.Equal((open.Id, false), (model.AppModelFinishRequest!.ThreadID, model.AppModelFinishRequest.Reopens));
            Assert.False(model.Thread(open.Id)!.IsArchived);

            model.SelectedThreadID = settled.Id;
            Assert.Equal("Reopen thread", On(commands, CtrlS).Title);
            Assert.False(On(commands, CtrlS).IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlS));
            Assert.Equal((settled.Id, true), (model.AppModelFinishRequest!.ThreadID, model.AppModelFinishRequest.Reopens));
            Assert.False(model.Thread(settled.Id)!.IsArchived);
        });
    }

    // ===== PR-WIN-222 to PR-WIN-226: the zoom items =====

    [Fact]
    [Trait("Req", "PR-WIN-222")] // App/AppCommands.swift:53-54; UI/Common/AppZoom.swift:12-15; App/AppSettings.swift:253-254
    public void Zoom_In_steps_the_zoom_up_one_level_and_at_200_stays_there_still_writing_the_setting()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var item = On(commands, CtrlPlus);
            Assert.Equal("Zoom In", item.Title);
            Assert.False(item.IsDisabled);

            model.Settings.AppZoom = 100;
            Assert.True(commands.PerformKeyEquivalent(CtrlPlus));
            Assert.Equal(110, model.Settings.AppZoom);
            Assert.True(commands.PerformKeyEquivalent(CtrlPlus));
            Assert.Equal(125, model.Settings.AppZoom);

            model.Settings.AppZoom = 200;
            h.Defaults.Writes.Clear();
            Assert.False(On(commands, CtrlPlus).IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlPlus));
            Assert.Equal(200, model.Settings.AppZoom);
            Assert.Equal(["appZoom"], h.Defaults.Writes);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-223")] // App/AppCommands.swift:55-56; UI/Common/AppZoom.swift:12-15; App/AppSettings.swift:253-254
    public void Zoom_Out_steps_the_zoom_down_one_level_and_at_75_stays_there_still_writing_the_setting()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var item = On(commands, CtrlMinus);
            Assert.Equal("Zoom Out", item.Title);
            Assert.False(item.IsDisabled);

            model.Settings.AppZoom = 100;
            Assert.True(commands.PerformKeyEquivalent(CtrlMinus));
            Assert.Equal(90, model.Settings.AppZoom);
            Assert.True(commands.PerformKeyEquivalent(CtrlMinus));
            Assert.Equal(75, model.Settings.AppZoom);

            h.Defaults.Writes.Clear();
            Assert.False(On(commands, CtrlMinus).IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlMinus));
            Assert.Equal(75, model.Settings.AppZoom);
            Assert.Equal(["appZoom"], h.Defaults.Writes);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-224")] // App/AppCommands.swift:57-58; Core/Models/Shortcuts.swift:233 (OD-0012 G6)
    public void Actual_Size_sets_the_zoom_to_100_from_any_level_and_alone_holds_ctrl_0()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var item = On(commands, Ctrl0);
            Assert.Equal("Actual Size", item.Title);
            Assert.False(item.IsDisabled);
            // OD-0012 G6: showMainWindow ships with no chord, so no command of the store shares Ctrl+0 by default.
            Assert.Null(ShortcutStore.Shared.Current.Chord(AppShortcut.ShowMainWindow));

            foreach (var level in new long[] { 75, 150, 200, 100 })
            {
                model.Settings.AppZoom = level;
                h.Defaults.Writes.Clear();
                Assert.True(commands.PerformKeyEquivalent(Ctrl0));
                Assert.Equal(100, model.Settings.AppZoom);
                Assert.Equal(["appZoom"], h.Defaults.Writes);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-225")] // App/AppCommands.swift:53-62
    public void A_second_Zoom_In_answers_ctrl_equals_after_Actual_Size_so_zoom_in_needs_no_shift()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var body = commands.Body;
            var zoomIn = body.Select((command, index) => (command, index)).Where(pair => pair.command.Title == "Zoom In").ToList();
            Assert.Equal(2, zoomIn.Count);
            Assert.Equal(CtrlPlus, zoomIn[0].command.Shortcut);
            Assert.Equal(CtrlEquals, zoomIn[1].command.Shortcut);
            // :53-61 Zoom In, Zoom Out, Actual Size, then the hidden Zoom In.
            Assert.Equal(new KeyboardShortcut?[] { CtrlPlus, CtrlMinus, Ctrl0, CtrlEquals },
                body.Skip(zoomIn[0].index).Take(4).Select(command => command.Shortcut));
            Assert.False(zoomIn[1].command.IsDisabled);

            model.Settings.AppZoom = 150;
            Assert.True(commands.PerformKeyEquivalent(CtrlEquals));
            Assert.Equal(175, model.Settings.AppZoom);
            model.Settings.AppZoom = 200;
            Assert.True(commands.PerformKeyEquivalent(CtrlEquals));
            Assert.Equal(200, model.Settings.AppZoom);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-223")] // App/AppCommands.swift:55-56
    [Trait("Req", "PR-WIN-224")] // App/AppCommands.swift:57-58
    [Trait("Req", "PR-WIN-225")] // App/AppCommands.swift:59-62
    [Trait("Req", "UI-WIN-077")] // App/AppCommands.swift:59-62
    [Trait("Decision", "ED-099")]
    public void In_a_window_ctrl_with_the_equals_minus_and_zero_keys_of_a_US_keyboard_reaches_the_zoom_commands() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        h.WriteLibrary([], []);
        var model = new AppModel();
        var commands = new AppCommands(model);
        var window = Show(new AvaloniaWindow());
        var passed = Passed(window);
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);
        model.Settings.AppZoom = 100;

        // The US keyboard's "=" key without Shift types "=" (AppCommands.swift:59), its "-" key "-", its "0" key "0".
        Press(window, Key.OemPlus, PhysicalKey.Equal, RawInputModifiers.Control);
        Assert.Equal(110, model.Settings.AppZoom);
        Press(window, Key.OemMinus, PhysicalKey.Minus, RawInputModifiers.Control);
        Press(window, Key.OemMinus, PhysicalKey.Minus, RawInputModifiers.Control);
        Assert.Equal(90, model.Settings.AppZoom);
        Press(window, Key.D0, PhysicalKey.Digit0, RawInputModifiers.Control);
        Assert.Equal(100, model.Settings.AppZoom);
        Assert.Empty(passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-226")] // App/AppCommands.swift:54, :56, :58, :61; Core/Models/Shortcuts.swift:251-257, :320-334
    public void The_zoom_chords_are_outside_the_shortcut_store_so_recording_one_for_a_command_is_not_refused()
    {
        using var h = new AppModelHarness();
        var store = h.Main.OnMain(FreshStore);
        using var shared = ShortcutStore.Shared.Override(store);
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            // No AppShortcut case stands for a zoom item, so none has a row, a default or an override.
            var titles = SwiftEnum<AppShortcut>.AllCases.Select(shortcut => shortcut.Title()).ToList();
            Assert.DoesNotContain("Zoom In", titles);
            Assert.DoesNotContain("Zoom Out", titles);
            Assert.DoesNotContain("Actual Size", titles);

            // ED-099 (2): the "-", "=" and "0" key positions, 0x0C, 0x0D and 0x0B, with Ctrl.
            var ctrlMinus = new KeyChord(0x0C, ChordModifiers.Control);
            var ctrlEquals = new KeyChord(0x0D, ChordModifiers.Control);
            var ctrlZero = new KeyChord(0x0B, ChordModifiers.Control);
            Assert.Null(store.Refusal(ctrlMinus, AppShortcut.NewThread));
            Assert.Null(store.Refusal(ctrlEquals, AppShortcut.NewThread));
            // OD-0012 G6: showMainWindow has no default chord, so Ctrl+0 is refused only once it is recorded for it.
            Assert.Null(store.Refusal(ctrlZero, AppShortcut.NewThread));
            store.Set(ctrlZero, AppShortcut.ShowMainWindow);
            Assert.Equal("Show nth already uses this chord.", store.Refusal(ctrlZero, AppShortcut.NewThread));
            // The show-main entry now exists. Restore the fixture's default before testing the fixed zoom chords;
            // its recorded chord is exercised separately above, rather than assuming that entry is absent.
            store.Reset(AppShortcut.ShowMainWindow);

            // Recorded anyway, the command takes the chord and the zoom items keep their own, which the store never sees.
            store.Set(ctrlMinus, AppShortcut.StopTurn);
            var commands = new AppCommands(model);
            Assert.Equal(CtrlMinus, store.KeyboardShortcut(AppShortcut.StopTurn));
            Assert.Equal(["Zoom Out", "Stop"], commands.Body.Where(command => command.Shortcut == CtrlMinus).Select(command => command.Title));
            Assert.Equal(["Zoom In", "Zoom Out", "Actual Size", "Zoom In"],
                new[] { CtrlPlus, CtrlMinus, Ctrl0, CtrlEquals }.Select(shortcut => commands.Body.First(command => command.Shortcut == shortcut).Title));
        });
    }

    // ===== UI-LAUNCH-006: no quit item =====

    [Fact]
    [Trait("Req", "UI-LAUNCH-006")] // App/AppCommands.swift:11-114 (the termination group is never replaced)
    [Trait("Decision", "ED-099")]
    public void The_command_set_adds_no_quit_item_and_leaves_alt_f4_and_ctrl_q_to_the_rest_of_the_window() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        h.WriteLibrary([], []);
        var model = new AppModel();
        var commands = new AppCommands(model);
        // OD-0008 C2: no quit or exit item and no label; the window's close button and Alt+F4 quit.
        Assert.DoesNotContain(commands.Body, command => command.Title.Contains("Quit", StringComparison.Ordinal) || command.Title.Contains("Exit", StringComparison.Ordinal));
        var window = Show(new AvaloniaWindow());
        var passed = Passed(window);
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        Press(window, Key.F4, PhysicalKey.F4, RawInputModifiers.Alt);
        Press(window, Key.Q, PhysicalKey.Q, RawInputModifiers.Control);

        Assert.Equal([Key.F4, Key.Q], passed);
        window.Close();
    });

    // ===== UI-WIN-008 to UI-WIN-010: the new-item group =====

    [Fact]
    [Trait("Req", "UI-WIN-008")] // App/AppCommands.swift:27-29
    [Trait("Req", "UI-WIN-009")] // App/AppCommands.swift:30-31
    [Trait("Req", "UI-WIN-010")] // App/AppCommands.swift:32-34
    public void The_new_item_group_keeps_its_three_commands_in_order_on_their_chords_and_never_disables_them()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var plain = AppModelSamples.Project("Plain", h.NewFolder("not a repository"));
        var thread = AppModelSamples.Thread(plain);
        var model = Model(h, [plain], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var titles = commands.Body.Select(command => command.Title).ToList();
            var first = titles.IndexOf("New thread");
            // :28, :30, :33 in order; the divider at :32 is drawn by no menu. U+2026 HORIZONTAL ELLIPSIS.
            Assert.Equal(["New thread", "New thread in worktree", "Add project\U00002026"], titles.Skip(first).Take(3));
            Assert.Equal(new KeyboardShortcut?[] { CtrlN, CtrlShiftN, CtrlO }, commands.Body.Skip(first).Take(3).Select(command => command.Shortcut));
            // The shortcut settings row carries no ellipsis (Shortcuts.swift:170).
            Assert.Equal("Add project", AppShortcut.AddProject.Title());

            foreach (var selected in new Guid?[] { null, thread.Id })
            {
                model.SelectedThreadID = selected;
                Assert.All(commands.Body.Skip(first).Take(3), command => Assert.False(command.IsDisabled));
            }
        });
    }

    // ===== UI-WIN-011 to UI-WIN-014, UI-TERM-031: the view group =====

    [Fact]
    [Trait("Req", "UI-WIN-011")] // App/AppCommands.swift:37-39
    [Trait("Req", "UI-WIN-012")] // App/AppCommands.swift:40-41
    [Trait("Req", "UI-WIN-013")] // App/AppCommands.swift:42-44
    [Trait("Req", "UI-WIN-014")] // App/AppCommands.swift:45-47
    [Trait("Req", "UI-TERM-031")] // App/AppCommands.swift:42-44
    public void The_view_group_keeps_its_five_toggles_in_order_and_only_terminal_and_changes_need_a_selection()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var titles = commands.Body.Select(command => command.Title).ToList();
            var first = titles.IndexOf("Toggle sidebar");
            // :38, :40, :42, :45, :48, then the zoom items: the divider at :52 is drawn by no menu.
            Assert.Equal(["Toggle sidebar", "Command palette", "Toggle terminal", "Toggle changes", "Toggle activity view", "Zoom In"],
                titles.Skip(first).Take(6));
            Assert.Equal(new KeyboardShortcut?[] { CtrlB, CtrlK, CtrlJ, CtrlD }, commands.Body.Skip(first).Take(4).Select(command => command.Shortcut));

            // :44, :47: terminal and changes are off with no selection; sidebar and palette never are.
            Assert.Equal([false, false, true, true], commands.Body.Skip(first).Take(4).Select(command => command.IsDisabled));
            model.SelectedThreadID = thread.Id;
            Assert.Equal([false, false, false, false], commands.Body.Skip(first).Take(4).Select(command => command.IsDisabled));

            // Each chord does what its item did.
            var sidebar = model.Sidebar.IsVisible;
            Assert.True(commands.PerformKeyEquivalent(CtrlB));
            Assert.Equal(!sidebar, model.Sidebar.IsVisible);
            Assert.True(commands.PerformKeyEquivalent(CtrlK));
            Assert.True(model.IsCommandPalettePresented);
            Assert.True(commands.PerformKeyEquivalent(CtrlJ));
            Assert.True(model.Runtime(thread.Id).IsTerminalVisible);
            Assert.True(commands.PerformKeyEquivalent(CtrlD));
            Assert.True(model.Runtime(thread.Id).IsChangesPaneOpen); // web W4 (ED-NEW-web-w4-1): the pane, not the popover
            Assert.False(model.Runtime(thread.Id).IsDiffVisible);
        });
    }

    // ===== UI-TERM-032: the run-script item =====

    [Fact]
    [Trait("Req", "UI-TERM-032")] // App/AppCommands.swift:87-95; App/AppModel.swift:1089-1092
    public void The_run_script_item_follows_Next_thread_before_Thread_1_and_reads_Run_and_the_first_scripts_name()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var scripted = AppModelSamples.Project("Scripted", h.NewFolder("scripted")) with
        {
            Scripts = new ValueList<ProjectScript>([new ProjectScript("Release", "Write-Output released"), new ProjectScript("Lint", "Write-Output linted")]),
        };
        var plain = AppModelSamples.Project("Plain", h.NewFolder("plain"));
        var scriptedThread = AppModelSamples.Thread(scripted);
        var plainThread = AppModelSamples.Thread(plain);
        var model = Model(h, [scripted, plain], [scriptedThread, plainThread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            (Guid? Selected, string Title, bool Disabled)[] cases =
            [
                (null, "Run top script", true),
                (plainThread.Id, "Run top script", true),
                (scriptedThread.Id, "Run Release", false),
            ];
            foreach (var (selected, title, disabled) in cases)
            {
                model.SelectedThreadID = selected;
                var body = commands.Body;
                var index = body.Select(command => command.Title).ToList().IndexOf(title);
                Assert.Equal(CtrlR, body[index].Shortcut);
                Assert.Equal(disabled, body[index].IsDisabled);
                Assert.Equal("Next thread", body[index - 1].Title);
                Assert.Equal("Thread 1", body[index + 1].Title);
            }
        });
    }

    // ===== UI-UPDATE-055: the Welcome tour item =====

    [Fact]
    // Partial evidence for UI-UPDATE-055; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:111-113
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void Welcome_tour_is_the_last_and_only_help_command_with_no_chord_and_never_disabled()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            foreach (var selected in new Guid?[] { null, thread.Id })
            {
                model.SelectedThreadID = selected;
                var body = commands.Body;
                // CommandGroup(replacing: .help) holds this one item: nothing follows it and nothing else is titled so.
                Assert.Equal("Welcome tour", body[^1].Title);
                Assert.Single(body, command => command.Title == "Welcome tour");
                Assert.Null(body[^1].Shortcut);
                Assert.False(body[^1].IsDisabled);
            }
        });
    }

    // ================================================================================================================
    // Part 3 of 3
    // ================================================================================================================

    // :65-109 CommandMenu("Thread"): its eighteen items start at "Stop" (:66), a title no other item carries.
    private static int ThreadMenuStart(IReadOnlyList<MenuCommand> body)
    {
        var start = body.Select(command => command.Title).ToList().IndexOf("Stop");
        Assert.True(start >= 0);
        return start;
    }

    private static List<MenuCommand> ThreadMenu(IReadOnlyList<MenuCommand> body) => body.Skip(ThreadMenuStart(body)).Take(18).ToList();

    // Shortcuts.swift:249, :259-265: the recorded chords as UserDefaults holds them. A plain JSONEncoder writes an
    // unsorted object, so either order of KeyChord's two keys is the Swift's (R-TEST-4; precedent
    // Marrow.Core.Tests/Models/ShortcutsTests.cs AssertStoredChord).
    private static void AssertStoredChord(AppModelHarness h, string raw, long keyCode, ulong modifiers)
    {
        var stored = Assert.IsType<byte[]>(h.Defaults.Object("keyboardShortcuts"));
        var code = "\"keyCode\":" + keyCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var mods = "\"modifiers\":" + modifiers.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var prefix = "{\"" + raw + "\":{\"chord\":{";
        string[] permutations = [prefix + code + "," + mods + "}}}", prefix + mods + "," + code + "}}}"];
        Assert.Contains(permutations, permutation => System.Text.Encoding.UTF8.GetBytes(permutation).AsSpan().SequenceEqual(stored));
    }

    // ===== UI-WIN-015: Toggle activity view =====

    [Fact]
    [Trait("Req", "UI-WIN-015")] // App/AppCommands.swift:37-52; Core/Models/Shortcuts.swift:184, :232; UI/Chrome/Chrome.swift:100-104
    public void Toggle_activity_view_is_the_fifth_view_toggle_on_ctrl_alt_u_never_disabled_with_the_zoom_items_right_after_it()
    {
        using var h = new AppModelHarness();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        var commands = h.Main.OnMain(() => new AppCommands(model));

        h.Main.OnMain(() =>
        {
            foreach (var selected in new Guid?[] { null, thread.Id })
            {
                model.SelectedThreadID = selected;
                var body = commands.Body;
                var titles = body.Select(command => command.Title).ToList();
                var index = titles.IndexOf("Toggle activity view");
                // :38, :40, :42, :45 before it: the fifth of the five.
                Assert.Equal(["Toggle sidebar", "Command palette", "Toggle terminal", "Toggle changes"], titles.Skip(index - 4).Take(4));
                // :52 the divider after it is drawn by no menu, so the first zoom item (:53) follows it directly.
                Assert.Equal("Zoom In", titles[index + 1]);
                Assert.Equal(CtrlAltU, body[index].Shortcut);
                Assert.False(body[index].IsDisabled); // :48-51 carry no .disabled
            }
            // OD-0008 B7: Option-Command-U (key code 32 with option and command, Shortcuts.swift:232) reads Ctrl+Alt+U.
            Assert.Equal("Ctrl+Alt+U", ShortcutStore.Label(AppShortcut.ToggleActivityView));
            // Shortcuts.swift:184: the shortcut settings list titles the same command "Activity view".
            Assert.Equal("Activity view", AppShortcut.ToggleActivityView.Title());
        });

        // :49 withAnimation(Chrome.panelSlide): the flip lands inside the spring 0.32 / 0.9 (Chrome.swift:101-103).
        var applied = new List<(bool Value, Animation? Animation)>();
        using var tracked = h.Main.OnMain(() => new Tracked<bool>(() => model.Settings.SidebarActivityView, value => applied.Add((value, Transaction.Current))));
        h.Main.OnMain(() => Assert.True(commands.PerformKeyEquivalent(CtrlAltU)));
        Assert.Equal(new (bool Value, Animation? Animation)[] { (false, null), (true, Animation.Spring(response: 0.32, dampingFraction: 0.9)) }, applied);
    }

    // ===== UI-WIN-016: the Thread menu =====

    [Fact]
    [Trait("Req", "UI-WIN-016")] // App/AppCommands.swift:65-109; App/AppModel.swift:855-858; App/AppSettings.swift:716
    public void The_thread_menu_holds_nine_named_and_nine_numbered_commands_in_order_between_the_zoom_items_and_the_tour()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var body = new AppCommands(model).Body;
            var start = ThreadMenuStart(body);
            // :66, :74, :78 | :85, :87, :89, :95-98 | :100, :103, :106. Nothing selected and the finish setting at its
            // default, settle (AppSettings.swift:716), so the finish item reads "Settle thread" (AppModel.swift:857).
            // U+2026 HORIZONTAL ELLIPSIS.
            string[] titles =
            [
                "Stop", "Queue a chat", "Toggle plan mode",
                "Previous thread", "Next thread", "Run top script",
                "Thread 1", "Thread 2", "Thread 3", "Thread 4", "Thread 5", "Thread 6", "Thread 7", "Thread 8", "Thread 9",
                "Settle thread", "Archive thread\U00002026", "Settle thread",
            ];
            KeyboardShortcut?[] chords =
            [
                CtrlPeriod, CtrlReturn, CtrlShiftP,
                CtrlShiftTab, CtrlTab, CtrlR,
                CtrlDigit(1), CtrlDigit(2), CtrlDigit(3), CtrlDigit(4), CtrlDigit(5), CtrlDigit(6), CtrlDigit(7), CtrlDigit(8), CtrlDigit(9),
                CtrlShiftDelete, CtrlW, CtrlS,
            ];
            Assert.Equal(titles, body.Skip(start).Take(18).Select(command => command.Title));
            Assert.Equal(chords, body.Skip(start).Take(18).Select(command => command.Shortcut));
            // The dividers at :84 and :99 are drawn by no menu, so the three blocks run on with no entry between them. The
            // view group ends right before the menu with the hidden Zoom In (:60-62) and the help group (:111-113) follows it.
            Assert.Equal(CtrlEquals, body[start - 1].Shortcut);
            Assert.Equal("Welcome tour", body[start + 18].Title);
        });
    }

    // ===== UI-WIN-017: Stop =====

    [Fact]
    [Trait("Req", "UI-WIN-017")] // App/AppCommands.swift:7-9, :66-68; Core/Models/Shortcuts.swift:171, :219
    public void Stop_is_the_first_thread_command_on_ctrl_period_and_is_on_only_while_the_selected_threads_runtime_runs()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var working = AppModelSamples.Thread(project);
        var idle = AppModelSamples.Thread(project);
        var model = Model(h, [project], [working, idle]);
        var runtime = h.Main.OnMain(() => model.Runtime(working.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var stop = ThreadMenu(commands.Body)[0];
            Assert.Equal("Stop", stop.Title);
            Assert.Equal(CtrlPeriod, stop.Shortcut);
            Assert.True(stop.IsDisabled); // :68 nothing selected: runtime?.isRunning ?? false

            model.SelectedThreadID = working.Id;
            Assert.True(ThreadMenu(commands.Body)[0].IsDisabled); // selected, not running
            runtime.RehearseTurn("working");
            Assert.False(ThreadMenu(commands.Body)[0].IsDisabled);

            // Another thread selected while the first still runs: the selected thread's own runtime decides.
            model.SelectedThreadID = idle.Id;
            Assert.True(ThreadMenu(commands.Body)[0].IsDisabled);
            Assert.False(commands.PerformKeyEquivalent(CtrlPeriod));
            Assert.True(runtime.IsRunning);

            Assert.Equal("Ctrl+.", ShortcutStore.Label(AppShortcut.StopTurn)); // OD-0008 B7: Command-period
            Assert.Equal("Stop the running turn", AppShortcut.StopTurn.Title()); // Shortcuts.swift:171
        });
    }

    // ===== UI-WIN-018: Queue a chat =====

    [Fact]
    // Partial evidence for UI-WIN-018; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:69-77; Core/Models/Shortcuts.swift:172, :220
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void Queue_a_chat_is_the_second_thread_command_on_ctrl_return_and_is_off_with_no_selection_an_empty_draft_or_an_approval()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            MenuCommand Queue() => ThreadMenu(commands.Body)[1];
            Assert.Equal("Queue a chat", Queue().Title);
            Assert.Equal(CtrlReturn, Queue().Shortcut);

            runtime.Draft = new ComposerDraft { Text = "next step" };
            Assert.True(Queue().IsDisabled); // :76 nothing selected: runtime?.draftIsEmpty ?? true

            // :76-77 read only the draft and the approvals: with a draft and nothing running the item is on, the case
            // queueDraftAsFollowUp answers by sending the draft (ThreadRuntime.swift:780-785; STOP #5 below).
            model.SelectedThreadID = thread.Id;
            Assert.False(runtime.IsRunning);
            Assert.False(Queue().IsDisabled);
            runtime.RehearseTurn("working");
            Assert.False(Queue().IsDisabled);

            runtime.Draft = new ComposerDraft();
            Assert.True(Queue().IsDisabled); // the draft is empty
            Assert.False(commands.PerformKeyEquivalent(CtrlReturn));

            runtime.Draft = new ComposerDraft { Text = "next step" };
            runtime.Rehearse(new ProviderEvent.Approval(new ApprovalRequest("approval", ApprovalRequest.Kind.Command, "Command", ValueList<ApprovalRequest.Option>.Empty)));
            Assert.True(Queue().IsDisabled); // :77 an approval is pending
            Assert.False(commands.PerformKeyEquivalent(CtrlReturn));
            Assert.Empty(runtime.FollowUps);

            Assert.Equal("Ctrl+Return", ShortcutStore.Label(AppShortcut.QueueChat)); // OD-0008 B7: Command-Return
            Assert.Equal("Queue a chat", AppShortcut.QueueChat.Title()); // Shortcuts.swift:172
        });
    }

    // ===== UI-WIN-019: Toggle plan mode =====

    [Fact]
    [Trait("Req", "UI-WIN-019")] // App/AppCommands.swift:78-84; Core/Models/Shortcuts.swift:173, :221
    public void Toggle_plan_mode_is_the_third_thread_command_on_ctrl_shift_p_keeping_one_title_in_either_mode()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var item = ThreadMenu(commands.Body)[2];
            Assert.Equal("Toggle plan mode", item.Title);
            Assert.Equal(CtrlShiftP, item.Shortcut);
            Assert.True(item.IsDisabled); // :83

            model.SelectedThreadID = thread.Id;
            foreach (var mode in new[] { InteractionMode.Plan, InteractionMode.Build })
            {
                Assert.False(ThreadMenu(commands.Body)[2].IsDisabled);
                Assert.True(commands.PerformKeyEquivalent(CtrlShiftP));
                Assert.Equal(mode, model.Thread(thread.Id)!.InteractionMode); // :80
                // :78 a constant title: no checkmark and no word for the mode in force.
                Assert.Equal("Toggle plan mode", ThreadMenu(commands.Body)[2].Title);
            }
            // :84 the divider after it is drawn by no menu: Previous thread comes next.
            Assert.Equal("Previous thread", ThreadMenu(commands.Body)[3].Title);

            Assert.Equal("Ctrl+Shift+P", ShortcutStore.Label(AppShortcut.TogglePlanMode)); // OD-0008 B7: Shift-Command-P
            Assert.Equal("Plan mode", AppShortcut.TogglePlanMode.Title()); // Shortcuts.swift:173
        });
    }

    // ===== UI-WIN-020, UI-WIN-021: previous and next thread =====

    [Fact]
    [Trait("Req", "UI-WIN-020")] // App/AppCommands.swift:85-86; Core/Models/Shortcuts.swift:174, :222
    [Trait("Req", "UI-WIN-021")] // App/AppCommands.swift:87-88; Core/Models/Shortcuts.swift:175, :223
    public void Previous_and_next_thread_are_the_fourth_and_fifth_thread_commands_on_the_tab_chords_and_never_disabled()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            // No thread at all and nothing selected: neither carries a .disabled.
            var menu = ThreadMenu(new AppCommands(model).Body);
            Assert.Equal(("Previous thread", (KeyboardShortcut?)CtrlShiftTab, false), (menu[3].Title, menu[3].Shortcut, menu[3].IsDisabled));
            Assert.Equal(("Next thread", (KeyboardShortcut?)CtrlTab, false), (menu[4].Title, menu[4].Shortcut, menu[4].IsDisabled));

            // OD-0008 B7: Control-Shift-Tab and Control-Tab.
            Assert.Equal("Ctrl+Shift+Tab", ShortcutStore.Label(AppShortcut.PreviousThread));
            Assert.Equal("Ctrl+Tab", ShortcutStore.Label(AppShortcut.NextThread));
            Assert.Equal("Previous thread", AppShortcut.PreviousThread.Title()); // Shortcuts.swift:174
            Assert.Equal("Next thread", AppShortcut.NextThread.Title()); // Shortcuts.swift:175
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-020")] // App/AppCommands.swift:85-86; App/Window/ThreadWindow.swift:25-38
    [Trait("Decision", "ED-099")]
    public void In_the_main_window_the_tab_chord_of_previous_thread_is_taken_by_the_window_and_a_rebound_chord_reaches_the_command() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        var store = FreshStore();
        using var shared = ShortcutStore.Shared.Override(store);
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);
        var model = new AppModel();
        var order = model.SidebarThreads.Select(thread => thread.Id).ToList();
        model.SelectedThreadID = order[1];
        var commands = new AppCommands(model);
        var window = Show(new ThreadWindow());
        var steps = new List<long>();
        window.SelectThread = steps.Add;
        var passed = Passed(window);
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        // ThreadWindow.swift:26-35: the default is a Tab chord, which the window takes before any menu sees it.
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal([-1L], steps);
        Assert.Equal(order[1], model.SelectedThreadID);
        Assert.Empty(passed);

        // Rebound off the Tab key (Y, scan code 0x15, with Ctrl and Alt), the chord goes the menu's way: the command runs
        // selectThread(offset: -1) (AppModel.swift:1109-1117) and the window's handler is not involved.
        var rebound = new KeyChord(0x15, ChordModifiers.Control | ChordModifiers.Alt);
        Assert.Null(store.Refusal(rebound, AppShortcut.PreviousThread));
        store.Set(rebound, AppShortcut.PreviousThread);
        Press(window, Key.Y, PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Equal([-1L], steps);
        Assert.Equal(order[0], model.SelectedThreadID);
        Assert.Empty(passed);

        // The old chord now belongs to no command, so nothing takes it.
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal([-1L], steps);
        Assert.Equal(order[0], model.SelectedThreadID);
        Assert.Equal([Key.Tab], passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-WIN-021")] // App/AppCommands.swift:87-88; App/Window/ThreadWindow.swift:25-38
    [Trait("Decision", "ED-099")]
    public void In_the_main_window_one_tab_chord_recorded_for_both_directions_selects_the_next_thread() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        var store = FreshStore();
        using var shared = ShortcutStore.Shared.Override(store);
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);
        var model = new AppModel();
        var commands = new AppCommands(model);
        var window = Show(new ThreadWindow());
        var steps = new List<long>();
        window.SelectThread = steps.Add;
        var passed = Passed(window);
        using var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyEquivalent);

        // Control-Tab, next thread's default, recorded for previous thread as well (set does not refuse, :300-302).
        store.Set(new KeyChord(0x0F, ChordModifiers.Control), AppShortcut.PreviousThread);
        Assert.Equal(store.Chord(AppShortcut.NextThread), store.Chord(AppShortcut.PreviousThread));

        // ThreadWindow.swift:28-35 tests next before previous.
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Equal([1L], steps);
        Assert.Null(model.SelectedThreadID);
        Assert.Empty(passed);
        window.Close();
    });

    // ===== UI-WIN-022: the run-script item =====

    [Fact]
    [Trait("Req", "UI-WIN-022")] // App/AppCommands.swift:89-94; App/AppModel.swift:1098-1101; Core/Models/Shortcuts.swift:176, :224
    public void The_run_script_item_is_the_sixth_thread_command_on_ctrl_r_titled_Run_and_the_scripts_own_name_verbatim()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var scripted = AppModelSamples.Project("Scripted", h.NewFolder("scripted")) with
        {
            Scripts = new ValueList<ProjectScript>([new ProjectScript("build", "Write-Output built"), new ProjectScript("Test", "Write-Output tested")]),
        };
        var plain = AppModelSamples.Project("Plain", h.NewFolder("plain"));
        var scriptedThread = AppModelSamples.Thread(scripted);
        var plainThread = AppModelSamples.Thread(plain);
        var model = Model(h, [scripted, plain], [scriptedThread, plainThread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // AppModel.swift:1099 finds the top script only through the selected thread, so with nothing selected there
            // is none, whatever scripts a project holds, and :94 disables the item. (The record UI-WIN-022 lists "enabled
            // with a script even when no thread is selected"; the Swift gives no such state. Reported for correction.)
            (Guid? Selected, string Title, bool Disabled)[] cases =
            [
                (null, "Run top script", true),
                (plainThread.Id, "Run top script", true),
                (scriptedThread.Id, "Run build", false), // :89 "Run " and the first script's name, case kept
            ];
            foreach (var (selected, title, disabled) in cases)
            {
                model.SelectedThreadID = selected;
                var item = ThreadMenu(commands.Body)[5];
                Assert.Equal(title, item.Title);
                Assert.Equal(CtrlR, item.Shortcut);
                Assert.Equal(disabled, item.IsDisabled);
            }

            Assert.Equal("Ctrl+R", ShortcutStore.Label(AppShortcut.RunScript)); // OD-0008 B7: Command-R
            Assert.Equal("Run top script", AppShortcut.RunScript.Title()); // Shortcuts.swift:176
        });
    }

    // ===== UI-WIN-023: Thread 1 to Thread 9 =====

    [Fact]
    [Trait("Req", "UI-WIN-023")] // App/AppCommands.swift:95-99; Core/Models/Shortcuts.swift:251-257, :320-334
    public void Thread_1_to_Thread_9_follow_the_run_script_item_on_ctrl_and_their_digit_never_disabled_and_never_rebindable()
    {
        using var h = new AppModelHarness();
        var store = h.Main.OnMain(FreshStore);
        using var shared = ShortcutStore.Shared.Override(store);
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var menu = ThreadMenu(commands.Body);
            // Items seven to fifteen. With no thread at all none is disabled, and each still takes its chord.
            for (var number = 1; number <= 9; number++)
            {
                var item = menu[5 + number];
                Assert.Equal("Thread " + number.ToString(System.Globalization.CultureInfo.InvariantCulture), item.Title); // :96
                Assert.Equal(CtrlDigit(number), item.Shortcut); // :97
                Assert.False(item.IsDisabled);
                Assert.True(commands.PerformKeyEquivalent(CtrlDigit(number)));
                Assert.Null(model.SelectedThreadID);
            }
            // :99 the divider after Thread 9 is drawn by no menu: the finish item comes next.
            Assert.Equal(CtrlShiftDelete, menu[15].Shortcut);

            // :97 hard-coded, outside the store: no command of the shortcut list stands for them.
            var listed = SwiftEnum<AppShortcut>.AllCases.Select(shortcut => shortcut.Title()).ToHashSet(StringComparer.Ordinal);
            for (var number = 1; number <= 9; number++)
                Assert.DoesNotContain("Thread " + number.ToString(System.Globalization.CultureInfo.InvariantCulture), listed);
            // Shortcuts.swift:255: the same nine chords are reserved, so the store refuses each of them to every command
            // (:329, in the OD-0008 B8 wording). ED-099 (2): the number row 1 to 9 is scan codes 0x02 to 0x0A.
            foreach (var code in new long[] { 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A })
                foreach (var shortcut in SwiftEnum<AppShortcut>.AllCases)
                    Assert.Equal("Windows or nth already uses this chord.", store.Refusal(new KeyChord(code, ChordModifiers.Control), shortcut));
        });
    }

    // ===== UI-WIN-024: the finish item =====

    [Fact]
    // Partial evidence for UI-WIN-024; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:99-102; App/AppModel.swift:855-858; App/AppSettings.swift:716; Core/Models/Shortcuts.swift:177, :225
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void The_finish_item_opens_the_last_block_on_ctrl_shift_delete_with_three_titles_and_needs_a_selection()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var open = AppModelSamples.Thread(project);
        var settled = AppModelSamples.Thread(project) with { IsSettled = true };
        var model = Model(h, [project], [open, settled]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // AppSettings.swift:716: settle unless Archive was chosen.
            Assert.Equal(ThreadFinishAction.Settle, model.Settings.ThreadFinishAction);
            // AppModel.swift:856-857: "Reopen thread" only with settle chosen and a settled thread selected.
            (ThreadFinishAction Action, Guid? Selected, string Title, bool Disabled)[] cases =
            [
                (ThreadFinishAction.Settle, null, "Settle thread", true),
                (ThreadFinishAction.Settle, open.Id, "Settle thread", false),
                (ThreadFinishAction.Settle, settled.Id, "Reopen thread", false),
                (ThreadFinishAction.Archive, null, "Archive thread", true),
                (ThreadFinishAction.Archive, open.Id, "Archive thread", false),
                (ThreadFinishAction.Archive, settled.Id, "Archive thread", false),
            ];
            foreach (var (action, selected, title, disabled) in cases)
            {
                model.Settings.ThreadFinishAction = action;
                model.SelectedThreadID = selected;
                var menu = ThreadMenu(commands.Body);
                Assert.Equal("Thread 9", menu[14].Title);
                Assert.Equal(title, menu[15].Title);
                Assert.Equal(CtrlShiftDelete, menu[15].Shortcut);
                Assert.Equal(disabled, menu[15].IsDisabled); // :102
            }

            // The shortcut settings list's "Finish thread" (Shortcuts.swift:177) is a fourth string, never the item's.
            Assert.Equal("Finish thread", AppShortcut.FinishThread.Title());
            Assert.Equal("Ctrl+Shift+Delete", ShortcutStore.Label(AppShortcut.FinishThread)); // OD-0008 B7: Shift-Command-Delete
        });
    }

    [Fact]
    // Partial evidence for UI-WIN-024; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:100-101; Core/Models/Shortcuts.swift:115-117, :259-265, :300-302
    [Trait("Req", "UI-WIN-025")] // App/AppCommands.swift:103-104; Core/Models/Shortcuts.swift:118-119, :259-265, :300-302
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void A_chord_recorded_for_the_finish_item_is_stored_under_archiveThread_and_one_for_the_archive_item_under_archiveThreadPrompt()
    {
        using var h = new AppModelHarness();
        var store = h.Main.OnMain(FreshStore);
        using var shared = ShortcutStore.Shared.Override(store);
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // Shortcuts.swift:117 finishThread = "archiveThread", its old name; :119 archiveThread = "archiveThreadPrompt".
            Assert.Equal("archiveThread", SwiftEnum<AppShortcut>.RawValue(AppShortcut.FinishThread));
            Assert.Equal("archiveThreadPrompt", SwiftEnum<AppShortcut>.RawValue(AppShortcut.ArchiveThread));

            // Ctrl+Alt with "[" (scan code 0x1A) for the finish item: stored under "archiveThread", taken by the item.
            var finish = new KeyChord(0x1A, ChordModifiers.Control | ChordModifiers.Alt);
            Assert.Null(store.Refusal(finish, AppShortcut.FinishThread));
            store.Set(finish, AppShortcut.FinishThread);
            AssertStoredChord(h, "archiveThread", 0x1A, 3);
            Assert.Equal(Chord("[", ChordModifiers.Control | ChordModifiers.Alt), ThreadMenu(commands.Body)[15].Shortcut);
            Assert.DoesNotContain(commands.Body, command => command.Shortcut == CtrlShiftDelete);

            // Back to its default, then Ctrl+Alt with "]" (0x1B) for the archive item: stored under "archiveThreadPrompt".
            store.Reset(AppShortcut.FinishThread);
            var archive = new KeyChord(0x1B, ChordModifiers.Control | ChordModifiers.Alt);
            Assert.Null(store.Refusal(archive, AppShortcut.ArchiveThread));
            store.Set(archive, AppShortcut.ArchiveThread);
            AssertStoredChord(h, "archiveThreadPrompt", 0x1B, 3);
            var menu = ThreadMenu(commands.Body);
            Assert.Equal(CtrlShiftDelete, menu[15].Shortcut);
            Assert.Equal(Chord("]", ChordModifiers.Control | ChordModifiers.Alt), menu[16].Shortcut);
            Assert.DoesNotContain(commands.Body, command => command.Shortcut == CtrlW);
        });
    }

    // ===== UI-WIN-025: Archive thread (with U+2026) =====

    [Fact]
    [Trait("Req", "UI-WIN-025")] // App/AppCommands.swift:103-105; App/AppModel.swift:902-912; Core/Models/Shortcuts.swift:178, :226
    public void Archive_thread_with_its_ellipsis_is_the_seventeenth_thread_command_on_ctrl_w_and_asks_for_the_selected_thread()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var item = ThreadMenu(commands.Body)[16];
            Assert.Equal("Archive thread\U00002026", item.Title); // U+2026 HORIZONTAL ELLIPSIS
            Assert.Equal(CtrlW, item.Shortcut);
            Assert.True(item.IsDisabled); // :105
            Assert.False(commands.PerformKeyEquivalent(CtrlW));

            model.SelectedThreadID = thread.Id;
            foreach (var action in new[] { ThreadFinishAction.Settle, ThreadFinishAction.Archive })
            {
                model.Settings.ThreadFinishAction = action;
                item = ThreadMenu(commands.Body)[16];
                Assert.Equal("Archive thread\U00002026", item.Title); // U+2026 HORIZONTAL ELLIPSIS
                Assert.False(item.IsDisabled);
            }

            // Something typed for the thread, so it is not the empty one askToArchiveSelectedThread deletes outright
            // (AppModel.swift:906-910): the chord raises the question on the thread's row (:911-912), nothing archived yet.
            model.Runtime(thread.Id).Draft = new ComposerDraft { Text = "keep" };
            Assert.True(commands.PerformKeyEquivalent(CtrlW));
            Assert.Equal(thread.Id, model.AppModelArchiveRequest?.ThreadID);
            Assert.False(model.Thread(thread.Id)!.IsArchived);

            Assert.Equal("Ctrl+W", ShortcutStore.Label(AppShortcut.ArchiveThread)); // OD-0008 B7: Command-W
            Assert.Equal("Archive thread", AppShortcut.ArchiveThread.Title()); // Shortcuts.swift:178, no ellipsis
        });
    }

    // ===== UI-WIN-026: Settle thread / Reopen thread =====

    [Fact]
    [Trait("Req", "UI-WIN-026")] // App/AppCommands.swift:106-109; Core/Models/Library.swift:143, :189; Core/Models/Shortcuts.swift:179, :227
    public void The_settle_item_ends_the_thread_menu_on_ctrl_s_titled_by_the_selection_alone_and_off_for_a_helper_or_nothing()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var open = AppModelSamples.Thread(project);
        var settled = AppModelSamples.Thread(project) with { IsSettled = true };
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = open.Id };
        var model = Model(h, [project], [open, settled, helper]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // :106 reads isSettled alone, whatever the finish setting; :108 maps no selection to true, so it is disabled.
            (ThreadFinishAction Action, Guid? Selected, string Title, bool Disabled)[] cases =
            [
                (ThreadFinishAction.Settle, null, "Settle thread", true),
                (ThreadFinishAction.Archive, null, "Settle thread", true),
                (ThreadFinishAction.Archive, open.Id, "Settle thread", false),
                (ThreadFinishAction.Archive, settled.Id, "Reopen thread", false),
                (ThreadFinishAction.Settle, settled.Id, "Reopen thread", false),
                (ThreadFinishAction.Settle, helper.Id, "Settle thread", true),
            ];
            foreach (var (action, selected, title, disabled) in cases)
            {
                model.Settings.ThreadFinishAction = action;
                model.SelectedThreadID = selected;
                var body = commands.Body;
                var start = ThreadMenuStart(body);
                var item = body[start + 17];
                Assert.Equal(title, item.Title);
                Assert.Equal(CtrlS, item.Shortcut);
                Assert.Equal(disabled, item.IsDisabled);
                // The eighteenth and last item of the menu: the help group's item follows it.
                Assert.Equal("Welcome tour", body[start + 18].Title);
            }

            Assert.Equal("Ctrl+S", ShortcutStore.Label(AppShortcut.SettleThread)); // OD-0008 B7: Command-S
            Assert.Equal("Settle thread", AppShortcut.SettleThread.Title()); // Shortcuts.swift:179
        });
    }

    // ===== UI-WIN-015, UI-WIN-017 to UI-WIN-022, UI-WIN-024 to UI-WIN-026: rebinding =====

    [Fact]
    [Trait("Req", "UI-WIN-015")] // App/AppCommands.swift:51; Core/Models/Shortcuts.swift:276-283, :300-302
    [Trait("Req", "UI-WIN-017")] // App/AppCommands.swift:67; Core/Models/Shortcuts.swift:276-283, :300-302
    // Partial evidence for UI-WIN-018; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:75; Core/Models/Shortcuts.swift:276-283, :300-302
    [Trait("Req", "UI-WIN-019")] // App/AppCommands.swift:82; Core/Models/Shortcuts.swift:276-283, :300-302
    [Trait("Req", "UI-WIN-020")] // App/AppCommands.swift:86; Core/Models/Shortcuts.swift:276-283, :300-302
    [Trait("Req", "UI-WIN-021")] // App/AppCommands.swift:88; Core/Models/Shortcuts.swift:276-283, :300-302
    [Trait("Req", "UI-WIN-022")] // App/AppCommands.swift:93; Core/Models/Shortcuts.swift:276-283, :300-302
    // Partial evidence for UI-WIN-024; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:101; Core/Models/Shortcuts.swift:276-283, :300-302
    [Trait("Req", "UI-WIN-025")] // App/AppCommands.swift:104; Core/Models/Shortcuts.swift:276-283, :300-302
    [Trait("Req", "UI-WIN-026")] // App/AppCommands.swift:107; Core/Models/Shortcuts.swift:276-283, :300-302
    public void Each_rebindable_item_takes_the_chord_recorded_for_its_command_and_gives_up_its_default()
    {
        using var h = new AppModelHarness();
        var store = h.Main.OnMain(FreshStore);
        using var shared = ShortcutStore.Shared.Override(store);
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            var body = commands.Body;
            var activity = body.Select(command => command.Title).ToList().IndexOf("Toggle activity view");
            var start = ThreadMenuStart(body);
            // Each item's place, its command, its default key equivalent, and a free Ctrl+Alt chord to record for it
            // (ED-099 (2) scan codes: F 0x21, G 0x22, E 0x12, I 0x17, Y 0x15, H 0x23, L 0x26, [ 0x1A, ] 0x1B, ; 0x27; T is the pane workspace's Ctrl+Alt+T since web W3b).
            (int Index, AppShortcut Shortcut, KeyboardShortcut Default, long Code, string Key)[] rows =
            [
                (activity, AppShortcut.ToggleActivityView, CtrlAltU, 0x21, "f"),
                (start, AppShortcut.StopTurn, CtrlPeriod, 0x22, "g"),
                (start + 1, AppShortcut.QueueChat, CtrlReturn, 0x12, "e"),
                (start + 2, AppShortcut.TogglePlanMode, CtrlShiftP, 0x17, "i"),
                (start + 3, AppShortcut.PreviousThread, CtrlShiftTab, 0x15, "y"),
                (start + 4, AppShortcut.NextThread, CtrlTab, 0x23, "h"),
                (start + 5, AppShortcut.RunScript, CtrlR, 0x26, "l"),
                (start + 15, AppShortcut.FinishThread, CtrlShiftDelete, 0x1A, "["),
                (start + 16, AppShortcut.ArchiveThread, CtrlW, 0x1B, "]"),
                (start + 17, AppShortcut.SettleThread, CtrlS, 0x27, ";"),
            ];
            foreach (var row in rows)
            {
                Assert.Equal(row.Default, commands.Body[row.Index].Shortcut);
                var chord = new KeyChord(row.Code, ChordModifiers.Control | ChordModifiers.Alt);
                Assert.Null(store.Refusal(chord, row.Shortcut));
                store.Set(chord, row.Shortcut);
                Assert.Equal(Chord(row.Key, ChordModifiers.Control | ChordModifiers.Alt), commands.Body[row.Index].Shortcut);
                Assert.DoesNotContain(commands.Body, command => command.Shortcut == row.Default);
            }

            // A chord cleared in the store leaves the item with none (Shortcuts.swift:277, :282), its title unchanged.
            store.Set(null, AppShortcut.StopTurn);
            Assert.Null(commands.Body[start].Shortcut);
            Assert.Equal("Stop", commands.Body[start].Title);
        });
    }

    // ===== UI-WIN-027: Welcome tour =====

    [Fact]
    // Partial evidence for UI-WIN-027; one clause of the record (its STOPs are closed; the whole record is proved with the Blitz pass tests, see the end of this file). // App/AppCommands.swift:111-113; App/Views/Tour/TourPages.swift:26-28
    [Trait("Rule", "R-TEST-1")] // partial evidence only; see the record-specific STOP ledger
    public void Welcome_tour_follows_the_thread_menu_alone_with_no_chord_never_disabled_and_presents_the_tour() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);
        var commands = h.Main.OnMain(() => new AppCommands(model));

        h.Main.OnMain(() =>
        {
            foreach (var selected in new Guid?[] { null, thread.Id })
            {
                model.SelectedThreadID = selected;
                var body = commands.Body;
                // CommandGroup(replacing: .help) (:111) holds this one item, after the Thread menu's last item.
                Assert.Equal(ThreadMenuStart(body) + 18, body.Count - 1);
                Assert.Equal("Welcome tour", body[^1].Title);
                Assert.Null(body[^1].Shortcut); // :112 no .keyboardShortcut
                Assert.False(body[^1].IsDisabled); // :112 no .disabled
            }
        });

        // :112 Tour.present(model:): only a tour presented for this model marks it seen again when it closes.
        h.Main.OnMain(() => model.Settings.HasSeenTour = true);
        h.Main.OnMain(() => commands.Body[^1].Action());
        h.Main.OnMain(() => model.Settings.HasSeenTour = false);
        h.Main.OnMain(() =>
        {
            // TourWindowController.swift:52-63 runs the caller's onClose only through TourView's onClose (the checkmark control); :139-143 close() only clears the retained window/delegate, so controller Close() is not the user-close path.
            var window = TourWindowController.Shared.Present(TourPages.Pages);
            var view = Assert.IsType<TourView>(window.Content);
            var checkmark = view.GetVisualDescendants().OfType<Button>().Single(button =>
                button.GetVisualDescendants().OfType<SwiftImage>().Any(image => image.SymbolName == "checkmark"));
            checkmark.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(h.Main.OnMain(() => model.Settings.HasSeenTour));
    });

    // ===== UI-WIN-074 to UI-WIN-077: the zoom items =====

    [Fact]
    [Trait("Req", "UI-WIN-074")] // App/AppCommands.swift:52-54
    [Trait("Req", "UI-WIN-075")] // App/AppCommands.swift:55-56
    [Trait("Req", "UI-WIN-076")] // App/AppCommands.swift:57-58
    [Trait("Req", "UI-WIN-077")] // App/AppCommands.swift:59-63
    public void The_four_zoom_items_follow_the_activity_toggle_in_order_on_their_literal_chords_and_are_never_disabled()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var model = Model(h, [project], [thread]);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // At the bottom level, at the default and at the top level, with and without a selection.
            foreach (var level in new long[] { 75, 100, 200 })
            {
                foreach (var selected in new Guid?[] { null, thread.Id })
                {
                    model.Settings.AppZoom = level;
                    model.SelectedThreadID = selected;
                    var body = commands.Body;
                    var first = body.Select(command => command.Title).ToList().IndexOf("Toggle activity view") + 1;
                    // :53, :55, :57, :60 in Title Case, the last one hidden (:62) and so the group's last element: the
                    // Thread menu (:65) comes right after it.
                    Assert.Equal(["Zoom In", "Zoom Out", "Actual Size", "Zoom In"], body.Skip(first).Take(4).Select(command => command.Title));
                    Assert.Equal(new KeyboardShortcut?[] { CtrlPlus, CtrlMinus, Ctrl0, CtrlEquals }, body.Skip(first).Take(4).Select(command => command.Shortcut));
                    Assert.All(body.Skip(first).Take(4), command => Assert.False(command.IsDisabled));
                    Assert.Equal(first + 4, ThreadMenuStart(body));
                }
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-074")] // App/AppCommands.swift:53-54; UI/Common/AppZoom.swift:6, :12-15
    [Trait("Req", "UI-WIN-077")] // App/AppCommands.swift:59-62; UI/Common/AppZoom.swift:6, :12-15
    // Both items are driven by their chords as the port holds them, Ctrl with "+" and Ctrl with "=" (the records: "only
    // the chord remains, Ctrl with the + key equivalent"). Which Windows key event is the "+" key equivalent is the open
    // question STOP App.AppCommands #2 (note at the end of this file); Ctrl with the "=" key is tested through a window.
    public void Both_Zoom_In_items_step_the_zoom_up_one_level_and_at_200_leave_it_there()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // AppZoom.swift:6 levels [75, 90, 100, 110, 125, 150, 175, 200]; next(increasing: true) takes the first above.
            foreach (var chord in new[] { CtrlPlus, CtrlEquals })
            {
                model.Settings.AppZoom = 75;
                foreach (var level in new long[] { 90, 100, 110, 125, 150, 175, 200, 200 })
                {
                    Assert.True(commands.PerformKeyEquivalent(chord));
                    Assert.Equal(level, model.Settings.AppZoom);
                }
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-075")] // App/AppCommands.swift:55-56; UI/Common/AppZoom.swift:6, :12-15
    public void Zoom_Out_steps_the_zoom_down_one_level_and_at_75_leaves_it_there()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // next(increasing: false) takes the last level below.
            model.Settings.AppZoom = 200;
            foreach (var level in new long[] { 175, 150, 125, 110, 100, 90, 75, 75 })
            {
                Assert.True(commands.PerformKeyEquivalent(CtrlMinus));
                Assert.Equal(level, model.Settings.AppZoom);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-076")] // App/AppCommands.swift:57-58; Core/Models/Shortcuts.swift:233 (OD-0012 G6)
    public void Actual_Size_alone_holds_ctrl_0_and_sets_the_zoom_to_100()
    {
        using var h = new AppModelHarness();
        using var shared = ShortcutStore.Shared.Override(h.Main.OnMain(FreshStore));
        var model = Model(h, [], []);

        h.Main.OnMain(() =>
        {
            var commands = new AppCommands(model);
            // OD-0012 G6: "Show Marrow" (showMainWindow) keeps no default chord, so nothing else answers Ctrl+0.
            Assert.Null(ShortcutStore.Shared.Current.Chord(AppShortcut.ShowMainWindow));
            Assert.Equal(["Actual Size"], commands.Body.Where(command => command.Shortcut == Ctrl0).Select(command => command.Title));
            foreach (var level in new long[] { 75, 90, 175, 200 })
            {
                model.Settings.AppZoom = level;
                Assert.True(commands.PerformKeyEquivalent(Ctrl0));
                Assert.Equal(100, model.Settings.AppZoom); // :57
            }
        });
    }
}

// STOP App.AppCommands #1 — RESOLVED by sweep B (2026-09-27).
// Swift: App/AppCommands.swift:12-15,21-25 (B3 52bc792).
// Records: PR-WIN-090, PR-WIN-092, UI-LAUNCH-007, UI-WIN-006, UI-WIN-007.
// WindowManager.Shared, ShowSettings and ShowMain landed before sweep base d62329d42.
// Both entries now appear first, in source order, never disabled; tests above prove their actions and rebinding.

// STOP App.AppCommands #2 (build agent, part 2, 2026-09-26)
// Kind: ambiguity
// Swift: DroppyCode/App/AppCommands.swift:53-62 (B3 52bc792), the four zoom items with literal key equivalents:
//   .keyboardShortcut("+", modifiers: .command) (:54), "-" (:56), "0" (:58), and after the comment
//   "US keyboards produce '=' for Command-plus without Shift." (:59) the hidden item on "=" (:61).
// Requirements: PR-WIN-222 (the key route to "+"); PR-WIN-223, PR-WIN-224, PR-WIN-225 beyond a US layout.
// What is unclear or missing: the spec calls each zoom chord "a character key equivalent, not a US key position"
// (PR-WIN-222 to PR-WIN-224 windows_path) and says Ctrl+= beside Ctrl with + lets zoom-in work "whether or not Shift is
// held on a US layout" (PR-WIN-225). The port's dispatch (Port/MenuKeyEquivalents.cs, ED-099) reads a key event as its
// US key position plus the modifiers held. On a US layout that gives "=", "-" and "0" for Ctrl with those keys (tested
// above through a window), but no key event gives "+": Ctrl+Shift+= reads as ("=", Ctrl+Shift), which no command holds.
// No decision says how a character key equivalent is recognised from a Windows key event. Avalonia's
// KeyEventArgs.KeySymbol cannot serve as it stands: Avalonia.Win32 12.1.2 fills it from ToUnicodeEx over the live
// keyboard state, Ctrl included (Avalonia.Win32.Input.KeyInterop.GetKeySymbol), so with Ctrl held it is null or a
// control character.
// Readings or options: (a) US position with Shift: "+" is the "=" key (scan code 0x0D) with Shift, as :59 says of US
// keyboards -> Ctrl+Shift+= zooms in at that position on every layout, the numpad + does nothing, "-", "0", "=" stay
// positions; (b) the character the key types in the active layout with Ctrl ignored and Shift kept (AppKit's
// charactersIgnoringModifiers), through a port-only ToUnicodeEx over a state without Ctrl -> Ctrl with whatever key
// types "+", "-", "0" or "=" in the user's layout, numpad keys included, and different keys from (a) on non-US layouts;
// needs rules for dead keys and AltGr (which Windows reports as Ctrl+Alt); (c) Windows virtual keys as Avalonia's
// KeyGesture reads them (VK_OEM_PLUS, VK_OEM_MINUS, VK_0, numpad Add and Subtract) -> on a US layout the unshifted "="
// key is VK_OEM_PLUS, so "+" and "=" would both answer Ctrl+=, and Shift needs its own rule.
// Needed to proceed: a decision (ED) choosing the reading, and for (b) its dead-key and AltGr handling.
// Done so far: the four entries are in AppCommands.Body in source order with their literal key equivalents, never
// disabled, and their actions are tested through PerformKeyEquivalent (green); Ctrl+=, Ctrl+- and Ctrl+0 of a US
// keyboard are tested through a window (green); nothing is written for a key event reaching "+".

// STOP App.AppCommands #3 (build agent, part 2, 2026-09-26)
// Kind: conflict
// Swift: DroppyCode/App/AppModel.swift:855-921 (B3 52bc792): finishActionTitle (:855-858), finishSelectedThread
// (:865-882), settleSelectedThread (:887-898), askToArchiveSelectedThread (:902-921), which AppCommands.swift:100-106
// calls.
// Requirements: PR-WIN-112 (the reopen action; the title for a helper), PR-WIN-113 (the empty-thread path and the
// 80 ms fallback), PR-WIN-114 (the 80 ms fallback).
// What is unclear or missing: nothing in AppCommands; the dependency differs from the Swift. In
// src/Marrow.App/AppModel.cs (unit App.AppModel): (1) FinishActionTitle and FinishActionSymbol answer "Archive thread"
// and "archivebox" for a helper when Settle is chosen, a branch marked "GD-005", an id no decision record holds; Swift
// :855-863 has no helper case, so a settled helper reads "Reopen thread" and an unsettled one "Settle thread". The unit's
// own test AppModelMoveThreadAndFinishActionTests.Finish_action_archives_a_helper_even_when_settle_is_the_action
// expects the port's title. (2) FinishSelectedThread with Settle chosen calls SettleAnimated at once; Swift :871-880
// posts FinishRequest(threadID:, reopens: thread.isSettled) and settles or reopens after 80 ms only if no row took it
// up, so for a settled thread the finish chord does nothing in the port (Settle returns early) where Swift reopens it;
// the archive branch also lacks withAnimation(Chrome.panelSlide) (:870). (3) SettleSelectedThread lacks the helper
// guard (:888) and the 80 ms fallback (:891-897). (4) AskToArchiveSelectedThread lacks the delete of an empty loaded
// thread (:906-910) and the 80 ms fallback archive (:913-920).
// Readings or options: none; the Swift reads one way.
// Needed to proceed: a repair of unit App.AppModel against AppModel.swift:855-921 (B3), including its test's
// expectation for the helper's title.
// Done so far: the three entries call those members exactly as :100-108 do and are tested where the port's AppModel
// matches the Swift (titles for threads that are not helpers, both disabled rules, Archive chosen, Settle chosen on an
// unsettled thread, a helper archived, the settle and archive requests), all green. Not written, because they would
// fail on the dependency rather than on this unit: the finish chord with Settle chosen on a settled thread (Swift:
// finishRequest is FinishRequest(reopens: true) at once and the thread is reopened 80 ms later), and the finish item's
// title for a helper with Settle chosen ("Settle thread" unsettled, "Reopen thread" settled).
// Part 3 (2026-09-26): reading AppCommands.body with a selection makes the thread's runtime (:7-9, :68;
// AppModel.swift:434-439), so whenever the archive chord fires the selected thread has one, and a thread with
// nothing in it is deleted with no question (AppModel.swift:906-910), which item (4) lacks. The part 2 archive tests
// asserted the question for such an empty thread; they now give the thread a draft, so the Swift asks too. Not
// written, for the same reason as above: the archive chord on an empty selected thread (Swift: the thread is
// deleted, no archive request). UI-WIN-025 is tested with a draft as well.

// STOP App.AppCommands #4 (build agent, part 2, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/AppCommands.swift:111-113 (B3 52bc792)
//   CommandGroup(replacing: .help) {
//       Button("Welcome tour") { Tour.present(model: model) }
// Requirements: PR-WIN-115; the command-palette half of UI-UPDATE-055
// What is unclear or missing: OD-0012 G5 moves this command into the command palette as an action titled "Welcome
// tour" that presents the tour. That row belongs to App/Views/Palette/CommandPalette.swift:153-178, whose twin
// src/Marrow.App/Views/Palette/CommandPalette.cs has landed but contains no Welcome tour row; PR-WIN-115 leaves the row's
// place among the actions (before or after Settings, which the source appends last), its symbol (every palette Item
// needs one, CommandPalette.swift:28) and its id unset.
// Readings or options: none to choose here; the row is outside this file.
// Needed to proceed: a decision naming the row's place, symbol and id; then the palette owner adds the row.
// Done so far: the help group's command itself is in AppCommands.Body, tested in part 1 (PR-UPDATE-092) and above for
// UI-UPDATE-055 (last, alone, no chord, never disabled), green; nothing is written for the palette row.
// Part 3 (2026-09-26): the palette halves of UI-WIN-015 to UI-WIN-027 and UI-WIN-074 to UI-WIN-077 are rows of the
// same CommandPalette.swift:153-178, not of this file: "no palette row" for every item but two, the palette's finish
// row with the same three titles (UI-WIN-024) and the "Welcome tour" row (UI-WIN-027, as PR-WIN-115). Nothing is
// tested for them here; the items themselves are tested above (green).

// STOP App.AppCommands #5 - RESOLVED by fix/canqueue (2026-09-27); first raised by the build agent, part 3, 2026-09-26.
// Kind: conflict
// Swift: DroppyCode/App/Runtime/ThreadRuntime.swift:771-788 (B3 52bc792), which AppCommands.swift:74 calls:
//   var canQueue: Bool { isRunning || hasWorkingHeads }
//   func queueDraftAsFollowUp() { guard !isReverting, !draft.isEmpty else { return }
//       guard canQueue else { send(); return } ... }
// Requirements: UI-WIN-018 ("or simply sends it when there is nothing to queue behind"), PR-WIN-105 (the same case)
// What is unclear or missing: nothing in AppCommands; the dependency differs from the Swift. In
// src/Marrow.App/Runtime/ThreadRuntime.cs (unit App.Runtime.ThreadRuntime), QueueDraftAsFollowUp returns at once when
// CanQueue is false, and CanQueue is "phase is not idle, not reverting, the draft not empty", where Swift :771-773 is
// isRunning || hasWorkingHeads and :782-785 sends the draft when it cannot queue. So with a draft and no running turn
// the item is on (:76-77, tested above) and takes Ctrl+Return, but the port then does nothing, where Swift sends the
// draft ("the queue chord is never a dead key", ThreadRuntime.swift:778-779; AppCommands.swift:71-73).
// Readings or options: none; the Swift reads one way.
// Needed to proceed: a repair of unit App.Runtime.ThreadRuntime against ThreadRuntime.swift:771-788 (B3).
// Done so far: the item, its chord and its three disabled rules are tested (green), and the queueing case is tested
// in part 1 (green). Not written, because it would fail on the dependency rather than on this unit: Ctrl+Return with
// a draft such as "/plan" on a selected idle thread (Swift: send() handles it locally, ThreadRuntime.swift:743-750,
// :1157-1162, so the thread flips to plan and the draft is emptied; no follow-up is queued).
// Resolution (fix/canqueue): ThreadRuntime.CanQueue is now IsRunning || HasWorkingHeads and QueueDraftAsFollowUp
// sends the draft when it cannot queue (ThreadRuntime.swift:771-788). The case above is now written:
// Queue_a_chat_with_nothing_to_queue_behind_sends_the_draft_so_a_plan_command_flips_the_thread_to_plan (green).
// fix/canqueue round 2: enqueueFollowUp now ends with flushFollowUpsIfIdle (ThreadRuntime.swift:1003-1007), so a draft
// this item queues on an idle lead with heads at work goes out at once as its own turn (ThreadRuntimeQueueDraftTests).

// STOP App.AppCommands #6 (repair agent, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/DroppyCodeApp.swift:21-25 (B3 52bc792), the one install site of AppCommands.swift:11-114:
//   Settings { EmptyView() }
//       .commands { AppCommands(model: WindowManager.shared.model) }
// with the comment (:22) "The windows themselves are AppKit windows owned by WindowManager. This scene only carries
// the menus." The menu bar :24 installs answers every key equivalent pressed while any window of the app is key.
// Requirements: PR-WIN-089 (windows_path: "every chord works with no menu present"), PR-WIN-110 (Ctrl+1 to Ctrl+9
// select a thread while another window, such as Settings, is key, "through the port's own dispatch of the chord");
// Also explicitly blocked: PR-WIN-090, PR-WIN-092, UI-LAUNCH-007, UI-WIN-006 and UI-WIN-007.
// Through them, the chord of every item of this file in the running app.
// What is unclear or missing: WindowManager.Shared, Model and MakeWindow are now implemented, and Port/AppShell.cs
// hosts them. No production caller attaches MenuKeyEquivalents.Attach to any window (search of src at d62329d42).
// The remaining blocker is the ED naming the replacement for DroppyCodeApp.swift:24, its owning unit and coverage
// (main, Settings, tour, floating guide). WindowManager.cs is outside this sweep's write scope.
// Readings or options: (a) makeWindow's port (WindowManager.swift:86-110), the one factory of the main and Settings
// windows, attaches the dispatch to each window it makes -> every chord works in both; the tour window and the guide
// panel need their own attach; (b) one port-only hook attaches it to every window the app opens -> every window of
// the app, the tour window and the guide panel included, answers the chords.
// Needed to proceed: a decision naming the runtime chord install site, its owning unit and the windows it covers.
// Done so far: MenuKeyEquivalents.Attach and AppCommands.PerformKeyEquivalent are implemented and tested on the
// tests' own windows (green); nothing attaches them to a window of the running app.
// Fixes: PR-WIN-090 now also has a real palette-row action test; PR-WIN-092 has a Settings-focused
// window test with test-owned dispatch. Both were writable; production attachment remains missing.
// Resolved (fix/shortcuts, 2026-09-27, base 6afbddb9f), reading (a) with the tour window added: WindowManager.MakeWindow
// attaches the dispatch to the main and Settings windows it makes, and TourWindowController.Present to the tour window
// (TourWindowController.swift:8-11 canBecomeKey, :110 makeKeyAndOrderFront). The floating guide panels never take
// activation in the port (FloatingGuidePanel.cs, ED-063), so they are never key and need no attach. Tested above
// through the production windows with a real key-down (the three tests under "STOP #6"), green. No ED records the
// install site yet; the orchestrator is asked to record one.

// STOP App.AppCommands #7 (repair agent, 2026-09-26)
// Kind: platform gap
// Swift: DroppyCode/Core/Models/Shortcuts.swift:232 (B3 52bc792)
//   case .toggleActivityView: KeyChord(keyCode: 32, modifiers: [.option, .command])
// and DroppyCode/App/AppCommands.swift:48-51 (B3 52bc792), the item that carries it. Every other item a user may
// rebind takes its chord from the same store (AppCommands.swift:14-107, shortcuts.keyboardShortcut(for:)).
// Requirements: PR-WIN-101 and UI-WIN-015 (the "Toggle activity view" item, its default chord Ctrl+Alt+U), PR-WIN-089
// (every chord of the table, a Ctrl+Alt chord the user records included)
// What is unclear or missing: upstream this item's default chord holds Command (Shortcuts.swift:232), which never
// types a character on a Mac, so typing U with Option (the Mac's character modifier) never runs it; only a chord a
// user records with Option and no Command can meet typing there (the recorder refuses only a typing key with no
// modifier but Shift, Shortcuts.swift:325-326). The port reads a key-down's modifiers as ED-099 (3) says, Control |
// Alt | Shift with Win masked out
// (Port/MenuKeyEquivalents.cs Shortcut), and Avalonia.Win32 12.1.2 fills them from the keyboard state: Alt from
// VK_LMENU or VK_RMENU, Control from VK_LCONTROL or VK_RCONTROL (Avalonia.Win32.Input.WindowsKeyboardDevice.Modifiers,
// decompiled). AltGr, the Windows character modifier, raises LCtrl and RAlt together, so AltGr with the U key reaches
// the dispatch as ("u", Ctrl+Alt), toggleActivityView's default (ED-050 (a), ED-099 (3)). The dispatch runs the item
// and consumes the event in the window's tunnel stage, and Avalonia.Win32 then drops the WM_CHAR of a handled
// WM_KEYDOWN (WindowImpl: _ignoreWmChar = e.Handled), so on a layout where AltGr with U types a character (the review
// names Polish programmers and Hungarian, where it is the euro sign; not checked here) typing that character flips the
// sidebar's activity view and the character is lost. Any Ctrl+Alt chord a user records collides the same way with the
// character AltGr gives that key in the user's layout. No decision covers AltGr: ED-050 and ED-099 map Option to Alt
// and say nothing of it, and STOP #2 names it only for the zoom chords' reading (b).
// Readings or options: (a) ED-099 (3) as written, the port as it stands: every key-down with Ctrl and Alt held is a
// chord, AltGr included -> AltGr characters on the chord keys are swallowed and run the command; (b) a key-down with
// AltGr held (right Alt on a layout that has AltGr) is never a chord -> AltGr always types; on such layouts a Ctrl+Alt
// chord works with the left Alt only; needs a port-only read of which Alt is down and whether the layout has AltGr,
// which Avalonia's KeyModifiers does not carry; (c) an AltGr key-down is a chord only when AltGr with that key types
// nothing in the active layout -> AltGr characters type, AltGr with a key that types nothing still runs the command;
// needs a port-only ToUnicodeEx over the key state, with the dead-key rules STOP #2 (b) also needs.
// Needed to proceed: a decision (ED or owner) choosing the reading, and for (b) or (c) the port-only keyboard read.
// Done so far: the dispatch follows ED-099 (3) as recorded (Ctrl+Alt+U runs "Toggle activity view" through
// PerformKeyEquivalent, and a Ctrl+Alt key-down reaches the dispatch through a window, both tested above, green);
// nothing is written to tell AltGr from Ctrl+Alt, and no test is written for it: the headless platform carries no
// AltGr, and the expectation rests on the reading this item asks for.

// Sweep B recheck (2026-09-27, base d62329d42): STOP #2 and #7 still lack character-key / AltGr decisions.
// STOP #3 still needs AppModel.FinishActionTitle's helper branch repaired, delayed finish/reopen and archive fallback,
// and empty loaded-thread deletion (AppModel.swift:855-921). AppModel.cs:850-899 still has all four divergences.
// STOP #5 still needed ThreadRuntime.CanQueue and QueueDraftAsFollowUp repaired at that base: :1020-1025 rejected an
// idle draft instead of sending it (Swift ThreadRuntime.swift:771-788). No AppModel or ThreadRuntime file is changed by
// this sweep. Later note (fix/canqueue, 2026-09-27): STOP #5 is RESOLVED; see its resolution above.

// STOP App.AppCommands #8 (opencode citation correction, 2026-09-27)
// Kind: unresolved precedence, macOS menu key-equivalent dispatch.
// Swift: App/AppCommands.swift:21-25,53-62,95-98; Core/Models/Shortcuts.swift:251-257,320-334 (B3 52bc792).
// Records: PR-WIN-092, PR-WIN-224, PR-WIN-226; PR-SETTINGS-210 failure_cases; UI-WIN-007.
// PR-WIN-224 and PR-WIN-226 track dispatch under UNK-0003 (depends_on_unknown), although UNK-0003's
// prose scopes only appearance. The orchestrator should widen UNK-0003 or file a runtime-dispatch unknown.
// OD-0012 G6 settles the default only: Ctrl+0 is Actual Size and Show Marrow has no default chord.
// OD-0008 B8 keeps Ctrl+1..9 reserved, so the recorder refuses Ctrl+digit; the digit overlap in the test
// above is reachable only through a direct ShortcutStore.Set, not through the recorder.
// Still missing: a ruling on Windows precedence or refusal for Ctrl+0, Ctrl+- or Ctrl+= recorded for
// Show Marrow. Current precedence: Body[1] app-name/show-main wins because dispatch takes the first
// enabled match; Actual Size and Zoom do not run. The untagged test documents this without claiming parity.
// Needed: a Windows precedence/refusal ruling, or observation of the pinned macOS build under UNK-0003's
// runtime resolution path (blocked on access, OD-0001). Do not silently choose a rule.

// STOP opencode-App.AppCommands #2 recheck (2026-09-27): also UI-WIN-074 and UI-WIN-077; no later
// docs/b3 ruling chooses literal-character zoom matching. Exact site: MenuKeyEquivalents.Shortcut.
// STOP opencode-App.AppCommands #3 recheck: also UI-WIN-024 and UI-WIN-025; AppModel still lacks
// the helper title, finish/reopen and 80 ms fallback paths at FinishActionTitle/FinishSelectedThread,
// SettleSelectedThread and AskToArchiveSelectedThread. Dependency repair has not landed on impl/b2.
// STOP opencode-App.AppCommands #4 recheck: PR-UPDATE-092 and PR-WIN-089 also need the Welcome tour
// palette row, as do PR-WIN-115, UI-UPDATE-055 and UI-WIN-027. Exact site: CommandPalette.BuildResults
// after the Settings action. docs/b3 PR-WIN-115 still asks for the row position, symbol and id.
// STOP opencode-App.AppCommands #5 recheck: fix/canqueue exists separately but is not on impl/b2;
// ThreadRuntime.QueueDraftAsFollowUp still returns when !CanQueue instead of sending the idle draft.
// PR-WIN-105 and UI-WIN-018 remain STOP. The approval-card end-to-end precedence half is not proved here.
// STOP #6 is closed by ED-226 on docs/b3 (batch 17); the earlier missing-install-site prose above is historical.
// STOP opencode-App.AppCommands #7 recheck: ED-226 explicitly leaves the AltGr typing question open;
// PR-WIN-101 and UI-WIN-015 remain STOP at MenuKeyEquivalents.Shortcut (Ctrl+Alt vs AltGr).

// Finishing pass (AppCommandsFinishTests.cs and the retagged tests above; base c639bab13).
// - STOP #3 is resolved: AppModel now holds the 80 ms fallbacks and the empty-thread delete (AppModel.cs:872-921), so
//   PR-WIN-113, PR-WIN-114 and UI-WIN-025 carry Req traits, with the chord carried out to its end in AppCommandsFinishTests.
// - STOP #2 is resolved by ED-230 (2026-10-02): Shift with the "=" key, and the keypad +, read as the "+" key equivalent
//   when the chord itself takes no command (AppCommandsFinishTests.Ctrl_Shift_equals_and_Ctrl_keypad_plus_...).
// - STOP #4 is resolved by OD-0053 (2026-10-02): the "Welcome tour" palette row follows Settings, symbol sparkles, id
//   welcome-tour, no chord; CommandPaletteTests carries PR-WIN-115, PR-UPDATE-092, UI-UPDATE-055 and UI-WIN-027.
// - STOP #7 (AltGr) and #8 (overlapping chords, UNK-0003) are unchanged; neither is part of the stated result of PR-WIN-101
//   or PR-WIN-092.

// Blitz pass (unit appcommands, 2026-10-04; AppCommandsZoomTests, AppCommandsFinishTests and CommandPaletteTests).
// - STOP #2 is built, not just decided: ED-270 (the zoom chords are key equivalents of the character typed) replaces
//   ED-230's position reading of "+". The four zoom items are marked MatchesTypedCharacter and matched by
//   MenuKeyEquivalents.Typed over the active layout (Port/KeyboardLayoutText.cs); AppCommandsZoomTests presses German, US and
//   French layouts through the production dispatch. The headless session's layout is ANSI US (Fixtures/KeyboardLayouts.cs).
// - STOP #3, #4, #5 and #6: closed (AppModel.cs:920-1003, OD-0053, ThreadRuntime.cs:1026-1038, WindowManager.cs and
//   TourWindowController.cs attach the dispatch). The earlier "STOP opencode" recheck lines and the "Sweep B recheck" above
//   are historical and superseded by this pass and the Finishing pass.
// - PR-WIN-105 and UI-WIN-018: AppCommandsFinishTests.With_an_approval_waiting_Ctrl_Return_approves_through_the_card_... presses
//   Ctrl+Return in a window holding a real ApprovalCard and a focused composer, through the production dispatch: the card
//   approves, nothing is queued; with the approval gone the same chord queues the draft.
// - PR-WIN-112, PR-WIN-114 and UI-WIN-024: CommandPaletteTests.The_finish_row_reads_... (literal titles, symbols, chord, live
//   retitle, the click's FinishRequest, no settle row) and AppCommandsFinishTests.The_finish_chord_reopens_a_settled_thread_...
// - PR-UPDATE-092, PR-WIN-115, UI-UPDATE-055 and UI-WIN-027: CommandPaletteTests.Choosing_the_welcome_tour_row_with_the_tour_open_...
// - PR-WIN-092, PR-WIN-224 and UI-WIN-007: AppCommandsFinishTests.Ctrl_0_pressed_in_the_focused_Settings_window_...
// - STOP #7 (AltGr) and #8 (overlapping chords, UNK-0003) are still open and are not clauses of these records.

// Blitz review round (unit appcommands, 2026-10-04, after the adversarial review).
// - PR-WIN-222, PR-WIN-224, PR-WIN-225 and UI-WIN-074 (the Shift half of the "+" key equivalent, French Shift with the 0 key):
//   AppCommandsZoomTests.Read_by_Windows_Ctrl_Shift_equals_and_Ctrl_equals_zoom_in_... presses the chords in the production main
//   and Settings windows over the production reader (Port/KeyboardLayoutText.cs) bound to the real US and French layouts
//   (Fixtures/WindowsLayoutReaders.cs, ED-283 item 7). The mutant "the reader ignores Shift" (which every table-layout
//   test let live) fails it, AppCommandsZoomTests.The_Windows_reader_over_the_US_German_and_French_layouts_... and
//   AppCommandsReviewTests.REVIEW_the_Windows_layout_reader_applies_Shift.
// - PR-WIN-115: CommandPaletteTests.The_welcome_tour_row_follows_settings_... now reads the row's id (welcome-tour, OD-0053); the
//   mutant that renames the id fails it and AppCommandsReviewTests.REVIEW_the_welcome_tour_row_has_the_id_welcome_tour.
// - French AZERTY by window: the main window is a ThreadWindow, whose digit monitor (ThreadWindow.swift:11-23) takes Ctrl with the
//   key at the US "6" position (the key that types "-" there) before the menu, so Ctrl with it selects Thread 6 and does not zoom
//   out; in the Settings and tour windows it reaches Zoom Out (:55-56) before Thread 6 (:95-98), so Thread 6 is not reachable
//   there on that layout. ED-283 item 5 and the French test's comment said Zoom Out in every window, which was wrong
//   for the main window; both are corrected and AppCommandsZoomTests.On_a_French_keyboard_Ctrl_with_the_key_that_types_a_minus_...
//   presses it in both production windows.
// - Test hygiene: AppCommandsFinishTests.The_palette_holds_only_the_source_actions_... and Ctrl_0_pressed_in_the_focused_Settings_window_...
//   prove one clause of each record they name, so they are R-TEST-4 partials, no longer Req.
// - STOP #7 (AltGr read as Ctrl+Alt, so Ctrl+Alt chords such as toggleActivityView are taken before a text box on layouts where
//   AltGr with that key types a character) is pre-existing and still open; it needs an AltGr decision (ED or owner), not a test.
