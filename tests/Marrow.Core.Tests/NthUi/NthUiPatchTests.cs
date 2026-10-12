using System.Text.Json.Nodes;
using Marrow.Core.NthUi;

namespace Marrow.Core.Tests.NthUi;

// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3 "ui_update {id, patch}: set state or replace nodes by id";
// ED-NEW-web-w24-4). A patch becomes the view's new text; the text stays the one source of truth.
[Trait("Decision", "ED-NEW-web-w24-4")]
public sealed class NthUiPatchTests
{
    private const string View =
        "card \"Hosting estimate\" icon=server\n"
        + "  state instances = 3\n"
        + "  state region = \"eu\"\n"
        + "  let price = region == \"eu\" ? 0.046 : 0.05\n"
        + "  stepper instances label=\"Instances\" min=1 max=24\n"
        + "  text \"About three instances.\" id=summary\n"
        + "  table cols=\"Item,Monthly\" id=costs\n"
        + "    row \"Compute\" = fmt(instances * price * 730, \"$0\")\n"
        + "    row total \"Total\" = fmt(instances * price * 730, \"$0\")\n"
        + "  code id=note\n"
        + "    line one\n"
        + "\n"
        + "    line two\n"
        + "followups\n"
        + "  chip \"Add a CDN\"\n";

    private static JsonObject Patch(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static NthUiPatchResult Apply(string json) => NthUiPatch.Apply(View, Patch(json));

    [Fact]
    public void A_node_is_replaced_in_place_and_the_new_lines_take_its_indent()
    {
        var result = Apply("{\"nodes\":{\"summary\":\"text \\\"Now it is five.\\\" id=summary\"}}");
        Assert.False(result.Failed, result.Error);
        Assert.Contains("  text \"Now it is five.\" id=summary\n  table", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("About three", result.Text, StringComparison.Ordinal);
        Assert.Equal(["replaced summary"], result.Done);
        Assert.True(NthUiParser.Parse(result.Text!).IsClean);
    }

    [Fact]
    public void A_node_with_children_is_replaced_whole_and_a_code_body_goes_with_its_blank_lines()
    {
        var table = Apply("{\"nodes\":{\"costs\":\"table cols=\\\"A\\\" id=costs\\n  row \\\"x\\\" = 1\"}}");
        Assert.DoesNotContain("Compute", table.Text, StringComparison.Ordinal);
        Assert.Contains("  table cols=\"A\" id=costs\n    row \"x\" = 1\n  code id=note", table.Text, StringComparison.Ordinal);

        var code = Apply("{\"nodes\":{\"note\":\"\"}}");
        Assert.Equal(["removed note"], code.Done);
        Assert.DoesNotContain("line one", code.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("line two", code.Text, StringComparison.Ordinal);
        Assert.Contains("      row total \"Total\"".TrimStart(), code.Text, StringComparison.Ordinal);
        Assert.Contains("followups\n  chip \"Add a CDN\"", code.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_are_appended_at_the_end_of_a_node_one_level_in()
    {
        var result = Apply("{\"append\":{\"costs\":\"row \\\"Backup\\\" = 4\"}}");
        Assert.False(result.Failed, result.Error);
        Assert.Contains("    row total \"Total\" = fmt(instances * price * 730, \"$0\")\n    row \"Backup\" = 4\n  code id=note", result.Text, StringComparison.Ordinal);
        Assert.True(NthUiParser.Parse(result.Text!).IsClean);
    }

    [Fact]
    public void State_is_set_by_name_for_declared_states_only()
    {
        var result = Apply("{\"state\":{\"instances\":5,\"region\":\"us\"}}");
        Assert.False(result.Failed, result.Error);
        Assert.Equal(View.TrimEnd('\n'), result.Text!.TrimEnd('\n'));
        Assert.Equal("{\"instances\":5,\"region\":\"us\"}", result.StateSets);
        Assert.Equal(["set instances", "set region"], result.Done);

        var let = Apply("{\"state\":{\"price\":1}}");
        Assert.True(let.Failed);
        Assert.Contains("\"price\" is not a state", let.Error, StringComparison.Ordinal);
        Assert.Contains("instances, region", let.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_edits_run_in_order_and_an_edit_may_use_an_id_an_earlier_one_made()
    {
        var result = Apply("{\"nodes\":{\"summary\":\"callout \\\"Careful\\\" tone=warn id=warning\"},\"append\":{\"costs\":\"row \\\"Backup\\\" = 4\"}}");
        Assert.False(result.Failed, result.Error);
        Assert.Equal(["replaced summary", "added to costs"], result.Done);

        var chained = NthUiPatch.Apply(View, Patch("{\"nodes\":{\"summary\":\"text \\\"a\\\" id=first\"}}"));
        var again = NthUiPatch.Apply(chained.Text!, Patch("{\"nodes\":{\"first\":\"text \\\"b\\\" id=first\"}}"));
        Assert.Contains("text \"b\" id=first", again.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_patch_that_cannot_apply_says_why_and_lists_what_it_could_have_used()
    {
        var missing = Apply("{\"nodes\":{\"nope\":\"text \\\"x\\\"\"}}");
        Assert.True(missing.Failed);
        Assert.Contains("No node has id=\"nope\"", missing.Error, StringComparison.Ordinal);
        Assert.Contains("summary, costs, note", missing.Error, StringComparison.Ordinal);
        Assert.Null(missing.Text);

        Assert.Contains("state, nodes or append", Apply("{\"oops\":1}").Error, StringComparison.Ordinal);
        Assert.Contains("changes nothing", Apply("{}").Error, StringComparison.Ordinal);
        Assert.Contains("must be an object", Apply("{\"nodes\":[1]}").Error, StringComparison.Ordinal);
        Assert.Contains("must be text", Apply("{\"nodes\":{\"summary\":5}}").Error, StringComparison.Ordinal);
        Assert.Contains("no lines to add", Apply("{\"append\":{\"costs\":\"\"}}").Error, StringComparison.Ordinal);

        var plain = NthUiPatch.Apply("card \"No ids\"\n  text \"a\"\n", Patch("{\"nodes\":{\"x\":\"text \\\"b\\\"\"}}"));
        Assert.Contains("gives no node an id", plain.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_patch_is_read_from_an_object_or_from_text_holding_one_and_fences_in_it_are_dropped()
    {
        Assert.NotNull(NthUiPatch.Read(JsonNode.Parse("{\"state\":{}}")));
        Assert.NotNull(NthUiPatch.Read(JsonValue.Create("{\"state\":{\"a\":1}}")));
        Assert.Null(NthUiPatch.Read(JsonValue.Create("not json")));
        Assert.Null(NthUiPatch.Read(JsonValue.Create("")));
        Assert.Null(NthUiPatch.Read(null));
        Assert.Null(NthUiPatch.Read(JsonNode.Parse("[1]")));

        var fenced = Apply("{\"nodes\":{\"summary\":\"```nth-ui\\ntext \\\"in a fence\\\" id=summary\\n```\"}}");
        Assert.Contains("  text \"in a fence\" id=summary\n", fenced.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("```", fenced.Text, StringComparison.Ordinal);
        Assert.Equal("a\nb", NthUiPatch.Clean("```nth-ui\na\nb\n```"));
    }

    [Fact]
    public void A_view_is_named_by_its_first_card_or_section_or_heading()
    {
        Assert.Equal("Hosting estimate", NthUiPatch.TitleOf(NthUiParser.Parse(View)));
        Assert.Equal("Notes", NthUiPatch.TitleOf(NthUiParser.Parse("heading \"Notes\"\ntext \"a\"\n")));
        Assert.Null(NthUiPatch.TitleOf(NthUiParser.Parse("text \"a\"\n")));
        Assert.EndsWith("…", NthUiPatch.TitleOf(NthUiParser.Parse("card \"" + new string('w', 100) + "\"\n")), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_sample_view_survives_a_replace_of_each_node_that_has_an_id_without_breaking_the_rest()
    {
        // Replacing a node by its own text changes nothing.
        var node = NthUiParser.Parse(View).Nodes.Single(n => n.Id == "costs");
        var lines = View.Split('\n');
        var own = string.Join('\n', lines.Skip(node.Line).Take(3));
        var result = Apply("{\"nodes\":{\"costs\":" + JsonValue.Create(own)!.ToJsonString() + "}}");
        Assert.Equal(View.TrimEnd('\n'), result.Text!.TrimEnd('\n'));
    }
}
