using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (OD-0092; voice-SPEC V-10.3, OD-0099 Q-V-4 A, OD-0098 / OD-0099 Q-V-1 C): the self-test thresholds and ladders, and the
// reason lines. The thresholds are the benchmark's proposals, kept as named constants (V-10.3).
public sealed class SelfTestPlanTests
{
    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3: RTF <= 0.15 live words on; <= 0.5 keep; > 0.5 step down
    public void The_thresholds_are_named_constants_and_the_band_edges_are_inclusive_below()
    {
        Assert.Equal(0.15, SelfTestPlan.LiveWordsMaxRtf);
        Assert.Equal(0.5, SelfTestPlan.KeepMaxRtf);
        Assert.Equal(10.0, SelfTestPlan.ClipSeconds);
        Assert.Equal(SelfTestBand.Fast, SelfTestPlan.BandOf(0));
        Assert.Equal(SelfTestBand.Fast, SelfTestPlan.BandOf(0.15));
        Assert.Equal(SelfTestBand.Slow, SelfTestPlan.BandOf(0.1500001));
        Assert.Equal(SelfTestBand.Slow, SelfTestPlan.BandOf(0.5));
        Assert.Equal(SelfTestBand.TooSlow, SelfTestPlan.BandOf(0.5000001));
        Assert.Equal(SelfTestBand.TooSlow, SelfTestPlan.BandOf(7));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3: RTF = seconds / 10 for the bundled 10 s clip
    public void RTF_is_the_seconds_over_the_ten_second_clip()
    {
        Assert.Equal(0.06, SelfTestPlan.Rtf(0.6), 12);
        Assert.Equal(0.5, SelfTestPlan.Rtf(5), 12);
        Assert.Equal(1.2, SelfTestPlan.Rtf(12), 12);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3 ladders, in order
    public void The_ladders_are_the_multilingual_and_english_ones_of_V_10_3()
    {
        Assert.Equal(["large-v3-turbo-q8_0", "large-v3-turbo-q5_0", "medium-q8_0", "small-q8_0", "small-q5_1"], SelfTestPlan.Ladder(english: false));
        Assert.Equal(["small.en-q8_0", "base.en", "tiny.en"], SelfTestPlan.Ladder(english: true));
        foreach (var id in SelfTestPlan.Ladder(false).Concat(SelfTestPlan.Ladder(true))) Assert.NotNull(ModelCatalog.Find(id));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3 / V-10.2: every candidate of the recommender is on a ladder; one rung down, one rung up
    public void Each_rung_steps_down_to_the_next_and_the_last_has_none()
    {
        Assert.Equal("large-v3-turbo-q5_0", SelfTestPlan.StepDown("large-v3-turbo-q8_0"));
        Assert.Equal("medium-q8_0", SelfTestPlan.StepDown("large-v3-turbo-q5_0"));
        Assert.Equal("small-q8_0", SelfTestPlan.StepDown("medium-q8_0"));
        Assert.Equal("small-q5_1", SelfTestPlan.StepDown("small-q8_0"));
        Assert.Null(SelfTestPlan.StepDown("small-q5_1"));
        Assert.Equal("base.en", SelfTestPlan.StepDown("small.en-q8_0"));
        Assert.Equal("tiny.en", SelfTestPlan.StepDown("base.en"));
        Assert.Null(SelfTestPlan.StepDown("tiny.en"));
        Assert.Null(SelfTestPlan.StepDown("large-v3")); // the user's own choice is on no ladder
        Assert.Null(SelfTestPlan.StepDown(null));
        Assert.Equal("large-v3-turbo-q8_0", SelfTestPlan.RungAbove("large-v3-turbo-q5_0"));
        Assert.Equal("small-q8_0", SelfTestPlan.RungAbove("small-q5_1"));
        Assert.Null(SelfTestPlan.RungAbove("large-v3-turbo-q8_0"));
        Assert.Null(SelfTestPlan.RungAbove("small.en-q8_0"));
        Assert.Null(SelfTestPlan.RungAbove("nope"));
        foreach (var language in new[] { "en", "auto" })
        foreach (var cores in new[] { 4, 16 })
        {
            var cpu = new HardwareProfile("c", cores, 16, "g", GpuVendor.Other, 0, 0, "", GpuBackend.None);
            var candidate = Recommender.Recommend(cpu, language).ModelId;
            Assert.True(SelfTestPlan.Ladder(language == "en").Contains(candidate), candidate);
        }
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3: RTF <= 0.15 keeps the model, live words on, nothing to ask
    public void A_fast_first_timing_keeps_the_model_with_live_words()
    {
        var d = SelfTestPlan.Decide("large-v3-turbo-q8_0", 0.06, steppedDown: false, RecommendationBasis.Cpu);
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "large-v3-turbo-q8_0", LiveWords: true, AskUser: false), d);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3 + OD-0099 Q-V-1 C: 0.15 < RTF <= 0.5 keeps the model and the app asks once (live words or accurate model)
    public void A_slow_timing_keeps_the_model_and_asks_the_user_once()
    {
        foreach (var rtf in new[] { 0.16, 0.3, 0.5 })
        {
            var d = SelfTestPlan.Decide("large-v3-turbo-q5_0", rtf, steppedDown: false, RecommendationBasis.Cpu);
            Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "large-v3-turbo-q5_0", LiveWords: null, AskUser: true), d);
        }
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3: RTF > 0.5 steps down one rung, to be timed again
    public void A_too_slow_first_timing_steps_down_one_rung()
    {
        var d = SelfTestPlan.Decide("large-v3-turbo-q8_0", 0.51, steppedDown: false, RecommendationBasis.Cpu);
        Assert.Equal(new SelfTestDecision(SelfTestAction.StepDown, "large-v3-turbo-q5_0", LiveWords: null, AskUser: false), d);
        Assert.Equal("medium-q8_0", SelfTestPlan.Decide("large-v3-turbo-q5_0", 2.0, false, RecommendationBasis.Cpu).ModelId);
        Assert.Equal("tiny.en", SelfTestPlan.Decide("base.en", 0.9, false, RecommendationBasis.Cpu).ModelId);
        Assert.Equal(SelfTestAction.StepDown, SelfTestPlan.Decide("small-q8_0", 0.9, false, RecommendationBasis.Cpu).Action);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3 "time again once only": after a step down there is no second step down
    public void After_one_step_down_the_second_timing_is_final_whatever_it_reads()
    {
        var fast = SelfTestPlan.Decide("large-v3-turbo-q5_0", 0.1, steppedDown: true, RecommendationBasis.Cpu);
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "large-v3-turbo-q5_0", LiveWords: true, AskUser: false), fast);
        var slow = SelfTestPlan.Decide("large-v3-turbo-q5_0", 0.4, steppedDown: true, RecommendationBasis.Cpu);
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "large-v3-turbo-q5_0", LiveWords: null, AskUser: true), slow);
        var tooSlow = SelfTestPlan.Decide("large-v3-turbo-q5_0", 3.0, steppedDown: true, RecommendationBasis.Cpu);
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "large-v3-turbo-q5_0", LiveWords: false, AskUser: false), tooSlow);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3: with no rung below (the bottom, or the user's own model) the model is kept, live words off
    public void With_no_rung_below_a_too_slow_model_is_kept_with_live_words_off()
    {
        foreach (var id in new[] { "small-q5_1", "tiny.en", "large-v3", "medium.en" })
            Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, id, LiveWords: false, AskUser: false), SelfTestPlan.Decide(id, 1.0, steppedDown: false, RecommendationBasis.Cpu));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3: the whole procedure walks at most one rung and times at most twice
    public void A_full_walk_down_the_ladder_stops_after_one_step()
    {
        var model = "large-v3-turbo-q8_0";
        var steppedDown = false;
        var timings = 0;
        foreach (var rtf in new[] { 0.9, 0.9, 0.9, 0.9 })
        {
            var d = SelfTestPlan.Decide(model, rtf, steppedDown, RecommendationBasis.Cpu);
            timings++;
            model = d.ModelId;
            if (d.Action == SelfTestAction.Keep) break;
            steppedDown = true;
        }
        Assert.Equal(("large-v3-turbo-q5_0", 2), (model, timings));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3: the Slow band names no live-words value; Fast and the final TooSlow do
    public void Only_the_slow_band_leaves_live_words_to_the_users_answer()
    {
        Assert.Null(SelfTestPlan.Decide("medium-q8_0", 0.3, false, RecommendationBasis.Cpu).LiveWords);
        Assert.True(SelfTestPlan.Decide("medium-q8_0", 0.15, false, RecommendationBasis.Cpu).LiveWords);
    }

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.4: t = RTF x 10 rounded to 0.5 s (half up), shown with no trailing zeros; ED: at least 0.5
    [InlineData(0.0, "0.5")] // never "0 seconds" for a measured time: the least shown is half a second
    [InlineData(0.02, "0.5")]
    [InlineData(0.05, "0.5")]
    [InlineData(0.06, "0.5")]
    [InlineData(0.1, "1")]
    [InlineData(0.125, "1.5")]
    [InlineData(0.25, "2.5")]
    [InlineData(0.2, "2")]
    [InlineData(0.7, "7")]
    [InlineData(1.26, "12.5")]
    public void Seconds_are_rounded_to_half_a_second(double rtf, string shown) =>
        Assert.Equal(shown, RecommendationReason.SecondsText(rtf));

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-4 A; voice-SPEC V-10.4: the GPU template, filled with the measured time
    public void The_GPU_reason_is_the_template_with_the_GPU_name_and_the_measured_time()
    {
        Assert.Equal("Your NVIDIA GeForce RTX 4080 runs it on the graphics card: text about 0.5 seconds after you stop speaking.",
            RecommendationReason.Gpu("NVIDIA GeForce RTX 4080", 0.06));
        Assert.Equal("Your AMD Radeon RX 7900 XTX runs it on the graphics card: text about 1.5 seconds after you stop speaking.",
            RecommendationReason.Gpu("AMD Radeon RX 7900 XTX", 0.125));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-4 A; voice-SPEC V-10.4: the CPU template with the measured times of this rung and the rung above
    public void The_CPU_reason_is_the_template_with_both_measured_times()
    {
        Assert.Equal("On the processor alone Large v3 Turbo answers in about 2.5 seconds; Medium would take 6 seconds or more.",
            RecommendationReason.Cpu("large-v3-turbo-q5_0", 0.25, "medium-q8_0", 0.6));
        Assert.Equal("On the processor alone Base (English only) answers in about 2 seconds; Small (English only) would take 5 seconds or more.",
            RecommendationReason.Cpu("base.en", 0.2, "small.en-q8_0", 0.5));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.4 "t2 ... measured or omitted": without a measured time for the other model the clause is left out
    public void Without_a_measured_time_for_the_other_model_the_second_clause_is_omitted()
    {
        Assert.Equal("On the processor alone Small answers in about 3 seconds.", RecommendationReason.Cpu("small-q5_1", 0.3, null, null));
        Assert.Equal("On the processor alone Small answers in about 3 seconds.", RecommendationReason.Cpu("small-q5_1", 0.3, "medium-q8_0", null));
        Assert.Equal("On the processor alone Small answers in about 3 seconds.", RecommendationReason.Cpu("small-q5_1", 0.3, null, 0.9));
    }
}
