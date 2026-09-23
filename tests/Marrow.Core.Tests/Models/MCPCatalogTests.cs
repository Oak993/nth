using System.Text.Json;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MCPCatalogTests
{
    // Frozen from PR-MCP-001..010 Expected text, independently of the implementation table.
    private const string FirstTen = """
    [["github","GitHub","GitHub","Issues, pull requests and code across your repositories","developer","mcp-github","24292F",true,"http","https://api.githubcopilot.com/mcp/",[],{"Authorization":"Bearer {token}"},[["token","Personal access token","ghp_…","Secret","Create a fine-grained token on the page, then copy it",null,true]],{},"https://github.com/github/github-mcp-server",["list_issues","create_pull_request","search_code","get_file_contents"],"https://github.com/settings/personal-access-tokens/new",true,false],
      ["gitlab","GitLab","GitLab","Issues, merge requests, pipelines and code across your projects","developer","mcp-gitlab","FC6D26",false,"oauth","https://{instance}/api/v4/mcp",[],{},[["instance","GitLab address","gitlab.com","Text","gitlab.com, or your own instance's host","gitlab.com",false]],{},"https://docs.gitlab.com/user/model_context_protocol/mcp_server/",["list_merge_requests","get_merge_request_diffs","create_issue","get_pipeline_jobs"],null,false,true],
      ["playwright","Playwright","Microsoft","Drives a real browser: open pages, click, fill forms, screenshot","browser","mcp-playwright","2EAD33",false,"stdio","npx",["-y","@playwright/mcp@latest"],{},[],{},"https://github.com/microsoft/playwright-mcp",["browser_navigate","browser_click","browser_snapshot","browser_take_screenshot"],null,false,false],
      ["chrome-devtools","Chrome DevTools","Google","Inspects pages, console messages and performance traces","browser","mcp-chrome-devtools","4285F4",false,"stdio","npx",["-y","chrome-devtools-mcp@latest"],{},[],{},"https://github.com/ChromeDevTools/chrome-devtools-mcp",["navigate_page","take_screenshot","list_console_messages","performance_start_trace"],null,false,false],
      ["context7","Context7","Upstash","Fresh docs and examples for the libraries you use","knowledge","mcp-context7","5B8DEF",false,"http","https://mcp.context7.com/mcp",[],{"Authorization":"Bearer {key}"},[["key","API key","ctx7sk-…","Secret","Optional. Raises the rate limit",null,false]],{},"https://github.com/upstash/context7",["resolve-library-id","query-docs"],"https://context7.com/dashboard",false,false],
      ["filesystem","Filesystem","Anthropic","Reads, writes and searches files in one folder","developer","mcp-filesystem","8E8E93",false,"stdio","npx",["-y","@modelcontextprotocol/server-filesystem","{root}"],{},[["root","Folder","/Users/you/Projects","Path","The folder the server may use","$HOME_FIXTURE",true]],{},"https://github.com/modelcontextprotocol/servers/tree/main/src/filesystem",["read_text_file","write_file","list_directory","search_files"],null,true,false],
      ["fetch","Fetch","Anthropic","Downloads web pages as plain text to read","browser","mcp-fetch","0A84FF",false,"stdio","uvx",["mcp-server-fetch"],{},[],{},"https://github.com/modelcontextprotocol/servers/tree/main/src/fetch",["fetch"],null,false,false],
      ["memory","Memory","Anthropic","Remembers people, facts and how they connect","knowledge","mcp-memory","AF52DE",false,"stdio","npx",["-y","@modelcontextprotocol/server-memory"],{},[],{},"https://github.com/modelcontextprotocol/servers/tree/main/src/memory",["create_entities","create_relations","search_nodes","read_graph"],null,false,false],
      ["sequential-thinking","Sequential Thinking","Anthropic","Thinks hard problems through step by step","knowledge","mcp-sequential-thinking","30B0C7",false,"stdio","npx",["-y","@modelcontextprotocol/server-sequential-thinking"],{},[],{},"https://github.com/modelcontextprotocol/servers/tree/main/src/sequentialthinking",["sequentialthinking"],null,false,false],
      ["git","Git","Anthropic","Status, diffs, history and commits for a repo","developer","mcp-git","F05032",false,"stdio","uvx",["mcp-server-git"],{},[],{},"https://github.com/modelcontextprotocol/servers/tree/main/src/git",["git_status","git_diff","git_log","git_commit"],null,false,false]
    ]
    """;

    [Fact] // Every scalar, collection, default and declaration order.
    [Trait("Req", "PR-MCP-001")]
    [Trait("Req", "PR-MCP-002")]
    [Trait("Req", "PR-MCP-003")]
    [Trait("Req", "PR-MCP-004")]
    [Trait("Req", "PR-MCP-005")]
    [Trait("Req", "PR-MCP-006")]
    [Trait("Req", "PR-MCP-007")]
    [Trait("Req", "PR-MCP-008")]
    [Trait("Req", "PR-MCP-009")]
    [Trait("Req", "PR-MCP-010")]
    public void First_ten_entries_match_the_complete_frozen_expected_table() =>
        AssertSnapshot(FirstTen, MCPCatalog.Entries.Take(10));

    // Frozen from PR-MCP-011..024 expected_result, independently of the implementation table.
    private const string NextFourteen = """
    [["brave-search","Brave Search","Brave","Searches the web, local places and news","search","mcp-brave-search","FB542B",false,"stdio","npx",["-y","@brave/brave-search-mcp-server"],{},[["key","API key","BSA…","Secret","Make a key on the page, then copy it",null,true]],{"BRAVE_API_KEY":"{key}"},"https://github.com/brave/brave-search-mcp-server",["brave_web_search","brave_local_search","brave_news_search"],"https://api-dashboard.search.brave.com/app/keys",true,false],
      ["tavily","Tavily","Tavily","Searches the web and pulls out page content","search","mcp-tavily","0D9488",false,"oauth","https://mcp.tavily.com/mcp",[],{},[],{},"https://github.com/tavily-ai/tavily-mcp",["tavily-search","tavily-extract","tavily-crawl"],null,false,true],
      ["exa","Exa","Exa","Searches the web and reads pages, with code-focused answers","search","mcp-exa","1F4FFF",false,"oauth","https://mcp.exa.ai/mcp",[],{},[],{},"https://github.com/exa-labs/exa-mcp-server",["web_search_exa","web_fetch_exa","agent_run"],null,false,true],
      ["firecrawl","Firecrawl","Firecrawl","Scrapes, searches and maps whole websites","browser","mcp-firecrawl","FF6B1A",false,"stdio","npx",["-y","firecrawl-mcp"],{},[["key","API key","fc-…","Secret","Make a key on the page, then copy it",null,true]],{"FIRECRAWL_API_KEY":"{key}"},"https://github.com/firecrawl/firecrawl-mcp-server",["firecrawl_scrape","firecrawl_search","firecrawl_crawl","firecrawl_map"],"https://www.firecrawl.dev/app/api-keys",true,false],
      ["perplexity","Perplexity","Perplexity","Asks the web and answers with sources","search","mcp-perplexity","20808D",false,"stdio","npx",["-y","@perplexity-ai/mcp-server"],{},[["key","API key","pplx-…","Secret","Make a key on the page, then copy it",null,true]],{"PERPLEXITY_API_KEY":"{key}"},"https://github.com/perplexityai/modelcontextprotocol",["perplexity_search","perplexity_ask","perplexity_research","perplexity_reason"],"https://www.perplexity.ai/settings/api",true,false],
      ["slack","Slack","Slack","Lists channels, reads and posts messages","work","mcp-slack","4A154B",false,"stdio","npx",["-y","slack-mcp-server@latest","--transport","stdio"],{},[["token","Bot token","xoxb-…","Secret","Copy the Bot User OAuth Token from your app, then invite the bot to the channels it should read",null,true]],{"SLACK_MCP_XOXB_TOKEN":"{token}","SLACK_MCP_ADD_MESSAGE_TOOL":"true"},"https://github.com/korotovsky/slack-mcp-server",["channels_list","conversations_history","conversations_search_messages","conversations_add_message"],"https://api.slack.com/apps",true,false],
      ["notion","Notion","Notion","Finds and edits pages in your workspace","work","mcp-notion","000000",true,"oauth","https://mcp.notion.com/mcp",[],{},[],{},"https://developers.notion.com/docs/mcp",["API-post-search","API-retrieve-a-page","API-patch-block-children"],null,false,true],
      ["linear","Linear","Linear","Issues and projects in your Linear teams","work","mcp-linear","5E6AD2",false,"oauth","https://mcp.linear.app/mcp",[],{},[],{},"https://linear.app/docs/mcp",["list_issues","create_issue","update_issue","list_projects"],null,false,true],
      ["atlassian","Atlassian","Atlassian","Jira issues and Confluence pages","work","mcp-atlassian","0052CC",false,"oauth","https://mcp.atlassian.com/v2/mcp",[],{},[],{},"https://developer.atlassian.com/cloud/rovo-mcp/",["searchJiraIssuesUsingJql","createJiraIssue","getConfluenceContent"],null,false,true],
      ["figma","Figma","Figma","Design details and screenshots from files","work","mcp-figma","F24E1E",false,"oauth","https://mcp.figma.com/mcp",[],{},[],{},"https://help.figma.com/hc/en-us/articles/32132100833559",["get_design_context","get_screenshot","get_metadata"],null,false,true],
      ["sentry","Sentry","Sentry","Error reports and which projects they hit","developer","mcp-sentry","7B61FF",false,"oauth","https://mcp.sentry.dev/mcp",[],{},[],{},"https://mcp.sentry.dev",["search_issues","get_issue_details","find_projects"],null,false,true],
      ["vercel","Vercel","Vercel","Projects, deployments and their build logs","cloud","mcp-vercel","000000",true,"oauth","https://mcp.vercel.com",[],{},[],{},"https://vercel.com/docs/agent-resources/vercel-mcp",["list_projects","list_deployments","get_deployment_build_logs"],null,false,true],
      ["cloudflare","Cloudflare","Cloudflare","Workers, storage buckets and databases","cloud","mcp-cloudflare","F38020",false,"oauth","https://bindings.mcp.cloudflare.com/mcp",[],{},[],{},"https://github.com/cloudflare/mcp-server-cloudflare",["workers_list","kv_namespaces_list","r2_buckets_list","d1_databases_list"],null,false,true],
      ["netlify","Netlify","Netlify","Sites, deploys, forms and environment variables","cloud","mcp-netlify","00C7B7",false,"oauth","https://netlify-mcp.netlify.app/mcp",[],{},[],{},"https://docs.netlify.com/welcome/build-with-ai/netlify-mcp-server/",["get-projects","deploy-site","manage-env-vars","get-deploy"],null,false,true]
    ]
    """;

    [Fact] // Every scalar, collection, default and declaration order.
    [Trait("Req", "PR-MCP-011")]
    [Trait("Req", "PR-MCP-012")]
    [Trait("Req", "PR-MCP-013")]
    [Trait("Req", "PR-MCP-014")]
    [Trait("Req", "PR-MCP-015")]
    [Trait("Req", "PR-MCP-016")]
    [Trait("Req", "PR-MCP-017")]
    [Trait("Req", "PR-MCP-018")]
    [Trait("Req", "PR-MCP-019")]
    [Trait("Req", "PR-MCP-020")]
    [Trait("Req", "PR-MCP-021")]
    [Trait("Req", "PR-MCP-022")]
    [Trait("Req", "PR-MCP-023")]
    [Trait("Req", "PR-MCP-024")]
    public void Entries_eleven_through_twentyfour_match_the_complete_frozen_expected_table() =>
        AssertSnapshot(NextFourteen, MCPCatalog.Entries.Skip(10).Take(14));

    [Fact] // R-STR-4 (ED-009): the id/name matcher lowercases with SwiftString.Lowercased, never ToLowerInvariant.
    [Trait("Req", "PR-MCP-002")]
    [Trait("Rule", "R-STR-4")]
    public void EntryMatching_finds_gitlab_by_uppercase_id_and_by_name_with_spaces()
    {
        Assert.Same(MCPCatalog.Entry("gitlab"), MCPCatalog.EntryMatching("GITLAB"));
        Assert.Same(MCPCatalog.Entry("brave-search"), MCPCatalog.EntryMatching("Brave Search"));
        Assert.Same(MCPCatalog.Entry("brave-search"), MCPCatalog.EntryMatching("brave_search"));
    }

    [Fact] // Home default is dynamic; the user-visible macOS placeholder stays verbatim.
    [Trait("Req", "PR-MCP-006")]
    public void Filesystem_home_default_uses_the_Windows_user_profile()
    {
        var entry = MCPCatalog.Entries.Single(e => e.Id == "filesystem");
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Assert.Single(entry.Fields).DefaultValue);
        Assert.Equal("/Users/you/Projects", entry.Fields[0].Placeholder);
    }

    internal static void AssertSnapshot(string fixture, IEnumerable<MCPCatalogEntry> entries)
    {
        fixture = fixture.Replace("\"$HOME_FIXTURE\"", JsonSerializer.Serialize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), StringComparison.Ordinal);
        using var expected = JsonDocument.Parse(fixture);
        var actual = JsonSerializer.SerializeToElement(entries.Select(Project));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual), "Complete catalog fixture differs: " + actual.GetRawText());
    }

    private static object?[] Project(MCPCatalogEntry entry)
    {
        (string kind, string address, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> headers) = entry.Transport switch
        {
            MCPTransport.Stdio stdio => ("stdio", stdio.Command, stdio.Args, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()),
            MCPTransport.Http http => ("http", http.Url, (IReadOnlyList<string>)Array.Empty<string>(), http.Headers),
            MCPTransport.Oauth oauth => ("oauth", oauth.Url, (IReadOnlyList<string>)Array.Empty<string>(), (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()),
            _ => throw new InvalidOperationException()
        };
        return [entry.Id, entry.Name, entry.Vendor, entry.Summary, entry.Category.Id(), entry.Asset,
            entry.ColorValue.ToString("X6", System.Globalization.CultureInfo.InvariantCulture), entry.IsMonochrome, kind, address, args, headers,
            entry.Fields.Select(f => new object?[] { f.Key, f.Label, f.Placeholder, f.FieldKind.ToString(), f.Help, f.DefaultValue, f.IsRequired }).ToArray(),
            entry.Env, entry.DocsURL, entry.SampleTools, entry.KeysURL, entry.NeedsInput, entry.IsOAuth];
    }
}
