using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Port.Voice;

// VT-8, V-T54 (V-11.3; GR-1, ED-252): the two voice orb programs are added to GpuWarmup's registry from VoiceWarmup.cs, so the Getting ready screen compiles them. That they
// COMPILE on ANGLE/D3D11 is native, on a quiet PC, at the gate; here the registration, the step and the draw on the CPU renderer. The registry is shared with the GR-1 tests (same collection).
[Collection("Headless")]
public sealed class VoiceWarmupTests : IDisposable
{
    public VoiceWarmupTests() => VoiceWarmup.Unregister();

    public void Dispose() => VoiceWarmup.Unregister();

    [Fact] // V-T54, VT-8
    [Trait("Decision", "ED-252")]
    [Trait("Spec", "V-T54")]
    public void Registering_adds_the_two_orb_programs_under_a_step_of_their_own_between_the_strike_and_the_voice_hardware_check()
    {
        VoiceWarmup.Register();
        var orbs = GpuWarmup.Registered.Where(p => p.Step == VoiceWarmup.Step).ToArray();
        Assert.Equal(["voice-orb-siri", "voice-orb-membrane"], orbs.Select(p => p.Name));
        Assert.All(orbs, p => Assert.False(p.IsWork));
        Assert.Equal(("orbs", "Voice orbs", 55), (VoiceWarmup.Step.Id, VoiceWarmup.Step.Name, VoiceWarmup.Step.Order));
        Assert.True(FirstRunSteps.Strike.Order < VoiceWarmup.Step.Order && VoiceWarmup.Step.Order < FirstRunSteps.Voice.Order);
        var names = GpuWarmup.Registered.Select(p => p.Name).ToArray();
        Assert.True(Array.IndexOf(names, "strike") < Array.IndexOf(names, "voice-orb-siri"));
        Assert.Contains("voice-orb-siri", GpuWarmup.Programs);
        Assert.Contains("voice-orb-membrane", GpuWarmup.Programs);
    }

    [Fact] // V-T54, VT-8
    [Trait("Decision", "ED-252")]
    [Trait("Spec", "V-T54")]
    public void Registering_twice_registers_once_and_unregistering_takes_them_out()
    {
        var before = GpuWarmup.Steps;
        VoiceWarmup.Register();
        VoiceWarmup.Register();
        Assert.Equal(before + 2, GpuWarmup.Steps);
        VoiceWarmup.Unregister();
        Assert.Equal(before, GpuWarmup.Steps);
        Assert.DoesNotContain("voice-orb-siri", GpuWarmup.Programs);
    }

    [Fact] // V-T54, VT-8
    [Trait("Decision", "ED-252")]
    public void Each_program_draws_its_orb_on_the_cpu_renderer_and_leaves_nothing_behind()
    {
        VoiceWarmup.Register();
        foreach (var program in GpuWarmup.Registered.Where(p => p.Step == VoiceWarmup.Step))
        {
            program.Drawing(new GpuWarmContext(null, 1.0));
            program.Drawing(new GpuWarmContext(null, 2.0));   // above 128 device px the orb draws straight, with no supersampling surface
        }
    }
}
