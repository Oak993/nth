using Marrow.Core.Models;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests;

public sealed class MCPCatalogResolutionTests
{
    [Fact] // PR-MCP-005: deliberately preserve the source's nonempty "Bearer " quirk.
    public void Context7_without_key_still_emits_the_Bearer_header()
    {
        var server = MCPResolver.Resolve(MCPCatalog.Entries.Single(e => e.Id == "context7"), new Dictionary<string,string>(), _ => throw new InvalidOperationException());
        Assert.Equal("Bearer ", server.Headers["Authorization"]);
    }
}
