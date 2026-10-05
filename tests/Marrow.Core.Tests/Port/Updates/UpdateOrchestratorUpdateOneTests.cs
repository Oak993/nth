using Marrow.Core.Port.Updates;

namespace Marrow.Core.Tests.Port.Updates;

// Unit UE-4 (BUILD-PLAN.md 2.4 UE-4: the Updates page has "per-row Update and Retry"). UE-2's orchestrator could run one item alone only to Retry a failed one, so the row's own
// Update needs a way to run one due item alone (ED-NEW-f-ue-4). Same fakes as the UE-2 tests: no CLI, no PATH, no network.
[Collection("UpdateOrchestrator")]
public sealed class UpdateOrchestratorUpdateOneTests
{
    private static FakeItem Item(string id, UpdateItemKind kind = UpdateItemKind.ProviderCli, string? installed = "1.0", string? latest = "2.0")
    {
        FakeItem.Reset();
        return new FakeItem(id, kind, installed, latest);
    }

    private static UpdateRow Row(UpdateOrchestrator o, string id) => o.Rows.Single(r => r.Id == id);

    [Fact]
    [Trait("Feature", "UE-4")]
    [Trait("Rule", "R-TEST-4")]
    public async Task UpdateOne_runs_only_that_available_item_and_leaves_the_others_as_they_were()
    {
        var a = Item("a");
        var b = Item("b");
        var o = new UpdateOrchestrator([a, b]);
        await o.CheckNow();
        await o.UpdateOne("a");
        Assert.Equal(UpdateRowState.Updated, Row(o, "a").State);
        Assert.Equal(UpdateRowState.UpdateAvailable, Row(o, "b").State);
        Assert.Equal(1, a.Updates);
        Assert.Equal(0, b.Updates);
        Assert.False(o.IsBusy);
    }

    [Fact]
    [Trait("Feature", "UE-4")]
    [Trait("Rule", "R-TEST-4")]
    public async Task UpdateOne_runs_an_item_whose_version_is_only_known_when_it_runs_and_one_that_failed_before()
    {
        var unknown = Item("u", latest: null);
        var attempts = 0;
        FakeItem.Reset();
        var flaky = new FakeItem("f", UpdateItemKind.ProviderCli, "1.0", "2.0", _ => Task.FromResult(++attempts == 1 ? new UpdateOutcome(false, null, false, false, "no") : new UpdateOutcome(true, "2.0", true, false, null)));
        var o = new UpdateOrchestrator([unknown, flaky]);
        await o.CheckNow();
        Assert.Equal(UpdateRowState.ChecksWhenUpdated, Row(o, "u").State);
        await o.UpdateOne("u");
        Assert.Equal(1, unknown.Updates);
        await o.UpdateOne("f");
        Assert.Equal(UpdateRowState.Failed, Row(o, "f").State);
        await o.UpdateOne("f");
        Assert.Equal(UpdateRowState.Updated, Row(o, "f").State);
    }

    [Fact]
    [Trait("Feature", "UE-4")]
    [Trait("Rule", "R-TEST-4")]
    public async Task UpdateOne_refuses_a_current_or_missing_item_an_unknown_id_and_a_run_that_is_going()
    {
        var current = Item("c", installed: "2.0", latest: "2.0");
        var missing = Item("m", installed: null);
        var gate = new TaskCompletionSource();
        FakeItem.Reset();
        var slow = new FakeItem("s", UpdateItemKind.ProviderCli, "1.0", "2.0", async _ => { await gate.Task; return new UpdateOutcome(true, "2.0", true, false, null); });
        var o = new UpdateOrchestrator([current, missing, slow]);
        await o.CheckNow();
        await Assert.ThrowsAsync<InvalidOperationException>(() => o.UpdateOne("c"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => o.UpdateOne("m"));
        await Assert.ThrowsAsync<ArgumentException>(() => o.UpdateOne("zz"));
        Assert.False(o.IsBusy);
        var first = o.UpdateOne("s");
        Assert.True(o.IsBusy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => o.UpdateOne("s"));
        gate.SetResult();
        await first;
        Assert.Equal(0, current.Updates);
        Assert.Equal(0, missing.Updates);
    }
}
