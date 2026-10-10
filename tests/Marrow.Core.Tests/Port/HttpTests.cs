using System.Globalization;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Core.Tests.Fixtures.HttpReading;

namespace Marrow.Core.Tests.Port;

// Foundation unit F.HTTP (IMPLEMENTING.md 4.3): the HTTP client behind every URLSession site, R-NET-1. PORT-ONLY
// code, so these tests carry Rule and Decision traits and no Req ids (a foundation unit has no requirement records).
// Every request goes to LoopbackHttpServer on 127.0.0.1 (R-TEST-10); every timer runs on the manual clock
// (R-TEST-5, ED-068); LoopbackHttpServer.Guard only stops a broken implementation from hanging the run.
// This file covers URLSession.shared; HttpSecondTests covers URLSession(configuration:) and the network defaults a
// server sees (cookies, default request headers, the response cache); HttpStoreTests covers the rules of the cookie
// store and the response cache case by case; HttpProcessStateTests holds the URLSession.shared store tests (R-TEST-5).
//
// Expected values come from the pinned Swift (B2 1fbf81e) and from URLSession's documented behaviour (SEMANTICS),
// never from a C# implementation. Where the Swift behaviour is not documented and no oracle has settled it, the
// test carries an Oracle trait (R-TEST-6) with its item id, O-26 to O-33 and O-48 (O-25 is ED-100's), registered in
// IMPLEMENTING.md Appendix B with the parked choices these tests assert (network defaults: ED-118):
//   O-26 bytes.lines (AsyncLineSequence): CR, CRLF, NEL, LS and PS end a line; empty lines are not yielded
//   O-27 request headers URLSession adds: Accept */*, Accept-Language, Accept-Encoding, a form Content-Type
//   O-28 cookies: HTTPCookieStorage.shared for URLSession.shared, a per-session store in RAM for .ephemeral
//   O-29 request headers kept when a redirect is followed: all of them to the same origin, Authorization included;
//        all but Authorization to another origin
//   O-30 a repeated response header is one allHeaderFields entry, its values joined with ", "
//   O-31 which idle limit applies: a request's default 60 s or its session configuration's timeoutIntervalForRequest
//   O-32 the error a read of URLSession.AsyncBytes throws when its task is cancelled
//   O-33 URLCache.shared answering a cacheable GET for URLSession.shared
//   O-48 the number of redirects followed: 20; the 21st fails with URLError(.httpTooManyRedirects)
//
// The call sites these tests stand for:
//   URLSession.shared.data(for:)   MCPHub.swift:455-477, MCPOAuth.swift:250-268, :715-742, :744-797,
//                                  MCPProbe.swift:309-325, CommandCodeAPI.swift:168-174, ZaiAPI.swift:182-194,
//                                  PlanLimits.swift:243-249, PiPlanLimits.swift:45-68, MarkdownView.swift:946-952
//   URLSession.shared.data(from:)  LinkTitles.swift:47-49
//   URLSession.shared.bytes(for:)  MCPProxy.swift:344-405, OpenAICompatibleSession.swift:623-643, :740-745
//   AsyncBytes.lines               OpenAICompatibleSession.swift:636 (the error body), :742 (the SSE drain)
//   URLSession(configuration:)     UpdateChecker.swift:101-110 and :222-228 (HttpSecondTests)
//   URLError codes                 OpenAICompatibleSession.swift:88-97 (retry by code), MCPProxy.swift:311, :354
//
// The API these tests fix (namespace Marrow.Services.Port, file src/Marrow.Services/Port/Http.cs). Names follow
// R-NAME-1 to R-NAME-3; URLRequest is a Swift struct, so it is an immutable sealed record whose mutating
// setValue returns the changed copy (R-VAL-1):
//   sealed record URLRequest            URLRequest(Uri url, double timeoutInterval = 60); Uri Url { get; init; };
//                                       double TimeoutInterval { get; init; } (seconds); string HttpMethod { get; init; }
//                                       ("GET" unless set); byte[]? HttpBody { get; init; };
//                                       URLRequest SetValue(string? value, string forHTTPHeaderField);
//                                       string? Value(string forHTTPHeaderField);
//                                       nested enum CachePolicy { UseProtocolCachePolicy = 0, ReloadIgnoringLocalCacheData = 1 }
//   abstract class URLResponse; sealed class HTTPURLResponse : URLResponse
//                                       int StatusCode; string? Value(string forHTTPHeaderField);
//                                       IReadOnlyDictionary<string, string> AllHeaderFields; long ExpectedContentLength
//   sealed class URLCache                static URLCache Shared { get; set; }               URLCache.shared (AppDelegate.swift:13, B3 52bc792)
//                                       URLCache(long memoryCapacity, long diskCapacity)   URLCache(memoryCapacity:diskCapacity:)
//                                       long MemoryCapacity { get; } long DiskCapacity { get; } (recorded, not enforced: ED-118 item 4 parks "no size cut-off")
//                                       Before B3 the source's only value was nil (UpdateChecker.swift:104); AppDelegate.swift:13 now
//                                       constructs one and replaces the shared instance at launch (App.AppDelegate's launch call, not yet wired: STOP note there)
//   sealed class URLSessionConfiguration
//                                       static URLSessionConfiguration Ephemeral { get; } (a new configuration per read);
//                                       URLRequest.CachePolicy RequestCachePolicy; URLCache? UrlCache;
//                                       double TimeoutIntervalForRequest; double TimeoutIntervalForResource;
//                                       bool WaitsForConnectivity; IReadOnlyDictionary<string, string>? HttpAdditionalHeaders
//                                       (all { get; set; }, as the Swift class's var properties) (ED-119)
//   sealed class URLSession             static URLSession Shared; URLSession(URLSessionConfiguration configuration);
//                                       Task<(byte[] Data, URLResponse Response)> Data(URLRequest request)   data(for:)
//                                       Task<(byte[] Data, URLResponse Response)> Data(Uri from)             data(from:)
//                                       Task<(URLSession.AsyncBytes Bytes, URLResponse Response)> Bytes(URLRequest request)
//                                       TimeSpan HttpClientTimeout { get; } PORT-ONLY, accepted by ED-119: the Timeout
//                                       of the HttpClient underneath, which R-NET-1 requires to be infinite
//                                       nested sealed AsyncBytes : IAsyncEnumerable<byte>                  bytes(for:)
//                                         IAsyncEnumerable<string> Lines { get; }                          bytes.lines
//   sealed class URLError : Exception   URLError(URLError.Code code); URLError.Code URLErrorCode (Swift: code, R-NAME-3);
//                                       nested enum Code with the Foundation raw values below
// Timer contract the timeout tests rely on: the inactivity timer comes from SwiftClock.Provider as the caller sees it,
// is armed before the request is written, and is re-armed when the response head arrives and whenever body bytes
// arrive, before those bytes reach the caller. So "the server received the request" means the timer is armed, and
// "the caller read a byte" means the timer was re-armed for it. A configuration's resource timer is armed on the same
// clock when the task starts and is never re-armed.
public sealed class HttpTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    /// <summary>An app whose Info values a test controls (Text.swift:3-9 reads these three keys).</summary>
    private static AppInfo AppVersion(string version, string build) =>
        AppInfo.Read(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CFBundleDisplayName"] = "Droppy Code",
            ["CFBundleShortVersionString"] = version,
            ["CFBundleVersion"] = build,
        }, isDevelopment: false);

    // ---- The shared session and URLRequest's defaults ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public void URLSession_shared_is_one_session()
    {
        // R-NET-1: URLSession.shared (41 sites) is one shared client.
        Assert.Same(URLSession.Shared, URLSession.Shared);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_client_underneath_has_no_timeout_of_its_own_unlike_a_default_HttpClient()
    {
        // R-NET-1: "HttpClient.Timeout is infinite and the unit arms its own timer". A client left at the .NET default
        // would end every exchange after 100 real seconds, killing MCPProxy's 3,600 s streams (MCPProxy.swift:315)
        // and the 300 s chat streams (OpenAICompatibleSession.swift:601); the manual clock cannot see that limit, so
        // the configured value is asserted here. Negative control (R-TEST-4): a default HttpClient has 100 s.
        using var plain = new HttpClient();
        Assert.Equal(TimeSpan.FromSeconds(100), plain.Timeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, URLSession.Shared.HttpClientTimeout);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public void A_request_made_from_a_URL_alone_waits_60_seconds_and_uses_GET_with_no_body()
    {
        // SEMANTICS: URLRequest(url:) defaults timeoutInterval to 60.0 and httpMethod to "GET"; httpBody is nil.
        // The source relies on it at UpdateChecker.swift:223 (URLRequest(url:)), MCPProxy.swift:313 and
        // MarkdownView.swift:947, which set only what they change.
        var request = new URLRequest(new Uri("http://127.0.0.1:9/x"));
        Assert.Equal(60.0, request.TimeoutInterval);
        Assert.Equal("GET", request.HttpMethod);
        Assert.Null(request.HttpBody);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public void The_timeout_given_with_the_URL_is_kept()
    {
        // URLRequest(url:timeoutInterval:) at CommandCodeAPI.swift:169 (15), ZaiAPI.swift:182 (20), MCPHub.swift:456.
        Assert.Equal(15.0, new URLRequest(new Uri("http://127.0.0.1:9/x"), timeoutInterval: 15).TimeoutInterval);
        Assert.Equal(20.0, new URLRequest(new Uri("http://127.0.0.1:9/x"), 20).TimeoutInterval);
    }

    [Fact]
    [Trait("Rule", "R-VAL-1")]
    [Trait("Rule", "R-NET-1")]
    public void Setting_a_header_on_a_copy_leaves_the_original_request_unchanged()
    {
        // URLRequest is a struct: MCPProxy.swift:347-349 changes Authorization on its `var request` and sends it again.
        var original = new URLRequest(new Uri("http://127.0.0.1:9/x")).SetValue("Bearer one", forHTTPHeaderField: "Authorization");
        var changed = original.SetValue("Bearer two", forHTTPHeaderField: "Authorization");
        Assert.Equal("Bearer one", original.Value(forHTTPHeaderField: "Authorization"));
        Assert.Equal("Bearer two", changed.Value(forHTTPHeaderField: "Authorization"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public void Request_header_names_match_case_insensitively_and_a_nil_value_removes_the_field()
    {
        // SEMANTICS: URLRequest header field names are case-insensitive; setValue(nil, ...) removes the field.
        var request = new URLRequest(new Uri("http://127.0.0.1:9/x"))
            .SetValue("text/plain", forHTTPHeaderField: "content-type")
            .SetValue("application/json", forHTTPHeaderField: "Content-Type");
        Assert.Equal("application/json", request.Value(forHTTPHeaderField: "CONTENT-TYPE"));
        Assert.Null(request.SetValue(null, forHTTPHeaderField: "Content-Type").Value(forHTTPHeaderField: "content-type"));
    }

    // ---- What goes onto the wire ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_POST_sends_its_method_body_and_headers_with_a_later_header_replacing_an_earlier_one_of_any_case()
    {
        // MCPHub.swift:456-462: user headers first, then Content-Type, Accept and MCP-Protocol-Version; a user
        // "content-type" is replaced, not duplicated, because URLRequest header names are case-insensitive.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var body = Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
        var request = new URLRequest(server.Url("/mcp"), timeoutInterval: 30) { HttpMethod = "POST", HttpBody = body }
            .SetValue("text/plain", forHTTPHeaderField: "content-type")
            .SetValue("value-1", forHTTPHeaderField: "X-Custom")
            .SetValue("application/json", forHTTPHeaderField: "Content-Type")
            .SetValue("application/json, text/event-stream", forHTTPHeaderField: "Accept")
            .SetValue("2025-06-18", forHTTPHeaderField: "MCP-Protocol-Version");
        var pending = URLSession.Shared.Data(request);
        var exchange = await server.NextExchange();
        Assert.Equal("POST", exchange.Method);
        Assert.Equal("/mcp", exchange.Target);
        Assert.Equal(body, exchange.Body);
        Assert.Equal(["application/json"], exchange.HeaderValues("Content-Type"));
        Assert.Equal(["value-1"], exchange.HeaderValues("X-Custom"));
        Assert.Equal(["application/json, text/event-stream"], exchange.HeaderValues("Accept"));
        Assert.Equal(["2025-06-18"], exchange.HeaderValues("MCP-Protocol-Version"));
        await exchange.Respond(200, "OK", Ascii("{}"), ("Content-Type", "application/json"));
        var (data, _) = await pending.WaitAsync(Guard);
        Assert.Equal(Ascii("{}"), data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_request_body_goes_out_with_its_Content_Length_and_is_not_chunked()
    {
        // SEMANTICS: httpBody is a Data of known length, and URLSession sends it with Content-Length, never with
        // Transfer-Encoding: chunked (a port built on a StreamContent would chunk it, and some MCP servers refuse a
        // chunked request). The bodies: MCPHub.swift:458, ZaiAPI.swift:193, MCPOAuth.swift:754, :778, MCPProbe.swift:311.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var body = Ascii("{\"targetType\":\"PERSONAL\",\"resetType\":\"daily\",\"recordId\":7}");
        var request = new URLRequest(server.Url("/reset"), timeoutInterval: 20) { HttpMethod = "POST", HttpBody = body }
            .SetValue("application/json", forHTTPHeaderField: "Content-Type");
        var pending = URLSession.Shared.Data(request);
        var exchange = await server.NextExchange();
        Assert.Equal([body.Length.ToString(CultureInfo.InvariantCulture)], exchange.HeaderValues("Content-Length"));
        Assert.Empty(exchange.HeaderValues("Transfer-Encoding"));
        Assert.Equal(body, exchange.Body);
        await exchange.Respond(200, "OK", Ascii("{}"));
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_streamed_request_body_also_goes_out_with_its_Content_Length_and_is_not_chunked()
    {
        // The same for bytes(for:): OpenAICompatibleSession.swift:601-607 (the chat request) and MCPProxy.swift:340-344
        // (the client's body, forwarded) send httpBody through the streaming call.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var body = Ascii("{\"model\":\"m\",\"stream\":true,\"messages\":[]}");
        var request = new URLRequest(server.Url("/v1/chat/completions"), timeoutInterval: 300) { HttpMethod = "POST", HttpBody = body }
            .SetValue("application/json", forHTTPHeaderField: "Content-Type");
        var pending = URLSession.Shared.Bytes(request);
        var exchange = await server.NextExchange();
        Assert.Equal([body.Length.ToString(CultureInfo.InvariantCulture)], exchange.HeaderValues("Content-Length"));
        Assert.Empty(exchange.HeaderValues("Transfer-Encoding"));
        Assert.Equal(body, exchange.Body);
        await exchange.Respond(200, "OK", Ascii("data: [DONE]\n"));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await using var reader = bytes.GetAsyncEnumerator();
        Assert.Equal(Ascii("data: [DONE]\n"), await ReadToEnd(reader));
    }

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    public async Task Any_method_the_proxy_forwards_goes_out_as_given(string method)
    {
        // MCPProxy.swift:314: `request.httpMethod = method`, the client's own method, whatever it is (an MCP client
        // ends its session with DELETE and the session id); :336-338 add no body when the client sent none.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var request = new URLRequest(server.Url("/mcp")) { HttpMethod = method, TimeoutInterval = 3_600 }
            .SetValue("abc123", forHTTPHeaderField: "Mcp-Session-Id");
        var pending = URLSession.Shared.Bytes(request);
        var exchange = await server.NextExchange();
        Assert.Equal(method, exchange.Method);
        Assert.Equal("/mcp", exchange.Target);
        Assert.Equal(["abc123"], exchange.HeaderValues("Mcp-Session-Id"));
        Assert.Empty(exchange.Body);
        await exchange.Respond(200, "OK", []);
        var (_, response) = await pending.WaitAsync(Guard);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task Data_from_a_URL_sends_one_GET_for_that_path_and_query()
    {
        // LinkTitles.swift:47: URLSession.shared.data(from: url) is a plain GET of the URL.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(server.Url("/oembed?url=https%3A%2F%2Fexample.invalid%2Fv&format=json"));
        var exchange = await server.NextExchange();
        Assert.Equal("GET", exchange.Method);
        Assert.Equal("/oembed?url=https%3A%2F%2Fexample.invalid%2Fv&format=json", exchange.Target);
        Assert.Empty(exchange.Body);
        await exchange.Respond(200, "OK", Ascii("{\"title\":\"t\"}"));
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal(Ascii("{\"title\":\"t\"}"), data);
    }

    // ---- The User-Agent ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-088")]
    [Trait("Decision", "ED-065")]
    public async Task The_default_User_Agent_is_one_header_line_Marrow_slash_the_app_version_and_nothing_else()
    {
        // R-NET-1: URLSession.shared sends a default User-Agent; the source's is "Droppy%20Code/<build> CFNetwork/<v>
        // Darwin/<v>" (the bundle name, CFBundleVersion, and two more product tokens).
        // DEVIATION OD-0012|ED-088: OD-0012 F1 (decision_batch_20260922b.md section F) names the network User-Agent
        // "Marrow/<version>", and ED-088 puts identities sent over the network under F1. So the build number becomes
        // the version (AppInfo.version, Text.swift:7, as in the source's own "Droppy Code/\(AppInfo.version)",
        // UpdateChecker.swift:108), and the CFNetwork and Darwin tokens are dropped (ED-118). The raw header line is
        // compared byte for byte, so an added product token (a .NET default, a runtime version) fails the test. The
        // version is one the test controls through the AppInfo.Running seam (ED-065, ED-119): 7.6.5, build 42, so a
        // port that writes a fixed version or the build number fails too.
        using var runtime = new TestRuntime();
        using var app = AppInfo.Running.Override(AppVersion("7.6.5", "42"));
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/ua")));
        var exchange = await server.NextExchange();
        var lines = RawHeaderLines(exchange.RawHead)
            .Where(line => line.StartsWith("user-agent:", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(["User-Agent: Nth/7.6.5"], lines);
        await exchange.Respond(204, "No Content", []);
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_User_Agent_set_on_the_request_replaces_the_default()
    {
        // AppUpdater.swift:481 sets User-Agent on the request; the server sees that value alone.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var request = new URLRequest(server.Url("/ua")).SetValue("Nth/9.8.7", forHTTPHeaderField: "User-Agent");
        var pending = URLSession.Shared.Data(request);
        var exchange = await server.NextExchange();
        Assert.Equal(["Nth/9.8.7"], exchange.HeaderValues("User-Agent"));
        await exchange.Respond(204, "No Content", []);
        await pending.WaitAsync(Guard);
    }

    // ---- Decompression ----

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task A_gzip_deflate_or_br_body_is_handed_over_decoded_and_the_request_offers_all_three(string coding)
    {
        // R-NET-1: gzip, deflate and br are decoded, as URLSession decodes them before data(for:) returns. "deflate"
        // is the zlib format (RFC 9110 8.4.1.2). The request must offer the three codings, or a server that honours
        // Accept-Encoding sends something else than it sends URLSession.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var plain = Ascii(string.Concat(Enumerable.Repeat("data: {\"choices\":[]}\n", 40)));
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/z")));
        var exchange = await server.NextExchange();
        var offered = exchange.HeaderValues("Accept-Encoding")
            .SelectMany(v => v.Split(',')).Select(t => t.Split(';')[0].Trim()).ToList();
        Assert.Contains("gzip", offered);
        Assert.Contains("deflate", offered);
        Assert.Contains("br", offered);
        await exchange.Respond(200, "OK", Compress(plain, coding), ("Content-Encoding", coding));
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal(plain, data);
    }

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task A_gzip_deflate_or_br_stream_is_handed_over_decoded_through_the_byte_sequence(string coding)
    {
        // R-NET-1 applies to bytes(for:) as well: MCPProxy.swift:344 and :377-396 forward the decoded bytes (the
        // client never sees Content-Encoding, :360-369 copy only Content-Type, Mcp-Session-Id and Cache-Control), and
        // OpenAICompatibleSession.swift:626 and :742 read decoded SSE text. The compressed body is sent in two chunks
        // while the stream is open, so a decoder that needs the whole body first would not hand over the first part.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var plain = Ascii(string.Concat(Enumerable.Repeat("data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}]}\n\n", 30)));
        var encoded = Compress(plain, coding);
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"), ("Content-Encoding", coding));
        await exchange.WriteChunk(encoded[..(encoded.Length / 2)]);
        var (bytes, _) = await pending.WaitAsync(Guard);
        await using var reader = bytes.GetAsyncEnumerator();
        await exchange.WriteChunk(encoded[(encoded.Length / 2)..]);
        await exchange.EndChunks();
        Assert.Equal(plain, await ReadToEnd(reader));
    }

    // ---- Redirects ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-48")]
    public async Task Twenty_redirects_are_followed_and_the_final_response_is_returned()
    {
        // UNVERIFIED O-48: corelibs Foundation mirrors Darwin's 20-redirect limit (ED-152).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/r0")));
        for (var n = 0; n < 20; n++)
        {
            var exchange = await server.NextExchange();
            Assert.Equal("/r" + n, exchange.Target);
            await exchange.Respond(302, "Found", [], ("Location", "/r" + (n + 1)));
        }
        var final = await server.NextExchange();
        Assert.Equal("/r20", final.Target);
        await final.Respond(200, "OK", Ascii("done"));
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal(Ascii("done"), data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-48")]
    public async Task The_21st_redirect_fails_with_httpTooManyRedirects_and_is_not_followed()
    {
        // UNVERIFIED O-48: the 21st redirect response fails before another request is sent (ED-152).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/r0")));
        for (var n = 0; n <= 20; n++)
        {
            var exchange = await server.NextExchange();
            Assert.Equal("/r" + n, exchange.Target);
            await exchange.Respond(302, "Found", [], ("Location", "/r" + (n + 1)));
        }
        var error = await Assert.ThrowsAsync<URLError>(() => pending.WaitAsync(Guard));
        Assert.Equal(URLError.Code.HTTPTooManyRedirects, error.URLErrorCode);
    }

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task A_GET_follows_a_redirect_and_returns_the_final_response(int status)
    {
        // R-NET-1: redirects are followed; the caller sees only the final response (LinkTitles.swift:47-49 and
        // PlanLimits.swift:247-249 read the status and body of wherever the chain ends).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/start")));
        var first = await server.NextExchange();
        await first.Respond(status, "Redirect", [], ("Location", "/final"));
        var second = await server.NextExchange();
        Assert.Equal("GET", second.Method);
        Assert.Equal("/final", second.Target);
        await second.Respond(200, "OK", Ascii("done"));
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal(Ascii("done"), data);
    }

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData(307)]
    [InlineData(308)]
    public async Task A_307_or_308_redirect_repeats_the_POST_with_its_body(int status)
    {
        // SEMANTICS: 307 and 308 keep the method and body (RFC 9110 15.4.8, 15.4.9); URLSession follows them so.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var body = Ascii("{\"grant_type\":\"refresh_token\"}");
        var request = new URLRequest(server.Url("/token"), timeoutInterval: 30) { HttpMethod = "POST", HttpBody = body }
            .SetValue("application/json", forHTTPHeaderField: "Content-Type");
        var pending = URLSession.Shared.Data(request);
        var first = await server.NextExchange();
        await first.Respond(status, "Redirect", [], ("Location", "/token2"));
        var second = await server.NextExchange();
        Assert.Equal("POST", second.Method);
        Assert.Equal("/token2", second.Target);
        Assert.Equal(body, second.Body);
        await second.Respond(200, "OK", Ascii("{}"));
        var (data, _) = await pending.WaitAsync(Guard);
        Assert.Equal(Ascii("{}"), data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_303_redirect_after_a_POST_is_followed_with_a_GET_and_no_body()
    {
        // SEMANTICS: 303 See Other is followed with GET (RFC 9110 15.4.4).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var request = new URLRequest(server.Url("/form"), timeoutInterval: 30) { HttpMethod = "POST", HttpBody = Ascii("a=1") }
            .SetValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type");
        var pending = URLSession.Shared.Data(request);
        var first = await server.NextExchange();
        await first.Respond(303, "See Other", [], ("Location", "/result"));
        var second = await server.NextExchange();
        Assert.Equal("GET", second.Method);
        Assert.Equal("/result", second.Target);
        Assert.Empty(second.Body);
        await second.Respond(200, "OK", Ascii("ok"));
        var (data, _) = await pending.WaitAsync(Guard);
        Assert.Equal(Ascii("ok"), data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-29")]
    public async Task A_same_origin_redirect_keeps_Authorization_Accept_and_custom_headers()
    {
        // PlanLimits.swift:243-246 sends Authorization, anthropic-beta and Accept; CommandCodeAPI.swift:169-171 and
        // MCPProxy.swift:326-339 send Authorization and the route's own headers. URLSession builds the redirected
        // request from the original one, so its header fields go along to the same origin.
        // UNVERIFIED O-29: whether URLSession keeps Authorization on a same-origin redirect is not documented; the
        // parked choice is that every field is kept. The negative control below shows the .NET redirect handler
        // drops Authorization, the plausible wrong port.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var request = new URLRequest(server.Url("/api/oauth/usage"), timeoutInterval: 15)
            .SetValue("Bearer token-1", forHTTPHeaderField: "Authorization")
            .SetValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
            .SetValue("application/json", forHTTPHeaderField: "Accept");
        var pending = URLSession.Shared.Data(request);
        var first = await server.NextExchange();
        await first.Respond(302, "Found", [], ("Location", "/api/oauth/usage2"));
        var second = await server.NextExchange();
        Assert.Equal("/api/oauth/usage2", second.Target);
        Assert.Equal(["Bearer token-1"], second.HeaderValues("Authorization"));
        Assert.Equal(["oauth-2025-04-20"], second.HeaderValues("anthropic-beta"));
        Assert.Equal(["application/json"], second.HeaderValues("Accept"));
        await second.Respond(200, "OK", Ascii("{}"));
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-29")]
    public async Task A_redirect_to_another_origin_keeps_anthropic_beta_and_Accept_and_drops_Authorization()
    {
        // The same request as PlanLimits.swift:243-246, redirected with an absolute Location to a second loopback
        // server on 127.0.0.1: another port is another origin (scheme, host and port, RFC 6454).
        // UNVERIFIED O-29: the parked choice drops Authorization on a redirect to another origin and keeps every
        // other field. Appendix B records that half as a safety default, not a reading of CFNetwork.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        await using var other = new LoopbackHttpServer();
        var request = new URLRequest(server.Url("/api/oauth/usage"), timeoutInterval: 15)
            .SetValue("Bearer token-1", forHTTPHeaderField: "Authorization")
            .SetValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
            .SetValue("application/json", forHTTPHeaderField: "Accept");
        var pending = URLSession.Shared.Data(request);
        var first = await server.NextExchange();
        Assert.Equal(["Bearer token-1"], first.HeaderValues("Authorization"));
        await first.Respond(302, "Found", [], ("Location", other.Url("/api/oauth/usage2").AbsoluteUri));
        var second = await other.NextExchange();
        Assert.Equal("GET", second.Method);
        Assert.Equal("/api/oauth/usage2", second.Target);
        Assert.Empty(second.HeaderValues("Authorization"));
        Assert.Equal(["oauth-2025-04-20"], second.HeaderValues("anthropic-beta"));
        Assert.Equal(["application/json"], second.HeaderValues("Accept"));
        await second.Respond(200, "OK", Ascii("{}"));
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal(Ascii("{}"), data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-209")]
    public async Task A_same_origin_redirect_keeps_an_origin_bound_header()
    {
        // ED-209 (OD-0022, extends ED-152): URLRequest.MarkOriginBound lets a caller (MCPWire.Post) name one of
        // its own headers as a server's secret, so Redirected treats it like Authorization above: kept to the
        // same origin, dropped the moment the chain leaves it (next test).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var request = new URLRequest(server.Url("/mcp"), timeoutInterval: 15)
            .SetValue("secret-key", forHTTPHeaderField: "X-Api-Key").MarkOriginBound("X-Api-Key");
        var pending = URLSession.Shared.Data(request);
        var first = await server.NextExchange();
        Assert.Equal(["secret-key"], first.HeaderValues("X-Api-Key"));
        await first.Respond(302, "Found", [], ("Location", "/mcp2"));
        var second = await server.NextExchange();
        Assert.Equal("/mcp2", second.Target);
        Assert.Equal(["secret-key"], second.HeaderValues("X-Api-Key"));
        await second.Respond(200, "OK", Ascii("{}"));
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-209")]
    public async Task A_redirect_to_another_origin_drops_an_origin_bound_header_but_keeps_an_unmarked_one()
    {
        // ED-209 (OD-0022): a redirect that changes scheme, host or port drops a marked header exactly as it
        // drops Authorization, while an unmarked header (here MCP-Protocol-Version, a protocol constant, never
        // marked by MCPWire.Post) is still carried across, same as :525's anthropic-beta and Accept.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        await using var other = new LoopbackHttpServer();
        var request = new URLRequest(server.Url("/mcp"), timeoutInterval: 15)
            .SetValue("secret-key", forHTTPHeaderField: "X-Api-Key").MarkOriginBound("X-Api-Key")
            .SetValue("2025-06-18", forHTTPHeaderField: "MCP-Protocol-Version");
        var pending = URLSession.Shared.Data(request);
        var first = await server.NextExchange();
        Assert.Equal(["secret-key"], first.HeaderValues("X-Api-Key"));
        await first.Respond(302, "Found", [], ("Location", other.Url("/mcp2").AbsoluteUri));
        var second = await other.NextExchange();
        Assert.Equal("GET", second.Method);
        Assert.Equal("/mcp2", second.Target);
        Assert.Empty(second.HeaderValues("X-Api-Key"));
        Assert.Equal(["2025-06-18"], second.HeaderValues("MCP-Protocol-Version"));
        await second.Respond(200, "OK", Ascii("{}"));
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal(Ascii("{}"), data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Negative_control_the_default_dotnet_redirect_handler_drops_Authorization_on_a_same_origin_redirect()
    {
        // R-TEST-4: the obvious .NET API behaves differently, so a port that only turns on AllowAutoRedirect loses the
        // header A_same_origin_redirect_keeps_Authorization_Accept_and_custom_headers keeps. Observed on this machine
        // (.NET 10.0.401, scratch run for this unit): the second request carried anthropic-beta and Accept but not
        // Authorization.
        await using var server = new LoopbackHttpServer();
        using var plain = new HttpClient();
        using var message = new HttpRequestMessage(HttpMethod.Get, server.Url("/start"));
        message.Headers.TryAddWithoutValidation("Authorization", "Bearer token-1");
        message.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        var pending = plain.SendAsync(message);
        var first = await server.NextExchange();
        Assert.Equal(["Bearer token-1"], first.HeaderValues("Authorization"));
        await first.Respond(302, "Found", [], ("Location", "/final"));
        var second = await server.NextExchange();
        Assert.Empty(second.HeaderValues("Authorization"));
        Assert.Equal(["oauth-2025-04-20"], second.HeaderValues("anthropic-beta"));
        await second.Respond(200, "OK", []);
        using var response = await pending.WaitAsync(Guard);
    }

    // ---- Status handling and the response ----

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A_non_2xx_status_is_a_response_with_its_body_not_an_error(int status)
    {
        // R-NET-1: never EnsureSuccessStatusCode. MCPHub.swift:469-471 and MCPOAuth.swift:737-739 read the status
        // themselves; MCPProbe.swift:321-323 reads the error body.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/status")));
        var exchange = await server.NextExchange();
        await exchange.Respond(status, "Status", Ascii("{\"error\":\"nope\"}"), ("Content-Type", "application/json"));
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(status, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Equal(Ascii("{\"error\":\"nope\"}"), data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_204_answer_gives_an_empty_body()
    {
        // MCPHub.swift:476: `guard !data.isEmpty else { return (session, nil) }` expects empty Data, not an error.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/note")));
        var exchange = await server.NextExchange();
        await exchange.Respond(204, "No Content", []);
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(204, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        Assert.Empty(data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task Response_header_values_are_found_whatever_the_case_and_a_missing_one_is_nil()
    {
        // SEMANTICS: HTTPURLResponse.value(forHTTPHeaderField:) matches case-insensitively. MCPHub.swift:468 and
        // :472 read "Mcp-Session-Id" and "Content-Type"; MCPProxy.swift:360-369 reads "Cache-Control".
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/mcp")));
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", Ascii("data: {}\n\n"),
            ("content-type", "text/event-stream"), ("mcp-session-id", "abc123"));
        var (_, response) = await pending.WaitAsync(Guard);
        var http = Assert.IsType<HTTPURLResponse>(response);
        Assert.Equal("text/event-stream", http.Value(forHTTPHeaderField: "Content-Type"));
        Assert.Equal("abc123", http.Value(forHTTPHeaderField: "Mcp-Session-Id"));
        Assert.Null(http.Value(forHTTPHeaderField: "Cache-Control"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_401_challenge_header_is_readable_from_all_header_fields()
    {
        // MCPOAuth.swift:255-268: a 401 is a response, and its WWW-Authenticate value is found by walking
        // allHeaderFields with a case-insensitive name comparison.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var request = new URLRequest(server.Url("/mcp"), timeoutInterval: 30)
            .SetValue("application/json", forHTTPHeaderField: "Accept");
        var pending = URLSession.Shared.Data(request);
        var exchange = await server.NextExchange();
        const string challenge = "Bearer resource_metadata=\"http://127.0.0.1/.well-known/oauth-protected-resource\"";
        await exchange.Respond(401, "Unauthorized", [], ("www-authenticate", challenge));
        var (_, response) = await pending.WaitAsync(Guard);
        var http = Assert.IsType<HTTPURLResponse>(response);
        Assert.Equal(401, http.StatusCode);
        var found = http.AllHeaderFields
            .Where(pair => string.Equals(pair.Key, "WWW-Authenticate", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value).ToList();
        Assert.Equal([challenge], found);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-30")]
    public async Task Two_challenge_header_lines_become_one_field_with_both_values_joined_by_a_comma_and_space()
    {
        // MCPOAuth.swift:259-268 collects every WWW-Authenticate entry of allHeaderFields (a String, or a [String]) and
        // then looks for resource_metadata and scopes in each challenge (:272-280). A port that kept only the first or the last
        // line would lose a challenge.
        // UNVERIFIED O-30: HTTPURLResponse is believed to fold a repeated field into one String entry, its
        // values joined with ", " in arrival order (the list form RFC 9110 5.3 allows), and value(forHTTPHeaderField:)
        // to return that same String; this is the parked choice.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/mcp"), timeoutInterval: 30));
        var exchange = await server.NextExchange();
        const string first = "Basic realm=\"mcp\"";
        const string second = "Bearer resource_metadata=\"http://127.0.0.1/.well-known/oauth-protected-resource\"";
        await exchange.Respond(401, "Unauthorized", [], ("WWW-Authenticate", first), ("WWW-Authenticate", second));
        var (_, response) = await pending.WaitAsync(Guard);
        var http = Assert.IsType<HTTPURLResponse>(response);
        var found = http.AllHeaderFields
            .Where(pair => string.Equals(pair.Key, "WWW-Authenticate", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value).ToList();
        Assert.Equal([first + ", " + second], found);
        Assert.Equal(first + ", " + second, http.Value(forHTTPHeaderField: "www-authenticate"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_body_of_unknown_length_reports_minus_one()
    {
        // SEMANTICS: expectedContentLength is NSURLResponseUnknownLength (-1) when the length is not known, so
        // UpdateChecker.swift:227 (`> 0`) treats it as no size.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/stream")));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK");
        await exchange.WriteChunk(Ascii("abc"));
        await exchange.EndChunks();
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Equal(Ascii("abc"), data);
        Assert.Equal(-1L, Assert.IsType<HTTPURLResponse>(response).ExpectedContentLength);
    }

    // ---- Streaming bytes ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task Bytes_returns_at_the_response_head_and_hands_over_body_bytes_as_they_arrive()
    {
        // MCPProxy.swift:344 and :377-383 forward each byte as it arrives (one chunk per byte, ED-043);
        // OpenAICompatibleSession.swift:626 and :740-745 drain an SSE stream while it is still open.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Ascii("data: one\n"));
        var (bytes, response) = await pending.WaitAsync(Guard);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        await using var reader = bytes.GetAsyncEnumerator();
        // The server has not finished: these bytes can only have come from a stream read while it is open.
        Assert.Equal(Ascii("data: one\n"), await ReadExactly(reader, 10));
        await exchange.WriteChunk(Ascii("data: two\n"));
        await exchange.EndChunks();
        Assert.Equal(Ascii("data: two\n"), await ReadToEnd(reader));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task Bytes_of_a_non_200_answer_are_readable_as_its_error_body()
    {
        // OpenAICompatibleSession.swift:633-643: a status other than 200 is a response whose body is then read.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/v1/chat/completions"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.Respond(401, "Unauthorized", Ascii("{\"error\":{\"message\":\"bad key\"}}"));
        var (bytes, response) = await pending.WaitAsync(Guard);
        Assert.Equal(401, Assert.IsType<HTTPURLResponse>(response).StatusCode);
        await using var reader = bytes.GetAsyncEnumerator();
        Assert.Equal(Ascii("{\"error\":{\"message\":\"bad key\"}}"), await ReadToEnd(reader));
    }

    // ---- The line sequence (bytes.lines) ----

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public async Task A_character_split_across_two_chunks_is_decoded_whole_in_its_line(int split)
    {
        // OpenAICompatibleSession.swift:742: `for try await line in bytes.lines`. AsyncBytes is a sequence of UInt8, so
        // chunk boundaries are invisible to `lines`: a line is decoded from all of its bytes as UTF-8. The body is
        // "data: it" + U+2019 (E2 80 99 at offsets 8-10) + "s " + U+1F600 (F0 9F 98 80 at offsets 13-16) + "!\nnext\n";
        // the first chunk holds `split` bytes, so 9, 10 and 14 to 16 cut a character in two and 11 falls between.
        byte[] body = [.. Ascii("data: it"), 0xE2, 0x80, 0x99, .. Ascii("s "), 0xF0, 0x9F, 0x98, 0x80, .. Ascii("!\nnext\n")];
        byte[] firstLine = body[..(Array.IndexOf(body, (byte)'\n'))];
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(body[..split]);
        var (bytes, _) = await pending.WaitAsync(Guard);
        await exchange.WriteChunk(body[split..]);
        await exchange.EndChunks();
        var lines = await ReadLines(bytes);
        Assert.Equal(2, lines.Count);
        Assert.Equal(firstLine, Utf8(lines[0]));
        Assert.Equal("next", lines[1]);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task Lines_end_at_a_line_feed_and_a_last_line_without_one_is_still_yielded()
    {
        // OpenAICompatibleSession.swift:636-639 appends every line of an error body and a "\n" after each; a body
        // whose last line has no terminator still gives that line. A terminator at the very end adds no line.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/v1/chat/completions"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(400, "Bad Request", ("Content-Type", "text/plain"));
        await exchange.WriteChunk(Ascii("first\nsec"));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await exchange.WriteChunk(Ascii("ond\nthird"));
        await exchange.EndChunks();
        Assert.Equal(["first", "second", "third"], await ReadLines(bytes));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-26")]
    public async Task A_carriage_return_line_feed_pair_and_a_lone_carriage_return_each_end_one_line()
    {
        // An SSE server may end its lines with CRLF or CR (the text/event-stream grammar allows all three); the drain
        // at OpenAICompatibleSession.swift:742-744 trims each line and needs "data:" at its start.
        // UNVERIFIED O-26: AsyncLineSequence is believed to end a line at LF, CR or CRLF, a CRLF pair
        // ending one line; this is the parked choice. The CRLF pair is split across two chunks here.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Ascii("data: a\r"));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await exchange.WriteChunk(Ascii("\ndata: b\rdata: c\n"));
        await exchange.EndChunks();
        Assert.Equal(["data: a", "data: b", "data: c"], await ReadLines(bytes));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-26")]
    public async Task Empty_lines_between_events_are_not_yielded()
    {
        // SSE separates events with an empty line. OpenAICompatibleSession.swift:636-637 builds the error body from the
        // lines it is given (`body += line + "\n"`), so whether empty lines arrive changes that text; :742-744 skips
        // anything without "data:" either way.
        // UNVERIFIED O-26: AsyncLineSequence is widely reported to yield no empty line, whatever the
        // terminators; this is the parked choice. The negative control below shows the .NET line reader yields them.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Ascii("data: 1\n\ndata: 2\r\n\r\n\n"));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await exchange.WriteChunk(Ascii("\rdata: 3\n\n"));
        await exchange.EndChunks();
        Assert.Equal(["data: 1", "data: 2", "data: 3"], await ReadLines(bytes));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Negative_control_a_dotnet_line_reader_yields_the_empty_lines()
    {
        // R-TEST-4: StringReader.ReadLine (and StreamReader.ReadLine, R-PROC-2) yields an empty string for every empty
        // line, so a port that wraps the stream in a reader would hand the error-body loop lines the parked
        // AsyncLineSequence choice drops. Observed on this machine (.NET 10.0.401): five lines, three of them empty.
        using var reader = new StringReader("data: 1\n\ndata: 2\r\n\r\n\n");
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        Assert.Equal(["data: 1", "", "data: 2", "", ""], lines);
    }

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-26")]
    [InlineData(new byte[] { 0xC2, 0x85 })]
    [InlineData(new byte[] { 0xE2, 0x80, 0xA8 })]
    [InlineData(new byte[] { 0xE2, 0x80, 0xA9 })]
    public async Task A_next_line_line_separator_or_paragraph_separator_also_ends_a_line(byte[] separator)
    {
        // A raw U+2028 inside an SSE JSON string is valid JSON (RFC 8259 escapes only controls), so a split there cuts
        // a "data:" line in two and OpenAICompatibleSession.swift:747 fails to parse either half.
        // UNVERIFIED O-26: AsyncLineSequence is believed to end a line at NEL (U+0085), LS (U+2028) and
        // PS (U+2029) as well as at CR and LF; this is the parked choice.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk([.. Ascii("data: a"), .. separator, .. Ascii("b\n")]);
        var (bytes, _) = await pending.WaitAsync(Guard);
        await exchange.EndChunks();
        Assert.Equal(["data: a", "b"], await ReadLines(bytes));
    }

    // ---- The inactivity timeout (timeoutInterval) ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    public async Task A_default_request_to_a_silent_server_fails_with_timedOut_after_60_seconds()
    {
        // R-NET-1: timeoutIntervalForRequest (60 s) is an inactivity limit; URLSession fails the task with
        // URLError(.timedOut) (NSURLErrorTimedOut, -1001). The retry at OpenAICompatibleSession.swift:94 keys on it.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/silent")));
        await server.NextExchange();
        runtime.Advance(TimeSpan.FromSeconds(60));
        var error = await Assert.ThrowsAsync<URLError>(() => pending.WaitAsync(Guard));
        Assert.Equal(URLError.Code.TimedOut, error.URLErrorCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    public async Task A_default_request_answered_just_before_60_idle_seconds_succeeds()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/slow")));
        var exchange = await server.NextExchange();
        runtime.Advance(TimeSpan.FromMilliseconds(59_999));
        await exchange.Respond(200, "OK", Ascii("late but in time"));
        var (data, _) = await pending.WaitAsync(Guard);
        Assert.Equal(Ascii("late but in time"), data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    public async Task The_request_timeout_interval_sets_the_limit_15_seconds_times_out_and_just_under_does_not()
    {
        // URLRequest(url:timeoutInterval: 15) at CommandCodeAPI.swift:169, PlanLimits.swift:243, ZaiAPI.swift:23.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var inTime = URLSession.Shared.Data(new URLRequest(server.Url("/a"), timeoutInterval: 15));
        var first = await server.NextExchange();
        runtime.Advance(TimeSpan.FromMilliseconds(14_999));
        await first.Respond(200, "OK", Ascii("a"));
        Assert.Equal(Ascii("a"), (await inTime.WaitAsync(Guard)).Data);

        var tooLate = URLSession.Shared.Data(new URLRequest(server.Url("/b"), timeoutInterval: 15));
        await server.NextExchange();
        runtime.Advance(TimeSpan.FromSeconds(15));
        var error = await Assert.ThrowsAsync<URLError>(() => tooLate.WaitAsync(Guard));
        Assert.Equal(URLError.Code.TimedOut, error.URLErrorCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    public async Task The_proxy_s_3600_second_limit_holds_a_silent_upstream_for_3600_seconds_and_no_longer()
    {
        // MCPProxy.swift:315: `request.timeoutInterval = 3_600` on the forwarded request. Together with
        // The_client_underneath_has_no_timeout_of_its_own_unlike_a_default_HttpClient this pins the whole limit: no
        // real-time cut at 100 s, and the unit's own idle timer at exactly 3,600 s on the manual clock.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var inTime = URLSession.Shared.Bytes(new URLRequest(server.Url("/a")) { HttpMethod = "GET", TimeoutInterval = 3_600 });
        var first = await server.NextExchange();
        runtime.Advance(TimeSpan.FromMilliseconds(3_599_999));
        await first.Respond(200, "OK", Ascii("a"));
        var (bytes, _) = await inTime.WaitAsync(Guard);
        await using (var reader = bytes.GetAsyncEnumerator()) Assert.Equal(Ascii("a"), await ReadToEnd(reader));

        var tooLate = URLSession.Shared.Bytes(new URLRequest(server.Url("/b")) { HttpMethod = "GET", TimeoutInterval = 3_600 });
        await server.NextExchange();
        runtime.Advance(TimeSpan.FromSeconds(3_600));
        var error = await Assert.ThrowsAsync<URLError>(() => tooLate.WaitAsync(Guard));
        Assert.Equal(URLError.Code.TimedOut, error.URLErrorCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    public async Task Every_arrival_of_data_re_arms_the_timeout_so_a_slow_stream_outlives_the_interval()
    {
        // R-NET-1: the limit is re-armed by every received chunk, not a total. Here the stream lasts 42 s under a
        // 15 s limit and never goes 15 s without data (the head at 14 s, a chunk at 28 s, the end at 42 s).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 15));
        var exchange = await server.NextExchange();
        runtime.Advance(TimeSpan.FromSeconds(14));
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Ascii("a"));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await using var reader = bytes.GetAsyncEnumerator();
        Assert.Equal(Ascii("a"), await ReadExactly(reader, 1));
        runtime.Advance(TimeSpan.FromSeconds(14));
        await exchange.WriteChunk(Ascii("b"));
        Assert.Equal(Ascii("b"), await ReadExactly(reader, 1));
        runtime.Advance(TimeSpan.FromSeconds(14));
        await exchange.WriteChunk(Ascii("c"));
        await exchange.EndChunks();
        Assert.Equal(Ascii("c"), await ReadToEnd(reader));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    public async Task A_stream_that_goes_silent_for_the_interval_fails_with_timedOut_while_being_read()
    {
        // The SSE drain (OpenAICompatibleSession.swift:740-745, :781-784) receives the transport error from the
        // byte sequence itself; its code decides the retry (:91-97).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 15));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Ascii("a"));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await using var reader = bytes.GetAsyncEnumerator();
        Assert.Equal(Ascii("a"), await ReadExactly(reader, 1));
        runtime.Advance(TimeSpan.FromSeconds(15));
        var error = await Assert.ThrowsAsync<URLError>(() => reader.MoveNextAsync().AsTask().WaitAsync(Guard));
        Assert.Equal(URLError.Code.TimedOut, error.URLErrorCode);
    }

    // ---- Cancellation and transport failures ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    public async Task Completing_while_a_cancellation_callback_waits_for_the_gate_does_not_deadlock()
    {
        // The timer pauses Complete after it takes the gate. Cancellation then enters Exchange.Fail and waits for
        // that gate. Complete must release the gate before synchronously unregistering the callback.
        using var runtime = new TestRuntime();
        using var callbackFinished = new ManualResetEventSlim();
        var clock = new HttpCompletionRaceClock();
        await using var server = new LoopbackHttpServer();
        var handle = TaskHandle.DetachedThrowing(async () =>
        {
            using var clockScope = SwiftClock.Provider.Override(clock);
            using var witness = SwiftTask.CancellationToken.Register(() => callbackFinished.Set());
            return await URLSession.Shared.Data(new URLRequest(server.Url("/race")));
        });
        var request = await server.NextExchange();
        await request.Respond(200, "OK", Ascii("done"));
        Assert.True(clock.WaitForIdleDispose(Guard));
        var cancelThread = new Thread(handle.Cancel) { IsBackground = true };
        cancelThread.Start();
        try
        {
            Assert.True(SpinWait.SpinUntil(
                () => (cancelThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, Guard));
            Assert.False(callbackFinished.IsSet);
        }
        finally
        {
            clock.ReleaseIdleDispose();
        }
        Assert.Equal(Ascii("done"), (await handle.Value.WaitAsync(Guard)).Data);
        Assert.True(cancelThread.Join(Guard));
        Assert.True(callbackFinished.IsSet);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TASK-2")]
    [Trait("Decision", "ED-066")]
    public async Task Cancelling_the_task_fails_the_request_with_URLError_cancelled_not_CancellationError()
    {
        // R-NET-1 and R-TASK-2: task cancellation surfaces as URLError(.cancelled) (NSURLErrorCancelled, -999), not as
        // CancellationError, so `catch is CancellationError` (OpenAICompatibleSession.swift:627) does not see it
        // and the generic catch (:629-632) does.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var url = server.Url("/hang");
        var handle = TaskHandle.DetachedThrowing(() => URLSession.Shared.Data(new URLRequest(url)));
        await server.NextExchange();
        handle.Cancel();
        var error = await Assert.ThrowsAsync<URLError>(() => handle.Value.WaitAsync(Guard));
        Assert.Equal(URLError.Code.Cancelled, error.URLErrorCode);
        Assert.False(typeof(OperationCanceledException).IsAssignableFrom(typeof(URLError)));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TASK-2")]
    [Trait("Decision", "ED-066")]
    [Trait("Oracle", "O-32")]
    public async Task Cancelling_the_task_while_a_stream_read_is_pending_fails_that_read_with_URLError_cancelled()
    {
        // The SSE drain runs in a detached task that a stop cancels directly (OpenAICompatibleSession.swift:652-661);
        // what the pending read then throws decides the path: `catch is CancellationError` (:781) finishes quietly,
        // any other error goes to `coalescer.fail(chunkError:)` (:784).
        // UNVERIFIED O-32: R-TASK-2 says URLSession throws URLError(.cancelled) for a cancelled task; that
        // a pending read of URLSession.AsyncBytes throws the same, and not CancellationError, is not documented. It is
        // the parked choice. The read is started before the cancel: `next` was observed not completed.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var url = server.Url("/sse");
        var readPending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = TaskHandle.DetachedThrowing(async () =>
        {
            var (bytes, _) = await URLSession.Shared.Bytes(new URLRequest(url, timeoutInterval: 300));
            await using var reader = bytes.GetAsyncEnumerator();
            Assert.True(await reader.MoveNextAsync());
            var next = reader.MoveNextAsync();
            readPending.SetResult(next.IsCompleted);
            return await next;
        });
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Ascii("d"));
        Assert.False(await readPending.Task.WaitAsync(Guard));
        handle.Cancel();
        var error = await Assert.ThrowsAsync<URLError>(() => handle.Value.WaitAsync(Guard));
        Assert.Equal(URLError.Code.Cancelled, error.URLErrorCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_refused_connection_fails_with_cannotConnectToHost()
    {
        // SEMANTICS: NSURLErrorCannotConnectToHost (-1004), "an attempt to connect to a host failed". One of the
        // three codes OpenAICompatibleSession.swift:94 retries.
        using var runtime = new TestRuntime();
        var pending = URLSession.Shared.Data(new URLRequest(LoopbackHttpServer.RefusedUrl("/closed")));
        var error = await Assert.ThrowsAsync<URLError>(() => pending.WaitAsync(Guard));
        Assert.Equal(URLError.Code.CannotConnectToHost, error.URLErrorCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_connection_severed_inside_the_body_fails_with_networkConnectionLost()
    {
        // SEMANTICS: NSURLErrorNetworkConnectionLost (-1005), "a client or server connection was severed in the
        // middle of an in-progress load". Retried by OpenAICompatibleSession.swift:94.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/cut")));
        var exchange = await server.NextExchange();
        await exchange.WriteHead(200, "OK", ("Content-Length", "100"));
        await exchange.Write(Ascii("0123456789"));
        exchange.Abort();
        var error = await Assert.ThrowsAsync<URLError>(() => pending.WaitAsync(Guard));
        Assert.Equal(URLError.Code.NetworkConnectionLost, error.URLErrorCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_stream_severed_while_being_read_fails_the_read_with_networkConnectionLost()
    {
        // OpenAICompatibleSession.swift:704-706: "The typed error (a URLError, normally) is carried up intact ... so
        // the caller can tell a dropped connection from any other failure."
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Bytes(new URLRequest(server.Url("/sse"), timeoutInterval: 300));
        var exchange = await server.NextExchange();
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
        await exchange.WriteChunk(Ascii("data: one\n"));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await using var reader = bytes.GetAsyncEnumerator();
        Assert.Equal(Ascii("data: one\n"), await ReadExactly(reader, 10));
        exchange.Abort();
        var error = await Assert.ThrowsAsync<URLError>(() => reader.MoveNextAsync().AsTask().WaitAsync(Guard));
        Assert.Equal(URLError.Code.NetworkConnectionLost, error.URLErrorCode);
    }

    // ---- URLError itself ----

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData(URLError.Code.Cancelled, -999L)]
    [InlineData(URLError.Code.BadURL, -1000L)]
    [InlineData(URLError.Code.TimedOut, -1001L)]
    [InlineData(URLError.Code.CannotConnectToHost, -1004L)]
    [InlineData(URLError.Code.NetworkConnectionLost, -1005L)]
    [InlineData(URLError.Code.BadServerResponse, -1011L)]
    public void URLError_codes_carry_Foundations_raw_values_and_a_constructed_error_keeps_its_code(URLError.Code code, long rawValue)
    {
        // SEMANTICS: URLError.Code.rawValue is the NSURLError constant (NSURLErrorCancelled = -999, NSURLErrorBadURL =
        // -1000, NSURLErrorTimedOut = -1001, NSURLErrorCannotConnectToHost = -1004, NSURLErrorNetworkConnectionLost =
        // -1005, NSURLErrorBadServerResponse = -1011). MCPProxy.swift:311 and :354 construct .badURL and
        // .badServerResponse themselves.
        Assert.Equal(rawValue, Convert.ToInt64(code, CultureInfo.InvariantCulture));
        Assert.Equal(code, new URLError(code).URLErrorCode);
    }

    // The text a URLError shows (OD-0056, replacing the earlier refusal to give it any) is in URLErrorTextTests.
}
