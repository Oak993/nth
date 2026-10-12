using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.6, 2.9, 4.3; critique C5, C6, C7, C9, C10, C13 to
// C17). The engine's safety rules on the fake desktop: Hydra heads never use "Always", one chat holds a window at a time, a
// reused window handle is never acted on, guarded buttons (English, Hindi, Urdu, ids, default actions, menu text, nameless
// buttons in mail apps) ask first, the clipboard is the user's, pc_focus_window asks and never activates, a locked or
// sleeping PC, a taken stop key, stops, the delta check that drops a way that moved the user's pointer, the yield rule,
// and a failed check falling to the next way once.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcEngineSafetyTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";
    private const string ClickSave = "{\"window\":\"w:1a\",\"element\":3}";

    private static async Task<(PcEngineHarness Harness, PcFakeApp App, PcControlEngine Engine)> Ready(string exe = "notepad.exe")
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad(exe: exe);
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        return (harness, app, engine);
    }

    // The Save button renamed (or given an id or default action), looked at again, then clicked with the card answered No.
    private static async Task<(PcResult Result, PcEngineHarness Harness)> GuardedClick(Action<PcFakeNode> change, string exe = "notepad.exe")
    {
        var (harness, app, engine) = await Ready(exe);
        change(app.Root.Children[1]);
        harness.Desktop.Bump(0x1a);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        harness.Confirmer.Answer = false;
        return (await PcEngineHarness.Call(engine, "pc_click", ClickSave), harness);
    }

    [Fact]
    public async Task A_hydra_head_is_offered_this_chat_only_and_never_uses_always()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        harness.Approver.Standings[("notepad.exe", PcCapability.See)] = PcApproval.Always;
        harness.Approver.Answer = PcApproval.Always;
        var head = harness.Engine("chat-1", head: "head 2");
        Assert.True((await PcEngineHarness.Call(head, "pc_observe", Look)).Ok);
        Assert.Equal(new[] { ("notepad.exe", PcCapability.See, false) }, harness.Approver.Asked);
        var list = await PcEngineHarness.Call(head, "pc_list_apps", "{}");
        Assert.Contains("approved: chat", list.Body, StringComparison.Ordinal);
        var away = await PcEngineHarness.Call(harness.Engine("chat-1", attended: false, head: "head 3"), "pc_observe", Look);
        Assert.Equal(PcErrorCode.Unattended, away.Error);
        Assert.Contains("Hydra heads", away.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_chat_holds_a_window_it_acts_in_until_its_turn_ends_or_it_is_idle_for_a_minute()
    {
        var (harness, _, engine) = await Ready();
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")).Ok);
        var other = harness.Engine("chat-2", head: "head 2");
        var busy = await PcEngineHarness.Call(other, "pc_observe", Look);
        Assert.Equal(PcErrorCode.BusyWindow, busy.Error);
        Assert.Contains("another chat is using this window", busy.Reason, StringComparison.Ordinal);
        Assert.Contains("in use by another chat", (await PcEngineHarness.Call(other, "pc_list_apps", "{}")).Body, StringComparison.Ordinal);
        engine.EndTurn();
        Assert.True((await PcEngineHarness.Call(other, "pc_click", "{\"window\":\"w:1a\",\"target\":{\"name\":\"Word wrap\"}}")).Ok);
        Assert.Equal(PcErrorCode.BusyWindow, (await PcEngineHarness.Call(engine, "pc_observe", Look)).Error);
        Assert.Contains("head 2 is using this window", (await PcEngineHarness.Call(engine, "pc_observe", Look)).Reason, StringComparison.Ordinal);
        harness.Desktop.Clock.NowMs += PcWindowOwnership.IdleMs;
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
    }

    [Fact]
    public async Task A_window_handle_windows_gave_to_another_window_is_never_acted_on()
    {
        var (harness, _, engine) = await Ready();
        harness.Desktop.Close(0x1a);
        var stranger = harness.Desktop.AddApp(0x1a, "Calculator", "calc.exe", 909);
        stranger.ClassName = "CalcFrame";
        var reused = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");
        Assert.Equal(PcErrorCode.WindowGone, reused.Error);
        Assert.Contains("gave its id to another window", reused.Reason, StringComparison.Ordinal);
        Assert.Equal(0, harness.Desktop.Performs);
        // A fresh list is the model's new view of the handle.
        await PcEngineHarness.Call(engine, "pc_list_apps", "{}");
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
    }

    [Fact]
    public async Task Guarded_names_in_english_hindi_and_urdu_ask_first_and_a_no_stops_the_action()
    {
        foreach (var (name, word) in new[] { ("Send", "send"), ("\u092d\u0947\u091c\u0947\u0902", "send"), ("\u0628\u06be\u06cc\u062c\u06cc\u06ba", "send"), ("Delete account", "delete account") })
        {
            var (result, harness) = await GuardedClick(n => n.Name = name);
            Assert.Equal(PcErrorCode.NeedsConfirmation, result.Error);
            Assert.Equal(new[] { word + " " + name }, harness.Confirmer.Asked);
            Assert.Equal(0, harness.Desktop.Performs);
        }
    }

    [Fact]
    public async Task Automation_ids_default_actions_and_nameless_buttons_in_mail_apps_ask_too()
    {
        var (byId, idHarness) = await GuardedClick(n =>
        {
            n.Name = "Go";
            n.AutomationId = "btnSendNow";
        });
        Assert.Equal(PcErrorCode.NeedsConfirmation, byId.Error);
        Assert.Equal(new[] { "send Go" }, idHarness.Confirmer.Asked);
        var (byAction, actionHarness) = await GuardedClick(n =>
        {
            n.Name = "Item";
            n.DefaultAction = "Delete";
        });
        Assert.Equal(PcErrorCode.NeedsConfirmation, byAction.Error);
        Assert.Equal(new[] { "delete Item" }, actionHarness.Confirmer.Asked);
        var (nameless, mailHarness) = await GuardedClick(n => n.Name = "", "whatsapp.exe");
        Assert.Equal(PcErrorCode.NeedsConfirmation, nameless.Error);
        Assert.Equal(new[] { "press a button with no name (it may be Send)" }, mailHarness.Confirmer.Asked);
        // The same nameless button in an app that sends nothing does not ask (and Save changes nothing, so it is checked).
        var (plain, plainHarness) = await GuardedClick(n => n.Name = "");
        Assert.Empty(plainHarness.Confirmer.Asked);
        Assert.NotEqual(PcErrorCode.NeedsConfirmation, plain.Error);
    }

    [Fact]
    public async Task The_card_reads_what_enter_will_press_and_a_menu_items_text()
    {
        var harness = new PcEngineHarness();
        var app = harness.Classic();
        var ok = app.Root.Children[1];
        ok.AutomationId = null;
        ok.Name = "Send";
        ok.States |= PcStates.Focused;
        app.FocusControl = ok;
        app.Menu.Add(("Ctrl+D", new PcMenuItem(77, "&Delete\tCtrl+D", false), _ => app.Title = "deleted"));
        var engine = harness.Engine();
        harness.Confirmer.Answer = false;
        Assert.Equal(PcErrorCode.NeedsConfirmation, (await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:2c\",\"keys\":\"enter\"}")).Error);
        Assert.Equal(PcErrorCode.NeedsConfirmation, (await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:2c\",\"keys\":\"ctrl+d\"}")).Error);
        Assert.Equal(new[] { "send Send", "delete Delete" }, harness.Confirmer.Asked);
        Assert.Equal(0, harness.Desktop.Messages);
        harness.Confirmer.Answer = true;
        var deleted = await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:2c\",\"keys\":\"ctrl+d\"}");
        Assert.True(deleted.Ok, deleted.Render());
        Assert.Contains("rung: R2 (wm_command)", deleted.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_the_card_off_in_settings_lets_guarded_buttons_run()
    {
        var (harness, app, engine) = await Ready();
        harness.Settings = harness.Settings with { ConfirmGuardedActions = false };
        app.Root.Children[1].Name = "Send";
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Sent";
        harness.Desktop.Bump(0x1a);
        await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", ClickSave)).Ok);
        Assert.Empty(harness.Confirmer.Asked);
    }

    [Fact]
    public async Task Copy_cut_and_paste_are_refused_as_keys_buttons_and_menu_commands()
    {
        var harness = new PcEngineHarness();
        var app = harness.Classic();
        app.Root.Add(new PcFakeNode("1.44.5", "button", "Paste") { Actions = PcActions.Invoke, Framework = "Win32", Bounds = new PcRect(300, 300, 60, 24) });
        app.Menu.Add(("Ctrl+Shift+V", new PcMenuItem(78, "&Paste\tCtrl+Shift+V", false), _ => { }));
        var engine = harness.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2c\",\"screenshot\":\"no\"}");
        var keys = await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:2c\",\"keys\":\"ctrl+v\"}");
        Assert.Equal(PcErrorCode.BadArgs, keys.Error);
        Assert.Contains("clipboard", keys.Reason, StringComparison.Ordinal);
        Assert.Equal(PcErrorCode.NotSupported, (await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:2c\",\"element\":5}")).Error);
        Assert.Equal(PcErrorCode.NotSupported, (await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:2c\",\"keys\":\"ctrl+shift+v\"}")).Error);
        Assert.Equal(0, harness.Desktop.Performs + harness.Desktop.Messages);
    }

    [Fact]
    public async Task Pc_focus_window_asks_on_the_pill_then_shows_the_window_without_taking_the_keyboard()
    {
        var (harness, app, engine) = await Ready();
        app.Minimized = true;
        var shown = await PcEngineHarness.Call(engine, "pc_focus_window", "{\"window\":\"w:1a\"}");
        Assert.True(shown.Ok, shown.Render());
        Assert.Equal(new[] { "notepad Untitled - notepad" }, harness.Confirmer.Shown);
        Assert.Contains("show w:1a", harness.Desktop.Log);
        Assert.False(app.Minimized);
        Assert.Equal((nint)0x77, harness.Desktop.ForegroundWindow);
        harness.Confirmer.ShowAnswer = PcShowAnswer.NotNow;
        Assert.Equal(PcErrorCode.DeclinedByUser, (await PcEngineHarness.Call(engine, "pc_focus_window", "{\"window\":\"w:1a\"}")).Error);
        Assert.DoesNotContain(harness.Desktop.Log, l => l.StartsWith("activate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_locked_pc_gives_tree_only_looks_cursor_free_steps_go_on_and_real_input_steps_say_locked()
    {
        var (harness, app, engine) = await Ready();
        harness.Desktop.IsLocked = true;
        var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}");
        Assert.True(look.Ok, look.Render());
        Assert.Null(look.Image);
        Assert.Contains("screen locked: tree only", look.Body, StringComparison.Ordinal);
        Assert.Equal(0, harness.CaptureSource.Calls);
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")).Ok);
        Assert.True(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        Assert.Equal(PcErrorCode.Locked, (await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"ctrl+shift+q\"}")).Error);
    }

    [Fact]
    public async Task Sleep_ends_the_running_step_as_locked_and_is_not_a_stop_for_the_turn()
    {
        var (harness, _, engine) = await Ready();
        harness.Desktop.BeforePerform = (_, _, _) => harness.Control.Sleeping();
        var slept = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");
        Assert.Equal(PcErrorCode.Locked, slept.Error);
        Assert.Contains("went to sleep", slept.Reason, StringComparison.Ordinal);
        harness.Desktop.BeforePerform = null;
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")).Ok);
    }

    [Fact]
    public async Task A_stop_of_the_chat_lets_the_running_action_finish_whole_and_blocks_the_rest_of_the_turn()
    {
        var (harness, app, engine) = await Ready();
        harness.Desktop.BeforePerform = (_, _, _) => engine.StopTurn();
        var stopped = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");
        Assert.Equal(PcErrorCode.StoppedByUser, stopped.Error);
        Assert.True(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        harness.Desktop.BeforePerform = null;
        var later = await PcEngineHarness.Call(engine, "pc_observe", Look);
        Assert.Equal(PcErrorCode.StoppedByUser, later.Error);
        Assert.Contains("for this turn", later.Reason, StringComparison.Ordinal);
        // Another chat is not stopped.
        Assert.Equal(PcErrorCode.BusyWindow, (await PcEngineHarness.Call(harness.Engine("chat-2"), "pc_observe", Look)).Error);
        engine.EndTurn();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
    }

    [Fact]
    public async Task A_stop_of_one_app_stops_only_that_app_for_the_turn()
    {
        var (harness, _, engine) = await Ready();
        var other = harness.Notepad(0x2b, 202, "wordpad.exe");
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2b\",\"screenshot\":\"no\"}")).Ok);
        harness.Control.Stop(PcScope.App(other.Info.App.Key), PcStopSource.Pill);
        Assert.Equal(PcErrorCode.StoppedByUser, (await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2b\",\"screenshot\":\"no\"}")).Error);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
    }

    [Fact]
    public async Task No_stream_starts_while_the_stop_key_cannot_be_registered()
    {
        var (harness, _, engine) = await Ready();
        harness.StopKey.Works = false;
        var refused = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");
        Assert.Equal(PcErrorCode.NotSupported, refused.Error);
        Assert.Contains("the stop key Ctrl+Alt+End is taken", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(PcErrorCode.NotSupported, (await PcEngineHarness.Call(engine, "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}")).Error);
        Assert.Equal(0, harness.Desktop.Performs);
    }

    [Fact]
    public async Task Pc_delegate_and_the_workspace_say_plainly_what_this_version_does_not_do()
    {
        var (harness, _, engine) = await Ready();
        Assert.Equal(PcErrorCode.BadArgs, (await PcEngineHarness.Call(engine, "pc_delegate", "{\"goal\":\"fill the form\"}")).Error);
        harness.Settings = harness.Settings with { FastHelper = true };
        Assert.Equal(PcErrorCode.NotSupported, (await PcEngineHarness.Call(engine, "pc_delegate", "{\"goal\":\"fill the form\"}")).Error);
        harness.Desktop.OnLaunch = _ => harness.Notepad(0x6f, 606, "calc.exe");
        Assert.Equal(PcErrorCode.NotInWorkspace, (await PcEngineHarness.Call(engine, "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\",\"place\":\"workspace\"}")).Error);
        Assert.DoesNotContain(harness.Desktop.Log, l => l.StartsWith("launch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Launching_waits_for_the_apps_first_window_on_window_events()
    {
        var (harness, _, engine) = await Ready();
        harness.Desktop.OnLaunch = _ => harness.Notepad(0x6f, 606, "calc.exe");
        var start = harness.Desktop.Clock.NowMs;
        var launched = await PcEngineHarness.Call(engine, "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}");
        Assert.True(launched.Ok, launched.Render());
        Assert.Contains("w:6f \"Untitled - calc\"", launched.Body, StringComparison.Ordinal);
        Assert.InRange(harness.Desktop.Clock.NowMs - start, harness.Desktop.LaunchMs, harness.Desktop.LaunchMs + PcFakeDesktop.EventLatencyMs);
        Assert.Empty(harness.Desktop.Clock.Delays);
    }

    [Fact]
    public async Task A_way_that_moved_the_users_pointer_is_not_used_again_in_that_app()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[1].OnInvoke = (desktop, _) =>
        {
            desktop.Cursor = new PcPoint(640, 480);
            app.Title = "Saved - notepad";
        };
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", ClickSave)).Ok);
        var again = await PcEngineHarness.Call(engine, "pc_click", ClickSave);
        Assert.Equal(PcErrorCode.NeedsRealInput, again.Error);
        Assert.Contains("took the user's focus or pointer", again.Reason, StringComparison.Ordinal);
        Assert.Equal(1, harness.Desktop.Performs);
    }

    [Fact]
    public async Task A_dialog_of_the_app_that_takes_the_foreground_is_reported_and_does_not_count_against_the_way()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[1].OnInvoke = (desktop, _) =>
        {
            desktop.AddApp(0x5e, "Save as", "notepad.exe", 101);
            desktop.ForegroundWindow = 0x5e;
        };
        var result = await PcEngineHarness.Call(engine, "pc_click", ClickSave);
        Assert.True(result.Ok, result.Render());
        Assert.Contains("new window: w:5e \"Save as\" (it took the foreground)", result.Body, StringComparison.Ordinal);
        var key = harness.Ladder.Plan(new PcInputRequest(PcInputKind.Click, app.Info) { Element = app.Root.Children[1].Snapshot(false) }).Plans[0].Key!;
        Assert.NotEqual(PcLearned.Refused, harness.Capabilities.State(key));
    }

    [Fact]
    public async Task A_write_into_the_control_the_user_is_typing_in_waits_and_then_runs()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[0].States |= PcStates.Focused;
        harness.Desktop.Bump(0x1a);
        harness.UserTypes(0x1a);
        var typed = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"x\"}");
        Assert.True(typed.Ok, typed.Render());
        Assert.Equal("x", app.Root.Children[0].Value);
        Assert.Equal(new[] { PcControlEngine.UserActiveMs }, harness.Desktop.Clock.Delays);
    }

    [Fact]
    public async Task A_user_who_keeps_typing_there_gets_user_typing_here_after_ten_seconds_and_nothing_is_written()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[0].States |= PcStates.Focused;
        harness.Desktop.Bump(0x1a);
        harness.KeepTyping(0x1a);
        var start = harness.Desktop.Clock.NowMs;
        var typed = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\"x\"}");
        Assert.Equal(PcErrorCode.UserTypingHere, typed.Error);
        Assert.Equal(PcControlEngine.YieldMs, harness.Desktop.Clock.NowMs - start);
        Assert.Equal("", app.Root.Children[0].Value);
        Assert.Equal(0, harness.Desktop.Performs);
        // A control the user is not in is written at once.
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")).Ok);
    }

    [Fact]
    public async Task A_failed_check_tries_the_next_way_once_and_reports_the_rung_that_worked()
    {
        var harness = new PcEngineHarness();
        var app = harness.Classic();
        var ok = app.Root.Children[1];
        ok.Ignores = true;
        ok.OnInvoke = (_, _) => app.Title = "Settings - saved";
        var engine = harness.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2c\",\"screenshot\":\"no\"}");
        var result = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:2c\",\"element\":3}");
        Assert.True(result.Ok, result.Render());
        Assert.Contains("rung: R2 (bm_click)", result.Body, StringComparison.Ordinal);
        Assert.Contains("invoke OK", harness.Desktop.Log);
        Assert.Equal("R2 bm_click", harness.Audit.Entries[^1].Rung);
    }
}
