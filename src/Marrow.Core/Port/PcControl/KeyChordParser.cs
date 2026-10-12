// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 2.1, 4.2). Turns the model's key text ("ctrl+s", "alt+f4",
// "shift+tab", "numpad0", X11-style "Control_L+s", "Page_Up", "KP_0") into Windows virtual-key codes. Refuses the Windows
// key and Ctrl+Alt+Del outright, and refuses a key it does not know rather than guessing (legacy Marrow, ED-NEW-pcctl-2).
namespace Marrow.Core.Port.PcControl;

/// <summary>A parsed chord: the modifier keys held, in order, and the one key pressed.</summary>
public sealed record KeyChord(IReadOnlyList<ushort> Modifiers, ushort Key)
{
    public bool HasModifier(ushort vk) => Modifiers.Contains(vk);
}

/// <summary>A parse: a chord, or a refusal with its error code (bad_args or denied_key) and reason.</summary>
public sealed record KeyChordParse(KeyChord? Chord, PcErrorCode? Error, string? Reason)
{
    public bool Ok => Chord is not null;
}

public static class KeyChordParser
{
    public const ushort Shift = 0x10;
    public const ushort Control = 0x11;
    public const ushort Alt = 0x12;
    public const ushort LeftWin = 0x5B;
    public const ushort RightWin = 0x5C;
    public const ushort Return = 0x0D;
    public const ushort Escape = 0x1B;
    public const ushort Delete = 0x2E;

    private static readonly Dictionary<string, ushort> modifierNames = new(StringComparer.Ordinal)
    {
        ["ctrl"] = Control, ["control"] = Control, ["ctl"] = Control, ["controll"] = Control, ["controlr"] = Control, ["lctrl"] = Control, ["rctrl"] = Control,
        ["alt"] = Alt, ["altl"] = Alt, ["altr"] = Alt, ["lalt"] = Alt, ["ralt"] = Alt, ["option"] = Alt,
        ["shift"] = Shift, ["shiftl"] = Shift, ["shiftr"] = Shift, ["lshift"] = Shift, ["rshift"] = Shift,
    };

    private static readonly HashSet<string> windowsKeyNames = new(StringComparer.Ordinal)
    {
        "win", "windows", "super", "superl", "superr", "meta", "metal", "metar", "cmd", "command", "lwin", "rwin", "winl", "winr", "start",
    };

    private static readonly Dictionary<string, ushort> keyNames = BuildKeyNames();

    private static Dictionary<string, ushort> BuildKeyNames()
    {
        var names = new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            ["enter"] = Return, ["return"] = Return, ["kpenter"] = Return,
            ["tab"] = 0x09, ["esc"] = Escape, ["escape"] = Escape, ["space"] = 0x20, ["spacebar"] = 0x20,
            ["backspace"] = 0x08, ["back"] = 0x08, ["delete"] = Delete, ["del"] = Delete, ["insert"] = 0x2D, ["ins"] = 0x2D,
            ["home"] = 0x24, ["end"] = 0x23,
            ["pageup"] = 0x21, ["pgup"] = 0x21, ["prior"] = 0x21, ["pagedown"] = 0x22, ["pgdn"] = 0x22, ["next"] = 0x22,
            ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
            ["arrowleft"] = 0x25, ["arrowup"] = 0x26, ["arrowright"] = 0x27, ["arrowdown"] = 0x28,
            ["capslock"] = 0x14, ["numlock"] = 0x90, ["scrolllock"] = 0x91, ["pause"] = 0x13,
            ["printscreen"] = 0x2C, ["print"] = 0x2C, ["prtsc"] = 0x2C,
            ["apps"] = 0x5D, ["menu"] = 0x5D, ["contextmenu"] = 0x5D,
            ["multiply"] = 0x6A, ["kpmultiply"] = 0x6A, ["add"] = 0x6B, ["kpadd"] = 0x6B,
            ["subtract"] = 0x6D, ["kpsubtract"] = 0x6D, ["decimal"] = 0x6E, ["kpdecimal"] = 0x6E, ["divide"] = 0x6F, ["kpdivide"] = 0x6F,
            ["semicolon"] = 0xBA, [";"] = 0xBA, ["equal"] = 0xBB, ["equals"] = 0xBB, ["="] = 0xBB, ["plus"] = 0xBB,
            ["comma"] = 0xBC, [","] = 0xBC, ["minus"] = 0xBD, ["-"] = 0xBD, ["period"] = 0xBE, ["dot"] = 0xBE, ["."] = 0xBE,
            ["slash"] = 0xBF, ["/"] = 0xBF, ["grave"] = 0xC0, ["backquote"] = 0xC0, ["`"] = 0xC0,
            ["bracketleft"] = 0xDB, ["["] = 0xDB, ["backslash"] = 0xDC, ["\\"] = 0xDC, ["bracketright"] = 0xDD, ["]"] = 0xDD,
            ["quote"] = 0xDE, ["apostrophe"] = 0xDE, ["'"] = 0xDE,
            ["volumemute"] = 0xAD, ["volumedown"] = 0xAE, ["volumeup"] = 0xAF,
            ["medianext"] = 0xB0, ["mediaprevious"] = 0xB1, ["mediastop"] = 0xB2, ["mediaplaypause"] = 0xB3,
            ["browserback"] = 0xA6, ["browserforward"] = 0xA7, ["browserrefresh"] = 0xA8,
        };
        for (var c = 'a'; c <= 'z'; c++) names[c.ToString()] = (ushort)(0x41 + (c - 'a'));
        for (var d = 0; d <= 9; d++)
        {
            var digit = ((char)('0' + d)).ToString();
            names[digit] = (ushort)(0x30 + d);
            names["numpad" + digit] = (ushort)(0x60 + d);
            names["kp" + digit] = (ushort)(0x60 + d);
            names["num" + digit] = (ushort)(0x60 + d);
        }
        for (var f = 1; f <= 24; f++) names["f" + f.ToString(System.Globalization.CultureInfo.InvariantCulture)] = (ushort)(0x70 + f - 1);
        return names;
    }

    /// <summary>Keys that SendInput must mark KEYEVENTF_EXTENDEDKEY.</summary>
    public static bool IsExtended(ushort vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;

    public static KeyChordParse Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Fail(PcErrorCode.BadArgs, "keys is empty");
        var parts = Split(text.Trim());
        if (parts is null) return Fail(PcErrorCode.BadArgs, $"\"{text}\" is not a key chord");
        var modifiers = new List<ushort>();
        ushort? key = null;
        for (var i = 0; i < parts.Count; i++)
        {
            var name = Normalise(parts[i]);
            if (windowsKeyNames.Contains(name)) return Fail(PcErrorCode.DeniedKey, "the Windows key is never pressed");
            var last = i == parts.Count - 1;
            if (!last)
            {
                if (!modifierNames.TryGetValue(name, out var modifier)) return Fail(PcErrorCode.BadArgs, $"\"{parts[i]}\" is not a modifier (use ctrl, alt or shift)");
                if (!modifiers.Contains(modifier)) modifiers.Add(modifier);
                continue;
            }
            if (keyNames.TryGetValue(name, out var vk)) key = vk;
            else if (modifierNames.TryGetValue(name, out var lone)) key = lone;
            else return Fail(PcErrorCode.BadArgs, $"unknown key \"{parts[i]}\"");
        }
        var chord = new KeyChord(modifiers, key!.Value);
        if (chord.HasModifier(Control) && chord.HasModifier(Alt) && chord.Key == Delete)
            return Fail(PcErrorCode.DeniedKey, "Ctrl+Alt+Delete is never pressed");
        return new KeyChordParse(chord, null, null);
    }

    // "ctrl++" is Ctrl with the plus key; "+" alone is the plus key.
    private static List<string>? Split(string text)
    {
        if (text == "+") return ["plus"];
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '+' && current.Length > 0)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length == 0) return null;
        parts.Add(current.ToString().Trim() == "+" ? "plus" : current.ToString().Trim());
        return parts.Any(p => p.Length == 0) ? null : parts;
    }

    private static string Normalise(string part)
    {
        var lower = part.ToLowerInvariant();
        return lower.Length <= 1 ? lower : lower.Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
    }

    private static KeyChordParse Fail(PcErrorCode code, string reason) => new(null, code, reason);
}
