using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Views.Terminal;

// nth web surfaces, unit W4 (work/design/web-surfaces/DESIGN.md 3.7 "Content scopes: ... the terminal ... Ctrl+D (bookmark in pages,
// EOF in the terminal), Ctrl+\ (SIGQUIT in the terminal) ... reach the page or the shell", 13.1 "Ctrl+\ and Ctrl+D reach the terminal",
// 14 W4 "terminal keeps Ctrl+\ and Ctrl+D"). W3b made the terminal a content scope and the dispatch honour it; this proves it end to
// end on the app's own commands: a real terminal view in a window that carries AppCommands over a real thread. Ctrl+D (Toggle changes,
// which W4 turned into the Changes pane), Ctrl+J, Ctrl+K and Ctrl+B do nothing from inside the terminal and reach the shell as the
// bytes a shell reads; from a text box they run.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class TerminalChordScopeTests(HeadlessSession headless)
{
    [Fact]
    public void Ctrl_D_and_Ctrl_backslash_reach_the_shell_as_end_of_input_and_quit_and_open_and_close_no_pane() => headless.OnUiThread(() =>
    {
        using var probe = new TerminalChordProbe();
        Assert.True(probe.Terminal.Focus());
        probe.Press(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        probe.Press(Key.OemBackslash, PhysicalKey.Backslash, RawInputModifiers.Control, "\\");
        Assert.False(probe.Runtime.IsChangesPaneOpen, "Ctrl+D is not Toggle changes in a terminal");
        Assert.False(probe.Runtime.IsDiffVisible);
        Assert.Equal(new byte[] { 0x04, 0x1c }, probe.Sent());
    });

    [Fact]
    public void The_other_chords_nth_has_stay_the_shells_from_inside_the_terminal() => headless.OnUiThread(() =>
    {
        using var probe = new TerminalChordProbe();
        Assert.True(probe.Terminal.Focus());
        probe.Press(Key.J, PhysicalKey.J, RawInputModifiers.Control); // Toggle terminal
        probe.Press(Key.K, PhysicalKey.K, RawInputModifiers.Control); // Command palette
        probe.Press(Key.B, PhysicalKey.B, RawInputModifiers.Control); // Toggle sidebar
        probe.Press(Key.W, PhysicalKey.W, RawInputModifiers.Control);
        probe.Press(Key.R, PhysicalKey.R, RawInputModifiers.Control);
        Assert.False(probe.Runtime.IsTerminalVisible);
        Assert.False(probe.Model.IsCommandPalettePresented);
        Assert.Equal(new byte[] { 0x0a, 0x0b, 0x02, 0x17, 0x12 }, probe.Sent());
    });

    [Fact]
    public void Outside_the_terminal_the_same_chords_run_the_commands() => headless.OnUiThread(() =>
    {
        using var probe = new TerminalChordProbe();
        Assert.True(probe.Box.Focus());
        probe.Press(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        Assert.True(probe.Runtime.IsChangesPaneOpen, "Ctrl+D opens the Changes pane from nth's own controls");
        Assert.False(probe.Runtime.IsDiffVisible);
        probe.Press(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        Assert.False(probe.Runtime.IsChangesPaneOpen);
        probe.Press(Key.J, PhysicalKey.J, RawInputModifiers.Control);
        Assert.True(probe.Runtime.IsTerminalVisible);
        probe.Press(Key.K, PhysicalKey.K, RawInputModifiers.Control);
        Assert.True(probe.Model.IsCommandPalettePresented);
        Assert.Empty(probe.Sent());
    });

    [Fact]
    public void Focus_moving_into_the_terminal_and_out_again_changes_who_gets_the_chord() => headless.OnUiThread(() =>
    {
        using var probe = new TerminalChordProbe();
        Assert.True(probe.Terminal.Focus());
        probe.Press(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        Assert.False(probe.Runtime.IsChangesPaneOpen);
        Assert.True(probe.Box.Focus());
        probe.Press(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        Assert.True(probe.Runtime.IsChangesPaneOpen);
        Assert.True(probe.Terminal.Focus());
        probe.Press(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        Assert.True(probe.Runtime.IsChangesPaneOpen, "back in the terminal, Ctrl+D is the shell's again");
        Assert.Equal(new byte[] { 0x04, 0x04 }, probe.Sent());
    });
}
