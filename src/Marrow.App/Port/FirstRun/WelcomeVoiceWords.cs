// PORT-ONLY, owner-ruled addition (OD-0098 welcome-voice pick "Place A, Words V2"; first-release WV-1): the words of the Welcome voice question.
using Marrow.App.Port.Voice;

namespace Marrow.App.Port.FirstRun;

/// <summary>
/// The words of the Welcome voice section, by state. UNRESOLVED (ED-NEW-cloud-wv-1): V2's own strings are in wv_lib.py:63-73 on E:\, which is not in the repository, so these are the minimal words that say
/// what the owner's OD-0098 amendments say and nothing else; the V2 fixture test fails until the file is added and the words are replaced by it. Every string is here and nowhere else.
/// </summary>
public static class WelcomeVoiceWords
{
    /// <summary>The section's title (OD-0098: "a 'Voice typing' section").</summary>
    public const string Title = "Voice typing";

    /// <summary>Where the model is changed (OD-0098's amendment; the page and section voice-SPEC defines).</summary>
    public const string SettingsPath = "Settings > Voice typing > Speech model";

    private const string Where = " To change the model, go to " + SettingsPath + ".";

    public static string? Message(VoiceQuestionState state) => state switch
    {
        VoiceQuestionState.Ask => "Would you like to use voice typing?",
        VoiceQuestionState.Yes => "Voice typing will work once its speech model has downloaded. Click the voice orb in the composer to see the progress." + Where,
        VoiceQuestionState.Ready => "Voice typing is ready. Click the voice orb in the composer to use it.",
        VoiceQuestionState.No => "To start the speech model download any time, click the voice orb in the composer." + Where,
        VoiceQuestionState.Slow => "This PC is too slow to show live words with the most accurate model. Which do you prefer?",
        VoiceQuestionState.SlowLive => "Live words, with a smaller model." + Where,
        VoiceQuestionState.SlowAcc => "Text after you stop, with the accurate model." + Where,
        _ => null,
    };

    /// <summary>Q-V-1 C's general statement about the smaller models (OD-0098's amendment: "state it generally that the other models don't support multi-lingual capabilities"); Hindi and Urdu from OD-0099.</summary>
    public static string? Note(VoiceQuestionState state) => state == VoiceQuestionState.Slow
        ? "The smaller models handle languages other than English less well: they mis-hear more, Hindi and Urdu most."
        : null;

    /// <summary>The buttons, in order. Ask: yes, no. Slow: live words with a smaller model, text after you stop with the accurate model (the two answers of Q-V-1 C).</summary>
    public static IReadOnlyList<string> Choices(VoiceQuestionState state) => state switch
    {
        VoiceQuestionState.Ask => ["Yes", "No"],
        VoiceQuestionState.Slow => ["Live words, smaller model", "Text after I stop, accurate model"],
        _ => [],
    };
}
