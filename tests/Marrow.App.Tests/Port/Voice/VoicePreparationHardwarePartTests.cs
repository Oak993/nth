using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

// WV-1 (Q-V-1 C, ED-NEW-cloud-vt-15 R5): the hardware part the slow-PC answer is stored under comes from the probe.
public sealed class VoicePreparationHardwarePartTests
{
    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task The_hardware_part_is_null_before_a_probe_and_the_cores_hash_after()
    {
        var rig = new VoicePrepRig(Marrow.Services.Port.Voice.Probe.ScriptedMachines.CpuOnlyLaptop, VoiceQuestionRig.SlowSeconds);
        Assert.Null(rig.Preparation.HardwarePart);
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        Assert.Equal(HardwareKey.ComputeHardwarePart(profile), rig.Preparation.HardwarePart);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task An_answer_before_any_run_stores_nothing_and_gives_no_record()
    {
        var rig = new VoicePrepRig(Marrow.Services.Port.Voice.Probe.ScriptedMachines.CpuOnlyLaptop, VoiceQuestionRig.SlowSeconds);
        var record = await rig.Preparation.AnswerSlowQuestionAsync(LiveWordsChoice.Live, CancellationToken.None);
        Assert.Null(record);
        Assert.Null(rig.Settings.Typing.SlowChoice);
    }
}
