using System.Text;
using Marrow.App.Views.Terminal.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

// UI-TERM-010: "mouse selection uses SwiftTerm's own handling". The bytes a program that asked for the mouse is sent, from SwiftTerm 1.20.0's
// Terminal.swift (:677-725 the mode's questions, :6916-6946 encodeButton, :6962-6999 sendEvent and sendMotion, :6879-6892 the UTF-8 coordinate), written out
// by hand here from that source, not from the port's code. The tag is the one project.yml:8-9 pins.
public sealed class TerminalMouseEncodingTests
{
    private static byte[] Csi(string tail) => Encoding.Latin1.GetBytes("\u001b[" + tail);

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:677-725
    public void The_modes_answer_which_events_a_program_is_told_of()
    {
        // press, release, button-tracking motion, any motion, modifiers
        var expected = new Dictionary<TerminalMouseMode, bool[]>
        {
            [TerminalMouseMode.Off] = [false, false, false, false, false],
            [TerminalMouseMode.X10] = [false, true, false, false, false], // a release is sent in every mode but off (:703-706)
            [TerminalMouseMode.Vt200] = [true, true, false, false, true],
            [TerminalMouseMode.ButtonEventTracking] = [true, true, true, false, true],
            [TerminalMouseMode.AnyEvent] = [true, true, true, true, true],
        };
        foreach (var (mode, answers) in expected)
            Assert.Equal(answers, new[] { mode.SendsButtonPress(), mode.SendsButtonRelease(), mode.SendsButtonTracking(), mode.SendsMotion(), mode.SendsModifiers() });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:6916-6946
    public void A_button_is_its_number_a_release_is_3_the_wheel_64_and_65_and_modifiers_add_4_8_16_where_the_mode_sends_them()
    {
        Assert.Equal(0, TerminalMouse.EncodeButton(TerminalMouseMode.Vt200, 0, release: false, shift: false, meta: false, control: false));
        Assert.Equal(1, TerminalMouse.EncodeButton(TerminalMouseMode.Vt200, 1, release: false, shift: false, meta: false, control: false));
        Assert.Equal(2, TerminalMouse.EncodeButton(TerminalMouseMode.Vt200, 2, release: false, shift: false, meta: false, control: false));
        Assert.Equal(64, TerminalMouse.EncodeButton(TerminalMouseMode.Vt200, 4, release: false, shift: false, meta: false, control: false));
        Assert.Equal(65, TerminalMouse.EncodeButton(TerminalMouseMode.Vt200, 5, release: false, shift: false, meta: false, control: false));
        Assert.Equal(0, TerminalMouse.EncodeButton(TerminalMouseMode.Vt200, 3, release: false, shift: false, meta: false, control: false)); // any other button is the left one
        Assert.Equal(3, TerminalMouse.EncodeButton(TerminalMouseMode.Vt200, 0, release: true, shift: false, meta: false, control: false));
        Assert.Equal(4 + 8 + 16, TerminalMouse.EncodeButton(TerminalMouseMode.ButtonEventTracking, 0, release: false, shift: true, meta: true, control: true));
        Assert.Equal(3 | 16, TerminalMouse.EncodeButton(TerminalMouseMode.AnyEvent, 0, release: true, shift: false, meta: false, control: true));
        // X10 and off send no modifier bits
        Assert.Equal(0, TerminalMouse.EncodeButton(TerminalMouseMode.X10, 0, release: false, shift: true, meta: true, control: true));
        Assert.Equal(3, TerminalMouse.EncodeButton(TerminalMouseMode.Off, 0, release: true, shift: true, meta: false, control: false));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:6966-6967
    public void The_default_encoding_is_CSI_M_then_the_button_and_the_cell_each_plus_32_capped_at_255()
    {
        Assert.Equal([0x1b, 0x5b, (byte)'M', 32, 32 + 5, 32 + 3], TerminalMouse.EncodeEvent(TerminalMouseEncoding.X10, 0, 4, 2, 0, 0)); // column 4 and row 2, zero based
        Assert.Equal([0x1b, 0x5b, (byte)'M', 32 + 64, 32 + 1, 32 + 1], TerminalMouse.EncodeEvent(TerminalMouseEncoding.X10, 64, 0, 0, 0, 0));
        Assert.Equal([0x1b, 0x5b, (byte)'M', 32 + 3, 255, 255], TerminalMouse.EncodeEvent(TerminalMouseEncoding.X10, 3, 300, 300, 0, 0)); // beyond 223 cells the byte stops at 255
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:6968-6971
    public void The_SGR_encoding_is_CSI_lt_button_column_row_then_M_and_m_for_a_release()
    {
        Assert.Equal(Csi("<0;5;3M"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.Sgr, 0, 4, 2, 130, 45));
        Assert.Equal(Csi("<0;5;3m"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.Sgr, 3, 4, 2, 130, 45)); // a release: the button bits go, the m is the release
        Assert.Equal(Csi("<64;5;3M"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.Sgr, 64, 4, 2, 130, 45));
        Assert.Equal(Csi("<16;1;1m"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.Sgr, 3 | 16, 0, 0, 0, 0)); // a release (19) loses the button bits, 3, and keeps the control bit, 16
        Assert.Equal(Csi("<2000;2001;2002M"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.Sgr, 2000, 2000, 2001, 0, 0)); // no cap
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:6972-6976
    public void The_SGR_pixel_encoding_writes_the_pixel_in_place_of_the_cell()
    {
        Assert.Equal(Csi("<0;130;45M"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.SgrPixel, 0, 4, 2, 130, 45));
        Assert.Equal(Csi("<0;130;45m"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.SgrPixel, 3, 4, 2, 130, 45));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:6978-6979
    public void The_urxvt_encoding_is_CSI_button_plus_32_column_row_then_M()
    {
        Assert.Equal(Csi("32;5;3M"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.Urxvt, 0, 4, 2, 0, 0));
        Assert.Equal(Csi("35;5;3M"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.Urxvt, 3, 4, 2, 0, 0));
        Assert.Equal(Csi("96;301;301M"), TerminalMouse.EncodeEvent(TerminalMouseEncoding.Urxvt, 64, 300, 300, 0, 0));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:6980-6985 with :6879-6892
    public void The_UTF8_encoding_writes_each_value_as_one_byte_below_127_and_two_up_to_2047()
    {
        Assert.Equal([0x1b, 0x5b, (byte)'M', 32, 37, 35], TerminalMouse.EncodeEvent(TerminalMouseEncoding.Utf8, 0, 4, 2, 0, 0));
        // column 300: 300 + 33 = 333 = 0b101_001101: 0xC0 | 5, 0x80 | 13
        Assert.Equal([0x1b, 0x5b, (byte)'M', 32, 0xc5, 0x8d, 35], TerminalMouse.EncodeEvent(TerminalMouseEncoding.Utf8, 0, 300, 2, 0, 0));
        // a value of exactly 2047 is a NUL; one beyond it stays at 2047, 0xDF 0xBF
        Assert.Equal([0x1b, 0x5b, (byte)'M', 32, 0, 35], TerminalMouse.EncodeEvent(TerminalMouseEncoding.Utf8, 0, 2014, 2, 0, 0));
        Assert.Equal([0x1b, 0x5b, (byte)'M', 32, 0xdf, 0xbf, 35], TerminalMouse.EncodeEvent(TerminalMouseEncoding.Utf8, 0, 5000, 2, 0, 0));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift:6996-6999
    public void A_motion_event_is_the_same_event_with_32_added_to_its_button()
    {
        Assert.Equal(Csi("<32;5;3M"), TerminalMouse.EncodeMotion(TerminalMouseEncoding.Sgr, 0, 4, 2, 0, 0)); // dragged with the left button
        Assert.Equal(Csi("<35;5;3M"), TerminalMouse.EncodeMotion(TerminalMouseEncoding.Sgr, 3, 4, 2, 0, 0)); // moved with none down: still a motion, not a release
        Assert.Equal([0x1b, 0x5b, (byte)'M', 32 + 32, 37, 35], TerminalMouse.EncodeMotion(TerminalMouseEncoding.X10, 0, 4, 2, 0, 0));
    }
}
