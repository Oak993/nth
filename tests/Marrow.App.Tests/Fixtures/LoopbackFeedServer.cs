using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture (R-TEST-10: HTTP in a test is a loopback server, never the internet). A minimal
// HTTP/1.1 server on 127.0.0.1 standing in for the release feed (UpdateChecker.swift:189-195) and the download host
// the size HEAD goes to (UpdateChecker.swift:221-229). Every connection carries one exchange and is closed after
// the answer ("Connection: close"), so each request the checker sends is recorded once, in arrival order. The
// answer comes from the test's respond function; a null answer closes the connection without a response, and a
// respond task that never completes holds the request open until the server is disposed.
public sealed class LoopbackFeedServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly Func<ReceivedRequest, Task<CannedResponse?>> respond;
    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private readonly List<ReceivedRequest> requests = [];

    public LoopbackFeedServer(Func<ReceivedRequest, Task<CannedResponse?>> respond)
    {
        this.respond = respond;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = AcceptLoop();
    }

    public LoopbackFeedServer(Func<ReceivedRequest, CannedResponse?> respond)
        : this(request => Task.FromResult(respond(request)))
    {
    }

    public int Port { get; }

    /// <summary>An absolute http URL on this server for a path that starts with "/" (and may carry a query).</summary>
    public string Url(string pathAndQuery) =>
        "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + pathAndQuery;

    public IReadOnlyList<ReceivedRequest> Requests
    {
        get { lock (gate) return requests.ToArray(); }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }

    private async Task AcceptLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch (Exception) { return; }
            _ = Serve(client);
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = await ReadHead(stream);
                if (head is null) return;
                var request = Parse(head);
                lock (gate) requests.Add(request);
                var answer = await respond(request).WaitAsync(stop.Token);
                if (answer is null) return;
                await Write(stream, request, answer);
            }
            catch (Exception)
            {
                // A client that gave up (a timeout, a cancelled request) or a disposed server ends the exchange.
            }
        }
    }

    private async Task<string?> ReadHead(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (buffer.Count < 65536)
        {
            var read = await stream.ReadAsync(one, stop.Token);
            if (read == 0) return null;
            buffer.Add(one[0]);
            var n = buffer.Count;
            if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n')
                return Encoding.Latin1.GetString(buffer.ToArray(), 0, n - 4);
        }
        return null;
    }

    private static ReceivedRequest Parse(string head)
    {
        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) continue;
            headers.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
        return new ReceivedRequest(requestLine[0], requestLine.Length > 1 ? requestLine[1] : "", headers);
    }

    private async Task Write(NetworkStream stream, ReceivedRequest request, CannedResponse answer)
    {
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(answer.Status.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(answer.Reason).Append("\r\n");
        var hasLength = false;
        foreach (var (name, value) in answer.Headers)
        {
            head.Append(name).Append(": ").Append(value).Append("\r\n");
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) hasLength = true;
        }
        if (!hasLength)
            head.Append("Content-Length: ").Append(answer.Body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        head.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), stop.Token);
        if (!string.Equals(request.Method, "HEAD", StringComparison.Ordinal))
            await stream.WriteAsync(answer.Body, stop.Token);
        await stream.FlushAsync(stop.Token);
    }
}

/// <summary>One request as the server read it: method, request target and header lines in wire order.</summary>
public sealed record ReceivedRequest(string Method, string Target, IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    public IReadOnlyList<string> HeaderValues(string name) =>
        Headers.Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToArray();
}

/// <summary>An answer: status line, extra header lines and a body (never sent for HEAD).</summary>
public sealed record CannedResponse(int Status, string Reason, byte[] Body, IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    public static CannedResponse Json(string json, int status = 200, string reason = "OK", params KeyValuePair<string, string>[] headers) =>
        new(status, reason, Encoding.UTF8.GetBytes(json),
            [new("Content-Type", "application/json"), .. headers]);

    public static CannedResponse Plain(int status, string reason) => new(status, reason, [], []);

    /// <summary>A HEAD answer that announces <paramref name="contentLength"/> bytes (the body is never sent).</summary>
    public static CannedResponse HeadWithLength(int status, long contentLength) =>
        new(status, status == 200 ? "OK" : "Status", [],
            [new("Content-Type", "application/octet-stream"), new("Content-Length", contentLength.ToString(CultureInfo.InvariantCulture))]);
}
