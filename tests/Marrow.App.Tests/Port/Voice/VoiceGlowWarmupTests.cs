using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Port.Voice;

// VT-9, V-13.5 (GR-1, ED-252): the glow's program is added to GpuWarmup's registry from VoiceGlowWarmup.cs, so the Getting ready screen compiles it before the first composer is
// shown ("a 50 ms first frame otherwise"). That it compiles on ANGLE/D3D11 is native, at the gate; here the registration, the step and the draw on the CPU renderer. The
// registry is shared with the GR-1 and VT-8 tests (same collection).
[Collection("Headless")]
public sealed class VoiceGlowWarmupTests : IDisposable
{
    public VoiceGlowWarmupTests() => VoiceGlowWarmup.Unregister();

    public void Dispose() => VoiceGlowWarmup.Unregister();

    [Fact] // V-13.5, VT-9
    [Trait("Decision", "ED-252")]
    public void Registering_adds_the_glow_program_under_a_step_of_its_own_after_the_voice_orbs()
    {
        VoiceGlowWarmup.Register();
        var programs = GpuWarmup.Registered.Where(p => p.Step == VoiceGlowWarmup.Step).ToArray();
        Assert.Equal(["voice-glow"], programs.Select(p => p.Name));
        Assert.All(programs, p => Assert.False(p.IsWork));
        Assert.Equal(("glow", "Voice glow", 56), (VoiceGlowWarmup.Step.Id, VoiceGlowWarmup.Step.Name, VoiceGlowWarmup.Step.Order));
        Assert.True(VoiceWarmup.Step.Order < VoiceGlowWarmup.Step.Order && VoiceGlowWarmup.Step.Order < FirstRunSteps.Voice.Order);
        Assert.NotEqual(VoiceWarmup.Step, VoiceGlowWarmup.Step);                     // the orbs' own step still names exactly the two orb programs
        Assert.Contains("voice-glow", GpuWarmup.Programs);
    }

    [Fact] // V-13.5, VT-9
    [Trait("Decision", "ED-252")]
    public void Registering_twice_registers_once_and_unregistering_takes_it_out()
    {
        var before = GpuWarmup.Steps;
        VoiceGlowWarmup.Register();
        VoiceGlowWarmup.Register();
        Assert.Equal(before + 1, GpuWarmup.Steps);
        VoiceGlowWarmup.Unregister();
        Assert.Equal(before, GpuWarmup.Steps);
        Assert.DoesNotContain("voice-glow", GpuWarmup.Programs);
    }

    [Fact] // V-13.5, VT-9
    [Trait("Decision", "ED-252")]
    public void The_program_draws_the_glow_on_the_cpu_renderer_at_both_scales_and_leaves_nothing_behind()
    {
        VoiceGlowWarmup.Register();
        var program = GpuWarmup.Registered.Single(p => p.Step == VoiceGlowWarmup.Step);
        program.Drawing(new GpuWarmContext(null, 1.0));
        program.Drawing(new GpuWarmContext(null, 2.0));
    }

    [Fact] // V-13.5, VT-9
    [Trait("Decision", "ED-252")]
    public void Registering_the_glow_leaves_the_orb_registration_as_it_was()
    {
        VoiceWarmup.Unregister();
        VoiceWarmup.Register();
        try
        {
            var orbs = GpuWarmup.Registered.Where(p => p.Step == VoiceWarmup.Step).Select(p => p.Name).ToArray();
            VoiceGlowWarmup.Register();
            Assert.Equal(orbs, GpuWarmup.Registered.Where(p => p.Step == VoiceWarmup.Step).Select(p => p.Name));
            Assert.Equal(["voice-orb-siri", "voice-orb-membrane"], orbs);
        }
        finally { VoiceWarmup.Unregister(); }
    }
}
