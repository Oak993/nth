using Marrow.App.Views.Terminal.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

// UI-TERM-010: what a program asks of the mouse, read from its output as SwiftTerm 1.20.0 reads it: Terminal.swift:5495-5554 (DECSET 9, 1000, 1002, 1003,
// 1005, 1006, 1015, 1016), :5260-5304 (their reset) and :6895-6905 with EscapeSequenceParser.swift:467 (XTSHIFTESCAPE, CSI > Ps s).
public sealed class TerminalScreenBufferMouseModeTests
{
    private static TerminalScreenBuffer Screen(params string[] output)
    {
        var screen = new TerminalScreenBuffer(80, 25);
        foreach (var piece in output) screen.Feed(piece);
        return screen;
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:727, :589
    public void A_new_screen_asks_for_no_mouse_and_the_default_encoding()
    {
        var screen = Screen();
        Assert.Equal(TerminalMouseMode.Off, screen.MouseMode);
        Assert.Equal(TerminalMouseEncoding.X10, screen.MouseEncoding);
        Assert.False(screen.MouseShiftCapture);
    }

    [Theory]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:5506 (9), :5520 (1000), :5523 (1002), :5527 (1003)
    [InlineData("\u001b[?9h", "X10")]
    [InlineData("\u001b[?1000h", "Vt200")]
    [InlineData("\u001b[?1002h", "ButtonEventTracking")]
    [InlineData("\u001b[?1003h", "AnyEvent")]
    public void Setting_a_tracking_mode_names_it(string output, string expected) => Assert.Equal(expected, Screen(output).MouseMode.ToString());

    [Theory]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:5547 (1005), :5550 (1006), :5552 (1015), :5554 (1016)
    [InlineData("\u001b[?1005h", "Utf8")]
    [InlineData("\u001b[?1006h", "Sgr")]
    [InlineData("\u001b[?1015h", "Urxvt")]
    [InlineData("\u001b[?1016h", "SgrPixel")]
    public void Setting_an_encoding_names_it(string output, string expected) => Assert.Equal(expected, Screen(output).MouseEncoding.ToString());

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:5272-5281 (a reset of any tracking mode turns tracking off), :5290-5304 (a reset encoding leaves tracking on)
    public void Resetting_a_tracking_mode_turns_tracking_off_and_resetting_an_encoding_keeps_tracking()
    {
        Assert.Equal(TerminalMouseMode.Off, Screen("\u001b[?1003h", "\u001b[?1000l").MouseMode); // whichever mode was on
        var screen = Screen("\u001b[?1003h", "\u001b[?1006h", "\u001b[?1006l");
        Assert.Equal(TerminalMouseMode.AnyEvent, screen.MouseMode); // mosh re-asserts ?1003l ?1003h ?1006l ?1006h on every resize
        Assert.Equal(TerminalMouseEncoding.X10, screen.MouseEncoding);
        Assert.Equal(TerminalMouseMode.AnyEvent, Screen("\u001b[?1003h", "\u001b[?1005l", "\u001b[?1015l", "\u001b[?1016l").MouseMode);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:5495 (the loop over the parameters of one sequence)
    public void One_sequence_can_set_a_mode_and_an_encoding_and_split_output_is_the_same()
    {
        var screen = Screen("\u001b[?1002;1006h");
        Assert.Equal(TerminalMouseMode.ButtonEventTracking, screen.MouseMode);
        Assert.Equal(TerminalMouseEncoding.Sgr, screen.MouseEncoding);
        var split = Screen("\u001b[?10", "03h");
        Assert.Equal(TerminalMouseMode.AnyEvent, split.MouseMode);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:6895-6905 cmdSetShiftEscape: 0 off, 1 on, anything else leaves it
    public void XTSHIFTESCAPE_asks_for_shift_clicks_to_reach_the_program()
    {
        Assert.True(Screen("\u001b[>1s").MouseShiftCapture);
        Assert.False(Screen("\u001b[>1s", "\u001b[>0s").MouseShiftCapture);
        Assert.True(Screen("\u001b[>1s", "\u001b[>2s").MouseShiftCapture);
        Assert.False(Screen("\u001b[>s").MouseShiftCapture); // no parameter is 0
    }
}
