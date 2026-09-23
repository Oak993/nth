using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/MCP.swift (B2 1fbf81e), :173-177 and :188-199: both synthesized Decodable values
// need a keyed container; a null, array, string or number root throws (R-CODE-1).
public sealed class MCPNullRootTests
{
    [Theory]
    [Trait("Req", "PR-MCP-047")] // MCP.swift:188-199 MCPConnection and :173-177 MCPToolSummary decode from an object only
    [InlineData(true, "null")]
    [InlineData(false, "null")]
    [InlineData(true, "[]")]
    [InlineData(false, "[]")]
    [InlineData(true, "\"x\"")]
    [InlineData(false, "1")]
    public void A_nonobject_root_cannot_decode_as_a_required_Swift_value(bool connection, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (connection) Assert.ThrowsAny<JsonException>(() => Coding.Decode<MCPConnection>(bytes));
        else Assert.ThrowsAny<JsonException>(() => Coding.Decode<MCPToolSummary>(bytes));
    }
}
