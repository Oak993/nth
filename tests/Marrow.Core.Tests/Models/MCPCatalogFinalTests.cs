using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models;

public sealed class MCPCatalogFinalTests
{
    [Fact]
    [Trait("Req", "PR-MCP-031")]
    [Trait("Req", "PR-MCP-032")]
    [Trait("Req", "PR-MCP-033")]
    public void Last_three_entries_match_every_expected_field()
    {
        const string fixture = """
        [["huggingface","Hugging Face","Hugging Face","Searches models, datasets and papers","knowledge","mcp-huggingface","FFB000",false,"http","https://huggingface.co/mcp",[],{"Authorization":"Bearer {token}"},[["token","Access token","hf_…","Secret","Optional. Raises the limits",null,false]],{},"https://github.com/huggingface/hf-mcp-server",["hub_repo_search","hub_repo_details","hf_fs","hf_whoami"],"https://huggingface.co/settings/tokens",false,false],
          ["deepwiki","DeepWiki","Cognition","Reads docs and answers questions about repos","knowledge","mcp-deepwiki","1D1D1F",true,"http","https://mcp.deepwiki.com/mcp",[],{},[],{},"https://docs.devin.ai/work-with-devin/deepwiki-mcp",["read_wiki_structure","read_wiki_contents","ask_question"],null,false,false],
          ["aws-docs","AWS Docs","Amazon Web Services","Searches and reads the AWS documentation","cloud","mcp-aws-docs","FF9900",false,"stdio","uvx",["awslabs.aws-documentation-mcp-server@latest"],{},[],{"FASTMCP_LOG_LEVEL":"ERROR"},"https://awslabs.github.io/mcp/servers/aws-documentation-mcp-server",["read_documentation","search_documentation","recommend"],null,false,false]
        ]
        """;
        MCPCatalogTests.AssertSnapshot(fixture, MCPCatalog.Entries.Skip(30));
    }

    [Fact]
    [Trait("Req", "PR-MCP-034")]
    public void Catalog_has_exactly_33_ids_in_source_order_and_the_fourteen_OAuth_entries()
    {
        const string ids = "github,gitlab,playwright,chrome-devtools,context7,filesystem,fetch,memory,sequential-thinking,git,brave-search,tavily,exa,firecrawl,perplexity,slack,notion,linear,atlassian,figma,sentry,vercel,cloudflare,netlify,supabase,stripe,resend,postgres,sqlite,mongodb,huggingface,deepwiki,aws-docs";
        Assert.Equal(33, MCPCatalog.Entries.Count); Assert.Equal(ids.Split(','), MCPCatalog.Ids);
        Assert.Equal(ids.Split(','), MCPCatalog.Entries.Select(entry => entry.Id));
        Assert.Equal("gitlab,tavily,exa,notion,linear,atlassian,figma,sentry,vercel,cloudflare,netlify,supabase,stripe,resend".Split(','), MCPCatalog.Entries.Where(entry => entry.IsOAuth).Select(entry => entry.Id));
    }

    [Fact]
    [Trait("Req", "PR-MCP-035")]
    public void Exact_id_lookup_is_case_sensitive_and_returns_null_for_unknown_ids()
    {
        foreach (var entry in MCPCatalog.Entries) Assert.Equal(entry, MCPCatalog.Entry(entry.Id));
        Assert.Null(MCPCatalog.Entry("GitHub")); Assert.Null(MCPCatalog.Entry("missing")); Assert.Null(MCPCatalog.Entry(" github"));
    }

    [Theory] // Corrected from executable source before test approval.
    [Trait("Req", "PR-MCP-036")]
    [InlineData("Chrome DevTools", "chrome-devtools")]
    [InlineData("CHROME_DEVTOOLS", "chrome-devtools")]
    [InlineData("chrome-devtools", "chrome-devtools")]
    [InlineData("Hugging Face", "huggingface")]
    [InlineData("hugging_face", "huggingface")]
    [InlineData("huggingface", "huggingface")]
    [InlineData("PostgreSQL", "postgres")]
    [InlineData("AWS Docs", "aws-docs")]
    [InlineData(" Hugging Face", null)]
    [InlineData("Hugging  Face", null)]
    [InlineData("chrome.devtools", null)]
    [InlineData("chrome\tdevtools", null)]
    [InlineData("", null)]
    public void Normalized_lookup_uses_only_lowercase_underscore_and_space_replacement(string value, string? id) =>
        Assert.Equal(id, MCPCatalog.EntryMatching(value)?.Id);

    [Fact]
    [Trait("Req", "PR-MCP-037")]
    public void Categories_and_their_members_preserve_both_source_orders()
    {
        var groups = MCPCatalog.ByCategory;
        // R-ENUM-1: never Enum.GetValues on a ported Swift enum; use SwiftEnum<T>.AllCases.
        Assert.Equal(SwiftEnum<MCPCategory>.AllCases, groups.Select(group => group.Category));
        string[] expected = ["github,gitlab,filesystem,git,sentry", "playwright,chrome-devtools,fetch,firecrawl", "brave-search,tavily,exa,perplexity", "slack,notion,linear,atlassian,figma,stripe,resend", "supabase,postgres,sqlite,mongodb", "vercel,cloudflare,netlify,aws-docs", "context7,memory,sequential-thinking,huggingface,deepwiki"];
        Assert.Equal(expected, groups.Select(group => string.Join(',', group.Entries.Select(entry => entry.Id))));
        Assert.All(groups, group => Assert.NotEmpty(group.Entries));
    }
}
