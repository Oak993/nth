using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin: a scripted HTTP/1.1 server on 127.0.0.1 for the HTTP unit
// (IMPLEMENTING.md 4.3 "First tests: a loopback server"; R-TEST-10: no internet, HTTP is a loopback server).
// Each request the client sends becomes one LoopbackExchange; the test decides, byte by byte, what the server
// answers and when, so timing and failure shapes are driven by the test and not by a real server's defaults.
// The fixture never reaches the network beyond the loopback interface and never reads a file.
public sealed class LoopbackHttpServer : IAsyncDisposable
{
    /// <summary>
    /// A hang guard only: every await on the fixture or on the client under test is bounded by it so a broken
    /// implementation fails the test instead of hanging the run. No expectation is measured against it.
    /// </summary>
    public static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    private readonly TcpListener listener;
    private readonly Channel<LoopbackExchange> exchanges = Channel.CreateUnbounded<LoopbackExchange>();
    private readonly CancellationTokenSource stopping = new();
    private readonly List<TcpClient> connections = [];
    private readonly Lock gate = new();
    private int connectionCount;

    public LoopbackHttpServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = AcceptLoop();
    }

    public int Port { get; }

    /// <summary>How many TCP connections the client opened so far.</summary>
    public int ConnectionCount
    {
        get { lock (gate) return connectionCount; }
    }

    public Uri Url(string pathAndQuery) =>
        new(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{Port}{pathAndQuery}"));

    /// <summary>The next request the client sent, in arrival order.</summary>
    public Task<LoopbackExchange> NextExchange() => exchanges.Reader.ReadAsync(stopping.Token).AsTask().WaitAsync(Guard);

    /// <summary>
    /// A loopback URL whose port is held for the whole test run by a socket that is bound with exclusive address use
    /// and never listens: no other listener in the parallel run can take the port, and every connection attempt to it
    /// is answered with a reset (refused). A port bound and then released could be reused by another test's server.
    /// </summary>
    public static Uri RefusedUrl(string pathAndQuery)
    {
        var port = ((IPEndPoint)RefusedPortHolder.Value.LocalEndPoint!).Port;
        return new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{pathAndQuery}"));
    }

    private static readonly Lazy<Socket> RefusedPortHolder = new(() =>
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    });

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        List<TcpClient> open;
        lock (gate) open = [.. connections];
        foreach (var connection in open) connection.Dispose();
        stopping.Dispose();
    }

    private async Task AcceptLoop()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            lock (gate)
            {
                connections.Add(client);
                connectionCount += 1;
            }
            _ = Serve(client);
        }
    }

    private async Task Serve(TcpClient client)
    {
        var stream = client.GetStream();
        var buffer = new List<byte>();
        var chunk = new byte[16 * 1024];
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                // The request head, up to the empty line.
                int headEnd;
                while ((headEnd = IndexOfHeadEnd(buffer)) < 0)
                {
                    var read = await stream.ReadAsync(chunk, stopping.Token);
                    if (read == 0) return;
                    buffer.AddRange(chunk.AsSpan(0, read).ToArray());
                }
                var head = buffer.GetRange(0, headEnd + 4).ToArray();
                buffer.RemoveRange(0, headEnd + 4);
                var lines = Encoding.Latin1.GetString(head, 0, headEnd).Split("\r\n");
                var requestLine = lines[0].Split(' ');
                var headers = new List<(string Name, string Value)>();
                for (var i = 1; i < lines.Length; i++)
                {
                    var colon = lines[i].IndexOf(':', StringComparison.Ordinal);
                    headers.Add((lines[i][..colon], lines[i][(colon + 1)..].Trim(' ', '\t')));
                }
                var body = await ReadBody(stream, buffer, chunk, headers);
                var exchange = new LoopbackExchange(client, stream, requestLine[0], requestLine[1], headers, head, body);
                await exchanges.Writer.WriteAsync(exchange, stopping.Token);
                // HTTP/1.1 without pipelining: the next request on this connection comes after this response.
                if (await exchange.Finished.WaitAsync(stopping.Token)) return;
            }
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client or the test closed the connection.
        }
    }

    private async Task<byte[]> ReadBody(NetworkStream stream, List<byte> buffer, byte[] chunk, List<(string Name, string Value)> headers)
    {
        var length = headers.Where(h => string.Equals(h.Name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(h => long.Parse(h.Value, NumberStyles.None, CultureInfo.InvariantCulture)).FirstOrDefault();
        var chunked = headers.Any(h => string.Equals(h.Name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                                       && h.Value.Contains("chunked", StringComparison.OrdinalIgnoreCase));
        if (!chunked)
        {
            while (buffer.Count < length)
            {
                var read = await stream.ReadAsync(chunk, stopping.Token);
                if (read == 0) throw new IOException("closed inside the request body");
                buffer.AddRange(chunk.AsSpan(0, read).ToArray());
            }
            var body = buffer.GetRange(0, checked((int)length)).ToArray();
            buffer.RemoveRange(0, checked((int)length));
            return body;
        }
        var decoded = new List<byte>();
        while (true)
        {
            int lineEnd;
            while ((lineEnd = IndexOf(buffer, "\r\n"u8)) < 0)
            {
                var read = await stream.ReadAsync(chunk, stopping.Token);
                if (read == 0) throw new IOException("closed inside a request chunk");
                buffer.AddRange(chunk.AsSpan(0, read).ToArray());
            }
            var sizeText = Encoding.Latin1.GetString(buffer.GetRange(0, lineEnd).ToArray()).Split(';')[0].Trim();
            var size = int.Parse(sizeText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            buffer.RemoveRange(0, lineEnd + 2);
            while (buffer.Count < size + 2)
            {
                var read = await stream.ReadAsync(chunk, stopping.Token);
                if (read == 0) throw new IOException("closed inside a request chunk");
                buffer.AddRange(chunk.AsSpan(0, read).ToArray());
            }
            decoded.AddRange(buffer.GetRange(0, size));
            buffer.RemoveRange(0, size + 2);
            if (size == 0) return [.. decoded];
        }
    }

    private static int IndexOfHeadEnd(List<byte> buffer) => IndexOf(buffer, "\r\n\r\n"u8);

    private static int IndexOf(List<byte> buffer, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i + needle.Length <= buffer.Count; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++) match = buffer[i + j] == needle[j];
            if (match) return i;
        }
        return -1;
    }
}

/// <summary>One request as the server received it, and the scripted answer to it.</summary>
public sealed class LoopbackExchange
{
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly TaskCompletionSource<bool> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal LoopbackExchange(TcpClient client, NetworkStream stream, string method, string target,
        IReadOnlyList<(string Name, string Value)> headers, byte[] rawHead, byte[] body)
    {
        this.client = client;
        this.stream = stream;
        Method = method;
        Target = target;
        Headers = headers;
        RawHead = rawHead;
        Body = body;
    }

    public string Method { get; }

    /// <summary>The request target as sent, path and query.</summary>
    public string Target { get; }

    /// <summary>Every header line in wire order, names as sent.</summary>
    public IReadOnlyList<(string Name, string Value)> Headers { get; }

    /// <summary>The request line and header lines exactly as received, including the final empty line.</summary>
    public byte[] RawHead { get; }

    /// <summary>The request body, de-chunked when the client sent it chunked.</summary>
    public byte[] Body { get; }

    /// <summary>True when the connection was closed after this exchange, false when it stays open for the next request.</summary>
    internal Task<bool> Finished => finished.Task;

    /// <summary>Every value of the header, matched case-insensitively (header names are case-insensitive in HTTP).</summary>
    public IReadOnlyList<string> HeaderValues(string name) =>
        Headers.Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToList();

    /// <summary>Writes a status line and the given header lines, then the empty line. Nothing is added.</summary>
    public Task WriteHead(int status, string reason, params (string Name, string Value)[] headers)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {status} {reason}\r\n");
        foreach (var (name, value) in headers) text.Append(CultureInfo.InvariantCulture, $"{name}: {value}\r\n");
        text.Append("\r\n");
        return Write(Encoding.Latin1.GetBytes(text.ToString()));
    }

    public async Task Write(byte[] bytes)
    {
        await stream.WriteAsync(bytes).AsTask().WaitAsync(LoopbackHttpServer.Guard);
        await stream.FlushAsync().WaitAsync(LoopbackHttpServer.Guard);
    }

    /// <summary>A complete response with a Content-Length; the connection stays open for the next request.</summary>
    public async Task Respond(int status, string reason, byte[] body, params (string Name, string Value)[] headers)
    {
        await WriteHead(status, reason,
            [.. headers, ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture))]);
        await Write(body);
        Finish();
    }

    /// <summary>A status line and headers with Transfer-Encoding: chunked; the body follows through WriteChunk.</summary>
    public Task WriteChunkedHead(int status, string reason, params (string Name, string Value)[] headers) =>
        WriteHead(status, reason, [.. headers, ("Transfer-Encoding", "chunked")]);

    public Task WriteChunk(byte[] data)
    {
        var size = Encoding.Latin1.GetBytes(data.Length.ToString("x", CultureInfo.InvariantCulture) + "\r\n");
        return Write([.. size, .. data, .. "\r\n"u8.ToArray()]);
    }

    /// <summary>The last chunk; the connection stays open for the next request.</summary>
    public async Task EndChunks()
    {
        await Write("0\r\n\r\n"u8.ToArray());
        Finish();
    }

    /// <summary>The response is complete (for example a HEAD answer); the connection stays open.</summary>
    public void Finish() => finished.TrySetResult(false);

    /// <summary>Severs the connection with a TCP reset, as a dropped link does.</summary>
    public void Abort()
    {
        client.Client.LingerState = new LingerOption(true, 0);
        client.Close();
        finished.TrySetResult(true);
    }
}
