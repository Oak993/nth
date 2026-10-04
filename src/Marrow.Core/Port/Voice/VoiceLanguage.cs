// PORT-ONLY, owner-ruled addition (OD-0092, OD-0097, OD-0099 Q-V-17; first-release voice typing, VT-1): the language list of the
// Voice typing page (voice-SPEC V-16.2), VoiceLanguage.Resolve (V-7.14) and the reset rule (V-7.14, V-12.11).
namespace Marrow.Core.Port.Voice;

/// <summary>One entry of the language list: the name shown and the Whisper code (or "auto").</summary>
public sealed record VoiceLanguageInfo(string Name, string Code);

/// <summary>
/// What a session does about the language (V-7.14): the code it forces, or "auto" to auto-detect; whether the detected language is
/// named in the pill (V-12.10); and whether the click that would start listening opens the language question instead (V-12.11).
/// </summary>
public sealed record LanguageResolution(string Language, bool Named, bool Ask);

/// <summary>The two settings a language-default change touches together (the reset rule).</summary>
public readonly record struct LanguageDefaultChange(VoiceOrbSettings Orb, VoiceTypingSettings Typing);

/// <summary>The language list, <see cref="Resolve"/> and the settings changes that store a language.</summary>
public static class VoiceLanguage
{
    /// <summary>The code of the Auto-detect item.</summary>
    public const string AutoCode = "auto";

    /// <summary>The Auto-detect line of the Language row. ENGINEERING DECISION ED-267 (6) / OD-0099 Q-V-17.</summary>
    public const string AutoDetail = "Whisper works out the language; pick one if Hindi and Urdu get mixed up";

    /// <summary>The languages in the design's order with their codes (V-16.2; DESIGN voice.js:26). Auto-detect is first.</summary>
    public static IReadOnlyList<VoiceLanguageInfo> Languages { get; } =
    [
        new("Auto-detect", "auto"), new("English", "en"), new("Hindi", "hi"), new("Urdu", "ur"), new("Arabic", "ar"),
        new("Bengali", "bn"), new("Chinese", "zh"), new("French", "fr"), new("German", "de"), new("Indonesian", "id"),
        new("Italian", "it"), new("Japanese", "ja"), new("Korean", "ko"), new("Persian", "fa"), new("Portuguese", "pt"),
        new("Punjabi", "pa"), new("Russian", "ru"), new("Spanish", "es"), new("Tamil", "ta"), new("Turkish", "tr"),
        new("Vietnamese", "vi"),
    ];

    /// <summary>The answers of the language question, in its order (V-12.11): English, Hindi, Urdu, Auto-detect.</summary>
    public static IReadOnlyList<string> QuestionAnswers { get; } = ["en", "hi", "ur", "auto"];

    /// <summary>The list entry with this exact code (including "auto"), or null.</summary>
    public static VoiceLanguageInfo? Find(string? code)
    {
        if (code is null) return null;
        foreach (var language in Languages)
            if (string.Equals(language.Code, code, StringComparison.Ordinal)) return language;
        return null;
    }

    /// <summary>True for a Whisper code of the list (not "auto").</summary>
    public static bool IsKnownCode(string? code) => code is not AutoCode && Find(code) is not null;

    /// <summary>
    /// The list code for a Windows display language such as "hi-IN" (V-7.14 rule 3 C): the part before the first '-' or '_', lower case
    /// (what <c>CultureInfo.TwoLetterISOLanguageName</c> gives for a two-letter language), when that is in the list; otherwise null.
    /// </summary>
    public static string? WindowsLanguageCode(string? windowsUiLanguage)
    {
        if (string.IsNullOrWhiteSpace(windowsUiLanguage)) return null;
        var text = windowsUiLanguage.Trim();
        var cut = text.IndexOfAny(['-', '_']);
        var code = (cut < 0 ? text : text[..cut]).ToLowerInvariant();
        return IsKnownCode(code) ? code : null;
    }

    /// <summary>
    /// V-7.14. <paramref name="lang"/> is <c>voiceTyping.lang</c>: a Whisper code forces it; "auto" auto-detects and is named only under
    /// <see cref="VoiceLangMode.Show"/>; null (nothing picked) leaves the decision to <paramref name="langMode"/>. A code outside the list
    /// reads as null (V-16.2's lenient read). <paramref name="windowsUiLanguage"/> is the display language read at the session start.
    /// </summary>
    public static LanguageResolution Resolve(string? lang, VoiceLangMode langMode, string? windowsUiLanguage)
    {
        if (IsKnownCode(lang)) return new LanguageResolution(lang!, false, false);
        if (lang == AutoCode) return new LanguageResolution(AutoCode, langMode == VoiceLangMode.Show, false);
        return langMode switch
        {
            VoiceLangMode.Auto => new LanguageResolution(AutoCode, false, false),
            VoiceLangMode.Show => new LanguageResolution(AutoCode, true, false),
            VoiceLangMode.Windows => new LanguageResolution(WindowsLanguageCode(windowsUiLanguage) ?? AutoCode, false, false),
            VoiceLangMode.Ask => new LanguageResolution(AutoCode, false, true),
        };
    }

    /// <summary>
    /// The reset rule (V-7.14): picking any option of the Customization row, also the one already picked, stores the language default
    /// and sets <c>voiceTyping.lang</c> back to null, so the default just chosen applies at once and D asks again at the next first click.
    /// </summary>
    public static LanguageDefaultChange WithLanguageDefault(VoiceOrbSettings orb, VoiceTypingSettings typing, VoiceLangMode picked) =>
        new(orb with { LangMode = picked }, typing with { Lang = null });

    /// <summary>An answer to the language question stores en, hi, ur or auto at once (V-12.11).</summary>
    public static VoiceTypingSettings WithQuestionAnswer(VoiceTypingSettings typing, string answer)
    {
        if (answer is null || !QuestionAnswers.Contains(answer, StringComparer.Ordinal))
            throw new ArgumentException("not an answer of the language question: " + answer, nameof(answer));
        return typing with { Lang = answer };
    }

    /// <summary>A pick in the Voice typing page's Language menu stores it explicitly, "auto" or the code (V-7.14), overriding the default.</summary>
    public static VoiceTypingSettings WithPagePick(VoiceTypingSettings typing, string code)
    {
        if (code is null || Find(code) is null)
            throw new ArgumentException("not a language of the list: " + code, nameof(code));
        return typing with { Lang = code };
    }
}
