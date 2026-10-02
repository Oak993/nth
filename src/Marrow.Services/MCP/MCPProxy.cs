using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;

// Source: DroppyCode/Services/MCP/MCPProxy.swift (B3 52bc792), :1-477.
// ENGINEERING DECISION: NWListener/NWConnection (Network.framework) -> System.Net.Sockets.TcpListener/TcpClient,
// a loopback socket proxy (R-NET-2). NWListener's port-bind failure surfaces asynchronously through a state
// machine (:113-132); TcpListener.Start() fails synchronously instead. Both failure shapes are folded into one
// catch around Start() (DEVIATION platform_adaptation), which plays the async ".failed" branch and its
// repick-once behavior (PR-MCP-124); the ":107-111" construction-failure branch is unreachable for a valid
// loopback ushort port on either platform and is kept only as the log text it would use.
namespace Marrow.Services.MCP;

// :6-477
public sealed class MCPProxy
{
    // :8. A static let shared that tests replace (R-LIFE-2).
    public static Ambient<MCPProxy> Shared { get; } = new(() => new MCPProxy());

    // :7 (ED-163: subsystem "iordv.marrow", category "mcp-proxy", per the Logger convention in this tree).
    private static readonly Logger Log = new(Logger.Subsystem, "mcp-proxy");

    // :16-27. Held for the process lifetime (SOURCE FACT): a Swift `static let` runs its initializer once, on
    // first access, and C#'s static field initializer does the same.
    private static readonly Mutex<ushort> portBox = new(LoadPort(PortFile));

    // :17
    private static string PortFile => SwiftPath.AppendingPathComponent(MCPPaths.Directory, "proxy-port");

    // :14
    public static ushort Port => portBox.WithLock((ref ushort value) => value);

    // :19-27
    internal static ushort LoadPort(string portFile)
    {
        var bytes = SwiftTry.Optional(() => File.ReadAllBytes(portFile)); // :20 String(contentsOf:encoding:) (R-FS-1)
        var text = bytes is not null ? SwiftText.DecodeUtf8OrNil(bytes) : null;
        if (text is not null && ParseUInt16(text.Trimmed()) is { } saved && saved != 0) return saved; // :21-22
        var picked = PickFreePort(); // :24
        SwiftTry.Ignore(() => FileOps.WriteAtomic(portFile, Encoding.UTF8.GetBytes(picked.ToString(CultureInfo.InvariantCulture)))); // :25
        return picked;
    }

    // :30-35
    private static ushort RepickPort()
    {
        var picked = PickFreePort(); // :31
        portBox.WithLock((ref ushort value) => value = picked); // :32
        SwiftTry.Ignore(() => FileOps.WriteAtomic(PortFile, Encoding.UTF8.GetBytes(picked.ToString(CultureInfo.InvariantCulture)))); // :33
        return picked;
    }

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests): PR-MCP-124's bind-failure/repick path (:113-132)
    // needs a second instance, isolated from the collection's shared MCPProxy.Shared.Current, so exercising a
    // failed bind on the fixture's already-listening port doesn't disturb the singleton the rest of the
    // collection depends on.
    internal static MCPProxy CreateForTests() => new();

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests): RepickPort (above) mutates the static portBox
    // and rewrites PortFile unconditionally - both process-wide, shared by every MCPProxy instance including
    // MCPProxy.Shared.Current. A test that forces a repick must rewind this afterward, or MCPProxy.Port (and
    // MCPProxy.Url) stop matching MCPProxySingletonFixture.Port for the rest of the collection.
    internal static void ResetPortForTests(ushort port)
    {
        portBox.WithLock((ref ushort value) => value = port);
        SwiftTry.Ignore(() => FileOps.WriteAtomic(PortFile, Encoding.UTF8.GetBytes(port.ToString(CultureInfo.InvariantCulture))));
    }

    // :19-27 UInt16(text.trimmed): unsigned, no leading '-', overflow is nil.
    private static ushort? ParseUInt16(string text)
    {
        if (text.Length == 0 || text[0] == '-') return null;
        return SwiftNumber.ParseInt(text) is { } value && value is >= 0 and <= ushort.MaxValue ? (ushort)value : null;
    }

    // :37-39. Swift: url(for serverID: String).
    public static string Url(string @for) => $"http://127.0.0.1:{Port}/mcp/{@for}";

    // :41-45
    private sealed record Route(string Upstream, IReadOnlyDictionary<string, string> Headers, bool UsesOAuth);

    // :51-53
    private const int HeadLimit = 65_536;
    private const int BodyLimit = 32 * 1_024 * 1_024;

    // :57-62. A Swift `struct` held only inside the Mutex box, never observed outside the lock: a plain mutable
    // field bag (as HubConversation.State and Storage.State already do in this tree) rather than R-VAL-1's
    // immutable record, since nothing here is a Swift-facing model value.
    private struct State
    {
        public Dictionary<string, Route> Routes;
        public TcpListener? Listener;
        public List<TcpClient> Connections;
        public bool Running;
    }

    // :64
    private readonly Mutex<State> state = new(new State
    {
        Routes = new Dictionary<string, Route>(StringComparer.Ordinal),
        Connections = [],
    });

    // :66
    private MCPProxy() { }

    // :68-72
    public void Register(string serverID, string upstream, IReadOnlyDictionary<string, string> headers, bool usesOAuth) =>
        state.WithLock((ref State s) => s.Routes[serverID] = new Route(upstream, headers, usesOAuth));

    // :74-76
    public void Unregister(string serverID) =>
        state.WithLock((ref State s) => s.Routes.Remove(serverID));

    // :78-84
    public void ReplaceAll(
        IReadOnlyList<(string ServerID, string Upstream, IReadOnlyDictionary<string, string> Headers, bool UsesOAuth)> routes) =>
        state.WithLock((ref State s) =>
        {
            var next = new Dictionary<string, Route>(StringComparer.Ordinal);
            foreach (var entry in routes)
            {
                if (!next.TryAdd(entry.ServerID, new Route(entry.Upstream, entry.Headers, entry.UsesOAuth)))
                    throw SwiftTraps.Fatal("Dictionary literal contains duplicate keys"); // :80 uniqueKeysWithValues (R-COLL-3)
            }
            s.Routes = next;
        });

    // :86-94
    public void Start()
    {
        var alreadyRunning = state.WithLock((ref State s) =>
        {
            if (s.Running) return true;
            s.Running = true;
            return false;
        });
        if (alreadyRunning) return;
        Listen(Port, canRepick: true);
    }

    // :101-137
    private void Listen(ushort port, bool canRepick)
    {
        TcpListener listener = new(IPAddress.Loopback, port); // :102-106 loopback only, never 0.0.0.0
        try
        {
            listener.Start();
        }
        catch (Exception error)
        {
            // :117-128, folded with :107-111 (see the file header note).
            Log.Error($"listener on {port} failed: {error.Message}"); // :118
            SwiftTry.Ignore(listener.Stop);
            if (canRepick)
            {
                Listen(RepickPort(), canRepick: false); // :125
            }
            else
            {
                state.WithLock((ref State s) => s.Running = false); // :127
            }
            return;
        }
        state.WithLock((ref State s) => s.Listener = listener); // :112
        Log.Info($"listening on {port}"); // :116
        _ = AcceptLoopAsync(listener);
    }

    // :133-135
    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch
            {
                return; // the listener was stopped (:146)
            }
            Accept(client);
        }
    }

    // :154-173. A loopback port nobody holds, from the kernel: bind port 0, read back what it gave, let it go.
    private static ushort PickFreePort()
    {
        Socket socket;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); // :158
        }
        catch
        {
            return 48123; // :159
        }
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0)); // :168
            var picked = ((IPEndPoint)socket.LocalEndPoint!).Port; // :171
            return picked == 0 ? (ushort)48123 : (ushort)picked; // :172
        }
        catch
        {
            return 48123; // :170
        }
        finally
        {
            socket.Close(); // :160 defer { close(socket) }
        }
    }

    // :175-179
    private void Accept(TcpClient client)
    {
        state.WithLock((ref State s) => s.Connections.Add(client)); // :176
        _ = HandleConnectionAsync(client);
    }

    // :181-183
    private void Forget(TcpClient client) =>
        state.WithLock((ref State s) => s.Connections.Remove(client));

    private void CloseAndForget(TcpClient client)
    {
        SwiftTry.Ignore(client.Close);
        Forget(client);
    }

    // :185-412. NWConnection.receive's callback chain becomes one async method per connection: same reads,
    // same limits, same silent/erroring branches, in the same order.
    private async Task HandleConnectionAsync(TcpClient client)
    {
        NetworkStream stream;
        try
        {
            stream = client.GetStream();
        }
        catch
        {
            CloseAndForget(client);
            return;
        }
        try
        {
            var head = await ReadHeadAsync(client, stream).ConfigureAwait(false);
            if (head is not { } parts) return; // already responded or closed inside ReadHeadAsync
            await HandleExchangeAsync(client, stream, parts.Head, parts.Tail).ConfigureAwait(false);
        }
        catch
        {
            CloseAndForget(client);
        }
    }

    // :185-212
    private async Task<(byte[] Head, byte[] Tail)?> ReadHeadAsync(TcpClient client, NetworkStream stream)
    {
        var buffer = new List<byte>();
        var chunk = new byte[8192];
        while (true)
        {
            int n;
            try
            {
                n = await stream.ReadAsync(chunk).ConfigureAwait(false); // :186
            }
            catch
            {
                n = -1;
            }
            if (n > 0) buffer.AddRange(chunk.AsSpan(0, n).ToArray()); // :192-194
            var divider = FindDivider(buffer);
            if (divider >= 0) // :195
            {
                var head = buffer.GetRange(0, divider).ToArray(); // :196
                var rest = buffer.GetRange(divider + 4, buffer.Count - divider - 4).ToArray(); // :197
                return (head, rest);
            }
            if (buffer.Count > HeadLimit) // :201
            {
                await RespondAsync(client, stream, 413, JsonError("request head too large")).ConfigureAwait(false); // :202
                return null;
            }
            if (n <= 0) // :205 isComplete || error
            {
                CloseAndForget(client); // :206-207
                return null;
            }
        }
    }

    private static int FindDivider(List<byte> data)
    {
        for (var i = 0; i + 4 <= data.Count; i++)
        {
            if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10) return i; // \r\n\r\n
        }
        return -1;
    }

    // :214-230
    private static (string Method, string Path, Dictionary<string, string> Headers)? ParseHead(byte[] head)
    {
        var text = Encoding.Latin1.GetString(head); // :215 String(data:encoding: .isoLatin1), never fails
        var lines = text.Split("\r\n"); // :216
        if (lines.Length == 0) return null; // :217
        var requestLine = lines[0];
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries); // :218
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0) return null; // :219
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++) // :221
        {
            var line = lines[i];
            var colon = line.IndexOf(':', StringComparison.Ordinal); // :222
            if (colon < 0) continue;
            var name = SwiftString.Lowercased(line[..colon].Trim(' ', '\t')); // :223
            var value = line[(colon + 1)..].Trim(' ', '\t'); // :224
            if (name.Length > 0) headers[name] = value; // :225-227
        }
        return (parts[0], parts[1], headers); // :229
    }

    // :232-252
    private async Task HandleExchangeAsync(TcpClient client, NetworkStream stream, byte[] head, byte[] rest)
    {
        if (ParseHead(head) is not { } parsed) // :233
        {
            await RespondAsync(client, stream, 400, JsonError("bad request")).ConfigureAwait(false); // :234
            return;
        }
        var (method, path, headers) = parsed;
        if (headers.TryGetValue("transfer-encoding", out var transferEncoding) &&
            SwiftString.Lowercased(transferEncoding).Contains("chunked", StringComparison.Ordinal)) // :237-238
        {
            await RespondAsync(client, stream, 411, JsonError("chunked request bodies are not supported")).ConfigureAwait(false); // :239
            return;
        }
        var contentLengthLong = 0L;
        if (headers.TryGetValue("content-length", out var contentLengthText) &&
            SwiftNumber.ParseInt(contentLengthText) is { } parsedLength) // :242
        {
            contentLengthLong = Math.Max(0, parsedLength);
        }
        if (contentLengthLong > BodyLimit) // :243
        {
            await RespondAsync(client, stream, 413, JsonError("request body too large")).ConfigureAwait(false); // :244
            return;
        }
        var contentLength = (int)contentLengthLong;
        byte[] body = rest.Length >= contentLength
            ? rest[..contentLength] // :248
            : await ReadBodyAsync(rest, contentLength, stream).ConfigureAwait(false); // :250
        await RouteRequestAsync(client, stream, method, path, headers, body).ConfigureAwait(false);
    }

    // :254-276
    private static async Task<byte[]> ReadBodyAsync(byte[] initial, int need, NetworkStream stream)
    {
        var buffer = new List<byte>(initial);
        if (buffer.Count >= need) return buffer.GetRange(0, need).ToArray(); // :255-256
        while (true)
        {
            var chunk = new byte[Math.Min(Math.Max(need - buffer.Count, 1), HeadLimit)]; // :259
            int n;
            try
            {
                n = await stream.ReadAsync(chunk).ConfigureAwait(false);
            }
            catch
            {
                n = -1;
            }
            if (n > 0) buffer.AddRange(chunk.AsSpan(0, n).ToArray()); // :265-267
            if (buffer.Count >= need) return buffer.GetRange(0, need).ToArray(); // :268-269
            if (n <= 0) return buffer.ToArray(); // :270-271, truncated relay, no error (PR-MCP-129, upstream defect 12)
        }
    }

    // :278-309
    private async Task RouteRequestAsync(
        TcpClient client, NetworkStream stream, string method, string path,
        IReadOnlyDictionary<string, string> clientHeaders, byte[] body)
    {
        var questionMark = path.IndexOf('?', StringComparison.Ordinal);
        var bare = questionMark >= 0 ? path[..questionMark] : path; // :279
        if (!SwiftString.HasPrefix(bare, "/mcp/")) // :282
        {
            await RespondAsync(client, stream, 502, JsonError("unknown server")).ConfigureAwait(false); // :283
            return;
        }
        var serverID = bare["/mcp/".Length..]; // :286
        if (serverID.Length == 0 || serverID.Contains('/', StringComparison.Ordinal)) // :287
        {
            await RespondAsync(client, stream, 502, JsonError("unknown server")).ConfigureAwait(false); // :288
            return;
        }
        var route = state.WithLock((ref State s) => s.Routes.TryGetValue(serverID, out var found) ? found : null); // :291
        if (route is null) // :292
        {
            await RespondAsync(client, stream, 502, JsonError("unknown server")).ConfigureAwait(false); // :293
            return;
        }
        // ENGINEERING DECISION: :297 spawns a detached Task; here the request runs inline in the same async
        // chain the connection already owns (equivalent observable behavior, no Swift task-cancellation seam
        // to preserve, R-TASK-1 is inapplicable to unstructured fire-and-forget work).
        try
        {
            await SendUpstreamAsync(client, stream, serverID, route, method, clientHeaders, body).ConfigureAwait(false); // :303
        }
        catch (Exception error)
        {
            Log.Error($"request to '{serverID}' failed: {SafeLocalizedDescription(error)}"); // :305
            await RespondAsync(client, stream, 502, JsonError("upstream request failed")).ConfigureAwait(false); // :306
        }
    }

    // ENGINEERING DECISION: :305's Logger.error uses error.localizedDescription, which never fails in Swift. A
    // raw .NET exception with no ILocalizedError/ISwiftErrorIdentity has no recorded Foundation text
    // (SwiftErrors.LocalizedDescription then throws, per R-ERR-2's "needs a recorded decision"); since this
    // string is diagnostic only, never the response body sent to the client, it falls back to error.Message
    // rather than losing the whole exchange to a log call. A URLError is not such an exception any more: its
    // description is the Windows networking error's own message (OD-0056).
    private static string SafeLocalizedDescription(Exception error) =>
        SwiftTry.Optional(() => SwiftErrors.LocalizedDescription(error)) ?? error.Message;

    // :311-345. OD-0022: the Authorization header this method sets is the only per-route secret any producer
    // ever populates (Route.headers is dead configuration, PR-MCP-126); it is protected across a cross-origin
    // redirect by F.HTTP's existing Authorization handling (ED-152), inherited here unchanged.
    private static readonly (string ClientName, string CanonicalName)[] Passthrough =
    [
        ("content-type", "Content-Type"),
        ("accept", "Accept"),
        ("mcp-session-id", "Mcp-Session-Id"),
        ("mcp-protocol-version", "MCP-Protocol-Version"),
        ("last-event-id", "Last-Event-ID"),
    ]; // :318-324

    private async Task SendUpstreamAsync(
        TcpClient client, NetworkStream stream, string serverID, Route route, string method,
        IReadOnlyDictionary<string, string> clientHeaders, byte[] body)
    {
        if (!Uri.TryCreate(route.Upstream, UriKind.Absolute, out var upstreamUrl)) throw new URLError(URLError.Code.BadURL); // :312-314
        var request = new URLRequest(upstreamUrl, timeoutInterval: 3_600) { HttpMethod = method }; // :315-317
        foreach (var (clientName, canonicalName) in Passthrough)
        {
            if (clientHeaders.TryGetValue(clientName, out var value)) request = request.SetValue(value, canonicalName); // :325-329
        }
        foreach (var (name, value) in route.Headers) request = request.SetValue(value, name); // :330-332
        if (route.UsesOAuth) // :333
        {
            string token;
            try
            {
                token = await MCPOAuth.AccessToken(serverID).ConfigureAwait(false); // :336
            }
            catch (Exception error)
            {
                await RespondAsync(client, stream, 401, JsonError(SwiftErrors.LocalizedDescription(error))).ConfigureAwait(false); // :338
                return;
            }
            request = request.SetValue($"Bearer {token}", "Authorization"); // :341
        }
        if (body.Length > 0) request = request with { HttpBody = body }; // :343-345
        var (bytesStream, response) = await URLSession.Shared.Bytes(request).ConfigureAwait(false); // :346
        var http = response as HTTPURLResponse; // :347
        if (route.UsesOAuth && http?.StatusCode == 401) // :348
        {
            if (await SwiftTry.OptionalAsync(() => MCPOAuth.AccessToken(serverID)!).ConfigureAwait(false) is { } refreshed) // :349
            {
                request = request.SetValue($"Bearer {refreshed}", "Authorization"); // :350
                (bytesStream, response) = await URLSession.Shared.Bytes(request).ConfigureAwait(false); // :351
                http = response as HTTPURLResponse; // :352
            }
        }
        if (http is null) throw new URLError(URLError.Code.BadServerResponse); // :355-357
        await WriteBackAsync(client, stream, http, bytesStream).ConfigureAwait(false); // :358
    }

    // :361-411
    private async Task WriteBackAsync(TcpClient client, NetworkStream stream, HTTPURLResponse response, URLSession.AsyncBytes stream_)
    {
        var headers = new List<(string Name, string Value)>();
        var contentType = response.Value("Content-Type"); // :362
        if (contentType is not null) headers.Add(("Content-Type", contentType)); // :364-366
        if (response.Value("Mcp-Session-Id") is { } sessionID) headers.Add(("Mcp-Session-Id", sessionID)); // :367-369
        if (response.Value("Cache-Control") is { } cacheControl) headers.Add(("Cache-Control", cacheControl)); // :370-372

        if (contentType is not null && SwiftString.Lowercased(contentType).Contains("text/event-stream", StringComparison.Ordinal)) // :374
        {
            headers.Add(("Transfer-Encoding", "chunked")); // :375
            headers.Add(("Connection", "close")); // :376
            await SendAsync(stream, Encoding.UTF8.GetBytes(StatusHead(response.StatusCode, headers))).ConfigureAwait(false); // :377
            try
            {
                // :379-389. One chunk per upstream line: bytes accumulate in `buffer` until a 0x0A is seen, then
                // the whole buffer (through that newline) is sent as one chunk and cleared (upstream_defects.md
                // entry 10, no longer reproduced: B2 sent one byte-sized "1\r\n"-framed chunk per byte).
                var buffer = new List<byte>();
                await foreach (var b in stream_.ConfigureAwait(false)) // :380
                {
                    buffer.Add(b); // :381
                    if (b == (byte)'\n') // :382
                    {
                        await SendAsync(stream, Chunk(buffer)).ConfigureAwait(false); // :383
                        buffer.Clear(); // :384, removeAll(keepingCapacity:) - List<T>.Clear() keeps its backing capacity too
                    }
                }
                if (buffer.Count > 0) // :387
                {
                    await SendAsync(stream, Chunk(buffer)).ConfigureAwait(false); // :388
                }
            }
            catch
            {
                // :390-391. A stream error ends the relay silently and DROPS the unsent remainder still in
                // `buffer`; the terminating chunk below is still sent.
            }
            await SendAsync(stream, "0\r\n\r\n"u8.ToArray()).ConfigureAwait(false); // :392
            CloseAndForget(client); // :393-394
        }
        else
        {
            var payload = new List<byte>();
            await foreach (var b in stream_.ConfigureAwait(false)) payload.Add(b); // :396-402, NOT caught: propagates to the caller. The
            // reserveCapacity call at :397-399 is not observable and is not ported (lane note).
            headers.Add(("Content-Length", payload.Count.ToString(CultureInfo.InvariantCulture))); // :403
            headers.Add(("Connection", "close")); // :404
            var head = Encoding.UTF8.GetBytes(StatusHead(response.StatusCode, headers)); // :405
            var outBytes = new byte[head.Length + payload.Count];
            head.CopyTo(outBytes, 0);
            payload.CopyTo(outBytes, head.Length); // :406
            await SendAsync(stream, outBytes).ConfigureAwait(false); // :407
            CloseAndForget(client); // :408-409
        }
    }

    // :413-418. String(payload.count, radix: 16): lowercase hexadecimal, no leading zeros - int.ToString("x")
    // matches (culture-insensitive; InvariantCulture passed only for the codebase's usual explicitness).
    private static byte[] Chunk(List<byte> payload)
    {
        var head = Encoding.ASCII.GetBytes(payload.Count.ToString("x", CultureInfo.InvariantCulture) + "\r\n"); // :414
        var frame = new byte[head.Length + payload.Count + 2];
        head.CopyTo(frame, 0);
        payload.CopyTo(frame, head.Length); // :415
        frame[^2] = (byte)'\r';
        frame[^1] = (byte)'\n'; // :416
        return frame; // :417
    }

    // :420-426. Swift's send(_:_:) is declared non-throwing (CheckedContinuation<Void, Never>) and its
    // completion's error is discarded at every call site, so a failed local write to a client that has already
    // disconnected is always silent, never observable. NetworkStream.WriteAsync has no such guarantee - it
    // throws IOException/ObjectDisposedException on a reset or already-closed socket - so this swallows here,
    // once, so every call site (WriteBackAsync's three writes above, RespondAsync's own) inherits the same
    // never-throws contract uniformly instead of each needing its own try/catch.
    private static async Task SendAsync(NetworkStream stream, byte[] data)
    {
        try
        {
            await stream.WriteAsync(data).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests): asserts SendAsync's never-throws contract
    // directly, since it is otherwise only reachable through the private call sites above.
    internal static Task SendAsyncForTests(NetworkStream stream, byte[] data) => SendAsync(stream, data);

    // :428-439
    private async Task RespondAsync(TcpClient client, NetworkStream stream, int status, byte[] body)
    {
        var head = Encoding.UTF8.GetBytes(StatusHead(status,
        [
            ("Content-Type", "application/json"),
            ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture)),
            ("Connection", "close"),
        ])); // :429-433
        var outBytes = new byte[head.Length + body.Length];
        head.CopyTo(outBytes, 0);
        body.CopyTo(outBytes, head.Length); // :434
        try
        {
            await SendAsync(stream, outBytes).ConfigureAwait(false);
        }
        catch
        {
            // the send's own completion drives cancel+forget below regardless (:435-438)
        }
        CloseAndForget(client);
    }

    // :441-448
    private static string StatusHead(int status, IReadOnlyList<(string Name, string Value)> headers)
    {
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(ReasonPhrase(status)).Append("\r\n"); // :442
        foreach (var (name, value) in headers) head.Append(name).Append(": ").Append(value).Append("\r\n"); // :443-445
        head.Append("\r\n"); // :446
        return head.ToString();
    }

    // :450-467
    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        411 => "Length Required",
        413 => "Content Too Large",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        _ => status is >= 200 and < 300 ? "OK" : "Error",
    };

    // :469-476
    private static byte[] JsonError(string message)
    {
        var escaped = message
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal); // :470-474
        return Encoding.UTF8.GetBytes($"{{\"error\":\"{escaped}\"}}"); // :475
    }

    // :139-152. No call sites at B2/B3, kept for shape (PR-MCP-138).
    public void Stop()
    {
        var (listener, open) = state.WithLock((ref State s) =>
        {
            var currentListener = s.Listener; // :141
            var currentOpen = new List<TcpClient>(s.Connections); // :142
            s.Listener = null; // :143
            s.Connections.Clear(); // :144
            s.Running = false; // :145
            return (currentListener, currentOpen);
        });
        SwiftTry.Ignore(() => listener?.Stop()); // :148
        foreach (var connection in open) SwiftTry.Ignore(connection.Close); // :149-151
    }
}
