using Marrow.Core.Port.Updates;

namespace Marrow.Core.Tests.Port.Updates;

// Unit UE-2 (BUILD-PLAN.md 2.4 UE-2; OD-0100 with its pick: places A and D, words U1, "items run one at a time, Marrow last"; ONDEMAND-PLAN.md 3.7: skills and tools "one at a time,
// after the provider CLIs and before Marrow itself"). The orchestrator is pure: items come through an interface, so no CLI runs and no network is reached.
[Collection("UpdateOrchestrator")]
public sealed class UpdateOrchestratorTests
{
    private static FakeItem Item(string id, UpdateItemKind kind = UpdateItemKind.ProviderCli, string? installed = "1.0", string? latest = "2.0", Func<IProgress<double?>, Task<UpdateOutcome>>? update = null)
    {
        FakeItem.Reset();
        return new FakeItem(id, kind, installed, latest, update);
    }

    private static UpdateRow Row(UpdateOrchestrator o, string id) => o.Rows.Single(r => r.Id == id);

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Rows_are_listed_in_run_order_cli_dev_tool_skills_then_Marrow_whatever_order_they_were_given_in()
    {
        var o = new UpdateOrchestrator([Item("m", UpdateItemKind.Marrow), Item("s", UpdateItemKind.SkillsAndTools), Item("d", UpdateItemKind.DevTool), Item("b"), Item("a")]);
        Assert.Equal(["b", "a", "d", "s", "m"], o.Rows.Select(r => r.Id));
        Assert.All(o.Rows, r => Assert.Equal(UpdateRowState.Unchecked, r.State));
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_duplicate_id_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new UpdateOrchestrator([Item("a"), Item("a")]));
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Check_now_sets_each_row_from_the_installed_and_the_latest_version()
    {
        var gone = Item("none", installed: null);
        var unknown = Item("nolist", latest: null);
        var same = Item("same", installed: "2.0", latest: "2.0");
        var offline = Item("off");
        offline.LatestThrows = true;
        var broken = Item("broken");
        broken.InstalledThrows = true;
        var o = new UpdateOrchestrator([Item("new"), same, unknown, gone, offline, broken]);
        await o.CheckNow();
        Assert.Equal((UpdateRowState.UpdateAvailable, "1.0", "2.0"), (Row(o, "new").State, Row(o, "new").Installed, Row(o, "new").Latest));
        Assert.Equal(UpdateRowState.UpToDate, Row(o, "same").State);
        Assert.Equal(UpdateRowState.ChecksWhenUpdated, Row(o, "nolist").State);
        Assert.Equal(UpdateRowState.NotInstalled, Row(o, "none").State);
        Assert.Equal(UpdateRowState.ChecksWhenUpdated, Row(o, "off").State);
        Assert.Equal("offline", Row(o, "off").Message);
        Assert.Equal(UpdateRowState.Failed, Row(o, "broken").State);
        Assert.Equal("no version", Row(o, "broken").Message);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Update_everything_runs_the_ones_that_need_it_one_at_a_time_with_Marrow_last()
    {
        var order = new[] { Item("m", UpdateItemKind.Marrow), Item("s", UpdateItemKind.SkillsAndTools), Item("up", installed: "2.0"), Item("a"), Item("unk", latest: null), Item("gone", installed: null) };
        FakeItem.Reset();
        var o = new UpdateOrchestrator(order);
        await o.UpdateEverything();
        Assert.Equal(["start a", "end a", "start unk", "end unk", "start s", "end s", "start m", "end m"], FakeItem.Log);
        Assert.Equal(1, FakeItem.MaxRunning);
        Assert.Equal(0, order[2].Updates);
        Assert.Equal(0, order[5].Updates);
        Assert.False(o.IsBusy);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_row_that_was_never_checked_is_checked_first_and_an_up_to_date_row_is_not_run()
    {
        var current = Item("cur", installed: "2.0");
        var o = new UpdateOrchestrator([current]);
        await o.UpdateEverything();
        Assert.Equal(0, current.Updates);
        Assert.Equal(UpdateRowState.UpToDate, Row(o, "cur").State);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Results_a_changed_version_is_updated_an_unchanged_one_is_up_to_date_and_a_failure_keeps_its_message_without_stopping_the_rest()
    {
        var changed = Item("ch");
        var unchanged = Item("un", update: _ => Task.FromResult(new UpdateOutcome(true, "1.0", false, false, null)));
        var failing = Item("fail", update: _ => Task.FromResult(new UpdateOutcome(false, null, false, false, "updater exited 1")));
        var throwing = Item("throw", update: _ => throw new InvalidOperationException("boom"));
        var marrow = Item("m", UpdateItemKind.Marrow, update: _ => Task.FromResult(new UpdateOutcome(true, "9.0", true, true, null)));
        var o = new UpdateOrchestrator([changed, unchanged, failing, throwing, marrow]);
        await o.UpdateEverything();
        Assert.Equal((UpdateRowState.Updated, "2.0"), (Row(o, "ch").State, Row(o, "ch").Installed));
        Assert.Equal(UpdateRowState.UpToDate, Row(o, "un").State);
        Assert.Equal((UpdateRowState.Failed, "updater exited 1"), (Row(o, "fail").State, Row(o, "fail").Message));
        Assert.Equal((UpdateRowState.Failed, "boom"), (Row(o, "throw").State, Row(o, "throw").Message));
        Assert.Equal(UpdateRowState.Updated, Row(o, "m").State);
        Assert.True(o.RestartRequired);
        Assert.True(Row(o, "m").RestartRequired);
        Assert.False(o.IsEverythingCurrent);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Progress_is_indeterminate_until_an_item_reports_a_fraction_and_is_cleared_when_it_ends()
    {
        var seen = new List<double?>();
        UpdateOrchestrator? o = null;
        var reporting = Item("r", update: p => { seen.Add(Row(o!, "r").Progress); p.Report(0.5); seen.Add(Row(o!, "r").Progress); return Task.FromResult(new UpdateOutcome(true, "2.0", true, false, null)); });
        o = new UpdateOrchestrator([reporting]);
        await o.UpdateEverything();
        Assert.Equal([null, 0.5], seen);
        Assert.Null(Row(o, "r").Progress);
        Assert.Equal(UpdateRowState.Updated, Row(o, "r").State);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Rows_pass_through_queued_and_updating_and_every_change_is_announced()
    {
        var states = new List<string>();
        UpdateOrchestrator? o = null;
        var a = Item("a", update: _ => { states.Add("a:" + Row(o!, "a").State + "/b:" + Row(o!, "b").State); return Task.FromResult(new UpdateOutcome(true, "2.0", true, false, null)); });
        o = new UpdateOrchestrator([a, Item("b")]);
        var changes = 0;
        o.Changed += () => changes++;
        await o.UpdateEverything();
        Assert.Equal(["a:Updating/b:Queued"], states);
        Assert.True(changes >= 6);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Retry_runs_only_the_failed_item_and_only_when_it_failed()
    {
        var attempts = 0;
        var flaky = Item("f", update: _ => Task.FromResult(++attempts == 1 ? new UpdateOutcome(false, null, false, false, "no") : new UpdateOutcome(true, "2.0", true, false, null)));
        var other = Item("o");
        var o = new UpdateOrchestrator([flaky, other]);
        await o.UpdateEverything();
        Assert.Equal(UpdateRowState.Failed, Row(o, "f").State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => o.Retry("o"));
        await Assert.ThrowsAsync<ArgumentException>(() => o.Retry("zz"));
        await o.Retry("f");
        Assert.Equal(UpdateRowState.Updated, Row(o, "f").State);
        Assert.Equal(1, other.Updates);
        Assert.Equal(2, attempts);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_second_run_while_one_is_going_is_refused_not_queued()
    {
        var gate = new TaskCompletionSource();
        var o = new UpdateOrchestrator([Item("a", update: async _ => { await gate.Task; return new UpdateOutcome(true, "2.0", true, false, null); })]);
        var first = o.UpdateEverything();
        Assert.True(o.IsBusy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => o.UpdateEverything());
        await Assert.ThrowsAsync<InvalidOperationException>(() => o.CheckNow());
        await Assert.ThrowsAsync<InvalidOperationException>(() => o.Retry("a"));
        gate.SetResult();
        await first;
        Assert.False(o.IsBusy);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Cancelling_puts_the_running_and_the_waiting_rows_back_and_ends_the_run()
    {
        using var cts = new CancellationTokenSource();
        var first = Item("a", update: _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        var second = Item("b");
        var o = new UpdateOrchestrator([first, second]);
        await o.CheckNow();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => o.UpdateEverything(cts.Token));
        Assert.Equal(UpdateRowState.UpdateAvailable, Row(o, "a").State);
        Assert.Equal(UpdateRowState.UpdateAvailable, Row(o, "b").State);
        Assert.Equal(0, second.Updates);
        Assert.False(o.IsBusy);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Everything_is_current_only_when_every_installed_row_is_up_to_date()
    {
        var o = new UpdateOrchestrator([Item("a", installed: "2.0"), Item("g", installed: null)]);
        Assert.False(o.IsEverythingCurrent);
        await o.CheckNow();
        Assert.True(o.IsEverythingCurrent);
        var p = new UpdateOrchestrator([Item("a", installed: "2.0"), Item("u", latest: null)]);
        await p.CheckNow();
        Assert.False(p.IsEverythingCurrent);
    }
}
