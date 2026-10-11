using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl.Accuracy;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.9, 2.12, 5.5 accuracy suite; critique C13, C26, C31).
// Timing and the user: dialogs that open late, a modal dialog inside an Invoke, three apps at once with one hung, the user
// typing or editing in the window Tactus works in, changes that happen on their own, and Send, Delete and Pay buttons in
// Hindi, Urdu and Arabic. Every variant must give the right result: 100%, with no write into the user's control and no
// step acting on what the model saw before the user changed it.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcAccuracyTimingTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    private static async Task<(PcEngineHarness Harness, PcFakeApp App, PcControlEngine Engine)> Ready()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        return (harness, app, engine);
    }

    // The user is at work in the window now: it is in front and has just had their input.
    private static void UserWorking(PcEngineHarness harness)
    {
        harness.Desktop.Clock.NowMs += 500;
        harness.Desktop.ForegroundWindow = 0x1a;
        harness.Desktop.LastInputAt = harness.Desktop.Clock.NowMs;
    }

    [Theory]
    [InlineData(300)]
    [InlineData(800)]
    [InlineData(1500)]
    [InlineData(2500)]
    public async Task A_dialog_that_opens_up_to_two_and_a_half_seconds_after_an_invoke_is_seen_and_stops_a_batch(int delayMs)
    {
        var (_, app, engine) = await Ready();
        app.Root.Children[1].OnInvoke = (desktop, _) => desktop.Schedule(delayMs, () => desktop.AddApp(0x5e, "Save as", "notepad.exe", 101));
        var single = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        Assert.True(single.Ok, single.Render());
        Assert.Contains("new window: w:5e \"Save as\"", single.Body, StringComparison.Ordinal);
        var (later, laterApp, laterEngine) = await Ready();
        laterApp.Root.Children[1].OnInvoke = (desktop, _) => desktop.Schedule(delayMs, () => desktop.AddApp(0x5f, "Confirm", "notepad.exe", 101));
        var batch = await PcEngineHarness.Call(laterEngine, "pc_batch",
            "{\"window\":\"w:1a\",\"actions\":[{\"tool\":\"pc_click\",\"element\":3,\"expect\":{\"name\":\"Save\"}},{\"tool\":\"pc_set_value\",\"element\":2,\"value\":\"x\"}]}");
        Assert.Equal(PcErrorCode.ExpectFailed, batch.Error);
        Assert.Contains("a window opened that the step did not expect: w:5f \"Confirm\"", batch.Reason, StringComparison.Ordinal);
        Assert.Contains("2. pc_set_value w:1a: not run", batch.Body, StringComparison.Ordinal);
        Assert.Equal("", laterApp.Root.Children[0].Value);
        Assert.DoesNotContain(later.Desktop.Log, l => l.StartsWith("setvalue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_invoke_held_by_a_modal_dialog_returns_and_the_dialog_is_driven_next()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Add(new PcFakeNode("1.26.5", "button", "Options") { Actions = PcActions.Invoke, Modal = true, Bounds = new PcRect(240, 110, 80, 30) });
        app.Root.Children[3].OnInvoke = (desktop, _) => desktop.Schedule(300, () =>
        {
            var options = desktop.AddApp(0x5e, "Options", "notepad.exe", 101, new PcRect(300, 300, 500, 300));
            options.Root.Add(new PcFakeNode("1.94.2", "button", "OK") { Actions = PcActions.Invoke, Bounds = new PcRect(600, 540, 80, 30), OnInvoke = (d, _) => d.Close(0x5e) });
        });
        harness.Desktop.Bump(0x1a, structure: true);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        var opened = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":5}");
        Assert.True(opened.Ok, opened.Render());
        Assert.Contains("the app is still running the action", opened.Body, StringComparison.Ordinal);
        Assert.Contains("new window: w:5e \"Options\"", opened.Body, StringComparison.Ordinal);
        var dialog = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:5e\",\"screenshot\":\"no\"}");
        Assert.True(dialog.Ok, dialog.Render());
        Assert.Contains("button \"OK\"", dialog.Body, StringComparison.Ordinal);
        var closed = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:5e\",\"element\":2}");
        Assert.True(closed.Ok, closed.Render());
        Assert.Null(harness.Desktop.Window(0x5e));
    }

    [Fact]
    public async Task Three_apps_at_once_with_one_hung_the_others_go_on_in_their_own_order()
    {
        var harness = new PcEngineHarness();
        var hung = harness.Notepad();
        var second = harness.Notepad(0x2b, 202, "wordpad.exe");
        var third = harness.Notepad(0x3c, 303, "write.exe");
        var engine = harness.Engine();
        foreach (var window in new[] { "w:1a", "w:2b", "w:3c" })
            Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"" + window + "\",\"screenshot\":\"no\"}")).Ok);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hung.Hang = release;
        var stuck = PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");
        async Task<PcResult[]> Drive(string window, string tag)
        {
            var results = new List<PcResult>();
            for (var n = 1; n <= 3; n++)
                results.Add(await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"" + window + "\",\"element\":2,\"value\":\"" + tag + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}"));
            return results.ToArray();
        }
        var done = await Task.WhenAll(Drive("w:2b", "b"), Drive("w:3c", "c")).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(done.SelectMany(r => r), r => Assert.True(r.Ok, r.Render()));
        // Nothing in the other two apps waited for the hung one.
        Assert.False(stuck.IsCompleted);
        Assert.Equal("b3", second.Root.Children[0].Value);
        Assert.Equal("c3", third.Root.Children[0].Value);
        List<string> log;
        lock (harness.Desktop.Log) log = harness.Desktop.Log.ToList();
        foreach (var tag in new[] { "b", "c" })
        {
            var at = Enumerable.Range(1, 3).Select(n => log.IndexOf("setvalue Text editor = " + tag + n.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            Assert.True(at[0] >= 0 && at[0] < at[1] && at[1] < at[2], string.Join(", ", at));
        }
        release.SetResult();
        var late = await stuck.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(late.Ok, late.Render());
        Assert.True(hung.Root.Children[2].States.HasFlag(PcStates.Checked));
    }

    [Fact]
    public async Task Text_the_user_typed_after_the_look_stops_the_step_and_a_fresh_look_lets_it_run()
    {
        var (harness, app, engine) = await Ready();
        var field = app.Root.Children[0];
        field.Value = "hello";
        field.States |= PcStates.Focused;
        UserWorking(harness);
        harness.Desktop.Bump(0x1a);
        var typed = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\" world\"}");
        Assert.Equal(PcErrorCode.ExpectFailed, typed.Error);
        Assert.Contains("the user changed edit \"Text editor\" since your look: it now shows \"hello\"", typed.Reason, StringComparison.Ordinal);
        Assert.Equal("hello", field.Value);
        // The user's other controls are not theirs to lose: a step elsewhere in the window runs while they type.
        Assert.True((await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}")).Ok);
        Assert.Equal("hello", field.Value);
        // The user stops; the model looks again and the step runs on what is there now.
        harness.Desktop.Clock.NowMs += 3000;
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        var again = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:1a\",\"element\":2,\"text\":\" world\"}");
        Assert.True(again.Ok, again.Render());
        Assert.Equal("hello world", field.Value);
    }

    [Fact]
    public async Task A_box_the_user_ticked_after_the_look_is_not_clicked_back()
    {
        var (harness, app, engine) = await Ready();
        var box = app.Root.Children[2];
        box.States |= PcStates.Checked;
        UserWorking(harness);
        harness.Desktop.Bump(0x1a);
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");
        Assert.Equal(PcErrorCode.ExpectFailed, click.Error);
        Assert.Contains("the user changed check box \"Word wrap\" since your look: it is now checked", click.Reason, StringComparison.Ordinal);
        Assert.True(box.States.HasFlag(PcStates.Checked));
        Assert.Equal(0, harness.Desktop.Performs);
    }

    [Fact]
    public async Task A_dialog_the_user_opened_after_the_look_stops_the_step()
    {
        var (harness, app, engine) = await Ready();
        harness.Desktop.AddApp(0x5e, "Find", "notepad.exe", 101, new PcRect(300, 300, 400, 200));
        var set = await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:1a\",\"element\":2,\"value\":\"x\"}");
        Assert.Equal(PcErrorCode.ExpectFailed, set.Error);
        Assert.Contains("a window of the app opened since your look at this window: w:5e \"Find\"", set.Reason, StringComparison.Ordinal);
        Assert.Equal("", app.Root.Children[0].Value);
    }

    [Theory]
    [InlineData("clock")]
    [InlineData("animation")]
    [InlineData("typing")]
    public async Task Changes_the_invoke_did_not_cause_give_unverified_never_ok(string noise)
    {
        var (harness, app, engine) = await Ready();
        // Refresh does nothing visible; something else in the window keeps changing.
        app.Root.Add(new PcFakeNode("1.26.5", "button", "Refresh") { Actions = PcActions.Invoke, Bounds = new PcRect(240, 110, 80, 30) });
        var ticker = app.Root.Add(new PcFakeNode("1.26.6", noise == "animation" ? "progress bar" : "text", noise == "animation" ? "Sync" : "Clock")
        {
            Actions = PcActions.None,
            Value = "0",
            Bounds = new PcRect(400, 700, 120, 20),
        });
        harness.Desktop.Bump(0x1a, structure: true);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        var field = app.Root.Children[0];
        if (noise == "typing")
        {
            field.States |= PcStates.Focused;
            harness.Desktop.ForegroundWindow = 0x1a;
            harness.Desktop.Bump(0x1a);
        }
        for (var at = 150; at <= 6000; at += 150)
        {
            var step = at;
            harness.Desktop.Schedule(at, () =>
            {
                if (noise == "typing")
                {
                    field.Value += "a";
                    harness.Desktop.LastInputAt = harness.Desktop.Clock.NowMs;
                }
                else
                {
                    ticker.Value = step.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                harness.Desktop.Bump(0x1a);
            });
        }
        var result = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":5}");
        Assert.Equal(PcStatus.Unverified, result.Status);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task A_focus_move_the_user_made_while_the_invoke_settled_is_not_counted_for_it()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Add(new PcFakeNode("1.26.5", "button", "Refresh") { Actions = PcActions.Invoke, Bounds = new PcRect(240, 110, 80, 30) });
        harness.Desktop.Bump(0x1a, structure: true);
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        harness.Desktop.ForegroundWindow = 0x1a;
        // 60 ms after the click the user clicks into the editor.
        harness.Desktop.Schedule(60, () =>
        {
            app.Root.Children[0].States |= PcStates.Focused;
            harness.Desktop.LastInputAt = harness.Desktop.Clock.NowMs;
            harness.Desktop.Bump(0x1a);
        });
        var result = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":5}");
        // Nothing the click did is left once the user's focus move is set aside: never ok.
        Assert.False(result.Ok, result.Render());
    }

    [Theory]
    [InlineData("हटाएं", "delete", "notepad.exe")]
    [InlineData("भुगतान करें", "pay", "notepad.exe")]
    [InlineData("حذف کریں", "delete", "notepad.exe")]
    [InlineData("ادائیگی کریں", "pay", "notepad.exe")]
    [InlineData("ارسال", "send", "notepad.exe")]
    [InlineData("भेजें", "send", "telegram.exe")]
    [InlineData("", "", "signal.exe")]
    public async Task Localised_and_icon_only_send_delete_and_pay_buttons_raise_the_card(string name, string word, string exe)
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad(exe: exe);
        app.Root.Children[1].Name = name;
        app.Root.Children[1].Shortcut = null;
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        harness.Confirmer.Answer = false;
        var click = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":3}");
        Assert.Equal(PcErrorCode.NeedsConfirmation, click.Error);
        var asked = Assert.Single(harness.Confirmer.Asked);
        Assert.StartsWith(name.Length == 0 ? "press a button with no name" : word + " ", asked, StringComparison.Ordinal);
        Assert.Equal(0, harness.Desktop.Performs);
    }
}
