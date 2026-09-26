using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
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
    [Trait("Req", "PR-WIN-089")] // App/AppCommands.swift:11-114
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
    [Trait("Req", "PR-WIN-089")] // App/AppCommands.swift:11-114
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
            Assert.True(commands.PerformKeyEquivalent(CtrlD));
            Assert.True(model.Runtime(thread.Id).IsDiffVisible);
            Assert.True(commands.PerformKeyEquivalent(CtrlD));
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
    [Trait("Req", "PR-WIN-105")] // App/AppCommands.swift:74-77
    public void Queue_a_chat_chord_lines_the_draft_up_behind_the_running_turn()
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
            var commands = new AppCommands(model);

            Assert.False(Named(commands, "Queue a chat").IsDisabled);
            Assert.True(commands.PerformKeyEquivalent(CtrlReturn));

            Assert.Equal("next step", Assert.Single(runtime.FollowUps).Text);
            Assert.True(runtime.DraftIsEmpty);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-105")] // App/AppCommands.swift:69-77
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
    [Trait("Req", "PR-WIN-105")] // App/AppCommands.swift:76-77
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
    [Trait("Req", "PR-WIN-105")] // App/AppCommands.swift:69-77
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

    // ===== PR-UPDATE-092: Welcome tour =====

    [Fact]
    [Trait("Req", "PR-UPDATE-092")] // App/AppCommands.swift:111-113; App/Views/Tour/TourPages.swift:26-28
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
        h.Main.OnMain(() => TourWindowController.Shared.Close());

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
    [Trait("Req", "PR-WIN-112")] // App/AppCommands.swift:100-102; App/AppModel.swift:855-858; App/AppSettings.swift:30-34
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
    [Trait("Req", "PR-WIN-112")] // App/AppCommands.swift:102
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
    [Trait("Req", "PR-WIN-112")] // App/AppCommands.swift:100-101; App/AppModel.swift:865-870
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
    [Trait("Req", "PR-WIN-112")] // App/AppCommands.swift:100-101; App/AppModel.swift:865-881
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
    [Trait("Req", "PR-WIN-112")] // App/AppCommands.swift:100-101; App/AppModel.swift:868-870
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
            Assert.True(commands.PerformKeyEquivalent(CtrlW));
            // No runtime was ever made for the thread, so it is asked about rather than deleted (AppModel.swift:906-910).
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
            Assert.Equal("Show Marrow already uses this chord.", store.Refusal(ctrlZero, AppShortcut.NewThread));

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
            Assert.True(model.Runtime(thread.Id).IsDiffVisible);
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
    [Trait("Req", "UI-UPDATE-055")] // App/AppCommands.swift:111-113
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
}

// STOP App.AppCommands #1 (build agent, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/AppCommands.swift:12-15 (B3 52bc792)
//   Button("Settings" + U+2026) { WindowManager.shared.showSettings() }
//       .keyboardShortcut(shortcuts.keyboardShortcut(for: .openSettings))
// and DroppyCode/App/AppCommands.swift:21-25 (B3 52bc792)
//   Button(AppInfo.name) { WindowManager.shared.showMain() }
//       .keyboardShortcut(shortcuts.keyboardShortcut(for: .showMainWindow))
// Requirements: PR-WIN-090, PR-WIN-092; part 2 adds the menu-item records of the same two sites: UI-LAUNCH-007,
// UI-WIN-006 (the Settings item) and UI-WIN-007 (the item titled with the app's name)
// What is unclear or missing: both actions call App.Window.WindowManager, whose Shared, ShowSettings() and ShowMain()
// are still MARROW_STUB (src/Marrow.App/Window/WindowManager.cs:29-30, :51-56: each throws NotImplementedException).
// Nothing about the two commands is unclear: the Settings command (openSettings chord, Ctrl+, by default, never
// disabled) and the show-main command (showMainWindow chord, no default under OD-0012 G6, never disabled) go into the
// table exactly where the source puts them, first and third of the groups (:12-15, :21-25), once the callee exists.
// Readings or options: (a) add the two entries now, calling the throwing stub -> the Settings chord would throw out of
// the window's key handler; (b) leave both sites unwritten until WindowManager is ported -> Ctrl+, does nothing yet.
// Needed to proceed: unit App.Window.WindowManager (Shared, ShowSettings(page:), ShowMain()).
// Done so far: no test written for these two records (any test would exercise only the throwing stub); both entries
// are absent from AppCommands.Body; everything else in part 1 is implemented and tested above.

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

// STOP App.AppCommands #4 (build agent, part 2, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/AppCommands.swift:111-113 (B3 52bc792)
//   CommandGroup(replacing: .help) {
//       Button("Welcome tour") { Tour.present(model: model) }
// Requirements: PR-WIN-115; the command-palette half of UI-UPDATE-055
// What is unclear or missing: OD-0012 G5 moves this command into the command palette as an action titled "Welcome
// tour" that presents the tour. That row belongs to App/Views/Palette/CommandPalette.swift:153-178, whose twin
// src/Marrow.App/Views/Palette/CommandPalette.cs is still MARROW_STUB, and PR-WIN-115's open_question leaves the row's
// place among the actions (before or after Settings, which the source appends last), its symbol (every palette Item
// needs one, CommandPalette.swift:28) and its id unset.
// Readings or options: none to choose here; the row is outside this file.
// Needed to proceed: unit App.Views.Palette.CommandPalette, and a decision (ED or owner) naming the row's place, symbol
// and id.
// Done so far: the help group's command itself is in AppCommands.Body, tested in part 1 (PR-UPDATE-092) and above for
// UI-UPDATE-055 (last, alone, no chord, never disabled), green; nothing is written for the palette row.
