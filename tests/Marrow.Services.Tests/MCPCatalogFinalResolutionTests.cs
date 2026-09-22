using Marrow.Core.Models;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests;

public sealed class MCPCatalogFinalResolutionTests
{
    [Fact] // PR-MCP-031..033
    public void Final_entries_preserve_empty_headers_optional_Bearer_and_literal_log_level()
    {
        MCPResolvedServer Resolve(string id) => MCPResolver.Resolve(MCPCatalog.Entry(id)!, new Dictionary<string,string>(), _ => throw new InvalidOperationException());
        Assert.Equal("Bearer ", Resolve("huggingface").Headers["Authorization"]);
        Assert.Empty(Resolve("deepwiki").Headers);
        Assert.Equal("ERROR", Resolve("aws-docs").Env["FASTMCP_LOG_LEVEL"]);
    }
}
