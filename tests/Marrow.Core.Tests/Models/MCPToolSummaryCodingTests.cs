using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/MCP.swift (B2 1fbf81e), :173-177 struct MCPToolSummary: Codable with name and summary.
public sealed class MCPToolSummaryCodingTests
{
    [Theory] // Synthesized Codable: name is required even outside MCPConnection.
    [Trait("Req", "PR-MCP-047")] // MCP.swift:174 var name: String (required), :175 var summary: String? (decodeIfPresent)
    [Trait("Rule", "R-CODE-1")]
    [InlineData("{}")]
    [InlineData("{\"summary\":\"s\"}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":1}")]
    [InlineData("{\"name\":\"tool\",\"summary\":false}")]
    [InlineData("{\"name\":\"tool\",\"summary\":{}}")]
    public void Standalone_tool_summary_rejects_missing_or_wrongly_typed_fields(string json) =>
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<MCPToolSummary>(Encoding.UTF8.GetBytes(json)));

    [Fact]
    [Trait("Req", "PR-MCP-047")] // MCP.swift:173-177 keys "name" and "summary"; nil summary omitted; id is computed, not coded
    public void Standalone_tool_summary_preserves_optional_summary_and_omits_computed_id()
    {
        Assert.Equal(new MCPToolSummary(Name: "tool", Summary: null), Coding.Decode<MCPToolSummary>("{\"name\":\"tool\"}"u8));
        Assert.Equal(new MCPToolSummary(Name: "tool", Summary: null), Coding.Decode<MCPToolSummary>("{\"name\":\"tool\",\"summary\":null}"u8));
        Assert.Equal(new MCPToolSummary(Name: "tool", Summary: "Description"), Coding.Decode<MCPToolSummary>("{\"name\":\"tool\",\"summary\":\"Description\"}"u8));
        Assert.Equal("{\"name\":\"tool\"}", Encoding.UTF8.GetString(Coding.Encode(new MCPToolSummary(Name: "tool", Summary: null))));
        Assert.Equal("{\"name\":\"tool\",\"summary\":\"Description\"}", Encoding.UTF8.GetString(Coding.Encode(new MCPToolSummary(Name: "tool", Summary: "Description"))));
        Assert.Equal("tool", new MCPToolSummary(Name: "tool", Summary: "Description").Id);
    }
}
