using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, ED-NEW-pcctl-7; DESIGN.md 2.2, 2.12). Strict arguments, the target and
// expect shapes, and the batch rules that run before any step (critique C26, C29).
[Trait("Decision", "ED-NEW-pcctl-7")]
public sealed class PcArgsTests
{
    private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

    private static JsonArray Steps(string text) => (JsonArray)JsonNode.Parse(text)!;

    [Fact]
    public void Wrong_types_are_refused_never_guessed()
    {
        var args = Json("{\"text\":5,\"submit\":\"yes\",\"count\":1.5,\"element\":\"7\",\"repeat\":3.0,\"amount\":2}");
        Assert.Throws<PcArgsException>(() => PcArgs.Str(args, "text"));
        Assert.Throws<PcArgsException>(() => PcArgs.Bool(args, "submit", false));
        Assert.Throws<PcArgsException>(() => PcArgs.Int(args, "count"));
        Assert.Throws<PcArgsException>(() => PcArgs.Int(args, "element"));
        Assert.Equal(3, PcArgs.Int(args, "repeat"));
        Assert.Equal(2.0, PcArgs.Num(args, "amount"));
        Assert.Null(PcArgs.Str(args, "missing"));
        Assert.True(PcArgs.Bool(args, "missing", true));
        Assert.Throws<PcArgsException>(() => PcArgs.Pair(JsonNode.Parse("[1]"), "point"));
        Assert.Equal((3.0, 4.5), PcArgs.Pair(JsonNode.Parse("[3, 4.5]"), "point"));
        Assert.Throws<PcArgsException>(() => PcArgs.Region(Json("{\"region\":[0,0,0,10]}")));
        Assert.Equal((1.0, 2.0, 30.0, 40.0), PcArgs.Region(Json("{\"region\":[1,2,30,40]}")));
    }

    [Fact]
    public void A_target_needs_a_name_a_type_or_an_id_and_nth_counts_from_one()
    {
        Assert.Equal(new PcLocator("Send", "button", null, "Reply", 2), PcArgs.Locator(Json("{\"target\":{\"name\":\"Send\",\"type\":\"button\",\"within\":\"Reply\",\"nth\":2}}")));
        Assert.Null(PcArgs.Locator(Json("{}")));
        Assert.Throws<PcArgsException>(() => PcArgs.Locator(Json("{\"target\":{\"within\":\"Reply\"}}")));
        Assert.Throws<PcArgsException>(() => PcArgs.Locator(Json("{\"target\":{\"name\":\"Send\",\"nth\":0}}")));
        Assert.Throws<PcArgsException>(() => PcArgs.Locator(Json("{\"target\":\"Send\"}")));
    }

    [Fact]
    public void An_expect_reads_every_condition_and_a_bind_needs_the_window_it_names()
    {
        var expect = PcArgs.Expect(Json("{\"expect\":{\"value\":\"42\",\"checked\":true,\"window_opens\":{\"title\":\"Save As\"},\"bind\":\"$dlg\",\"gone\":\"Saving\"}}"))!;
        Assert.Equal("42", expect.Value);
        Assert.True(expect.Checked);
        Assert.Equal(new PcWindowMatch("Save As", null), expect.WindowOpens);
        Assert.Equal("$dlg", expect.Bind);
        Assert.Equal("Saving", expect.Gone);
        Assert.Throws<PcArgsException>(() => PcArgs.Expect(Json("{\"expect\":{\"bind\":\"$dlg\",\"appears\":\"x\"}}")));
        Assert.Throws<PcArgsException>(() => PcArgs.Expect(Json("{\"expect\":{\"window_opens\":{},\"bind\":\"dlg\"}}")));
        Assert.Throws<PcArgsException>(() => PcArgs.Expect(Json("{\"expect\":{}}")));
        Assert.True(PcArgs.IsName("$total"));
        Assert.False(PcArgs.IsName("$5"));
        Assert.False(PcArgs.IsName("total"));
    }

    [Fact]
    public void A_step_that_later_steps_depend_on_must_carry_an_expect()
    {
        var bare = Steps("[{\"tool\":\"pc_click\",\"element\":4},{\"tool\":\"pc_type\",\"element\":9,\"text\":\"hi\"}]");
        Assert.Contains("step 1 (pc_click)", PcBatchPlan.Problem(bare), StringComparison.Ordinal);
        var checkedFirst = Steps("[{\"tool\":\"pc_click\",\"element\":4,\"expect\":{\"appears\":\"Subject\"}},{\"tool\":\"pc_type\",\"element\":9,\"text\":\"hi\"}]");
        Assert.Null(PcBatchPlan.Problem(checkedFirst));
        // Fields filled one after another need no expect (each reads back), and the last step depends on nothing after it.
        var form = Steps("[{\"tool\":\"pc_set_value\",\"element\":4,\"value\":\"a\"},{\"tool\":\"pc_type\",\"element\":5,\"text\":\"b\"},{\"tool\":\"pc_click\",\"target\":{\"name\":\"Save\"}}]");
        Assert.Null(PcBatchPlan.Problem(form));
        var submit = Steps("[{\"tool\":\"pc_type\",\"element\":5,\"text\":\"q\",\"submit\":true},{\"tool\":\"pc_read\",\"element\":8}]");
        Assert.Contains("needs an expect", PcBatchPlan.Problem(submit), StringComparison.Ordinal);
        var focus = Steps("[{\"tool\":\"pc_action\",\"element\":5,\"action\":\"scroll_into_view\"},{\"tool\":\"pc_click\",\"element\":5}]");
        Assert.Null(PcBatchPlan.Problem(focus));
    }

    [Fact]
    public void Steps_in_other_windows_do_not_depend_on_a_step_unless_it_opened_their_window()
    {
        var two = Steps("[{\"tool\":\"pc_click\",\"window\":\"w:10\",\"element\":4},{\"tool\":\"pc_set_value\",\"window\":\"w:20\",\"element\":4,\"value\":\"x\"}]");
        Assert.Null(PcBatchPlan.Problem(two));
        var dialog = Steps("[{\"tool\":\"pc_click\",\"element\":4,\"expect\":{\"window_opens\":{\"title\":\"Save As\"},\"bind\":\"$dlg\"}},{\"tool\":\"pc_set_value\",\"window\":\"$dlg\",\"target\":{\"name\":\"File name\"},\"value\":\"a.txt\"}]");
        Assert.Null(PcBatchPlan.Problem(dialog));
        var early = Steps("[{\"tool\":\"pc_set_value\",\"window\":\"$dlg\",\"target\":{\"name\":\"File name\"},\"value\":\"a.txt\"},{\"tool\":\"pc_read\",\"element\":3}]");
        Assert.Contains("before a step's expect window_opens binds it", PcBatchPlan.Problem(early), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_then_type_binds_text_and_an_unbound_name_is_refused()
    {
        var flow = Steps("[{\"tool\":\"pc_read\",\"window\":\"w:10\",\"element\":7,\"bind\":\"$total\"},{\"tool\":\"pc_set_value\",\"window\":\"w:20\",\"element\":3,\"value\":\"$total\"}]");
        Assert.Null(PcBatchPlan.Problem(flow));
        var unbound = Steps("[{\"tool\":\"pc_read\",\"element\":7},{\"tool\":\"pc_type\",\"element\":3,\"text\":\"$total\"}]");
        Assert.Contains("no earlier pc_read binds it", PcBatchPlan.Problem(unbound), StringComparison.Ordinal);
        var wrongBinder = Steps("[{\"tool\":\"pc_set_value\",\"element\":7,\"value\":\"x\",\"bind\":\"$v\"},{\"tool\":\"pc_read\",\"element\":3}]");
        Assert.Contains("only a pc_read step binds text", PcBatchPlan.Problem(wrongBinder), StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_holds_two_to_fifty_known_steps()
    {
        Assert.NotNull(PcBatchPlan.Problem(Steps("[{\"tool\":\"pc_read\",\"element\":1}]")));
        var fifty = new JsonArray();
        for (var i = 0; i < 51; i++) fifty.Add(new JsonObject { ["tool"] = "pc_read", ["element"] = 1 });
        Assert.NotNull(PcBatchPlan.Problem(fifty));
        fifty.RemoveAt(0);
        Assert.Null(PcBatchPlan.Problem(fifty));
        Assert.Contains("must name one of", PcBatchPlan.Problem(Steps("[{\"tool\":\"pc_launch_app\"},{\"tool\":\"pc_read\",\"element\":1}]")), StringComparison.Ordinal);
        Assert.Contains("window must be", PcBatchPlan.Problem(Steps("[{\"tool\":\"pc_read\",\"window\":\"notepad\"},{\"tool\":\"pc_read\",\"element\":1}]")), StringComparison.Ordinal);
    }
}
