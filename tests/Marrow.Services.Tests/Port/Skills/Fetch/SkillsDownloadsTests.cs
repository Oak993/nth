// PORT-ONLY: first-release feature unit SK-16 (DL-19): ONE download per item, whoever asks (ONDEMAND-PLAN.md section 7). Scripted seams; no network, no CLI.
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Decision", "ED-326")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class SkillsDownloadsTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task The_same_item_asked_for_twice_joins_the_running_download_and_both_see_its_progress()
    {
        using var rig = new FetchRig();
        var downloads = rig.Host.Downloads;
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new List<double?>();
        var second = new List<double?>();
        var a = downloads.Install(rig.Item("S1"), new PipelineProgress(first), CancellationToken.None);
        await rig.Entered("S1");
        var b = downloads.Install(rig.Item("S1"), new PipelineProgress(second), CancellationToken.None);
        Assert.Equal([0.25], second); // the joiner is told where the download is now
        rig.Hold["S1"].SetResult();
        var paths = await Task.WhenAll(a, b).WaitAsync(Guard);
        Assert.Equal(paths[0], paths[1]);
        Assert.Equal(["S1"], rig.Downloaded);
        Assert.Equal(1, first.Last());
        Assert.Equal(1, second.Last());
        Assert.False(downloads.IsRunning("S1"));
    }

    [Fact]
    public async Task One_asker_leaving_does_not_stop_the_download_the_last_one_leaving_does()
    {
        using var rig = new FetchRig();
        var downloads = rig.Host.Downloads;
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var one = new CancellationTokenSource();
        using var two = new CancellationTokenSource();
        var a = downloads.Install(rig.Item("S1"), new PipelineProgress([]), one.Token);
        await rig.Entered("S1");
        var b = downloads.Install(rig.Item("S1"), new PipelineProgress([]), two.Token);
        await one.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.WaitAsync(Guard));
        Assert.True(downloads.IsRunning("S1"));
        rig.Hold["S1"].SetResult();
        await b.WaitAsync(Guard);
        Assert.Equal(ItemState.Installed, rig.Host.Pipeline.State.Get("S1").State);
        Assert.Equal(["S1"], rig.Downloaded);
    }

    [Fact]
    public async Task When_every_asker_has_left_the_download_is_cancelled_and_the_item_is_not_installed()
    {
        using var rig = new FetchRig();
        var downloads = rig.Host.Downloads;
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var one = new CancellationTokenSource();
        using var two = new CancellationTokenSource();
        var a = downloads.Install(rig.Item("S1"), new PipelineProgress([]), one.Token);
        await rig.Entered("S1");
        var b = downloads.Install(rig.Item("S1"), new PipelineProgress([]), two.Token);
        await one.CancelAsync();
        await two.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.WaitAsync(Guard));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.WaitAsync(Guard));
        await WaitUntil(() => !downloads.IsRunning("S1"));
        Assert.Null(rig.Host.Pipeline.InstalledFolder("S1"));
    }

    [Fact]
    public async Task A_failure_reaches_every_asker_and_the_next_ask_starts_a_new_download()
    {
        using var rig = new FetchRig();
        var downloads = rig.Host.Downloads;
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Fail["S1"] = "Couldn't reach example.test.";
        var a = downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        var b = downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        rig.Hold["S1"].SetResult();
        Assert.Equal("Couldn't reach example.test.", (await Assert.ThrowsAsync<IOException>(() => a.WaitAsync(Guard))).Message);
        Assert.Equal("Couldn't reach example.test.", (await Assert.ThrowsAsync<IOException>(() => b.WaitAsync(Guard))).Message);
        Assert.False(downloads.IsRunning("S1"));
        rig.Fail.Clear();
        await downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        Assert.Equal(["S1", "S1"], rig.Downloaded);
    }

    [Fact]
    public async Task An_item_that_needs_another_downloads_the_need_first_and_shares_a_download_of_the_need_already_running()
    {
        using var rig = new FetchRig();
        var downloads = rig.Host.Downloads;
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var need = downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        var reports = new List<double?>();
        var wanted = downloads.Install(rig.Item("S2"), new PipelineProgress(reports), CancellationToken.None);
        rig.Hold["S1"].SetResult();
        await Task.WhenAll(need, wanted).WaitAsync(Guard);
        Assert.Equal(["S1", "S2"], rig.Downloaded);
        Assert.Equal(1, reports.Last());
        Assert.All(reports, value => Assert.InRange(value!.Value, 0, 1));
        Assert.Equal(reports.OrderBy(v => v).ToList(), reports); // one bar that only moves forward across the need and the item
    }

    [Fact]
    public async Task An_item_that_is_here_is_not_downloaded_again_and_its_folder_is_given_back()
    {
        using var rig = new FetchRig();
        var folder = await rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        var again = await rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        Assert.Equal(folder, again);
        Assert.Equal(["S1"], rig.Downloaded);
    }

    [Fact]
    public async Task A_running_download_is_visible_to_every_surface_and_a_watcher_hears_of_each_change()
    {
        using var rig = new FetchRig();
        var downloads = rig.Host.Downloads;
        var heard = 0;
        using var watch = downloads.Watch(() => Interlocked.Increment(ref heard));
        Assert.Null(downloads.Running("S1"));
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        Assert.Equal(0.25, downloads.Running("S1")!.Progress);
        Assert.True(downloads.IsRunning("S1"));
        Assert.True(Volatile.Read(ref heard) >= 1);
        rig.Hold["S1"].SetResult();
        await run.WaitAsync(Guard);
        Assert.Null(downloads.Running("S1"));
        var after = Volatile.Read(ref heard);
        watch.Dispose();
        await Task.Yield();
        Assert.Equal(after, Volatile.Read(ref heard));
    }

    [Fact]
    public async Task With_the_seams_unbound_nothing_can_be_fetched_and_the_item_fails_with_the_one_unavailable_message()
    {
        using var rig = new FetchRig(bound: false);
        var downloads = rig.Host.Downloads;
        Assert.False(downloads.Available);
        Assert.False(downloads.CanFetch(rig.Item("S1")));
        var error = await Assert.ThrowsAsync<FirstInstallUnavailableException>(() => downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None));
        Assert.Equal(LibraryWords.DownloadsUnavailable, error.Message);
        Assert.Empty(rig.Downloaded);
    }

    [Fact]
    public async Task With_the_seams_bound_fetching_is_available_for_a_complete_pin_and_not_for_an_unpinned_item()
    {
        using var rig = new FetchRig();
        var downloads = rig.Host.Downloads;
        Assert.True(downloads.Available);
        Assert.True(downloads.CanFetch(rig.Item("S1")));
        Assert.False(downloads.CanFetch(rig.Item("U1")));
        var error = await Assert.ThrowsAsync<FirstInstallUnavailableException>(() => downloads.Install(rig.Item("U1"), new PipelineProgress([]), CancellationToken.None));
        Assert.Equal(LibraryWords.DownloadsUnavailable, error.Message);
        Assert.Empty(rig.Downloaded);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Guard;
        while (!condition()) { Assert.True(DateTime.UtcNow < until, "the condition never came true"); await Task.Delay(10); }
    }
}
