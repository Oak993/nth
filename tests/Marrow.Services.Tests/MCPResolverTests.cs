using Marrow.Core.Models;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests;

public sealed class MCPResolverTests
{
    private static MCPCatalogEntry Entry(MCPTransport transport) =>
        new("server", "Server", "Vendor", "Summary", MCPCategory.Developer, "asset", 0,
            transport, "docs") { Fields = [new("key", "Key", "", IsRequired: false)] };

    [Fact] // PR-MCP-041, PR-MCP-046
    public void Stdio_fills_args_and_env_values_but_never_command_or_env_keys()
    {
        var entry = Entry(new MCPTransport.Stdio("{key}", ["{key}", "{unknown}"])) with
        { Env = new Dictionary<string,string> { ["{key}"] = "present", ["DROP"] = "{key}", ["KEEP"] = "Bearer {key}" } };
        var server = MCPResolver.Resolve(entry, new Dictionary<string,string>(), _ => throw new InvalidOperationException());
        Assert.Equal("server", server.Id); Assert.Equal("Server", server.Name); Assert.Equal("{key}", server.Command);
        Assert.Equal(new[] { "", "{unknown}" }, server.Args); Assert.Equal(2, server.Env.Count);
        Assert.Equal("present", server.Env["{key}"]); Assert.Equal("Bearer ", server.Env["KEEP"]);
        Assert.Null(server.Url); Assert.Null(server.OAuthUpstream); Assert.Empty(server.Headers); Assert.False(server.IsRemote);
    }

    [Fact] // PR-MCP-042, PR-MCP-046
    public void Http_keeps_Bearer_space_but_drops_a_wholly_empty_header()
    {
        var entry = Entry(new MCPTransport.Http("https://host/{key}", new Dictionary<string,string> { ["Authorization"] = "Bearer {key}", ["Empty"] = "{key}", ["{key}"] = "value" }));
        var server = MCPResolver.Resolve(entry, new Dictionary<string,string>(), _ => throw new InvalidOperationException());
        Assert.Equal("https://host/", server.Url); Assert.Equal("Bearer ", server.Headers["Authorization"]);
        Assert.Equal("value", server.Headers["{key}"]); Assert.Equal(2, server.Headers.Count);
        Assert.Null(server.Command); Assert.Empty(server.Args); Assert.Empty(server.Env); Assert.Null(server.OAuthUpstream); Assert.True(server.IsRemote);
    }

    [Fact] // PR-MCP-043; Core-to-Services boundary mandated by architecture V1.
    public void OAuth_calls_only_the_proxy_route_supplier_and_keeps_upstream_separate()
    {
        var entry = Entry(new MCPTransport.OAuth("https://remote/{key}"));
        var requested = new List<string>();
        var server = MCPResolver.Resolve(entry, new Dictionary<string,string> { ["key"] = "value" }, id => { requested.Add(id); return "http://127.0.0.1:54321/mcp/" + id; });
        Assert.Equal(new[] { "server" }, requested); Assert.Equal("http://127.0.0.1:54321/mcp/server", server.Url);
        Assert.Equal("https://remote/value", server.OAuthUpstream); Assert.True(server.IsRemote);
        Assert.Empty(server.Headers); Assert.Empty(server.Args); Assert.Empty(server.Env); Assert.Null(server.Command);
    }
}
