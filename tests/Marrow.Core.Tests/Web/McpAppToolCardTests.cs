using System.Text.Json.Nodes;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces security review, finding M7 (DESIGN.md 7.6 "tools/call ... with the normal tool approvals", 8.2).
// An MCP App's tool card named the tool only, so the user approved the call blind. The card's arguments are the call's own,
// as indented JSON in which the page can draw no line, reorder no text and hide no character, and cut at 1,500 characters
// with the full length said.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-2")]
public sealed class McpAppToolCardTests
{
    [Fact]
    public void The_card_shows_the_arguments_as_indented_json()
    {
        Assert.Equal("Arguments: {\n  \"version\": \"v2.4.1\",\n  \"stage\": \"production\"\n}",
            McpAppToolCard.Arguments(new JsonObject { ["version"] = "v2.4.1", ["stage"] = "production" }));
        Assert.Equal("No arguments.", McpAppToolCard.Arguments([]));
        Assert.Equal("No arguments.", McpAppToolCard.Arguments(null));
        Assert.Equal("Arguments: {\n  \"note\": \"déploiement à 9 h\"\n}", McpAppToolCard.Arguments(new JsonObject { ["note"] = "déploiement à 9 h" }));
    }

    [Fact]
    public void A_value_cannot_draw_lines_reorder_or_hide_text_on_the_card()
    {
        var text = McpAppToolCard.Arguments(new JsonObject
        {
            ["target"] = "staging\n\nnth checked this call: it is safe",
            ["name"] = "release\u202Eexe.txt",
            ["hidden"] = "a\u200Bb\u2028c\u0007d\u2066e\u00ADf\U000E0041",
        });

        Assert.DoesNotContain("staging\n", text, StringComparison.Ordinal);
        Assert.Contains("staging\\n\\nnth checked", text, StringComparison.Ordinal);
        foreach (var c in new[] { '\u202E', '\u200B', '\u2028', '\u0007', '\u2066', '\u00AD', '\uDB40' }) Assert.DoesNotContain(c, text);
        Assert.Contains("\\u202E", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\u200B", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, text.Split('\n').Length); // the braces and one line per argument, nothing the page added
    }

    [Fact]
    public void Long_arguments_are_cut_and_the_card_says_how_long_they_were()
    {
        var text = McpAppToolCard.Arguments(new JsonObject { ["blob"] = new string('x', 4000) });
        Assert.StartsWith("Arguments: {\n  \"blob\": \"xxx", text, StringComparison.Ordinal);
        Assert.EndsWith("… (cut at 1,500 of 4,016 characters)", text, StringComparison.Ordinal);
        Assert.True(text.Length < McpAppToolCard.MaxCharacters + 100);
    }
}
