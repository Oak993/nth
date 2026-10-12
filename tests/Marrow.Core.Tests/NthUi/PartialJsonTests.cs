using System.Text.Json;
using Marrow.Core.NthUi;

namespace Marrow.Core.Tests.NthUi;

// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.2 "streamed line by line"; ED-NEW-web-w24-3). A model that calls
// ui_show streams its arguments as JSON; the view inside is read before the call ends, from text that may stop anywhere.
[Trait("Decision", "ED-NEW-web-w24-3")]
public sealed class PartialJsonTests
{
    private const string Full = "{\"id\":\"hosting\",\"ui\":\"card \\\"Hosting\\\"\\n  text \\\"3 \\u00d7 \\\\ x\\\"\\n\",\"height\":320,\"note\":{\"a\":[1,\"}\"]},\"lite\":true}";

    [Fact]
    public void A_string_comes_back_as_far_as_it_has_arrived_and_is_unfinished_until_its_closing_quote()
    {
        var json = Full[..(Full.IndexOf("text", StringComparison.Ordinal) + 4)];
        var (text, complete) = PartialJson.String(json, "ui")!.Value;
        Assert.Equal("card \"Hosting\"\n  text", text);
        Assert.False(complete);
        Assert.Equal("hosting", PartialJson.String(json, "id")!.Value.Text);
        Assert.True(PartialJson.String(json, "id")!.Value.Complete);
    }

    [Fact]
    public void The_whole_object_reads_escapes_numbers_and_literals_and_skips_nested_members()
    {
        var members = PartialJson.Members(Full);
        Assert.Equal("card \"Hosting\"\n  text \"3 \u00d7 \\ x\"\n", members["ui"].Value);
        Assert.True(members["ui"].Complete);
        Assert.Equal(("320", false, true), (members["height"].Value, members["height"].IsString, members["height"].Complete));
        Assert.Equal("true", members["lite"].Value);
        Assert.False(members.ContainsKey("note"));
        Assert.Equal(320, PartialJson.Integer(Full, "height"));
        Assert.Null(PartialJson.Integer(Full, "id"));
    }

    [Fact]
    public void A_cut_escape_is_left_out_and_every_prefix_agrees_with_the_whole_where_it_has_arrived()
    {
        var whole = PartialJson.String(Full, "ui")!.Value.Text;
        var previous = "";
        for (var cut = 0; cut <= Full.Length; cut++)
        {
            var seen = PartialJson.String(Full[..cut], "ui");
            if (seen is null) continue;
            Assert.StartsWith(seen.Value.Text, whole, StringComparison.Ordinal);
            Assert.True(seen.Value.Text.Length >= previous.Length, "a prefix never shows less than a shorter one");
            previous = seen.Value.Text;
        }
        Assert.Equal(whole, previous);
        Assert.Equal("a", PartialJson.String("{\"ui\":\"a\\", "ui")!.Value.Text);
        Assert.Equal("a", PartialJson.String("{\"ui\":\"a\\u00", "ui")!.Value.Text);
    }

    [Fact]
    public void A_member_is_found_only_at_the_top_level_and_an_unfinished_number_is_not_complete()
    {
        Assert.Null(PartialJson.String("{\"outer\":{\"ui\":\"inner\"}}", "ui"));
        Assert.Null(PartialJson.Integer("{\"height\":32", "height"));
        Assert.Equal(32, PartialJson.Integer("{\"height\":32}", "height"));
        Assert.Empty(PartialJson.Members("[1,2]"));
        Assert.Empty(PartialJson.Members("not json"));
        Assert.Empty(PartialJson.Members(null));
        Assert.Empty(PartialJson.Members("{\"ui"));
        Assert.Equal("second", PartialJson.String("{\"ui\":\"first\",\"ui\":\"second\"}", "ui")!.Value.Text);
    }

    [Fact]
    public void A_finished_object_agrees_with_the_system_parser_for_every_string_member()
    {
        var random = new Random(7);
        const string alphabet = "ab \n\t\"\\/\u00e9\u4e2d{}[],:";
        for (var round = 0; round < 200; round++)
        {
            var text = new string([.. Enumerable.Range(0, random.Next(0, 40)).Select(_ => alphabet[random.Next(alphabet.Length)])]);
            var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["x"] = 1, ["ui"] = text, ["y"] = new[] { text } });
            var (read, complete) = PartialJson.String(json, "ui")!.Value;
            Assert.Equal(text, read);
            Assert.True(complete);
        }
    }
}
