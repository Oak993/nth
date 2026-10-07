using Marrow.Core.Port.Skills.State;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Shims;
namespace Marrow.Services.Tests.Port.Skills.Pipelines;
[Trait("Spec", "DL-8")]
public sealed class PipelineRecoveryTests
{
    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Actual_shim_reader_selects_the_committed_pin_and_refuses_removed_item()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create();
        var sidecar = Path.Combine(rig.Paths.Addons, "fixture.shim");
        ShimDefinition.Write(sidecar, new() { Target = "unused", RoutingStateFile = rig.Paths.StateFile, RoutingName = "fixture.exe" });
        var old = ShimDefinition.Read(sidecar).Target;
        await pipeline.UpdateAsync("fixture", rig.Progress);
        Assert.NotEqual(old, ShimDefinition.Read(sidecar).Target);
        Assert.Equal(Path.Combine(pipeline.Snapshot().Single().Folder, "payload.txt"), ShimDefinition.Read(sidecar).Target);
        await pipeline.RemoveAsync("fixture");
        Assert.Throws<InvalidOperationException>(() => ShimDefinition.Read(sidecar));
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task UE2_runs_the_group_and_reports_item_progress_and_updated_versions()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create();
        var updates = new UpdateOrchestrator(pipeline.UpdateItems());
        var progress = new List<double?>();
        updates.Changed += () => progress.Add(updates.Rows.Single().Progress);
        await updates.CheckNow();
        var before = Assert.Single(updates.Rows);
        Assert.Equal(rig.Old.Pin, before.Installed);
        Assert.Equal(rig.Next.Pin, before.Latest);
        Assert.Equal(UpdateRowState.UpdateAvailable, before.State);
        await updates.UpdateEverything();
        Assert.Equal(UpdateRowState.Updated, updates.Rows.Single().State);
        Assert.Equal(rig.Next.Pin, updates.Rows.Single().Installed);
        Assert.Contains(0.5, progress);
        Assert.Contains(1, progress);
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Failed_update_retries_without_a_colliding_final_folder()
    {
        using var rig = new PipelineRig();
        var fail = true;
        var pipeline = await rig.Create(step => { if (fail && step == PipelineStep.BeforeCommit) throw new IOException("Injected crash"); });
        await Assert.ThrowsAsync<IOException>(() => pipeline.UpdateAsync("fixture", rig.Progress));
        fail = false;
        await pipeline.UpdateAsync("fixture", rig.Progress);
        Assert.Equal(rig.Next.Pin, pipeline.State.Get("fixture").InstalledPin);
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Cancel_before_commit_keeps_previous_pin_and_can_retry()
    {
        using var rig = new PipelineRig();
        using var cancellation = new CancellationTokenSource();
        var pipeline = await rig.Create(step => { if (step == PipelineStep.RoutesPrepared) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.UpdateAsync("fixture", rig.Progress, cancellation.Token));
        using var old = pipeline.Acquire("fixture");
        Assert.NotNull(old);
        Assert.Equal(rig.Old.Pin, pipeline.State.Get("fixture").InstalledPin);
        await pipeline.UpdateAsync("fixture", rig.Progress);
        Assert.Equal(rig.Next.Pin, pipeline.State.Get("fixture").InstalledPin);
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Tampered_routing_cannot_make_GC_delete_the_trusted_active_tree()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.Create();
        var old = pipeline.Snapshot().Single();
        var file = PipelineRoutingReader.FileFor(rig.Paths.StateFile, old.Id, old.Pin);
        var text = File.ReadAllText(file);
        // integ-sk8-sk9: compare JSON-encoded paths; on Windows the route file escapes every backslash, so a raw Replace changed nothing.
        static string Json(string path) => System.Text.Json.JsonSerializer.Serialize(path)[1..^1];
        var tampered = text.Replace(Json(old.Folder), Json(Path.Combine(rig.Paths.Tools, "other")), StringComparison.Ordinal);
        Assert.NotEqual(text, tampered);
        File.WriteAllText(file, tampered);
        Assert.Throws<InvalidDataException>(() => pipeline.Collect());
        Assert.True(Directory.Exists(old.Folder));
    }
    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Missing_pipeline_seams_fail_closed_before_any_download_or_install()
    {
        using var rig = new PipelineRig();
        await rig.Create();
        var unbound = rig.Unbound();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unbound.UpdateAsync("fixture", rig.Progress));
        Assert.Equal(0, rig.Downloads);
        Assert.Equal(rig.Old.Pin, unbound.State.Get("fixture").InstalledPin);
        using var old = unbound.Acquire("fixture");
        Assert.NotNull(old);
    }
}
