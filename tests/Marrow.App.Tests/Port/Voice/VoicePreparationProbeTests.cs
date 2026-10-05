using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15, V-T100 (V-10.1, V-10.7; OD-0096 "real progress and steps (no fake timer)"): ProbeAsync reports each row as the probe finishes it, and nothing reads or waits on a clock.
public sealed class VoicePreparationProbeTests
{
    private static readonly Dictionary<string, double> Seconds = new() { ["large-v3-turbo-q8_0"] = 0.6 };

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T100")]
    [Trait("Decision", "ED-289")]
    public async Task The_probe_reports_one_item_per_finished_row_under_its_step_with_the_real_share_done()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Seconds);
        var profile = await rig.Preparation.ProbeAsync(rig.Items, CancellationToken.None);
        Assert.Equal("voice.probe", VoicePreparation.ProbeId);
        Assert.Equal(
        [
            new VoicePrepItem("voice.probe", "Voice hardware", 0.0, VoicePrepState.Running),
            new VoicePrepItem("voice.probe", "Voice hardware", 0.25, VoicePrepState.Running),
            new VoicePrepItem("voice.probe", "Voice hardware", 0.5, VoicePrepState.Running),
            new VoicePrepItem("voice.probe", "Voice hardware", 0.75, VoicePrepState.Running),
            new VoicePrepItem("voice.probe", "Voice hardware", 1.0, VoicePrepState.Done),
        ], rig.Items.All);
        Assert.Equal("AMD Ryzen 7 9800X3D 8-Core Processor", profile.CpuName);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T100")]
    [Trait("Decision", "ED-289")]
    public async Task The_probe_makes_no_timer_and_moves_no_clock()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, Seconds);
        await rig.Preparation.ProbeAsync(rig.Items, CancellationToken.None);
        Assert.Equal(0, rig.Clock.ActiveTimers);
        Assert.Equal(TimeSpan.Zero, rig.Clock.Elapsed);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T100")]
    [Trait("Decision", "ED-289")]
    public async Task A_cancelled_probe_stops_and_leaves_no_done_item()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Seconds);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Preparation.ProbeAsync(rig.Items, cancelled.Token));
        Assert.DoesNotContain(rig.Items.All, i => i.State == VoicePrepState.Done);
    }
}
