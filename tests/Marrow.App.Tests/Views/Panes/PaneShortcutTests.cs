using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Web;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3b: the pane commands on the window's keys and the scope rule (work/design/web-surfaces/DESIGN.md 3.7
// "Scope rule, three scopes", 10 "F6 cycles panes and splitters", 10.1, 13.1, 13.3). In nth chrome every chord applies; in a web
// view or the terminal the content gets every key first and only the reserved chords are taken; the chords are the user's
// bindings; a chord from inside a page runs in the pane that holds the page.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3b-1")]
public sealed class PaneShortcutTests(HeadlessSession headless)
{
    private const RawInputModifiers Ctrl = RawInputModifiers.Control;
    private const RawInputModifiers Alt = RawInputModifiers.Alt;
    private const RawInputModifiers Shift = RawInputModifiers.Shift;

    // The keys of the design's table, as the keyboard sends them (the key and its position).
    private static void ToggleVisus(PaneWorkspaceRig rig) => rig.Chord(Key.B, PhysicalKey.B, Ctrl | Shift);

    private static void ToggleSide(PaneWorkspaceRig rig) => rig.Chord(Key.OemSemicolon, PhysicalKey.Semicolon, Ctrl);

    private static void ToggleTabula(PaneWorkspaceRig rig) => rig.Chord(Key.A, PhysicalKey.A, Ctrl | Shift);

    private static void ToggleTasks(PaneWorkspaceRig rig) => rig.Chord(Key.G, PhysicalKey.G, Ctrl | Shift);

    private static void ClosePane(PaneWorkspaceRig rig) => rig.Chord(Key.OemPipe, PhysicalKey.Backslash, Ctrl);

    private static void ExpandPane(PaneWorkspaceRig rig) => rig.Chord(Key.OemPipe, PhysicalKey.Backslash, Ctrl | Shift);

    private static void F6(PaneWorkspaceRig rig, bool shift = false) => rig.Chord(Key.F6, PhysicalKey.F6, shift ? Shift : RawInputModifiers.None);

    [Fact]
    public void Each_toggle_chord_opens_and_closes_its_pane_from_nth_chrome() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.ChatColumn.Composer.Focus();
        foreach (var (toggle, kind) in new (Action<PaneWorkspaceRig>, PaneKind)[]
        {
            (ToggleVisus, PaneKind.Browser), (ToggleSide, PaneKind.SideChat), (ToggleTabula, PaneKind.Artifacts), (ToggleTasks, PaneKind.BackgroundTasks),
        })
        {
            toggle(rig);
            Assert.True(rig.Workspace.State.IsOpen(kind), $"{kind} opens");
            rig.Settle();
            toggle(rig);
            Assert.False(rig.Workspace.State.IsOpen(kind), $"{kind} closes");
            rig.Settle();
        }
        Assert.Equal(8, rig.Changes.Count);
        Assert.True(rig.ChatColumn.Composer.IsFocused, "a toggle does not move focus");
    });

    [Fact]
    public void A_chord_for_a_pane_kind_this_build_cannot_draw_is_not_taken_and_reaches_the_control() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.IsKindAvailable = kind => kind != PaneKind.Browser;
        var spy = new KeySpy(rig.ChatColumn.Composer);
        rig.ChatColumn.Composer.Focus();
        ToggleVisus(rig);
        Assert.False(rig.Workspace.State.IsOpen(PaneKind.Browser));
        Assert.Contains(spy.Keys, key => key.Key == Key.B);
        ToggleTabula(rig);
        Assert.True(rig.Workspace.State.IsOpen(PaneKind.Artifacts));
    });

    [Fact]
    public void Ctrl_Backslash_closes_the_focused_pane_and_gives_focus_to_the_chat_and_is_not_taken_elsewhere() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        rig.Changes.Clear();
        var spy = new KeySpy(rig.ChatColumn.Composer);
        rig.ChatColumn.Composer.Focus();
        ClosePane(rig);
        Assert.True(rig.Workspace.State.IsOpen(PaneKind.Browser) && rig.Workspace.State.IsOpen(PaneKind.Changes), "the chat is no pane: nothing closes");
        Assert.Contains(spy.Keys, key => key.Key == Key.OemPipe);

        rig.Bodies[PaneKind.Changes].Focus();
        ClosePane(rig);
        Assert.False(rig.Workspace.State.IsOpen(PaneKind.Changes));
        Assert.True(rig.Workspace.State.IsOpen(PaneKind.Browser));
        Assert.True(rig.ChatColumn.Composer.IsFocused, "closing the focused pane gives focus to the chat's composer");
        rig.Settle();
    });

    [Fact]
    public void Ctrl_Shift_Backslash_expands_the_focused_pane_and_restores_it_and_Esc_restores_from_chrome() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        rig.Bodies[PaneKind.Browser].Focus();
        ExpandPane(rig);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);
        rig.Settle();
        ExpandPane(rig);
        Assert.Null(rig.Workspace.State.Expanded);
        rig.Settle();

        ExpandPane(rig);
        rig.Settle();
        rig.Key(Key.Escape); // from nth chrome, Esc restores as before (3.4)
        Assert.Null(rig.Workspace.State.Expanded);
        rig.Settle();

        // With no pane focused and one expanded, the chord restores it.
        ExpandPane(rig);
        rig.Settle();
        rig.ChatColumn.Composer.Focus();
        ExpandPane(rig);
        Assert.Null(rig.Workspace.State.Expanded);
    });

    [Fact]
    public void In_a_content_scope_only_the_reserved_chords_are_taken_and_every_other_key_reaches_the_page() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        rig.MarkContent(PaneKind.Browser);
        var box = rig.Bodies[PaneKind.Browser];
        var spy = new KeySpy(box);
        box.Focus();
        Assert.Equal(ShortcutScope.Content, ShortcutScopeHost.ScopeOfFocus(rig.Window));
        var state = rig.Workspace.State;

        // The page keeps its date, its find-previous, its close and the like.
        ToggleSide(rig); // Ctrl+;
        ToggleTasks(rig); // Ctrl+Shift+G
        ClosePane(rig); // Ctrl+\
        rig.Chord(Key.T, PhysicalKey.T, Ctrl | Alt); // Ctrl+Alt+T
        rig.Chord(Key.D, PhysicalKey.D, Ctrl); // Ctrl+D
        Assert.Equal(state, rig.Workspace.State);
        Assert.Equal(5, spy.Keys.Count(key => key.Key is not Key.None));
        Assert.Contains(spy.Keys, key => key.Key == Key.OemSemicolon && key.Modifiers == KeyModifiers.Control);
        Assert.Contains(spy.Keys, key => key.Key == Key.G && key.Modifiers == (KeyModifiers.Control | KeyModifiers.Shift));

        // The reserved chords still reach nth.
        ExpandPane(rig);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);
        rig.Settle();
        rig.Key(Key.Escape); // Esc never restores an expanded pane from inside content
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);
        ExpandPane(rig);
        Assert.Null(rig.Workspace.State.Expanded);
        rig.Settle();
        rig.Chord(Key.Right, PhysicalKey.ArrowRight, Ctrl | Alt); // move
        Assert.Equal([PaneKind.Changes, PaneKind.Browser], rig.Workspace.State.GridPanes);
        rig.Settle();
        F6(rig);
        Assert.False(box.IsKeyboardFocusWithin, "F6 leaves the content");
        box.Focus();
        ToggleVisus(rig); // Ctrl+Shift+B is reserved: it closes Visus even from inside a page
        Assert.False(rig.Workspace.State.IsOpen(PaneKind.Browser));
    });

    [Fact]
    public void The_chords_are_the_users_bindings() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        rig.Bodies[PaneKind.Browser].Focus();
        rig.Shortcuts.Set(new KeyChord(0x12, ChordModifiers.Control | ChordModifiers.Alt), AppShortcut.ExpandPane); // Ctrl+Alt+E
        ExpandPane(rig); // the old chord is nothing now
        Assert.Null(rig.Workspace.State.Expanded);
        rig.Chord(Key.E, PhysicalKey.E, Ctrl | Alt);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);
        rig.Settle();
        rig.Shortcuts.Set(null, AppShortcut.ExpandPane); // cleared
        rig.Chord(Key.E, PhysicalKey.E, Ctrl | Alt);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded); // nothing took it: the cleared command has no chord
    });

    [Fact]
    public void F6_and_Shift_F6_cycle_the_chat_the_panes_in_order_Side_chat_the_splitters_and_wrap() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(width: 1600);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.BackgroundTasks);
        rig.Workspace.Open(PaneKind.SideChat);
        rig.Settle();
        var stops = rig.Workspace.FocusStops();
        var expected = new List<Control> { rig.Workspace.Chat, rig.Card(PaneKind.BackgroundTasks), rig.Card(PaneKind.Browser), rig.Card(PaneKind.SideChat) };
        expected.AddRange(rig.Expected().Gutters.Select(gutter => (Control)rig.Workspace.SplitterOf(gutter.Key)));
        Assert.Equal(expected, stops);

        rig.Window.FocusManager!.Focus(null);
        var visited = new List<Control>();
        for (var i = 0; i < stops.Count; i++)
        {
            F6(rig);
            var focused = (Visual)rig.Focused!;
            visited.Add(stops.Single(stop => ReferenceEquals(stop, focused) || stop.IsVisualAncestorOf(focused)));
        }
        Assert.Equal(stops, visited);
        F6(rig); // wraps to the chat
        Assert.True(rig.ChatColumn.Composer.IsFocused);
        F6(rig, shift: true); // back to the last stop
        Assert.True(stops[^1] == rig.Focused || stops[^1].IsVisualAncestorOf((Visual)rig.Focused!));
        F6(rig, shift: true);
        F6(rig, shift: true);
        Assert.True(stops[^3] == rig.Focused || stops[^3].IsVisualAncestorOf((Visual)rig.Focused!));

        // A pane takes the focus as a whole, and the focus brightens its rim (3.12).
        rig.Window.FocusManager!.Focus(null);
        F6(rig);
        F6(rig);
        Assert.Same(rig.Card(PaneKind.BackgroundTasks), rig.Focused);
        Assert.True(rig.Card(PaneKind.BackgroundTasks).IsFocusedLook);
        Assert.Equal(PaneKind.BackgroundTasks, rig.Workspace.FocusedPane);
    });

    [Fact]
    public void An_expanded_pane_is_the_only_stop_and_an_open_prompt_takes_F6_first() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        PaneWorkspaceRig.Click(rig.Card(PaneKind.Browser).Header.ExpandButton);
        rig.Settle();
        Assert.Equal(new Control[] { rig.Card(PaneKind.Browser) }, rig.Workspace.FocusStops());
        F6(rig);
        Assert.Same(rig.Card(PaneKind.Browser), rig.Focused);
        PaneWorkspaceRig.Click(rig.Card(PaneKind.Browser).Header.ExpandButton);
        rig.Settle();

        var asked = 0;
        rig.Workspace.PromptFirst = () => ++asked == 1;
        rig.Window.FocusManager!.Focus(null);
        F6(rig); // the prompt takes it
        Assert.Null(rig.Focused);
        F6(rig); // answered: the normal cycle
        Assert.NotNull(rig.Focused);
        Assert.Equal(2, asked);
    });

    [Fact]
    public void The_menu_dispatch_takes_a_command_from_a_content_scope_only_when_it_works_inside_content() => headless.OnUiThread(() =>
    {
        var ran = new List<string>();
        MenuCommand[] body =
        [
            new("Toggle changes", new KeyboardShortcut(new KeyEquivalent("d"), ChordModifiers.Control), false, () => ran.Add("changes")),
            new("Reserved", new KeyboardShortcut(new KeyEquivalent("b"), ChordModifiers.Control | ChordModifiers.Shift), false, () => ran.Add("reserved"), WorksInsideContent: true),
        ];
        var ctrlD = new KeyboardShortcut(new KeyEquivalent("d"), ChordModifiers.Control);
        var ctrlShiftB = new KeyboardShortcut(new KeyEquivalent("b"), ChordModifiers.Control | ChordModifiers.Shift);
        Assert.True(MenuKeyEquivalents.Perform(body, ctrlD, () => ctrlD, ShortcutScope.Chrome));
        Assert.False(MenuKeyEquivalents.Perform(body, ctrlD, () => ctrlD, ShortcutScope.Content), "Ctrl+D reaches the terminal");
        Assert.True(MenuKeyEquivalents.Perform(body, ctrlShiftB, () => ctrlShiftB, ShortcutScope.Content));
        Assert.True(MenuKeyEquivalents.Perform(body, ctrlD, () => ctrlD)); // the old signature is chrome
        Assert.Equal(["changes", "reserved", "changes"], ran);
    });

    [Fact]
    public void A_web_view_host_and_the_terminal_are_content_scopes_and_what_they_hold_inherits_it() => headless.OnUiThread(() =>
    {
        var terminal = new TerminalHostContainer();
        var inner = new TextBox();
        terminal.HostedView = inner;
        var window = new Avalonia.Controls.Window { Content = new StackPanel { Children = { terminal, new TextBox() } } };
        window.Show();
        Assert.Equal(ShortcutScope.Content, ShortcutScopeHost.GetScope(terminal));
        Assert.Equal(ShortcutScope.Content, ShortcutScopeHost.GetScope(inner));
        Assert.Equal(ShortcutScope.Chrome, ShortcutScopeHost.GetScope((Control)((StackPanel)window.Content!).Children[1]));
        inner.Focus();
        Assert.Equal(ShortcutScope.Content, ShortcutScopeHost.ScopeOfFocus(window));
        window.Close();

        var host = new WebViewHost(new PaneWebSurface(), _ => new Border { Focusable = true });
        Assert.Equal(ShortcutScope.Content, ShortcutScopeHost.GetScope(host));
    });

    [Fact]
    public void A_chord_pressed_inside_a_page_runs_in_the_pane_that_holds_the_page_under_the_users_bindings() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var surface = new WebSurface(new FakeWebEngine(time), new WebProfiles(@"C:\nth-test\WebView2", "en-US"), time, work => work());
        var view = surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/", null);
        view.AttachNative(1, 2);
        view.SetShown(true);
        WebViewHost? host = null;
        PaneContent? Contents(PaneKind kind)
        {
            Control body = kind == PaneKind.Browser
                ? host = new WebViewHost(view, _ => new Border { Focusable = true })
                : new Border { Child = new TextBox() };
            return new PaneContent(body, PaneKinds.Title(kind));
        }

        using var rig = new PaneWorkspaceRig(contents: Contents);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        using var binding = PaneShortcuts.Bind(surface, rig.Shortcuts);
        var fake = Assert.IsType<FakeWebView>(view.Engine);

        // Ctrl+Shift+Backslash is reserved: the page does not see it, nth expands the pane the page is in.
        var expand = new KeyChord(0x2B, ChordModifiers.Control | ChordModifiers.Shift);
        Assert.True(fake.RaiseKey(expand).Handled);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);
        rig.Settle();
        Assert.True(fake.RaiseKey(expand).Handled);
        Assert.Null(rig.Workspace.State.Expanded);
        rig.Settle();

        // A chord nth does not take reaches the page.
        Assert.False(fake.RaiseKey(new KeyChord(0x27, ChordModifiers.Control)).Handled); // Ctrl+; is the page's
        Assert.False(fake.RaiseKey(new KeyChord(0x2B, ChordModifiers.Control)).Handled); // so is Ctrl+\
        Assert.False(fake.RaiseKey(new KeyChord(0x22, ChordModifiers.Control | ChordModifiers.Shift)).Handled); // and Ctrl+Shift+G

        // Moving a pane is reserved and moves the pane the page is in.
        Assert.True(fake.RaiseKey(new KeyChord(0xE04D, ChordModifiers.Control | ChordModifiers.Alt)).Handled);
        Assert.Equal([PaneKind.Changes, PaneKind.Browser], rig.Workspace.State.GridPanes);
        Assert.True(fake.RaiseKey(new KeyChord(0xE048, ChordModifiers.Control | ChordModifiers.Alt)).Handled); // Up: the twin of Left
        Assert.Equal([PaneKind.Browser, PaneKind.Changes], rig.Workspace.State.GridPanes);

        // The reserved list follows the user's bindings.
        rig.Shortcuts.Set(new KeyChord(0x12, ChordModifiers.Control | ChordModifiers.Alt), AppShortcut.ExpandPane);
        Assert.False(fake.RaiseKey(expand).Handled);
        Assert.True(fake.RaiseKey(new KeyChord(0x12, ChordModifiers.Control | ChordModifiers.Alt)).Handled);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);

        // The Tactus Stop chord is reserved but is no workspace command.
        Assert.True(fake.RaiseKey(ShortcutScopes.TactusStop).Handled);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);
        Assert.NotNull(host);

        binding.Dispose();
        Assert.False(fake.RaiseKey(new KeyChord(0x12, ChordModifiers.Control | ChordModifiers.Alt)).Handled, "unbound: the default list is back");
    });
}
