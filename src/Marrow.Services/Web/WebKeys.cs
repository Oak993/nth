using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Web;

/// <summary>
/// DESIGN 3.7 (ED-NEW-web-w7-5): chords from inside a web view. Web views raise <c>AcceleratorKeyPressed</c> before
/// the page sees a key; nth takes only the reserved chords (F6 and Shift+F6, the Tactus Stop chord Ctrl+Alt+Esc,
/// Ctrl+Shift+\, Ctrl+Alt+Arrow and Ctrl+Shift+B) and lets every other key reach the page. The default list here is the
/// design's; the shortcut layer (W3b) replaces <see cref="WebSurface.IsReservedChord"/> with the user's re-bound chords.
/// </summary>
public static class WebKeys
{
    // ED-099 scan codes (Set 1; extended keys as 0xE000 | code).
    public const long F6 = 0x40;
    public const long Escape = 0x01;
    public const long Backslash = 0x2B;
    public const long B = 0x30;
    public const long Up = 0xE048;
    public const long Down = 0xE050;
    public const long Left = 0xE04B;
    public const long Right = 0xE04D;

    /// <summary>The design's reserved chords (3.7), before any re-binding.</summary>
    public static IReadOnlySet<KeyChord> DefaultReserved { get; } = new HashSet<KeyChord>
    {
        new(F6, ChordModifiers.None), // next pane, or into the newest prompt first (10.1)
        new(F6, ChordModifiers.Shift), // previous pane
        new(Escape, ChordModifiers.Control | ChordModifiers.Alt), // Tactus Stop
        new(Backslash, ChordModifiers.Control | ChordModifiers.Shift), // expand or restore the focused pane
        new(Up, ChordModifiers.Control | ChordModifiers.Alt), // move the focused pane
        new(Down, ChordModifiers.Control | ChordModifiers.Alt),
        new(Left, ChordModifiers.Control | ChordModifiers.Alt),
        new(Right, ChordModifiers.Control | ChordModifiers.Alt),
        new(B, ChordModifiers.Control | ChordModifiers.Shift), // Visus
    };

    /// <summary>
    /// nth's chord for a key the web view reported: the physical scan code (extended keys as 0xE000 | code, ED-099 (1))
    /// and the Control, Alt and Shift state at the time of the key (ED-099 (2)).
    /// </summary>
    public static KeyChord ChordFrom(uint scanCode, bool isExtendedKey, bool control, bool alt, bool shift)
    {
        var code = (long)(scanCode & 0xFF);
        if (isExtendedKey) code |= 0xE000;
        var modifiers = ChordModifiers.None;
        if (control) modifiers |= ChordModifiers.Control;
        if (alt) modifiers |= ChordModifiers.Alt;
        if (shift) modifiers |= ChordModifiers.Shift;
        return new KeyChord(code, modifiers);
    }
}
