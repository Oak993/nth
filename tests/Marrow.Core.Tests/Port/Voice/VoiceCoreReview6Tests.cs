using System.Reflection;
using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// Review round 6 of VT-1 (scoped re-check of lead ruling R8 at fe05a9f5f; ED-289 item 13).
//
// 1. R8 holds only if the caller gates the stored answer itself. R7 keeps an answer given on OTHER hardware in the same field
//    (voiceTyping.liveWordsChoice) that ApplyAnswer is fed from, and R5 says that answer does not count here. On new hardware with a
//    dismissed (or not yet answered) question, the call the R8 test itself writes, ApplyAnswer(model, typing.SlowChoice), applies the old
//    PC's "live": a smaller, untimed model with live words on, where R8 (OD-0099 welcome-voice pick: "until answered the accurate model is
//    used") wants the accurate model with live words off. Core has AnsweredFor and ApplyAnswer but nothing that yields the answer that
//    counts for today's hardware part, so every caller must remember AnsweredFor(...) ? SlowChoice : null. Expected: a public static
//    SelfTestPlan method (VoiceTypingSettings, string hardware part) -> LiveWordsChoice? giving the stored answer only when it was given
//    for this hardware part (found by its signature, any name, so the file compiles today).
public sealed class VoiceCoreReview6Tests
{
    private const string Feature = "VT-1";

    private static readonly HardwareKeyInputs PcA = new("AMD Radeon RX 7900 XTX", "31.0.24027.1012", "AMD Ryzen 7 7700X 8-Core Processor", 31, "1.0.0+100", "1.8.1");

    private static Func<VoiceTypingSettings, string, LiveWordsChoice?>? AnswerThatCounts()
    {
        var m = typeof(SelfTestPlan).GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(x =>
            x.ReturnType == typeof(LiveWordsChoice?)
            && x.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(VoiceTypingSettings), typeof(string)]));
        return m is null ? null : (t, part) => (LiveWordsChoice?)m.Invoke(null, [t, part]);
    }

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead rulings R5, R7, R8: an answer given on other hardware does not decide here; until answered, the accurate model
    [InlineData("large-v3-turbo-q5_0", 0.3, false)]
    [InlineData("medium-q8_0", 0.8, true)]
    [InlineData("small.en-q8_0", 0.3, false)]
    public void REVIEW6_an_answer_given_on_other_hardware_does_not_decide_a_dismissed_question_here(string model, double rtf, bool steppedDown)
    {
        var oldPc = HardwareKey.ComputeHardwarePart(PcA);
        var newPc = HardwareKey.ComputeHardwarePart(PcA with { DriverVersion = "32.0.11021.1011" });
        var earlier = SelfTestPlan.WithSlowAnswer(VoiceTypingSettings.Default, LiveWordsChoice.Live, oldPc);
        var dismissedHere = SelfTestPlan.WithSlowAnswer(earlier, null, newPc);   // R7: nothing stored, the old PC's answer stays
        Assert.Equal(LiveWordsChoice.Live, dismissedHere.SlowChoice);
        var asked = SelfTestPlan.Decide(model, rtf, steppedDown, RecommendationBasis.Cpu, SelfTestPlan.AnsweredFor(dismissedHere, newPc));
        Assert.True(asked.AskUser);

        var answerThatCounts = AnswerThatCounts();
        Assert.True(answerThatCounts is not null,
            "Core gives no answer that counts for today's hardware part: fed from voiceTyping.liveWordsChoice, ApplyAnswer applies the old PC's "
            + "\"live\" (" + SelfTestPlan.ApplyAnswer(asked.ModelId, dismissedHere.SlowChoice) + ") where R8 keeps the accurate model with live words off");
        Assert.Null(answerThatCounts!(dismissedHere, newPc));
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: false, AskUser: false),
            SelfTestPlan.ApplyAnswer(asked.ModelId, answerThatCounts(dismissedHere, newPc)));

        // The answer given for this hardware part is the one that counts.
        Assert.Equal(LiveWordsChoice.Live, answerThatCounts(earlier, oldPc));
        var answeredHere = SelfTestPlan.WithSlowAnswer(dismissedHere, LiveWordsChoice.Accurate, newPc);
        Assert.Equal(LiveWordsChoice.Accurate, answerThatCounts(answeredHere, newPc));
        Assert.Null(answerThatCounts(answeredHere, oldPc));
        Assert.Null(answerThatCounts(VoiceTypingSettings.Default, newPc));
    }
}
