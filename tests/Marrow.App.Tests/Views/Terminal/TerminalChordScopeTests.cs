using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// nth web surfaces, unit W4 (work/design/web-surfaces/DESIGN.md 3.7 "Scope rule ... Content scopes: a web view ... and the terminal.
// Here the content gets every key first, so ... Ctrl+D (bookmark in pages, EOF in the terminal), Ctrl+\ (SIGQUIT in the terminal)
// ... reach the page or the shell. Reserved chords that nth always takes, even from a content scope: F6 and Shift+F6, the Tactus
// Stop chord, Ctrl+Shift+\, Ctrl+Alt+Arrow and Ctrl+Shift+B"; 13.1 "Shortcut scope tests: in a content scope only the reserved
// list is taken; Ctrl+\ and Ctrl+D reach the terminal"; 14 W4 "terminal keeps Ctrl+\ and Ctrl+D"). A real terminal view in a window
// that carries the app's chord dispatch: what the dispatch is offered, and what the shell is sent.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w4-2")]
public sealed class TerminalChordScopeTests(HeadlessSession headless)
{
    private void Run(bool takesEverything, Action<TerminalChordProbe> body) => headless.OnUiThread(() => TerminalChordProbe.Run(takesEverything, body));

    [Fact]
    public void Ctrl_D_and_Ctrl_backslash_reach_the_shell_as_end_of_input_and_quit_and_the_dispatch_is_never_offered_them() => Run(takesEverything: true, probe =>
    {
        Assert.True(probe.Terminal.Focus());
        probe.Press(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        probe.Press(Key.OemBackslash, PhysicalKey.Backslash, RawInputModifiers.Control, "\\");
        Assert.Empty(probe.Offered);
        Assert.Equal(new byte[] { 0x04, 0x1c }, probe.Sent());
    });

    [Fact]
    public void Every_other_chord_nth_has_stays_the_shells_from_inside_the_terminal() => Run(takesEverything: true, probe =>
    {
        Assert.True(probe.Terminal.Focus());
        probe.Press(Key.J, PhysicalKey.J, RawInputModifiers.Control); // Toggle terminal
        probe.Press(Key.K, PhysicalKey.K, RawInputModifiers.Control); // Command palette
        probe.Press(Key.B, PhysicalKey.B, RawInputModifiers.Control); // Toggle sidebar
        probe.Press(Key.W, PhysicalKey.W, RawInputModifiers.Control);
        probe.Press(Key.R, PhysicalKey.R, RawInputModifiers.Control);
        Assert.Empty(probe.Offered);
        Assert.Equal(new byte[] { 0x0a, 0x0b, 0x02, 0x17, 0x12 }, probe.Sent());
    });

    [Fact]
    public void The_reserved_chords_are_nths_from_inside_the_terminal_and_never_the_shells() => Run(takesEverything: false, probe =>
    {
        Assert.True(probe.Terminal.Focus());
        probe.Press(Key.F6, PhysicalKey.F6);
        probe.Press(Key.F6, PhysicalKey.F6, RawInputModifiers.Shift);
        probe.Press(Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Control | RawInputModifiers.Alt);
        probe.Press(Key.OemBackslash, PhysicalKey.Backslash, RawInputModifiers.Control | RawInputModifiers.Shift, "|");
        probe.Press(Key.B, PhysicalKey.B, RawInputModifiers.Control | RawInputModifiers.Shift);
        probe.Press(Key.Escape, PhysicalKey.Escape, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Equal(
            [
                new KeyboardShortcut(KeyEquivalent.Function(6)!.Value, ChordModifiers.None),
                new KeyboardShortcut(KeyEquivalent.Function(6)!.Value, ChordModifiers.Shift),
                new KeyboardShortcut(KeyEquivalent.LeftArrow, ChordModifiers.Control | ChordModifiers.Alt),
                new KeyboardShortcut(new KeyEquivalent("\\"), ChordModifiers.Control | ChordModifiers.Shift),
                new KeyboardShortcut(new KeyEquivalent("b"), ChordModifiers.Control | ChordModifiers.Shift),
                new KeyboardShortcut(KeyEquivalent.Escape, ChordModifiers.Control | ChordModifiers.Alt),
            ],
            probe.Offered.Select(chord => chord!.Value).ToArray());
        // The dispatch did not take them (no command is bound yet): the terminal still sends the shell nothing for them.
        Assert.Empty(probe.Sent());
    });

    [Fact]
    public void Outside_the_terminal_every_chord_is_nths_as_before() => Run(takesEverything: true, probe =>
    {
        Assert.True(probe.Box.Focus());
        probe.Press(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        probe.Press(Key.J, PhysicalKey.J, RawInputModifiers.Control);
        probe.Press(Key.OemBackslash, PhysicalKey.Backslash, RawInputModifiers.Control, "\\");
        Assert.Equal(3, probe.Offered.Count);
        Assert.Equal("d", probe.Offered[0]!.Value.Key.Name);
        Assert.Equal(ChordModifiers.Control, probe.Offered[0]!.Value.Modifiers);
        Assert.Empty(probe.Sent());
    });

    [Fact]
    public void The_reserved_list_is_the_designs_and_no_default_app_shortcut_is_on_it() => headless.OnUiThread(() =>
    {
        // W7's list (ED-NEW-web-w7-5) is the one the terminal reads, through the seam the shortcut layer replaces.
        Assert.Equal(9, Marrow.Services.Web.WebKeys.DefaultReserved.Count);
        foreach (var chord in Marrow.Services.Web.WebKeys.DefaultReserved) Assert.True(ChordScope.IsReserved(chord), $"{chord}");
        foreach (var shortcut in Enum.GetValues<AppShortcut>())
            if (shortcut.DefaultChord() is { } chord)
                Assert.False(ChordScope.IsReserved(chord), $"{shortcut} would be taken from a shell or a page");
        Assert.False(ChordScope.IsReserved(new KeyChord(0x20, ChordModifiers.Control)), "Ctrl+D");
        Assert.False(ChordScope.IsReserved(new KeyChord(0x2B, ChordModifiers.Control)), @"Ctrl+\");
    });

    [Fact]
    public void The_terminal_surface_marks_itself_a_content_scope_and_a_key_from_inside_it_is_from_content() => headless.OnUiThread(() =>
    {
        var view = new TerminalHostView();
        Assert.True(ChordScope.GetIsContent(view));
        var outside = new TextBox();
        Assert.False(ChordScope.GetIsContent(outside));
        var window = new AvaloniaWindow { Width = 200, Height = 200, Content = new StackPanel { Children = { outside, view } } };
        try
        {
            window.Show();
            window.UpdateLayout();
            KeyEventArgs Press(Control source, Key key, PhysicalKey physical, KeyModifiers modifiers) =>
                new() { RoutedEvent = InputElement.KeyDownEvent, Source = source, Key = key, PhysicalKey = physical, KeyModifiers = modifiers };
            var ctrlD = Press(view.Child!, Key.D, PhysicalKey.D, KeyModifiers.Control);
            var ctrlDOutside = Press(outside, Key.D, PhysicalKey.D, KeyModifiers.Control);
            var f6 = Press(view.Child!, Key.F6, PhysicalKey.F6, KeyModifiers.None);
            var unknown = Press(view.Child!, Key.MediaPlayPause, PhysicalKey.MediaPlayPause, KeyModifiers.None);
            Assert.True(ChordScope.IsFromContent(ctrlD));
            Assert.False(ChordScope.IsFromContent(ctrlDOutside));
            Assert.True(ChordScope.Withholds(ctrlD));
            Assert.False(ChordScope.Withholds(f6));
            Assert.False(ChordScope.Withholds(ctrlDOutside));
            Assert.True(ChordScope.Withholds(unknown), "a key no menu can hold is the shell's too");
            Assert.True(ChordScope.IsReservedKey(f6));
            Assert.False(ChordScope.IsReservedKey(ctrlD));
        }
        finally { window.Close(); }
    });
}
