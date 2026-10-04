using System.Text;
using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// The lead's rulings R5 to R7 (2026-10-04, from the OD-0099 Q-V-1 C amendment: "asks the user once ... the answer is kept in Settings >
// Voice typing and can be changed there"), answering the round-3 re-check (VoiceCoreReview3Tests, 97edbff23).
//
// R5. The answer is stored with the key it was given under: voiceTyping.liveWordsChoiceKey, the HARDWARE part of the V-10.5 key only
//     (adapter description, driver version, processor name, memory GB; not the app version and build, not the Whisper.net runtime).
//     AnsweredFor compares it with today's hardware part, never with the stored voiceHardware entry, which every check rewrites. The question
//     comes back only when the hardware part changes, never after an app or runtime update.
// R6. A stored "live" answer takes the next smaller rung on the model's ladder with live words on, not timed again; a stored "accurate"
//     answer keeps the model with text after the user stops.
// R7. A question dismissed without an answer stores nothing, so it is asked again on the same key at the next self-test (the default
//     until the question's design, OD-0070, says otherwise).
public sealed class VoiceCoreReview4Tests
{
    private const string Feature = "VT-1";

    private static readonly HardwareKeyInputs PcA = new("AMD Radeon RX 7900 XTX", "31.0.24027.1012", "AMD Ryzen 7 7700X 8-Core Processor", 31, "1.0.0+100", "1.8.1");

    // The REVIEW commit (46af30d03) reached these by reflection so that the file compiled before they existed; now plain calls.
    private static string HardwarePart(HardwareKeyInputs inputs) => HardwareKey.ComputeHardwarePart(inputs);

    private static bool AnsweredFor(VoiceTypingSettings typing, string currentHardwarePart) => SelfTestPlan.AnsweredFor(typing, currentHardwarePart);

    private static VoiceTypingSettings WithSlowAnswer(VoiceTypingSettings typing, LiveWordsChoice? answer, string hardwarePart) =>
        SelfTestPlan.WithSlowAnswer(typing, answer, hardwarePart);

    private static SelfTestDecision ApplyAnswer(string modelId, LiveWordsChoice answer) => SelfTestPlan.ApplyAnswer(modelId, answer);

    private static string? AnswerKey(VoiceTypingSettings typing) => typing.SlowChoiceKey;

    private static SelfTestDecision SlowCpu(string model, VoiceTypingSettings typing, string hardwarePart) =>
        SelfTestPlan.Decide(model, 0.3, false, RecommendationBasis.Cpu, AnsweredFor(typing, hardwarePart));

    // ---- R5 ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // lead ruling R5: the hardware part is adapter, driver, processor and memory, not the app build or the runtime
    public void R5_the_hardware_part_changes_with_the_hardware_only()
    {
        var part = HardwarePart(PcA);
        Assert.Equal(64, part.Length);
        Assert.All(part, c => Assert.True(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));
        Assert.Equal(part, HardwarePart(PcA with { App = "2.0.0+300" }));
        Assert.Equal(part, HardwarePart(PcA with { RuntimeVersion = "1.9.9" }));
        var changed = new[]
        {
            PcA with { AdapterDescription = "NVIDIA GeForce RTX 4080" }, PcA with { DriverVersion = "32.0.11021.1011" },
            PcA with { CpuName = "Intel(R) Core(TM) i5-1235U" }, PcA with { RamGB = 15 },
        };
        foreach (var c in changed) Assert.NotEqual(part, HardwarePart(c));
        Assert.Equal(changed.Length, changed.Select(HardwarePart).Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(HardwareKey.Compute(PcA), part); // not the V-10.5 key itself
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // lead ruling R5: liveWordsChoiceKey beside liveWordsChoice; the default bytes stay V-16.2's
    public void R5_the_answer_is_stored_with_its_hardware_part_and_reads_back()
    {
        var part = HardwarePart(PcA);
        var typing = WithSlowAnswer(VoiceTypingSettings.Default, LiveWordsChoice.Accurate, part);
        Assert.Equal(LiveWordsChoice.Accurate, typing.SlowChoice);
        Assert.Equal(part, AnswerKey(typing));
        Assert.Equal("{\"engine\":\"local\",\"lang\":null,\"model\":null,\"liveWordsChoice\":\"accurate\",\"liveWordsChoiceKey\":\"" + part + "\"}",
            Encoding.UTF8.GetString(typing.ToJson()));
        Assert.Equal(typing, VoiceTypingSettings.FromJson(typing.ToJson()));
        Assert.Equal("{\"engine\":\"local\",\"lang\":null,\"model\":null}", Encoding.UTF8.GetString(VoiceTypingSettings.Default.ToJson()));
        Assert.Null(AnswerKey(VoiceTypingSettings.Default));
        foreach (var junk in new[] { "{\"liveWordsChoiceKey\":\"\"}", "{\"liveWordsChoiceKey\":7}", "{\"liveWordsChoiceKey\":null}" })
            Assert.Null(AnswerKey(VoiceTypingSettings.FromJson(Encoding.UTF8.GetBytes(junk))));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // lead ruling R5: AnsweredFor compares the answer's key with today's hardware part
    public void R5_an_answer_counts_only_with_its_key_equal_to_todays_hardware_part()
    {
        var part = HardwarePart(PcA);
        var other = HardwarePart(PcA with { DriverVersion = "32.0.11021.1011" });
        foreach (var answer in new[] { LiveWordsChoice.Live, LiveWordsChoice.Accurate })
        {
            var typing = WithSlowAnswer(VoiceTypingSettings.Default, answer, part);
            Assert.True(AnsweredFor(typing, part));
            Assert.False(AnsweredFor(typing, other));
        }
        Assert.False(AnsweredFor(VoiceTypingSettings.Default, part));                                // never answered
        Assert.False(AnsweredFor(VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Live }, part)); // an answer with no key (stored before R5)
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // lead ruling R5: a key with no readable answer beside it, or a key that differs in any way, is no answer (mutants P6, P14)
    public void R5_a_key_without_an_answer_or_with_another_spelling_counts_as_not_answered()
    {
        var part = HardwarePart(PcA);
        Assert.False(AnsweredFor(VoiceTypingSettings.Default with { SlowChoiceKey = part }, part));
        var unreadable = VoiceTypingSettings.FromJson(Encoding.UTF8.GetBytes("{\"liveWordsChoice\":\"maybe\",\"liveWordsChoiceKey\":\"" + part + "\"}"));
        Assert.Equal(part, AnswerKey(unreadable));
        Assert.False(AnsweredFor(unreadable, part));
        var answered = WithSlowAnswer(VoiceTypingSettings.Default, LiveWordsChoice.Live, part);
        Assert.False(AnsweredFor(answered with { SlowChoiceKey = part.ToUpperInvariant() }, part)); // compared as stored, like HardwareKey.IsStale
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // lead ruling R5: the question comes back on new hardware only, whatever the order of the timings
    public void R5_the_question_comes_back_on_new_hardware_only()
    {
        var typing = WithSlowAnswer(VoiceTypingSettings.Default, LiveWordsChoice.Accurate, HardwarePart(PcA));
        Assert.False(SlowCpu("large-v3-turbo-q5_0", typing, HardwarePart(PcA with { App = "1.0.1+101", RuntimeVersion = "1.8.2" })).AskUser); // an update
        var newDriver = HardwarePart(PcA with { DriverVersion = "32.0.11021.1011" });
        Assert.True(SlowCpu("large-v3-turbo-q5_0", typing, newDriver).AskUser);
        // a fast timing under the new driver asks nothing and stores nothing; a later slow one under it is still asked
        Assert.False(SelfTestPlan.Decide("large-v3-turbo-q5_0", 0.1, false, RecommendationBasis.Cpu, AnsweredFor(typing, newDriver)).AskUser);
        Assert.True(SlowCpu("large-v3-turbo-q5_0", typing, newDriver).AskUser);
        var answeredAgain = WithSlowAnswer(typing, LiveWordsChoice.Live, newDriver);
        Assert.False(SlowCpu("large-v3-turbo-q5_0", answeredAgain, newDriver).AskUser);
        Assert.True(SlowCpu("large-v3-turbo-q5_0", answeredAgain, HardwarePart(PcA)).AskUser); // the old driver back is another hardware part again
    }

    // ---- R6 ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R6: "live" takes the next smaller rung with live words on, not timed again
    [InlineData("large-v3-turbo-q5_0", "medium-q8_0")]
    [InlineData("medium-q8_0", "small-q8_0")]
    [InlineData("small-q8_0", "small-q5_1")]
    [InlineData("small.en-q8_0", "base.en")]
    [InlineData("base.en", "tiny.en")]
    public void R6_a_live_answer_takes_the_next_smaller_rung_with_live_words_on(string model, string smaller) =>
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, smaller, LiveWords: true, AskUser: false), ApplyAnswer(model, LiveWordsChoice.Live));

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R6: "accurate" keeps the model with text after the user stops
    [InlineData("large-v3-turbo-q5_0")]
    [InlineData("medium-q8_0")]
    [InlineData("base.en")]
    public void R6_an_accurate_answer_keeps_the_model_with_text_after_the_user_stops(string model) =>
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: false, AskUser: false), ApplyAnswer(model, LiveWordsChoice.Accurate));

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R2 with R6: with no smaller rung there is nothing live to take; the model stays, text after the stop
    [InlineData("small-q5_1")]
    [InlineData("tiny.en")]
    [InlineData("large-v3")]
    public void R6_a_live_answer_on_a_model_with_no_smaller_rung_keeps_it_with_text_after_the_stop(string model) =>
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: false, AskUser: false), ApplyAnswer(model, LiveWordsChoice.Live));

    // ---- R8 ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R8, OD-0099 welcome-voice pick: "until answered the accurate model is used"
    [InlineData("large-v3-turbo-q5_0", 0.8, true)] // too slow after the step, asked
    [InlineData("small.en-q8_0", 0.3, false)]      // slow band, asked
    public void R8_an_unanswered_or_dismissed_question_keeps_the_accurate_model_with_live_words_off(string model, double rtf, bool steppedDown)
    {
        var part = HardwarePart(PcA);
        var dismissed = WithSlowAnswer(VoiceTypingSettings.Default, null, part);
        var asked = SelfTestPlan.Decide(model, rtf, steppedDown, RecommendationBasis.Cpu, AnsweredFor(dismissed, part));
        Assert.True(asked.AskUser);
        var outcome = SelfTestPlan.ApplyAnswer(asked.ModelId, dismissed.SlowChoice);
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: false, AskUser: false), outcome);
        // the voiceHardware entry is written from the outcome: liveWords false
        var stored = new VoiceHardwareRecord { Key = HardwareKey.Compute(PcA), Model = outcome.ModelId, Rtf = rtf, LiveWords = outcome.LiveWords!.Value };
        Assert.Contains("\"liveWords\":false", Encoding.UTF8.GetString(stored.ToJson()), StringComparison.Ordinal);
    }

    // ---- R7 ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R7: a dismissed question stores nothing and is asked again on the same key at the next self-test
    public void R7_a_dismissed_question_is_asked_again_on_the_same_key()
    {
        var part = HardwarePart(PcA);
        var dismissed = WithSlowAnswer(VoiceTypingSettings.Default, null, part);
        Assert.Equal(VoiceTypingSettings.Default, dismissed);
        Assert.True(SlowCpu("large-v3-turbo-q5_0", dismissed, part).AskUser);
        var earlier = WithSlowAnswer(VoiceTypingSettings.Default, LiveWordsChoice.Live, HardwarePart(PcA with { CpuName = "Intel(R) Core(TM) i5-1235U" }));
        var dismissedHere = WithSlowAnswer(earlier, null, part);
        Assert.Equal(earlier, dismissedHere); // the earlier PC's answer is not erased by a dismissal
        Assert.True(SlowCpu("large-v3-turbo-q5_0", dismissedHere, part).AskUser);
    }
}
