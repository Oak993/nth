using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15, V-T102 (V-10.3; lead rulings R1 to R3 and R8 of ED-289 item 13): the scripted RTF above 0.5 on the processor steps down one rung, installs it and times again once only.
public sealed class VoicePreparationStepDownTests
{
    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T102")]
    [Trait("Decision", "ED-289")]
    public async Task A_processor_rtf_above_half_steps_down_one_rung_installs_it_and_times_again()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, new Dictionary<string, double> { ["large-v3-turbo-q5_0"] = 8.0, ["medium-q8_0"] = 4.0 });
        var (_, recommendation, result) = await rig.FirstRunAsync();
        Assert.Equal("large-v3-turbo-q5_0", recommendation.ModelId);
        Assert.Equal(["large-v3-turbo-q5_0", "medium-q8_0"], rig.Models.Installs);
        Assert.Equal(["large-v3-turbo-q5_0", "medium-q8_0"], rig.Engine.Loads.Select(l => l.Model.Id));
        Assert.All(rig.Engine.Loads, l => Assert.False(l.Options.UseGpu));
        Assert.Equal(2, rig.Engine.Decodes.Count);
        var record = result.Record!;
        Assert.Equal("large-v3-turbo-q5_0", record.Candidate);
        Assert.Equal("medium-q8_0", record.Model);
        Assert.Equal(0.4, record.Rtf, 6);
        Assert.Equal(VoiceBackend.Cpu, record.Backend);
        Assert.Equal("medium-q8_0", rig.Settings.Typing.Model);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T102")]
    [Trait("Decision", "ED-289")]
    public async Task The_step_down_is_once_only_even_when_the_smaller_rung_is_also_too_slow()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, new Dictionary<string, double> { ["large-v3-turbo-q5_0"] = 9.0, ["medium-q8_0"] = 7.0, ["small-q8_0"] = 1.0 });
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal(["large-v3-turbo-q5_0", "medium-q8_0"], rig.Engine.Loads.Select(l => l.Model.Id));
        Assert.Equal("medium-q8_0", result.Record!.Model);
        Assert.Equal(0.7, result.Record.Rtf, 6);
        Assert.False(result.Record.LiveWords);
        Assert.True(result.AskSlowQuestion);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T102")]
    [Trait("Decision", "ED-289")]
    public async Task The_english_ladder_steps_from_base_to_tiny_and_the_bottom_rung_is_kept_with_nothing_asked()
    {
        var rig = new VoicePrepRig(ScriptedMachines.FourCoreMachine, new Dictionary<string, double> { ["base.en"] = 6.0, ["tiny.en"] = 3.0 }, language: "en");
        var (_, recommendation, result) = await rig.FirstRunAsync();
        Assert.Equal("base.en", recommendation.ModelId);
        Assert.Equal(["base.en", "tiny.en"], rig.Models.Installs);
        Assert.Equal("tiny.en", result.Record!.Model);
        Assert.Equal("base.en", result.Record.Candidate);
        Assert.False(result.Record.LiveWords);
        Assert.False(result.AskSlowQuestion);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T102")]
    [Trait("Decision", "ED-289")]
    public async Task A_timing_on_a_working_gpu_backend_never_steps_down_at_any_rtf()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, new Dictionary<string, double> { ["large-v3-turbo-q8_0"] = 30.0 });
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal(["large-v3-turbo-q8_0"], rig.Engine.Loads.Select(l => l.Model.Id));
        Assert.Equal("large-v3-turbo-q8_0", result.Record!.Model);
        Assert.True(result.Record.LiveWords);
        Assert.False(result.AskSlowQuestion);
    }
}
