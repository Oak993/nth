using Marrow.Services.MCP;

// PORT-ONLY: xUnit class fixture for MCPProxyTests. MCPProxy.Shared is a process-wide singleton (a Swift
// `static let`): this binds Storage.Root once, before anything touches MCPProxy.Port for the first time, and
// keeps that scratch folder alive for the whole test class (R-TEST-9).
namespace Marrow.Services.Tests.Fixtures;

public sealed class MCPProxySingletonFixture : IDisposable
{
    private readonly StorageScratchFolder folder = new();
    public int Port { get; }

    public MCPProxySingletonFixture()
    {
        Port = MCPProxy.Port; // DroppyCode/Services/MCP/MCPProxy.swift:12-25, first access in the process
        MCPProxy.Shared.Current.Start(); // DroppyCode/Services/MCP/MCPProxy.swift:84-92
    }

    public void Dispose() => folder.Dispose();
}
