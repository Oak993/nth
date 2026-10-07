// PORT-ONLY, owner-ruled addition (OD-0097 B; first-release voice typing, VT-12, voice-SPEC V-12.10): the name the pill shows for a language Whisper heard.
namespace Marrow.Core.Port.Voice;

/// <summary>
/// The name of a Whisper language code for the pill (V-12.10): the Voice typing page's list name when the code is in the list (V-16.2), else Whisper's own English name for it with an initial capital
/// (the tokenizer's table of 99 languages), else the code itself.
/// </summary>
public static class VoiceLanguageNames
{
    // whisper/tokenizer.py LANGUAGES (and whisper.cpp's language table), lower case as Whisper writes them
    private static readonly Dictionary<string, string> Whisper = new(StringComparer.Ordinal)
    {
        ["en"] = "english", ["zh"] = "chinese", ["de"] = "german", ["es"] = "spanish", ["ru"] = "russian", ["ko"] = "korean", ["fr"] = "french", ["ja"] = "japanese",
        ["pt"] = "portuguese", ["tr"] = "turkish", ["pl"] = "polish", ["ca"] = "catalan", ["nl"] = "dutch", ["ar"] = "arabic", ["sv"] = "swedish", ["it"] = "italian",
        ["id"] = "indonesian", ["hi"] = "hindi", ["fi"] = "finnish", ["vi"] = "vietnamese", ["he"] = "hebrew", ["uk"] = "ukrainian", ["el"] = "greek", ["ms"] = "malay",
        ["cs"] = "czech", ["ro"] = "romanian", ["da"] = "danish", ["hu"] = "hungarian", ["ta"] = "tamil", ["no"] = "norwegian", ["th"] = "thai", ["ur"] = "urdu",
        ["hr"] = "croatian", ["bg"] = "bulgarian", ["lt"] = "lithuanian", ["la"] = "latin", ["mi"] = "maori", ["ml"] = "malayalam", ["cy"] = "welsh", ["sk"] = "slovak",
        ["te"] = "telugu", ["fa"] = "persian", ["lv"] = "latvian", ["bn"] = "bengali", ["sr"] = "serbian", ["az"] = "azerbaijani", ["sl"] = "slovenian", ["kn"] = "kannada",
        ["et"] = "estonian", ["mk"] = "macedonian", ["br"] = "breton", ["eu"] = "basque", ["is"] = "icelandic", ["hy"] = "armenian", ["ne"] = "nepali", ["mn"] = "mongolian",
        ["bs"] = "bosnian", ["kk"] = "kazakh", ["sq"] = "albanian", ["sw"] = "swahili", ["gl"] = "galician", ["mr"] = "marathi", ["pa"] = "punjabi", ["si"] = "sinhala",
        ["km"] = "khmer", ["sn"] = "shona", ["yo"] = "yoruba", ["so"] = "somali", ["af"] = "afrikaans", ["oc"] = "occitan", ["ka"] = "georgian", ["be"] = "belarusian",
        ["tg"] = "tajik", ["sd"] = "sindhi", ["gu"] = "gujarati", ["am"] = "amharic", ["yi"] = "yiddish", ["lo"] = "lao", ["uz"] = "uzbek", ["fo"] = "faroese",
        ["ht"] = "haitian creole", ["ps"] = "pashto", ["tk"] = "turkmen", ["nn"] = "nynorsk", ["mt"] = "maltese", ["sa"] = "sanskrit", ["lb"] = "luxembourgish",
        ["my"] = "myanmar", ["bo"] = "tibetan", ["tl"] = "tagalog", ["mg"] = "malagasy", ["as"] = "assamese", ["tt"] = "tatar", ["haw"] = "hawaiian", ["ln"] = "lingala",
        ["ha"] = "hausa", ["ba"] = "bashkir", ["jw"] = "javanese", ["su"] = "sundanese", ["yue"] = "cantonese",
    };

    /// <summary>The name to show for <paramref name="code"/> (case-insensitive), or null for no code.</summary>
    public static string? NameOf(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var lower = code.Trim().ToLowerInvariant();
        if (VoiceLanguage.IsKnownCode(lower)) return VoiceLanguage.Find(lower)!.Name;
        if (!Whisper.TryGetValue(lower, out var english)) return code.Trim();
        return Capitalised(english);
    }

    private static string Capitalised(string text)
    {
        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++) words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        return string.Join(' ', words);
    }
}
