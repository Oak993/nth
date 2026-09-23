namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. The modifiers a recorded chord keeps (ED-099): Command and Control both become Control, Option becomes
/// Alt, and Win is masked out. The values are Avalonia's <c>KeyModifiers</c> values, so the UI casts directly.
/// </summary>
[Flags]
public enum ChordModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
}

/// <summary>
/// PORT-ONLY. SwiftUI's <c>KeyEquivalent</c> as a value (ED-099): <see cref="Name"/> is the SwiftUI spelling, a lowercase
/// character or one of return, tab, space, delete, escape, deleteForward, leftArrow, rightArrow, downArrow, upArrow,
/// home, end, pageUp, pageDown and F1 to F12. The UI maps it to an Avalonia key gesture.
/// </summary>
public readonly record struct KeyEquivalent(string Name)
{
    public static KeyEquivalent Character(char character) => new(character.ToString());

    public static KeyEquivalent Return => new("return");
    public static KeyEquivalent Tab => new("tab");
    public static KeyEquivalent Space => new("space");
    public static KeyEquivalent Delete => new("delete");
    public static KeyEquivalent Escape => new("escape");
    public static KeyEquivalent DeleteForward => new("deleteForward");
    public static KeyEquivalent LeftArrow => new("leftArrow");
    public static KeyEquivalent RightArrow => new("rightArrow");
    public static KeyEquivalent DownArrow => new("downArrow");
    public static KeyEquivalent UpArrow => new("upArrow");
    public static KeyEquivalent Home => new("home");
    public static KeyEquivalent End => new("end");
    public static KeyEquivalent PageUp => new("pageUp");
    public static KeyEquivalent PageDown => new("pageDown");

    /// <summary>F1 to F12, the menu key equivalent <c>NSF1FunctionKey + number - 1</c> stands for; nil for any other
    /// number, as the guard at Shortcuts.swift:98 gives.</summary>
    public static KeyEquivalent? Function(int number) =>
        number is >= 1 and <= 12 ? new("F" + number.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;
}

/// <summary>PORT-ONLY. SwiftUI's <c>KeyboardShortcut</c>: a key equivalent and its modifiers (ED-099).</summary>
public readonly record struct KeyboardShortcut(KeyEquivalent Key, ChordModifiers Modifiers);
