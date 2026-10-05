using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Voice;

namespace Marrow.App.Tests.Port.FirstRun;

// WV-1 (OD-0098 welcome-voice pick, Words V2; the amendment naming where the model is changed): what the words must say. Their byte-equality with wv_lib.py's V2 is the fixture test below.
public sealed class WelcomeVoiceWordsTests
{
    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_section_is_titled_Voice_typing_and_the_path_is_the_speech_model_page()
    {
        Assert.Equal("Voice typing", WelcomeVoiceWords.Title);
        Assert.Equal("Settings > Voice typing > Speech model", WelcomeVoiceWords.SettingsPath);
    }

    [Theory]
    [InlineData(VoiceQuestionState.Yes)]
    [InlineData(VoiceQuestionState.No)]
    [InlineData(VoiceQuestionState.SlowLive)]
    [InlineData(VoiceQuestionState.SlowAcc)]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_messages_that_end_a_choice_name_where_the_model_is_changed(VoiceQuestionState state) =>
        Assert.Contains(WelcomeVoiceWords.SettingsPath, WelcomeVoiceWords.Message(state), StringComparison.Ordinal);

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_yes_message_says_the_orb_shows_the_progress_and_the_no_message_says_the_orb_starts_the_download()
    {
        Assert.Contains("orb", WelcomeVoiceWords.Message(VoiceQuestionState.Yes), StringComparison.Ordinal);
        Assert.Contains("orb", WelcomeVoiceWords.Message(VoiceQuestionState.No), StringComparison.Ordinal);
        Assert.Contains("progress", WelcomeVoiceWords.Message(VoiceQuestionState.Yes), StringComparison.Ordinal);
        Assert.Contains("download", WelcomeVoiceWords.Message(VoiceQuestionState.No), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_slow_question_says_the_smaller_models_handle_other_languages_less_well()
    {
        Assert.Contains("English", WelcomeVoiceWords.Note(VoiceQuestionState.Slow), StringComparison.Ordinal);
        Assert.Contains("Hindi and Urdu", WelcomeVoiceWords.Note(VoiceQuestionState.Slow), StringComparison.Ordinal);
        Assert.Equal(2, WelcomeVoiceWords.Choices(VoiceQuestionState.Slow).Count);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void Ask_offers_yes_and_no_and_the_states_with_nothing_to_say_say_nothing()
    {
        Assert.Equal(["Yes", "No"], WelcomeVoiceWords.Choices(VoiceQuestionState.Ask));
        foreach (var state in new[] { VoiceQuestionState.Yes, VoiceQuestionState.Ready, VoiceQuestionState.No, VoiceQuestionState.SlowLive, VoiceQuestionState.SlowAcc })
            Assert.Empty(WelcomeVoiceWords.Choices(state));
        Assert.Null(WelcomeVoiceWords.Message(VoiceQuestionState.Gone));
        Assert.Empty(WelcomeVoiceWords.Choices(VoiceQuestionState.Gone));
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_words_equal_the_V2_fixture_byte_for_byte()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "welcome-voice-v2.json");
        Assert.True(File.Exists(path), "tests/Marrow.App.Tests/Fixtures/welcome-voice-v2.json (the V2 strings of wv_lib.py:63-73) is not in the repository: the file is on E:\\ only. Until it is added the words are provisional (ED-NEW-cloud-wv-1).");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var state in Enum.GetValues<VoiceQuestionState>())
        {
            var entry = doc.RootElement.GetProperty(state.ToString().ToLowerInvariant());
            Assert.Equal(entry.GetProperty("message").GetString(), WelcomeVoiceWords.Message(state));
        }
    }
}
