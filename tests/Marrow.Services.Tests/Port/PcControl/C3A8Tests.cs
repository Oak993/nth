using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// ED-NEW-pcctl-cx-8: the trait names the owner decision, as a two-word ED id is not a well-formed trait id (ED-NEW-pcctl-35).
[Trait("Decision", "OD-0144")]
public sealed class C3A8Tests
{
    [Fact]
    public async Task A_recycled_focused_button_never_receives_the_confirmed_keyless_space()
    {
        var h = new PcEngineHarness();
        var app = h.Classic();
        var button = app.Root.Children[1];
        button.Name = "Delete old.txt";
        app.FocusControl = button;
        button.States |= PcStates.Focused;
        using var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2c\",\"screenshot\":\"no\"}");
        h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:2c\",\"keys\":\"space\"}");
        await PcTestWait.Until(() => h.Confirmer.Asked.Count == 1);
        button.Name = "Delete new.txt";
        h.Desktop.Bump(app.Handle);
        h.Desktop.Clock.NowMs += 3000;
        h.Confirmer.Hold.SetResult(true);
        Assert.Equal(PcErrorCode.StaleElement, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.DoesNotContain(h.Desktop.Log, line => line.StartsWith("post key", StringComparison.Ordinal));
        Assert.Equal(0, h.Desktop.Messages);
    }

    [Fact]
    public async Task A_point_target_that_moves_during_confirmation_is_not_clicked_at_its_old_position()
    {
        var h = new PcEngineHarness();
        var app = h.Classic();
        var button = app.Root.Children[1];
        button.Name = "Delete old.txt";
        button.Actions = PcActions.None;
        button.ClassName = "CustomButton";
        using var engine = h.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2c\",\"screenshot\":\"yes\"}");
        h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:2c\",\"point\":[30,90],\"obs\":\"" + look.Window!.Obs + "\"}");
        await PcTestWait.Until(() => h.Confirmer.Asked.Count == 1);
        var old = button.Bounds;
        button.Bounds = new PcRect(500, 500, old.Width, old.Height);
        app.Root.Add(new PcFakeNode("1.44.9", "button", "Delete new.txt") { Bounds = old, OnInvoke = (_, _) => app.Title = "wrong file deleted" });
        h.Desktop.Bump(app.Handle);
        h.Confirmer.Hold.SetResult(true);
        Assert.Equal(PcErrorCode.StaleObs, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.DoesNotContain(h.Desktop.Log, line => line.StartsWith("post_mouse", StringComparison.Ordinal));
        Assert.Equal("Settings", app.Title);
    }

    [Fact]
    public async Task A_recycled_target_while_R4_waits_for_the_card_never_receives_input()
    {
        var rig = new PcBorrowRig().ProveAll();
        var h = new PcEngineHarness { Settings = PcSettings.Default with { Enabled = true, RealInput = true } };
        var app = h.Notepad(PcBorrowRig.Target, 42, @"c:\windows\mspaint.exe");
        app.Root.Children[1].Name = "Delete old.txt";
        h.OverrideLease = rig.Lease;
        h.OverrideLadder = new PcActionLadder([new PcBorrowRung(() => h.Settings, rig.Lease)], rig.Capabilities);
        using var engine = h.Engine();
        rig.CardUp = true;
        var action = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:200\",\"target\":{\"name\":\"Delete old.txt\"}}");
        await PcTestWait.Until(() => rig.Lease.Waits.Count == 1);
        app.Root.Children[1].Name = "Delete new.txt";
        h.Desktop.Bump(app.Handle);
        rig.CardUp = false;
        rig.Clock.Advance(RealInputLease.PollMs);
        Assert.Equal(PcErrorCode.StaleElement, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.Empty(rig.Host.Sent);
        Assert.Empty(rig.Hold.Log);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_unique_identity_rematch_uses_the_new_runtime_id(bool shortcut)
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        var button = app.Root.Children[1];
        button.Name = "Delete old.txt";
        var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}");
        h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = shortcut
            ? PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"ctrl+s\"}")
            : PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        await PcTestWait.Until(() => h.Confirmer.Asked.Count == 1);
        app.Root.Children.Remove(button);
        app.Root.Add(new PcFakeNode("1.26.9", "button", "Delete old.txt")
            { Actions = PcActions.Invoke, Shortcut = "Ctrl+S", Bounds = button.Bounds, OnInvoke = (_, _) => app.Title = "deleted" });
        h.Desktop.Bump(0x1a);
        h.Desktop.Clock.NowMs += 3000;
        h.Confirmer.Hold.SetResult(true);
        var result = await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Ok, result.Render());
        Assert.Equal("deleted", app.Title);
        Assert.Equal(1, h.Desktop.Performs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_recycled_button_during_confirmation_never_receives_the_approved_delete(bool shortcut)
    {
        var h = new PcEngineHarness();
        var app = h.Notepad();
        var button = app.Root.Children[1];
        button.Name = "Delete old.txt";
        button.OnInvoke = (_, _) => app.Title = "deleted";
        var engine = h.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}");
        h.Confirmer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = shortcut
            ? PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"ctrl+s\"}")
            : PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        await PcTestWait.Until(() => h.Confirmer.Asked.Count == 1);
        button.Name = "Delete new.txt";
        h.UserTypes(0x1a);
        h.Desktop.Bump(0x1a);
        h.Desktop.Clock.NowMs += 3000;
        h.Confirmer.Hold.SetResult(true);
        var result = await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcErrorCode.StaleElement, result.Error);
        Assert.Equal(0, h.Desktop.Performs);
        Assert.Equal("Untitled - notepad", app.Title);
    }
}
