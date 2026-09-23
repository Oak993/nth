using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

// PORT-ONLY: a minimal HTTP/1.1 server for MCPProxyTests, so upstream forwarding (headers, status, SSE
// framing) can be observed without a real MCP server. Not a Swift twin.
namespace Marrow.Services.Tests.Fixtures;

internal sealed record FakeRequest(string Method, string Path, Dictionary<string, string> Headers, byte[] Body);

internal sealed record FakeResponse(
    int Status,
    (string Name, string Value)[] Headers,
    byte[] Body,
    string[]? SseChunks = null);

internal sealed class FakeUpstreamServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cts = new();
    private readonly Task acceptTask;

    public Func<FakeRequest, FakeResponse> Handler { get; set; } = _ => new FakeResponse(200, [], []);
    public List<FakeRequest> Received { get; } = [];

    public FakeUpstreamServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        acceptTask = AcceptLoop();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    private async Task AcceptLoop()
    {
        while (!cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(cts.Token); }
            catch { return; }
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var owned = client;
        var stream = owned.GetStream();
        var buffer = new List<byte>();
        var chunk = new byte[8192];
        var divider = -1;
        while (divider < 0)
        {
            int n;
            try { n = await stream.ReadAsync(chunk); } catch { return; }
            if (n <= 0) return;
            buffer.AddRange(chunk.AsSpan(0, n).ToArray());
            for (var i = 0; i + 4 <= buffer.Count && divider < 0; i++)
                if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10) divider = i;
        }
        var headText = Encoding.Latin1.GetString(buffer.GetRange(0, divider).ToArray());
        var body = buffer.GetRange(divider + 4, buffer.Count - divider - 4).ToArray();
        var lines = headText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var method = requestLine[0];
        var path = requestLine.Length > 1 ? requestLine[1] : "/";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var contentLength = headers.TryGetValue("Content-Length", out var clText) && int.TryParse(clText, out var cl) ? cl : 0;
        while (body.Length < contentLength)
        {
            int n;
            try { n = await stream.ReadAsync(chunk); } catch { break; }
            if (n <= 0) break;
            var combined = new byte[body.Length + n];
            body.CopyTo(combined, 0);
            Array.Copy(chunk, 0, combined, body.Length, n);
            body = combined;
        }
        var request = new FakeRequest(method, path, headers, body);
        Received.Add(request);
        var response = Handler(request);
        if (response.SseChunks is not null)
        {
            var head = new StringBuilder("HTTP/1.1 " + response.Status.ToString(CultureInfo.InvariantCulture) + " X\r\n");
            foreach (var (name, value) in response.Headers) head.Append(name).Append(": ").Append(value).Append("\r\n");
            head.Append("\r\n");
            await stream.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()));
            foreach (var text in response.SseChunks)
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
                await stream.FlushAsync();
            }
        }
        else
        {
            var head = new StringBuilder("HTTP/1.1 " + response.Status.ToString(CultureInfo.InvariantCulture) + " X\r\n");
            foreach (var (name, value) in response.Headers) head.Append(name).Append(": ").Append(value).Append("\r\n");
            head.Append("Content-Length: ").Append(response.Body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n\r\n");
            await stream.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()));
            await stream.WriteAsync(response.Body);
        }
    }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        listener.Stop();
        try { await acceptTask; } catch { }
    }
}
