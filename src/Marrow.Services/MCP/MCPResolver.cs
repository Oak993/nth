using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.MCP;

// MCP.swift:152-169. Architecture V1 moves the proxy-dependent resolver out of Core.
public static class MCPResolver
{
    public static MCPResolvedServer Resolve(MCPCatalogEntry entry, ValueMap<string, string> values,
        Func<string, string> proxyUrl)
    {
        string Fill(string text) => entry.Fill(text, values);
        ValueMap<string, string> FillValues(ValueMap<string, string> source) => new(source
            .Select(pair => new KeyValuePair<string, string>(pair.Key, Fill(pair.Value)))
            .Where(pair => pair.Value.Length > 0), SwiftStringComparer.Instance);
        var server = new MCPResolvedServer(entry.Id, entry.Name);
        return entry.Transport switch
        {
            MCPTransport.Stdio stdio => server with { Command = stdio.Command, Args = new ValueList<string>(stdio.Args.Select(Fill)), Env = FillValues(entry.Env) },
            MCPTransport.Http http => server with { Url = Fill(http.Url), Headers = FillValues(http.Headers) },
            MCPTransport.Oauth oauth => server with { Url = proxyUrl(entry.Id), OauthUpstream = Fill(oauth.Url) },
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }
}
