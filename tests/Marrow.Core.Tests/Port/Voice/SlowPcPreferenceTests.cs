using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-14 (OD-0099 Q-V-1 C; BUILD-PLAN VT-14 "the Q-V-1 C preference row, V2's words"; voice-SPEC V-7.7, V-10.3): the preference row's words, when it is offered, and what an answer does.
// The words are the welcome-voice word set V2 (E:\Droppy\work\design\decisions-2026-10-04\welcome-voice\src\wv_lib.py, WORDS['V2']['slow']).
public sealed class SlowPcPreferenceTests
{
    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-1 C + welcome-voice V2 strings, byte for byte
    public void The_words_are_the_welcome_voice_V2_set()
    {
        Assert.Equal("How should voice typing work here?", SlowPcPreference.Title);
        Assert.Equal("Your PC can’t write your words live with the accurate model. Which matters more to you?", SlowPcPreference.Question);
        Assert.Equal("See my words as I speak", SlowPcPreference.LiveTitle);
        Assert.Equal("Get every word right", SlowPcPreference.AccurateTitle);
        Assert.Equal("Keeps the accurate model; your text appears when you stop speaking.", SlowPcPreference.AccurateDetail);
        Assert.Equal("Uses a smaller model (190 MB more). It hears languages other than English less well, Hindi and Urdu most.", SlowPcPreference.LiveDetail("small-q5_1"));
        Assert.Equal("Uses a smaller model (264 MB more). It hears languages other than English less well, Hindi and Urdu most.", SlowPcPreference.LiveDetail("small-q8_0"));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // R6: the accurate model is the one that was kept before a Live answer took the rung below it
    public void The_accurate_model_is_the_recorded_one_or_the_rung_above_a_live_choice()
    {
        Assert.Null(SlowPcPreference.AccurateModelId(null));
        Assert.Null(SlowPcPreference.AccurateModelId(new VoiceHardwareRecord { Model = "" }));
        Assert.Equal("large-v3-turbo-q5_0", SlowPcPreference.AccurateModelId(new VoiceHardwareRecord { Model = "large-v3-turbo-q5_0", LiveWords = false }));
        Assert.Equal("medium-q8_0", SlowPcPreference.AccurateModelId(new VoiceHardwareRecord { Model = "small-q8_0", LiveWords = true }));
        Assert.Null(SlowPcPreference.AccurateModelId(new VoiceHardwareRecord { Model = "large-v3-turbo-q8_0", LiveWords = true }));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // ED: offered for a measured, too slow processor with something smaller to offer, or once an answer is stored
    public void The_row_is_offered_only_where_the_question_applies()
    {
        var slow = new VoiceHardwareRecord { Backend = VoiceBackend.Cpu, Model = "large-v3-turbo-q5_0", Rtf = 0.3, LiveWords = false };
        Assert.True(SlowPcPreference.IsOffered(slow, VoiceTypingSettings.Default));
        Assert.False(SlowPcPreference.IsOffered(null, VoiceTypingSettings.Default));
        Assert.False(SlowPcPreference.IsOffered(slow with { Rtf = 0 }, VoiceTypingSettings.Default)); // not measured: nothing says it is slow
        Assert.False(SlowPcPreference.IsOffered(slow with { Backend = VoiceBackend.Vulkan }, VoiceTypingSettings.Default)); // R1: a GPU is never asked
        Assert.False(SlowPcPreference.IsOffered(slow with { LiveWords = true }, VoiceTypingSettings.Default)); // fast enough: live already
        Assert.False(SlowPcPreference.IsOffered(slow with { Model = "small-q5_1" }, VoiceTypingSettings.Default)); // R2: nothing smaller to offer
        var answered = VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Live, SlowChoiceKey = "k" };
        Assert.True(SlowPcPreference.IsOffered(slow with { Model = "small-q8_0", LiveWords = true }, answered)); // the answer can be changed back
        Assert.False(SlowPcPreference.IsOffered(null, answered));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // R6 via SelfTestPlan.ApplyAnswer
    public void An_answer_picks_the_model_and_whether_words_are_live()
    {
        var live = SlowPcPreference.Apply("large-v3-turbo-q5_0", LiveWordsChoice.Live);
        Assert.Equal("medium-q8_0", live.ModelId);
        Assert.True(live.LiveWords);
        var accurate = SlowPcPreference.Apply("large-v3-turbo-q5_0", LiveWordsChoice.Accurate);
        Assert.Equal("large-v3-turbo-q5_0", accurate.ModelId);
        Assert.False(accurate.LiveWords);
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // the stored answer shows as chosen; none stored shows the accurate model, which is what R8 keeps until the user answers
    public void The_chosen_option_follows_the_stored_answer()
    {
        Assert.Equal(LiveWordsChoice.Accurate, SlowPcPreference.Shown(VoiceTypingSettings.Default));
        Assert.Equal(LiveWordsChoice.Live, SlowPcPreference.Shown(VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Live }));
        Assert.Equal(LiveWordsChoice.Accurate, SlowPcPreference.Shown(VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Accurate }));
    }
}
