using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;

namespace Marrow.Services.Tests;

public sealed class MCPResolverTests
{
    private static MCPCatalogEntry Entry(MCPTransport transport) =>
        new("server", "Server", "Vendor", "Summary", MCPCategory.Developer, "asset", 0,
            transport, "docs") { Fields = new ValueList<MCPField>([new("key", "Key", "", IsRequired: false)]) };

    [Fact]
    [Trait("Req", "PR-MCP-041")]
    [Trait("Req", "PR-MCP-046")]
    public void Stdio_fills_args_and_env_values_but_never_command_or_env_keys()
    {
        var entry = Entry(new MCPTransport.Stdio("{key}", new ValueList<string>(["{key}", "{unknown}"]))) with
        { Env = new ValueMap<string, string>(new Dictionary<string, string> { ["{key}"] = "present", ["DROP"] = "{key}", ["KEEP"] = "Bearer {key}" }) };
        var server = MCPResolver.Resolve(entry, ValueMap<string, string>.Empty, _ => throw new InvalidOperationException());
        Assert.Equal("server", server.Id); Assert.Equal("Server", server.Name); Assert.Equal("{key}", server.Command);
        Assert.Equal(new[] { "", "{unknown}" }, server.Args); Assert.Equal(2, server.Env.Count);
        Assert.Equal("present", server.Env["{key}"]); Assert.Equal("Bearer ", server.Env["KEEP"]);
        Assert.Null(server.Url); Assert.Null(server.OauthUpstream); Assert.Empty(server.Headers); Assert.False(server.IsRemote);
    }

    [Fact]
    [Trait("Req", "PR-MCP-042")]
    [Trait("Req", "PR-MCP-046")]
    public void Http_keeps_Bearer_space_but_drops_a_wholly_empty_header()
    {
        var entry = Entry(new MCPTransport.Http("https://host/{key}", new ValueMap<string, string>(new Dictionary<string, string> { ["Authorization"] = "Bearer {key}", ["Empty"] = "{key}", ["{key}"] = "value" })));
        var server = MCPResolver.Resolve(entry, ValueMap<string, string>.Empty, _ => throw new InvalidOperationException());
        Assert.Equal("https://host/", server.Url); Assert.Equal("Bearer ", server.Headers["Authorization"]);
        Assert.Equal("value", server.Headers["{key}"]); Assert.Equal(2, server.Headers.Count);
        Assert.Null(server.Command); Assert.Empty(server.Args); Assert.Empty(server.Env); Assert.Null(server.OauthUpstream); Assert.True(server.IsRemote);
    }

    [Fact] // Core-to-Services boundary mandated by architecture V1.
    [Trait("Req", "PR-MCP-043")]
    public void OAuth_calls_only_the_proxy_route_supplier_and_keeps_upstream_separate()
    {
        var entry = Entry(new MCPTransport.Oauth("https://remote/{key}"));
        var requested = new List<string>();
        var server = MCPResolver.Resolve(entry, new ValueMap<string, string>(new Dictionary<string, string> { ["key"] = "value" }), id => { requested.Add(id); return "http://127.0.0.1:54321/mcp/" + id; });
        Assert.Equal(new[] { "server" }, requested); Assert.Equal("http://127.0.0.1:54321/mcp/server", server.Url);
        Assert.Equal("https://remote/value", server.OauthUpstream); Assert.True(server.IsRemote);
        Assert.Empty(server.Headers); Assert.Empty(server.Args); Assert.Empty(server.Env); Assert.Null(server.Command);
    }
}
