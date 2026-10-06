// PORT-ONLY: first-release feature unit SK-16, review fixes (ED-368): the cases the reviewer's REVIEW_ tests do not pin. Scripted seams on a fresh data root; no network, no CLI.
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Decision", "ED-326")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class FetchReviewFixesTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task An_offer_that_fails_on_one_item_keeps_a_transfer_a_Settings_page_also_asked_for_running()
    {
        using var rig = new FetchRig();
        rig.Fail["S1"] = "fixture download failed";
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Hold["S3"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var offer = rig.Host.OfferDownloads.Install([rig.Item("S1"), rig.Item("S3")], _ => { }, CancellationToken.None);
        await rig.Entered("S1"); // S1 holds the pipeline; S3's download waits behind it
        var settings = new SkillsFirstInstall(rig.Host.Downloads).InstallAsync(rig.Item("S3"), new PipelineProgress([]), CancellationToken.None);
        rig.Hold["S1"].SetResult(); // S1 fails
        var error = await Assert.ThrowsAsync<IOException>(() => offer.WaitAsync(Guard));
        Assert.Equal("fixture download failed", error.Message);
        await rig.Entered("S3");
        Assert.True(rig.Host.Downloads.IsRunning("S3")); // the page still wants it
        rig.Hold["S3"].SetResult();
        await settings.WaitAsync(Guard);
        Assert.Equal(ItemState.Installed, rig.Host.Pipeline.State.Get("S3").State);
    }

    [Fact]
    public async Task An_offer_that_fails_on_one_item_stops_a_transfer_only_it_asked_for()
    {
        using var rig = new FetchRig();
        rig.Fail["S1"] = "fixture download failed";
        rig.Hold["S3"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstAsker = new CancellationTokenSource();
        var first = rig.Host.Downloads.Install(rig.Item("S3"), new PipelineProgress([]), firstAsker.Token);
        await rig.Entered("S3"); // deterministically hold S3 before asking for failing S1
        var offer = rig.Host.OfferDownloads.Install([rig.Item("S1"), rig.Item("S3")], _ => { }, CancellationToken.None);
        firstAsker.Cancel(); // only the offer still wants S3
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Guard));
        await Assert.ThrowsAsync<IOException>(() => offer.WaitAsync(Guard));
        var until = DateTime.UtcNow + Guard;
        while (rig.Host.Downloads.IsRunning("S3")) { Assert.True(DateTime.UtcNow < until); await Task.Delay(10); }
        Assert.Null(rig.Host.Pipeline.InstalledFolder("S3"));
    }

    [Fact]
    public async Task A_late_joiner_is_given_the_replay_before_any_newer_report_whatever_the_threads_do()
    {
        using var rig = new FetchRig();
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        var seen = new List<double?>();
        var joined = rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress(seen), CancellationToken.None);
        rig.Hold["S1"].SetResult();
        await Task.WhenAll(first, joined).WaitAsync(Guard);
        var values = seen.Select(v => v!.Value).ToArray();
        Assert.Equal(values.OrderBy(v => v).ToArray(), values);
        Assert.Equal(1, values.Last());
    }

    [Fact]
    public async Task A_kept_catalogue_entry_that_does_not_hold_up_leaves_the_item_unrestored_after_an_update_of_the_catalogue()
    {
        using var rig = new FetchRig();
        await rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        rig.Host.Dispose();
        var manifest = Directory.EnumerateFiles(Path.Combine(rig.Root, "addons", "manifests"), "*.json").Single();
        File.WriteAllText(manifest, "{\"Id\":\"S1\",\"Pin\":\"" + rig.Item("S1").Pin + "\",\"Item\":\"not a catalogue\"}");
        using var reopened = SkillsHost.Open(rig.Root, SkillsHostRig.Options(rig.Protection, SkillsHostRig.Catalogue(rig.Item("S1") with { Pin = new string('a', 40) })));
        Assert.Contains("S1", reopened.Unrestored);
        Assert.Null(reopened.Pipeline.InstalledFolder("S1"));
    }

    [Fact]
    public async Task A_kept_catalogue_entry_for_another_pin_is_not_used()
    {
        using var rig = new FetchRig();
        await rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        rig.Host.Dispose();
        Assert.Null(PipelineManifests.Read(Path.Combine(rig.Root, "addons", "state.json"), "S1", new string('b', 40)));
        Assert.NotNull(PipelineManifests.Read(Path.Combine(rig.Root, "addons", "state.json"), "S1", rig.Item("S1").Pin!));
    }

    [Fact]
    public async Task An_installed_set_that_is_damaged_after_a_catalogue_update_is_still_found_damaged_against_the_kept_inventory()
    {
        using var rig = new FetchRig();
        var folder = await rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        rig.Host.Dispose();
        var trees = new Marrow.Services.Port.Skills.Store.StoreTrees(rig.Protection);
        trees.Unlock(folder);
        File.WriteAllText(Path.Combine(folder, "skill", "SKILL.md"), "tampered");
        using var reopened = SkillsHost.Open(rig.Root, SkillsHostRig.Options(rig.Protection, SkillsHostRig.Catalogue(rig.Item("S1") with { Pin = new string('a', 40) })));
        Assert.Null(reopened.Pipeline.Acquire("S1")); // refused: the tree is not the one that was verified
        Assert.Equal(ItemState.Damaged, reopened.Pipeline.State.Get("S1").State);
    }
}
