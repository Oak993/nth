using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "sends ui/notifications/tool-input-partial while arguments stream, then
// tool-input and tool-result"; 13.1 "partial input"). What an app hears of the call it draws, read off the thread's ToolCall:
// the stage, the arguments (a streaming object closed where it stops), and the result as an MCP CallToolResult.
[Trait("Decision", "ED-NEW-web-w19-1")]
public sealed class McpAppToolDataTests
{
    private static ToolCall Call(string? detail, ToolCall.Status status = ToolCall.Status.Running, string output = "", ToolPayload? payload = null) =>
        new() { ToolCallKind = ToolCall.Kind.Mcp, Title = "shipyard · list_releases", Detail = detail, ToolCallStatus = status, Output = output, Payload = payload };

    [Theory]
    [InlineData("{\"env\":\"stag", "{\"env\":\"stag\"}")]
    [InlineData("{\"env\":\"staging\",", "{\"env\":\"staging\"}")]
    [InlineData("{\"env\":\"staging\",\"limit\"", "{\"env\":\"staging\"}")]
    [InlineData("{\"env\":\"staging\",\"limit\":", "{\"env\":\"staging\"}")]
    [InlineData("{\"filter\":{\"tags\":[\"a\",\"b", "{\"filter\":{\"tags\":[\"a\",\"b\"]}}")]
    [InlineData("{\"note\":\"line \\\"quoted", "{\"note\":\"line \\\"quoted\"}")]
    [InlineData("{\"n\":12", "{\"n\":12}")]
    [InlineData("{", "{}")]
    public void A_streaming_object_closes_where_it_stops(string partial, string expected)
    {
        var parsed = McpAppToolData.PartialArguments(partial);
        Assert.NotNull(parsed);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), parsed), parsed!.ToJsonString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[1,2")]
    [InlineData("not json")]
    public void Text_that_is_no_object_gives_no_arguments(string? partial) => Assert.Null(McpAppToolData.PartialArguments(partial));

    [Fact]
    public void The_stage_follows_the_call()
    {
        Assert.Equal(McpAppToolStage.None, McpAppToolData.Read(Call(null)).Stage);
        var partial = McpAppToolData.Read(Call("{\"env\":\"st"));
        Assert.Equal(McpAppToolStage.Partial, partial.Stage);
        Assert.Equal("st", partial.Arguments!["env"]!.GetValue<string>());
        var input = McpAppToolData.Read(Call("{\"env\":\"staging\"}"));
        Assert.Equal(McpAppToolStage.Input, input.Stage);
        Assert.Equal(McpAppToolStage.Result, McpAppToolData.Read(Call("{}", ToolCall.Status.Completed)).Stage);
        Assert.Equal(McpAppToolStage.Result, McpAppToolData.Read(Call("{}", ToolCall.Status.Failed)).Stage);
        Assert.Equal(McpAppToolStage.Cancelled, McpAppToolData.Read(Call("{}", ToolCall.Status.Declined)).Stage);
        // A finished call never reads its text as a partial object.
        Assert.Null(McpAppToolData.Read(Call("{\"env\":\"st", ToolCall.Status.Completed)).Arguments);
    }

    [Fact]
    public void The_result_carries_text_images_links_structured_content_and_meta()
    {
        var payload = new ToolPayload
        {
            Server = "shipyard",
            Tool = "list_releases",
            Images = new ValueList<ToolImage>([new ToolImage { MimeType = "image/png", Data = "iVBORw0KGgo=" }]),
            ResourceLinks = new ValueList<ToolResourceLink>([new ToolResourceLink { Uri = "https://shipyard.dev/r/241", Name = "v2.4.1", MimeType = "text/html" }]),
            StructuredContent = "{\"releases\":[{\"version\":\"v2.4.1\"}]}",
            Meta = "{\"ui\":{\"resourceUri\":\"ui://shipyard/release-board.html\"}}",
        };
        var result = McpAppToolData.Result(Call("{}", ToolCall.Status.Completed, "3 releases", payload));
        var content = result["content"]!.AsArray();
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("3 releases", content[0]!["text"]!.GetValue<string>());
        Assert.Equal("image", content[1]!["type"]!.GetValue<string>());
        Assert.Equal("resource_link", content[2]!["type"]!.GetValue<string>());
        Assert.Equal("v2.4.1", content[2]!["name"]!.GetValue<string>());
        Assert.False(result["isError"]!.GetValue<bool>());
        Assert.Equal("v2.4.1", result["structuredContent"]!["releases"]![0]!["version"]!.GetValue<string>());
        Assert.Equal("ui://shipyard/release-board.html", result["_meta"]!["ui"]!["resourceUri"]!.GetValue<string>());

        var failed = McpAppToolData.Result(Call("{}", ToolCall.Status.Failed, "boom"));
        Assert.True(failed["isError"]!.GetValue<bool>());
        Assert.False(failed.ContainsKey("structuredContent"));
    }

    [Fact]
    public void A_declined_call_is_cancelled_with_a_reason()
    {
        Assert.Equal("The user declined the call.", McpAppToolData.CancelReason(Call("{}", ToolCall.Status.Declined)));
        Assert.Equal("The call was stopped.", McpAppToolData.CancelReason(Call("{}")));
    }
}
