using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MCPWhitespaceTests
{
    [Fact] // Foundation whitespace differs from String.Trim().
    [Trait("Req", "PR-MCP-040")]
    public void Host_placeholder_trims_Foundation_zero_width_space_before_removing_scheme_and_slash()
    {
        var entry = new MCPCatalogEntry("test", "test", "test", "test", MCPCategory.Developer,
            "", 0, new MCPTransport.OAuth("https://{host}/mcp"), "")
        {
            Fields = [new MCPField("host", "host", "")]
        };
        Assert.Equal("https://example.com/mcp", entry.OAuthUrl(
            new Dictionary<string, string> { ["host"] = "\u200B\nhttps://example.com/\t\u200B" }));
    }
}
