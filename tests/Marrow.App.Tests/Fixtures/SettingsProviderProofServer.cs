using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Marrow.App.Tests.Fixtures;

// R-TEST-10: loopback HTTP fixture drains POST bodies before closing the TCP connection.
internal sealed class SettingsProviderProofServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly Func<ReceivedRequest, Task<CannedResponse?>> respond;
    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private readonly List<ReceivedRequest> requests = [];

    public SettingsProviderProofServer(Func<ReceivedRequest, Task<CannedResponse?>> respond)
    {
        this.respond = respond;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = AcceptLoop();
    }

    public SettingsProviderProofServer(Func<ReceivedRequest, CannedResponse?> respond)
        : this(request => Task.FromResult(respond(request)))
    {
    }

    public int Port { get; }
    public string? PostBody { get; private set; }

    /// <summary>An absolute http URL on this server for a path that starts with "/" (and may carry a query).</summary>
    public string Url(string pathAndQuery) =>
        "http://127.0.0.1:" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + pathAndQuery;

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
                var length = request.HeaderValues("Content-Length").FirstOrDefault();
                if (length is not null)
                {
                    var payload = new byte[int.Parse(length, System.Globalization.CultureInfo.InvariantCulture)];
                    await stream.ReadExactlyAsync(payload, stop.Token);
                    if (request.Method == "POST") PostBody = Encoding.UTF8.GetString(payload);
                }
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
        head.Append("HTTP/1.1 ").Append(answer.Status.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
            .Append(answer.Reason).Append("\r\n");
        var hasLength = false;
        foreach (var (name, value) in answer.Headers)
        {
            head.Append(name).Append(": ").Append(value).Append("\r\n");
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) hasLength = true;
        }
        if (!hasLength)
            head.Append("Content-Length: ").Append(answer.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
        head.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), stop.Token);
        if (!string.Equals(request.Method, "HEAD", StringComparison.Ordinal))
            await stream.WriteAsync(answer.Body, stop.Token);
        await stream.FlushAsync(stop.Token);
    }
}
