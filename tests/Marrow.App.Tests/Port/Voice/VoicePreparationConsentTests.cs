using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15 (V-10.7; OD-0098 "D3 with consent"): nothing downloads, verifies, loads or is stored until Welcome's yes or an orb click grants consent. The model source here throws on any transfer.
public sealed class VoicePreparationConsentTests
{
    private static readonly Dictionary<string, double> Fast = new() { ["large-v3-turbo-q8_0"] = 0.6 };

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task Without_consent_nothing_is_downloaded_verified_loaded_or_stored_and_the_downloader_that_throws_is_never_touched()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.ThrowOnAnyTransfer = true;
        Assert.False(rig.Preparation.HasConsent);
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None); // the probe is always allowed: it reads the PC and fetches nothing
        var recommendation = rig.Preparation.Recommend(profile);
        var result = await rig.Preparation.InstallAndTimeAsync(recommendation.ModelId, profile, rig.Items, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.AwaitingConsent, result.Status);
        Assert.Null(result.Record);
        Assert.Empty(rig.Engine.Loads);
        Assert.Equal(0, rig.Settings.RecordWrites);
        Assert.Empty(rig.Items.All);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task After_consent_the_same_call_downloads_and_prepares()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.AwaitingConsent, (await rig.Preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, null, CancellationToken.None)).Status);
        rig.Preparation.GrantConsent();
        Assert.True(rig.Preparation.HasConsent);
        var result = await rig.Preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, null, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.Prepared, result.Status);
        Assert.Equal(["large-v3-turbo-q8_0"], rig.Models.Installs);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task Prepare_without_consent_probes_and_stops_before_any_transfer()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.ThrowOnAnyTransfer = true;
        var result = await rig.Preparation.PrepareAsync(rig.Items, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.AwaitingConsent, result.Status);
        Assert.Equal(VoicePrepState.Done, rig.Items.Of("voice.probe")[^1].State);
        Assert.Empty(rig.Engine.Loads);
    }
}
