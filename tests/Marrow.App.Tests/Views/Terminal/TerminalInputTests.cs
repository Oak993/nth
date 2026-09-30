using Avalonia.Input;
using Marrow.App.Views.Terminal.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

// UNVERIFIED drafts: owner deferred all execution to Claude on 2026-09-30.
public sealed class TerminalInputTests
{
    [Theory]
    [InlineData(Key.C, 3)]
    [InlineData(Key.D, 4)]
    [InlineData(Key.L, 12)]
    [InlineData(Key.Z, 26)]
    [InlineData(Key.Space, 0)]
    [InlineData(Key.D6, 30)]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm AppleTerminalView.swift:2570-2600.
    public void Control_keys_encode_the_source_control_bytes(Key key, byte expected)
    {
        Assert.Equal(new[] { expected }, TerminalInput.EncodeKey(key, KeyModifiers.Control, null, false, true));
    }

    [Theory]
    [InlineData(Key.Up, "\U0000001b[A", "\U0000001bOA")]
    [InlineData(Key.Down, "\U0000001b[B", "\U0000001bOB")]
    [InlineData(Key.Left, "\U0000001b[D", "\U0000001bOD")]
    [InlineData(Key.Right, "\U0000001b[C", "\U0000001bOC")]
    [InlineData(Key.Home, "\U0000001b[H", "\U0000001bOH")]
    [InlineData(Key.End, "\U0000001b[F", "\U0000001bOF")]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm KittyKeyboardEncoder.swift:650-668,682-690.
    public void Cursor_mode_switches_navigation_between_CSI_and_SS3(Key key, string normal, string application)
    {
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(normal), TerminalInput.EncodeKey(key, KeyModifiers.None, null, false, true));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(application), TerminalInput.EncodeKey(key, KeyModifiers.None, null, true, true));
    }

    [Theory]
    [InlineData(Key.Tab, KeyModifiers.Shift, "\U0000001b[Z")]
    [InlineData(Key.Back, KeyModifiers.Control, "\b")]
    [InlineData(Key.Enter, KeyModifiers.Alt, "\U0000001b\r")]
    [InlineData(Key.Left, KeyModifiers.Control, "\U0000001b[1;5D")]
    [InlineData(Key.Up, KeyModifiers.Shift | KeyModifiers.Alt, "\U0000001b[1;4A")]
    [InlineData(Key.Delete, KeyModifiers.Control, "\U0000001b[3;5~")]
    [InlineData(Key.F1, KeyModifiers.None, "\U0000001bOP")]
    [InlineData(Key.F4, KeyModifiers.Shift, "\U0000001b[1;2S")]
    [InlineData(Key.F5, KeyModifiers.None, "\U0000001b[15~")]
    [InlineData(Key.F12, KeyModifiers.Control | KeyModifiers.Alt, "\U0000001b[24;7~")]
    [InlineData(Key.F13, KeyModifiers.None, "\U0000001b[1;2P")]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm KittyKeyboardEncoder.swift:572-767 (legacy, no enhancements).
    public void Special_keys_and_modifiers_use_the_pinned_legacy_sequences(Key key, KeyModifiers modifiers, string expected)
    {
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(expected), TerminalInput.EncodeKey(key, modifiers, null, false, true));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm MacTerminalView.swift:1647-1666; TerminalStore.swift:23.
    public void Meta_text_uses_the_event_symbol_and_plain_text_is_left_for_TextInput()
    {
        Assert.Equal("\U0000001b!"u8.ToArray(), TerminalInput.EncodeKey(Key.D1, KeyModifiers.Alt | KeyModifiers.Shift, "!", false, true));
        Assert.Equal("\U0000001bx"u8.ToArray(), TerminalInput.EncodeKey(Key.X, KeyModifiers.Alt, null, false, true));
        Assert.Null(TerminalInput.EncodeKey(Key.X, KeyModifiers.None, "x", false, true));
        Assert.Null(TerminalInput.EncodeKey(Key.X, KeyModifiers.Alt, "x", false, false));
    }
}
