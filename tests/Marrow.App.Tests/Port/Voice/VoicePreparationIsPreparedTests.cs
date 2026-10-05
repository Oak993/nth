using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15 (V-10.5, V-10.7): IsPrepared is true only for a stored result whose key is today's; a stale or missing result runs the check again at the next first-run moment.
public sealed class VoicePreparationIsPreparedTests
{
    private static readonly Dictionary<string, double> Fast = new() { ["large-v3-turbo-q8_0"] = 0.6 };

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public void Nothing_stored_is_not_prepared_and_gives_no_record()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        Assert.False(rig.Preparation.IsPrepared(out var record));
        Assert.Null(record);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task After_a_first_run_the_stored_result_is_prepared_under_the_probed_key()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        var (_, _, result) = await rig.FirstRunAsync();
        Assert.True(rig.Preparation.IsPrepared(out var record));
        Assert.Same(result.Record, record);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task A_result_under_another_key_is_stale_but_still_given_back()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        var stale = new VoiceHardwareRecord { Key = "an old key", Model = "base.en", LiveWords = true };
        rig.Settings.Record = stale;
        Assert.False(rig.Preparation.IsPrepared(out var record));
        Assert.Same(stale, record);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public void A_stored_result_with_no_probe_and_no_key_source_is_not_prepared()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Settings.Record = new VoiceHardwareRecord { Key = "k" };
        Assert.False(rig.Preparation.IsPrepared(out _));
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public void The_key_source_of_the_constructor_is_asked_when_nothing_was_probed()
    {
        var settings = new VoicePrepSettingsFake { Record = new VoiceHardwareRecord { Key = "k1" } };
        var items = new VoicePrepItems();
        var current = "k1";
        var preparation = new VoicePreparation(new ScriptedProbe(ScriptedMachines.OwnersDesktop), new VoicePrepModelSource { ThrowOnAnyTransfer = true },
            () => throw new InvalidOperationException("no engine"), settings, () => ReadOnlyMemory<float>.Empty, "app", "rt", null, () => current);
        Assert.True(preparation.IsPrepared(out _));
        current = "k2";
        Assert.False(preparation.IsPrepared(out _));
        Assert.Empty(items.All);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task Prepare_with_a_current_result_does_no_work_and_gives_the_stored_record()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        var (_, _, first) = await rig.FirstRunAsync();
        rig.Models.ThrowOnAnyTransfer = true;
        var items = new VoicePrepItems();
        var again = await rig.Preparation.PrepareAsync(items, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.Prepared, again.Status);
        Assert.Same(first.Record, again.Record);
        Assert.Empty(items.All);
        Assert.Single(rig.Engine.Loads);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task Prepare_probes_once_then_reuses_the_profile()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, Fast);
        rig.Preparation.GrantConsent();
        await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        var result = await rig.Preparation.PrepareAsync(rig.Items, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.Prepared, result.Status);
        Assert.Empty(rig.Items.Of("voice.probe"));
    }
}
