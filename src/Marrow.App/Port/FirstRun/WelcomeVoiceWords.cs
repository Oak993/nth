// PORT-ONLY, owner-ruled addition (OD-0098 welcome-voice pick "Place A, Words V2"; first-release WV-1): the words of the Welcome voice question.
using Marrow.App.Port.Voice;

namespace Marrow.App.Port.FirstRun;

/// <summary>
/// The words of the Welcome voice section, by state: V2 "Warm" of the owner's pick, byte for byte from <c>WORDS['V2']</c> in porting/first-release/design-src/welcome-voice/wv_lib.py
/// (checked against tests/fixtures/welcome-voice/v2.json, generated from that file). A template keeps wv_lib's three placeholders: <c>{mb}</c> the size of the model the recommendation
/// downloads, <c>{small}</c> the size of the smaller live model the slow question offers, <c>{path}</c> where the model is changed (<see cref="SettingsPath"/>).
/// Every string is here and nowhere else. The state fields are wv_lib's: <c>t</c> the heading, <c>b</c> the body; Ask has <c>yes</c> and <c>no</c>; Slow has <c>live_t</c>, <c>live_d</c>,
/// <c>acc_t</c>, <c>acc_d</c> and <c>foot</c>. V2's <c>done</c> is a button of the sheet place only (wv_lib.py: <c>on_buttons = place in ('D',)</c>), not of Place A.
/// </summary>
public static class WelcomeVoiceWords
{
    /// <summary>The section's title (wv_lib.py: the <c>sh</c> head of place A; OD-0098 "a 'Voice typing' section").</summary>
    public const string Title = "Voice typing";

    /// <summary>wv_lib.py <c>PATH</c> (OD-0098's amendment; the page and section voice-SPEC V-16.3.2 defines).</summary>
    public const string SettingsPath = "Settings > Voice typing > Speech model";

    /// <summary>V2's string for <paramref name="field"/> of <paramref name="state"/>, with its placeholders; null where V2 has none (Gone).</summary>
    public static string? Template(VoiceQuestionState state, string field) => (Name(state), field) switch
    {
            ("ask", "t") => "Talk instead of type?",
            ("ask", "b") => "Nth can turn your voice into text, right here on your PC. It needs a one-time {mb} download.",
            ("ask", "yes") => "Yes, set it up",
            ("ask", "no") => "Maybe later",
            ("yes", "t") => "Setting up voice typing",
            ("yes", "b") => "It will be ready as soon as the download finishes. Click the voice orb in the composer any time to see how it\u2019s going. Want a different model? It\u2019s in {path}.",
            ("ready", "t") => "Voice typing is ready",
            ("ready", "b") => "Click the voice orb in the composer and start talking. Want a different model? It\u2019s in {path}.",
            ("no", "t") => "No problem",
            ("no", "b") => "Nothing was downloaded. Whenever you\u2019d like voice typing, click the voice orb in the composer and the download starts. You can pick the model in {path}.",
            ("slow", "t") => "How should voice typing work here?",
            ("slow", "b") => "Your PC can\u2019t write your words live with the accurate model. Which matters more to you?",
            ("slow", "live_t") => "See my words as I speak",
            ("slow", "live_d") => "Uses a smaller model ({small} more). It hears languages other than English less well, Hindi and Urdu most.",
            ("slow", "acc_t") => "Get every word right",
            ("slow", "acc_d") => "Keeps the accurate model; your text appears when you stop speaking.",
            ("slow", "foot") => "You can switch any time in {path}.",
            ("slowlive", "t") => "Live words it is",
            ("slowlive", "b") => "The smaller model is on its way. You can switch any time in {path}.",
            ("slowacc", "t") => "Accuracy it is",
            ("slowacc", "b") => "Your text appears when you stop speaking. You can switch any time in {path}.",
        _ => null,
    };

    /// <summary>The heading (<c>t</c>) of a state; null for Gone.</summary>
    public static string? Heading(VoiceQuestionState state) => Template(state, "t");

    /// <summary>The body (<c>b</c>) of a state with its placeholders filled; null for Gone, or while a size it needs is not known.</summary>
    public static string? Body(VoiceQuestionState state, string? mb, string? small) => Fill(Template(state, "b"), mb, small);

    /// <summary>The buttons in the order wv_lib's markup draws them. Ask: no, yes. Slow: live words, then accurate (the two options' headings).</summary>
    public static IReadOnlyList<string> Choices(VoiceQuestionState state) => state switch
    {
        VoiceQuestionState.Ask => [Template(state, "no")!, Template(state, "yes")!],
        VoiceQuestionState.Slow => [Template(state, "live_t")!, Template(state, "acc_t")!],
        _ => [],
    };

    /// <summary>The line under each choice of the slow question (<c>live_d</c>, <c>acc_d</c>), filled; empty for the other states. The live one names <c>{small}</c>.</summary>
    public static IReadOnlyList<string?> Details(VoiceQuestionState state, string? small) => state == VoiceQuestionState.Slow
        ? [Fill(Template(state, "live_d"), null, small), Fill(Template(state, "acc_d"), null, small)]
        : [];

    /// <summary>The slow question's last line (<c>foot</c>), filled; null for the other states.</summary>
    public static string? Foot(VoiceQuestionState state) => Fill(Template(state, "foot"), null, null);

    /// <summary>Fills wv_lib's placeholders; null when a template needs a size that is not known.</summary>
    public static string? Fill(string? template, string? mb, string? small)
    {
        if (template is null) return null;
        if (template.Contains("{mb}", StringComparison.Ordinal) && mb is null) return null;
        if (template.Contains("{small}", StringComparison.Ordinal) && small is null) return null;
        return template.Replace("{mb}", mb, StringComparison.Ordinal).Replace("{small}", small, StringComparison.Ordinal).Replace("{path}", SettingsPath, StringComparison.Ordinal);
    }

    private static string Name(VoiceQuestionState state) => state switch
    {
        VoiceQuestionState.Ask => "ask",
        VoiceQuestionState.Yes => "yes",
        VoiceQuestionState.Ready => "ready",
        VoiceQuestionState.No => "no",
        VoiceQuestionState.Slow => "slow",
        VoiceQuestionState.SlowLive => "slowlive",
        VoiceQuestionState.SlowAcc => "slowacc",
        _ => "",
    };
}
