using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests;

public sealed class MCPCatalogSlackTests
{
    [Fact] // The literal flag survives an omitted token.
    [Trait("Req", "PR-MCP-016")]
    public void Slack_message_tool_flag_is_literal_and_token_placeholder_is_optional_at_resolution()
    {
        var entry = MCPCatalog.Entries.Single(e => e.Id == "slack");
        var absent = MCPResolver.Resolve(entry, ValueMap<string, string>.Empty, _ => throw new InvalidOperationException());
        Assert.Equal("true", Assert.Single(absent.Env).Value); Assert.False(absent.Env.ContainsKey("SLACK_MCP_XOXB_TOKEN"));
        var present = MCPResolver.Resolve(entry, new ValueMap<string, string>(new Dictionary<string, string> { ["token"] = "fixture-only" }), _ => throw new InvalidOperationException());
        Assert.Equal("fixture-only", present.Env["SLACK_MCP_XOXB_TOKEN"]); Assert.Equal("true", present.Env["SLACK_MCP_ADD_MESSAGE_TOOL"]);
    }
}
