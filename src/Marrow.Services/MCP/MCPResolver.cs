using Marrow.Core.Models;

namespace Marrow.Services.MCP;

// MCP.swift:152-169. Architecture V1 moves the proxy-dependent resolver out of Core.
public static class MCPResolver
{
    public static MCPResolvedServer Resolve(MCPCatalogEntry entry, IReadOnlyDictionary<string, string> values,
        Func<string, string> proxyUrl)
    {
        string Fill(string text) => entry.Fill(text, values);
        Dictionary<string, string> FillValues(IReadOnlyDictionary<string, string> source) => source
            .Select(pair => new KeyValuePair<string, string>(pair.Key, Fill(pair.Value)))
            .Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value);
        var server = new MCPResolvedServer(entry.Id, entry.Name);
        return entry.Transport switch
        {
            MCPTransport.Stdio stdio => server with { Command = stdio.Command, Args = stdio.Args.Select(Fill).ToArray(), Env = FillValues(entry.Env) },
            MCPTransport.Http http => server with { Url = Fill(http.Url), Headers = FillValues(http.Headers) },
            MCPTransport.OAuth oauth => server with { Url = proxyUrl(entry.Id), OAuthUpstream = Fill(oauth.Url) },
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }
}
