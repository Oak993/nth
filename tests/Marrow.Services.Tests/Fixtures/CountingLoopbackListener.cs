using System.Net;
using System.Net.Sockets;

// PORT-ONLY test fixture, no Swift twin: a loopback TCP listener that counts the connections made to it and drops
// each one at once. MCPStoreOAuthProbeTests points a GitLab entry's "https://{instance}/api/v4/mcp" address at it
// (instance = "127.0.0.1:<port>"): every attempt to reach the OAuth upstream shows up here as one connection, and
// the TLS handshake the client starts is never answered, so nothing leaves the machine and no certificate is
// involved. The catalog has no OAuth server on a plain-http address (R-TEST-10: no internet, loopback only).
namespace Marrow.Services.Tests.Fixtures;

internal sealed class CountingLoopbackListener : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task acceptLoop;
    private int accepted;

    public CountingLoopbackListener()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        acceptLoop = AcceptLoop();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>"127.0.0.1:port": what a user types into the GitLab address field.</summary>
    public string HostAndPort => $"127.0.0.1:{Port}";

    /// <summary>Connections made since construction or the last <see cref="Reset"/>.</summary>
    public int Accepted => Volatile.Read(ref accepted);

    public void Reset() => Volatile.Write(ref accepted, 0);

    private async Task AcceptLoop()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stopping.Token); }
            catch { return; }
            Interlocked.Increment(ref accepted);
            client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        try { await acceptLoop; } catch { }
        stopping.Dispose();
    }
}
