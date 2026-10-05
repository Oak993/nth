using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15 (V-10.7; BUILD-PLAN "an early orb click shows the download progress"): a second caller while the download runs (Welcome's yes, then an orb click) joins it: it is shown the progress so far,
// and the model is fetched once.
public sealed class VoicePreparationSingleFlightTests
{
    private static readonly Dictionary<string, double> Fast = new() { ["large-v3-turbo-q8_0"] = 0.6 };

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task A_second_caller_joins_the_running_download_sees_its_progress_and_gets_the_same_result()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Preparation.GrantConsent();
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        var first = rig.Preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, rig.Items, CancellationToken.None);
        Assert.Equal(["large-v3-turbo-q8_0"], rig.Models.Installs);
        var orb = new VoicePrepItems();
        var second = rig.Preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, orb, CancellationToken.None);
        Assert.Equal(new VoicePrepItem("voice.download", "Voice model", 1.0, VoicePrepState.Running), orb.All[^1]);
        rig.Models.Gate.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Same(results[0], results[1]);
        Assert.Equal(["large-v3-turbo-q8_0"], rig.Models.Installs);
        Assert.Single(rig.Engine.Loads);
        Assert.Equal(1, rig.Settings.RecordWrites);
        Assert.Equal(VoicePrepState.Done, orb.All[^1].State);
        Assert.Equal("voice.selftest", orb.All[^1].Id);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task After_a_run_ends_a_later_call_starts_a_new_one()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.Outcome = Marrow.Services.Port.Voice.Models.ModelDownloadStatus.Failed;
        var (profile, _, failed) = await rig.FirstRunAsync();
        Assert.Equal(VoicePreparationStatus.Failed, failed.Status);
        rig.Models.Outcome = Marrow.Services.Port.Voice.Models.ModelDownloadStatus.Installed;
        var again = await rig.Preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, null, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.Prepared, again.Status);
        Assert.Equal(["large-v3-turbo-q8_0", "large-v3-turbo-q8_0"], rig.Models.Installs);
    }
}
