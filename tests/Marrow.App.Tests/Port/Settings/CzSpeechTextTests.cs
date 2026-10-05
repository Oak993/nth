using Marrow.Core.Port.Settings;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-11.1, CZ-T81, CZ-T98: the words of the Speech section's three rows, from the stored voice typing settings and the language default.
[Trait("Decision", "ED-251")]
public sealed class CzSpeechTextTests
{
    private const string AutoWords = "Auto-detect: Whisper works out the language";

    [Fact]
    public void The_section_and_row_words_are_the_specs()
    {
        Assert.Equal("Speech", CzSpeechText.Heading);
        Assert.Equal("Speech model", CzSpeechText.ModelTitle);
        Assert.Equal("Language", CzSpeechText.LanguageTitle);
        Assert.Equal("Engine", CzSpeechText.EngineTitle);
        Assert.Equal("Change", CzSpeechText.Change);
    }

    [Theory]
    [InlineData("large-v3-turbo-q8_0", "Large v3 Turbo, balanced 874 MB")]
    [InlineData("small.en-q5_1", "Small (English only), compact 190 MB")]
    public void The_local_engine_shows_the_selected_models_name(string id, string name) => Assert.Equal(name, CzSpeechText.ModelDetail(VoiceEngine.Local, id));

    [Fact]
    public void The_cloud_engine_says_the_model_is_not_used() => Assert.Equal("Not used while the cloud engine is on", CzSpeechText.ModelDetail(VoiceEngine.Cloud, "large-v3-turbo-q8_0"));

    [Theory]
    [InlineData(null)]
    [InlineData("no-such-model")]
    public void The_local_engine_with_no_selected_model_has_no_detail(string? id) => Assert.Equal("", CzSpeechText.ModelDetail(VoiceEngine.Local, id));

    [Fact]
    public void The_engine_detail_is_the_specs_for_local_and_cloud()
    {
        Assert.Equal("On this PC: your voice never leaves this computer", CzSpeechText.EngineDetail(VoiceEngine.Local));
        Assert.Equal("OpenAI cloud, with your own key", CzSpeechText.EngineDetail(VoiceEngine.Cloud));
    }

    [Theory]
    [InlineData("auto", VoiceLangMode.Auto, "en-US", AutoWords)]
    [InlineData("auto", VoiceLangMode.Show, "en-US", AutoWords)]
    [InlineData("auto", VoiceLangMode.Windows, "hi-IN", AutoWords)]
    [InlineData("auto", VoiceLangMode.Ask, "en-US", AutoWords)]
    [InlineData("hi", VoiceLangMode.Auto, "en-US", "Hindi")]
    [InlineData("ur", VoiceLangMode.Ask, "en-US", "Urdu")]
    [InlineData("en", VoiceLangMode.Windows, "hi-IN", "English")]
    public void A_picked_language_is_named_and_an_explicit_auto_is_Auto_detect(string lang, VoiceLangMode mode, string windows, string expected) =>
        Assert.Equal(expected, CzSpeechText.LanguageDetail(lang, mode, windows));

    [Theory]
    [InlineData(VoiceLangMode.Auto, "en-US", AutoWords)]
    [InlineData(VoiceLangMode.Show, "en-US", "Auto-detect, named in the pill while you speak")]
    [InlineData(VoiceLangMode.Windows, "hi-IN", "Hindi (from Windows)")]
    [InlineData(VoiceLangMode.Windows, "en-GB", "English (from Windows)")]
    [InlineData(VoiceLangMode.Windows, "fi-FI", AutoWords)]
    [InlineData(VoiceLangMode.Windows, "", AutoWords)]
    [InlineData(VoiceLangMode.Ask, "en-US", "Asked on first use")]
    public void With_nothing_picked_the_row_follows_the_language_default(VoiceLangMode mode, string windows, string expected) =>
        Assert.Equal(expected, CzSpeechText.LanguageDetail(null, mode, windows));

    [Fact]
    public void A_code_outside_the_list_reads_as_nothing_picked() => Assert.Equal("Asked on first use", CzSpeechText.LanguageDetail("xx", VoiceLangMode.Ask, "en-US"));
}
