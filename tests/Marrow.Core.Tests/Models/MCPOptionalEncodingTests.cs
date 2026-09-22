using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

public sealed class MCPOptionalEncodingTests
{
    [Fact] // Swift Optional.none encodes null; required-value decoding still rejects null.
    public void An_absent_optional_connection_encodes_JSON_null() =>
        Assert.Equal("null", Encoding.UTF8.GetString(Coding.Encode<MCPConnection?>(null)));

    [Fact]
    public void An_absent_optional_tool_encodes_JSON_null() =>
        Assert.Equal("null", Encoding.UTF8.GetString(Coding.Encode<MCPToolSummary?>(null)));
}
