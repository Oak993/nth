using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.1 and 4.2 key text; ED-NEW-pcctl-2 carries legacy Marrow's rules
// (pc-control.test.ts:129-151): the Windows key is refused alone or in a chord, and an unknown key is refused, not guessed.
[Trait("Decision", "ED-NEW-pcctl-1")]
[Trait("Decision", "ED-NEW-pcctl-2")]
public sealed class KeyChordParserTests
{
    private static KeyChord Chord(string text)
    {
        var parse = KeyChordParser.Parse(text);
        Assert.True(parse.Ok, parse.Reason);
        return parse.Chord!;
    }

    [Theory]
    [InlineData("enter", 0x0D)]
    [InlineData("Return", 0x0D)]
    [InlineData("ESC", 0x1B)]
    [InlineData("tab", 0x09)]
    [InlineData("f5", 0x74)]
    [InlineData("F12", 0x7B)]
    [InlineData("f24", 0x87)]
    [InlineData("numpad0", 0x60)]
    [InlineData("KP_7", 0x67)]
    [InlineData("Page_Up", 0x21)]
    [InlineData("pgdn", 0x22)]
    [InlineData("a", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("7", 0x37)]
    [InlineData("space", 0x20)]
    [InlineData("backspace", 0x08)]
    [InlineData("delete", 0x2E)]
    [InlineData("comma", 0xBC)]
    [InlineData("/", 0xBF)]
    [InlineData("+", 0xBB)]
    public void Single_keys_and_their_aliases_map_to_virtual_keys_in_any_case(string text, int vk)
    {
        var chord = Chord(text);
        Assert.Empty(chord.Modifiers);
        Assert.Equal((ushort)vk, chord.Key);
    }

    [Fact]
    public void Chords_keep_their_modifiers_in_the_order_written_without_repeats()
    {
        var chord = Chord("Ctrl+Shift+S");
        Assert.Equal(new ushort[] { KeyChordParser.Control, KeyChordParser.Shift }, chord.Modifiers);
        Assert.Equal((ushort)0x53, chord.Key);
        Assert.Equal(new ushort[] { KeyChordParser.Alt }, Chord("alt+alt+f4").Modifiers);
        Assert.Equal(new ushort[] { KeyChordParser.Control }, Chord("Control_L+s").Modifiers);
    }

    [Fact]
    public void Ctrl_plus_plus_is_ctrl_with_the_plus_key()
    {
        var chord = Chord("ctrl++");
        Assert.Equal(new ushort[] { KeyChordParser.Control }, chord.Modifiers);
        Assert.Equal((ushort)0xBB, chord.Key);
        Assert.Equal((ushort)0xBB, Chord("ctrl+plus").Key);
    }

    [Fact]
    public void A_lone_modifier_is_a_key_press_of_that_modifier()
    {
        var chord = Chord("shift");
        Assert.Empty(chord.Modifiers);
        Assert.Equal(KeyChordParser.Shift, chord.Key);
    }

    [Theory]
    [InlineData("win")]
    [InlineData("Win+R")]
    [InlineData("super_l")]
    [InlineData("ctrl+windows+d")]
    [InlineData("LWin")]
    [InlineData("meta+e")]
    public void The_windows_key_is_refused_alone_or_with_anything_else(string text)
    {
        var parse = KeyChordParser.Parse(text);
        Assert.False(parse.Ok);
        Assert.Equal(PcErrorCode.DeniedKey, parse.Error);
    }

    [Theory]
    [InlineData("ctrl+alt+delete")]
    [InlineData("alt+ctrl+del")]
    [InlineData("Control+Alt+Delete")]
    public void Ctrl_alt_delete_is_refused(string text)
    {
        var parse = KeyChordParser.Parse(text);
        Assert.Equal(PcErrorCode.DeniedKey, parse.Error);
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("ctrl+banana")]
    [InlineData("s+ctrl")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ctrl+")]
    [InlineData("f25")]
    public void An_unknown_key_or_a_malformed_chord_is_refused_rather_than_guessed(string text)
    {
        var parse = KeyChordParser.Parse(text);
        Assert.False(parse.Ok);
        Assert.Equal(PcErrorCode.BadArgs, parse.Error);
    }

    [Fact]
    public void Navigation_keys_are_marked_extended_and_letters_are_not()
    {
        foreach (var name in new[] { "left", "up", "right", "down", "home", "end", "pageup", "pagedown", "insert", "delete", "divide", "apps" })
            Assert.True(KeyChordParser.IsExtended(Chord(name).Key), name);
        foreach (var name in new[] { "a", "enter", "f5", "numpad1", "tab", "esc" })
            Assert.False(KeyChordParser.IsExtended(Chord(name).Key), name);
    }
}
