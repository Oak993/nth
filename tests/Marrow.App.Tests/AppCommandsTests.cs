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
}

// STOP App.AppCommands #1 (build agent, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/AppCommands.swift:12-15 (B3 52bc792)
//   Button("Settings" + U+2026) { WindowManager.shared.showSettings() }
//       .keyboardShortcut(shortcuts.keyboardShortcut(for: .openSettings))
// and DroppyCode/App/AppCommands.swift:21-25 (B3 52bc792)
//   Button(AppInfo.name) { WindowManager.shared.showMain() }
//       .keyboardShortcut(shortcuts.keyboardShortcut(for: .showMainWindow))
// Requirements: PR-WIN-090, PR-WIN-092
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
