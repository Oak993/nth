using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests;

public sealed class MCPCatalogResolutionTests
{
    [Fact] // Deliberately preserve the source's nonempty "Bearer " quirk.
    [Trait("Req", "PR-MCP-005")]
    public void Context7_without_key_still_emits_the_Bearer_header()
    {
        var server = MCPResolver.Resolve(MCPCatalog.Entries.Single(e => e.Id == "context7"), ValueMap<string, string>.Empty, _ => throw new InvalidOperationException());
        Assert.Equal("Bearer ", server.Headers["Authorization"]);
    }
}
