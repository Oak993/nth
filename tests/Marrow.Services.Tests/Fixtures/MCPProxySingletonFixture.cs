using System.Collections.Concurrent;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Testing;

// PORT-ONLY: xUnit class fixture for MCPProxyTests. MCPProxy.Shared is a process-wide singleton (a Swift
// `static let`): this binds Storage.Root once, before anything touches MCPProxy.Port for the first time, and
// keeps that scratch folder alive for the whole test class (R-TEST-9).
//
// R-TEST-9 (no test touches a real credential): MCPProxy.Shared's accept loop and every connection it hands off
// run in the ExecutionContext captured at Start() (SecretStore.Shared and Logger.Sink are both Ambient<T>/
// AsyncLocal, Port/Ambient.cs), which is this constructor's own flow. Starting it inside both Override scopes
// bakes an in-memory credential backend, and this fixture's own log sink, into that captured flow for the whole
// test class: MCPProxyTests.Override(fixture.Store) (its own constructor) then reaches the same SecretStore
// instance from each test method's flow, so a test's MCPKeychain.Set/Delete and the proxy's own
// MCPOAuth.AccessToken read agree, and nothing reaches the real Windows Credential Manager; LoggedLines
// (B3 52bc792, ED-163's Logger(subsystem:"iordv.droppycode", category:"mcp-proxy")) is reachable from every test
// method the same way, because a per-test Logger.Sink.Override would only be visible to that test's own async
// flow, never to the proxy's background accept loop or the connections it hands off.
//
// OD-0057: the same holds for the HTTP client seam (URLSession.Transport, Ambient): the proxy forwards an https
// OAuth upstream's requests from its own captured flow, so the in-process handler is installed in that same start
// scope, and a test class that needs the proxy and its own sign-in to reach one fake upstream puts the very same
// handler on its flow (Https). The handler answers loopback requests with the real client settings (the proxy's own
// listener, a stand-in server) and every other address from the test's script, or refuses it: no real network.
namespace Marrow.Services.Tests.Fixtures;

public sealed class MCPProxySingletonFixture : IDisposable
{
    private readonly StorageScratchFolder folder = new();
    public int Port { get; }
    public SecretStore Store { get; } = new(new InMemoryCredentialBackend());

    /// <summary>The in-process handler behind URLSession.Transport for the proxy's flow; a test scripts its Answer and resets it after.</summary>
    public InProcessHttpHandler Https { get; } = new();

    /// <summary>Every (message, channel) MCPProxy has logged since the process's one Start() call, this fixture's own.</summary>
    public ConcurrentQueue<(string Message, string Channel)> LoggedLines { get; } = new();

    public MCPProxySingletonFixture()
    {
        Port = MCPProxy.Port; // DroppyCode/Services/MCP/MCPProxy.swift:14-27, first access in the process
        using (SecretStore.Shared.Override(Store))
        using (Logger.Sink.Override((message, channel) => LoggedLines.Enqueue((message, channel))))
        using (URLSession.Transport.Override(Https))
        {
            MCPProxy.Shared.Current.Start(); // DroppyCode/Services/MCP/MCPProxy.swift:86-94
        }
    }

    // Https is not disposed: MCPProxy.Shared outlives this fixture and keeps the handler in its captured flow.
    public void Dispose() => folder.Dispose();
}
