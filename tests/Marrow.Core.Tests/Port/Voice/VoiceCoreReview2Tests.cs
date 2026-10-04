using System.Text;
using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// Review round 1 of VT-1, the lead's rulings (2026-10-04, from the OD-0099 Q-V-1 C amendment: "when the speed test finds the PC too slow
// to run the accurate model live, the app asks the user once ... a fast PC (every GPU) is never asked and stays live with the accurate
// model"), and the tests the review found missing.
//
// R1. A GPU timing never steps down and never asks, at any RTF: every GPU stays live with the accurate model. V-10.3's step down is for
//     the processor only.
// R2. The question is asked only when a smaller rung exists on the model's ladder. Otherwise a slow processor timing keeps the model with
//     text after the user stops (live words off) and asks nothing.
// R3. ED-NEW-f-vt-1 item 8 is replaced: the question is asked once whenever a processor timing is too slow for live words and a smaller
//     rung exists, also after the one step down; never twice for the same hardware key.
// R4. "about 1 second", "about 2 seconds": the templates pluralise correctly; otherwise they are Q-V-4 A's.
// Also: the JSON writers never throw on a NaN or infinite double or an enum value outside its names (a settings write must not take the
// app down); what they write reads back as the default, as the lenient read of V-16.6 does for any unreadable value.
public sealed class VoiceCoreReview2Tests
{
    private const string Feature = "VT-1";
    private const string CurrentKey = "1111111111111111111111111111111111111111111111111111111111111111";

    // The REVIEW commit (66aa57560) found these two by reflection so that the file compiled before they existed; now plain calls.
    private static SelfTestDecision Decide(string modelId, double rtf, bool steppedDown, RecommendationBasis basis, bool answeredForThisKey) =>
        SelfTestPlan.Decide(modelId, rtf, steppedDown, basis, answeredForThisKey);

    private static bool AnsweredFor(VoiceHardwareRecord? stored, string currentKey, VoiceTypingSettings typing) =>
        SelfTestPlan.AnsweredFor(stored, currentKey, typing);

    // ---- R1 ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R1, OD-0099 Q-V-1 C: every GPU is never asked and stays live with the accurate model
    [InlineData("large-v3-turbo-q8_0", 0.51, false)]
    [InlineData("large-v3-turbo-q8_0", 3.0, false)]
    [InlineData("large-v3-turbo-q5_0", 0.9, false)]
    [InlineData("large-v3-turbo-q5_0", 0.9, true)]
    [InlineData("large-v3-turbo-q8_0", 0.06, false)]
    [InlineData("large-v3", 2.0, false)]
    public void R1_a_GPU_timing_never_steps_down_and_never_asks_at_any_RTF(string model, double rtf, bool steppedDown) =>
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: true, AskUser: false), SelfTestPlan.Decide(model, rtf, steppedDown, RecommendationBasis.Gpu, answeredForThisKey: false));

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R1: the V-10.3 step down applies to the processor basis only
    public void R1_the_processor_still_steps_down_once() =>
        Assert.Equal(new SelfTestDecision(SelfTestAction.StepDown, "medium-q8_0", LiveWords: null, AskUser: false),
            SelfTestPlan.Decide("large-v3-turbo-q5_0", 0.9, false, RecommendationBasis.Cpu, answeredForThisKey: false));

    // ---- R2 ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R2: no smaller rung, no question; the model is kept with text after the user stops
    [InlineData("small-q5_1", 0.3, false)]
    [InlineData("tiny.en", 0.3, false)]
    [InlineData("large-v3", 0.3, false)]
    [InlineData("small-q5_1", 0.9, true)]
    [InlineData("tiny.en", 0.9, true)]
    [InlineData("large-v3", 0.9, false)]
    public void R2_a_slow_processor_timing_with_no_smaller_rung_keeps_the_model_without_live_words_and_asks_nothing(string model, double rtf, bool steppedDown) =>
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: false, AskUser: false), SelfTestPlan.Decide(model, rtf, steppedDown, RecommendationBasis.Cpu, answeredForThisKey: false));

    // ---- R3 ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R3: asked whenever a processor timing is too slow for live words and a smaller rung exists
    [InlineData("large-v3-turbo-q5_0", 0.3, false)] // slow band, first timing
    [InlineData("small.en-q8_0", 0.5, false)]
    [InlineData("medium-q8_0", 0.3, true)]          // slow band after the step
    [InlineData("medium-q8_0", 0.8, true)]          // too slow after the step
    [InlineData("base.en", 2.0, true)]
    public void R3_a_processor_too_slow_for_live_words_with_a_smaller_rung_is_asked_and_keeps_the_model(string model, double rtf, bool steppedDown) =>
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: null, AskUser: true), SelfTestPlan.Decide(model, rtf, steppedDown, RecommendationBasis.Cpu, answeredForThisKey: false));

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R3: never asked twice for the same hardware key; the stored answer (voiceTyping.liveWordsChoice) decides
    [InlineData("large-v3-turbo-q5_0", 0.3, false)]
    [InlineData("medium-q8_0", 0.8, true)]
    public void R3_an_answer_already_given_for_this_hardware_key_is_not_asked_again(string model, double rtf, bool steppedDown)
    {
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: null, AskUser: true), Decide(model, rtf, steppedDown, RecommendationBasis.Cpu, answeredForThisKey: false));
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: null, AskUser: false), Decide(model, rtf, steppedDown, RecommendationBasis.Cpu, answeredForThisKey: true));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R3: the flag changes nothing where no question applies
    public void R3_the_answered_flag_changes_nothing_where_no_question_applies()
    {
        foreach (var answered in new[] { false, true })
        {
            Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "small-q5_1", false, false), Decide("small-q5_1", 0.3, false, RecommendationBasis.Cpu, answered));
            Assert.Equal(new SelfTestDecision(SelfTestAction.StepDown, "medium-q8_0", null, false), Decide("large-v3-turbo-q5_0", 0.9, false, RecommendationBasis.Cpu, answered));
            Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "large-v3-turbo-q8_0", true, false), Decide("large-v3-turbo-q8_0", 0.3, false, RecommendationBasis.Gpu, answered));
            Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "small-q8_0", true, false), Decide("small-q8_0", 0.1, false, RecommendationBasis.Cpu, answered));
        }
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // lead ruling R3: "the same hardware key" is the stored check's key (V-10.5), with an answer stored
    public void R3_an_answer_counts_for_this_hardware_key_only_when_the_stored_check_has_this_key_and_an_answer_is_stored()
    {
        var answered = VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Accurate };
        var stored = new VoiceHardwareRecord { Key = CurrentKey };
        Assert.True(AnsweredFor(stored, CurrentKey, answered));
        Assert.True(AnsweredFor(stored, CurrentKey, answered with { SlowChoice = LiveWordsChoice.Live }));
        Assert.False(AnsweredFor(stored, CurrentKey, VoiceTypingSettings.Default));                  // never answered
        Assert.False(AnsweredFor(stored with { Key = "2" + CurrentKey[1..] }, CurrentKey, answered)); // a new GPU, driver or app build
        Assert.False(AnsweredFor(null, CurrentKey, answered));                                        // no stored check
    }

    // ---- R4 ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R4, OD-0099 Q-V-4 A: the V-10.4 templates, pluralised correctly
    public void R4_one_second_is_singular_and_every_other_time_plural()
    {
        Assert.Equal("Your NVIDIA GeForce RTX 4080 runs it on the graphics card: text about 1 second after you stop speaking.",
            RecommendationReason.Gpu("NVIDIA GeForce RTX 4080", 0.1));
        Assert.Equal("Your NVIDIA GeForce RTX 4080 runs it on the graphics card: text about 2 seconds after you stop speaking.",
            RecommendationReason.Gpu("NVIDIA GeForce RTX 4080", 0.2));
        Assert.Equal("Your NVIDIA GeForce RTX 4080 runs it on the graphics card: text about 0.5 seconds after you stop speaking.",
            RecommendationReason.Gpu("NVIDIA GeForce RTX 4080", 0.05));
        Assert.Equal("Your NVIDIA GeForce RTX 4080 runs it on the graphics card: text about 1.5 seconds after you stop speaking.",
            RecommendationReason.Gpu("NVIDIA GeForce RTX 4080", 0.15));
        Assert.Equal("On the processor alone Base (English only) answers in about 1 second.", RecommendationReason.Cpu("base.en", 0.1, null, null));
        Assert.Equal("On the processor alone Tiny (English only) answers in about 0.5 seconds; Base (English only) would take 1 second or more.",
            RecommendationReason.Cpu("tiny.en", 0.05, "base.en", 0.1));
        Assert.Equal("On the processor alone Small answers in about 1 second; Medium would take 11 seconds or more.",
            RecommendationReason.Cpu("small-q8_0", 0.1, "medium-q8_0", 1.1));
    }

    // ---- the JSON writers on values they have no wire form for ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.4, V-16.6: a NaN or infinite double is written as null and reads back as the default
    public void The_hardware_record_writes_a_NaN_or_infinite_double_as_null_and_reads_it_back_as_the_default()
    {
        var record = new VoiceHardwareRecord { Key = CurrentKey, Rtf = double.NaN, VramGB = double.PositiveInfinity, FreeVramGB = double.NegativeInfinity, Cores = 8 };
        var json = Encoding.UTF8.GetString(record.ToJson());
        Assert.Contains("\"vramGB\":null,\"freeVramGB\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"rtf\":null", json, StringComparison.Ordinal);
        Assert.Equal(record with { Rtf = 0, VramGB = 0, FreeVramGB = 0 }, VoiceHardwareRecord.FromJson(record.ToJson()));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.1, V-16.6: an enum value outside its wire names is written as the default's name
    public void The_orb_settings_write_an_enum_value_outside_the_names_as_the_default()
    {
        var odd = new VoiceOrbSettings
        {
            Orb = (VoiceOrbKind)9, ListeningStyle = (VoiceListeningStyle)(-1), Attachment = (VoiceAttachment)2, AfterVoice = (VoiceAfterVoice)5,
            Labels = (VoicePillWords)6, LightVoice = (VoiceLightVoice)3, LangMode = (VoiceLangMode)4,
        };
        Assert.Equal(VoiceOrbSettings.Default.ToJson(), odd.ToJson());
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.2, V-16.4, V-16.6: the same for voiceTyping and voiceHardware
    public void The_typing_and_hardware_records_write_an_enum_value_outside_the_names_as_the_default()
    {
        var typing = VoiceTypingSettings.Default with { Engine = (VoiceEngine)7, SlowChoice = (LiveWordsChoice)2 };
        Assert.Equal(VoiceTypingSettings.Default.ToJson(), typing.ToJson()); // an unknown answer is no answer: the key stays absent
        var hardware = new VoiceHardwareRecord { Key = CurrentKey, Origin = (HardwareOrigin)4, Backend = (VoiceBackend)(-3) };
        Assert.Equal(new VoiceHardwareRecord { Key = CurrentKey }.ToJson(), hardware.ToJson());
    }
}
