using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;

namespace Marrow.App.Port;

// PORT-ONLY: web surfaces W4, which keys reach content (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.7 "Scope rule, three
// scopes: nth chrome ... every nth chord applies. Content scopes: a web view ... and the terminal. Here the content gets every
// key first ... Ctrl+D (bookmark in pages, EOF in the terminal), Ctrl+\ (SIGQUIT in the terminal) ... reach the page or the
// shell. Reserved chords that nth always takes, even from a content scope ... How: ... The terminal pane's key handler does
// the same with the same list"; 13.1 "Shortcut scope tests: in a content scope only the reserved list is taken; Ctrl+\ and
// Ctrl+D reach the terminal"; ED-NEW-web-w4-2). No Swift counterpart. A control marks itself a content scope (the terminal's
// surface does; the web views do when they land); the window's chord dispatch (MenuKeyEquivalents.Attach) asks here before
// it runs a command for a key that came from inside one.

/// <summary>Content scopes: parts of a window whose keys belong to what they show (a shell, a page).</summary>
public static class ChordScope
{
    public static readonly AttachedProperty<bool> IsContentProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsContent", typeof(ChordScope), false);

    /// <summary>Whether <paramref name="control"/> is a content scope (the terminal's surface, a web view).</summary>
    public static bool GetIsContent(Control control) => control.GetValue(IsContentProperty);

    public static void SetIsContent(Control control, bool value) => control.SetValue(IsContentProperty, value);

    /// <summary>Whether the key-down came from inside a content scope (its source, or an ancestor of it, is one).</summary>
    public static bool IsFromContent(KeyEventArgs e)
    {
        for (var visual = e.Source as Visual; visual is not null; visual = visual.GetVisualParent())
            if (visual is Control control && GetIsContent(control)) return true;
        return false;
    }

    /// <summary>
    /// Whether a chord is one of the reserved ones nth takes even from content. The default is the design's list (W7's
    /// <see cref="WebKeys.DefaultReserved"/>); the shortcut layer (W3b) replaces it with the user's re-bound chords.
    /// </summary>
    public static Func<KeyChord, bool> IsReserved { get; set; } = WebKeys.DefaultReserved.Contains;

    /// <summary>The chord a key-down presses (ED-099): its physical key's scan code and the Control, Alt and Shift held; null for a key with no scan code in the table.</summary>
    public static KeyChord? ChordOf(KeyEventArgs e) =>
        MenuKeyEquivalents.ScanCodeOf(e.PhysicalKey) is { } code
            ? new KeyChord(code, (ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers)
            : null;

    /// <summary>Whether the key-down is a reserved chord.</summary>
    public static bool IsReservedKey(KeyEventArgs e) => ChordOf(e) is { } chord && IsReserved(chord);

    /// <summary>
    /// Whether the window's chord dispatch leaves this key-down alone: it came from a content scope and is not a reserved chord.
    /// Everything it leaves goes on to the content (the terminal sends Ctrl+D to the shell as end of input and Ctrl+\ as the quit
    /// character).
    /// </summary>
    public static bool Withholds(KeyEventArgs e) => IsFromContent(e) && !IsReservedKey(e);
}
