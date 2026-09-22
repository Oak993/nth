using System.Text.Json;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MCPCatalogTests
{
    // Frozen from PR-MCP-001..010 Expected text, independently of the implementation table.
    private const string FirstTen = """
    [
      ["github","GitHub","GitHub","Issues, pull requests and code across your repositories","developer","mcp-github","24292F",true,"http","https://api.githubcopilot.com/mcp/",[],{"Authorization":"Bearer {token}"},[["token","Personal access token","ghp_…","Secret","Create a fine-grained token on the page, then copy it",null,true]],{},"https://github.com/github/github-mcp-server",["list_issues","create_pull_request","search_code","get_file_contents"],"https://github.com/settings/personal-access-tokens/new",true,false],
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

    [Fact] // PR-MCP-001..010: every scalar, collection, default and declaration order.
    public void First_ten_entries_match_the_complete_frozen_expected_table() =>
        AssertSnapshot(FirstTen, MCPCatalog.Entries.Take(10));

    [Fact] // PR-MCP-006: home default is dynamic; the user-visible macOS placeholder stays verbatim.
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
        var (kind, address, args, headers) = entry.Transport switch
        {
            MCPTransport.Stdio stdio => ("stdio", stdio.Command, stdio.Args, (IReadOnlyDictionary<string,string>)new Dictionary<string,string>()),
            MCPTransport.Http http => ("http", http.Url, (IReadOnlyList<string>)Array.Empty<string>(), http.Headers),
            MCPTransport.OAuth oauth => ("oauth", oauth.Url, (IReadOnlyList<string>)Array.Empty<string>(), (IReadOnlyDictionary<string,string>)new Dictionary<string,string>()),
            _ => throw new InvalidOperationException()
        };
        return [entry.Id, entry.Name, entry.Vendor, entry.Summary, entry.Category.Id(), entry.Asset,
            entry.ColorValue.ToString("X6"), entry.IsMonochrome, kind, address, args, headers,
            entry.Fields.Select(f => new object?[] { f.Key, f.Label, f.Placeholder, f.FieldKind.ToString(), f.Help, f.DefaultValue, f.IsRequired }).ToArray(),
            entry.Env, entry.DocsUrl, entry.SampleTools, entry.KeysUrl, entry.NeedsInput, entry.IsOAuth];
    }
}
