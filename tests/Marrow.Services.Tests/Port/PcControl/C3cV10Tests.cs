using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-11")]
public sealed class C3cV10Tests
{
    [Fact]
    public async Task A_keyboard_borrow_follows_the_same_field_after_its_bounds_move_in_the_queue()
    {
        var rig = new C3cR4TextRig();
        using var engine = rig.Harness.Engine();
        rig.Borrow.CardUp = true;
        var action = PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"hello\"}");
        await PcTestWait.Until(() => rig.Borrow.Lease.Waits.Count == 1);
        rig.Field.Bounds = rig.Field.Bounds with { Y = rig.Field.Bounds.Y + 30 };
        rig.Harness.Desktop.Bump(rig.App.Handle);
        rig.Borrow.CardUp = false;
        rig.Borrow.Clock.Advance(RealInputLease.PollMs);
        var result = await rig.Drive(action);
        Assert.True(result.Ok, result.Render());
        Assert.Equal("hello", rig.Field.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_point_keeps_its_place_inside_a_window_that_moves_during_a_wait(bool queue, bool canvas)
    {
        var rig = new PcBorrowRig().ProveAll();
        var h = new PcEngineHarness { Settings = PcSettings.Default with { Enabled = true, RealInput = true, ConfirmGuardedActions = !queue } };
        var app = h.Classic(PcBorrowRig.Target, 42, @"c:\windows\mspaint.exe");
        var frame = app.Frame;
        app.Frame = app.Frame with { X = 0, Y = 0 };
        foreach (var node in app.Root.All()) node.Bounds = node.Bounds with { X = node.Bounds.X - frame.X, Y = node.Bounds.Y - frame.Y };
        var button = app.Root.Children[1];
        button.Name = "Delete old.txt";
        if (canvas) app.Root.Children.Clear();
        h.OverrideLease = rig.Lease;
        h.OverrideLadder = new PcActionLadder([new PcBorrowRung(() => h.Settings, rig.Lease, h.Desktop)], rig.Capabilities);
        using var engine = h.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"yes\"}");
        if (queue) rig.CardUp = true;
        else h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:200\",\"point\":[30,90],\"obs\":\"" + look.Window!.Obs + "\"}");
        await PcTestWait.Until(() => queue ? rig.Lease.Waits.Count == 1 : h.Confirmer.Asked.Count == 1);
        app.Frame = app.Frame with { X = app.Frame.X + 100, Y = app.Frame.Y + 50 };
        foreach (var node in app.Root.All()) node.Bounds = node.Bounds with { X = node.Bounds.X + 100, Y = node.Bounds.Y + 50 };
        h.Desktop.Bump(app.Handle);
        rig.Host.OnSend = events =>
        {
            if (events.Any(e => (e.Flags & PcInputPlan.LEFTDOWN) != 0)) { app.Title = "deleted"; h.Desktop.Bump(app.Handle); }
        };
        if (queue) { rig.CardUp = false; rig.Clock.Advance(RealInputLease.PollMs); }
        else h.Confirmer.Hold!.SetResult(true);
        var result = await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Ok, result.Render());
        var move = rig.Host.Sent.SelectMany(s => s.Events).First(e => (e.Flags & PcInputPlan.MOVE) != 0);
        var point = CoordinateMapper.FromAbsolute(move.Dx, move.Dy, rig.Host.Desktop());
        Assert.InRange(Math.Abs(point.X - 130), 0, 1);
        Assert.InRange(Math.Abs(point.Y - 140), 0, 1);
    }

    [Fact]
    public async Task A_first_piece_rematched_to_a_new_runtime_id_keeps_that_focus_for_later_pieces()
    {
        var rig = new C3cR4TextRig();
        using var engine = rig.Harness.Engine();
        rig.Borrow.CardUp = true;
        var text = new string('a', 200);
        var action = PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"" + text + "\"}");
        await PcTestWait.Until(() => rig.Borrow.Lease.Waits.Count == 1);
        rig.Field.RuntimeId = "replacement.field";
        rig.Harness.Desktop.Bump(rig.App.Handle);
        rig.Borrow.CardUp = false;
        rig.Borrow.Clock.Advance(RealInputLease.PollMs);
        var result = await rig.Drive(action);
        Assert.True(result.Ok, result.Render());
        Assert.Equal(text, rig.Field.Value);
    }
}
