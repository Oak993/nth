namespace Marrow.Core.Models;

// Verbatim catalog data from DroppyCode/Core/Models/MCPCatalog.swift.
public static partial class MCPCatalog
{
    public static IReadOnlyList<MCPCatalogEntry> Entries { get; } = Array.AsReadOnly(new MCPCatalogEntry[]
    {
        new("github", "GitHub", "GitHub", "Issues, pull requests and code across your repositories", MCPCategory.Developer, "mcp-github", 0x24292F, new MCPTransport.Http("https://api.githubcopilot.com/mcp/", new Dictionary<string, string> { ["Authorization"] = "Bearer {token}" }), "https://github.com/github/github-mcp-server")
        { IsMonochrome = true, Fields = [new MCPField("token", "Personal access token", "ghp_…", MCPField.Kind.Secret, Help: "Create a fine-grained token on the page, then copy it")], SampleTools = ["list_issues", "create_pull_request", "search_code", "get_file_contents"], KeysUrl = "https://github.com/settings/personal-access-tokens/new" },
        new("gitlab", "GitLab", "GitLab", "Issues, merge requests, pipelines and code across your projects", MCPCategory.Developer, "mcp-gitlab", 0xFC6D26, new MCPTransport.OAuth("https://{instance}/api/v4/mcp"), "https://docs.gitlab.com/user/model_context_protocol/mcp_server/")
        { Fields = [new MCPField("instance", "GitLab address", "gitlab.com", MCPField.Kind.Text, Help: "gitlab.com, or your own instance's host", DefaultValue: "gitlab.com", IsRequired: false)], SampleTools = ["list_merge_requests", "get_merge_request_diffs", "create_issue", "get_pipeline_jobs"] },
        new("playwright", "Playwright", "Microsoft", "Drives a real browser: open pages, click, fill forms, screenshot", MCPCategory.Browser, "mcp-playwright", 0x2EAD33, new MCPTransport.Stdio("npx", ["-y", "@playwright/mcp@latest"]), "https://github.com/microsoft/playwright-mcp")
        { SampleTools = ["browser_navigate", "browser_click", "browser_snapshot", "browser_take_screenshot"] },
        new("chrome-devtools", "Chrome DevTools", "Google", "Inspects pages, console messages and performance traces", MCPCategory.Browser, "mcp-chrome-devtools", 0x4285F4, new MCPTransport.Stdio("npx", ["-y", "chrome-devtools-mcp@latest"]), "https://github.com/ChromeDevTools/chrome-devtools-mcp")
        { SampleTools = ["navigate_page", "take_screenshot", "list_console_messages", "performance_start_trace"] },
        new("context7", "Context7", "Upstash", "Fresh docs and examples for the libraries you use", MCPCategory.Knowledge, "mcp-context7", 0x5B8DEF, new MCPTransport.Http("https://mcp.context7.com/mcp", new Dictionary<string, string> { ["Authorization"] = "Bearer {key}" }), "https://github.com/upstash/context7")
        { Fields = [new MCPField("key", "API key", "ctx7sk-…", MCPField.Kind.Secret, Help: "Optional. Raises the rate limit", IsRequired: false)], SampleTools = ["resolve-library-id", "query-docs"], KeysUrl = "https://context7.com/dashboard" },
        new("filesystem", "Filesystem", "Anthropic", "Reads, writes and searches files in one folder", MCPCategory.Developer, "mcp-filesystem", 0x8E8E93, new MCPTransport.Stdio("npx", ["-y", "@modelcontextprotocol/server-filesystem", "{root}"]), "https://github.com/modelcontextprotocol/servers/tree/main/src/filesystem")
        { Fields = [new MCPField("root", "Folder", "/Users/you/Projects", MCPField.Kind.Path, Help: "The folder the server may use", DefaultValue: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))], SampleTools = ["read_text_file", "write_file", "list_directory", "search_files"] },
        new("fetch", "Fetch", "Anthropic", "Downloads web pages as plain text to read", MCPCategory.Browser, "mcp-fetch", 0x0A84FF, new MCPTransport.Stdio("uvx", ["mcp-server-fetch"]), "https://github.com/modelcontextprotocol/servers/tree/main/src/fetch")
        { SampleTools = ["fetch"] },
        new("memory", "Memory", "Anthropic", "Remembers people, facts and how they connect", MCPCategory.Knowledge, "mcp-memory", 0xAF52DE, new MCPTransport.Stdio("npx", ["-y", "@modelcontextprotocol/server-memory"]), "https://github.com/modelcontextprotocol/servers/tree/main/src/memory")
        { SampleTools = ["create_entities", "create_relations", "search_nodes", "read_graph"] },
        new("sequential-thinking", "Sequential Thinking", "Anthropic", "Thinks hard problems through step by step", MCPCategory.Knowledge, "mcp-sequential-thinking", 0x30B0C7, new MCPTransport.Stdio("npx", ["-y", "@modelcontextprotocol/server-sequential-thinking"]), "https://github.com/modelcontextprotocol/servers/tree/main/src/sequentialthinking")
        { SampleTools = ["sequentialthinking"] },
        new("git", "Git", "Anthropic", "Status, diffs, history and commits for a repo", MCPCategory.Developer, "mcp-git", 0xF05032, new MCPTransport.Stdio("uvx", ["mcp-server-git"]), "https://github.com/modelcontextprotocol/servers/tree/main/src/git")
        { SampleTools = ["git_status", "git_diff", "git_log", "git_commit"] }
    });
}
