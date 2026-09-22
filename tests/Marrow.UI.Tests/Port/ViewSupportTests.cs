using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// R-VIEW rules that need no Avalonia window (IMPLEMENTING.md 3.7). Source: draft C (proto-C ObservationTests).
public sealed class ViewSupportTests
{
    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-071")]
    public void Tracked_coalesces_a_burst_into_one_apply_and_carries_the_animation()
    {
        using var runtime = new TestRuntime();
        var applied = new List<(int Value, Animation? Animation)>();
        Tracked<int>? tracked = null;
        runtime.OnMain(() =>
        {
            var cell = new CellSample<int>(0);
            tracked = new Tracked<int>(() => cell.Value, v => applied.Add((v, Transaction.Current)));
            Assert.Single(applied);
            Transaction.WithAnimation(new EaseOutSample(0.15), () =>
            {
                cell.Value = 1;
                cell.Value = 2;
            });
            cell.Value = 3;
            Assert.Single(applied); // nothing re-renders inside the mutation turn
        });
        Assert.Equal(2, applied.Count);
        Assert.Equal(3, applied[1].Value);
        Assert.Equal(new EaseOutSample(0.15), applied[1].Animation);
        tracked!.Dispose();
    }

    [Fact]
    [Trait("Rule", "R-VIEW-5")]
    public void OnChange_reports_old_and_new_after_the_turn_and_ignores_a_change_that_reverts()
    {
        using var runtime = new TestRuntime();
        var calls = new List<(string Old, string New)>();
        CellSample<string>? cell = null;
        runtime.OnMain(() =>
        {
            cell = new CellSample<string>("a");
            _ = new OnChange<string>(() => cell.Value, (o, n) => calls.Add((o, n)), initial: true);
            Assert.Equal([("a", "a")], calls);
            cell.Value = "b";
            cell.Value = "a";
        });
        Assert.Single(calls);
        runtime.OnMain(() =>
        {
            cell!.Value = "c";
            Assert.Single(calls);
        });
        Assert.Equal(("a", "c"), calls[1]);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-3")]
    public void ViewTask_starts_after_appear_cancels_on_disappear_and_restarts_on_a_new_id()
    {
        using var runtime = new TestRuntime();
        var log = new List<string>();
        var task = new ViewTask<string>("a.png", async id =>
        {
            log.Add("start " + id);
            await SwiftTask.TrySleep(TimeSpan.FromHours(1));
            log.Add((SwiftTask.IsCancelled ? "cancelled " : "done ") + id);
        });
        runtime.OnMain(() =>
        {
            task.Appear();
            Assert.Empty(log); // a later main turn, never inside appear
        });
        runtime.OnMain(() => task.UpdateId("a.png")); // an equal id: nothing restarts
        runtime.OnMain(() => task.UpdateId("b.png"));
        runtime.OnMain(task.Disappear);
        Assert.Equal(["start a.png", "cancelled a.png", "start b.png", "cancelled b.png"], log);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-7")]
    public void A_binding_reads_and_writes_through_the_source_property()
    {
        var source = new CellSample<bool>(true);
        var binding = new Binding<bool>(() => source.Value, v => source.Value = v); // $model.flag
        var fired = 0;
        ObservationTracking.WithTracking(() => binding.Value, () => fired++); // a child view reading the binding
        binding.Value = false;
        Assert.Equal(1, fired); // tracked on the source property
        Assert.False(source.Value);
        Assert.False(Binding<bool>.Constant(false).Value);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    public void A_computed_property_tells_its_binding_when_another_objects_cell_changes()
    {
        var model = new SelectionSample();
        model.Put("a", "Alpha");
        model.Put("b", "Beta");
        model.Selected = "a";
        using var log = new ChangeLog(model);
        Assert.Equal("Alpha", model.SelectedTitle); // a read arms tracking, as a binding's does
        model.Put("b", "Beta 2");
        Assert.Empty(log.Names);
        model.Put("a", "Alpha 2");
        Assert.Equal(["SelectedTitle"], log.Names);
    }
}
