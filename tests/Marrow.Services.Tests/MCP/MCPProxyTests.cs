using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Xunit;

// Tests for src/Marrow.Services/MCP/MCPProxy.cs, the port of DroppyCode/Services/MCP/MCPProxy.swift (B2
// 1fbf81e), :1-460, records PR-MCP-121 to PR-MCP-138. MCPProxy.Shared is a process-wide singleton (a Swift
// `static let`): MCPProxySingletonFixture binds Storage.Root once, before anything in this class first touches
// MCPProxy.Port, and keeps that scratch folder alive for the whole class (R-TEST-9). Every test in this class
// shares the one running proxy and the one route table, so server ids are unique per test (GUID-based) and
// xUnit runs the methods of one class sequentially, never in parallel with each other.
namespace Marrow.Services.Tests.MCP;

[Collection("MCPProxy singleton")]
public sealed class MCPProxyTests : IClassFixture<MCPProxySingletonFixture>
{
    private readonly MCPProxySingletonFixture fixture;
    private MCPProxy Proxy => MCPProxy.Shared.Current;

    public MCPProxyTests(MCPProxySingletonFixture fixture) => this.fixture = fixture;

    private static string NewID() => "t-" + Guid.NewGuid().ToString("N");

    // ---- raw HTTP/1.1 client over the proxy's loopback socket ----

    private async Task<(int Status, Dictionary<string, string> Headers, byte[] Body)> SendRawAsync(
        string requestHead, byte[]? body = null, bool closeAfterHead = false)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, fixture.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(requestHead));
        if (body is not null) await stream.WriteAsync(body);
        if (closeAfterHead) client.Client.Shutdown(SocketShutdown.Send);
        using var ms = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            int n;
            var readTask = stream.ReadAsync(buffer).AsTask();
            if (await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(10))) != readTask) break;
            try { n = await readTask; } catch { break; }
            if (n <= 0) break;
            ms.Write(buffer, 0, n);
        }
        var all = ms.ToArray();
        if (all.Length == 0) return (0, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);
        var text = Encoding.Latin1.GetString(all);
        var divider = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headText = divider >= 0 ? text[..divider] : text;
        var bodyBytes = divider >= 0 ? all[(divider + 4)..] : [];
        var lines = headText.Split("\r\n");
        var status = int.Parse(lines[0].Split(' ', 3)[1], CultureInfo.InvariantCulture);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return (status, headers, bodyBytes);
    }

    private static byte[] Dechunk(byte[] chunked)
    {
        var result = new List<byte>();
        var i = 0;
        while (i < chunked.Length)
        {
            var lineEnd = Array.IndexOf(chunked, (byte)'\n', i);
            if (lineEnd < 0) break;
            var sizeLine = Encoding.ASCII.GetString(chunked, i, lineEnd - i).TrimEnd('\r');
            var size = Convert.ToInt32(sizeLine, 16);
            i = lineEnd + 1;
            if (size == 0) break;
            result.AddRange(chunked.AsSpan(i, size).ToArray());
            i += size + 2;
        }
        return result.ToArray();
    }

    // ---- PR-MCP-121 ----

    [Trait("Req", "PR-MCP-121")] // DroppyCode/Services/MCP/MCPProxy.swift:35-37
    [Fact]
    public void Url_is_http_127_0_0_1_port_mcp_serverID()
    {
        var url = MCPProxy.Url("my server");
        Assert.Matches(new Regex(@"^http://127\.0\.0\.1:\d+/mcp/my server$"), url);
        Assert.Contains($":{fixture.Port}/", url, StringComparison.Ordinal);
    }

    // ---- PR-MCP-122 ----

    [Trait("Req", "PR-MCP-122")] // DroppyCode/Services/MCP/MCPProxy.swift:12-25
    [Fact]
    public void LoadPort_reads_a_saved_port_from_the_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "54321");
            Assert.Equal((ushort)54321, MCPProxy.LoadPort(path));
        }
        finally { File.Delete(path); }
    }

    [Trait("Req", "PR-MCP-122")] // DroppyCode/Services/MCP/MCPProxy.swift:17-24
    [Fact]
    public void LoadPort_trims_whitespace_and_newlines_around_the_saved_port()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "  4242\n");
            Assert.Equal((ushort)4242, MCPProxy.LoadPort(path));
        }
        finally { File.Delete(path); }
    }

    [Trait("Req", "PR-MCP-122")] // DroppyCode/Services/MCP/MCPProxy.swift:18-25, garbage falls through to a fresh pick
    [Fact]
    public void LoadPort_falls_back_to_a_free_port_on_garbage_and_writes_it_back()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "proxy-port");
        try
        {
            File.WriteAllText(path, "not-a-port");
            var picked = MCPProxy.LoadPort(path);
            Assert.NotEqual((ushort)0, picked);
            Assert.Equal(picked.ToString(CultureInfo.InvariantCulture), File.ReadAllText(path));
        }
        finally { dir.Delete(true); }
    }

    [Trait("Req", "PR-MCP-122")] // DroppyCode/Services/MCP/MCPProxy.swift:19, saved != 0 required
    [Fact]
    public void LoadPort_falls_back_to_a_free_port_when_the_saved_value_is_zero()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "proxy-port");
        try
        {
            File.WriteAllText(path, "0");
            Assert.NotEqual((ushort)0, MCPProxy.LoadPort(path));
        }
        finally { dir.Delete(true); }
    }

    [Trait("Req", "PR-MCP-122")] // DroppyCode/Services/MCP/MCPProxy.swift:18, an unreadable file falls through too
    [Fact]
    public void LoadPort_falls_back_to_a_free_port_when_the_file_is_missing()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "does-not-exist", "proxy-port");
        try
        {
            Assert.NotEqual((ushort)0, MCPProxy.LoadPort(path));
        }
        finally { dir.Delete(true); }
    }

    // ---- PR-MCP-123 ----

    [Trait("Req", "PR-MCP-123")] // DroppyCode/Services/MCP/MCPProxy.swift:152-171
    [Fact]
    public void PickFreePort_returns_a_nonzero_port_that_can_be_bound()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "proxy-port");
        try
        {
            var picked = MCPProxy.LoadPort(path); // exercises pickFreePort via the fallback path (no saved file)
            Assert.NotEqual((ushort)0, picked);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, picked));
            Assert.Equal(picked, (ushort)((IPEndPoint)socket.LocalEndPoint!).Port);
        }
        finally { dir.Delete(true); }
    }

    // ---- PR-MCP-125, PR-MCP-138 (start/listen/accept) ----

    [Trait("Req", "PR-MCP-125")] // DroppyCode/Services/MCP/MCPProxy.swift:84-92, 99-135
    [Fact]
    public void Start_is_idempotent_and_the_listener_accepts_loopback_connections()
    {
        Proxy.Start();
        Proxy.Start(); // :86-90, already running -> returns immediately, no crash, no second bind
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, fixture.Port);
        Assert.True(client.Connected);
    }

    // ---- PR-MCP-126, PR-MCP-130 ----

    [Trait("Req", "PR-MCP-126")] // DroppyCode/Services/MCP/MCPProxy.swift:66-82
    [Trait("Req", "PR-MCP-130")] // DroppyCode/Services/MCP/MCPProxy.swift:276-293
    [Fact]
    public async Task Register_makes_a_route_reachable_and_unregister_removes_it()
    {
        var id = NewID();
        await using var upstream = new FakeUpstreamServer { Handler = _ => new FakeResponse(200, [("Content-Type", "text/plain")], "hi"u8.ToArray()) };
        Proxy.Register(id, upstream.BaseUrl + "/x", new Dictionary<string, string>(), usesOAuth: false);
        var (status1, _, _) = await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\nHost: x\r\n\r\n");
        Assert.Equal(200, status1);

        Proxy.Unregister(id);
        var (status2, _, body2) = await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\nHost: x\r\n\r\n");
        Assert.Equal(502, status2);
        Assert.Equal("{\"error\":\"unknown server\"}", Encoding.UTF8.GetString(body2));
    }

    [Trait("Req", "PR-MCP-126")] // DroppyCode/Services/MCP/MCPProxy.swift:76-82, replaceAll rebuilds the whole table
    [Fact]
    public async Task ReplaceAll_rebuilds_the_whole_route_table()
    {
        var kept = NewID();
        var dropped = NewID();
        var added = NewID();
        await using var upstream = new FakeUpstreamServer { Handler = _ => new FakeResponse(200, [], []) };
        Proxy.Register(kept, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);
        Proxy.Register(dropped, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);

        Proxy.ReplaceAll([
            (kept, upstream.BaseUrl, new Dictionary<string, string>(), false),
            (added, upstream.BaseUrl, new Dictionary<string, string>(), false),
        ]);

        Assert.Equal(200, (await SendRawAsync($"GET /mcp/{kept} HTTP/1.1\r\n\r\n")).Status);
        Assert.Equal(200, (await SendRawAsync($"GET /mcp/{added} HTTP/1.1\r\n\r\n")).Status);
        Assert.Equal(502, (await SendRawAsync($"GET /mcp/{dropped} HTTP/1.1\r\n\r\n")).Status);
    }

    // ---- PR-MCP-130 (the other three failure shapes) ----

    [Trait("Req", "PR-MCP-130")] // DroppyCode/Services/MCP/MCPProxy.swift:280-281, wrong prefix
    [Fact]
    public async Task Path_without_mcp_prefix_answers_502_never_404()
    {
        var (status, _, body) = await SendRawAsync("GET /other HTTP/1.1\r\n\r\n");
        Assert.Equal(502, status);
        Assert.Equal("{\"error\":\"unknown server\"}", Encoding.UTF8.GetString(body));
    }

    [Trait("Req", "PR-MCP-130")] // DroppyCode/Services/MCP/MCPProxy.swift:285-286, empty id and an id with a slash
    [Fact]
    public async Task Empty_server_id_and_a_server_id_containing_a_slash_answer_502()
    {
        Assert.Equal(502, (await SendRawAsync("GET /mcp/ HTTP/1.1\r\n\r\n")).Status);
        Assert.Equal(502, (await SendRawAsync("GET /mcp/a/b HTTP/1.1\r\n\r\n")).Status);
    }

    [Trait("Req", "PR-MCP-130")] // DroppyCode/Services/MCP/MCPProxy.swift:277, the query string is stripped before routing
    [Fact]
    public async Task Query_string_is_stripped_before_matching_the_server_id()
    {
        var id = NewID();
        await using var upstream = new FakeUpstreamServer { Handler = _ => new FakeResponse(200, [], []) };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);
        Assert.Equal(200, (await SendRawAsync($"GET /mcp/{id}?x=1 HTTP/1.1\r\n\r\n")).Status);
    }

    // ---- PR-MCP-127 ----

    [Trait("Req", "PR-MCP-127")] // DroppyCode/Services/MCP/MCPProxy.swift:183-210, over 65536 bytes with no divider
    [Fact]
    public async Task Head_over_65536_bytes_without_a_divider_answers_413()
    {
        var head = "GET /mcp/x HTTP/1.1\r\nX-Pad: " + new string('a', 70_000) + "\r\n";
        var (status, _, body) = await SendRawAsync(head);
        Assert.Equal(413, status);
        Assert.Equal("{\"error\":\"request head too large\"}", Encoding.UTF8.GetString(body));
    }

    [Trait("Req", "PR-MCP-127")] // DroppyCode/Services/MCP/MCPProxy.swift:203-207, stream ends before the divider
    [Fact]
    public async Task A_connection_closed_before_the_head_divider_gets_no_reply()
    {
        var (status, _, body) = await SendRawAsync("GET /mcp/x HTTP/1.1\r\n", closeAfterHead: true);
        Assert.Equal(0, status);
        Assert.Empty(body);
    }

    // ---- PR-MCP-128 ----

    [Trait("Req", "PR-MCP-128")] // DroppyCode/Services/MCP/MCPProxy.swift:212-228, not exactly three parts
    [Fact]
    public async Task A_request_line_that_is_not_method_path_version_answers_400()
    {
        var (status, _, body) = await SendRawAsync("BADLINE\r\n\r\n");
        Assert.Equal(400, status);
        Assert.Equal("{\"error\":\"bad request\"}", Encoding.UTF8.GetString(body));
    }

    [Trait("Req", "PR-MCP-128")] // DroppyCode/Services/MCP/MCPProxy.swift:221, header names are folded to lowercase
    [Fact]
    public async Task Header_names_are_folded_to_lowercase_before_matching_the_passthrough_list()
    {
        var id = NewID();
        FakeRequest? seen = null;
        await using var upstream = new FakeUpstreamServer { Handler = r => { seen = r; return new FakeResponse(200, [], []); } };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);
        await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\nCONTENT-TYPE: text/plain\r\n\r\n");
        Assert.NotNull(seen);
        Assert.Equal("text/plain", seen!.Headers["Content-Type"]);
    }

    // ---- PR-MCP-129 ----

    [Trait("Req", "PR-MCP-129")] // DroppyCode/Services/MCP/MCPProxy.swift:235-238
    [Fact]
    public async Task Chunked_transfer_encoding_is_refused_with_411()
    {
        var (status, _, body) = await SendRawAsync("POST /mcp/x HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n");
        Assert.Equal(411, status);
        Assert.Equal("{\"error\":\"chunked request bodies are not supported\"}", Encoding.UTF8.GetString(body));
    }

    [Trait("Req", "PR-MCP-129")] // DroppyCode/Services/MCP/MCPProxy.swift:241-243
    [Fact]
    public async Task A_declared_body_over_32MiB_is_refused_with_413()
    {
        var (status, _, body) = await SendRawAsync($"POST /mcp/x HTTP/1.1\r\nContent-Length: {33_554_433}\r\n\r\n", closeAfterHead: true);
        Assert.Equal(413, status);
        Assert.Equal("{\"error\":\"request body too large\"}", Encoding.UTF8.GetString(body));
    }

    [Trait("Req", "PR-MCP-129")] // DroppyCode/Services/MCP/MCPProxy.swift:268-269, upstream defect 12, reproduced
    [Fact]
    public async Task A_short_body_is_relayed_truncated_with_no_refusal()
    {
        var id = NewID();
        FakeRequest? seen = null;
        await using var upstream = new FakeUpstreamServer { Handler = r => { seen = r; return new FakeResponse(200, [], []); } };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);
        await SendRawAsync($"POST /mcp/{id} HTTP/1.1\r\nContent-Length: 100\r\n\r\nhello", closeAfterHead: true);
        await Task.Delay(300); // the proxy relays after its client connection ends
        Assert.NotNull(seen);
        Assert.Equal("hello", Encoding.UTF8.GetString(seen!.Body));
    }

    // ---- PR-MCP-131 ----

    [Trait("Req", "PR-MCP-131")] // DroppyCode/Services/MCP/MCPProxy.swift:309-330, exactly five headers plus route headers
    [Fact]
    public async Task Only_the_five_passthrough_headers_and_the_routes_own_headers_are_forwarded()
    {
        var id = NewID();
        FakeRequest? seen = null;
        await using var upstream = new FakeUpstreamServer { Handler = r => { seen = r; return new FakeResponse(200, [], []); } };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string> { ["X-Route"] = "route-value" }, usesOAuth: false);
        await SendRawAsync(
            $"GET /mcp/{id} HTTP/1.1\r\n" +
            "Content-Type: application/json\r\n" +
            "Accept: application/json\r\n" +
            "Mcp-Session-Id: sess-1\r\n" +
            "MCP-Protocol-Version: 2025-01-01\r\n" +
            "Last-Event-ID: 7\r\n" +
            "X-Not-Forwarded: nope\r\n" +
            "Cookie: a=b\r\n\r\n");
        Assert.NotNull(seen);
        Assert.Equal("application/json", seen!.Headers["Content-Type"]);
        Assert.Equal("application/json", seen.Headers["Accept"]);
        Assert.Equal("sess-1", seen.Headers["Mcp-Session-Id"]);
        Assert.Equal("2025-01-01", seen.Headers["MCP-Protocol-Version"]);
        Assert.Equal("7", seen.Headers["Last-Event-ID"]);
        Assert.Equal("route-value", seen.Headers["X-Route"]);
        Assert.False(seen.Headers.ContainsKey("X-Not-Forwarded"));
        Assert.False(seen.Headers.ContainsKey("Cookie"));
    }

    [Trait("Req", "PR-MCP-131")] // DroppyCode/Services/MCP/MCPProxy.swift:331-339, a fresh bearer token for an OAuth route
    [Fact]
    public async Task An_OAuth_route_adds_a_fresh_bearer_token()
    {
        var id = NewID();
        MCPKeychain.Set("{\"access_token\":\"tok-abc\"}", server: id, field: "oauth");
        FakeRequest? seen = null;
        await using var upstream = new FakeUpstreamServer { Handler = r => { seen = r; return new FakeResponse(200, [], []); } };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: true);
        await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\n\r\n");
        Assert.NotNull(seen);
        Assert.Equal("Bearer tok-abc", seen!.Headers["Authorization"]);
        MCPKeychain.Delete(id, "oauth");
    }

    [Trait("Req", "PR-MCP-131")] // DroppyCode/Services/MCP/MCPProxy.swift:310-312, an unparseable upstream URL
    [Fact]
    public async Task An_unparseable_upstream_URL_answers_502()
    {
        var id = NewID();
        Proxy.Register(id, "not a url", new Dictionary<string, string>(), usesOAuth: false); // not a valid absolute URL
        var (status, _, body) = await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\n\r\n");
        Assert.Equal(502, status);
        Assert.Equal("{\"error\":\"upstream request failed\"}", Encoding.UTF8.GetString(body));
    }

    // ---- PR-MCP-132 ----

    [Trait("Req", "PR-MCP-132")] // DroppyCode/Services/MCP/MCPProxy.swift:344-355, one replay with the same token, then give up
    [Fact]
    public async Task OAuth_401_is_retried_once_with_a_refetched_token_and_then_given_up_on()
    {
        var id = NewID();
        MCPKeychain.Set("{\"access_token\":\"same-token\"}", server: id, field: "oauth");
        var attempts = 0;
        await using var upstream = new FakeUpstreamServer
        {
            Handler = _ => { attempts++; return new FakeResponse(401, [], []); },
        };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: true);
        var (status, _, _) = await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\n\r\n");
        Assert.Equal(401, status); // the final (second) upstream response, relayed as-is
        Assert.Equal(2, attempts); // exactly one replay, no second retry
        MCPKeychain.Delete(id, "oauth");
    }

    // ---- PR-MCP-133 ----

    [Trait("Req", "PR-MCP-133")] // DroppyCode/Services/MCP/MCPProxy.swift:359-370, only these three, WWW-Authenticate dropped
    [Fact]
    public async Task Only_Content_Type_Mcp_Session_Id_and_Cache_Control_are_copied_back()
    {
        var id = NewID();
        await using var upstream = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200,
            [
                ("Content-Type", "application/json"),
                ("Mcp-Session-Id", "sess-9"),
                ("Cache-Control", "no-store"),
                ("WWW-Authenticate", "Bearer realm=\"x\""),
                ("X-Extra", "dropped"),
            ], "{}"u8.ToArray()),
        };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);
        var (_, headers, _) = await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\n\r\n");
        Assert.Equal("application/json", headers["Content-Type"]);
        Assert.Equal("sess-9", headers["Mcp-Session-Id"]);
        Assert.Equal("no-store", headers["Cache-Control"]);
        Assert.False(headers.ContainsKey("WWW-Authenticate"));
        Assert.False(headers.ContainsKey("X-Extra"));
    }

    // ---- PR-MCP-134 ----

    [Trait("Req", "PR-MCP-134")] // DroppyCode/Services/MCP/MCPProxy.swift:372-387, chunked, one chunk per byte
    [Fact]
    public async Task An_event_stream_response_is_relayed_as_chunked_with_content_preserved()
    {
        var id = NewID();
        await using var upstream = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200, [("Content-Type", "text/event-stream")], [],
                SseChunks: ["data: hi\n\n"]),
        };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);
        var (status, headers, body) = await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\n\r\n");
        Assert.Equal(200, status);
        Assert.Equal("chunked", headers["Transfer-Encoding"]);
        Assert.Equal("close", headers["Connection"]);
        Assert.Equal("data: hi\n\n", Encoding.UTF8.GetString(Dechunk(body)));
    }

    // ---- PR-MCP-135 ----

    [Trait("Req", "PR-MCP-135")] // DroppyCode/Services/MCP/MCPProxy.swift:388-400, buffered whole, sent with Content-Length
    [Fact]
    public async Task A_non_event_stream_response_is_sent_whole_with_a_content_length()
    {
        var id = NewID();
        await using var upstream = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200, [("Content-Type", "application/json")], "{\"ok\":true}"u8.ToArray()),
        };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);
        var (status, headers, body) = await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\n\r\n");
        Assert.Equal(200, status);
        Assert.Equal("11", headers["Content-Length"]);
        Assert.Equal("close", headers["Connection"]);
        Assert.Equal("{\"ok\":true}", Encoding.UTF8.GetString(body));
    }

    // ---- PR-MCP-136, PR-MCP-137 ----

    [Trait("Req", "PR-MCP-136")] // DroppyCode/Services/MCP/MCPProxy.swift:411-422, 452-459
    [Fact]
    public async Task A_proxy_error_response_escapes_the_message_into_a_json_error_body()
    {
        var head = "GET /mcp/" + new string('a', 200_000) + " HTTP/1.1\r\n\r\n"; // forces a 413 through the head-limit path
        var (status, headers, body) = await SendRawAsync(head);
        Assert.Equal(413, status);
        Assert.Equal("application/json", headers["Content-Type"]);
        Assert.Equal("close", headers["Connection"]);
        Assert.DoesNotContain('\n', Encoding.UTF8.GetString(body));
    }

    [Trait("Req", "PR-MCP-137")] // DroppyCode/Services/MCP/MCPProxy.swift:433-450
    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(411, "Length Required")]
    [InlineData(413, "Content Too Large")]
    [InlineData(502, "Bad Gateway")]
    public async Task Proxy_generated_statuses_carry_the_matching_reason_phrase(int status, string reason)
    {
        var head = status switch
        {
            400 => "NOPE\r\n\r\n",
            411 => "POST /mcp/x HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n",
            413 => $"POST /mcp/x HTTP/1.1\r\nContent-Length: {33_554_433}\r\n\r\n",
            _ => "GET /nope HTTP/1.1\r\n\r\n",
        };
        var (_, _, _) = (0, 0, 0);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, fixture.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(head));
        var buffer = new byte[4096];
        var n = await stream.ReadAsync(buffer);
        var statusLine = Encoding.Latin1.GetString(buffer, 0, n).Split("\r\n")[0];
        Assert.Equal($"HTTP/1.1 {status} {reason}", statusLine);
    }

    [Trait("Req", "PR-MCP-137")] // DroppyCode/Services/MCP/MCPProxy.swift:447-449, an upstream status not in the table
    [Fact]
    public async Task An_upstream_status_outside_the_table_defaults_to_OK_or_Error()
    {
        var id = NewID();
        await using var upstream = new FakeUpstreamServer { Handler = _ => new FakeResponse(201, [], []) };
        Proxy.Register(id, upstream.BaseUrl, new Dictionary<string, string>(), usesOAuth: false);
        var (status, _, _) = await SendRawAsync($"GET /mcp/{id} HTTP/1.1\r\n\r\n");
        Assert.Equal(201, status);
    }

    // ---- OD-0022 ----

    [Trait("Req", "PR-MCP-131")] // OD-0022: custom headers/Authorization forwarded only to that route's server
    [Fact]
    public async Task OAuth_bearer_token_is_dropped_on_a_cross_origin_redirect_but_kept_same_origin()
    {
        var crossOriginId = NewID();
        MCPKeychain.Set("{\"access_token\":\"secret-tok\"}", server: crossOriginId, field: "oauth");
        FakeRequest? seenByOther = null;
        await using var other = new FakeUpstreamServer { Handler = r => { seenByOther = r; return new FakeResponse(200, [], []); } };
        await using var origin = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(302, [("Location", other.BaseUrl + "/elsewhere")], []),
        };
        Proxy.Register(crossOriginId, origin.BaseUrl + "/start", new Dictionary<string, string>(), usesOAuth: true);
        await SendRawAsync($"GET /mcp/{crossOriginId} HTTP/1.1\r\n\r\n");
        Assert.NotNull(seenByOther);
        Assert.False(seenByOther!.Headers.ContainsKey("Authorization"));

        var sameOriginId = NewID();
        MCPKeychain.Set("{\"access_token\":\"secret-tok-2\"}", server: sameOriginId, field: "oauth");
        FakeRequest? seenAfterRedirect = null;
        var first = true;
        await using var sameOrigin = new FakeUpstreamServer
        {
            Handler = r =>
            {
                if (first) { first = false; return new FakeResponse(302, [("Location", "/next")], []); }
                seenAfterRedirect = r;
                return new FakeResponse(200, [], []);
            },
        };
        Proxy.Register(sameOriginId, sameOrigin.BaseUrl + "/start", new Dictionary<string, string>(), usesOAuth: true);
        await SendRawAsync($"GET /mcp/{sameOriginId} HTTP/1.1\r\n\r\n");
        Assert.NotNull(seenAfterRedirect);
        Assert.Equal("Bearer secret-tok-2", seenAfterRedirect!.Headers["Authorization"]);

        MCPKeychain.Delete(crossOriginId, "oauth");
        MCPKeychain.Delete(sameOriginId, "oauth");
    }
}
