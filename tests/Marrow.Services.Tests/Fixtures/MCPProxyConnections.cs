using System.Reflection;
using Marrow.Services.MCP;

// PORT-ONLY test fixture, no Swift twin: how many accepted connections an MCPProxy holds in its lock-guarded map
// (MCPProxy.swift:57-62 state.connections, filled at :176, emptied at :181-183). Nothing public observes that map
// (the only way to see an entry leak is to count the entries; ED-119), so this reads the proxy's private state by
// reflection: the Mutex's boxed State struct and its Connections list.
namespace Marrow.Services.Tests.Fixtures;

internal static class MCPProxyConnections
{
    public static int Open(MCPProxy proxy)
    {
        var mutex = typeof(MCPProxy).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(proxy)!;
        var box = mutex.GetType().GetField("value", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mutex)!;
        var connections = (System.Collections.ICollection)box.GetType().GetField("Connections", BindingFlags.Instance | BindingFlags.Public)!.GetValue(box)!;
        return connections.Count;
    }

    /// <summary>Waits until the proxy holds exactly <paramref name="expected"/> connections, or fails after a bound.</summary>
    public static async Task WaitForOpen(MCPProxy proxy, int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (Open(proxy) != expected)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"The proxy holds {Open(proxy)} connections, wanted {expected}.");
            await Task.Delay(10);
        }
    }
}
