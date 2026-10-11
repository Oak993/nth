// PORT-ONLY, owner-ruled addition (OD-0092, OD-0097, OD-0099 Q-V-19 B, OD-0098 / OD-0099 Q-V-1 C; first-release voice typing, VT-1):
// the voiceTyping settings (voice-SPEC V-16.2) with the Microphone row's pick (Q-V-19 B) and the slow-PC answer (Q-V-1 C).
using System.Text.Json;

namespace Marrow.Core.Port.Voice;

/// <summary>The recognition engine (V-16.2): on this PC (default) or the OpenAI cloud engine (opt-in, off by default: OD-0099 Q-V-15).</summary>
public enum VoiceEngine { Local, Cloud }

/// <summary>
/// The answer to the slow-PC question (OD-0099 Q-V-1 C): live words with a smaller model (<see cref="Live"/>), or the text after you
/// stop with the accurate model (<see cref="Accurate"/>). Stored as <c>liveWordsChoice</c>; absent until the user is asked.
/// </summary>
public enum LiveWordsChoice { Live, Accurate }

/// <summary>
/// The <c>voiceTyping</c> settings. <see cref="Lang"/> is null when nothing is picked (the language default of V-7.14 decides),
/// "auto" when Auto-detect is picked, or a Whisper code of the list (V-16.2). <see cref="Model"/> is a catalogue id or null.
/// <see cref="Mic"/> is the chosen input device's id, null for "Windows default" (Q-V-19 B: it is the first item and the default).
/// </summary>
public sealed record VoiceTypingSettings
{
    /// <summary>The settings key.</summary>
    public const string StoreKey = "voiceTyping";

    /// <summary>The Microphone row's first item (OD-0099 Q-V-19 B: "Windows default" is the first item and the default).</summary>
    public const string WindowsDefaultMicrophone = "Windows default";

    public VoiceEngine Engine { get; init; } = VoiceEngine.Local;
    public string? Lang { get; init; }
    public string? Model { get; init; }
    public string? Mic { get; init; }
    public LiveWordsChoice? SlowChoice { get; init; }

    /// <summary>
    /// The hardware part of the key (<see cref="HardwareKey.ComputeHardwarePart(HardwareKeyInputs)"/>) the answer was given under, stored
    /// as <c>liveWordsChoiceKey</c> beside it (lead ruling R5, 2026-10-04, from the OD-0099 Q-V-1 C amendment); null when none is stored.
    /// </summary>
    public string? SlowChoiceKey { get; init; }

    /// <summary>
    /// ED-NEW-voicefix-4: the Hindustani code ("hi" or "ur") of the last session whose text was written in Hindi or Urdu, stored as <c>lastHindustani</c>; null until one
    /// was. Auto-detect weighs it (<c>AutoLanguagePolicy</c>) and writes Hindustani in its script.
    /// </summary>
    public string? LastHindustani { get; init; }

    public static VoiceTypingSettings Default { get; } = new();

    private static readonly string[] EngineNames = ["local", "cloud"];
    private static readonly string[] ChoiceNames = ["live", "accurate"];

    /// <summary>
    /// Compact JSON: <c>{"engine":"local","lang":null,"model":null}</c> for the defaults (V-16.2 as written). ED: <c>mic</c> and
    /// <c>liveWordsChoice</c> (and its <c>liveWordsChoiceKey</c>, R5) are written only when set, so the default bytes stay V-16.2's; so is <c>lastHindustani</c> (ED-NEW-voicefix-4).
    /// </summary>
    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, VoiceJson.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("engine", VoiceJson.WireName(EngineNames, Engine, Default.Engine));
            if (Lang is null) w.WriteNull("lang"); else w.WriteString("lang", Lang);
            if (Model is null) w.WriteNull("model"); else w.WriteString("model", Model);
            if (Mic is not null) w.WriteString("mic", Mic);
            if (SlowChoice is { } choice && (int)choice >= 0 && (int)choice < ChoiceNames.Length) w.WriteString("liveWordsChoice", ChoiceNames[(int)choice]); // an unknown answer is no answer
            if (SlowChoiceKey is not null) w.WriteString("liveWordsChoiceKey", SlowChoiceKey);
            if (IsHindustani(LastHindustani)) w.WriteString("lastHindustani", LastHindustani);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Lenient read (V-16.2, V-16.6): a missing <c>lang</c> is null and so is an unknown string (a code outside the list, any case other
    /// than the list's); an unknown engine is Local; a model that is not in the catalogue is null; an empty or non-string <c>mic</c> or <c>liveWordsChoiceKey</c> is null;
    /// a <c>lastHindustani</c> other than "hi" or "ur" is null.
    /// </summary>
    public static VoiceTypingSettings FromJson(ReadOnlySpan<byte> json) => VoiceJson.ReadObject(json, Default, o =>
    {
        var lang = VoiceJson.Text(o, "lang");
        var model = VoiceJson.Text(o, "model");
        var mic = VoiceJson.Text(o, "mic");
        var choiceText = VoiceJson.Text(o, "liveWordsChoice");
        var choiceKey = VoiceJson.Text(o, "liveWordsChoiceKey");
        var hindustani = VoiceJson.Text(o, "lastHindustani");
        var choice = choiceText is null ? -1 : Array.IndexOf(ChoiceNames, choiceText);
        return new VoiceTypingSettings
        {
            Engine = VoiceJson.Name(o, "engine", EngineNames, Default.Engine),
            Lang = VoiceLanguage.Find(lang) is not null ? lang : null,
            Model = ModelCatalog.Find(model) is not null ? model : null,
            Mic = string.IsNullOrEmpty(mic) ? null : mic,
            SlowChoice = choice < 0 ? null : (LiveWordsChoice)choice,
            SlowChoiceKey = string.IsNullOrEmpty(choiceKey) ? null : choiceKey,
            LastHindustani = IsHindustani(hindustani) ? hindustani : null,
        };
    });

    private static bool IsHindustani(string? code) => code is "hi" or "ur";
}

/// <summary>The one-time privacy note per engine (V-16.5, OD-0099 Q-V-8 A): whether each engine's note has been shown.</summary>
public sealed record VoiceNotesSettings(bool Local, bool Cloud)
{
    /// <summary>The settings key.</summary>
    public const string StoreKey = "voiceNotes";

    public static VoiceNotesSettings Default { get; } = new(false, false);

    /// <summary><c>{"local":false,"cloud":false}</c> for the defaults.</summary>
    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, VoiceJson.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteBoolean("local", Local);
            w.WriteBoolean("cloud", Cloud);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Lenient read (V-16.6): a missing or non-boolean value is false.</summary>
    public static VoiceNotesSettings FromJson(ReadOnlySpan<byte> json) => VoiceJson.ReadObject(json, Default,
        o => new VoiceNotesSettings(VoiceJson.Bool(o, "local", false), VoiceJson.Bool(o, "cloud", false)));
}
