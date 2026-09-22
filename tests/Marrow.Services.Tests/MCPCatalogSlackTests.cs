using Marrow.Core.Models;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests;

public sealed class MCPCatalogSlackTests
{
    [Fact] // PR-MCP-016: the literal flag survives an omitted token.
    public void Slack_message_tool_flag_is_literal_and_token_placeholder_is_optional_at_resolution()
    {
        var entry = MCPCatalog.Entries.Single(e => e.Id == "slack");
        var absent = MCPResolver.Resolve(entry, new Dictionary<string,string>(), _ => throw new InvalidOperationException());
        Assert.Equal("true", Assert.Single(absent.Env).Value); Assert.False(absent.Env.ContainsKey("SLACK_MCP_XOXB_TOKEN"));
        var present = MCPResolver.Resolve(entry, new Dictionary<string,string> { ["token"] = "fixture-only" }, _ => throw new InvalidOperationException());
        Assert.Equal("fixture-only", present.Env["SLACK_MCP_XOXB_TOKEN"]); Assert.Equal("true", present.Env["SLACK_MCP_ADD_MESSAGE_TOOL"]);
    }
}
