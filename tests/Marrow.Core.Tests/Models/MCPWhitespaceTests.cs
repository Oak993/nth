using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/MCP.swift (B2 1fbf81e), :141 value.trimmed (Text.swift:132,
// trimmingCharacters(in: .whitespacesAndNewlines)): Foundation's set, which String.Trim() does not match (R-STR-7).
public sealed class MCPWhitespaceTests
{
    [Fact]
    [Trait("Req", "PR-MCP-040")] // MCP.swift:141 value.trimmed before the scheme loop and the trailing-slash loop
    public void Host_placeholder_trims_Foundation_whitespace_and_zero_width_space_before_removing_scheme_and_slash()
    {
        var entry = new MCPCatalogEntry(Id: "test", Name: "test", Vendor: "test", Summary: "test", Category: MCPCategory.Developer,
            Asset: "", ColorValue: 0, Transport: new MCPTransport.Oauth("https://{host}/mcp"), DocsURL: "")
        {
            Fields = new ValueList<MCPField>([new MCPField(Key: "host", Label: "host", Placeholder: "")]),
        };
        // U+200B ZERO WIDTH SPACE is in Foundation's whitespace set.
        var values = new ValueMap<string, string>([KeyValuePair.Create("host", "\U0000200B\nhttps://example.com/\t\U0000200B")], SwiftStringComparer.Instance);
        Assert.Equal("https://example.com/mcp", entry.OauthURL(values));
    }
}
