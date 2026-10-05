using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15 (V-10.7; the port's rule that a failed preparation is never stored as prepared): a failed download, a corrupt file, an engine that throws and a cancel store nothing.
public sealed class VoicePreparationFailureTests
{
    private static readonly Dictionary<string, double> Fast = new() { ["large-v3-turbo-q8_0"] = 0.6 };

    [Theory]
    [InlineData(ModelDownloadStatus.Failed)]
    [InlineData(ModelDownloadStatus.Corrupt)]
    [InlineData(ModelDownloadStatus.Cancelled)]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task A_download_that_did_not_install_fails_the_run_and_stores_nothing(ModelDownloadStatus outcome)
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.Outcome = outcome;
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal(VoicePreparationStatus.Failed, result.Status);
        Assert.Equal("The voice model download " + outcome.ToString().ToLowerInvariant() + ".", result.Error);
        Assert.Null(result.Record);
        Assert.Equal(0, rig.Settings.RecordWrites);
        Assert.Empty(rig.Engine.Loads);
        Assert.DoesNotContain(rig.Items.All, i => i.Id == "voice.download" && i.State == VoicePrepState.Done);
    }

    [Theory]
    [InlineData(ModelVerification.Corrupt)]
    [InlineData(ModelVerification.Missing)]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task A_file_that_does_not_verify_is_never_loaded(ModelVerification verification)
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.Verification = verification;
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal(VoicePreparationStatus.Failed, result.Status);
        Assert.Equal("The voice model did not verify (" + verification.ToString().ToLowerInvariant() + ").", result.Error);
        Assert.Empty(rig.Engine.Loads);
        Assert.Equal(0, rig.Settings.RecordWrites);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task An_engine_that_throws_on_load_fails_the_run_with_its_message_and_stores_nothing()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Engine.FailLoad = new InvalidOperationException("no runtime");
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal(VoicePreparationStatus.Failed, result.Status);
        Assert.Equal("no runtime", result.Error);
        Assert.Equal(0, rig.Settings.RecordWrites);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task The_unbound_engine_seam_fails_the_run_closed()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        var preparation = new VoicePreparation(new ScriptedProbe(ScriptedMachines.OwnersDesktop), rig.Models,
            () => Marrow.Core.Port.Voice.Seams.VoiceSeams.Engine.Current, rig.Settings, () => new float[10], "a", "r");
        preparation.GrantConsent();
        var profile = await preparation.ProbeAsync(null, CancellationToken.None);
        var result = await preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, null, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.Failed, result.Status);
        Assert.Equal(0, rig.Settings.RecordWrites);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task A_model_the_catalogue_does_not_have_fails_without_touching_the_source()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.ThrowOnAnyTransfer = true;
        rig.Preparation.GrantConsent();
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        var result = await rig.Preparation.InstallAndTimeAsync("large-v1", profile, null, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.Failed, result.Status);
        Assert.Equal("large-v1 is not a voice model of the catalogue.", result.Error);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task A_cancelled_run_throws_cancelled_and_stores_nothing()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Preparation.GrantConsent();
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, null, cancelled.Token));
        Assert.Equal(0, rig.Settings.RecordWrites);
    }
}
