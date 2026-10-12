using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-18")]
public sealed class C3cV5Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_identity_changed_value_or_state_during_confirmation_is_not_acted_on(bool state)
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        var node = app.Root.Children[state ? 2 : 0];
        node.Name = "Delete option";
        if (!state) node.Actions |= PcActions.Invoke;
        using var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}");
        h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"target\":{\"name\":\"Delete option\"}}");
        await PcTestWait.Until(() => h.Confirmer.Asked.Count == 1);
        if (state) node.States |= PcStates.Checked;
        else node.Value = "user edit";
        h.Desktop.Bump(app.Handle);
        h.Confirmer.Hold.SetResult(true);
        Assert.Equal(PcErrorCode.ExpectFailed, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.Equal(0, h.Desktop.Performs);
        Assert.Equal(0, h.Desktop.Messages);
    }

    [Fact]
    public async Task Same_target_without_the_intended_live_way_is_refused_before_dispatch()
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        var node = app.Root.Children[1];
        node.Name = "Delete option";
        using var engine = h.Engine();
        h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"target\":{\"name\":\"Delete option\"}}");
        await PcTestWait.Until(() => h.Confirmer.Asked.Count == 1);
        node.Actions = PcActions.Toggle;
        h.Desktop.Bump(app.Handle);
        h.Confirmer.Hold.SetResult(true);
        var result = await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcErrorCode.StaleElement, result.Error);
        Assert.Contains("intended action", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, h.Desktop.Performs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_queued_first_text_piece_refuses_changed_value_or_recycled_identity(bool recycled)
    {
        var rig = new C3cR4TextRig();
        using var engine = rig.Harness.Engine();
        rig.Borrow.CardUp = true;
        var action = PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"hello\"}");
        await PcTestWait.Until(() => rig.Borrow.Lease.Waits.Count == 1);
        if (recycled) rig.Field.Name = "different field";
        else rig.Field.Value = "user edit";
        // No provider event: the short wait must still check the live element.
        rig.Borrow.CardUp = false;
        rig.Borrow.Clock.Advance(RealInputLease.PollMs);
        var result = await rig.Drive(action);
        Assert.Equal(recycled ? PcErrorCode.StaleElement : PcErrorCode.ExpectFailed, result.Error);
        Assert.Empty(rig.Borrow.Host.Sent);
        Assert.Empty(rig.Borrow.Hold.Log);
    }

    [Fact]
    public async Task A_queued_point_refuses_a_control_moved_away_inside_the_same_window()
    {
        var rig = new C3cR4TextRig();
        var button = rig.App.Root.Children[1];
        using var engine = rig.Harness.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"yes\"}");
        rig.Borrow.CardUp = true;
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:200\",\"point\":[30,20],\"obs\":\"" + look.Window!.Obs + "\"}");
        await PcTestWait.Until(() => rig.Borrow.Lease.Waits.Count == 1);
        button.Bounds = button.Bounds with { X = 700, Y = 700 };
        rig.Borrow.CardUp = false;
        rig.Borrow.Clock.Advance(RealInputLease.PollMs);
        Assert.Equal(PcErrorCode.StaleObs, (await rig.Drive(action)).Error);
        Assert.Empty(rig.Borrow.Host.Sent);
    }

    [Fact]
    public async Task Later_text_pieces_require_the_first_piece_runtime_id_to_keep_focus()
    {
        var rig = new C3cR4TextRig();
        var send = rig.Borrow.Host.OnSend!;
        rig.Borrow.Host.OnSend = events => { send(events); rig.App.FocusControl = rig.App.Root.Children[1]; rig.Harness.Desktop.Bump(rig.App.Handle); };
        using var engine = rig.Harness.Engine();
        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"" + new string('a', 200) + "\"}"));
        Assert.Equal(PcErrorCode.StaleElement, result.Error);
        Assert.Equal(new string('a', 64), rig.Field.Value);
        Assert.Single(rig.Borrow.Host.Sent);
    }
}
