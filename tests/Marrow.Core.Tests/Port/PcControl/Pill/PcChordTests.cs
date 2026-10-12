// PORT-ONLY: the Tactus chords (OD-0145, ED-NEW-pcctl-15; DESIGN.md 4.4, 10.1, 10.10, 10.11): chord text read and written
// as Settings keeps it, the defaults, and the validator: the same chord as another, a single key, a Win chord and a chord
// that types a character on a layout are refused.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-15")]
public sealed class PcChordTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 0x20)]
    [InlineData("ctrl + alt + esc", PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 0x1B)]
    [InlineData("Control+Shift+F12", PcHotkeyModifiers.Control | PcHotkeyModifiers.Shift, 0x7B)]
    [InlineData("Alt+P", PcHotkeyModifiers.Alt, 0x50)]
    [InlineData("Win+Alt+Space", PcHotkeyModifiers.Win | PcHotkeyModifiers.Alt, 0x20)]
    [InlineData("Pause", PcHotkeyModifiers.None, 0x13)]
    public void Chord_text_is_read_as_Settings_keeps_it(string text, PcHotkeyModifiers modifiers, int key)
    {
        Assert.True(PcHotkey.TryParse(text, out var chord));
        Assert.Equal(new PcHotkey(modifiers, (ushort)key), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Hyper+A")]
    [InlineData("Ctrl+Alt+Banana")]
    [InlineData("Ctrl+F25")]
    public void Text_that_is_not_a_chord_is_refused(string text)
    {
        Assert.False(PcHotkey.TryParse(text, out _));
    }

    [Fact]
    public void The_defaults_are_Ctrl_Alt_Esc_and_Ctrl_Alt_Space_and_print_as_Settings_shows_them()
    {
        Assert.Equal("Ctrl+Alt+Esc", PcHotkey.StopDefault.Format());
        Assert.Equal("Ctrl+Alt+Space", PcHotkey.PauseDefault.Format());
        Assert.True(PcHotkey.TryParse("Shift+Alt+Ctrl+Win+F1", out var all));
        Assert.Equal("Ctrl+Alt+Shift+Win+F1", all.Format());
        Assert.True(PcHotkey.TryParse(PcHotkey.PauseDefault.Format(), out var again));
        Assert.Equal(PcHotkey.PauseDefault, again);
    }

    [Fact]
    public void The_validator_refuses_a_clash_a_single_key_a_Win_chord_and_a_typing_chord()
    {
        static bool None(PcHotkey _) => false;
        var stop = PcHotkey.StopDefault;

        Assert.Equal(PcChordProblem.None, ChordValidator.Check(PcHotkey.PauseDefault, [stop], None));
        Assert.Equal(PcChordProblem.Clash, ChordValidator.Check(stop, [stop], None));
        Assert.Equal(PcChordProblem.SingleKey, ChordValidator.Check(new PcHotkey(PcHotkeyModifiers.None, PcHotkey.Space), [stop], None));
        Assert.Equal(PcChordProblem.SingleKey, ChordValidator.Check(new PcHotkey(PcHotkeyModifiers.None, PcHotkey.Return), [stop], None));
        Assert.Equal(PcChordProblem.WinKey, ChordValidator.Check(new PcHotkey(PcHotkeyModifiers.Win | PcHotkeyModifiers.Alt, PcHotkey.Space), [stop], None));
        // Ctrl+Alt is AltGr: on a layout where Ctrl+Alt+E types a character, it is refused.
        var altGr = new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 'E');
        Assert.Equal(PcChordProblem.TypesCharacter, ChordValidator.Check(altGr, [stop], chord => chord == altGr));
    }

    [Fact]
    public void The_layout_check_runs_only_for_a_chord_that_passed_the_rest()
    {
        var asked = 0;
        bool Count(PcHotkey _)
        {
            asked++;
            return false;
        }

        ChordValidator.Check(new PcHotkey(PcHotkeyModifiers.None, 'A'), [], Count);
        ChordValidator.Check(PcHotkey.StopDefault, [PcHotkey.StopDefault], Count);
        Assert.Equal(0, asked);
        ChordValidator.Check(PcHotkey.PauseDefault, [PcHotkey.StopDefault], Count);
        Assert.Equal(1, asked);
    }
}
