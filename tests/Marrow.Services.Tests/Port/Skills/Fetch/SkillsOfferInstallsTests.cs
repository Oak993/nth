// PORT-ONLY: first-release feature unit SK-16 (DL-19): Settings and the in-chat offers share one download per item, in either order, and see the same progress. Scripted seams; no network.
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Offers;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Decision", "ED-326")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class SkillsOfferInstallsTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task An_offer_joins_a_download_Settings_has_running_and_the_item_is_fetched_once()
    {
        using var rig = new FetchRig();
        var settings = new SkillsFirstInstall(rig.Host.Downloads);
        var offers = new SkillsOfferInstalls(rig.Host.Downloads);
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var row = new List<double?>();
        var started = settings.InstallAsync(rig.Item("S1"), new PipelineProgress(row), CancellationToken.None);
        await rig.Entered("S1");
        var card = new List<OfferProgress>();
        var joined = offers.Install([rig.Item("S1")], card.Add, CancellationToken.None);
        rig.Hold["S1"].SetResult();
        var paths = await joined.WaitAsync(Guard);
        await started.WaitAsync(Guard);
        Assert.Equal(["S1"], rig.Downloaded);
        Assert.Equal(rig.Host.Pipeline.InstalledFolder("S1"), paths["S1"]);
        Assert.Equal(1, row.Last());
        var total = rig.Item("S1").Files.Sum(f => f.Size);
        Assert.Equal(new OfferProgress(OfferStage.Downloading, total / 4, total), card.First()); // the card is told where the download is at once
        Assert.Equal(new OfferProgress(OfferStage.Checking, total, total), card.Last());
        Assert.All(card, value => Assert.True(value.BytesDone <= value.BytesTotal));
    }

    [Fact]
    public async Task Settings_joins_a_download_an_offer_has_running_and_the_item_is_fetched_once()
    {
        using var rig = new FetchRig();
        var settings = new SkillsFirstInstall(rig.Host.Downloads);
        var offers = new SkillsOfferInstalls(rig.Host.Downloads);
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = offers.Install([rig.Item("S1")], _ => { }, CancellationToken.None);
        await rig.Entered("S1");
        var row = new List<double?>();
        var joined = settings.InstallAsync(rig.Item("S1"), new PipelineProgress(row), CancellationToken.None);
        Assert.Equal([0.25], row);
        rig.Hold["S1"].SetResult();
        await Task.WhenAll(started, joined).WaitAsync(Guard);
        Assert.Equal(["S1"], rig.Downloaded);
    }

    [Fact]
    public async Task A_card_of_two_items_gives_back_both_paths_and_fetches_each_once_needs_first()
    {
        using var rig = new FetchRig();
        var offers = new SkillsOfferInstalls(rig.Host.Downloads);
        var paths = await offers.Install([rig.Item("S1"), rig.Item("S2")], _ => { }, CancellationToken.None).WaitAsync(Guard);
        Assert.Equal(["S1", "S2"], rig.Downloaded);
        Assert.Equal(rig.Host.Pipeline.InstalledFolder("S1"), paths["S1"]);
        Assert.Equal(rig.Host.Pipeline.InstalledFolder("S2"), paths["S2"]);
    }

    [Fact]
    public async Task Attach_joins_only_a_download_that_is_running_and_fails_closed_otherwise()
    {
        using var rig = new FetchRig();
        var offers = new SkillsOfferInstalls(rig.Host.Downloads);
        var none = await Assert.ThrowsAsync<InvalidOperationException>(() => offers.Attach([rig.Item("S1")], _ => { }, CancellationToken.None));
        Assert.Equal("No existing download is bound.", none.Message);
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = new SkillsFirstInstall(rig.Host.Downloads).InstallAsync(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        var attached = offers.Attach([rig.Item("S1")], _ => { }, CancellationToken.None);
        rig.Hold["S1"].SetResult();
        var paths = await attached.WaitAsync(Guard);
        await running.WaitAsync(Guard);
        Assert.Equal(["S1"], rig.Downloaded);
        Assert.Equal(rig.Host.Pipeline.InstalledFolder("S1"), paths["S1"]);
    }

    [Fact]
    public async Task An_offer_cancelling_leaves_the_download_running_for_Settings()
    {
        using var rig = new FetchRig();
        var settings = new SkillsFirstInstall(rig.Host.Downloads);
        var offers = new SkillsOfferInstalls(rig.Host.Downloads);
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = settings.InstallAsync(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        using var cancel = new CancellationTokenSource();
        var offer = offers.Install([rig.Item("S1")], _ => { }, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => offer.WaitAsync(Guard));
        Assert.True(rig.Host.Downloads.IsRunning("S1"));
        rig.Hold["S1"].SetResult();
        await started.WaitAsync(Guard);
        Assert.Equal(["S1"], rig.Downloaded);
    }

    [Fact]
    public async Task An_item_that_is_here_gives_back_its_folder_without_a_download()
    {
        using var rig = new FetchRig();
        var folder = await new SkillsFirstInstall(rig.Host.Downloads).InstallAsync(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).ContinueWith(_ => rig.Host.Pipeline.InstalledFolder("S1")!);
        var paths = await new SkillsOfferInstalls(rig.Host.Downloads).Install([rig.Item("S1")], _ => { }, CancellationToken.None).WaitAsync(Guard);
        Assert.Equal(folder, paths["S1"]);
        Assert.Equal(["S1"], rig.Downloaded);
    }
}
