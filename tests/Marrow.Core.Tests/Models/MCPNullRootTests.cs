using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

public sealed class MCPNullRootTests
{
    [Theory] // Both Swift values require a keyed container, never a null struct.
    [Trait("Req", "PR-MCP-047")]
    [InlineData(true, "null")]
    [InlineData(false, "null")]
    [InlineData(true, "[]")]
    [InlineData(false, "[]")]
    public void A_nonobject_root_cannot_decode_as_a_required_Swift_value(bool connection, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (connection) Assert.Throws<JsonException>(() => Coding.Decode<MCPConnection>(bytes));
        else Assert.Throws<JsonException>(() => Coding.Decode<MCPToolSummary>(bytes));
    }
}
