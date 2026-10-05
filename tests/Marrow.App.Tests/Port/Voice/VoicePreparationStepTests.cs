using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15 (V-10.7; GR-1, OD-0096): the "Voice hardware" step is registered in GR-1's registry as a work step, ordered 60 after "Voice orbs" and "Voice glow"; running it does the probe (always on the
// Getting ready screen) and nothing that downloads. The registry is shared with the GR-1 tests (same collection).
[Collection("Headless")]
public sealed class VoicePreparationStepTests
{
    private static readonly Dictionary<string, double> Fast = new() { ["large-v3-turbo-q8_0"] = 0.6 };

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T100")]
    [Trait("Decision", "ED-252")]
    public void Registering_adds_one_work_step_named_Voice_hardware_under_W1s_step_and_disposing_takes_it_out()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        var before = GpuWarmup.Registered.Count;
        using (rig.Preparation.RegisterStep())
        {
            var program = Assert.Single(GpuWarmup.Registered, p => p.Name == "voice-hardware");
            Assert.True(program.IsWork);
            Assert.Equal(FirstRunSteps.Voice, program.Step);
            Assert.Equal(("hw", "Voice hardware", 60), (program.Step.Id, program.Step.Name, program.Step.Order));
            Assert.Contains(FirstRunPreparation.Default.Plan(), s => s.Step == FirstRunSteps.Voice && s.Parts == 1);
            Assert.Equal(before + 1, GpuWarmup.Registered.Count);
        }
        Assert.Equal(before, GpuWarmup.Registered.Count);
        Assert.DoesNotContain(GpuWarmup.Registered, p => p.Name == "voice-hardware");
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T100")]
    [Trait("Decision", "ED-252")]
    public async Task Running_the_step_probes_with_real_items_and_downloads_and_loads_nothing()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.ThrowOnAnyTransfer = true;
        using var registration = rig.Preparation.RegisterStep();
        var program = GpuWarmup.Registered.Single(p => p.Name == "voice-hardware");
        await program.Working(CancellationToken.None);
        Assert.Empty(rig.Engine.Loads);
        Assert.Equal(0, rig.Clock.ActiveTimers);
        // the profile it took is the one PrepareAsync and IsPrepared go on from
        rig.Settings.Record = new Marrow.Core.Port.Voice.VoiceHardwareRecord
        {
            Key = Marrow.Core.Port.Voice.HardwareKey.Compute(await rig.Preparation.ProbeAsync(null, CancellationToken.None), VoicePrepRig.App, VoicePrepRig.Runtime),
        };
        Assert.True(rig.Preparation.IsPrepared(out _));
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T100")]
    [Trait("Decision", "ED-252")]
    public void Registering_twice_is_refused_by_the_registry_not_doubled()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        using var first = rig.Preparation.RegisterStep();
        Assert.Throws<ArgumentException>(() => rig.Preparation.RegisterStep());
    }
}
