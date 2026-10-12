using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-13")]
public sealed class C3cV12Tests
{
    [Fact]
    public async Task Grant_removed_by_the_sent_action_preserves_its_verified_result()
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        h.Approver.Standings[("notepad.exe", PcCapability.Act)] = PcApproval.Always;
        using var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}");
        app.Root.Children[1].OnInvoke = (_, _) =>
        {
            app.Title = "saved";
            h.Desktop.Bump(app.Handle);
            h.Approver.Standings.Remove(("notepad.exe", PcCapability.Act));
        };
        var result = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        Assert.True(result.Ok, result.ToString());
        Assert.Contains("grant was then removed", result.Body, StringComparison.Ordinal);
        Assert.Equal(1, h.Desktop.Performs);
        Assert.Equal("saved", app.Title);
    }

    [Fact]
    public async Task Grant_removed_after_first_text_piece_reports_exact_partial_progress()
    {
        var rig = new C3cR4TextRig();
        rig.Harness.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        var send = rig.Borrow.Host.OnSend!;
        rig.Borrow.Host.OnSend = events =>
        {
            send(events);
            rig.Harness.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act));
        };
        using var engine = rig.Harness.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"no\"}");
        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"element\":2,\"text\":\"" + new string('a', 200) + "\"}"));
        Assert.Equal(PcErrorCode.ExpectFailed, result.Error);
        Assert.Contains("typed 64 of 200", result.Reason, StringComparison.Ordinal);
        Assert.Contains("grant was then removed", result.Body, StringComparison.Ordinal);
        Assert.Equal(new string('a', 64), rig.Field.Value);
        Assert.Single(rig.Borrow.Host.Sent);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Grant_removed_after_first_drag_array_preserves_outcome_and_attempts_release(bool partial)
    {
        var rig = new C3cR4TextRig();
        rig.Harness.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        if (partial) rig.Borrow.Host.AcceptedCount = 1;
        rig.Borrow.Host.OnSend = _ =>
        {
            rig.Borrow.Host.OnSend = null;
            rig.Harness.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act));
        };
        using var engine = rig.Harness.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"yes\"}");
        var action = PcEngineHarness.Call(engine, "pc_drag", "{\"window\":\"w:200\",\"path\":[[30,90],[50,90]],\"obs\":\"" + look.Window!.Obs + "\"}");
        await PcTestWait.Until(() => action.IsCompleted || rig.Borrow.Host.Sent.Count > 0);
        rig.Borrow.Clock.Advance(100);
        var result = await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(PcErrorCode.NotApproved, result.Error);
        Assert.Contains("grant was then removed", result.Body, StringComparison.Ordinal);
        Assert.Contains(rig.Borrow.Host.Sent.SelectMany(x => x.Events), e => (e.Flags & PcInputPlan.LEFTDOWN) != 0);
        Assert.Contains(rig.Borrow.Host.Sent.SelectMany(x => x.Events), e => (e.Flags & PcInputPlan.LEFTUP) != 0);
        var acceptedFirst = rig.Borrow.Host.Accepted[0].Events;
        Assert.Contains(acceptedFirst, e => (e.Flags & PcInputPlan.MOVE) != 0);
        if (partial) Assert.DoesNotContain(acceptedFirst, e => (e.Flags & PcInputPlan.LEFTDOWN) != 0);
        else Assert.Contains(acceptedFirst, e => (e.Flags & PcInputPlan.LEFTDOWN) != 0);
        Assert.False(rig.Borrow.Hold.Swallowing);
    }
    [Fact]
    public async Task Grant_removed_after_typing_preserves_that_result_without_sending_submit()
    {
        var rig = new C3cR4TextRig();
        rig.Harness.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        var send = rig.Borrow.Host.OnSend!;
        rig.Borrow.Host.OnSend = events => { send(events); rig.Harness.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act)); };
        using var engine = rig.Harness.Engine();
        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"hello\",\"submit\":true}"));
        Assert.True(result.Ok, result.Render());
        Assert.Equal("hello", rig.Field.Value);
        Assert.Contains("submit was not sent", result.Body, StringComparison.Ordinal);
        Assert.Single(rig.Borrow.Host.Sent);
    }

    [Fact]
    public async Task Grant_removed_while_the_next_piece_is_queued_reports_the_completed_piece()
    {
        var rig = new C3cR4TextRig();
        rig.Harness.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        var send = rig.Borrow.Host.OnSend!;
        rig.Borrow.Host.OnSend = events => { send(events); rig.Borrow.CardUp = true; };
        using var engine = rig.Harness.Engine();
        var action = PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"" + new string('a', 200) + "\"}");
        await PcTestWait.Until(() => rig.Field.Value!.Length == 64 && rig.Borrow.Lease.Waits.Count == 1 && !rig.Borrow.Hold.Swallowing && rig.Borrow.Clock.Pending > 0);
        rig.Harness.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act));
        rig.Borrow.CardUp = false;
        rig.Borrow.Clock.Advance(RealInputLease.PollMs);
        var result = await rig.Drive(action);
        Assert.Equal(PcErrorCode.ExpectFailed, result.Error);
        Assert.Contains("typed 64 of 200", result.Reason, StringComparison.Ordinal);
        Assert.Contains("grant was then removed", result.Body, StringComparison.Ordinal);
        Assert.Equal(new string('a', 64), rig.Field.Value);
        Assert.Single(rig.Borrow.Host.Sent);
    }

    [Fact]
    public async Task Partial_text_acceptance_applies_only_the_accepted_prefix()
    {
        var rig = new C3cR4TextRig();
        rig.Harness.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        rig.Borrow.Host.AcceptedCount = 2;
        var send = rig.Borrow.Host.OnSend!;
        rig.Borrow.Host.OnSend = events => { send(events); rig.Harness.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act)); };
        using var engine = rig.Harness.Engine();
        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:200\",\"target\":{\"name\":\"Text editor\"},\"text\":\"hello\"}"));
        Assert.Equal(PcErrorCode.ExpectFailed, result.Error);
        Assert.Contains("typed 1 of 5", result.Reason, StringComparison.Ordinal);
        Assert.Contains("grant was then removed", result.Body, StringComparison.Ordinal);
        Assert.Equal("h", rig.Field.Value);
    }


    [Fact]
    public async Task Partial_click_acceptance_still_releases_the_accepted_down()
    {
        var rig = new C3cR4TextRig();
        rig.Harness.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        rig.Borrow.Host.AcceptedCount = 2;
        rig.Borrow.Host.OnSend = _ =>
        {
            rig.Borrow.Host.OnSend = null;
            rig.Borrow.Host.AcceptedCount = null;
            rig.Harness.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act));
        };
        using var engine = rig.Harness.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"no\"}");
        var result = await rig.Drive(PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:200\",\"element\":3}"));
        Assert.NotEqual(PcErrorCode.NotApproved, result.Error);
        Assert.Contains("grant was then removed", result.Body, StringComparison.Ordinal);
        var actual = rig.Borrow.Host.Accepted.SelectMany(x => x.Events).ToList();
        Assert.Contains(actual, e => (e.Flags & PcInputPlan.LEFTDOWN) != 0);
        Assert.Single(actual, e => (e.Flags & PcInputPlan.LEFTUP) != 0);
        Assert.False(rig.Borrow.Hold.Swallowing);
    }

    [Fact]
    public async Task Rejected_final_drag_up_remains_owed_to_release()
    {
        var rig = new C3cR4TextRig();
        rig.Harness.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        var calls = 0;
        rig.Borrow.Host.OnSend = _ =>
        {
            if (++calls == 2) rig.Borrow.Host.AcceptedCount = 0;
            if (calls == 3)
            {
                rig.Borrow.Host.OnSend = null;
                rig.Borrow.Host.AcceptedCount = null;
                rig.Harness.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act));
            }
        };
        using var engine = rig.Harness.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:200\",\"screenshot\":\"yes\"}");
        var action = PcEngineHarness.Call(engine, "pc_drag", "{\"window\":\"w:200\",\"path\":[[30,90],[50,90]],\"obs\":\"" + look.Window!.Obs + "\"}");
        await PcTestWait.Until(() => action.IsCompleted || (rig.Borrow.Host.Accepted.Count >= 1 && rig.Borrow.Clock.Pending >= 2));
        rig.Borrow.Clock.Advance(100);
        await PcTestWait.Until(() => action.IsCompleted || (rig.Borrow.Host.Accepted.Count >= 2 && rig.Borrow.Clock.Pending >= 2));
        rig.Borrow.Clock.Advance(InputInjector.DragStepMs);
        var result = await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(PcErrorCode.NotApproved, result.Error);
        Assert.Contains("grant was then removed", result.Body, StringComparison.Ordinal);
        Assert.Empty(rig.Borrow.Host.Accepted[2].Events);
        var actual = rig.Borrow.Host.Accepted.SelectMany(x => x.Events).ToList();
        Assert.Contains(actual, e => (e.Flags & PcInputPlan.LEFTDOWN) != 0);
        Assert.Single(actual, e => (e.Flags & PcInputPlan.LEFTUP) != 0);
        Assert.False(rig.Borrow.Hold.Swallowing);
    }

}
