using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// Review round 3 of VT-1 (scoped re-check of the lead's rulings R1 to R4, ED-NEW-f-vt-1 item 13; OD-0099 Q-V-1 C amendment: "when the
// speed test finds the PC too slow to run the accurate model live, the app asks the user once ... a fast PC (every GPU) is never asked").
//
// 1. "Answered for this hardware key" is read from the stored voiceHardware check, which every check rewrites, also a check that asked
//    nothing (a GPU timing, a fast timing, a bottom rung). The answer itself is stored without the key it was given under. So on a new
//    key the user is asked again, or not, depending only on whether the first check under that key happened to ask: answered under key
//    A, a new driver gives key B; if the first timing under B is slow the user is asked (stored check still has key A), but if it is fast
//    (stored check rewritten with key B, no question) a later slow "Check again" under B is never asked and A's answer decides. R3 cannot
//    hold both ways; the outcome for key B must not depend on the order of the timings under it.
// 2. The V-10.5 key holds the app's version and build and the Whisper.net runtime version, so with AnsweredFor as built every app
//    update asks a slow PC the question again, on the same hardware. The owner's words are "asks the user once" and "the answer is kept
//    in Settings > Voice typing and can be changed there"; an app update is not new hardware. FOR THE LEAD: if R3's "hardware key" is
//    meant as V-10.5's key (app build included), this test is to be dropped; otherwise the answer needs a key without the app build.
//    (The test uses today's AnsweredFor signature; a fix may change the call, the scenario must hold.)
// 3. Test strength (mutation M13 survived): no test timed a model with no smaller rung (the bottom of a ladder, or the user's own model
//    on no ladder) as fast on the processor, so moving R2's "no smaller rung: live words off" ahead of the fast band went unnoticed.
public sealed class VoiceCoreReview3Tests
{
    private const string Feature = "VT-1";

    private static readonly HardwareKeyInputs PcA = new("AMD Radeon RX 7900 XTX", "31.0.24027.1012", "AMD Ryzen 7 7700X 8-Core Processor", 31, "1.0.0+100", "1.8.1");

    private static readonly VoiceTypingSettings Answered = VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Accurate };

    // ---- finding 1: the outcome for a new key depends on whether its first check asked ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // lead ruling R3 (ED-NEW-f-vt-1 item 13): asked once whenever too slow with a smaller rung; never twice for the same hardware key
    public void REVIEW3_on_a_new_key_being_asked_does_not_depend_on_whether_the_first_check_under_it_asked()
    {
        var keyA = HardwareKey.Compute(PcA);
        var keyB = HardwareKey.Compute(PcA with { DriverVersion = "32.0.11021.1011" }); // a new GPU driver
        Assert.NotEqual(keyA, keyB);

        // The question was answered under key A; the stored check has key A.
        var storedA = new VoiceHardwareRecord { Key = keyA, Model = "large-v3-turbo-q5_0", Rtf = 0.3 };

        // Order 1: the first timing under key B is slow (smaller rung exists): the stored check still has key A.
        var firstSlow = SelfTestPlan.Decide("large-v3-turbo-q5_0", 0.3, false, RecommendationBasis.Cpu, SelfTestPlan.AnsweredFor(storedA, keyB, Answered));

        // Order 2: the first timing under key B is fast (no question), so the check under B is stored with key B; then "Check again" under
        // the same key B times slow.
        var fastUnderB = SelfTestPlan.Decide("large-v3-turbo-q5_0", 0.14, false, RecommendationBasis.Cpu, SelfTestPlan.AnsweredFor(storedA, keyB, Answered));
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, "large-v3-turbo-q5_0", true, false), fastUnderB);
        var storedB = storedA with { Key = keyB, Rtf = 0.14, LiveWords = true };
        var laterSlow = SelfTestPlan.Decide("large-v3-turbo-q5_0", 0.3, false, RecommendationBasis.Cpu, SelfTestPlan.AnsweredFor(storedB, keyB, Answered));

        Assert.True(firstSlow.AskUser == laterSlow.AskUser,
            "under key B the question is " + (firstSlow.AskUser ? "asked" : "not asked") + " when B's first timing is slow, but "
            + (laterSlow.AskUser ? "asked" : "not asked") + " when B's first timing was fast and a later one is slow: the answer is stored without "
            + "the key it was given under, so any check that asked nothing makes key A's answer count for key B");
    }

    // ---- finding 2: an app update alone asks again ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-1 C amendment: "asks the user once"; the answer "is kept in Settings > Voice typing" (lead to confirm)
    [InlineData("1.0.1+101", "1.8.1")] // a new app version and build
    [InlineData("1.0.0+100", "1.8.2")] // a new Whisper.net runtime with the app
    public void REVIEW3_an_app_update_on_the_same_hardware_does_not_ask_the_slow_PC_question_again(string app, string runtime)
    {
        var before = HardwareKey.Compute(PcA);
        var after = HardwareKey.Compute(PcA with { App = app, RuntimeVersion = runtime });
        Assert.NotEqual(before, after); // the check itself re-runs (V-10.5); only the question is in doubt
        var stored = new VoiceHardwareRecord { Key = before, Model = "large-v3-turbo-q5_0", Rtf = 0.3 };
        var d = SelfTestPlan.Decide("large-v3-turbo-q5_0", 0.3, false, RecommendationBasis.Cpu, SelfTestPlan.AnsweredFor(stored, after, Answered));
        Assert.False(d.AskUser, "the same PC is asked the slow-PC question again after every app update");
    }

    // ---- finding 3: a fast processor timing on a model with no smaller rung ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-10.3 "RTF <= 0.15: keep, live words on"; lead ruling R2 applies only to a slow timing
    [InlineData("small-q5_1", 0.1, false)]
    [InlineData("small-q5_1", 0.15, true)]
    [InlineData("tiny.en", 0.05, false)]
    [InlineData("tiny.en", 0.12, true)]
    [InlineData("large-v3", 0.1, false)]
    [InlineData("medium.en", 0.15, false)]
    public void REVIEW3_a_fast_processor_timing_with_no_smaller_rung_keeps_live_words_on(string model, double rtf, bool steppedDown)
    {
        foreach (var answered in new[] { false, true })
            Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: true, AskUser: false),
                SelfTestPlan.Decide(model, rtf, steppedDown, RecommendationBasis.Cpu, answered));
    }
}
