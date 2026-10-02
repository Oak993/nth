using Marrow.Core.Port;
using Marrow.Services.MCP;

// PORT-ONLY test fixture, no Swift twin: a second MCPProxy, isolated from the process-wide MCPProxy.Shared, that
// really listens on loopback. The shared proxy already holds MCPProxy.Port, so this instance's Start() collides,
// repicks once and listens on a fresh port (PR-MCP-124, MCPProxyTests' bind-failure test does the same); RepickPort
// rewrites the static port and the proxy-port file, so this scope binds a scratch Storage.Root for the file and
// puts the static port back on dispose (MCPProxy.ResetPortForTests), or MCPProxy.Port and MCPProxy.Url would stop
// matching MCPProxySingletonFixture.Port for the rest of the collection. Its log lines go nowhere.
namespace Marrow.Services.Tests.Fixtures;

internal sealed class IsolatedMCPProxy : IDisposable
{
    private readonly ushort originalPort = MCPProxy.Port;
    private readonly StorageScratchFolder storage = new();

    public IsolatedMCPProxy()
    {
        Proxy = MCPProxy.CreateForTests();
        using (Logger.Sink.Override((_, _) => { })) Proxy.Start();
    }

    public MCPProxy Proxy { get; }

    /// <summary>The port this instance listens on (the repicked one while it runs).</summary>
    public int Port => MCPProxy.Port;

    public void Dispose()
    {
        Proxy.Stop();
        MCPProxy.ResetPortForTests(originalPort);
        storage.Dispose();
    }
}
