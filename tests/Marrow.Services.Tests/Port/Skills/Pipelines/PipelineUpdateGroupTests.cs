// PORT-ONLY: integ-sk8-sk9. DL-8: "update everything" receives the Skills and tools group with per-item progress and results (UE-2's orchestrator, offline items).
using Marrow.Core.Port.Updates;

namespace Marrow.Services.Tests.Port.Skills.Pipelines;

[Trait("Spec", "DL-8")]
public sealed class PipelineUpdateGroupTests
{
    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Update_everything_runs_each_item_one_at_a_time_with_its_own_progress_result_and_retry()
    {
        using var rig = new PipelineRig();
        var pipeline = await rig.CreateTwo();
        rig.FailDownload = "second";
        var updates = new UpdateOrchestrator(pipeline.UpdateItems());
        var seen = new List<(string Id, UpdateRowState State, double? Progress, UpdateRowState Other)>();
        updates.Changed += () =>
        {
            var rows = updates.Rows;
            foreach (var row in rows) seen.Add((row.Id, row.State, row.Progress, rows.Single(r => r.Id != row.Id).State));
        };
        await updates.CheckNow();
        Assert.Equal(new[] { "fixture", "second" }, updates.Rows.Select(r => r.Id).Order(StringComparer.Ordinal));
        Assert.All(updates.Rows, r => Assert.Equal(UpdateRowState.UpdateAvailable, r.State));
        Assert.All(updates.Rows, r => Assert.Equal(UpdateItemKind.SkillsAndTools, r.Kind));
        await updates.UpdateEverything();
        var fixture = updates.Rows.Single(r => r.Id == "fixture");
        var second = updates.Rows.Single(r => r.Id == "second");
        Assert.Equal(UpdateRowState.Updated, fixture.State);
        Assert.Equal(rig.Next.Pin, fixture.Installed);
        Assert.Equal(UpdateRowState.Failed, second.State);
        Assert.Equal("Injected failure", second.Message);
        Assert.Equal(rig.Old.Pin, second.Installed); // The failed item keeps its usable old version.
        Assert.Equal(rig.Old.Pin, pipeline.State.Get("second").InstalledPin);
        // Per-item progress, and one at a time: neither item reports progress while the other is updating.
        Assert.Contains(seen, s => s.Id == "fixture" && s.Progress == 0.5);
        Assert.Contains(seen, s => s.Id == "second" && s.Progress == 0.5);
        Assert.DoesNotContain(seen, s => s.State == UpdateRowState.Updating && s.Other == UpdateRowState.Updating);
        rig.FailDownload = null;
        await updates.Retry("second");
        Assert.Equal(UpdateRowState.Updated, updates.Rows.Single(r => r.Id == "second").State);
        Assert.Equal(rig.Next.Pin, pipeline.State.Get("second").InstalledPin);
    }
}
