using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

[Trait("Rule", "R-TEST-10")] // Scripted seams only; no model, network or hardware.
public sealed class VoiceProbeOnlyRecordTests
{
    [Fact]
    [Trait("Feature", "VT-15")]
    public async Task Manual_hardware_check_without_a_model_still_needs_consent_download_and_timing()
    {
        var rig = VoiceQuestionRig.Pc();
        var profile = await rig.Prep.Preparation.ProbeAsync(null, CancellationToken.None);
        rig.Prep.Settings.Record = ManualHardwareCheck.Record(profile, rig.Prep.Preparation.Recommend(profile), null, null,
            rig.Prep.Clock.GetUtcNow(), VoicePrepRig.App, VoicePrepRig.Runtime);
        var flow = new VoiceFirstRun(rig.Prep.Preparation, rig.Question, rig.Prep.Models);
        Assert.False(flow.CanListen);
        Assert.False(await flow.OrbAsync(CancellationToken.None));
        Assert.Single(rig.Prep.Models.Installs);
        Assert.Single(rig.Prep.Engine.Loads);
        Assert.True(flow.CanListen);
    }
}
