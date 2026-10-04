using System.Reflection;
using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// Review round 5 of VT-1 (scoped re-check of the lead's rulings R5 to R7 at 5e0db0cbe; ED-289 item 13).
//
// 1. R7 leaves the self-test without an outcome. When the question is asked, Decide returns live words null ("the answer decides");
//    ApplyAnswer turns an answer into the model and live words (R6); a dismissal stores nothing (R7). Nothing says what the PC then runs
//    until the next self-test asks again: which model, and whether live words are on (the voiceHardware entry's liveWords is a bool and
//    has to be written). The test proposes the default the sources already name: the model kept with text after the user stops (BENCH
//    "Live words are off by default, and the text appears after the user stops"; voice-SPEC section 23, Q-V-1's recommended answer
//    "Keep the accurate model"). FOR THE LEAD: another default (for example an earlier answer given on other hardware) is equally a fix;
//    what is needed is that Core defines it. Found by reflection (an ApplyAnswer that takes no answer) so the file compiles today.
// 2. Test strength (mutants P1 and P2 survived): the HardwareProfile overload of ComputeHardwarePart, the one the probe will call, had no
//    test. Swapping its adapter and processor names, or dropping the driver (so a new driver would never ask again), went unnoticed.
public sealed class VoiceCoreReview5Tests
{
    private const string Feature = "VT-1";

    private static readonly HardwareProfile Pc = new(
        "AMD Ryzen 7 9800X3D 8-Core Processor", 8, 31, "NVIDIA GeForce RTX 4080", GpuVendor.Nvidia, 16L << 30, 14L << 30, "32.0.15.6094", GpuBackend.Vulkan);

    // ---- finding 1: what a dismissed question leaves ----

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T15")]
    [Trait("Decision", "ED-267")] // lead ruling R7 (a dismissal stores nothing) with BENCH's default and Q-V-1's recommended answer (lead to confirm)
    [InlineData("large-v3-turbo-q5_0")]
    [InlineData("medium-q8_0")]
    [InlineData("base.en")]
    public void REVIEW5_a_dismissed_question_leaves_a_defined_outcome_the_model_kept_with_text_after_the_stop(string model)
    {
        var asked = SelfTestPlan.Decide(model, 0.3, false, RecommendationBasis.Cpu, answeredForThisKey: false);
        Assert.True(asked.AskUser);
        Assert.Null(asked.LiveWords);

        var noAnswer = typeof(SelfTestPlan).GetMethod("ApplyAnswer", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(LiveWordsChoice?)]);
        Assert.True(noAnswer is not null,
            "a dismissed slow-PC question (R7) has no outcome in Core: Decide left live words to the answer, ApplyAnswer needs one, and the "
            + "voiceHardware entry's liveWords must still be written; expected SelfTestPlan.ApplyAnswer(string, LiveWordsChoice?) with null = dismissed");
        var d = (SelfTestDecision)noAnswer!.Invoke(null, [model, null])!;
        Assert.Equal(new SelfTestDecision(SelfTestAction.Keep, model, LiveWords: false, AskUser: false), d);
        Assert.Equal(SelfTestPlan.ApplyAnswer(model, LiveWordsChoice.Live), (SelfTestDecision)noAnswer.Invoke(null, [model, LiveWordsChoice.Live])!);
        Assert.Equal(SelfTestPlan.ApplyAnswer(model, LiveWordsChoice.Accurate), (SelfTestDecision)noAnswer.Invoke(null, [model, LiveWordsChoice.Accurate])!);
    }

    // ---- finding 2: the probe's overload of the hardware part ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T16")]
    [Trait("Decision", "ED-267")] // lead ruling R5: the hardware part of a probe's profile is adapter, driver, processor and memory, in that meaning
    public void REVIEW5_the_hardware_part_of_a_profile_is_the_inputs_part_and_changes_with_those_four_only()
    {
        var part = HardwareKey.ComputeHardwarePart(Pc);
        Assert.Equal(HardwareKey.ComputeHardwarePart(new HardwareKeyInputs(Pc.GpuDescription, Pc.DriverVersion, Pc.CpuName, Pc.MemoryGB, "1.7.0+21", "1.9.1")), part);

        foreach (var changed in new[]
                 {
                     Pc with { GpuDescription = "AMD Radeon RX 7900 XTX" }, Pc with { DriverVersion = "32.0.15.7000" },
                     Pc with { CpuName = "Intel(R) Core(TM) i5-1235U" }, Pc with { MemoryGB = 15 },
                 })
            Assert.NotEqual(part, HardwareKey.ComputeHardwarePart(changed));

        foreach (var same in new[]
                 {
                     Pc with { PhysicalCores = 6 }, Pc with { GpuVendor = GpuVendor.Amd }, Pc with { DedicatedVramBytes = 8L << 30 },
                     Pc with { FreeVramBytes = 1L << 30 }, Pc with { Backend = GpuBackend.None },
                 })
            Assert.Equal(part, HardwareKey.ComputeHardwarePart(same));

        // The adapter and processor names are not interchangeable.
        Assert.NotEqual(part, HardwareKey.ComputeHardwarePart(Pc with { GpuDescription = Pc.CpuName, CpuName = Pc.GpuDescription }));
    }
}
