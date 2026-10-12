// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12, ED-NEW-pcctl-15; DESIGN.md 4.4, 10.1, 10.10). A Tactus chord as
// Settings saves it ("Ctrl+Alt+Space") and as RegisterHotKey takes it: the modifier flags (MOD_ALT, MOD_CONTROL,
// MOD_SHIFT, MOD_WIN) and one virtual key. The stop chord defaults to Ctrl+Alt+Esc, the pause chord to Ctrl+Alt+Space;
// "Answer Tactus" has none until the user sets one. Text that is not a chord is refused, never guessed.
using System.Globalization;

namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>The modifier flags of RegisterHotKey.</summary>
[Flags]
public enum PcHotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

public readonly record struct PcHotkey(PcHotkeyModifiers Modifiers, ushort Key)
{
    public const ushort Escape = 0x1B;
    public const ushort Space = 0x20;
    public const ushort Return = 0x0D;

    private static readonly (string Name, ushort Key)[] Named =
    [
        ("Esc", Escape), ("Space", Space), ("Enter", Return), ("Tab", 0x09), ("Backspace", 0x08), ("Pause", 0x13),
        ("Insert", 0x2D), ("Delete", 0x2E), ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
    ];

    private static readonly (string Name, ushort Key)[] Aliases =
    [
        ("Escape", Escape), ("Return", Return), ("Del", 0x2E), ("Ins", 0x2D), ("PgUp", 0x21), ("PgDn", 0x22), ("Break", 0x13),
    ];

    /// <summary>Ctrl+Alt+Esc, the stop chord's default (DESIGN.md 4.4).</summary>
    public static PcHotkey StopDefault { get; } = new(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, Escape);

    /// <summary>Ctrl+Alt+Space, the pause chord's default (DESIGN.md 10.1).</summary>
    public static PcHotkey PauseDefault { get; } = new(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, Space);

    /// <summary>Reads "Ctrl+Alt+Space" (any case, "Control" for Ctrl); false for empty or unknown text.</summary>
    public static bool TryParse(string? text, out PcHotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = PcHotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var flag = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => PcHotkeyModifiers.Control,
                "ALT" => PcHotkeyModifiers.Alt,
                "SHIFT" => PcHotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => PcHotkeyModifiers.Win,
                _ => PcHotkeyModifiers.None,
            };
            if (flag == PcHotkeyModifiers.None || modifiers.HasFlag(flag)) return false;
            modifiers |= flag;
        }
        if (KeyOf(parts[^1]) is not { } key) return false;
        hotkey = new PcHotkey(modifiers, key);
        return true;
    }

    /// <summary>"Ctrl+Alt+Space": Ctrl, Alt, Shift, Win, then the key.</summary>
    public string Format()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(PcHotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(PcHotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(PcHotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(PcHotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(NameOf(Key));
        return string.Join('+', parts);
    }

    public override string ToString() => Format();

    private static ushort? KeyOf(string name)
    {
        foreach (var (known, key) in Named.Concat(Aliases))
        {
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) return key;
        }
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return char.ToUpperInvariant(name[0]);
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24)
            return (ushort)(0x70 + f - 1);
        return null;
    }

    private static string NameOf(ushort key)
    {
        foreach (var (name, known) in Named)
        {
            if (known == key) return name;
        }
        if (key is >= 0x70 and <= 0x87) return "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture);
        if (key is >= '0' and <= '9' or >= 'A' and <= 'Z') return ((char)key).ToString();
        return "0x" + key.ToString("X2", CultureInfo.InvariantCulture);
    }
}
