using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

[Collection("VoiceFeature")]
[Trait("Rule", "R-TEST-10")] // Scripted seams only; no model, network or hardware.
public sealed class VoiceStartupWiringTests
{
    [Fact]
    [Trait("Feature", "VT-15")]
    public void Startup_binds_one_question_registers_hardware_and_switches_on_without_transfer()
    {
        using var runtime = new Marrow.Testing.TestRuntime();
        var rig = VoiceQuestionRig.Pc();
        rig.Prep.Models.ThrowOnAnyTransfer = true;
        var settings = new AppSettings();
        var before = GpuWarmup.Registered.Count;
        VoiceFeature.Reset();
        try
        {
            using var registration = VoiceComposition.Connect(settings, rig.Prep.Preparation, rig.Prep.Models);
            Assert.True(VoiceFeature.Enabled);
            Assert.NotNull(VoiceFirstRun.Find(settings));
            Assert.Equal("Voice hardware", GpuWarmup.Registered.Last(p => p.Name == "voice-hardware").Step.Name);
            Assert.Equal(before + 1, GpuWarmup.Registered.Count);
            Assert.False(rig.Prep.Preparation.HasConsent);
            Assert.Empty(rig.Prep.Models.Installs);
        }
        finally { VoiceFeature.Reset(); }
        Assert.Equal(before, GpuWarmup.Registered.Count);
    }
}
