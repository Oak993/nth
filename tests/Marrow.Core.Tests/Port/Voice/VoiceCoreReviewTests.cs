using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// Review round 1 of VT-1 (unit/integ-vt1, the merge of cloud/f-vt-1 onto the VT-0 staging branch).
//
// 1. OD-0099 Q-V-1 C (owner_decisions.md, the amendment of 2026-10-04): "a fast PC (every GPU) is never asked and stays live with the
//    accurate model". SelfTestPlan.Decide does not know whether the timing ran on a working GPU backend, so a GPU timing between RTF
//    0.15 and 0.5 asks the slow-PC question and leaves live words undecided.
// 2. The same ruling: the question offers "live words with a smaller, faster model, or text after they stop with the accurate model".
//    With no smaller model (the bottom rung of a ladder, or a model on no ladder such as the user's own large-v3) the question has
//    no first option, yet Decide asks it.
// 3. The same ruling asks once "when the speed test finds the PC too slow to run the accurate model live". ED-NEW-f-vt-1 item 8 keeps
//    a model that is still too slow after the one step down with live words off and asks nothing, although a smaller rung exists.
//    This conflicts with the owner's words; the lead decides.
// 4. RecommendationReason (Q-V-4 A templates): a measured time of one second reads "about 1 seconds", which is not English.
// 5. Test strength (mutation M8 survived): HardwareKey's doc says the inputs are length-prefixed so that values that run together
//    cannot collide, but no test told the length prefix apart from the plain "name:value" lines. The pair below collides without it.
public sealed class VoiceCoreReviewTests
{
    private const string Feature = "VT-1";

    // Decide with the basis of the timing (GPU or processor). The REVIEW commit found this overload by reflection so that the file
    // compiled before the fix; the fix made the basis a parameter of Decide, so this is now a plain call.
    private static SelfTestDecision Decide(string modelId, double rtf, bool steppedDown, RecommendationBasis basis) =>
        SelfTestPlan.Decide(modelId, rtf, steppedDown, basis, answeredForThisKey: false);

    // ---- finding 1: every GPU is never asked and stays live ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-1 C amendment: "a fast PC (every GPU) is never asked and stays live with the accurate model"
    [InlineData("large-v3-turbo-q8_0", 0.16, false)]
    [InlineData("large-v3-turbo-q8_0", 0.5, false)]
    [InlineData("large-v3-turbo-q5_0", 0.3, false)]
    [InlineData("large-v3-turbo-q5_0", 0.3, true)]
    public void REVIEW_a_GPU_timing_in_the_slow_band_keeps_the_model_live_and_asks_nothing(string model, double rtf, bool steppedDown) =>
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: true, AskUser: false), Decide(model, rtf, steppedDown, RecommendationBasis.Gpu));

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-1 C: the processor-only slow band still asks (unchanged by finding 1)
    public void REVIEW_a_processor_timing_in_the_slow_band_still_asks()
    {
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "large-v3-turbo-q5_0", LiveWords: null, AskUser: true),
            Decide("large-v3-turbo-q5_0", 0.3, false, RecommendationBasis.Cpu));
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "small.en-q8_0", LiveWords: null, AskUser: true),
            Decide("small.en-q8_0", 0.2, false, RecommendationBasis.Cpu));
    }

    // ---- finding 2: no smaller model, no question ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-1 C: the question's first answer is "live words with a smaller, faster model"
    [InlineData("small-q5_1")] // the bottom of the multilingual ladder
    [InlineData("tiny.en")]    // the bottom of the English ladder
    [InlineData("large-v3")]   // the user's own choice, on no ladder
    public void REVIEW_with_no_smaller_model_the_slow_band_asks_nothing_and_decides_live_words(string model)
    {
        var d = SelfTestPlan.Decide(model, 0.3, steppedDown: false, RecommendationBasis.Cpu, answeredForThisKey: false);
        Assert.Equal(SelfTestAction.Keep, d.Action);
        Assert.Equal(model, d.ModelId);
        Assert.False(d.AskUser, "the slow-PC question would offer a smaller model where none exists");
        Assert.False(d.LiveWords); // lead ruling R2: the current model is kept with text after the user stops

    }

    // ---- finding 3: still too slow after the one step, with a smaller rung left ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-1 C: "when the speed test finds the PC too slow to run the accurate model live, the app asks the user once"
    [InlineData("medium-q8_0")]
    [InlineData("base.en")]
    public void REVIEW_a_processor_too_slow_after_the_one_step_is_asked_while_a_smaller_rung_exists(string model)
    {
        Assert.NotNull(SelfTestPlan.StepDown(model));
        var d = SelfTestPlan.Decide(model, 0.8, steppedDown: true, RecommendationBasis.Cpu, answeredForThisKey: false);
        Assert.Equal(SelfTestAction.Keep, d.Action);
        Assert.Equal(model, d.ModelId);
        Assert.True(d.AskUser, "ED-NEW-f-vt-1 item 8 asks nothing here; OD-0099 Q-V-1 C asks once when the PC is too slow to run the accurate model live");
    }

    // ---- finding 4: "1 seconds" ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-4 A: the V-10.4 templates filled with measured times
    [InlineData(0.08)]
    [InlineData(0.1)]
    [InlineData(0.12)]
    public void REVIEW_a_measured_time_of_one_second_is_not_written_1_seconds(double rtf)
    {
        Assert.Equal("1", RecommendationReason.SecondsText(rtf));
        Assert.DoesNotContain("about 1 seconds", RecommendationReason.Gpu("NVIDIA GeForce RTX 4080", rtf), StringComparison.Ordinal);
        Assert.DoesNotContain("about 1 seconds", RecommendationReason.Cpu("base.en", rtf, null, null), StringComparison.Ordinal);
        Assert.DoesNotContain(" 1 seconds or more", RecommendationReason.Cpu("tiny.en", 0.05, "base.en", rtf), StringComparison.Ordinal);
    }

    // ---- finding 5: the length prefix of the hardware key (test strength) ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // ED-NEW-f-vt-1 item 5: each input is written as its name, its length and its text, so values cannot run together
    public void REVIEW_text_that_imitates_the_next_input_does_not_collide()
    {
        var inputs = new HardwareKeyInputs("NVIDIA GeForce RTX 4080", "32.0.15.6094", "AMD Ryzen 7 9800X3D", 31, "1.7.0+21", "1.9.1");
        // Without the lengths both pairs would hash the same text: "adapter:a", "driver:b", "driver:c" lines, and "app:1", "runtime:2", "runtime:3" lines.
        var a = inputs with { AdapterDescription = "a\ndriver:b", DriverVersion = "c" };
        var b = inputs with { AdapterDescription = "a", DriverVersion = "b\ndriver:c" };
        Assert.NotEqual(HardwareKey.Compute(a), HardwareKey.Compute(b));
        var c = inputs with { App = "1\nruntime:2", RuntimeVersion = "3" };
        var d = inputs with { App = "1", RuntimeVersion = "2\nruntime:3" };
        Assert.NotEqual(HardwareKey.Compute(c), HardwareKey.Compute(d));
    }
}
