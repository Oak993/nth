using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-2")]
public sealed class C3A2Tests
{
    [Fact]
    public async Task Removing_a_grant_during_show_confirmation_does_not_show_the_window()
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        app.Minimized = true;
        h.Approver.Standings[("notepad.exe", PcCapability.Act)] = PcApproval.Always;
        h.Confirmer.ShowHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = h.Engine();
        var showing = PcEngineHarness.Call(engine, "pc_focus_window", "{\"window\":\"w:1a\"}");
        await PcTestWait.Until(() => h.Confirmer.Shown.Count == 1);
        h.Approver.Standings.Remove(("notepad.exe", PcCapability.Act));
        h.Confirmer.ShowHold.SetResult(PcShowAnswer.Show);
        Assert.Equal(PcErrorCode.NotApproved, (await showing.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.True(app.Minimized);
        Assert.DoesNotContain(h.Desktop.Log, line => line.StartsWith("show ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Removing_a_grant_while_R4_waits_for_the_card_prevents_input()
    {
        var rig = new PcBorrowRig().ProveAll();
        var h = new PcEngineHarness { Settings = PcSettings.Default with { Enabled = true, RealInput = true } };
        h.Notepad(PcBorrowRig.Target, 42, @"c:\windows\mspaint.exe");
        h.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        var ladder = new PcActionLadder([new PcBorrowRung(() => h.Settings, rig.Lease)], rig.Capabilities);
        h.OverrideLease = rig.Lease;
        h.OverrideLadder = ladder;
        using var engine = h.Engine();
        rig.CardUp = true;
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:200\",\"target\":{\"name\":\"Save\"}}");
        await PcTestWait.Until(() => rig.Lease.Waits.Count == 1);
        h.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act));
        rig.CardUp = false;
        rig.Clock.Advance(RealInputLease.PollMs);
        Assert.Equal(PcErrorCode.NotApproved, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.Empty(rig.Host.Sent);
        Assert.Empty(rig.Hold.Log);
    }

    [Fact]
    public async Task Removing_a_grant_while_text_is_paused_prevents_the_remaining_chunks()
    {
        var h = new PcEngineHarness();
        var app = h.Classic();
        var field = new PcFakeNode("1.44.5", "edit", "Notes")
        {
            Actions = PcActions.Focus, ClassName = "Scintilla", Framework = "Win32",
            NativeWindow = 0x32c, Value = "", Bounds = new PcRect(20, 160, 300, 24),
        };
        app.Root.Add(field);
        h.Approver.Standings[("legacy.exe", PcCapability.Act)] = PcApproval.Always;
        using var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2c\",\"screenshot\":\"no\"}");
        h.Desktop.OnMessage = (live, what) =>
        {
            if (what.StartsWith("wm_char", StringComparison.Ordinal) && h.Pause.Epoch(live.Info.App.Key) == 0)
                h.Pause.Pause(PcScope.App(live.Info.App.Key), PcPauseSource.Pill);
        };
        var paused = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:2c\",\"target\":{\"name\":\"Notes\"},\"text\":\"" + new string('a', 200) + "\"}");
        Assert.Equal(PcWaitReason.Paused, paused.Reason);
        Assert.Equal(64, field.Value!.Length);
        h.Approver.Standings.Remove(("legacy.exe", PcCapability.Act));
        h.Pause.Resume(PcScope.All, PcPauseSource.Pill);
        var done = await PcEngineHarness.Call(engine, "pc_wait", "{\"window\":\"w:2c\",\"until\":\"step\",\"token\":\"" + paused.Token + "\"}");
        Assert.Equal(PcErrorCode.ExpectFailed, done.Error);
        Assert.Contains("typed 64 of 200", done.Reason, StringComparison.Ordinal);
        Assert.Contains("grant was then removed", done.Body, StringComparison.Ordinal);
        Assert.Equal(64, field.Value.Length);
    }

    [Fact]
    public async Task Removing_a_see_grant_refuses_the_next_observation()
    {
        var h = new PcEngineHarness();
        h.Notepad();
        h.Approver.Standings[("notepad.exe", PcCapability.See)] = PcApproval.Always;
        var engine = h.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}")).Ok);
        h.Approver.Standings.Remove(("notepad.exe", PcCapability.See));
        h.Approver.Answer = PcApproval.Denied;
        Assert.Equal(PcErrorCode.NotApproved,
            (await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}")).Error);
    }

    [Fact]
    public async Task Removing_a_grant_during_confirmation_prevents_the_approved_action()
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        app.Root.Children[1].Name = "Delete old.txt";
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "deleted";
        h.Approver.Standings[("notepad.exe", PcCapability.Act)] = PcApproval.Always;
        var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}");
        h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        await PcTestWait.Until(() => h.Confirmer.Asked.Count == 1);
        h.Approver.Standings.Remove(("notepad.exe", PcCapability.Act));
        h.Confirmer.Hold.SetResult(true);
        Assert.Equal(PcErrorCode.NotApproved, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.Equal(0, h.Desktop.Performs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_an_always_grant_makes_the_same_engine_ask_again(bool endTurn)
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        h.Approver.Standings[("notepad.exe", PcCapability.Act)] = PcApproval.Always;
        var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}");
        Assert.True((await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"old\"}")).Ok);
        if (endTurn) engine.EndTurn();
        h.Approver.Standings.Remove(("notepad.exe", PcCapability.Act));
        h.Approver.Answer = PcApproval.Denied;
        var result = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"new\"}");
        Assert.Equal(PcErrorCode.NotApproved, result.Error);
        Assert.Equal("old", app.Root.Children[0].Value);
        Assert.Single(h.Approver.Asked, a => a.Capability == PcCapability.Act);
    }
}
