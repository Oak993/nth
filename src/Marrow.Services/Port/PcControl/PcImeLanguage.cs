// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17, ED-NEW-pcctl-44; DESIGN.md 2.7 "IME composition" and "The gate",
// critique C4). The pure decision behind the keyboard-still part of the gate: an open composition (the IME WinEvents, or
// UI Automation TextEditPattern.GetActiveComposition on the focused element) blocks a borrow outright; otherwise the
// keyboard must be still 2 s instead of 700 ms when the foreground thread's input language is served by an IME or a TSF
// text service, which may give no composition signal at all. Modern Hindi and Urdu phonetic input is a TSF text service
// (an input processor profile), which ImmIsIME does not report; their plain keyboard layouts compose nothing.
namespace Marrow.Services.Port.PcControl;

/// <summary>What serves the foreground thread's input language (TSF's active keyboard profile for that language).</summary>
public enum PcInputProfileType
{
    /// <summary>TSF could not be read: treated as an input processor, so the gate waits the longer time.</summary>
    Unknown,

    /// <summary>A plain keyboard layout (TF_PROFILETYPE_KEYBOARDLAYOUT): keys arrive as typed.</summary>
    KeyboardLayout,

    /// <summary>A TSF text service (TF_PROFILETYPE_INPUTPROCESSOR): it may compose text with no signal Tactus can read.</summary>
    InputProcessor,
}

/// <summary>The gate's keyboard part: blocked by an open composition, else how long the keyboard must be still.</summary>
public readonly record struct PcImeVerdict(bool CompositionOpen, int KeysStillMs);

public static class PcImeLanguage
{
    // Primary languages whose input is normally composed even through an IMM IME: Chinese, Japanese, Korean, Vietnamese.
    private static readonly int[] ComposedLanguages = [0x04, 0x11, 0x12, 0x2A];

    /// <summary>The primary language of a LANGID (its low 10 bits).</summary>
    public static int Primary(ushort languageId) => languageId & 0x3FF;

    /// <summary>
    /// Whether the input language is served by an IME or a TSF text service: a TSF input processor (or a TSF read that
    /// failed), an IMM IME (ImmIsIME on the layout), or a language whose input is composed.
    /// </summary>
    public static bool Served(ushort languageId, PcInputProfileType profile, bool immIsIme) =>
        profile is PcInputProfileType.InputProcessor or PcInputProfileType.Unknown || immIsIme || ComposedLanguages.Contains(Primary(languageId));

    /// <summary>The keyboard-still threshold for a served or plain language.</summary>
    public static int KeysStillMs(bool served) => served ? PcInputGate.ImeKeysStillMs : PcInputGate.KeysStillMs;

    /// <summary>The whole decision: language id, profile type and the composition signal to the gate's keyboard part.</summary>
    public static PcImeVerdict Decide(ushort languageId, PcInputProfileType profile, bool immIsIme, bool compositionOpen) =>
        new(compositionOpen, KeysStillMs(Served(languageId, profile, immIsIme)));
}
