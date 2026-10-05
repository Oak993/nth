// PORT-ONLY: first-release feature unit SK-16 (DL-19, ED-326 (6)(a)): the pipeline's first-install path. No network, no CLI: scripted seams on a fresh data root.
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Decision", "ED-326")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class PipelineFirstInstallTests
{
    [Fact]
    public async Task A_pinned_item_is_downloaded_once_installed_routed_and_committed()
    {
        using var rig = new FetchRig();
        var pipeline = rig.Host.Pipeline;
        var reports = new List<double?>();
        var path = await pipeline.InstallAsync("S1", new PipelineProgress(reports));
        Assert.Equal(["S1"], rig.Downloaded);
        Assert.StartsWith(rig.Host.Paths.Store, path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(path, "skill", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(path, "LICENSE")));
        var record = pipeline.State.Get("S1");
        Assert.Equal((ItemState.Installed, rig.Item("S1").Pin), (record.State, record.InstalledPin));
        Assert.Equal(path, pipeline.InstalledFolder("S1"));
        Assert.Equal(1, reports.Last());
        Assert.Equal(rig.Item("S1").Pin, AddonsStateStore.Load(rig.Host.Paths.StateFile).Get("S1").InstalledPin);
    }

    [Fact]
    public async Task After_a_restart_the_item_is_still_installed_and_nothing_is_fetched_on_its_own()
    {
        using var rig = new FetchRig();
        var path = await rig.Host.Pipeline.InstallAsync("S1", new PipelineProgress([]));
        var again = rig.Reopen();
        Assert.Equal(ItemState.Installed, again.Pipeline.State.Get("S1").State);
        Assert.Equal(path, again.Pipeline.InstalledFolder("S1"));
        Assert.Equal(["S1"], rig.Downloaded);
    }

    [Fact]
    public async Task An_item_that_is_not_installed_has_no_folder()
    {
        using var rig = new FetchRig();
        Assert.Null(rig.Host.Pipeline.InstalledFolder("S1"));
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("U1", "unpinned")]
    [InlineData("S2", "needs S1")]
    [InlineData("nope", "not in the catalogue")]
    public async Task An_item_that_cannot_be_installed_is_refused_before_any_download_and_writes_nothing(string id, string why)
    {
        using var rig = new FetchRig();
        var error = await Record.ExceptionAsync(() => rig.Host.Pipeline.InstallAsync(id, new PipelineProgress([])));
        Assert.True(error is InvalidOperationException, why + ": " + error);
        Assert.Empty(rig.Downloaded);
        Assert.False(Directory.Exists(rig.Host.Paths.Store));
        Assert.Empty(rig.Host.Pipeline.State.Items);
    }

    [Fact]
    public async Task Terms_not_accepted_refuse_the_install_before_any_download()
    {
        using var rig = new FetchRig();
        await Assert.ThrowsAsync<PipelineTermsRequiredException>(() => rig.Host.Pipeline.InstallAsync("N1", new PipelineProgress([])));
        Assert.Empty(rig.Downloaded);
    }

    [Fact]
    public async Task With_the_download_seams_unbound_the_pipeline_says_so_and_fetching_is_not_bound()
    {
        using var rig = new FetchRig(bound: false);
        Assert.False(rig.Host.Pipeline.FetchingBound);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.Pipeline.InstallAsync("S1", new PipelineProgress([])));
        Assert.Equal("Pipeline seams are not bound.", error.Message);
        Assert.Empty(rig.Downloaded);
    }

    [Fact]
    public async Task Fetching_is_bound_when_both_seams_are_bound()
    {
        using var rig = new FetchRig();
        Assert.True(rig.Host.Pipeline.FetchingBound);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task An_installed_item_is_not_installed_a_second_time()
    {
        using var rig = new FetchRig();
        await rig.Host.Pipeline.InstallAsync("S1", new PipelineProgress([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.Pipeline.InstallAsync("S1", new PipelineProgress([])));
        Assert.Equal(["S1"], rig.Downloaded);
    }

    [Fact]
    public async Task A_cancel_stops_the_install_and_leaves_the_item_not_installed()
    {
        using var rig = new FetchRig();
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var run = rig.Host.Pipeline.InstallAsync("S1", new PipelineProgress([]), cancel.Token);
        await rig.Entered("S1");
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Null(rig.Host.Pipeline.InstalledFolder("S1"));
        Assert.Empty(rig.Host.Pipeline.State.Items);
        Assert.False(Directory.Exists(rig.Host.Paths.Store) && Directory.EnumerateFileSystemEntries(rig.Host.Paths.Store).Any(p => !Path.GetFileName(p).StartsWith("tmp-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_failed_download_leaves_nothing_installed_and_the_same_item_installs_on_the_next_try()
    {
        using var rig = new FetchRig();
        rig.Fail["S1"] = "Couldn't reach example.test.";
        var error = await Assert.ThrowsAsync<IOException>(() => rig.Host.Pipeline.InstallAsync("S1", new PipelineProgress([])));
        Assert.Equal("Couldn't reach example.test.", error.Message);
        Assert.Empty(rig.Host.Pipeline.State.Items);
        rig.Fail.Clear();
        await rig.Host.Pipeline.InstallAsync("S1", new PipelineProgress([]));
        Assert.Equal(["S1", "S1"], rig.Downloaded);
        Assert.Equal(ItemState.Installed, rig.Host.Pipeline.State.Get("S1").State);
    }

    [Fact]
    public async Task Changed_is_raised_once_after_a_finished_install_and_not_for_a_refusal()
    {
        using var rig = new FetchRig();
        var raised = 0;
        rig.Host.Pipeline.Changed += () => Interlocked.Increment(ref raised);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.Pipeline.InstallAsync("U1", new PipelineProgress([])));
        Assert.Equal(0, raised);
        await rig.Host.Pipeline.InstallAsync("S1", new PipelineProgress([]));
        Assert.Equal(1, raised);
    }
}
