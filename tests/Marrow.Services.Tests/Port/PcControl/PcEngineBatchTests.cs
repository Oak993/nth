using Marrow.Core.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.2 pc_batch, 2.11; critique C10, C21, C29). pc_batch on
// the fake desktop: every step checked, the first failure stops the batch (or only its window's steps with parallel), a
// step's expect window_opens with bind names the dialog it opened for later steps, a pc_read bind carries text from one
// app into another without sending it to the model, steps that already hold skip the quiet window, and the batch settles
// once at its end.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcEngineBatchTests
{
    private static async Task<(PcEngineHarness Harness, PcFakeApp App, Marrow.Services.Port.PcControl.PcControlEngine Engine)> Ready()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:1a\",\"screenshot\":\"no\"}")).Ok);
        return (harness, app, engine);
    }

    // A form with ten fields [2] to [11] and a Save button [12].
    private static PcFakeApp Form(PcEngineHarness harness)
    {
        var app = harness.Desktop.AddApp(0x3d, "Order", "orders.exe", 303);
        for (var i = 0; i < 10; i++)
        {
            app.Root.Add(new PcFakeNode("1.61." + (i + 2).ToString(System.Globalization.CultureInfo.InvariantCulture), "edit", "Field " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                Actions = PcActions.Value,
                Value = "",
                Bounds = new PcRect(120, 120 + (i * 30), 300, 24),
            });
        }
        return app;
    }

    [Fact]
    public async Task A_batch_fills_a_form_checking_each_step()
    {
        var (_, app, engine) = await Ready();
        var ok = await PcEngineHarness.Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"actions\":[{\"tool\":\"pc_set_value\",\"element\":2,\"value\":\"a\"},{\"tool\":\"pc_click\",\"element\":4,\"expect\":{\"checked\":true}},{\"tool\":\"pc_type\",\"element\":2,\"text\":\"b\",\"expect\":{\"value\":\"ab\"}}]}");
        Assert.True(ok.Ok, ok.Render());
        Assert.Equal("ab", app.Root.Children[0].Value);
        Assert.True(app.Root.Children[2].States.HasFlag(PcStates.Checked));
        Assert.Contains("1. pc_set_value w:1a: ok: ", ok.Body, StringComparison.Ordinal);
        Assert.Contains("2. pc_click w:1a: ok: toggled", ok.Body, StringComparison.Ordinal);
        Assert.Contains("(rung: R1 (set_value))", ok.Body, StringComparison.Ordinal);
        Assert.Contains("w:1a \"Untitled - notepad\":", ok.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dependent_step_without_an_expect_is_refused_before_anything_runs()
    {
        var (harness, _, engine) = await Ready();
        var refused = await PcEngineHarness.Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"actions\":[{\"tool\":\"pc_click\",\"element\":3},{\"tool\":\"pc_set_value\",\"element\":2,\"value\":\"x\"}]}");
        Assert.Equal(PcErrorCode.BadArgs, refused.Error);
        Assert.Contains("needs an expect", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(0, harness.Desktop.Performs);
    }

    [Fact]
    public async Task The_first_mismatch_stops_the_batch_and_says_what_did_not_run()
    {
        var (harness, _, engine) = await Ready();
        var bad = await PcEngineHarness.Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"actions\":[{\"tool\":\"pc_set_value\",\"element\":2,\"value\":\"x\",\"expect\":{\"text_in_window\":\"Saved\"}},{\"tool\":\"pc_set_value\",\"element\":2,\"value\":\"y\"}]}");
        Assert.Equal(PcErrorCode.ExpectFailed, bad.Error);
        Assert.StartsWith("step 1 (pc_set_value): ", bad.Reason, StringComparison.Ordinal);
        Assert.Contains("2. pc_set_value w:1a: not run: an earlier step in its window failed", bad.Body, StringComparison.Ordinal);
        Assert.Contains("1 step did not run", bad.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("setvalue Text editor = y", harness.Desktop.Log);
    }

    [Fact]
    public async Task Steps_whose_check_already_holds_skip_the_quiet_window_and_the_batch_settles_once()
    {
        var harness = new PcEngineHarness();
        var form = Form(harness);
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:3d\",\"screenshot\":\"no\"}")).Ok);
        var steps = string.Join(',', Enumerable.Range(2, 10).Select(n => "{\"tool\":\"pc_set_value\",\"element\":" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"value\":\"v" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}"));
        var start = harness.Desktop.Clock.NowMs;
        var filled = await PcEngineHarness.Call(engine, "pc_batch", "{\"window\":\"w:3d\",\"observe\":\"none\",\"actions\":[" + steps + "]}");
        Assert.True(filled.Ok, filled.Render());
        for (var i = 0; i < 10; i++) Assert.Equal("v" + (i + 2).ToString(System.Globalization.CultureInfo.InvariantCulture), form.Root.Children[i].Value);
        // One quiet window at the end (100 ms after the last change event), not one per step; no fixed sleep anywhere.
        Assert.InRange(harness.Desktop.Clock.NowMs - start, 0, PcFakeDesktop.EventLatencyMs + Marrow.Services.Port.PcControl.PcSettle.QuietMs);
        Assert.Empty(harness.Desktop.Clock.Delays);
    }

    [Fact]
    public async Task With_parallel_a_failure_stops_only_its_own_windows_steps()
    {
        var (harness, app, engine) = await Ready();
        var other = harness.Notepad(0x2b, 202, "wordpad.exe");
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2b\",\"screenshot\":\"no\"}")).Ok);
        app.Root.Children[0].Accepts = v => v.ToUpperInvariant();
        var result = await PcEngineHarness.Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"parallel\":true,\"actions\":[{\"tool\":\"pc_set_value\",\"element\":2,\"value\":\"low\"},{\"tool\":\"pc_click\",\"element\":4,\"expect\":{\"checked\":true}},"
            + "{\"tool\":\"pc_set_value\",\"window\":\"w:2b\",\"element\":2,\"value\":\"one\"},{\"tool\":\"pc_click\",\"window\":\"w:2b\",\"element\":4,\"expect\":{\"checked\":true}}]}");
        Assert.Equal(PcErrorCode.ExpectFailed, result.Error);
        Assert.Contains("2. pc_click w:1a: not run", result.Body, StringComparison.Ordinal);
        Assert.Contains("3. pc_set_value w:2b: ok", result.Body, StringComparison.Ordinal);
        Assert.Contains("4. pc_click w:2b: ok", result.Body, StringComparison.Ordinal);
        Assert.Equal("one", other.Root.Children[0].Value);
        Assert.True(other.Root.Children[2].States.HasFlag(PcStates.Checked));
        Assert.False(app.Root.Children[2].States.HasFlag(PcStates.Checked));
    }

    [Fact]
    public async Task A_dialog_a_step_opened_is_bound_by_name_for_the_steps_after_it()
    {
        var (harness, app, engine) = await Ready();
        PcFakeApp? dialog = null;
        app.Root.Children[1].OnInvoke = (desktop, _) =>
        {
            dialog = desktop.AddApp(0x5e, "Save as", "notepad.exe", 101, new PcRect(300, 300, 600, 400));
            dialog.Root.Add(new PcFakeNode("1.94.2", "edit", "File name") { Actions = PcActions.Value, Value = "", Bounds = new PcRect(320, 350, 400, 24) });
        };
        var result = await PcEngineHarness.Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"actions\":[{\"tool\":\"pc_click\",\"element\":3,\"expect\":{\"window_opens\":{\"title\":\"Save as\"},\"bind\":\"$dlg\"}},"
            + "{\"tool\":\"pc_set_value\",\"window\":\"$dlg\",\"target\":{\"name\":\"File name\"},\"value\":\"report.txt\"}]}");
        Assert.True(result.Ok, result.Render());
        Assert.Equal("report.txt", dialog!.Root.Children[0].Value);
        Assert.Contains("2. pc_set_value $dlg: ok", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_that_did_not_open_its_window_leaves_the_steps_that_use_it_unrun()
    {
        var (harness, app, engine) = await Ready();
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Saved - notepad";
        var result = await PcEngineHarness.Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"actions\":[{\"tool\":\"pc_click\",\"element\":3,\"expect\":{\"window_opens\":{\"title\":\"Save as\"},\"bind\":\"$dlg\"}},"
            + "{\"tool\":\"pc_set_value\",\"window\":\"$dlg\",\"target\":{\"name\":\"File name\"},\"value\":\"report.txt\"}]}");
        Assert.Equal(PcErrorCode.ExpectFailed, result.Error);
        Assert.Contains("2. pc_set_value $dlg: not run", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_then_type_moves_text_between_apps_without_showing_it_to_the_model()
    {
        var (harness, app, engine) = await Ready();
        var other = harness.Notepad(0x2b, 202, "wordpad.exe");
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2b\",\"screenshot\":\"no\"}")).Ok);
        app.Root.Children[0].Value = "Total: 4,250.00 PKR";
        harness.Desktop.Bump(0x1a);
        var result = await PcEngineHarness.Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"observe\":\"none\",\"actions\":[{\"tool\":\"pc_read\",\"element\":2,\"bind\":\"$total\"},{\"tool\":\"pc_type\",\"window\":\"w:2b\",\"element\":2,\"text\":\"$total\"}]}");
        Assert.True(result.Ok, result.Render());
        Assert.Equal("Total: 4,250.00 PKR", other.Root.Children[0].Value);
        Assert.Contains("1. pc_read w:1a: ok: read into $total", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("4,250", result.Body, StringComparison.Ordinal);
    }
}
