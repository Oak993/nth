using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MCP.Import;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.MCP.Import;

// ED-NEW-mcpimport-1: the stand-in for MCPStore with the same id rule and validation as SaveCustom, recording what reaches SaveCustomValues (McpImportTests and the import controller's tests).
internal sealed class FakeMcpImportTarget : IMcpImportTarget
{
    public List<MCPCustomServer> Saved { get; } = [];
    public Dictionary<string, IReadOnlyList<(string Key, string Value, bool IsSecret)>> Values { get; } = [];
    public List<(string Id, string? Replacing)> Calls { get; } = [];
    public HashSet<string> Connected { get; } = [];
    public List<string> Refreshed { get; } = [];

    public IReadOnlyList<MCPCustomServer> Customs => [.. Saved];

    public IReadOnlySet<string> TakenIds => Saved.Select(s => s.Id).ToHashSet(SwiftStringComparer.Instance);

    public bool IsConnected(string id) => Connected.Contains(id);

    public MCPCustomError? SaveCustom(MCPCustomServer server, string? replacing)
    {
        var old = replacing is null ? null : Saved.FirstOrDefault(s => s.Id == replacing);
        var taken = Saved.Select(s => s.Id).ToHashSet(SwiftStringComparer.Instance);
        if (old is not null) taken.Remove(old.Id);
        server = server with { Name = server.Name.Trimmed() };
        server = server with { Id = old?.Id ?? MCPCustomServer.MakeID(server.Name) };
        if (MCPCustomError.Validate(server, taken) is { } error) return error;
        var index = Saved.FindIndex(s => s.Id == server.Id);
        if (index >= 0) Saved[index] = server; else Saved.Add(server);
        Calls.Add((server.Id, replacing));
        return null;
    }

    public bool SaveCustomValues(IReadOnlyList<(string Key, string Value, bool IsSecret)> values, string id, MCPCatalogEntry? replacingFieldsOf)
    {
        Values[id] = values;
        return true;
    }

    public void Refresh(string id) => Refreshed.Add(id);
}
