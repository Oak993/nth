using System.Collections.Concurrent;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

// All inputs are local fixtures, download seams are scripted, protection is fake.
// No HTTP client, vendor executable, or installer process is used.
[Collection("Dev data root env")]
[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Review", "SK-16")]
[Trait("Decision", "ED-326")]
public sealed class REVIEW_Sk16FetchTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task REVIEW_Unbound_single_download_does_not_queue_or_fail_a_row()
    {
        using var rig = new FetchRig(bound: false);
        using var controller = new LibraryController(LibrarySection.Skills, new HostLibraryBackend(rig.Host, rig.Catalogue));
        Assert.False(controller.FetchingAvailable);
        await controller.Download("S1").WaitAsync(Guard);
        Assert.Equal(ItemState.Available, controller.Rows.Single(r => r.Id == "S1").State);
        Assert.Equal(0, controller.Revision);
        Assert.Empty(rig.Downloaded);
    }

    [Fact]
    public async Task REVIEW_Unbound_Continue_does_not_start_a_download_all_run()
    {
        using var rig = new LibraryRig(install: new FailClosedFirstInstall());
        rig.Page.OpenDownloadAll();
        await rig.Page.Continue().WaitAsync(Guard);
        Assert.Equal(RunPhase.Confirm, rig.Page.Dialog.Phase);
        Assert.False(rig.Page.Dialog.CanContinue);
        Assert.Equal(ItemState.Available, rig.Row("S1").State);
    }

    [Fact]
    public async Task REVIEW_Offer_failure_is_returned_without_waiting_for_another_held_item()
    {
        using var rig = new FetchRig();
        rig.Fail["S1"] = "fixture download failed";
        rig.Hold["S3"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var run = rig.Host.OfferDownloads.Install([rig.Item("S1"), rig.Item("S3")], _ => { }, cancel.Token);
        try
        {
            await rig.Entered("S3"); // S1 has failed and released the pipeline; S3 is held.
            var error = await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("fixture download failed", error.Message);
        }
        finally
        {
            cancel.Cancel();
            rig.Hold["S3"].TrySetResult();
            try { await run.WaitAsync(Guard); } catch (Exception) { }
            // Drain the serialized pipeline before deleting the disposable root.
            await rig.Host.Pipeline.RemoveAsync("S3").WaitAsync(Guard);
        }
    }

    [Fact]
    public async Task REVIEW_Offer_progress_counts_dependency_bytes_once()
    {
        using var rig = new FetchRig();
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Hold["S2"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = new ConcurrentQueue<Marrow.Services.Port.Skills.Offers.OfferProgress>();
        var run = rig.Host.OfferDownloads.Install([rig.Item("S1"), rig.Item("S2")], reports.Enqueue, CancellationToken.None);
        try
        {
            await rig.Entered("S1");
            // S2 must have subscribed to S1's flight before that dependency completes.
            Assert.True(SpinWait.SpinUntil(() => reports.Count >= 2, Guard));
            rig.Hold["S1"].TrySetResult();
            await rig.Entered("S2");
            var expected = (long)Math.Floor(SkillsDownloads.Bytes(rig.Item("S1")) + 0.25 * SkillsDownloads.Bytes(rig.Item("S2")));
            Assert.Equal(expected, reports.Last().BytesDone);
        }
        finally
        {
            rig.Hold["S1"].TrySetResult();
            rig.Hold["S2"].TrySetResult();
            await run.WaitAsync(Guard);
        }
    }

    [Fact]
    public async Task REVIEW_Already_installed_offer_item_reports_completed_progress()
    {
        using var rig = new FetchRig();
        await rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        var reports = new ConcurrentQueue<Marrow.Services.Port.Skills.Offers.OfferProgress>();
        var paths = await rig.Host.OfferDownloads.Install([rig.Item("S1"), rig.Item("S3")], reports.Enqueue, CancellationToken.None).WaitAsync(Guard);
        Assert.Equal(2, paths.Count);
        Assert.Equal(Marrow.Services.Port.Skills.Offers.OfferStage.Checking, reports.Last().Stage);
        Assert.Equal(reports.Last().BytesTotal, reports.Last().BytesDone);
    }

    [Fact]
    public async Task REVIEW_Replayed_progress_allows_another_thread_to_read_shared_download_state()
    {
        using var rig = new FetchRig();
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        Task<FetchView?>? read = null;
        using var cancel = new CancellationTokenSource();
        var joined = rig.Host.OfferDownloads.Install([rig.Item("S1")], _ =>
        {
            read = Task.Run(() => rig.Host.Downloads.Running("S1"));
            Assert.True(read.Wait(TimeSpan.FromSeconds(1)), "A progress callback cannot hold the lock needed by a different thread reading the same flight.");
        }, cancel.Token);
        try
        {
            rig.Hold["S1"].TrySetResult();
            await Task.WhenAll(first, joined).WaitAsync(Guard);
        }
        finally
        {
            cancel.Cancel();
            rig.Hold["S1"].TrySetResult();
            await first.WaitAsync(Guard);
            if (read is not null) await read.WaitAsync(Guard);
        }
    }

    [Fact]
    public async Task REVIEW_Installed_skill_set_survives_restart_with_a_new_catalogue_pin_as_update_available()
    {
        using var rig = new FetchRig();
        var folder = await rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        rig.Host.Dispose();
        var nextItem = rig.Item("S1") with { Pin = new string('a', 40) };
        var nextCatalogue = SkillsHostRig.Catalogue(nextItem);
        using var reopened = SkillsHost.Open(rig.Root, SkillsHostRig.Options(rig.Protection, nextCatalogue));
        Assert.True(Directory.Exists(folder));
        Assert.Equal(ItemState.UpdateAvailable, reopened.Pipeline.State.Get("S1").State);
        Assert.Equal(folder, reopened.Pipeline.InstalledFolder("S1"));
        Assert.Single(reopened.Pipeline.UpdateItems());
        Assert.DoesNotContain("S1", reopened.Unrestored);
    }
}
