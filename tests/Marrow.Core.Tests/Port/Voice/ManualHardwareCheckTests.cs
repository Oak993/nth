using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-14 (voice-SPEC V-10.6, V-16.3.2, V-16.4, V-10.4; OD-0099 Q-V-4 A): the record a "Check my hardware" leaves, and the reason line under the recommendation.
public sealed class ManualHardwareCheckTests
{
    private const long Gb = 1L << 30;
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 7, 321, TimeSpan.Zero);

    private static readonly HardwareProfile Desktop = new("AMD Ryzen 7 9800X3D 8-Core Processor", 8, 31, "NVIDIA GeForce RTX 4080", GpuVendor.Nvidia,
        16 * Gb, 14 * Gb, "32.0.15.6094", GpuBackend.Vulkan);

    private static readonly HardwareProfile Laptop = new("12th Gen Intel(R) Core(TM) i5-1235U", 10, 15, "Intel(R) Iris(R) Xe Graphics", GpuVendor.Intel,
        128L << 20, 6 * Gb, "31.0.101.4502", GpuBackend.None);

    private static Recommendation Rec(HardwareProfile profile) => Recommender.Recommend(profile, "auto");

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.4: the record of a manual check
    public void A_manual_check_records_the_hardware_the_candidate_and_when()
    {
        var record = ManualHardwareCheck.Record(Desktop, Rec(Desktop), null, "large-v3-turbo-q5_0", Now, "1.7.0+21", "1.9.1");
        Assert.Equal(HardwareKey.Compute(Desktop, "1.7.0+21", "1.9.1"), record.Key);
        Assert.Equal(HardwareOrigin.Manual, record.Origin);
        Assert.Equal("AMD Ryzen 7 9800X3D 8-Core Processor", record.Cpu);
        Assert.Equal(8, record.Cores);
        Assert.Equal(31, record.RamGB);
        Assert.Equal("NVIDIA GeForce RTX 4080", record.Gpu);
        Assert.Equal(16, record.VramGB);
        Assert.Equal(14, record.FreeVramGB);
        Assert.Equal(VoiceBackend.Vulkan, record.Backend);
        Assert.Equal("large-v3-turbo-q8_0", record.Candidate);
        Assert.Equal("large-v3-turbo-q5_0", record.Model);
        Assert.Equal(Now.AddMilliseconds(-321), record.At);
        Assert.Equal("1.7.0+21", record.App);
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.4: backend names; shared memory has no dedicated video memory worth a figure
    public void The_processor_alone_is_the_cpu_backend_and_video_memory_is_rounded_to_a_tenth()
    {
        var record = ManualHardwareCheck.Record(Laptop, Rec(Laptop), null, null, Now, "1.7.0+21", "1.9.1");
        Assert.Equal(VoiceBackend.Cpu, record.Backend);
        Assert.Equal(0.1, record.VramGB);
        Assert.Equal(6 * Gb / (double)Gb, record.FreeVramGB);
        Assert.Equal("", record.Model);
        Assert.Equal("large-v3-turbo-q5_0", record.Candidate);
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // ED: with nothing measured, a working GPU stays live (lead ruling R1) and the processor writes after you stop (R8) until the self-test says otherwise
    public void Live_words_default_to_the_GPU_rule_when_nothing_was_measured()
    {
        Assert.True(ManualHardwareCheck.Record(Desktop, Rec(Desktop), null, null, Now, "a", "b").LiveWords);
        Assert.False(ManualHardwareCheck.Record(Laptop, Rec(Laptop), null, null, Now, "a", "b").LiveWords);
        Assert.Equal(0, ManualHardwareCheck.Record(Desktop, Rec(Desktop), null, null, Now, "a", "b").Rtf);
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // ED: a check on the same hardware under the same build keeps what the self-test measured; other hardware starts again
    public void A_check_on_the_same_hardware_keeps_the_measured_timing_and_live_words()
    {
        var first = new VoiceHardwareRecord
        {
            Key = HardwareKey.Compute(Laptop, "a", "b"), Origin = HardwareOrigin.Auto, Model = "small-q5_1", Rtf = 0.3, LiveWords = true,
            At = Now.AddDays(-3), App = "a",
        };
        var again = ManualHardwareCheck.Record(Laptop, Rec(Laptop), first, "small-q5_1", Now, "a", "b");
        Assert.Equal(0.3, again.Rtf);
        Assert.True(again.LiveWords);
        Assert.Equal("small-q5_1", again.Model);
        Assert.Equal(HardwareOrigin.Manual, again.Origin);

        var otherModel = ManualHardwareCheck.Record(Laptop, Rec(Laptop), first, "large-v3-turbo-q5_0", Now, "a", "b");
        Assert.Equal(0, otherModel.Rtf);
        Assert.True(otherModel.LiveWords);

        var updated = ManualHardwareCheck.Record(Laptop, Rec(Laptop), first, "small-q5_1", Now, "a2", "b");
        Assert.Equal(0, updated.Rtf);
        Assert.False(updated.LiveWords);

        var newMachine = ManualHardwareCheck.Record(Desktop, Rec(Desktop), first, "small-q5_1", Now, "a", "b");
        Assert.Equal(0, newMachine.Rtf);
        Assert.True(newMachine.LiveWords);
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-10.4 / Q-V-4 A: the reason carries the measured time; with none measured for the recommended model the reason is empty (nothing is invented)
    public void The_reason_needs_a_time_measured_for_the_recommended_model()
    {
        var gpu = Rec(Desktop);
        Assert.Equal("", ManualHardwareCheck.Reason(Desktop, gpu, null));
        Assert.Equal("", ManualHardwareCheck.Reason(Desktop, gpu, new VoiceHardwareRecord { Model = gpu.ModelId, Rtf = 0 }));
        Assert.Equal("", ManualHardwareCheck.Reason(Desktop, gpu, new VoiceHardwareRecord { Model = "small-q5_1", Rtf = 0.1 }));
        Assert.Equal("Your NVIDIA GeForce RTX 4080 runs it on the graphics card: text about 0.5 seconds after you stop speaking.",
            ManualHardwareCheck.Reason(Desktop, gpu, new VoiceHardwareRecord { Model = gpu.ModelId, Rtf = 0.037 }));
        var cpu = Rec(Laptop);
        Assert.Equal("On the processor alone Large v3 Turbo answers in about 3 seconds.",
            ManualHardwareCheck.Reason(Laptop, cpu, new VoiceHardwareRecord { Model = cpu.ModelId, Rtf = 0.3 }));
    }
}
