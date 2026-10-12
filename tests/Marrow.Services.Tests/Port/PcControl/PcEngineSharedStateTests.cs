using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-19; DESIGN.md 2.9; critique C5, C6). The same-window rules on the
// fake desktop with the physical input monitor: a press that may open a window waits for a still moment while the user
// types in the app (Do it now, Not now), a box elsewhere is ticked at once, an expand waits for the pointer inside the
// window, a target the user changed while the step waited stops it, a passing pointer is not a change by the user, a
// write waits while the hooks are blind, a window a step opened in front is announced, the user's input makes the cached
// tree dirty, and a focus-moving way that is the only way is kept to wait.
[Trait("Decision", "ED-NEW-pcctl-19")]
public sealed class PcEngineSharedStateTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";
    private const string ClickSave = "{\"window\":\"w:1a\",\"element\":3}";
    private const string ClickWrap = "{\"window\":\"w:1a\",\"element\":4}";
    private const string ClickFifth = "{\"window\":\"w:1a\",\"element\":5}";

    [Fact]
    public async Task A_press_that_may_open_a_window_waits_for_a_still_moment_while_the_user_types_in_the_app()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";
        var waits = new List<PcGateBlock>();
        harness.Lease.Waiting += wait => waits.Add(wait.Check.Block);
        harness.UserTypes(0x1a);
        var from = harness.Desktop.Clock.Delays.Count;

        var result = await PcEngineHarness.Call(engine, "pc_click", ClickSave);

        Assert.True(result.Ok, result.Render());
        Assert.Equal(1, harness.Desktop.Performs);
        Assert.Equal(PcInputGate.KeysStillMs, Waited(harness, from));
        Assert.Equal(PcGateBlock.Keys, waits[0]);
        Assert.Empty(harness.Lease.Waits);
    }

    [Fact]
    public async Task Not_now_defers_the_press_and_nothing_is_pressed()
    {
        var (harness, _, engine) = await Ready();
        harness.Lease.Waiting += wait => wait.NotNow();
        harness.UserTypes(0x1a);

        var result = await PcEngineHarness.Call(engine, "pc_click", ClickSave);

        Assert.Equal(PcErrorCode.DeferredByUser, result.Error);
        Assert.Contains("Not now", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, harness.Desktop.Performs);
    }

    [Fact]
    public async Task Do_it_now_presses_without_waiting_for_the_stillness()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";
        harness.Lease.Waiting += wait => wait.DoItNow();
        harness.UserTypes(0x1a);
        var from = harness.Desktop.Clock.Delays.Count;

        var result = await PcEngineHarness.Call(engine, "pc_click", ClickSave);

        Assert.True(result.Ok, result.Render());
        Assert.Equal(RealInputLease.PollMs, Waited(harness, from));
    }

    [Fact]
    public async Task A_box_elsewhere_in_the_app_is_ticked_at_once_while_the_user_types()
    {
        var (harness, app, engine) = await Ready();
        var waits = 0;
        harness.Lease.Waiting += _ => waits++;
        harness.UserTypes(0x1a);
        var from = harness.Desktop.Clock.Delays.Count;

        var result = await PcEngineHarness.Call(engine, "pc_click", ClickWrap);

        Assert.True(result.Ok, result.Render());
        Assert.True(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        Assert.Equal(0, waits);
        Assert.Equal(0, Waited(harness, from));
    }

    [Fact]
    public async Task An_expand_waits_while_the_pointer_moves_inside_the_window_and_not_while_it_is_elsewhere()
    {
        var (harness, app, engine) = await Ready();
        var folder = app.Root.Add(new PcFakeNode("1.26.5", "tree item", "Folder")
        {
            Actions = PcActions.ExpandCollapse,
            States = PcStates.Collapsed,
            Bounds = new PcRect(240, 110, 120, 20),
        });
        harness.Desktop.Bump(0x1a, structure: true);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        harness.UserPoints(0x1a, new PcPoint(300, 300));
        var from = harness.Desktop.Clock.Delays.Count;

        var expanded = await PcEngineHarness.Call(engine, "pc_click", ClickFifth);

        Assert.True(expanded.Ok, expanded.Render());
        Assert.True(folder.States.HasFlag(PcStates.Expanded));
        Assert.Equal(new[] { AppStateGuard.PointerNearMs }, harness.Desktop.Clock.Delays.Skip(from));

        harness.UserPoints(0x77, new PcPoint(5, 5));
        from = harness.Desktop.Clock.Delays.Count;
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", ClickFifth)).Ok);
        Assert.True(folder.States.HasFlag(PcStates.Collapsed));
        Assert.Equal(0, Waited(harness, from));
    }

    [Fact]
    public async Task A_target_the_user_changed_while_the_step_waited_stops_it()
    {
        var (harness, app, engine) = await Ready();
        var draft = app.Root.Add(new PcFakeNode("1.26.5", "list item", "Draft") { Actions = PcActions.Select, Bounds = new PcRect(240, 110, 120, 20) });
        harness.Desktop.Bump(0x1a, structure: true);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        harness.UserTypes(0x1a);
        // 300 ms into the wait the user clicks the item themselves.
        harness.Desktop.Schedule(300, () =>
        {
            draft.States |= PcStates.Selected;
            harness.UserButton(0x1a, new PcPoint(250, 115), down: true);
            harness.UserButton(0x1a, new PcPoint(250, 115), down: false);
            harness.Desktop.Bump(0x1a);
        });

        var result = await PcEngineHarness.Call(engine, "pc_click", ClickFifth);

        Assert.Equal(PcErrorCode.ExpectFailed, result.Error);
        Assert.Equal("the user changed list item \"Draft\" while Tactus waited: it is now selected; look again before acting", result.Reason);
        Assert.Equal(0, harness.Desktop.Performs);
    }

    [Fact]
    public async Task A_pointer_passing_over_the_window_is_not_a_change_by_the_user_and_a_key_is()
    {
        var (harness, app, engine) = await Ready();
        var field = app.Root.Children[0];
        field.Value = "autosaved draft";
        harness.Desktop.Bump(0x1a);
        harness.UserPoints(0x1a, new PcPoint(300, 300));

        var set = await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"x\"}");
        Assert.True(set.Ok, set.Render());
        Assert.Equal("x", field.Value);

        harness.Desktop.Clock.NowMs += 3000;
        field.Value = "x and the user's words";
        harness.UserTypes(0x1a);
        harness.Desktop.Bump(0x1a);
        var again = await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"y\"}");

        Assert.Equal(PcErrorCode.ExpectFailed, again.Error);
        Assert.StartsWith("the user changed edit \"Text editor\" since your look", again.Reason, StringComparison.Ordinal);
        Assert.Equal("x and the user's words", field.Value);
    }

    [Fact]
    public async Task A_write_into_the_focused_field_in_front_waits_while_the_hooks_are_blind()
    {
        var (harness, app, engine) = await Ready();
        var field = app.Root.Children[0];
        field.States |= PcStates.Focused;
        harness.Desktop.ForegroundWindow = 0x1a;
        harness.Desktop.Bump(0x1a);
        harness.Input.Installed(false);
        harness.Desktop.Schedule(1000, () =>
        {
            harness.Input.Installed(true);
            harness.Input.LastInputSeen(harness.Desktop.Clock.NowMs);
        });
        var from = harness.Desktop.Clock.Delays.Count;

        var typed = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"x\"}");

        Assert.True(typed.Ok, typed.Render());
        Assert.Equal(1000, Waited(harness, from));
        Assert.Equal("x", field.Value);

        harness.Input.Installed(false);
        from = harness.Desktop.Clock.Delays.Count;
        var blind = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"y\"}");
        Assert.Equal(PcErrorCode.UserTypingHere, blind.Error);
        Assert.Equal(PcControlEngine.YieldMs, Waited(harness, from));
        Assert.Equal("the user may be typing in edit \"Text editor\" (notepad): Tactus cannot see the keyboard right now", blind.Reason);
        Assert.Equal("x", field.Value);
    }

    [Fact]
    public async Task A_window_a_step_opened_that_took_the_foreground_is_announced_for_the_pill()
    {
        var (harness, app, engine) = await Ready();
        var notices = new List<PcOpenedNotice>();
        harness.Control.WindowOpened += notices.Add;
        app.Root.Children[1].OnInvoke = (desktop, _) =>
        {
            desktop.AddApp(0x5e, "Save as", "notepad.exe", 101);
            desktop.ForegroundWindow = 0x5e;
        };

        Assert.True((await PcEngineHarness.Call(engine, "pc_click", ClickSave)).Ok);

        var notice = Assert.Single(notices);
        Assert.Equal("notepad opened Save as", notice.Text);
        Assert.Equal(2000, PcOpenedNotice.ShowMs);
    }

    [Fact]
    public async Task The_users_input_in_the_window_makes_its_cached_tree_dirty()
    {
        var (harness, _, engine) = await Ready();
        var reads = harness.Desktop.TreeReads;

        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        Assert.Equal(reads, harness.Desktop.TreeReads);

        harness.UserPoints(0x1a, new PcPoint(300, 300));
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        Assert.Equal(reads + 1, harness.Desktop.TreeReads);

        harness.UserPoints(0x77, new PcPoint(5, 5));
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        Assert.Equal(reads + 1, harness.Desktop.TreeReads);
    }

    [Fact]
    public void A_focus_moving_way_that_is_the_only_way_is_kept_to_wait_for_a_still_moment()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var click = new PcInputRequest(PcInputKind.Click, app.Info) { Element = app.Root.Children[1].Snapshot(false) };
        harness.Capabilities.FocusMoving(harness.Ladder.Plan(click).Plans[0].Key!);

        var idle = Assert.Single(harness.Ladder.Plan(click).Plans);
        var active = Assert.Single(harness.Ladder.Plan(click with { UserActive = true }).Plans);

        Assert.False(idle.WaitsForStill);
        Assert.True(active.WaitsForStill);
        Assert.Equal("invoke", active.Method);
        Assert.Equal(PcStepEffect.AppGlobal | PcStepEffect.MovesFocus, AppStateGuard.Classify(click, active));
    }

    // The milliseconds the engine waited on its clock since the given count of waits.
    private static int Waited(PcEngineHarness harness, int from) => harness.Desktop.Clock.Delays.Skip(from).Sum();

    private static async Task<(PcEngineHarness Harness, PcFakeApp App, PcControlEngine Engine)> Ready()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        return (harness, app, engine);
    }
}
