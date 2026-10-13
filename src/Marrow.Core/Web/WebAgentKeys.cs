// PORT-ONLY: web surfaces W11, web_key's presses (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "web_key {tab, keys}: key
// presses (Input.dispatchKeyEvent)"; ED-NEW-web-w11-1). No Swift counterpart. "tab tab enter", "ctrl+a", "shift+tab" become
// DevTools key events (key, code, Windows virtual-key code, the text a printable key types, and CDP's modifier bits: Alt 1,
// Ctrl 2, Meta 4, Shift 8). The keys go only to the page through CDP: never the user's keyboard, never Windows. A chord is judged
// like the key it modifies (ED-NEW-web-sec-agent-2): Ctrl+Space presses a button, Ctrl+Z edits a field, and a clipboard chord never runs.
using System.Globalization;

namespace Marrow.Core.Web;

/// <summary>One key press for CDP: key and code names, the virtual-key code, the text it types (null for none) and the modifier bits.</summary>
public sealed record WebKeyPress(string Key, string Code, int KeyCode, string? Text, int Modifiers)
{
    public bool IsEnter => Key == "Enter";
}

public static class WebAgentKeys
{
    public const int Alt = 1;
    public const int Ctrl = 2;
    public const int Meta = 4;
    public const int Shift = 8;

    /// <summary>The most presses one web_key runs.</summary>
    public const int MaxPresses = 40;

    private static readonly Dictionary<string, (string Key, string Code, int KeyCode, string? Text)> named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = ("Enter", "Enter", 13, "\r"),
        ["return"] = ("Enter", "Enter", 13, "\r"),
        ["tab"] = ("Tab", "Tab", 9, null),
        ["escape"] = ("Escape", "Escape", 27, null),
        ["esc"] = ("Escape", "Escape", 27, null),
        ["backspace"] = ("Backspace", "Backspace", 8, null),
        ["delete"] = ("Delete", "Delete", 46, null),
        ["del"] = ("Delete", "Delete", 46, null),
        ["space"] = (" ", "Space", 32, " "),
        ["up"] = ("ArrowUp", "ArrowUp", 38, null),
        ["arrowup"] = ("ArrowUp", "ArrowUp", 38, null),
        ["down"] = ("ArrowDown", "ArrowDown", 40, null),
        ["arrowdown"] = ("ArrowDown", "ArrowDown", 40, null),
        ["left"] = ("ArrowLeft", "ArrowLeft", 37, null),
        ["arrowleft"] = ("ArrowLeft", "ArrowLeft", 37, null),
        ["right"] = ("ArrowRight", "ArrowRight", 39, null),
        ["arrowright"] = ("ArrowRight", "ArrowRight", 39, null),
        ["home"] = ("Home", "Home", 36, null),
        ["end"] = ("End", "End", 35, null),
        ["pageup"] = ("PageUp", "PageUp", 33, null),
        ["pagedown"] = ("PageDown", "PageDown", 34, null),
        ["insert"] = ("Insert", "Insert", 45, null),
    };

    /// <summary>The presses, or the reason the text is not keys this tool takes.</summary>
    public static (IReadOnlyList<WebKeyPress> Presses, string? Error) Parse(string? keys)
    {
        var presses = new List<WebKeyPress>();
        if (string.IsNullOrWhiteSpace(keys)) return (presses, "keys is empty");
        foreach (var token in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split('+');
            var modifiers = 0;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].ToLowerInvariant())
                {
                    case "ctrl" or "control": modifiers |= Ctrl; break;
                    case "alt": modifiers |= Alt; break;
                    case "shift": modifiers |= Shift; break;
                    case "meta" or "cmd": modifiers |= Meta; break;
                    case "win" or "windows": return ([], "the Windows key is never pressed");
                    default: return ([], "\"" + parts[i] + "\" is not a modifier (ctrl, alt, shift)");
                }
            }
            var last = parts[^1];
            if (last.Length == 0) return ([], "\"" + token + "\" has no key");
            if (named.TryGetValue(last, out var key))
            {
                presses.Add(new WebKeyPress(key.Key, key.Code, key.KeyCode, (modifiers & (Ctrl | Alt | Meta)) == 0 ? key.Text : null, modifiers));
            }
            else if (last.Length >= 2 && (last[0] is 'f' or 'F') && int.TryParse(last[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 12)
            {
                presses.Add(new WebKeyPress("F" + f.ToString(CultureInfo.InvariantCulture), "F" + f.ToString(CultureInfo.InvariantCulture), 111 + f, null, modifiers));
            }
            else if (last.Length == 1)
            {
                var c = last[0];
                var upper = char.ToUpperInvariant(c);
                var code = char.IsLetter(c) ? "Key" + upper : char.IsDigit(c) ? "Digit" + c : "";
                var keyCode = char.IsLetterOrDigit(c) && c < 128 ? upper : 0;
                var typed = (modifiers & (Ctrl | Alt | Meta)) == 0 ? ((modifiers & Shift) != 0 ? upper.ToString() : c.ToString()) : null;
                var keyName = (modifiers & Shift) != 0 ? upper.ToString() : c.ToString();
                presses.Add(new WebKeyPress(keyName, code, keyCode, typed, modifiers));
            }
            else
            {
                return ([], "\"" + last + "\" is not a key this tool presses (enter, tab, escape, arrows, page up and down, home, end, backspace, delete, a letter or ctrl+ a letter)");
            }
            if (presses.Count > MaxPresses) return ([], "at most " + MaxPresses.ToString(CultureInfo.InvariantCulture) + " presses in one call");
        }
        return (presses, null);
    }

    // ED-NEW-web-sec-agent-2 (security review H2): which presses web_key judges, whatever their modifiers.

    /// <summary>
    /// A press that uses the user's Windows clipboard: a paste (Ctrl+V, Ctrl+Shift+V, Shift+Insert) reads it into the page, a copy or
    /// cut (Ctrl+C, Ctrl+X, Ctrl+Insert, Shift+Delete) replaces it. Agent tabs never read the clipboard and page content never
    /// reaches it without the user (DESIGN 8.4), so web_key refuses these.
    /// </summary>
    public static bool TouchesClipboard(WebKeyPress press)
    {
        ArgumentNullException.ThrowIfNull(press);
        var command = (press.Modifiers & (Ctrl | Meta)) != 0;
        var shift = (press.Modifiers & Shift) != 0;
        return (command && press.Code is "KeyV" or "KeyC" or "KeyX")
            || (press.Key == "Insert" && (shift || command))
            || (press.Key == "Delete" && shift);
    }

    /// <summary>A press of the focused control: Space (with any modifier) presses a button as a click does.</summary>
    public static bool Activates(WebKeyPress press)
    {
        ArgumentNullException.ThrowIfNull(press);
        return press.Key == " ";
    }

    /// <summary>
    /// A press that can change what a field holds: a typed character, Backspace or Delete, and any Ctrl, Alt or Meta chord on a
    /// character key (undo, redo, delete a word, a page's own editing shortcut). The focused field is judged before it runs.
    /// </summary>
    public static bool Edits(WebKeyPress press)
    {
        ArgumentNullException.ThrowIfNull(press);
        if (press.Text is not null || press.Key is "Backspace" or "Delete") return true;
        return (press.Modifiers & (Ctrl | Alt | Meta)) != 0 && press.Key.Length == 1;
    }
}
