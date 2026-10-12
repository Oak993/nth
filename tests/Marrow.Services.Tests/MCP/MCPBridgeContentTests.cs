using System.Text.Json;
using Marrow.Services.Tests.Fixtures.McpBridge;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-3). The bridge Pi imports used to turn every non-text
// result item into "[image]". It still answers call() as one string for a consumer that wants a string, and now also
// answers callContent() as content blocks, so a picture reaches the provider as a picture; a tool whose
// _meta.ui.visibility leaves out "model" is not offered to the model. Driven against the real Node.js binary and the
// real installed bridge file, with a scripted stand-in server (Fixtures/McpBridge/fake-rich-server.mjs).
[Trait("Decision", "ED-NEW-web-w18-3")]
public sealed class MCPBridgeContentTests
{
    private static NodeHarness.ToolSummary Tool(string name) =>
        McpBridgeScenarios.Rich.Tools.Single(tool => tool.Name == name);

    private static JsonElement[] Blocks(string name) => Tool(name).CallContent!.Value.EnumerateArray().ToArray();

    [Fact]
    public void An_app_only_tool_is_not_offered_to_the_model()
    {
        var names = McpBridgeScenarios.Rich.Tools.Select(tool => tool.Name).ToArray();
        Assert.Equal(["shown", "structure", "links", "failing"], names);
        Assert.Equal("", McpBridgeScenarios.Rich.Stderr);
    }

    [Fact]
    public void The_string_answer_is_what_it_always_was()
    {
        // call() keeps its contract: one string, any non-text item as "[image]".
        Assert.Equal("the chart\n[image]\n[image]", Tool("shown").CallResult);
        Assert.Equal("Error: boom\n[image]", Tool("failing").CallResult);
    }

    [Fact]
    public void An_image_with_data_comes_through_as_an_image_block_next_to_the_text()
    {
        var blocks = Blocks("shown");
        Assert.Equal(3, blocks.Length);
        Assert.Equal(("text", "the chart"), (blocks[0].GetProperty("type").GetString(), blocks[0].GetProperty("text").GetString()));
        Assert.Equal("image", blocks[1].GetProperty("type").GetString());
        Assert.Equal("iVBORw0KGgo=", blocks[1].GetProperty("data").GetString());
        Assert.Equal("image/png", blocks[1].GetProperty("mimeType").GetString());
        // An image the server sent without data cannot be shown: it stays the placeholder.
        Assert.Equal(("text", "[image]"), (blocks[2].GetProperty("type").GetString(), blocks[2].GetProperty("text").GetString()));
    }

    [Fact]
    public void A_result_with_only_structured_content_is_answered_as_that_content()
    {
        var blocks = Blocks("structure");
        var only = Assert.Single(blocks);
        Assert.Equal("text", only.GetProperty("type").GetString());
        Assert.Equal("""{"answer":42}""", only.GetProperty("text").GetString());
    }

    [Fact]
    public void Links_and_embedded_resources_are_text_and_an_embedded_app_page_is_not_shown_to_the_model()
    {
        var texts = Blocks("links").Select(block => block.GetProperty("text").GetString()!).ToArray();
        Assert.Equal(
            ["[resource: file:///report.pdf]", "[resource: file:///inline.txt]\ninline words", "[resource: ui://board/view.html]", "[audio]"],
            texts);
    }

    [Fact]
    public void A_failing_tool_is_answered_as_one_error_text_block_without_its_images()
    {
        var only = Assert.Single(Blocks("failing"));
        Assert.Equal("text", only.GetProperty("type").GetString());
        Assert.StartsWith("Error: boom", only.GetProperty("text").GetString(), StringComparison.Ordinal);
    }
}
