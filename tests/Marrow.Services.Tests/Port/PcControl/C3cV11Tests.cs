using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-12")]
public sealed class C3cV11Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R1_reads_no_extra_tree_without_a_wait_and_one_after_confirmation(bool waited)
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        var button = app.Root.Children[1];
        if (waited) button.Name = "Delete old.txt";
        using var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}");
        var baseline = h.Desktop.TreeReads;
        var readsAtSend = -1;
        button.OnInvoke = (_, _) => { readsAtSend = h.Desktop.TreeReads - baseline; app.Title = "saved"; h.Desktop.Bump(app.Handle); };
        if (waited) h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        if (waited)
        {
            await PcTestWait.Until(() => h.Confirmer.Asked.Count == 1);
            h.Desktop.Clock.NowMs += 3000;
            h.Confirmer.Hold!.SetResult(true);
        }
        Assert.True((await action.WaitAsync(TimeSpan.FromSeconds(5))).Ok);
        Assert.Equal(waited ? 1 : 0, readsAtSend);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R4_reads_no_extra_tree_without_a_wait_and_one_after_an_old_queue_wait(bool waited)
    {
        var rig = new C3cR4TextRig();
        using var engine = rig.Harness.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"no\"}");
        var baseline = rig.Harness.Desktop.TreeReads;
        var readsAtSend = -1;
        var sends = rig.Borrow.Host.OnSend!;
        rig.Borrow.Host.OnSend = events => { readsAtSend = rig.Harness.Desktop.TreeReads - baseline; sends(events); };
        if (waited) rig.Borrow.CardUp = true;
        var action = PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"element\":2,\"text\":\"hello\"}");
        if (waited)
        {
            await PcTestWait.Until(() => rig.Borrow.Lease.Waits.Count == 1);
            rig.Harness.Desktop.Clock.NowMs += 3000;
            rig.Borrow.CardUp = false;
            rig.Borrow.Clock.Advance(RealInputLease.PollMs);
        }
        Assert.True((await rig.Drive(action)).Ok);
        Assert.Equal(waited ? 1 : 0, readsAtSend);
    }

    [Fact]
    public async Task Immediate_R4_insert_uses_the_cheap_live_value_without_an_extra_tree()
    {
        var rig = new C3cR4TextRig();
        using var engine = rig.Harness.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"no\"}");
        var baseline = rig.Harness.Desktop.TreeReads;
        rig.Field.Value = "app value ";
        var readsAtSend = -1;
        var sends = rig.Borrow.Host.OnSend!;
        rig.Borrow.Host.OnSend = events => { readsAtSend = rig.Harness.Desktop.TreeReads - baseline; sends(events); };
        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"element\":2,\"text\":\"hello\",\"insert\":true}"));
        Assert.True(result.Ok, result.Render());
        Assert.Equal("app value hello", rig.Field.Value);
        Assert.Equal(0, readsAtSend);
    }

    [Fact]
    public async Task Immediate_R4_click_uses_the_cheap_live_bounds_without_an_extra_tree()
    {
        var rig = new C3cR4TextRig();
        using var engine = rig.Harness.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"no\"}");
        var baseline = rig.Harness.Desktop.TreeReads;
        var button = rig.App.Root.Children[1];
        button.Bounds = button.Bounds with { X = button.Bounds.X + 200 };
        var readsAtSend = -1;
        rig.Borrow.Host.OnSend = events =>
        {
            readsAtSend = rig.Harness.Desktop.TreeReads - baseline;
            if (events.Any(e => (e.Flags & PcInputPlan.LEFTDOWN) != 0)) { rig.App.Title = "saved"; rig.Harness.Desktop.Bump(rig.App.Handle); }
        };
        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:200\",\"element\":3}"));
        Assert.True(result.Ok, result.Render());
        var move = rig.Borrow.Host.Sent.SelectMany(s => s.Events).First(e => (e.Flags & PcInputPlan.MOVE) != 0);
        var point = CoordinateMapper.FromAbsolute(move.Dx, move.Dy, rig.Borrow.Host.Desktop());
        Assert.InRange(Math.Abs(point.X - (button.Bounds.X + button.Bounds.Width / 2)), 0, 1);
        Assert.InRange(Math.Abs(point.Y - (button.Bounds.Y + button.Bounds.Height / 2)), 0, 1);
        Assert.Equal(0, readsAtSend);
    }
}
