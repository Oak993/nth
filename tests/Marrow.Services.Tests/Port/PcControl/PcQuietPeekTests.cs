// PORT-ONLY: Tactus, the quiet peek (ED-NEW-pcctl-46; DESIGN.md 2.10 "Windows behind or minimized", critique C18). A step
// that needs a minimized window on screen asks first on the pill ("Show notepad for a moment?" with why): a screenshot, a
// click or a scroll at a point, a drag, the real mouse or keyboard. On Show the window is shown for that step only and is
// minimized again when the step ends, however it ends (done, refused, failed); the look taken then keeps the shown frame,
// so a point from it lands where it was seen. On Not now nothing is shown: the look is the tree only, an action is
// declined. Work from the tree (patterns, messages) never asks. A grant removed while the card is up shows nothing, and a
// window Windows does not show is reported. The fake desktop and the fake pill.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-46")]
public sealed class PcQuietPeekTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";
    private const string Shot = "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}";

    private static async Task<(PcEngineHarness Harness, PcFakeApp App, PcControlEngine Engine)> Ready()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        return (harness, app, engine);
    }

    private static List<string> Peeks(PcEngineHarness harness) =>
        [.. harness.Desktop.Log.Where(l => l.StartsWith("peek ", StringComparison.Ordinal) || l.StartsWith("unpeek ", StringComparison.Ordinal))];

    [Fact]
    public async Task A_minimized_window_s_screenshot_asks_first_and_is_taken_inside_a_peek_that_ends_with_the_look()
    {
        var (harness, app, engine) = await Ready();
        app.Minimized = true;
        var shot = await PcEngineHarness.Call(engine, "pc_observe", Shot);
        Assert.True(shot.Ok, shot.Render());
        Assert.NotNull(shot.Image);
        Assert.Equal(1, harness.CaptureSource.Calls);
        Assert.Equal(["notepad Untitled - notepad"], harness.Confirmer.Shown);
        Assert.Equal([PcControlEngine.ScreenshotReason], harness.Confirmer.ShownWhy);
        Assert.Contains("minimized again", shot.Body, StringComparison.Ordinal);
        Assert.Equal(["peek w:1a", "unpeek w:1a"], Peeks(harness));
        Assert.True(app.Minimized);
        // the user's window kept the foreground
        Assert.Equal((nint)0x77, harness.Desktop.ForegroundWindow);
        Assert.DoesNotContain(harness.Desktop.Log, l => l.StartsWith("activate", StringComparison.Ordinal));

        // a point from that look lands where it was seen: asked again, shown again, clicked, minimized again
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"point\":[150,20],\"obs\":\"" + shot.Window!.Obs + "\"}");
        Assert.True(click.Ok, click.Render());
        Assert.True(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        Assert.Equal("a click at a point needs it on screen", harness.Confirmer.ShownWhy[^1]);
        Assert.Equal(["peek w:1a", "unpeek w:1a", "peek w:1a", "unpeek w:1a"], Peeks(harness));
        Assert.True(app.Minimized);
    }

    [Fact]
    public async Task Not_now_leaves_the_window_minimized_the_look_tree_only_and_a_point_declined()
    {
        var (harness, app, engine) = await Ready();
        var shot = await PcEngineHarness.Call(engine, "pc_observe", Shot);
        app.Minimized = true;
        harness.Confirmer.ShowAnswer = PcShowAnswer.NotNow;

        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\",\"full\":true}");
        Assert.True(look.Ok, look.Render());
        Assert.Null(look.Image);
        Assert.Contains(PcControlEngine.NotShown, look.Body, StringComparison.Ordinal);
        Assert.Contains("[2]", look.Body, StringComparison.Ordinal);
        var pixelsOnly = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\",\"tree\":false}");
        Assert.Equal(PcErrorCode.DeclinedByUser, pixelsOnly.Error);

        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"point\":[150,20],\"obs\":\"" + shot.Window!.Obs + "\"}");
        Assert.Equal(PcErrorCode.DeclinedByUser, click.Error);
        Assert.False(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        var scroll = await PcEngineHarness.Call(engine, "pc_scroll", "{\"window\":\"w:1a\",\"point\":[150,20],\"obs\":\"" + shot.Window!.Obs + "\",\"direction\":\"down\"}");
        Assert.Equal(PcErrorCode.DeclinedByUser, scroll.Error);
        var drag = await PcEngineHarness.Call(engine, "pc_drag", "{\"window\":\"w:1a\",\"path\":[[150,20],[170,20]],\"obs\":\"" + shot.Window!.Obs + "\"}");
        Assert.Equal(PcErrorCode.DeclinedByUser, drag.Error);
        Assert.Equal(
            [PcControlEngine.ScreenshotReason, PcControlEngine.ScreenshotReason, "a click at a point needs it on screen", "a scroll at a point needs it on screen", "a drag needs it on screen"],
            harness.Confirmer.ShownWhy);
        Assert.Empty(Peeks(harness));
        Assert.Equal(1, harness.CaptureSource.Calls);
        Assert.True(app.Minimized);
    }

    [Fact]
    public async Task Work_from_the_tree_never_asks_and_a_step_that_fails_inside_the_peek_still_ends_it()
    {
        var (harness, app, engine) = await Ready();
        var shot = await PcEngineHarness.Call(engine, "pc_observe", Shot);
        app.Minimized = true;

        Assert.True((await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"from the background\"}")).Ok);
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")).Ok);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        Assert.Empty(harness.Confirmer.Shown);
        Assert.Empty(Peeks(harness));

        // a drag needs the real mouse, which is off: refused inside the peek, and the window is minimized again
        var drag = await PcEngineHarness.Call(engine, "pc_drag", "{\"window\":\"w:1a\",\"path\":[[150,20],[170,20]],\"obs\":\"" + shot.Window!.Obs + "\"}");
        Assert.False(drag.Ok);
        Assert.NotEqual(PcErrorCode.DeclinedByUser, drag.Error);
        Assert.Equal(["peek w:1a", "unpeek w:1a"], Peeks(harness));
        Assert.True(app.Minimized);
    }

    [Fact]
    public async Task A_grant_removed_while_the_card_is_up_shows_nothing_and_a_window_Windows_does_not_show_is_reported()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        harness.Approver.Standings[("notepad.exe", PcCapability.See)] = PcApproval.Always;
        using var engine = harness.Engine();
        app.Minimized = true;
        harness.Confirmer.ShowHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var look = PcEngineHarness.Call(engine, "pc_observe", Shot);
        await PcTestWait.Until(() => harness.Confirmer.Shown.Count == 1);
        harness.Approver.Standings.Remove(("notepad.exe", PcCapability.See));
        harness.Confirmer.ShowHold.SetResult(PcShowAnswer.Show);
        var removed = await look.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(removed.Image);
        Assert.Empty(Peeks(harness));
        Assert.True(app.Minimized);

        harness.Confirmer.ShowHold = null;
        harness.Desktop.Peeks = false;
        var unshown = await PcEngineHarness.Call(engine, "pc_observe", Shot);
        Assert.Null(unshown.Image);
        Assert.Contains("Windows did not show it", unshown.Render(), StringComparison.Ordinal);
        Assert.Equal(0, harness.CaptureSource.Calls);
        Assert.True(app.Minimized);
    }

    [Fact]
    public async Task The_real_mouse_or_keyboard_in_a_minimized_window_asks_first_and_the_borrow_runs_inside_the_peek()
    {
        var rig = new PcBorrowRig().ProveAll();
        var h = new PcEngineHarness { Settings = PcSettings.Default with { Enabled = true, RealInput = true } };
        var app = h.Notepad(PcBorrowRig.Target, 42, @"c:\windows\mspaint.exe");
        h.Approver.Standings[("mspaint.exe", PcCapability.Act)] = PcApproval.Always;
        h.OverrideLease = rig.Lease;
        h.OverrideLadder = new PcActionLadder([new PcBorrowRung(() => h.Settings, rig.Lease)], rig.Capabilities);
        using var engine = h.Engine();
        app.Minimized = true;
        const string Save = "{\"window\":\"w:200\",\"target\":{\"name\":\"Save\"}}";

        h.Confirmer.ShowAnswer = PcShowAnswer.NotNow;
        Assert.Equal(PcErrorCode.DeclinedByUser, (await PcEngineHarness.Call(engine, "pc_click", Save)).Error);
        Assert.Equal([PcControlEngine.RealInputReason], h.Confirmer.ShownWhy);
        Assert.Empty(rig.Lease.Waits);
        Assert.Empty(rig.Host.Sent);
        Assert.Empty(Peeks(h));

        // Show: the window is shown before the borrow waits at its gate, and minimized again when the step ends
        h.Confirmer.ShowAnswer = PcShowAnswer.Show;
        rig.CardUp = true;
        var action = PcEngineHarness.Call(engine, "pc_click", Save);
        await PcTestWait.Until(() => rig.Lease.Waits.Count == 1);
        Assert.Equal(["peek w:200"], Peeks(h));
        Assert.False(app.Minimized);
        h.Approver.Standings.Remove(("mspaint.exe", PcCapability.Act));
        rig.CardUp = false;
        rig.Clock.Advance(RealInputLease.PollMs);
        Assert.Equal(PcErrorCode.NotApproved, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.Empty(rig.Host.Sent);
        Assert.Equal(["peek w:200", "unpeek w:200"], Peeks(h));
        Assert.True(app.Minimized);
    }
}
