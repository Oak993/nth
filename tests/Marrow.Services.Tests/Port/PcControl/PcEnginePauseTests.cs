using System.Diagnostics;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-14; DESIGN.md 10.1, 10.9, 10.11). Pause in the engine, on the fake
// desktop: a call for a paused app answers waiting_for_user "paused" at once and does nothing in the app; a call already
// waiting for its approval answers within 100 ms, and an answer given while paused is kept; held steps keep their order;
// the Pause all latch; pc_list_apps states; the re-read at the Resume (window gone, grant removed, points stale, a target
// changed while paused); a guarded action answered while paused asks again; typing holds between 64-character runs and
// reads the typed part back; Stop while paused; "Pausing" while an action runs.
[Trait("Decision", "ED-NEW-pcctl-14")]
public sealed class PcEnginePauseTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    private static string SetValue(string value) => "{\"window\":\"w:1a\",\"element\":2,\"value\":\"" + value + "\"}";

    private static string Resume(string? token, string window = "w:1a") => "{\"window\":\"" + window + "\",\"until\":\"step\",\"token\":\"" + token + "\"}";

    private static Task<PcResult> Call(PcControlEngine engine, string tool, string json) => PcEngineHarness.Call(engine, tool, json);

    [Fact]
    public async Task A_call_for_a_paused_app_answers_paused_at_once_and_runs_after_the_Resume()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);

        var paused = await Call(engine, "pc_set_value", SetValue("hello")).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PcStatus.WaitingForUser, paused.Status);
        Assert.Equal(PcWaitReason.Paused, paused.Reason);
        Assert.Equal(0, harness.WaitClock.NowMs);
        Assert.Equal("", app.Root.Children[0].Value);
        harness.Pause.Resume(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        var done = await Call(engine, "pc_wait", Resume(paused.Token));
        Assert.True(done.Ok, done.Render());
        Assert.Equal("hello", app.Root.Children[0].Value);
        Assert.NotNull(done.Resumed);
        Assert.True(done.Resumed!.Reread);
        Assert.Contains("resumed: {\"after_ms\":", done.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_waiting_for_its_approval_answers_paused_within_100_ms_and_the_answer_given_while_paused_is_kept()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = Call(engine, "pc_set_value", SetValue("kept"));
        await PcTestWait.Until(() => harness.Approver.Asked.Count == 2);

        var watch = Stopwatch.StartNew();
        harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        var paused = await call.WaitAsync(TimeSpan.FromSeconds(5));
        watch.Stop();

        Assert.Equal(PcWaitReason.Paused, paused.Reason);
        Assert.InRange(watch.ElapsedMilliseconds, 0, 100);
        Assert.Equal(0, harness.WaitClock.NowMs);
        // The card stays answerable: the answer is kept, and the step waits for the Resume.
        harness.Approver.Hold.SetResult(PcApproval.Chat);
        await Task.Delay(100);
        Assert.Equal("", app.Root.Children[0].Value);
        harness.Pause.Resume(PcScope.App(app.Info.App.Key), PcPauseSource.Row);
        var done = await Call(engine, "pc_wait", Resume(paused.Token));
        Assert.True(done.Ok, done.Render());
        Assert.Equal("kept", app.Root.Children[0].Value);
        Assert.Equal(2, harness.Approver.Asked.Count);
    }

    [Fact]
    public async Task Held_steps_keep_their_order_and_nothing_is_dropped()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);

        var first = await Call(engine, "pc_set_value", SetValue("one"));
        var second = await Call(engine, "pc_set_value", SetValue("two"));
        Assert.Equal(PcWaitReason.Paused, first.Reason);
        Assert.Equal(PcWaitReason.Paused, second.Reason);

        harness.Pause.Resume(PcScope.All, PcPauseSource.Chord);
        var a = await Call(engine, "pc_wait", Resume(first.Token));
        var b = await Call(engine, "pc_wait", Resume(second.Token));
        Assert.True(a.Ok, a.Render());
        Assert.True(b.Ok, b.Render());
        Assert.Equal("two", app.Root.Children[0].Value);
    }

    [Fact]
    public async Task With_Pause_all_on_an_app_that_starts_work_starts_paused_and_the_list_says_so()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var classic = harness.Classic();
        using var engine = harness.Engine();
        harness.Pause.Pause(PcScope.All, PcPauseSource.Tray);

        var look = await Call(engine, "pc_observe", "{\"window\":\"w:2c\",\"screenshot\":\"no\"}");
        var list = await Call(engine, "pc_list_apps", "{}");

        Assert.Equal(PcWaitReason.Paused, look.Reason);
        Assert.Contains("exe legacy.exe approved: chat, state: paused", list.Body, StringComparison.Ordinal);
        Assert.Contains("exe notepad.exe approved: none, state: idle", list.Body, StringComparison.Ordinal);
        harness.Pause.Resume(PcScope.All, PcPauseSource.Tray);
        var seen = await Call(engine, "pc_wait", Resume(look.Token, "w:2c"));
        Assert.True(seen.Ok, seen.Render());
        Assert.False(harness.Pause.IsPaused(classic.Info.App.Key));
    }

    [Fact]
    public async Task A_guarded_action_answered_while_paused_asks_again_after_the_Resume_and_the_list_shows_it_working()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        harness.Classic();
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        app.Root.Children[1].Name = "Send";
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Sent";
        harness.Desktop.Bump(0x1a);
        await Call(engine, "pc_observe", Look);
        harness.Confirmer.Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var click = Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        await PcTestWait.Until(() => harness.Confirmer.Asked.Count == 1);

        var list = await Call(engine, "pc_list_apps", "{}");
        Assert.Contains("exe notepad.exe approved: chat, state: working", list.Body, StringComparison.Ordinal);
        Assert.Contains("exe legacy.exe approved: none, state: idle", list.Body, StringComparison.Ordinal);
        harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        var paused = await click.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcWaitReason.Paused, paused.Reason);
        // No action is in flight while the card waits: the pill reads "Paused".
        Assert.Equal(PcPausePhase.Paused, harness.Pause.Status(app.Info.App.Key)!.Phase);
        harness.Confirmer.Hold.SetResult(true);
        await Task.Delay(100);
        Assert.Single(harness.Confirmer.Asked);
        Assert.NotEqual("Sent", app.Title);

        harness.Pause.Resume(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        var done = await Call(engine, "pc_wait", Resume(paused.Token));
        Assert.True(done.Ok, done.Render());
        Assert.Equal(2, harness.Confirmer.Asked.Count);
        Assert.Equal("Sent", app.Title);
    }

    [Fact]
    public async Task At_the_Resume_a_window_that_closed_is_window_gone()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        var paused = await Call(engine, "pc_set_value", SetValue("x"));

        harness.Desktop.Close(0x1a);
        harness.Pause.Resume(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        var done = await Call(engine, "pc_wait", Resume(paused.Token));

        Assert.Equal(PcErrorCode.WindowGone, done.Error);
        Assert.Contains("closed while Tactus was paused", done.Reason, StringComparison.Ordinal);
        Assert.NotNull(done.Resumed);
    }

    [Fact]
    public async Task At_the_Resume_an_app_removed_from_the_standing_grants_is_not_approved()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        harness.Approver.Standings[("notepad.exe", PcCapability.See)] = PcApproval.Always;
        harness.Approver.Standings[("notepad.exe", PcCapability.Act)] = PcApproval.Always;
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        var paused = await Call(engine, "pc_set_value", SetValue("x"));

        harness.Approver.Standings.Remove(("notepad.exe", PcCapability.Act));
        harness.Pause.Resume(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        var done = await Call(engine, "pc_wait", Resume(paused.Token));

        Assert.Equal(PcErrorCode.NotApproved, done.Error);
        Assert.Equal("", app.Root.Children[0].Value);
    }

    [Fact]
    public async Task A_point_from_a_look_before_the_pause_is_stale_and_says_how_long_the_pause_was()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        var shot = await Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"yes\"}");
        Assert.True(shot.Ok, shot.Render());
        harness.Desktop.Clock.NowMs += 1_000;
        harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        harness.Desktop.Clock.NowMs += 240_000;
        harness.Pause.Resume(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);

        var click = await Call(engine, "pc_click", "{\"window\":\"w:1a\",\"point\":[50,30],\"obs\":\"" + shot.Window!.Obs + "\"}");

        Assert.Equal(PcErrorCode.StaleObs, click.Error);
        Assert.Equal("paused 4 min; observe again", click.Reason);
    }

    [Fact]
    public async Task A_target_that_changed_while_paused_stops_the_step()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        harness.Desktop.Clock.NowMs += 1_000;
        harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        app.Root.Children[0].Value = "typed by the user";
        harness.Desktop.Bump(0x1a);
        harness.Pause.Resume(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);

        var set = await Call(engine, "pc_set_value", SetValue("x"));

        Assert.Equal(PcErrorCode.ExpectFailed, set.Error);
        Assert.StartsWith("changed while paused: ", set.Reason, StringComparison.Ordinal);
        Assert.Equal("typed by the user", app.Root.Children[0].Value);
    }

    [Fact]
    public async Task Typing_holds_between_64_character_runs_and_goes_on_when_the_typed_part_reads_back_intact()
    {
        var (harness, field, engine) = await Typing();
        using var _ = engine;
        var text = new string('a', 100) + new string('b', 100);
        harness.Desktop.OnMessage = (app, what) =>
        {
            if (what.StartsWith("wm_char", StringComparison.Ordinal) && !harness.Pause.IsPaused(app.Info.App.Key) && harness.Pause.Epoch(app.Info.App.Key) == 0)
                harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Chord);
        };

        var paused = await Call(engine, "pc_type", "{\"window\":\"w:2c\",\"target\":{\"name\":\"Notes\"},\"text\":\"" + text + "\"}").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcWaitReason.Paused, paused.Reason);
        Assert.Equal(64, field.Value!.Length);
        harness.Pause.Resume(PcScope.All, PcPauseSource.Chord);
        var done = await Call(engine, "pc_wait", Resume(paused.Token, "w:2c"));

        Assert.True(done.Ok, done.Render());
        Assert.Equal(text, field.Value);
        Assert.Equal(4, harness.Desktop.Log.Count(l => l.StartsWith("wm_char", StringComparison.Ordinal)));
        Assert.NotNull(done.Resumed);
    }

    [Fact]
    public async Task Typing_stops_when_the_field_changed_while_paused_and_says_how_much_was_typed()
    {
        var (harness, field, engine) = await Typing();
        using var _ = engine;
        harness.Desktop.OnMessage = (app, what) =>
        {
            if (what.StartsWith("wm_char", StringComparison.Ordinal) && harness.Pause.Epoch(app.Info.App.Key) == 0)
                harness.Pause.Pause(PcScope.App(app.Info.App.Key), PcPauseSource.Pill);
        };

        var paused = await Call(engine, "pc_type", "{\"window\":\"w:2c\",\"target\":{\"name\":\"Notes\"},\"text\":\"" + new string('c', 200) + "\"}").WaitAsync(TimeSpan.FromSeconds(5));
        field.Value += " and the user's words";
        harness.Pause.Resume(PcScope.All, PcPauseSource.Pill);
        var done = await Call(engine, "pc_wait", Resume(paused.Token, "w:2c"));

        Assert.Equal(PcErrorCode.ExpectFailed, done.Error);
        Assert.Equal("changed while paused: typed 64 of 200 characters; look again before typing the rest", done.Reason);
        Assert.Equal(1, harness.Desktop.Log.Count(l => l.StartsWith("wm_char", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Stop_while_paused_ends_the_held_step_and_clears_the_pause()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        harness.Pause.Pause(PcScope.All, PcPauseSource.Pill);
        var paused = await Call(engine, "pc_set_value", SetValue("x"));

        harness.Control.Stop(PcScope.All, PcStopSource.Pill);
        var done = await Call(engine, "pc_wait", Resume(paused.Token));

        Assert.Equal(PcErrorCode.StoppedByUser, done.Error);
        Assert.Empty(harness.Pause.PausedApps);
        Assert.False(harness.Pause.Latched);
        Assert.Equal("", app.Root.Children[0].Value);
    }

    [Fact]
    public async Task Pausing_while_an_action_runs_then_Paused_and_the_next_batch_step_holds()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        using var engine = harness.Engine();
        await Call(engine, "pc_observe", Look);
        var key = app.Info.App.Key;
        PcPausePhase? during = null;
        harness.Desktop.BeforePerform = (_, _, _) =>
        {
            if (harness.Pause.IsPaused(key)) return;
            harness.Pause.Pause(PcScope.App(key), PcPauseSource.Pill);
            during = harness.Pause.Status(key)!.Phase;
        };

        var batch = await Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"actions\":[{\"tool\":\"pc_set_value\",\"element\":2,\"value\":\"first\"},{\"tool\":\"pc_click\",\"element\":4}]}").WaitAsync(TimeSpan.FromSeconds(5));
        harness.Desktop.BeforePerform = null;
        await PcTestWait.Until(() => harness.Pause.Status(key)!.Phase == PcPausePhase.Paused);

        // The action in flight was not cut; the next step held.
        Assert.Equal(PcPausePhase.Pausing, during);
        Assert.Equal(PcWaitReason.Paused, batch.Reason);
        Assert.Equal("first", app.Root.Children[0].Value);
        Assert.Equal(PcPausePhase.Paused, harness.Pause.Status(key)!.Phase);
        Assert.False(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        harness.Pause.Resume(PcScope.App(key), PcPauseSource.Pill);
        var done = await Call(engine, "pc_wait", Resume(batch.Token));
        Assert.True(done.Ok, done.Render());
        Assert.True(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        Assert.NotNull(done.Resumed);
    }

    // The classic dialog with a Unicode control that takes text only as WM_CHAR (no Value pattern, not a stock Edit).
    private static async Task<(PcEngineHarness Harness, PcFakeNode Field, PcControlEngine Engine)> Typing()
    {
        var harness = new PcEngineHarness();
        var app = harness.Classic();
        var field = new PcFakeNode("1." + PcEngineHarness.Id(0x2c) + ".5", "edit", "Notes")
        {
            Actions = PcActions.Focus,
            ClassName = "Scintilla",
            Framework = "Win32",
            NativeWindow = 0x2c + 0x300,
            Value = "",
            Bounds = new PcRect(app.Frame.X + 20, app.Frame.Y + 160, 300, 24),
        };
        app.Root.Add(field);
        var engine = harness.Engine();
        var look = await Call(engine, "pc_observe", "{\"window\":\"w:2c\",\"screenshot\":\"no\"}");
        Assert.True(look.Ok, look.Render());
        return (harness, field, engine);
    }
}
