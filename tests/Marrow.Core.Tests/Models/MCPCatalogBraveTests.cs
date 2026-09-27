using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MCPCatalogBraveTests
{
    [Fact]
    [Trait("Req", "PR-MCP-011")] // Core/Models/MCPCatalog.swift:140-156; Core/Models/MCP.swift:34,78-96.
    public void Brave_search_matches_the_complete_1_7_0_catalog_record()
    {
        // Frozen from the Swift entry, including MCPField's isRequired=true default and needsInput.
        const string fixture = """
        [["brave-search","Brave Search","Brave","Searches the web, local places and news","search","mcp-brave-search","FB542B",false,"stdio","npx",["-y","@brave/brave-search-mcp-server"],{},[["key","API key","BSA\u2026","Secret","Make a key on the page, then copy it",null,true]],{"BRAVE_API_KEY":"{key}"},"https://github.com/brave/brave-search-mcp-server",["brave_web_search","brave_local_search","brave_news_search"],"https://api-dashboard.search.brave.com/app/keys",true,false]]
        """;
        MCPCatalogTests.AssertSnapshot(fixture, [Assert.Single(MCPCatalog.Entries, e => e.Id == "brave-search")]);
    }
}
