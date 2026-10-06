// PORT-ONLY CZ-8 (customization-SPEC CZ-11.1; OWNER OD-0097; DESIGN cz.js:642-654, voiceopts.js:253-257): every word of the Customization page's Speech section and the detail each of its three link rows shows for the stored voice typing settings.
// The section only summarises the Voice typing page; the words that page already owns (the model's name, the language's) come from Core's catalogue and list.
using Marrow.Core.Port.Voice;

namespace Marrow.Core.Port.Settings;

public static class CzSpeechText
{
    public const string Heading = "Speech";
    public const string ModelTitle = "Speech model";
    public const string LanguageTitle = "Language";
    public const string EngineTitle = "Engine";
    public const string Change = "Change";

    private const string ModelCloud = "Not used while the cloud engine is on";
    private const string AutoDetect = "Auto-detect: Whisper works out the language";
    private const string AutoShown = "Auto-detect, named in the pill while you speak";
    private const string FromWindows = " (from Windows)";
    private const string AskedOnFirstUse = "Asked on first use";
    private const string EngineLocal = "On this PC: your voice never leaves this computer";
    private const string EngineCloud = "OpenAI cloud, with your own key";

    /// <summary>The Speech model row: the selected model's name (voice-SPEC V-9.6) for the local engine, a note for the cloud engine. ENGINEERING DECISION: with no model selected (or an id outside the catalogue) the local engine has no name to show, and the detail is empty rather than invented.</summary>
    public static string ModelDetail(VoiceEngine engine, string? modelId) =>
        engine == VoiceEngine.Cloud ? ModelCloud : ModelCatalog.ModelName(modelId) ?? "";

    /// <summary>The Engine row.</summary>
    public static string EngineDetail(VoiceEngine engine) => engine == VoiceEngine.Cloud ? EngineCloud : EngineLocal;

    /// <summary>
    /// The Language row. A language picked on the Voice typing page (<paramref name="lang"/>) comes first: its name, and an explicit Auto-detect says what it does. With nothing picked (null, or a code outside the list)
    /// the row follows the language default: A (<see cref="VoiceLangMode.Auto"/>) the Auto-detect line, B (<see cref="VoiceLangMode.Show"/>) its pill words, C (<see cref="VoiceLangMode.Windows"/>) the Windows language's name, or A's line when
    /// Windows' language is not in the list, D (<see cref="VoiceLangMode.Ask"/>) the first-use line.
    /// </summary>
    /// <param name="windowsUiLanguage">Windows' display language, such as "hi-IN".</param>
    public static string LanguageDetail(string? lang, VoiceLangMode mode, string? windowsUiLanguage)
    {
        if (VoiceLanguage.Find(lang) is { } picked) return picked.Code == VoiceLanguage.AutoCode ? AutoDetect : picked.Name;
        return mode switch
        {
            VoiceLangMode.Show => AutoShown,
            VoiceLangMode.Windows => VoiceLanguage.WindowsLanguageCode(windowsUiLanguage) is { } code ? VoiceLanguage.Find(code)!.Name + FromWindows : AutoDetect,
            VoiceLangMode.Ask => AskedOnFirstUse,
            _ => AutoDetect,
        };
    }
}
