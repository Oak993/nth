using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Marrow.Testing.Downloads;

// PORT-ONLY test fixture, no Swift twin (SK-0 = DL-0 of skills-ONDEMAND-PLAN.md section 7; reused by SK-3, VT-7 and UE-2;
// R-TEST-10: no internet, HTTP is a loopback server): a scripted HTTP/1.1 download server on a loopback address, over TLS with a
// test certificate (StartHttps) or in plain text (StartPlainHttp, the target of a redirect to http). It serves FakeArtifacts under
// paths, answers each route as its FakeBehavior says, and logs every request it answered so a test can check what crossed the wire.
// It never reaches beyond the loopback interface and never reads a file. Only GET and HEAD are served; a request body is read and
// ignored.
public sealed class FakeDownloadServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stopping = new();
    private readonly TimeProvider clock;
    private readonly X509Certificate2? trustedCertificate;
    private readonly X509Certificate2? untrustedCertificate;
    private readonly Lock gate = new();
    private readonly Dictionary<string, Route> routes = new(StringComparer.Ordinal);
    private readonly List<FakeDownloadRequest> log = [];
    private readonly List<(int Count, TaskCompletionSource Done)> logWaiters = [];
    private readonly List<TcpClient> connections = [];
    private readonly List<Hold> holds = [];
    private int connectionCount;
    private int arrivals;
    private int inFlight;
    private int peakInFlight;

    private FakeDownloadServer(IPAddress address, bool https, TimeProvider? clock)
    {
        // The fixture never listens beyond the loopback interface (R-TEST-10).
        if (!IPAddress.IsLoopback(address)) throw new ArgumentException("A fake download server listens on loopback only.", nameof(address));
        this.clock = clock ?? TimeProvider.System;
        if (https)
        {
            trustedCertificate = FakeTlsCertificates.Create("Marrow fake download server");
            untrustedCertificate = FakeTlsCertificates.Create("Marrow fake download server (untrusted)");
        }
        listener = new TcpListener(address, 0);
        listener.Start();
        EndPoint = (IPEndPoint)listener.LocalEndpoint;
        _ = AcceptLoop();
    }

    /// <summary>A hang guard only, as <see cref="LoopbackHttpServer.Guard"/>: no expectation is measured against it.</summary>
    public static TimeSpan Guard => LoopbackHttpServer.Guard;

    /// <summary>
    /// An https server on <paramref name="address"/> (127.0.0.1 unless given; 127.0.0.2 is a second loopback host for a redirect
    /// "to another host") with a certificate made for this server. <paramref name="clock"/> times the slow trickle (the system
    /// clock unless a test passes a <see cref="ManualTimeProvider"/>).
    /// </summary>
    public static FakeDownloadServer StartHttps(string address = "127.0.0.1", TimeProvider? clock = null) =>
        new(IPAddress.Parse(address), https: true, clock);

    /// <summary>A plain-text http server on <paramref name="address"/>: the target of a redirect to http.</summary>
    public static FakeDownloadServer StartPlainHttp(string address = "127.0.0.1", TimeProvider? clock = null) =>
        new(IPAddress.Parse(address), https: false, clock);

    public IPEndPoint EndPoint { get; }

    public bool IsHttps => trustedCertificate is not null;

    /// <summary>The certificate the handler of <see cref="FakeDownloadTransport"/> trusts for this server; null for a plain server.</summary>
    public X509Certificate2? TrustedCertificate => trustedCertificate;

    /// <summary>
    /// When true the server presents a second, self-signed certificate that nothing trusts: the handshake fails on the client
    /// (the "TLS failure" case) and no request is ever read. Switchable at any time; read once per connection.
    /// </summary>
    public bool PresentUntrustedCertificate { get; set; }

    /// <summary>How many TCP connections were accepted.</summary>
    public int ConnectionCount
    {
        get { lock (gate) return connectionCount; }
    }

    /// <summary>The most requests that were being answered at the same moment, from the head read to the last body byte.</summary>
    public int PeakConcurrentRequests
    {
        get { lock (gate) return peakInFlight; }
    }

    /// <summary>Every request answered so far, in arrival order.</summary>
    public IReadOnlyList<FakeDownloadRequest> Requests
    {
        get { lock (gate) return [.. log.OrderBy(r => r.Sequence)]; }
    }

    public Uri Url(string path) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{(IsHttps ? "https" : "http")}://{HostText}:{EndPoint.Port}{path}"));

    private string HostText => EndPoint.Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{EndPoint.Address}]" : EndPoint.Address.ToString();

    /// <summary>Serves <paramref name="artifact"/> at <paramref name="path"/> ("/files/a.zip"; the query is ignored).</summary>
    public FakeDownloadServer Serve(string path, FakeArtifact artifact, FakeBehavior? behavior = null)
    {
        lock (gate) routes[path] = new Route(artifact, behavior ?? new FakeBehavior());
        return this;
    }

    /// <summary>Replaces how <paramref name="path"/> answers, from the next request on. The route's request count carries on.</summary>
    public void Behave(string path, FakeBehavior behavior)
    {
        lock (gate)
        {
            var route = routes.TryGetValue(path, out var existing) ? existing : throw new InvalidOperationException("No route " + path);
            route.Behavior = behavior;
        }
    }

    /// <summary>Completes when at least <paramref name="count"/> requests have been answered; returns the log at that moment.</summary>
    public async Task<IReadOnlyList<FakeDownloadRequest>> WaitForRequests(int count)
    {
        Task wait;
        lock (gate)
        {
            if (log.Count >= count) return Requests;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            logWaiters.Add((count, done));
            wait = done.Task;
        }
        await wait.WaitAsync(Guard);
        return Requests;
    }

    /// <summary>
    /// From now on a request that has been read waits, before any byte is answered, until the hold is released. A test that needs
    /// several downloads in flight at once holds them, waits for the arrivals, and releases.
    /// </summary>
    public FakeHold HoldRequests()
    {
        var hold = new Hold();
        lock (gate) holds.Add(hold);
        return new FakeHold(hold);
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        List<TcpClient> open;
        lock (gate) open = [.. connections];
        foreach (var connection in open) connection.Dispose();
        lock (gate) foreach (var hold in holds) hold.Release();
        stopping.Dispose();
        trustedCertificate?.Dispose();
        untrustedCertificate?.Dispose();
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
            int id;
            lock (gate)
            {
                connections.Add(client);
                id = ++connectionCount;
            }
            _ = Serve(client, id);
        }
    }

    private async Task Serve(TcpClient client, int connectionId)
    {
        try
        {
            Stream stream = client.GetStream();
            if (trustedCertificate is not null)
            {
                var tls = new SslStream(stream);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = PresentUntrustedCertificate ? untrustedCertificate : trustedCertificate,
                    ApplicationProtocols = [SslApplicationProtocol.Http11],
                    ClientCertificateRequired = false,
                }, stopping.Token);
                stream = tls;
            }
            var buffer = new List<byte>();
            var chunk = new byte[16 * 1024];
            while (!stopping.IsCancellationRequested)
            {
                int headEnd;
                while ((headEnd = IndexOfHeadEnd(buffer)) < 0)
                {
                    var read = await stream.ReadAsync(chunk, stopping.Token);
                    if (read == 0) return;
                    buffer.AddRange(chunk.AsSpan(0, read).ToArray());
                }
                var lines = Encoding.Latin1.GetString(buffer.GetRange(0, headEnd).ToArray()).Split("\r\n");
                buffer.RemoveRange(0, headEnd + 4);
                var requestLine = lines[0].Split(' ');
                var headers = new List<(string Name, string Value)>();
                for (var i = 1; i < lines.Length; i++)
                {
                    var colon = lines[i].IndexOf(':', StringComparison.Ordinal);
                    headers.Add((lines[i][..colon], lines[i][(colon + 1)..].Trim(' ', '\t')));
                }
                var bodyLength = headers.Where(h => h.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    .Select(h => long.Parse(h.Value, NumberStyles.None, CultureInfo.InvariantCulture)).FirstOrDefault();
                while (buffer.Count < bodyLength)
                {
                    var read = await stream.ReadAsync(chunk, stopping.Token);
                    if (read == 0) return;
                    buffer.AddRange(chunk.AsSpan(0, read).ToArray());
                }
                buffer.RemoveRange(0, (int)bodyLength);
                if (!await Answer(client, stream, connectionId, requestLine[0], requestLine[1], headers)) return;
            }
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException or OperationCanceledException
                                          or System.Security.Authentication.AuthenticationException)
        {
            // The client left, the handshake was refused (an untrusted certificate), or the test is over.
        }
    }

    /// <summary>Answers one request; false when the connection must not be used again.</summary>
    private async Task<bool> Answer(TcpClient client, Stream stream, int connectionId, string method, string target,
        List<(string Name, string Value)> headers)
    {
        int sequence;
        Hold[] pending;
        lock (gate)
        {
            sequence = ++arrivals;
            inFlight += 1;
            peakInFlight = Math.Max(peakInFlight, inFlight);
            pending = [.. holds];
        }
        var path = target.Split('?')[0];
        var range = headers.Where(h => h.Name.Equals("Range", StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).FirstOrDefault();
        var status = 0;
        long promised = 0, sent = 0;
        bool dropped = false, clientGone = false, reusable = true;
        try
        {
            foreach (var hold in pending) await hold.Arrive(stopping.Token);
            Route? route;
            int requestNumber = 0;
            lock (gate)
            {
                routes.TryGetValue(path, out route);
                if (route is not null) requestNumber = ++route.Requests;
            }
            var plan = route is null ? NotFound() : Plan(route, requestNumber, method, range);
            status = plan.Status;
            promised = plan.PromisedLength;
            var head = new StringBuilder();
            head.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {plan.Status} {plan.Reason}\r\n");
            foreach (var (name, value) in plan.Headers) head.Append(CultureInfo.InvariantCulture, $"{name}: {value}\r\n");
            head.Append(CultureInfo.InvariantCulture, $"Content-Length: {plan.PromisedLength}\r\nConnection: keep-alive\r\n\r\n");
            await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), stopping.Token);
            await stream.FlushAsync(stopping.Token);
            if (method != "HEAD")
            {
                var body = plan.Body;
                var limit = plan.DropAfter is { } drop ? Math.Min(drop, body.Length) : body.Length;
                var step = plan.Trickle?.ChunkBytes ?? Math.Max(limit, 1);
                for (var offset = 0; offset < limit; offset += step)
                {
                    if (offset > 0 && plan.Trickle is { } trickle) await Task.Delay(trickle.Interval, clock, stopping.Token);
                    var count = Math.Min(step, limit - offset);
                    await stream.WriteAsync(body.AsMemory(offset, count), stopping.Token);
                    await stream.FlushAsync(stopping.Token);
                    sent += count;
                }
                if (plan.DropAfter is { } cut && cut < body.Length)
                {
                    // A cut stream: the promised length is not met and the connection ends (FIN, after the bytes written).
                    dropped = true;
                    reusable = false;
                    client.Client.Shutdown(SocketShutdown.Send);
                    client.Close();
                }
            }
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
        {
            clientGone = true;
            reusable = false;
        }
        finally
        {
            Record(new FakeDownloadRequest(sequence, connectionId, method, target, path, range, RangeStart(range), status, promised,
                sent, dropped, clientGone, headers));
        }
        return reusable;
    }

    private static Reply NotFound() =>
        new(404, "Not Found", "no such file\n"u8.ToArray(), [("Content-Type", "text/plain")], null, null);

    private Reply Plan(Route route, int requestNumber, string method, string? range)
    {
        FakeBehavior behavior;
        lock (gate) behavior = route.Behavior;
        var faulty = requestNumber <= behavior.FaultyRequests;
        if (faulty && behavior.RedirectTo is { } location)
            return new Reply(behavior.RedirectStatus, ReasonFor(behavior.RedirectStatus), [], [("Location", location.OriginalString)], null, null);
        if (faulty && behavior.Status is { } status)
            return new Reply(status, ReasonFor(status), Encoding.ASCII.GetBytes($"status {status}\n"), [("Content-Type", "text/plain")], null, null);

        var content = route.Artifact.Bytes;
        if (faulty && behavior.WrongByteAt is { } index)
        {
            content = [.. content];
            content[index] ^= 0xFF;
        }
        if (faulty && behavior.WrongLengthDelta != 0)
            content = behavior.WrongLengthDelta < 0
                ? content[..Math.Max(0, content.Length + behavior.WrongLengthDelta)]
                : [.. content, .. new byte[behavior.WrongLengthDelta]];

        var headers = new List<(string Name, string Value)> { ("Content-Type", "application/octet-stream") };
        var total = content.Length;
        var body = content;
        var code = 200;
        if (behavior.AcceptRanges)
        {
            headers.Add(("Accept-Ranges", "bytes"));
            if (RangeStart(range) is { } start && method is "GET" or "HEAD")
            {
                if (start >= total)
                    return new Reply(416, "Range Not Satisfiable", [], [.. headers, ("Content-Range", $"bytes */{total}")], null, null);
                code = 206;
                body = content[(int)start..];
                headers.Add(("Content-Range", $"bytes {start}-{total - 1}/{total}"));
            }
        }
        return new Reply(code, ReasonFor(code), body, headers, faulty ? behavior.DropAfterBytes : null, faulty ? behavior.Trickle : null);
    }

    private static long? RangeStart(string? range)
    {
        if (range is null || !range.StartsWith("bytes=", StringComparison.Ordinal)) return null;
        var spec = range["bytes=".Length..];
        var dash = spec.IndexOf('-', StringComparison.Ordinal);
        if (dash <= 0 || spec.Contains(',', StringComparison.Ordinal)) return null;
        return long.TryParse(spec.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : null;
    }

    private static string ReasonFor(int status) => status switch
    {
        200 => "OK",
        206 => "Partial Content",
        301 => "Moved Permanently",
        302 => "Found",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        404 => "Not Found",
        410 => "Gone",
        416 => "Range Not Satisfiable",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "Status",
    };

    private void Record(FakeDownloadRequest request)
    {
        List<TaskCompletionSource> ready = [];
        lock (gate)
        {
            inFlight -= 1;
            log.Add(request);
            for (var i = logWaiters.Count - 1; i >= 0; i--)
            {
                if (log.Count < logWaiters[i].Count) continue;
                ready.Add(logWaiters[i].Done);
                logWaiters.RemoveAt(i);
            }
        }
        foreach (var done in ready) done.TrySetResult();
    }

    private static int IndexOfHeadEnd(List<byte> buffer)
    {
        for (var i = 0; i + 4 <= buffer.Count; i++)
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n') return i;
        return -1;
    }

    private sealed record Reply(int Status, string Reason, byte[] Body, IReadOnlyList<(string Name, string Value)> Headers, int? DropAfter,
        FakeTrickle? Trickle)
    {
        public long PromisedLength { get; } = Status == 416 ? 0 : Body.Length;
    }

    private sealed class Route(FakeArtifact artifact, FakeBehavior behavior)
    {
        public FakeArtifact Artifact { get; } = artifact;

        public FakeBehavior Behavior { get; set; } = behavior;

        public int Requests { get; set; }
    }

    internal sealed class Hold
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock gate = new();
        private readonly List<(int Count, TaskCompletionSource Done)> waiters = [];
        private int arrived;

        internal async Task Arrive(CancellationToken cancel)
        {
            lock (gate)
            {
                arrived += 1;
                foreach (var waiter in waiters.Where(w => arrived >= w.Count)) waiter.Done.TrySetResult();
            }
            await released.Task.WaitAsync(cancel);
        }

        internal Task WaitForArrivals(int count)
        {
            lock (gate)
            {
                if (arrived >= count) return Task.CompletedTask;
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Add((count, done));
                return done.Task.WaitAsync(Guard);
            }
        }

        internal void Release() => released.TrySetResult();
    }
}

/// <summary>A hold on a <see cref="FakeDownloadServer"/> (see <see cref="FakeDownloadServer.HoldRequests"/>).</summary>
public sealed class FakeHold
{
    private readonly FakeDownloadServer.Hold hold;

    internal FakeHold(FakeDownloadServer.Hold hold) => this.hold = hold;

    /// <summary>Completes when this many requests, counted from the hold's start, have been read and are waiting.</summary>
    public Task WaitForArrivals(int count) => hold.WaitForArrivals(count);

    /// <summary>Lets the waiting requests and every later one through.</summary>
    public void Release() => hold.Release();
}
