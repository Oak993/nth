using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-099: the port values for SwiftUI's KeyEquivalent, KeyboardShortcut and the chord modifiers.
public sealed class KeyboardTests
{
    [Fact]
    [Trait("Decision", "ED-099")]
    public void Modifier_values_are_Avalonias_so_the_UI_can_cast()
    {
        // Avalonia.Input.KeyModifiers: Alt = 1, Control = 2, Shift = 4 (Meta = 8 is Win, which a chord never keeps).
        Assert.Equal(1u, (uint)ChordModifiers.Alt);
        Assert.Equal(2u, (uint)ChordModifiers.Control);
        Assert.Equal(4u, (uint)ChordModifiers.Shift);
        Assert.Equal(0u, (uint)default(ChordModifiers));
    }

    [Fact]
    [Trait("Decision", "ED-099")]
    public void Key_equivalents_use_the_SwiftUI_spelling()
    {
        Assert.Equal("n", KeyEquivalent.Character('n').Name);
        Assert.Equal("return", KeyEquivalent.Return.Name);
        Assert.Equal("delete", KeyEquivalent.Delete.Name);
        Assert.Equal("deleteForward", KeyEquivalent.DeleteForward.Name);
        Assert.Equal("leftArrow", KeyEquivalent.LeftArrow.Name);
        Assert.Equal("pageDown", KeyEquivalent.PageDown.Name);
        Assert.Equal("F1", KeyEquivalent.Function(1)?.Name);
        Assert.Equal("F12", KeyEquivalent.Function(12)?.Name);
    }

    [Fact]
    [Trait("Decision", "ED-099")]
    public void A_function_number_outside_one_to_twelve_has_no_key_equivalent()
    {
        Assert.Null(KeyEquivalent.Function(0));
        Assert.Null(KeyEquivalent.Function(13));
    }

    [Fact]
    [Trait("Decision", "ED-099")]
    public void Shortcuts_compare_by_value()
    {
        var a = new KeyboardShortcut(KeyEquivalent.Character('n'), ChordModifiers.Control | ChordModifiers.Shift);
        var b = new KeyboardShortcut(KeyEquivalent.Character('n'), ChordModifiers.Shift | ChordModifiers.Control);
        Assert.Equal(a, b);
        Assert.NotEqual(a, new KeyboardShortcut(KeyEquivalent.Character('n'), ChordModifiers.Control));
    }
}
