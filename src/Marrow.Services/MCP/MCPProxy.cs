using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;

// Source: DroppyCode/Services/MCP/MCPProxy.swift (B2 1fbf81e), :1-460.
// ENGINEERING DECISION: NWListener/NWConnection (Network.framework) -> System.Net.Sockets.TcpListener/TcpClient,
// a loopback socket proxy (R-NET-2). NWListener's port-bind failure surfaces asynchronously through a state
// machine (:111-130); TcpListener.Start() fails synchronously instead. Both failure shapes are folded into one
// catch around Start() (DEVIATION platform_adaptation), which plays the async ".failed" branch and its
// repick-once behavior (PR-MCP-124); the ":105-109" construction-failure branch is unreachable for a valid
// loopback ushort port on either platform and is kept only as the print text it would use.
namespace Marrow.Services.MCP;

// :5-460
public sealed class MCPProxy
{
    // :6. A static let shared that tests replace (R-LIFE-2).
    public static Ambient<MCPProxy> Shared { get; } = new(() => new MCPProxy());

    // :14-25. Held for the process lifetime (SOURCE FACT): a Swift `static let` runs its initializer once, on
    // first access, and C#'s static field initializer does the same.
    private static readonly Mutex<ushort> portBox = new(LoadPort(PortFile));

    // :15
    private static string PortFile => SwiftPath.AppendingPathComponent(MCPPaths.Directory, "proxy-port");

    // :12
    public static ushort Port => portBox.WithLock((ref ushort value) => value);

    // :17-25
    internal static ushort LoadPort(string portFile)
    {
        var bytes = SwiftTry.Optional(() => File.ReadAllBytes(portFile)); // :18 String(contentsOf:encoding:) (R-FS-1)
        var text = bytes is not null ? SwiftText.DecodeUtf8OrNil(bytes) : null;
        if (text is not null && ParseUInt16(text.Trimmed()) is { } saved && saved != 0) return saved; // :19-20
        var picked = PickFreePort(); // :22
        SwiftTry.Ignore(() => FileOps.WriteAtomic(portFile, Encoding.UTF8.GetBytes(picked.ToString(CultureInfo.InvariantCulture)))); // :23
        return picked;
    }

    // :28-33
    private static ushort RepickPort()
    {
        var picked = PickFreePort(); // :29
        portBox.WithLock((ref ushort value) => value = picked); // :30
        SwiftTry.Ignore(() => FileOps.WriteAtomic(PortFile, Encoding.UTF8.GetBytes(picked.ToString(CultureInfo.InvariantCulture)))); // :31
        return picked;
    }

    // :17-25 UInt16(text.trimmed): unsigned, no leading '-', overflow is nil.
    private static ushort? ParseUInt16(string text)
    {
        if (text.Length == 0 || text[0] == '-') return null;
        return SwiftNumber.ParseInt(text) is { } value && value is >= 0 and <= ushort.MaxValue ? (ushort)value : null;
    }

    // :35-37. Swift: url(for serverID: String).
    public static string Url(string @for) => $"http://127.0.0.1:{Port}/mcp/{@for}";

    // :39-43
    private sealed record Route(string Upstream, IReadOnlyDictionary<string, string> Headers, bool UsesOAuth);

    // :49-51
    private const int HeadLimit = 65_536;
    private const int BodyLimit = 32 * 1_024 * 1_024;

    // :55-60. A Swift `struct` held only inside the Mutex box, never observed outside the lock: a plain mutable
    // field bag (as HubConversation.State and Storage.State already do in this tree) rather than R-VAL-1's
    // immutable record, since nothing here is a Swift-facing model value.
    private struct State
    {
        public Dictionary<string, Route> Routes;
        public TcpListener? Listener;
        public List<TcpClient> Connections;
        public bool Running;
    }

    // :62
    private readonly Mutex<State> state = new(new State
    {
        Routes = new Dictionary<string, Route>(StringComparer.Ordinal),
        Connections = [],
    });

    // :64
    private MCPProxy() { }

    // :66-70
    public void Register(string serverID, string upstream, IReadOnlyDictionary<string, string> headers, bool usesOAuth) =>
        state.WithLock((ref State s) => s.Routes[serverID] = new Route(upstream, headers, usesOAuth));

    // :72-74
    public void Unregister(string serverID) =>
        state.WithLock((ref State s) => s.Routes.Remove(serverID));

    // :76-82
    public void ReplaceAll(
        IReadOnlyList<(string ServerID, string Upstream, IReadOnlyDictionary<string, string> Headers, bool UsesOAuth)> routes) =>
        state.WithLock((ref State s) =>
        {
            var next = new Dictionary<string, Route>(StringComparer.Ordinal);
            foreach (var entry in routes)
            {
                if (!next.TryAdd(entry.ServerID, new Route(entry.Upstream, entry.Headers, entry.UsesOAuth)))
                    throw SwiftTraps.Fatal("Dictionary literal contains duplicate keys"); // :78 uniqueKeysWithValues (R-COLL-3)
            }
            s.Routes = next;
        });

    // :84-92
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

    // :99-135
    private void Listen(ushort port, bool canRepick)
    {
        TcpListener listener = new(IPAddress.Loopback, port); // :100-104 loopback only, never 0.0.0.0
        try
        {
            listener.Start();
        }
        catch (Exception error)
        {
            // :115-126, folded with :105-109 (see the file header note).
            Console.WriteLine($"MCPProxy: listener on {port} failed: {error.Message}"); // :116
            SwiftTry.Ignore(listener.Stop);
            if (canRepick)
            {
                Listen(RepickPort(), canRepick: false); // :123
            }
            else
            {
                state.WithLock((ref State s) => s.Running = false); // :125
            }
            return;
        }
        state.WithLock((ref State s) => s.Listener = listener); // :110
        Console.WriteLine($"MCPProxy: listening on {port}"); // :114
        _ = AcceptLoopAsync(listener);
    }

    // :131-133
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

    // :152-171. A loopback port nobody holds, from the kernel: bind port 0, read back what it gave, let it go.
    private static ushort PickFreePort()
    {
        Socket socket;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); // :156
        }
        catch
        {
            return 48123; // :157
        }
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0)); // :166
            var picked = ((IPEndPoint)socket.LocalEndPoint!).Port; // :169
            return picked == 0 ? (ushort)48123 : (ushort)picked; // :170
        }
        catch
        {
            return 48123; // :168
        }
        finally
        {
            socket.Close(); // :158 defer { close(socket) }
        }
    }

    // :173-177
    private void Accept(TcpClient client)
    {
        state.WithLock((ref State s) => s.Connections.Add(client)); // :174
        _ = HandleConnectionAsync(client);
    }

    // :179-181
    private void Forget(TcpClient client) =>
        state.WithLock((ref State s) => s.Connections.Remove(client));

    private void CloseAndForget(TcpClient client)
    {
        SwiftTry.Ignore(client.Close);
        Forget(client);
    }

    // :183-402. NWConnection.receive's callback chain becomes one async method per connection: same reads,
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

    // :183-210
    private async Task<(byte[] Head, byte[] Tail)?> ReadHeadAsync(TcpClient client, NetworkStream stream)
    {
        var buffer = new List<byte>();
        var chunk = new byte[8192];
        while (true)
        {
            int n;
            try
            {
                n = await stream.ReadAsync(chunk).ConfigureAwait(false); // :184
            }
            catch
            {
                n = -1;
            }
            if (n > 0) buffer.AddRange(chunk.AsSpan(0, n).ToArray()); // :190-192
            var divider = FindDivider(buffer);
            if (divider >= 0) // :193
            {
                var head = buffer.GetRange(0, divider).ToArray(); // :194
                var rest = buffer.GetRange(divider + 4, buffer.Count - divider - 4).ToArray(); // :195
                return (head, rest);
            }
            if (buffer.Count > HeadLimit) // :199
            {
                await RespondAsync(client, stream, 413, JsonError("request head too large")).ConfigureAwait(false); // :200
                return null;
            }
            if (n <= 0) // :203 isComplete || error
            {
                CloseAndForget(client); // :204-205
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

    // :212-228
    private static (string Method, string Path, Dictionary<string, string> Headers)? ParseHead(byte[] head)
    {
        var text = Encoding.Latin1.GetString(head); // :213 String(data:encoding: .isoLatin1), never fails
        var lines = text.Split("\r\n"); // :214
        if (lines.Length == 0) return null; // :215
        var requestLine = lines[0];
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries); // :216
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0) return null; // :217
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++) // :219
        {
            var line = lines[i];
            var colon = line.IndexOf(':', StringComparison.Ordinal); // :220
            if (colon < 0) continue;
            var name = SwiftString.Lowercased(line[..colon].Trim(' ', '\t')); // :221
            var value = line[(colon + 1)..].Trim(' ', '\t'); // :222
            if (name.Length > 0) headers[name] = value; // :223-225
        }
        return (parts[0], parts[1], headers); // :227
    }

    // :230-250
    private async Task HandleExchangeAsync(TcpClient client, NetworkStream stream, byte[] head, byte[] rest)
    {
        if (ParseHead(head) is not { } parsed) // :231
        {
            await RespondAsync(client, stream, 400, JsonError("bad request")).ConfigureAwait(false); // :232
            return;
        }
        var (method, path, headers) = parsed;
        if (headers.TryGetValue("transfer-encoding", out var transferEncoding) &&
            SwiftString.Lowercased(transferEncoding).Contains("chunked", StringComparison.Ordinal)) // :235-236
        {
            await RespondAsync(client, stream, 411, JsonError("chunked request bodies are not supported")).ConfigureAwait(false); // :237
            return;
        }
        var contentLengthLong = 0L;
        if (headers.TryGetValue("content-length", out var contentLengthText) &&
            SwiftNumber.ParseInt(contentLengthText) is { } parsedLength) // :240
        {
            contentLengthLong = Math.Max(0, parsedLength);
        }
        if (contentLengthLong > BodyLimit) // :241
        {
            await RespondAsync(client, stream, 413, JsonError("request body too large")).ConfigureAwait(false); // :242
            return;
        }
        var contentLength = (int)contentLengthLong;
        byte[] body = rest.Length >= contentLength
            ? rest[..contentLength] // :246
            : await ReadBodyAsync(rest, contentLength, stream).ConfigureAwait(false); // :248
        await RouteRequestAsync(client, stream, method, path, headers, body).ConfigureAwait(false);
    }

    // :252-274
    private static async Task<byte[]> ReadBodyAsync(byte[] initial, int need, NetworkStream stream)
    {
        var buffer = new List<byte>(initial);
        if (buffer.Count >= need) return buffer.GetRange(0, need).ToArray(); // :253-254
        while (true)
        {
            var chunk = new byte[Math.Min(Math.Max(need - buffer.Count, 1), HeadLimit)]; // :257
            int n;
            try
            {
                n = await stream.ReadAsync(chunk).ConfigureAwait(false);
            }
            catch
            {
                n = -1;
            }
            if (n > 0) buffer.AddRange(chunk.AsSpan(0, n).ToArray()); // :263-265
            if (buffer.Count >= need) return buffer.GetRange(0, need).ToArray(); // :266-267
            if (n <= 0) return buffer.ToArray(); // :268-269, truncated relay, no error (PR-MCP-129, upstream defect 12)
        }
    }

    // :276-307
    private async Task RouteRequestAsync(
        TcpClient client, NetworkStream stream, string method, string path,
        IReadOnlyDictionary<string, string> clientHeaders, byte[] body)
    {
        var questionMark = path.IndexOf('?', StringComparison.Ordinal);
        var bare = questionMark >= 0 ? path[..questionMark] : path; // :277
        if (!SwiftString.HasPrefix(bare, "/mcp/")) // :280
        {
            await RespondAsync(client, stream, 502, JsonError("unknown server")).ConfigureAwait(false); // :281
            return;
        }
        var serverID = bare["/mcp/".Length..]; // :284
        if (serverID.Length == 0 || serverID.Contains('/', StringComparison.Ordinal)) // :285
        {
            await RespondAsync(client, stream, 502, JsonError("unknown server")).ConfigureAwait(false); // :286
            return;
        }
        var route = state.WithLock((ref State s) => s.Routes.TryGetValue(serverID, out var found) ? found : null); // :289
        if (route is null) // :290
        {
            await RespondAsync(client, stream, 502, JsonError("unknown server")).ConfigureAwait(false); // :291
            return;
        }
        // ENGINEERING DECISION: :295 spawns a detached Task; here the request runs inline in the same async
        // chain the connection already owns (equivalent observable behavior, no Swift task-cancellation seam
        // to preserve, R-TASK-1 is inapplicable to unstructured fire-and-forget work).
        try
        {
            await SendUpstreamAsync(client, stream, serverID, route, method, clientHeaders, body).ConfigureAwait(false); // :301
        }
        catch (Exception error)
        {
            Console.WriteLine($"MCPProxy: request to '{serverID}' failed: {SafeLocalizedDescription(error)}"); // :303
            await RespondAsync(client, stream, 502, JsonError("upstream request failed")).ConfigureAwait(false); // :304
        }
    }

    // ENGINEERING DECISION: :303's print uses error.localizedDescription, which never fails in Swift. A raw
    // .NET exception with no ILocalizedError/ISwiftErrorIdentity (a bare URLError, for instance) has no
    // recorded Foundation text (SwiftErrors.LocalizedDescription then throws, per R-ERR-2's "needs a recorded
    // decision"); since this string is diagnostic only, never the response body sent to the client, it falls
    // back to error.Message rather than losing the whole exchange to a print statement.
    private static string SafeLocalizedDescription(Exception error) =>
        SwiftTry.Optional(() => SwiftErrors.LocalizedDescription(error)) ?? error.Message;

    // :309-343. OD-0022: the Authorization header this method sets is the only per-route secret any producer
    // ever populates (Route.headers is dead configuration, PR-MCP-126); it is protected across a cross-origin
    // redirect by F.HTTP's existing Authorization handling (ED-152), inherited here unchanged.
    private static readonly (string ClientName, string CanonicalName)[] Passthrough =
    [
        ("content-type", "Content-Type"),
        ("accept", "Accept"),
        ("mcp-session-id", "Mcp-Session-Id"),
        ("mcp-protocol-version", "MCP-Protocol-Version"),
        ("last-event-id", "Last-Event-ID"),
    ]; // :316-322

    private async Task SendUpstreamAsync(
        TcpClient client, NetworkStream stream, string serverID, Route route, string method,
        IReadOnlyDictionary<string, string> clientHeaders, byte[] body)
    {
        if (!Uri.TryCreate(route.Upstream, UriKind.Absolute, out var upstreamUrl)) throw new URLError(URLError.Code.BadURL); // :310-312
        var request = new URLRequest(upstreamUrl, timeoutInterval: 3_600) { HttpMethod = method }; // :313-315
        foreach (var (clientName, canonicalName) in Passthrough)
        {
            if (clientHeaders.TryGetValue(clientName, out var value)) request = request.SetValue(value, canonicalName); // :323-327
        }
        foreach (var (name, value) in route.Headers) request = request.SetValue(value, name); // :328-330
        if (route.UsesOAuth) // :331
        {
            string token;
            try
            {
                token = await MCPOAuth.AccessToken(serverID).ConfigureAwait(false); // :334
            }
            catch (Exception error)
            {
                await RespondAsync(client, stream, 401, JsonError(SwiftErrors.LocalizedDescription(error))).ConfigureAwait(false); // :336
                return;
            }
            request = request.SetValue($"Bearer {token}", "Authorization"); // :339
        }
        if (body.Length > 0) request = request with { HttpBody = body }; // :341-343
        var (bytesStream, response) = await URLSession.Shared.Bytes(request).ConfigureAwait(false); // :344
        var http = response as HTTPURLResponse; // :345
        if (route.UsesOAuth && http?.StatusCode == 401) // :346
        {
            if (await SwiftTry.OptionalAsync(() => MCPOAuth.AccessToken(serverID)!).ConfigureAwait(false) is { } refreshed) // :347
            {
                request = request.SetValue($"Bearer {refreshed}", "Authorization"); // :348
                (bytesStream, response) = await URLSession.Shared.Bytes(request).ConfigureAwait(false); // :349
                http = response as HTTPURLResponse; // :350
            }
        }
        if (http is null) throw new URLError(URLError.Code.BadServerResponse); // :353-355
        await WriteBackAsync(client, stream, http, bytesStream).ConfigureAwait(false); // :356
    }

    // :359-401
    private async Task WriteBackAsync(TcpClient client, NetworkStream stream, HTTPURLResponse response, URLSession.AsyncBytes stream_)
    {
        var headers = new List<(string Name, string Value)>();
        var contentType = response.Value("Content-Type"); // :360
        if (contentType is not null) headers.Add(("Content-Type", contentType)); // :362-364
        if (response.Value("Mcp-Session-Id") is { } sessionID) headers.Add(("Mcp-Session-Id", sessionID)); // :365-367
        if (response.Value("Cache-Control") is { } cacheControl) headers.Add(("Cache-Control", cacheControl)); // :368-370

        if (contentType is not null && SwiftString.Lowercased(contentType).Contains("text/event-stream", StringComparison.Ordinal)) // :372
        {
            headers.Add(("Transfer-Encoding", "chunked")); // :373
            headers.Add(("Connection", "close")); // :374
            await SendAsync(stream, Encoding.UTF8.GetBytes(StatusHead(response.StatusCode, headers))).ConfigureAwait(false); // :375
            try
            {
                await foreach (var b in stream_.ConfigureAwait(false)) // :377
                {
                    await SendAsync(stream, [(byte)'1', (byte)'\r', (byte)'\n', b, (byte)'\r', (byte)'\n']).ConfigureAwait(false); // :378-381
                }
            }
            catch
            {
                // :383-384. A stream error ends the relay silently; the terminating chunk is still sent.
            }
            await SendAsync(stream, "0\r\n\r\n"u8.ToArray()).ConfigureAwait(false); // :385
            CloseAndForget(client); // :386-387
        }
        else
        {
            var payload = new List<byte>();
            await foreach (var b in stream_.ConfigureAwait(false)) payload.Add(b); // :390-392, NOT caught: propagates to the caller
            headers.Add(("Content-Length", payload.Count.ToString(CultureInfo.InvariantCulture))); // :393
            headers.Add(("Connection", "close")); // :394
            var head = Encoding.UTF8.GetBytes(StatusHead(response.StatusCode, headers)); // :395
            var outBytes = new byte[head.Length + payload.Count];
            head.CopyTo(outBytes, 0);
            payload.CopyTo(outBytes, head.Length); // :396
            await SendAsync(stream, outBytes).ConfigureAwait(false); // :397
            CloseAndForget(client); // :398-399
        }
    }

    private static async Task SendAsync(NetworkStream stream, byte[] data) =>
        await stream.WriteAsync(data).ConfigureAwait(false); // :403-409, a completion-callback continuation here is one write-and-await

    // :411-422
    private async Task RespondAsync(TcpClient client, NetworkStream stream, int status, byte[] body)
    {
        var head = Encoding.UTF8.GetBytes(StatusHead(status,
        [
            ("Content-Type", "application/json"),
            ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture)),
            ("Connection", "close"),
        ])); // :412-416
        var outBytes = new byte[head.Length + body.Length];
        head.CopyTo(outBytes, 0);
        body.CopyTo(outBytes, head.Length); // :417
        try
        {
            await SendAsync(stream, outBytes).ConfigureAwait(false);
        }
        catch
        {
            // the send's own completion drives cancel+forget below regardless (:418-421)
        }
        CloseAndForget(client);
    }

    // :424-431
    private static string StatusHead(int status, IReadOnlyList<(string Name, string Value)> headers)
    {
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(ReasonPhrase(status)).Append("\r\n"); // :425
        foreach (var (name, value) in headers) head.Append(name).Append(": ").Append(value).Append("\r\n"); // :426-428
        head.Append("\r\n"); // :429
        return head.ToString();
    }

    // :433-450
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

    // :452-459
    private static byte[] JsonError(string message)
    {
        var escaped = message
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal); // :453-457
        return Encoding.UTF8.GetBytes($"{{\"error\":\"{escaped}\"}}"); // :458
    }

    // :137-150. No call sites at B2, kept for shape (PR-MCP-138).
    public void Stop()
    {
        var (listener, open) = state.WithLock((ref State s) =>
        {
            var currentListener = s.Listener; // :139
            var currentOpen = new List<TcpClient>(s.Connections); // :140
            s.Listener = null; // :141
            s.Connections.Clear(); // :142
            s.Running = false; // :143
            return (currentListener, currentOpen);
        });
        SwiftTry.Ignore(() => listener?.Stop()); // :146
        foreach (var connection in open) SwiftTry.Ignore(connection.Close); // :147-149
    }
}
