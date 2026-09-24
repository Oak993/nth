using Marrow.Services.MCP;
using Marrow.Services.Port;

// PORT-ONLY: xUnit class fixture for MCPProxyTests. MCPProxy.Shared is a process-wide singleton (a Swift
// `static let`): this binds Storage.Root once, before anything touches MCPProxy.Port for the first time, and
// keeps that scratch folder alive for the whole test class (R-TEST-9).
//
// R-TEST-9 (no test touches a real credential): MCPProxy.Shared's accept loop and every connection it hands off
// run in the ExecutionContext captured at Start() (SecretStore.Shared is Ambient<T>/AsyncLocal, Port/Ambient.cs),
// which is this constructor's own flow. Starting it inside SecretStore.Shared.Override(Store) bakes an in-memory
// backend into that captured flow for the whole test class; MCPProxyTests.Override(fixture.Store) (its own
// constructor) then reaches the same instance from each test method's flow, so a test's MCPKeychain.Set/Delete
// and the proxy's own MCPOAuth.AccessToken read agree, and nothing reaches the real Windows Credential Manager.
namespace Marrow.Services.Tests.Fixtures;

public sealed class MCPProxySingletonFixture : IDisposable
{
    private readonly StorageScratchFolder folder = new();
    public int Port { get; }
    public SecretStore Store { get; } = new(new InMemoryCredentialBackend());

    public MCPProxySingletonFixture()
    {
        Port = MCPProxy.Port; // DroppyCode/Services/MCP/MCPProxy.swift:12-25, first access in the process
        using (SecretStore.Shared.Override(Store))
        {
            MCPProxy.Shared.Current.Start(); // DroppyCode/Services/MCP/MCPProxy.swift:84-92
        }
    }

    public void Dispose() => folder.Dispose();
}
