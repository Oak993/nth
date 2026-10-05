// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-13 B; first-release voice typing, VT-14): the words of the Voice typing page's Speech model section (voice-SPEC V-16.3.2, V-16.3.3).
namespace Marrow.App.Port.Settings;

/// <summary>
/// The section's fixed words, one place each. The hardware card's changing words are Core's <c>HardwareCardText</c>, the list's <c>SpeechModelList</c> and the preference row's
/// <c>SlowPcPreference</c>.
/// </summary>
internal static class SpeechModelText
{
    public const string Section = "Speech model";
    public const string Recommended = "Recommended";
    public const string EnglishOnly = "English only";
    public const string Use = "Use";
    public const string Download = "Download";

    /// <summary>The version control's name, `Version of {model}` (DESIGN voice.js:137).</summary>
    public static string VersionOf(string model) => "Version of " + model;
}
