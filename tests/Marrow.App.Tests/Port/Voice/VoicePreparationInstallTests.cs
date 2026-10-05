using System.Text;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15, V-T100 and V-T101 (V-10.3, V-10.7, V-16.4): the first run installs the recommendation, times it, stores voiceHardware with origin auto, and reports real items only.
public sealed class VoicePreparationInstallTests
{
    private static readonly Dictionary<string, double> Fast = new() { ["large-v3-turbo-q8_0"] = 0.6 };

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T101")]
    [Trait("Decision", "ED-289")]
    public async Task The_first_run_installs_the_recommendation_times_it_and_stores_voiceHardware_with_origin_auto()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        var (profile, recommendation, result) = await rig.FirstRunAsync();
        Assert.Equal("large-v3-turbo-q8_0", recommendation.ModelId);
        Assert.Equal(VoicePreparationStatus.Prepared, result.Status);
        Assert.False(result.AskSlowQuestion);
        Assert.Equal(["large-v3-turbo-q8_0"], rig.Models.Installs);
        var record = Assert.IsType<VoiceHardwareRecord>(result.Record);
        Assert.Same(record, rig.Settings.Record);
        Assert.Equal(1, rig.Settings.RecordWrites);
        Assert.Equal(HardwareKey.Compute(profile, VoicePrepRig.App, VoicePrepRig.Runtime), record.Key);
        Assert.Equal(HardwareOrigin.Auto, record.Origin);
        Assert.Equal("AMD Ryzen 7 9800X3D 8-Core Processor", record.Cpu);
        Assert.Equal(8, record.Cores);
        Assert.Equal(31, record.RamGB);
        Assert.Equal("NVIDIA GeForce RTX 4080", record.Gpu);
        Assert.Equal(16.0, record.VramGB);
        Assert.Equal(14.0, record.FreeVramGB);
        Assert.Equal(VoiceBackend.Vulkan, record.Backend);
        Assert.Equal("large-v3-turbo-q8_0", record.Candidate);
        Assert.Equal("large-v3-turbo-q8_0", record.Model);
        Assert.Equal(0.06, record.Rtf, 6);
        Assert.True(record.LiveWords);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), record.At);
        Assert.Equal(VoicePrepRig.App, record.App);
        Assert.Contains("\"origin\":\"auto\"", Encoding.UTF8.GetString(record.ToJson()), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T101")]
    [Trait("Decision", "ED-289")]
    public async Task The_timing_loads_the_model_from_its_path_then_decodes_the_clip_once_with_live_parameters()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        await rig.FirstRunAsync();
        var load = Assert.Single(rig.Engine.Loads);
        Assert.Equal(new Marrow.Core.Port.Voice.Seams.SpeechModel("large-v3-turbo-q8_0", Path.Combine("models", "ggml-large-v3-turbo-q8_0.bin")), load.Model);
        Assert.True(load.Options.UseGpu);
        var decode = Assert.Single(rig.Engine.Decodes);
        Assert.Equal(new Marrow.Core.Port.Voice.Seams.DecodeRequest("en", "", Marrow.Core.Port.Voice.Seams.DecodeMode.Live, TimeSpan.Zero), decode);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T101")]
    [Trait("Decision", "ED-289")]
    public async Task The_chosen_model_is_selected_when_none_was_picked_and_a_picked_one_stays()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        await rig.FirstRunAsync();
        Assert.Equal("large-v3-turbo-q8_0", rig.Settings.Typing.Model);

        var picked = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        picked.Settings.Typing = VoiceTypingSettings.Default with { Model = "small-q5_1" };
        await picked.FirstRunAsync();
        Assert.Equal("small-q5_1", picked.Settings.Typing.Model);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T100")]
    [Trait("Decision", "ED-289")]
    public async Task A_fresh_install_reports_download_then_verify_then_speed_test_from_real_events_with_no_timer()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Preparation.GrantConsent();
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        await rig.Preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, rig.Items, CancellationToken.None);
        Assert.Equal(
        [
            new VoicePrepItem("voice.download", "Voice model", 0.0, VoicePrepState.Running),
            new VoicePrepItem("voice.download", "Voice model", 0.5, VoicePrepState.Running),
            new VoicePrepItem("voice.download", "Voice model", 1.0, VoicePrepState.Running),
            new VoicePrepItem("voice.download", "Voice model", 1.0, VoicePrepState.Done),
            new VoicePrepItem("voice.verify", "Voice model", null, VoicePrepState.Running),
            new VoicePrepItem("voice.verify", "Voice model", 1.0, VoicePrepState.Done),
            new VoicePrepItem("voice.selftest", "Voice speed test", null, VoicePrepState.Running),
            new VoicePrepItem("voice.selftest", "Voice speed test", 1.0, VoicePrepState.Done),
        ], rig.Items.All);
        Assert.Equal(0, rig.Clock.ActiveTimers);
        Assert.Equal(TimeSpan.FromSeconds(0.6), rig.Clock.Elapsed); // only the scripted decode moved the clock: nothing waited
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T100")]
    [Trait("Decision", "ED-289")]
    public async Task A_model_already_installed_is_not_downloaded_again_and_its_download_item_is_done_at_once()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Models.Installed.Add("large-v3-turbo-q8_0");
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.Equal(VoicePreparationStatus.Prepared, result.Status);
        Assert.Empty(rig.Models.Installs);
        Assert.Equal([new VoicePrepItem("voice.download", "Voice model", 1.0, VoicePrepState.Done)], rig.Items.Of("voice.download"));
        Assert.Equal(["large-v3-turbo-q8_0"], rig.Models.Verifies);
    }
}
