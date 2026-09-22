using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MCPCatalogSecondTests
{
    [Fact] // PR-MCP-011..020, frozen from packet Expected text in source declaration order.
    public void Second_ten_entries_match_every_expected_field()
    {
        const string fixture = """
        [
          ["brave-search","Brave Search","Brave","Searches the web, local places and news","search","mcp-brave-search","FB542B",false,"stdio","npx",["-y","@brave/brave-search-mcp-server"],{},[["key","API key","BSA…","Secret","Make a key on the page, then copy it",null,true]],{"BRAVE_API_KEY":"{key}"},"https://github.com/brave/brave-search-mcp-server",["brave_web_search","brave_local_search","brave_news_search"],"https://api-dashboard.search.brave.com/app/keys",true,false],
          ["tavily","Tavily","Tavily","Searches the web and pulls out page content","search","mcp-tavily","0D9488",false,"oauth","https://mcp.tavily.com/mcp",[],{},[],{},"https://github.com/tavily-ai/tavily-mcp",["tavily-search","tavily-extract","tavily-crawl"],null,false,true],
          ["exa","Exa","Exa","Searches the web and reads pages, with code-focused answers","search","mcp-exa","1F4FFF",false,"oauth","https://mcp.exa.ai/mcp",[],{},[],{},"https://github.com/exa-labs/exa-mcp-server",["web_search_exa","web_fetch_exa","agent_run"],null,false,true],
          ["firecrawl","Firecrawl","Firecrawl","Scrapes, searches and maps whole websites","browser","mcp-firecrawl","FF6B1A",false,"stdio","npx",["-y","firecrawl-mcp"],{},[["key","API key","fc-…","Secret","Make a key on the page, then copy it",null,true]],{"FIRECRAWL_API_KEY":"{key}"},"https://github.com/firecrawl/firecrawl-mcp-server",["firecrawl_scrape","firecrawl_search","firecrawl_crawl","firecrawl_map"],"https://www.firecrawl.dev/app/api-keys",true,false],
          ["perplexity","Perplexity","Perplexity","Asks the web and answers with sources","search","mcp-perplexity","20808D",false,"stdio","npx",["-y","@perplexity-ai/mcp-server"],{},[["key","API key","pplx-…","Secret","Make a key on the page, then copy it",null,true]],{"PERPLEXITY_API_KEY":"{key}"},"https://github.com/perplexityai/modelcontextprotocol",["perplexity_search","perplexity_ask","perplexity_research","perplexity_reason"],"https://www.perplexity.ai/settings/api",true,false],
          ["slack","Slack","Slack","Lists channels, reads and posts messages","work","mcp-slack","4A154B",false,"stdio","npx",["-y","slack-mcp-server@latest","--transport","stdio"],{},[["token","Bot token","xoxb-…","Secret","Copy the Bot User OAuth Token from your app, then invite the bot to the channels it should read",null,true]],{"SLACK_MCP_XOXB_TOKEN":"{token}","SLACK_MCP_ADD_MESSAGE_TOOL":"true"},"https://github.com/korotovsky/slack-mcp-server",["channels_list","conversations_history","conversations_search_messages","conversations_add_message"],"https://api.slack.com/apps",true,false],
          ["notion","Notion","Notion","Finds and edits pages in your workspace","work","mcp-notion","000000",true,"oauth","https://mcp.notion.com/mcp",[],{},[],{},"https://developers.notion.com/docs/mcp",["API-post-search","API-retrieve-a-page","API-patch-block-children"],null,false,true],
          ["linear","Linear","Linear","Issues and projects in your Linear teams","work","mcp-linear","5E6AD2",false,"oauth","https://mcp.linear.app/mcp",[],{},[],{},"https://linear.app/docs/mcp",["list_issues","create_issue","update_issue","list_projects"],null,false,true],
          ["atlassian","Atlassian","Atlassian","Jira issues and Confluence pages","work","mcp-atlassian","0052CC",false,"oauth","https://mcp.atlassian.com/v2/mcp",[],{},[],{},"https://developer.atlassian.com/cloud/rovo-mcp/",["searchJiraIssuesUsingJql","createJiraIssue","getConfluenceContent"],null,false,true],
          ["figma","Figma","Figma","Design details and screenshots from files","work","mcp-figma","F24E1E",false,"oauth","https://mcp.figma.com/mcp",[],{},[],{},"https://help.figma.com/hc/en-us/articles/32132100833559",["get_design_context","get_screenshot","get_metadata"],null,false,true]
        ]
        """;
        MCPCatalogTests.AssertSnapshot(fixture, MCPCatalog.Entries.Skip(10).Take(10));
    }
}
