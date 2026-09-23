using Marrow.Core.Port;

// Source: DroppyCode/Services/MCP/MCPProxy.swift (B2 1fbf81e), :1-460. Skeleton: the private port helpers (:14-33),
// Route, ConnectionBox, State, the limits (:39-62) and the listener and exchange machinery (:99-135, :155-460) are
// left to the implementing unit.

namespace Marrow.Services.MCP;

// :5-460
public sealed class MCPProxy
{
    // :6. A static let shared that tests replace (R-LIFE-2).
    public static Ambient<MCPProxy> Shared { get; } = new(() => new MCPProxy());

    // :12
    public static ushort Port => throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPProxy.swift:12");

    // :35-37. Swift: url(for serverID: String).
    public static string Url(string @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPProxy.swift:35");

    // :64
    private MCPProxy() { }

    // :66-70
    public void Register(string serverID, string upstream, IReadOnlyDictionary<string, string> headers, bool usesOAuth) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPProxy.swift:66");

    // :72-74
    public void Unregister(string serverID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPProxy.swift:72");

    // :76-82
    public void ReplaceAll(
        IReadOnlyList<(string ServerID, string Upstream, IReadOnlyDictionary<string, string> Headers, bool UsesOAuth)> routes) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPProxy.swift:76");

    // :84-92
    public void Start() => throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPProxy.swift:84");

    // :137-150
    public void Stop() => throw new NotImplementedException("MARROW_STUB DroppyCode/Services/MCP/MCPProxy.swift:137");
}
