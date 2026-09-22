using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MCPCatalogThirdTests
{
    [Fact] // PR-MCP-021..030. Expected text fixtures, not generated from production.
    public void Third_ten_entries_match_every_expected_field()
    {
        const string fixture = """
        [
          ["sentry","Sentry","Sentry","Error reports and which projects they hit","developer","mcp-sentry","7B61FF",false,"oauth","https://mcp.sentry.dev/mcp",[],{},[],{},"https://mcp.sentry.dev",["search_issues","get_issue_details","find_projects"],null,false,true],
          ["vercel","Vercel","Vercel","Projects, deployments and their build logs","cloud","mcp-vercel","000000",true,"oauth","https://mcp.vercel.com",[],{},[],{},"https://vercel.com/docs/agent-resources/vercel-mcp",["list_projects","list_deployments","get_deployment_build_logs"],null,false,true],
          ["cloudflare","Cloudflare","Cloudflare","Workers, storage buckets and databases","cloud","mcp-cloudflare","F38020",false,"oauth","https://bindings.mcp.cloudflare.com/mcp",[],{},[],{},"https://github.com/cloudflare/mcp-server-cloudflare",["workers_list","kv_namespaces_list","r2_buckets_list","d1_databases_list"],null,false,true],
          ["netlify","Netlify","Netlify","Sites, deploys, forms and environment variables","cloud","mcp-netlify","00C7B7",false,"oauth","https://netlify-mcp.netlify.app/mcp",[],{},[],{},"https://docs.netlify.com/welcome/build-with-ai/netlify-mcp-server/",["get-projects","deploy-site","manage-env-vars","get-deploy"],null,false,true],
          ["supabase","Supabase","Supabase","Projects, SQL queries and database changes","data","mcp-supabase","3ECF8E",false,"oauth","https://mcp.supabase.com/mcp",[],{},[],{},"https://supabase.com/docs/guides/ai-tools/mcp",["list_projects","execute_sql","apply_migration","query_logs"],null,false,true],
          ["stripe","Stripe","Stripe","Customers, payment links and balances","work","mcp-stripe","635BFF",false,"oauth","https://mcp.stripe.com",[],{},[],{},"https://docs.stripe.com/mcp",["create_customer","list_customers","create_payment_link","retrieve_balance"],null,false,true],
          ["resend","Resend","Resend","Sends email and manages contacts, broadcasts and domains","work","mcp-resend","000000",true,"oauth","https://mcp.resend.com/mcp",[],{},[],{},"https://github.com/resend/resend-mcp",["send-email","list-contacts","create-broadcast","list-domains"],null,false,true],
          ["postgres","PostgreSQL","PostgreSQL","Runs SQL and explores the schema of a Postgres database","data","mcp-postgres","4169E1",false,"stdio","npx",["-y","@bytebase/dbhub@latest","--transport","stdio","--dsn","{url}"],{},[["url","Connection string","postgresql://user:pass@host:5432/db","Secret","The connection string; a read-only user is safest",null,true]],{},"https://github.com/bytebase/dbhub",["execute_sql","search_objects"],null,true,false],
          ["sqlite","SQLite","SQLite","Reads and writes a local database file","data","mcp-sqlite","0F80CC",false,"stdio","uvx",["mcp-server-sqlite","--db-path","{path}"],{},[["path","Database file","/path/to/app.db","Path","Created if it does not exist",null,true]],{},"https://github.com/modelcontextprotocol/servers-archived/tree/main/src/sqlite",["read_query","write_query","list_tables","describe-table"],null,true,false],
          ["mongodb","MongoDB","MongoDB","Finds and changes documents in your collections","data","mcp-mongodb","47A248",false,"stdio","npx",["-y","mongodb-mcp-server"],{},[["url","Connection string","mongodb+srv://…","Secret","In Atlas: Connect → Drivers, then copy the string",null,true]],{"MDB_MCP_CONNECTION_STRING":"{url}"},"https://github.com/mongodb-js/mongodb-mcp-server",["find","aggregate","list-collections","insert-many"],"https://cloud.mongodb.com",true,false]
        ]
        """;
        MCPCatalogTests.AssertSnapshot(fixture, MCPCatalog.Entries.Skip(20).Take(10));
    }
}
