using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture (R-TEST-10: a network call in a test goes to loopback, never to the internet). A listening socket on
// 127.0.0.1 that only counts the connections made to it: a feed address that is reached shows up here as a connection,
// whatever the client then sends, and nothing is answered. Polled from the test's own thread (no accept loop, no async work),
// so it is safe beside a test body that blocks the headless dispatcher.
public sealed class LoopbackConnectionCounter : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private int accepted;

    public LoopbackConnectionCounter() => listener.Start();

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>An absolute http URL on this listener for a path that starts with "/".</summary>
    public string Url(string path) => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + path;

    /// <summary>How many connections have been made to the listener so far.</summary>
    public int Connections()
    {
        while (listener.Pending())
        {
            listener.AcceptTcpClient().Dispose();
            accepted++;
        }
        return accepted;
    }

    public void Dispose() => listener.Stop();
}
