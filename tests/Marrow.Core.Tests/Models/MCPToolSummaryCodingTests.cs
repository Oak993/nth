using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

public sealed class MCPToolSummaryCodingTests
{
    [Theory] // Synthesized Codable name is required even outside MCPConnection.
    [Trait("Req", "PR-MCP-047")]
    [Trait("Rule", "R-CODE-1")]
    [InlineData("{}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":1}")]
    [InlineData("{\"name\":\"tool\",\"summary\":false}")]
    public void Standalone_tool_summary_rejects_missing_or_wrongly_typed_required_name(string json) =>
        Assert.Throws<JsonException>(() => Coding.Decode<MCPToolSummary>(Encoding.UTF8.GetBytes(json)));

    [Fact]
    [Trait("Req", "PR-MCP-047")]
    public void Standalone_tool_summary_preserves_optional_summary_and_omits_computed_id()
    {
        Assert.Equal(new MCPToolSummary("tool", null), Coding.Decode<MCPToolSummary>("{\"name\":\"tool\"}"u8));
        Assert.Equal(new MCPToolSummary("tool", null), Coding.Decode<MCPToolSummary>("{\"name\":\"tool\",\"summary\":null}"u8));
        Assert.Equal(new MCPToolSummary("tool", "Description"), Coding.Decode<MCPToolSummary>("{\"name\":\"tool\",\"summary\":\"Description\"}"u8));
        Assert.Equal("{\"name\":\"tool\"}", Encoding.UTF8.GetString(Coding.Encode(new MCPToolSummary("tool", null))));
    }
}
