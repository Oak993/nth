using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, ED-NEW-pcctl-6, ED-NEW-pcctl-8). The engine on the fake desktop: its
// gates in order, looking (tree, diff, screenshot rules), acting in the background through UI Automation without ever
// taking the foreground, checking every action, keys through the commands that show them, reading, zooming and the audit.
[Trait("Decision", "ED-NEW-pcctl-6")]
public sealed class PcControlEngineTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    private static async Task<(PcEngineHarness Harness, PcFakeApp App, PcControlEngine Engine)> Ready()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var engine = harness.Engine();
        var look = await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.True(look.Ok, look.Render());
        return (harness, app, engine);
    }

    [Fact]
    public async Task The_gates_refuse_in_order_with_stable_codes()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var engine = harness.Engine();
        harness.Settings = harness.Settings with { Enabled = false };
        Assert.Equal(PcErrorCode.PcOff, (await PcEngineHarness.Call(engine, "pc_observe", Look)).Error);
        harness.Settings = harness.Settings with { Enabled = true };
        Assert.Equal(PcErrorCode.Unattended, (await PcEngineHarness.Call(harness.Engine(attended: false), "pc_observe", Look)).Error);
        harness.Desktop.Secure = true;
        Assert.Equal(PcErrorCode.SecureDesktop, (await PcEngineHarness.Call(engine, "pc_observe", Look)).Error);
        harness.Desktop.Secure = false;
        Assert.Equal(PcErrorCode.WindowGone, (await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:99\"}")).Error);
        harness.Desktop.OwnHandles.Add(0x1a);
        Assert.Equal(PcErrorCode.OwnWindow, (await PcEngineHarness.Call(engine, "pc_observe", Look)).Error);
        harness.Desktop.OwnHandles.Clear();
        harness.Desktop.ElevatedProcesses.Add(101);
        Assert.Equal(PcErrorCode.Elevated, (await PcEngineHarness.Call(engine, "pc_observe", Look)).Error);
        harness.Desktop.ElevatedProcesses.Clear();
        harness.Desktop.AddApp(0x3c, "PowerShell", "pwsh.exe", 303);
        Assert.Equal(PcErrorCode.DeniedApp, (await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:3c\"}")).Error);
        harness.Approver.Answer = PcApproval.Denied;
        Assert.Equal(PcErrorCode.NotApproved, (await PcEngineHarness.Call(engine, "pc_observe", Look)).Error);
        Assert.Equal(PcErrorCode.BadArgs, (await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"notepad\"}")).Error);
        Assert.Equal(PcErrorCode.BadArgs, (await PcEngineHarness.Call(engine, "pc_teleport", "{}")).Error);
        Assert.Equal(0, harness.Desktop.Performs);
        Assert.Equal(0, harness.Desktop.TreeReads);
    }

    [Fact]
    public async Task Seeing_and_acting_are_asked_separately_once_per_chat()
    {
        var (harness, _, engine) = await Ready();
        await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.Equal(new[] { ("notepad.exe", PcCapability.See, true) }, harness.Approver.Asked);
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")).Ok);
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")).Ok);
        Assert.Equal(new[] { ("notepad.exe", PcCapability.See, true), ("notepad.exe", PcCapability.Act, true) }, harness.Approver.Asked);
    }

    [Fact]
    public async Task A_standing_always_grant_is_used_without_asking()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        harness.Approver.Standings[("notepad.exe", PcCapability.See)] = PcApproval.Always;
        Assert.True((await PcEngineHarness.Call(harness.Engine(), "pc_observe", Look)).Ok);
        Assert.Empty(harness.Approver.Asked);
    }

    [Fact]
    public async Task A_look_numbers_the_tree_then_answers_with_diffs_and_keeps_numbers_stable()
    {
        var (harness, app, engine) = await Ready();
        var again = await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.Contains("no visible change since o:1", again.Body, StringComparison.Ordinal);
        app.Root.Children.Insert(0, new PcFakeNode("1.26.9", "button", "New") { Actions = PcActions.Invoke, Bounds = new PcRect(110, 110, 40, 20) });
        // The app's structure event for the new button.
        harness.Desktop.Bump(0x1a, structure: true);
        var diff = await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.Contains("+ [5]", diff.Body, StringComparison.Ordinal);
        // Save keeps number 3 although it moved down a line.
        var full = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\",\"full\":true}");
        Assert.Contains("[3]   button \"Save\"", full.Body, StringComparison.Ordinal);
        Assert.Equal("o:4", full.Window!.Obs);
    }

    [Fact]
    public async Task A_warm_tree_is_reused_until_a_change_event_arrives()
    {
        var (harness, app, engine) = await Ready();
        var reads = harness.Desktop.TreeReads;
        await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.Equal(reads, harness.Desktop.TreeReads);
        app.Title = "Changed - notepad";
        harness.Desktop.Bump(0x1a);
        await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.Equal(reads + 1, harness.Desktop.TreeReads);
    }

    [Fact]
    public async Task Screenshots_come_only_when_needed_and_never_for_a_minimized_window_the_user_keeps_minimized_or_a_text_only_model()
    {
        var (harness, app, engine) = await Ready();
        Assert.Equal(0, harness.CaptureSource.Calls);
        var shot = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}");
        Assert.NotNull(shot.Image);
        Assert.Equal(1, harness.CaptureSource.Calls);
        Assert.Contains("element numbers in small labels", shot.Body, StringComparison.Ordinal);
        Assert.Equal(new[] { 2, 3, 4 }, harness.Encoder.Marks[^1].Select(m => m.Number).Order().ToArray());
        var blind = await PcEngineHarness.Call(harness.Engine("chat-2", vision: false), "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}");
        Assert.Null(blind.Image);
        Assert.Contains("does not take images", blind.Body, StringComparison.Ordinal);
        Assert.Empty(harness.Confirmer.Shown);
        // a minimized window is shown for a screenshot only after the user's Show (ED-NEW-pcctl-46); with Not now, no pixels
        harness.Confirmer.ShowAnswer = PcShowAnswer.NotNow;
        app.Minimized = true;
        var minimized = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}");
        Assert.True(minimized.Ok);
        Assert.Null(minimized.Image);
        Assert.Contains("minimized", minimized.Body, StringComparison.Ordinal);
        Assert.Contains("chose Not now", minimized.Body, StringComparison.Ordinal);
        Assert.Equal(1, harness.CaptureSource.Calls);
        Assert.Single(harness.Confirmer.Shown);
        Assert.True(app.Minimized);
        Assert.DoesNotContain(harness.Desktop.Log, l => l.StartsWith("peek", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_weak_tree_gets_a_screenshot_in_auto()
    {
        var harness = new PcEngineHarness();
        var app = harness.Desktop.AddApp(0x5a, "Paint", "mspaint.exe", 505);
        app.Root.Add(new PcFakeNode("1.90.2", "pane", "Canvas") { Bounds = new PcRect(110, 140, 1100, 700) });
        var look = await PcEngineHarness.Call(harness.Engine(), "pc_observe", "{\"window\":\"w:5a\"}");
        Assert.True(look.Ok, look.Render());
        Assert.NotNull(look.Image);
    }

    [Fact]
    public async Task A_click_runs_the_elements_own_pattern_in_the_background_and_is_checked()
    {
        var (harness, app, engine) = await Ready();
        var result = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");
        Assert.True(result.Ok, result.Render());
        Assert.True(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        Assert.Contains("~ [4]", result.Body, StringComparison.Ordinal);
        Assert.Contains("rung: R1 (toggle)", result.Body, StringComparison.Ordinal);
        // The user's window kept the foreground; nothing was activated and no message was sent.
        Assert.Equal((nint)0x77, harness.Desktop.ForegroundWindow);
        Assert.DoesNotContain(harness.Desktop.Log, l => l.StartsWith("activate", StringComparison.Ordinal));
        Assert.Equal(0, harness.Desktop.Messages);
        Assert.Equal("R1 toggle", harness.Audit.Entries[^1].Rung);
    }

    [Fact]
    public async Task A_window_minimized_or_behind_others_is_driven_all_the_same()
    {
        var (_, app, engine) = await Ready();
        app.Minimized = true;
        var result = await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"from the background\"}");
        Assert.True(result.Ok, result.Render());
        Assert.Equal("from the background", app.Root.Children[0].Value);
        Assert.True(app.Minimized);
    }

    [Fact]
    public async Task Typing_goes_straight_into_the_control_and_is_read_back()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[0].Value = "Dear ";
        var result = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"team\"}");
        Assert.True(result.Ok, result.Render());
        Assert.Equal("Dear team", app.Root.Children[0].Value);
        Assert.Equal(4, harness.Audit.Entries[^1].TextLength);
        Assert.Null(harness.Audit.Entries[^1].Text);
        var replaced = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"Hello\",\"mode\":\"replace\"}");
        Assert.True(replaced.Ok, replaced.Render());
        Assert.Equal("Hello", app.Root.Children[0].Value);
    }

    [Fact]
    public async Task Text_without_a_field_goes_to_the_apps_own_focused_control()
    {
        var (harness, app, engine) = await Ready();
        var result = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"text\":\"hi\"}");
        Assert.True(result.Ok, result.Render());
        Assert.Equal("hi", app.Root.Children[0].Value);
        Assert.Equal((nint)0x77, harness.Desktop.ForegroundWindow);
    }

    [Fact]
    public async Task A_field_that_stores_something_else_is_expect_failed()
    {
        var (_, app, engine) = await Ready();
        app.Root.Children[0].Accepts = v => v.ToUpperInvariant();
        var result = await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"quiet\"}");
        Assert.Equal(PcErrorCode.ExpectFailed, result.Error);
        Assert.Contains("shows \"QUIET\", not \"quiet\"", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_fields_are_never_typed_into_or_read()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[0].States |= PcStates.Password;
        harness.Desktop.Bump(0x1a);
        await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.Equal(PcErrorCode.PasswordField, (await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"x\"}")).Error);
        Assert.Equal(PcErrorCode.PasswordField, (await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"x\"}")).Error);
        Assert.Equal(PcErrorCode.PasswordField, (await PcEngineHarness.Call(engine, "pc_read", "{\"window\":\"w:1a\",\"element\":2}")).Error);
        Assert.Equal(0, harness.Desktop.Performs);
    }

    [Fact]
    public async Task A_shortcut_runs_the_command_that_shows_it_and_alt_f4_closes_through_the_window()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";
        var save = await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"ctrl+s\"}");
        Assert.True(save.Ok, save.Render());
        Assert.Contains("rung: R1 (accelerator)", save.Body, StringComparison.Ordinal);
        Assert.Contains("invoke Save", harness.Desktop.Log);
        // No command shows ctrl+shift+q, and real input is off: Tactus says so instead of typing it.
        var unknown = await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"ctrl+shift+q\"}");
        Assert.Equal(PcErrorCode.NeedsRealInput, unknown.Error);
        Assert.Contains(PcRealInputReason.Off, unknown.Reason, StringComparison.Ordinal);
        var close = await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"alt+f4\"}");
        Assert.True(close.Ok, close.Render());
        Assert.Contains("the window closed", close.Body, StringComparison.Ordinal);
        Assert.Equal(0, harness.Desktop.Messages);
    }

    [Fact]
    public async Task Denied_keys_and_bad_chords_are_refused_before_anything_runs()
    {
        var (harness, _, engine) = await Ready();
        Assert.Equal(PcErrorCode.DeniedKey, (await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"win+r\"}")).Error);
        Assert.Equal(PcErrorCode.BadArgs, (await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"ctrl+blorp\"}")).Error);
        Assert.Equal(PcErrorCode.BadArgs, (await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"tab\",\"repeat\":21}")).Error);
        Assert.Equal(0, harness.Desktop.Performs + harness.Desktop.Messages);
    }

    [Fact]
    public async Task A_name_from_the_model_finds_the_element_and_a_double_match_is_refused()
    {
        var (harness, app, engine) = await Ready();
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"target\":{\"name\":\"Word wrap\"}}")).Ok);
        app.Root.Add(new PcFakeNode("1.26.8", "check box", "Word wrap") { Actions = PcActions.Toggle, Bounds = new PcRect(400, 110, 120, 30) });
        harness.Desktop.Bump(0x1a, structure: true);
        var twice = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"target\":{\"name\":\"Word wrap\"}}");
        Assert.Equal(PcErrorCode.AmbiguousTarget, twice.Error);
        Assert.Contains("[4]", twice.Reason, StringComparison.Ordinal);
        Assert.Contains("[5]", twice.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_point_acts_on_the_element_under_it_with_a_full_resolution_crop_and_is_refused_on_nths_window_or_a_stale_look()
    {
        var (harness, app, engine) = await Ready();
        var shot = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}");
        Assert.Equal("o:2", shot.Window!.Obs);
        // The checkbox is at 200..320 x 110..140 on screen; the 1200 x 800 window is shown 1:1 on the grid.
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"point\":[150,20],\"obs\":\"o:2\"}");
        Assert.True(click.Ok, click.Render());
        Assert.True(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        Assert.Single(click.Details);
        Assert.Equal(PcErrorCode.StaleObs, (await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"point\":[150,20],\"obs\":\"o:1\"}")).Error);
        harness.Desktop.OwnRects.Add(new PcRect(240, 100, 100, 100));
        Assert.Equal(PcErrorCode.OwnWindow, (await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"point\":[150,20],\"obs\":\"o:2\"}")).Error);
    }

    [Fact]
    public async Task A_dialog_that_opens_late_is_waited_for_on_window_events()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[1].OnInvoke = (desktop, _) => desktop.Schedule(700, () => desktop.AddApp(0x5e, "Save as", "notepad.exe", 101));
        var result = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3,\"expect\":{\"window_opens\":{\"title\":\"Save as\"}}}");
        Assert.True(result.Ok, result.Render());
        Assert.Contains("new window: w:5e \"Save as\"", result.Body, StringComparison.Ordinal);
        var waited = await PcEngineHarness.Call(engine, "pc_wait", "{\"window\":\"w:5e\",\"until\":\"idle\",\"timeout_ms\":1000}");
        Assert.True(waited.Ok, waited.Render());
    }

    [Fact]
    public async Task Wait_for_text_wakes_on_the_apps_change_event()
    {
        var (harness, app, engine) = await Ready();
        harness.Desktop.Schedule(900, () =>
        {
            app.Root.Add(new PcFakeNode("1.26.7", "text", "Saved") { Bounds = new PcRect(300, 700, 60, 20) });
            harness.Desktop.Notify(0x1a, structure: true);
        });
        var start = harness.Desktop.Clock.NowMs;
        var result = await PcEngineHarness.Call(engine, "pc_wait", "{\"window\":\"w:1a\",\"until\":\"text\",\"text\":\"Saved\",\"timeout_ms\":5000}");
        Assert.True(result.Ok, result.Render());
        Assert.InRange(harness.Desktop.Clock.NowMs - start, 900, 900 + PcFakeDesktop.EventLatencyMs);
        Assert.Equal(PcErrorCode.Timeout, (await PcEngineHarness.Call(engine, "pc_wait", "{\"window\":\"w:1a\",\"until\":\"text\",\"text\":\"never\",\"timeout_ms\":300}")).Error);
        Assert.Empty(harness.Desktop.Clock.Delays);
    }

    [Fact]
    public async Task Reading_pages_through_long_text_and_zoom_recrops_the_last_screenshot()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[0].Value = new string('x', 9000) + "END";
        harness.Desktop.Bump(0x1a);
        var first = await PcEngineHarness.Call(engine, "pc_read", "{\"window\":\"w:1a\",\"element\":2}");
        Assert.Contains("text 0 to 8000 of 9003 characters", first.Body, StringComparison.Ordinal);
        var rest = await PcEngineHarness.Call(engine, "pc_read", "{\"window\":\"w:1a\",\"element\":2,\"offset\":8000}");
        Assert.EndsWith("END", rest.Body, StringComparison.Ordinal);
        var shot = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}");
        var zoom = await PcEngineHarness.Call(engine, "pc_zoom", "{\"obs\":\"" + shot.Window!.Obs + "\",\"region\":[100,10,120,30]}");
        Assert.True(zoom.Ok, zoom.Render());
        Assert.NotNull(zoom.Image);
        Assert.Equal(1, harness.CaptureSource.Calls);
        Assert.Equal(new PcRect(100, 10, 120, 30), harness.Encoder.Calls[^1].Crop);
    }

    [Fact]
    public async Task Pc_list_apps_names_windows_their_state_and_what_the_user_allowed()
    {
        var (harness, app, engine) = await Ready();
        harness.Desktop.AddApp(0x3c, "PowerShell", "pwsh.exe", 303);
        app.Minimized = true;
        harness.Desktop.OwnHandles.Add(0x4d);
        harness.Desktop.AddApp(0x4d, "nth", "nth.exe", 404);
        var list = await PcEngineHarness.Call(engine, "pc_list_apps", "{}");
        Assert.True(list.Ok, list.Render());
        Assert.Contains("exe notepad.exe approved: chat", list.Body, StringComparison.Ordinal);
        Assert.Contains("w:1a \"Untitled - notepad\" screen 1, minimized", list.Body, StringComparison.Ordinal);
        Assert.Contains("exe pwsh.exe approved: blocked (", list.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("w:4d", list.Body, StringComparison.Ordinal);
        Assert.Contains("screen 1: 2560x1440 at 0,0, scale 1.5, primary", list.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_call_is_audited_with_the_time_of_each_step()
    {
        var (harness, _, engine) = await Ready();
        await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");
        var entry = harness.Audit.Entries[^1];
        Assert.Equal("pc_click", entry.Tool);
        Assert.Equal("ok", entry.Result);
        Assert.Equal("notepad.exe", entry.App);
        Assert.Equal("Word wrap", entry.Element);
        var steps = entry.Steps.Select(s => s.Key).ToList();
        foreach (var step in new[] { "guard", "approve", "queue", "resolve", "plan", "act", "settle", "verify", "observe" }) Assert.Contains(step, steps);
        Assert.True(entry.Steps.Sum(s => s.Value) <= entry.DurationMs);
    }
}
