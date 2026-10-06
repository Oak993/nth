using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture (R-TEST-10: a network call in a test goes to loopback, never to the internet). A listening socket on
// 127.0.0.1 that only counts the connections made to it: a feed address that is reached shows up here as a connection,
// with an optional invalid-JSON response when the test ends an in-flight check. Polled from the test's own thread (no accept loop, no async work),
// so it is safe beside a test body that blocks the headless dispatcher.
public sealed class LoopbackConnectionCounter : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private int accepted;
    private readonly bool completeWithInvalidJson;

    public LoopbackConnectionCounter(bool completeWithInvalidJson = false)
    {
        this.completeWithInvalidJson = completeWithInvalidJson;
        listener.Start();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>An absolute http URL on this listener for a path that starts with "/".</summary>
    public string Url(string path) => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + path;

    /// <summary>How many connections have been made to the listener so far.</summary>
    public int Connections()
    {
        while (listener.Pending())
        {
            using var client = listener.AcceptTcpClient();
            if (completeWithInvalidJson)
            {
                // Closing an unanswered GET lets HttpClient retry on some platforms, leaving a second connection waiting.
                // End this scripted in-flight check with a complete HTTP response and invalid JSON instead: the checker
                // still fails, on one real loopback request, with no transport retry or real-time timeout.
                client.ReceiveTimeout = 2000;
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
                while (reader.ReadLine() is { Length: > 0 }) { }
                stream.Write(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1\r\nConnection: close\r\n\r\n{"));
            }
            accepted++;
        }
        return accepted;
    }

    public void Dispose() => listener.Stop();
}
