using System.Text;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MCPCatalogBraveTests
{
    [Fact]
    [Trait("Req", "PR-MCP-011")] // Core/Models/MCPCatalog.swift:140-156; Core/Models/MCP.swift:34,78-100.
    public void Brave_search_matches_the_complete_1_7_0_catalog_record()
    {
        // Frozen from the Swift entry, including MCPField's isRequired=true default and needsInput.
        // Compare every text field as UTF-8 bytes directly (R-TEST-4), without JSON equivalence.
        var entry = Assert.Single(MCPCatalog.Entries, e => e.Id == "brave-search");
        AssertText("brave-search", entry.Id);
        AssertText("Brave Search", entry.Name);
        AssertText("Brave", entry.Vendor);
        AssertText("Searches the web, local places and news", entry.Summary);
        Assert.Equal(MCPCategory.Search, entry.Category);
        AssertText("mcp-brave-search", entry.Asset);
        Assert.Equal(0xFB542Bu, entry.ColorValue);
        Assert.False(entry.IsMonochrome);
        var transport = Assert.IsType<MCPTransport.Stdio>(entry.Transport);
        AssertText("npx", transport.Command);
        Assert.Equal(2, transport.Args.Count);
        AssertText("-y", transport.Args[0]);
        AssertText("@brave/brave-search-mcp-server", transport.Args[1]);
        var field = Assert.Single(entry.Fields);
        AssertText("key", field.Key);
        AssertText("API key", field.Label);
        AssertText("BSA\U00002026", field.Placeholder); // U+2026 HORIZONTAL ELLIPSIS
        Assert.Equal(MCPField.Kind.Secret, field.FieldKind);
        AssertText("Make a key on the page, then copy it", field.Help!);
        Assert.Null(field.DefaultValue);
        Assert.True(field.IsRequired);
        var environment = Assert.Single(entry.Env);
        AssertText("BRAVE_API_KEY", environment.Key);
        AssertText("{key}", environment.Value);
        AssertText("https://github.com/brave/brave-search-mcp-server", entry.DocsURL);
        Assert.Equal(3, entry.SampleTools.Count);
        AssertText("brave_web_search", entry.SampleTools[0]);
        AssertText("brave_local_search", entry.SampleTools[1]);
        AssertText("brave_news_search", entry.SampleTools[2]);
        AssertText("https://api-dashboard.search.brave.com/app/keys", entry.KeysURL!);
        Assert.True(entry.NeedsInput);
        Assert.False(entry.IsOAuth);
    }

    private static void AssertText(string expected, string actual) =>
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
