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
        { SampleTools = ["git_status", "git_diff", "git_log", "git_commit"] },
        new("brave-search", "Brave Search", "Brave", "Searches the web, local places and news", MCPCategory.Search, "mcp-brave-search", 0xFB542B, new MCPTransport.Stdio("npx", ["-y", "@brave/brave-search-mcp-server"]), "https://github.com/brave/brave-search-mcp-server")
        { Fields = [new MCPField("key", "API key", "BSA…", MCPField.Kind.Secret, Help: "Make a key on the page, then copy it")], Env = new Dictionary<string, string> { ["BRAVE_API_KEY"] = "{key}" }, SampleTools = ["brave_web_search", "brave_local_search", "brave_news_search"], KeysUrl = "https://api-dashboard.search.brave.com/app/keys" },
        new("tavily", "Tavily", "Tavily", "Searches the web and pulls out page content", MCPCategory.Search, "mcp-tavily", 0x0D9488, new MCPTransport.OAuth("https://mcp.tavily.com/mcp"), "https://github.com/tavily-ai/tavily-mcp")
        { SampleTools = ["tavily-search", "tavily-extract", "tavily-crawl"] },
        new("exa", "Exa", "Exa", "Searches the web and reads pages, with code-focused answers", MCPCategory.Search, "mcp-exa", 0x1F4FFF, new MCPTransport.OAuth("https://mcp.exa.ai/mcp"), "https://github.com/exa-labs/exa-mcp-server")
        { SampleTools = ["web_search_exa", "web_fetch_exa", "agent_run"] },
        new("firecrawl", "Firecrawl", "Firecrawl", "Scrapes, searches and maps whole websites", MCPCategory.Browser, "mcp-firecrawl", 0xFF6B1A, new MCPTransport.Stdio("npx", ["-y", "firecrawl-mcp"]), "https://github.com/firecrawl/firecrawl-mcp-server")
        { Fields = [new MCPField("key", "API key", "fc-…", MCPField.Kind.Secret, Help: "Make a key on the page, then copy it")], Env = new Dictionary<string, string> { ["FIRECRAWL_API_KEY"] = "{key}" }, SampleTools = ["firecrawl_scrape", "firecrawl_search", "firecrawl_crawl", "firecrawl_map"], KeysUrl = "https://www.firecrawl.dev/app/api-keys" },
        new("perplexity", "Perplexity", "Perplexity", "Asks the web and answers with sources", MCPCategory.Search, "mcp-perplexity", 0x20808D, new MCPTransport.Stdio("npx", ["-y", "@perplexity-ai/mcp-server"]), "https://github.com/perplexityai/modelcontextprotocol")
        { Fields = [new MCPField("key", "API key", "pplx-…", MCPField.Kind.Secret, Help: "Make a key on the page, then copy it")], Env = new Dictionary<string, string> { ["PERPLEXITY_API_KEY"] = "{key}" }, SampleTools = ["perplexity_search", "perplexity_ask", "perplexity_research", "perplexity_reason"], KeysUrl = "https://www.perplexity.ai/settings/api" },
        new("slack", "Slack", "Slack", "Lists channels, reads and posts messages", MCPCategory.Work, "mcp-slack", 0x4A154B, new MCPTransport.Stdio("npx", ["-y", "slack-mcp-server@latest", "--transport", "stdio"]), "https://github.com/korotovsky/slack-mcp-server")
        { Fields = [new MCPField("token", "Bot token", "xoxb-…", MCPField.Kind.Secret, Help: "Copy the Bot User OAuth Token from your app, then invite the bot to the channels it should read")], Env = new Dictionary<string, string> { ["SLACK_MCP_XOXB_TOKEN"] = "{token}", ["SLACK_MCP_ADD_MESSAGE_TOOL"] = "true" }, SampleTools = ["channels_list", "conversations_history", "conversations_search_messages", "conversations_add_message"], KeysUrl = "https://api.slack.com/apps" },
        new("notion", "Notion", "Notion", "Finds and edits pages in your workspace", MCPCategory.Work, "mcp-notion", 0x000000, new MCPTransport.OAuth("https://mcp.notion.com/mcp"), "https://developers.notion.com/docs/mcp")
        { IsMonochrome = true, SampleTools = ["API-post-search", "API-retrieve-a-page", "API-patch-block-children"] },
        new("linear", "Linear", "Linear", "Issues and projects in your Linear teams", MCPCategory.Work, "mcp-linear", 0x5E6AD2, new MCPTransport.OAuth("https://mcp.linear.app/mcp"), "https://linear.app/docs/mcp")
        { SampleTools = ["list_issues", "create_issue", "update_issue", "list_projects"] },
        new("atlassian", "Atlassian", "Atlassian", "Jira issues and Confluence pages", MCPCategory.Work, "mcp-atlassian", 0x0052CC, new MCPTransport.OAuth("https://mcp.atlassian.com/v2/mcp"), "https://developer.atlassian.com/cloud/rovo-mcp/")
        { SampleTools = ["searchJiraIssuesUsingJql", "createJiraIssue", "getConfluenceContent"] },
        new("figma", "Figma", "Figma", "Design details and screenshots from files", MCPCategory.Work, "mcp-figma", 0xF24E1E, new MCPTransport.OAuth("https://mcp.figma.com/mcp"), "https://help.figma.com/hc/en-us/articles/32132100833559")
        { SampleTools = ["get_design_context", "get_screenshot", "get_metadata"] },
        new("sentry", "Sentry", "Sentry", "Error reports and which projects they hit", MCPCategory.Developer, "mcp-sentry", 0x7B61FF, new MCPTransport.OAuth("https://mcp.sentry.dev/mcp"), "https://mcp.sentry.dev")
        { SampleTools = ["search_issues", "get_issue_details", "find_projects"] },
        new("vercel", "Vercel", "Vercel", "Projects, deployments and their build logs", MCPCategory.Cloud, "mcp-vercel", 0x000000, new MCPTransport.OAuth("https://mcp.vercel.com"), "https://vercel.com/docs/agent-resources/vercel-mcp")
        { IsMonochrome = true, SampleTools = ["list_projects", "list_deployments", "get_deployment_build_logs"] },
        new("cloudflare", "Cloudflare", "Cloudflare", "Workers, storage buckets and databases", MCPCategory.Cloud, "mcp-cloudflare", 0xF38020, new MCPTransport.OAuth("https://bindings.mcp.cloudflare.com/mcp"), "https://github.com/cloudflare/mcp-server-cloudflare")
        { SampleTools = ["workers_list", "kv_namespaces_list", "r2_buckets_list", "d1_databases_list"] },
        new("netlify", "Netlify", "Netlify", "Sites, deploys, forms and environment variables", MCPCategory.Cloud, "mcp-netlify", 0x00C7B7, new MCPTransport.OAuth("https://netlify-mcp.netlify.app/mcp"), "https://docs.netlify.com/welcome/build-with-ai/netlify-mcp-server/")
        { SampleTools = ["get-projects", "deploy-site", "manage-env-vars", "get-deploy"] },
        new("supabase", "Supabase", "Supabase", "Projects, SQL queries and database changes", MCPCategory.Data, "mcp-supabase", 0x3ECF8E, new MCPTransport.OAuth("https://mcp.supabase.com/mcp"), "https://supabase.com/docs/guides/ai-tools/mcp")
        { SampleTools = ["list_projects", "execute_sql", "apply_migration", "query_logs"] },
        new("stripe", "Stripe", "Stripe", "Customers, payment links and balances", MCPCategory.Work, "mcp-stripe", 0x635BFF, new MCPTransport.OAuth("https://mcp.stripe.com"), "https://docs.stripe.com/mcp")
        { SampleTools = ["create_customer", "list_customers", "create_payment_link", "retrieve_balance"] },
        new("resend", "Resend", "Resend", "Sends email and manages contacts, broadcasts and domains", MCPCategory.Work, "mcp-resend", 0x000000, new MCPTransport.OAuth("https://mcp.resend.com/mcp"), "https://github.com/resend/resend-mcp")
        { IsMonochrome = true, SampleTools = ["send-email", "list-contacts", "create-broadcast", "list-domains"] },
        new("postgres", "PostgreSQL", "PostgreSQL", "Runs SQL and explores the schema of a Postgres database", MCPCategory.Data, "mcp-postgres", 0x4169E1, new MCPTransport.Stdio("npx", ["-y", "@bytebase/dbhub@latest", "--transport", "stdio", "--dsn", "{url}"]), "https://github.com/bytebase/dbhub")
        { Fields = [new MCPField("url", "Connection string", "postgresql://user:pass@host:5432/db", MCPField.Kind.Secret, Help: "The connection string; a read-only user is safest")], SampleTools = ["execute_sql", "search_objects"] },
        new("sqlite", "SQLite", "SQLite", "Reads and writes a local database file", MCPCategory.Data, "mcp-sqlite", 0x0F80CC, new MCPTransport.Stdio("uvx", ["mcp-server-sqlite", "--db-path", "{path}"]), "https://github.com/modelcontextprotocol/servers-archived/tree/main/src/sqlite")
        { Fields = [new MCPField("path", "Database file", "/path/to/app.db", MCPField.Kind.Path, Help: "Created if it does not exist")], SampleTools = ["read_query", "write_query", "list_tables", "describe-table"] },
        new("mongodb", "MongoDB", "MongoDB", "Finds and changes documents in your collections", MCPCategory.Data, "mcp-mongodb", 0x47A248, new MCPTransport.Stdio("npx", ["-y", "mongodb-mcp-server"]), "https://github.com/mongodb-js/mongodb-mcp-server")
        { Fields = [new MCPField("url", "Connection string", "mongodb+srv://…", MCPField.Kind.Secret, Help: "In Atlas: Connect → Drivers, then copy the string")], Env = new Dictionary<string, string> { ["MDB_MCP_CONNECTION_STRING"] = "{url}" }, SampleTools = ["find", "aggregate", "list-collections", "insert-many"], KeysUrl = "https://cloud.mongodb.com" },
        new("huggingface", "Hugging Face", "Hugging Face", "Searches models, datasets and papers", MCPCategory.Knowledge, "mcp-huggingface", 0xFFB000, new MCPTransport.Http("https://huggingface.co/mcp", new Dictionary<string, string> { ["Authorization"] = "Bearer {token}" }), "https://github.com/huggingface/hf-mcp-server")
        { Fields = [new MCPField("token", "Access token", "hf_…", MCPField.Kind.Secret, Help: "Optional. Raises the limits", IsRequired: false)], SampleTools = ["hub_repo_search", "hub_repo_details", "hf_fs", "hf_whoami"], KeysUrl = "https://huggingface.co/settings/tokens" },
        new("deepwiki", "DeepWiki", "Cognition", "Reads docs and answers questions about repos", MCPCategory.Knowledge, "mcp-deepwiki", 0x1D1D1F, new MCPTransport.Http("https://mcp.deepwiki.com/mcp", new Dictionary<string, string> {  }), "https://docs.devin.ai/work-with-devin/deepwiki-mcp")
        { IsMonochrome = true, SampleTools = ["read_wiki_structure", "read_wiki_contents", "ask_question"] },
        new("aws-docs", "AWS Docs", "Amazon Web Services", "Searches and reads the AWS documentation", MCPCategory.Cloud, "mcp-aws-docs", 0xFF9900, new MCPTransport.Stdio("uvx", ["awslabs.aws-documentation-mcp-server@latest"]), "https://awslabs.github.io/mcp/servers/aws-documentation-mcp-server")
        { Env = new Dictionary<string, string> { ["FASTMCP_LOG_LEVEL"] = "ERROR" }, SampleTools = ["read_documentation", "search_documentation", "recommend"] }
    });

    public static MCPCatalogEntry? Entry(string id) => Entries.FirstOrDefault(entry => entry.Id == id);

    public static MCPCatalogEntry? EntryMatching(string serverName)
    {
        static string Normalize(string value) => value.ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        var wanted = Normalize(serverName);
        return Entries.FirstOrDefault(entry => Normalize(entry.Id) == wanted)
            ?? Entries.FirstOrDefault(entry => Normalize(entry.Name) == wanted);
    }

    public static IReadOnlyList<(MCPCategory Category, IReadOnlyList<MCPCatalogEntry> Entries)> ByCategory =>
        Array.AsReadOnly(Enum.GetValues<MCPCategory>()
            .Select(category => (Category: category, Entries: (IReadOnlyList<MCPCatalogEntry>)Array.AsReadOnly(Entries.Where(entry => entry.Category == category).ToArray())))
            .Where(group => group.Entries.Count > 0).ToArray());

    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(Entries.Select(entry => entry.Id).ToArray());
}
