// PORT-ONLY: first-release feature unit SK-16 (DL-19): the Settings pages' backend and controller over the host's shared downloads. Scripted seams on a fresh data root; no network, no CLI.
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Offers;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Decision", "ED-326")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class HostLibraryBackendFetchTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    [Fact]
    public void With_the_seams_bound_the_backend_installs_through_the_shared_downloads_and_says_fetching_is_available()
    {
        using var rig = new FetchRig();
        var backend = new HostLibraryBackend(rig.Host, rig.Catalogue);
        Assert.IsType<SkillsFirstInstall>(backend.Installs);
        Assert.True(backend.FetchingAvailable);
        Assert.True(new LibraryController(LibrarySection.Skills, backend).FetchingAvailable);
    }

    [Fact]
    public void With_the_seams_unbound_the_backend_keeps_the_seam_that_fails_closed_and_says_fetching_is_not_available()
    {
        using var rig = new FetchRig(bound: false);
        var backend = new HostLibraryBackend(rig.Host, rig.Catalogue);
        Assert.IsType<FailClosedFirstInstall>(backend.Installs);
        Assert.False(backend.FetchingAvailable);
        Assert.False(new LibraryController(LibrarySection.Skills, backend).FetchingAvailable);
    }

    [Fact]
    public async Task A_download_pressed_on_the_page_installs_the_item_through_the_pipeline_and_its_row_ends_Installed()
    {
        using var rig = new FetchRig();
        using var controller = new LibraryController(LibrarySection.Skills, new HostLibraryBackend(rig.Host, rig.Catalogue));
        await controller.Download("S1").WaitAsync(Guard);
        Assert.Equal(["S1"], rig.Downloaded);
        Assert.Equal(ItemState.Installed, controller.Rows.Single(r => r.Id == "S1").State);
        Assert.Equal(ItemState.Installed, rig.Host.Pipeline.State.Get("S1").State);
    }

    [Fact]
    public async Task A_download_an_offer_started_shows_on_the_page_with_its_progress_and_a_press_does_not_start_a_second_one()
    {
        using var rig = new FetchRig();
        using var controller = new LibraryController(LibrarySection.Skills, new HostLibraryBackend(rig.Host, rig.Catalogue));
        var changes = 0;
        controller.Changed += () => Interlocked.Increment(ref changes);
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var offer = new SkillsOfferInstalls(rig.Host.Downloads).Install([rig.Item("S1")], _ => { }, CancellationToken.None);
        await rig.Entered("S1");
        var row = controller.Rows.Single(r => r.Id == "S1");
        Assert.Equal((ItemState.Downloading, 0.25), (row.State, row.Progress));
        Assert.True(Volatile.Read(ref changes) > 0);
        var pressed = controller.Download("S1"); // the row is downloading: the press is ignored, the one download goes on
        rig.Hold["S1"].SetResult();
        await Task.WhenAll(offer, pressed).WaitAsync(Guard);
        Assert.Equal(["S1"], rig.Downloaded);
        await WaitUntil(() => controller.Rows.Single(r => r.Id == "S1").State == ItemState.Installed);
    }

    [Fact]
    public async Task A_download_the_page_started_is_joined_by_an_offer_and_the_page_row_follows_the_same_progress()
    {
        using var rig = new FetchRig();
        using var controller = new LibraryController(LibrarySection.Skills, new HostLibraryBackend(rig.Host, rig.Catalogue));
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pressed = controller.Download("S1");
        await rig.Entered("S1");
        var card = new List<OfferProgress>();
        var offer = new SkillsOfferInstalls(rig.Host.Downloads).Install([rig.Item("S1")], card.Add, CancellationToken.None);
        Assert.Equal(OfferStage.Downloading, card.First().Stage);
        rig.Hold["S1"].SetResult();
        await Task.WhenAll(pressed, offer).WaitAsync(Guard);
        Assert.Equal(["S1"], rig.Downloaded);
        Assert.Equal(OfferStage.Checking, card.Last().Stage);
        Assert.Equal(ItemState.Installed, controller.Rows.Single(r => r.Id == "S1").State);
    }

    [Fact]
    public async Task Cancel_on_the_page_stops_a_download_an_offer_started_and_the_row_goes_back_to_what_it_was()
    {
        using var rig = new FetchRig();
        using var controller = new LibraryController(LibrarySection.Skills, new HostLibraryBackend(rig.Host, rig.Catalogue));
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var offer = new SkillsOfferInstalls(rig.Host.Downloads).Install([rig.Item("S1")], _ => { }, CancellationToken.None);
        await rig.Entered("S1");
        controller.CancelItem("S1");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => offer.WaitAsync(Guard));
        await WaitUntil(() => controller.Rows.Single(r => r.Id == "S1").State == ItemState.Available);
        Assert.Equal(["S1"], rig.Downloaded);
        Assert.Null(rig.Host.Pipeline.InstalledFolder("S1"));
    }

    [Fact]
    public async Task With_the_seams_unbound_a_press_fails_the_row_with_the_one_unavailable_message_and_nothing_is_fetched()
    {
        using var rig = new FetchRig(bound: false);
        using var controller = new LibraryController(LibrarySection.Skills, new HostLibraryBackend(rig.Host, rig.Catalogue));
        await controller.Download("S1").WaitAsync(Guard);
        var row = controller.Rows.Single(r => r.Id == "S1");
        Assert.Equal((ItemState.Failed, LibraryWords.DownloadsUnavailable), (row.State, row.Failure));
        Assert.Empty(rig.Downloaded);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Guard;
        while (!condition()) { Assert.True(DateTime.UtcNow < until, "the condition never came true"); await Task.Delay(10); }
    }
}
