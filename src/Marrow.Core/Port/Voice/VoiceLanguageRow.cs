// PORT-ONLY, owner-ruled addition (OD-0092, OD-0097, OD-0099 Q-V-17; first-release voice typing, VT-13): the Voice typing page's Language row (voice-SPEC V-16.3.1),
// pure so the page and its tests share one source of the words, the check and the detail.
namespace Marrow.Core.Port.Voice;

/// <summary>
/// The Language row of the Voice typing page. With a language picked (or Auto-detect picked) it shows that item. With nothing picked
/// (<c>voiceTyping.lang</c> null) it shows the language default's value (V-7.14): A Auto-detect, B Auto-detect shown while you speak,
/// C the Windows language, D asked on first use. The check and the detail are ENGINEERING DECISIONS of V-16.3.1: the check sits on
/// the item the button names (none for D) and the detail is that item's detail (D: the first-click line).
/// </summary>
public static class VoiceLanguageRow
{
    /// <summary>The middle dot of the B value is a settings value's text, not the pill's (V-16.3.1; the amendment's rule is the pill's).</summary>
    public const string ShownWhileYouSpeak = "Auto-detect \u00b7 shown while you speak";

    /// <summary>The D value.</summary>
    public const string AskedOnFirstUse = "Asked on first use";

    /// <summary>The D detail (V-16.3.1).</summary>
    public const string AskDetail = "You will be asked the first time you click the orb";

    /// <summary>The menu's items: the language list in the design's order (V-16.2), Auto-detect first; the id is the code.</summary>
    public static IReadOnlyList<(string Id, string Text)> MenuItems { get; } = [.. VoiceLanguage.Languages.Select(language => (language.Code, language.Name))];

    /// <summary>The detail of a language item (V-16.3.1): Auto-detect's Q-V-17 line, Hindi's and Urdu's script lines, otherwise "Every dictation is read as {Language}".</summary>
    public static string DetailFor(string code) => code switch
    {
        VoiceLanguage.AutoCode => VoiceLanguage.AutoDetail,
        "hi" => "Written in Devanagari",
        "ur" => "Written in Urdu script, right to left",
        _ => "Every dictation is read as " + (VoiceLanguage.Find(code)?.Name ?? throw new ArgumentException("not a language of the list: " + code, nameof(code))),
    };

    /// <summary>The row for the stored <paramref name="lang"/>, the language default <paramref name="mode"/> and Windows' display language (such as "hi-IN").</summary>
    public static VoiceLanguageRowState For(string? lang, VoiceLangMode mode, string? windowsUiLanguage)
    {
        if (VoiceLanguage.Find(lang) is { } picked) return Item(picked.Name, picked.Code);
        switch (mode)
        {
            case VoiceLangMode.Show:
                return new VoiceLanguageRowState(ShownWhileYouSpeak, VoiceLanguage.AutoCode, DetailFor(VoiceLanguage.AutoCode));
            case VoiceLangMode.Windows:
                if (VoiceLanguage.WindowsLanguageCode(windowsUiLanguage) is { } code)
                    return new VoiceLanguageRowState(VoiceLanguage.Find(code)!.Name + " (from Windows)", code, DetailFor(code));
                return Item("Auto-detect", VoiceLanguage.AutoCode);
            case VoiceLangMode.Ask:
                return new VoiceLanguageRowState(AskedOnFirstUse, null, AskDetail);
            default:
                return Item("Auto-detect", VoiceLanguage.AutoCode);
        }
    }

    private static VoiceLanguageRowState Item(string text, string code) => new(text, code, DetailFor(code));
}
