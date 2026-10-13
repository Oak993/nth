// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12, ED-NEW-pcctl-15; DESIGN.md 10.1). The checks a Tactus chord
// passes before it is registered or saved: it is not the same as another Tactus chord (the pause chord differs from the
// stop chord), not a single key (Enter, Space or Esc alone included), not a Windows-key chord (Windows and PowerToys keep
// claiming them), and it types no character on any installed keyboard layout (Ctrl+Alt is AltGr on many layouts, Hindi
// and Urdu ones included). The layout check is the caller's (ToUnicodeEx over GetKeyboardLayoutList in the app).
namespace Marrow.Core.Port.PcControl.Pill;

public enum PcChordProblem
{
    None,

    /// <summary>The same chord as another Tactus chord.</summary>
    Clash,

    /// <summary>One key with no Ctrl, Alt, Shift or Win.</summary>
    SingleKey,

    /// <summary>A Windows-key chord.</summary>
    WinKey,

    /// <summary>It types a character on one of the installed layouts.</summary>
    TypesCharacter,
}

public static class ChordValidator
{
    /// <summary>The first problem of <paramref name="chord"/>, or None. <paramref name="typesCharacter"/> runs last (it reads every layout).</summary>
    public static PcChordProblem Check(PcHotkey chord, IEnumerable<PcHotkey> others, Func<PcHotkey, bool> typesCharacter)
    {
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(typesCharacter);
        if (chord.Modifiers == PcHotkeyModifiers.None) return PcChordProblem.SingleKey;
        if (chord.Modifiers.HasFlag(PcHotkeyModifiers.Win)) return PcChordProblem.WinKey;
        if (others.Contains(chord)) return PcChordProblem.Clash;
        return typesCharacter(chord) ? PcChordProblem.TypesCharacter : PcChordProblem.None;
    }
}
