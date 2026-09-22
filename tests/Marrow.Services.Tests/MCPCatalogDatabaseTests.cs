using Marrow.Core.Models;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests;

public sealed class MCPCatalogDatabaseTests
{
    [Fact] // Test-only strings, no external database or process.
    [Trait("Req", "PR-MCP-028")]
    [Trait("Req", "PR-MCP-029")]
    [Trait("Req", "PR-MCP-030")]
    public void Database_values_resolve_into_the_source_argument_or_environment_location()
    {
        var postgres = Resolve("postgres", "url", "postgresql://fixture.invalid/db");
        Assert.Equal(new[] { "-y", "@bytebase/dbhub@latest", "--transport", "stdio", "--dsn", "postgresql://fixture.invalid/db" }, postgres.Args);
        Assert.Empty(postgres.Env);
        var sqlite = Resolve("sqlite", "path", "C:\\fixture folder\\app.db");
        Assert.Equal(new[] { "mcp-server-sqlite", "--db-path", "C:\\fixture folder\\app.db" }, sqlite.Args);
        var mongo = Resolve("mongodb", "url", "mongodb://fixture.invalid");
        Assert.Equal("mongodb://fixture.invalid", mongo.Env["MDB_MCP_CONNECTION_STRING"]);
        Assert.DoesNotContain("mongodb://fixture.invalid", mongo.Args);
    }

    private static MCPResolvedServer Resolve(string id, string key, string value) =>
        MCPResolver.Resolve(MCPCatalog.Entries.Single(e => e.Id == id), new Dictionary<string,string> { [key] = value }, _ => throw new InvalidOperationException());
}
