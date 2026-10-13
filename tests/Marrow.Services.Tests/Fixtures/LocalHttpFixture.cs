using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>What the fixture answers a request with.</summary>
public sealed record LocalReply(int Status, string Body, string ContentType = "text/html; charset=utf-8")
{
    public string? Location { get; init; }

    public string? SetCookie { get; init; }

    /// <summary>Send the body in chunks with no Content-Length (a stream of unknown size).</summary>
    public bool Chunked { get; init; }

    /// <summary>Answer nothing, and hold the connection open.</summary>
    public bool Hang { get; init; }

    /// <summary>Declare this many bytes though the body is shorter (a Content-Length the server then does not fill is not used; this is the declared size only).</summary>
    public long? DeclaredLength { get; init; }
}

/// <summary>
/// PORT-ONLY (AS-10): a tiny HTTP/1.1 server on 127.0.0.1 for the page-watch tests: a socket, a request line, a reply. It needs no
/// URL reservation, keeps nothing between requests, and records what each request carried.
/// </summary>
public sealed class LocalHttpFixture : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly List<(string Path, string Headers)> requests = [];

    public LocalHttpFixture()
    {
        listener.Start();
        _ = Task.Run(async () =>
        {
            try { await Serve().ConfigureAwait(false); }
            finally { stop.Dispose(); }
        });
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public string Url(string path = "/") => $"http://127.0.0.1:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}{path}";

    /// <summary>Answers a path. The default is a 404.</summary>
    public Func<string, LocalReply> Reply { get; set; } = _ => new LocalReply(404, "not found");

    public IReadOnlyList<(string Path, string Headers)> Requests
    {
        get
        {
            lock (requests) return [.. requests];
        }
    }

    private async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var head = new StringBuilder();
                while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, stop.Token).ConfigureAwait(false);
                    if (read == 0) return;
                    head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                var lines = head.ToString().Split("\r\n");
                var path = lines[0].Split(' ')[1];
                lock (requests) requests.Add((path, string.Join("\n", lines.Skip(1))));
                var reply = Reply(path);
                if (reply.Hang)
                {
                    await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
                    return;
                }
                var body = Encoding.UTF8.GetBytes(reply.Body);
                var header = new StringBuilder();
                header.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"HTTP/1.1 {reply.Status} Reply\r\n"));
                header.Append("Content-Type: ").Append(reply.ContentType).Append("\r\n");
                if (reply.Location is not null) header.Append("Location: ").Append(reply.Location).Append("\r\n");
                if (reply.SetCookie is not null) header.Append("Set-Cookie: ").Append(reply.SetCookie).Append("\r\n");
                header.Append(reply.Chunked
                    ? "Transfer-Encoding: chunked\r\n"
                    : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Content-Length: {reply.DeclaredLength ?? body.Length}\r\n"));
                header.Append("Connection: close\r\n\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()), stop.Token).ConfigureAwait(false);
                if (reply.Chunked)
                {
                    for (var at = 0; at < body.Length; at += 4096)
                    {
                        var size = Math.Min(4096, body.Length - at);
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(size.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "\r\n"), stop.Token).ConfigureAwait(false);
                        await stream.WriteAsync(body.AsMemory(at, size), stop.Token).ConfigureAwait(false);
                        await stream.WriteAsync("\r\n"u8.ToArray(), stop.Token).ConfigureAwait(false);
                    }
                    await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), stop.Token).ConfigureAwait(false);
                }
                else if (reply.DeclaredLength is null)
                {
                    await stream.WriteAsync(body, stop.Token).ConfigureAwait(false);
                }
                await stream.FlushAsync(stop.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // the client went away
            }
        }
    }

    private bool disposed;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        listener.Stop();
        // The serve loop ends by itself once the listener is stopped and the token is cancelled; it lets go of the token as its last act.
    }
}
