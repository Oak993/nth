using System.Globalization;
using System.IO.Compression;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port;

// Foundation unit F.HTTP (IMPLEMENTING.md 4.3): the HTTP client behind every URLSession site, R-NET-1. PORT-ONLY
// code, so these tests carry Rule and Decision traits and no Req ids (a foundation unit has no requirement records).
// Every request goes to LoopbackHttpServer on 127.0.0.1 (R-TEST-10); every timer runs on the manual clock
// (R-TEST-5, ED-068); LoopbackHttpServer.Guard only stops a broken implementation from hanging the run.
//
// Expected values come from the pinned Swift (B2 1fbf81e) and from URLSession's documented behaviour (SEMANTICS),
// never from a C# implementation. The call sites these tests stand for:
//   URLSession.shared.data(for:)   MCPHub.swift:455-477, MCPOAuth.swift:250-268, :715-742, :744-797,
//                                  MCPProbe.swift:309-325, CommandCodeAPI.swift:168-174, ZaiAPI.swift:182-194,
//                                  PlanLimits.swift:243-249, PiPlanLimits.swift:45-68, MarkdownView.swift:946-952
//   URLSession.shared.data(from:)  LinkTitles.swift:47-49
//   URLSession.shared.bytes(for:)  MCPProxy.swift:344-405, OpenAICompatibleSession.swift:623-643, :740-745
//   URLError codes                 OpenAICompatibleSession.swift:88-97 (retry by code), MCPProxy.swift:311, :354
//   expectedContentLength          UpdateChecker.swift:222-228
//
// The API these tests fix (namespace Marrow.Services.Port, file src/Marrow.Services/Port/Http.cs). Names follow
// R-NAME-1 to R-NAME-3; URLRequest is a Swift struct, so it is an immutable sealed record whose mutating
// setValue returns the changed copy (R-VAL-1):
//   sealed record URLRequest            URLRequest(Uri url, double timeoutInterval = 60); Uri Url { get; init; };
//                                       double TimeoutInterval { get; init; } (seconds); string HttpMethod { get; init; }
//                                       ("GET" unless set); byte[]? HttpBody { get; init; };
//                                       URLRequest SetValue(string? value, string forHTTPHeaderField);
//                                       string? Value(string forHTTPHeaderField)
//   abstract class URLResponse; sealed class HTTPURLResponse : URLResponse
//                                       int StatusCode; string? Value(string forHTTPHeaderField);
//                                       IReadOnlyDictionary<string, string> AllHeaderFields; long ExpectedContentLength
//   sealed class URLSession             static URLSession Shared;
//                                       Task<(byte[] Data, URLResponse Response)> Data(URLRequest request)   data(for:)
//                                       Task<(byte[] Data, URLResponse Response)> Data(Uri from)             data(from:)
//                                       Task<(URLSession.AsyncBytes Bytes, URLResponse Response)> Bytes(URLRequest request)
//                                       nested sealed AsyncBytes : IAsyncEnumerable<byte>                  bytes(for:)
//   sealed class URLError : Exception   URLError(URLError.Code code); URLError.Code URLErrorCode (Swift: code, R-NAME-3);
//                                       nested enum Code with the Foundation raw values below
// Timer contract the timeout tests rely on: the inactivity timer comes from SwiftClock.Provider as the caller sees it,
// is armed before the request is written, and is re-armed when the response head arrives and whenever body bytes
// arrive, before those bytes reach the caller. So "the server received the request" means the timer is armed, and
// "the caller read a byte" means the timer was re-armed for it.
public sealed class HttpTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

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

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task The_default_User_Agent_is_Marrow_slash_the_app_version_sent_once()
    {
        // R-NET-1: URLSession.shared sends a default User-Agent (the source's is "Droppy%20Code/<build> CFNetwork/...").
        // DEVIATION OD-0012 F1 (owner_decisions.md, decision_batch_20260922b.md section F): every "Droppy Code" the
        // app sends out becomes "Marrow", and the sheet names the network User-Agent "Marrow/<version>";
        // <version> is AppInfo.version (Text.swift:7), as in the source's own "Droppy Code/\(AppInfo.version)"
        // (UpdateChecker.swift:108, AppUpdater.swift:481).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/ua")));
        var exchange = await server.NextExchange();
        Assert.Equal(["Marrow/" + AppInfo.Current.Version], exchange.HeaderValues("User-Agent"));
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
        var request = new URLRequest(server.Url("/ua")).SetValue("Marrow/9.8.7", forHTTPHeaderField: "User-Agent");
        var pending = URLSession.Shared.Data(request);
        var exchange = await server.NextExchange();
        Assert.Equal(["Marrow/9.8.7"], exchange.HeaderValues("User-Agent"));
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

    // ---- Redirects ----

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task A_GET_follows_a_redirect_and_returns_the_final_response(int status)
    {
        // R-NET-1: redirects are followed; the caller sees only the final response (UpdateChecker.swift:223-228 and
        // LinkTitles.swift:47-49 read the status and body of wherever the chain ends).
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
    public async Task A_HEAD_answer_reports_its_Content_Length_as_the_expected_length()
    {
        // UpdateChecker.swift:222-228: HEAD, then expectedContentLength when it is above zero.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(new URLRequest(server.Url("/Marrow.exe")) { HttpMethod = "HEAD" });
        var exchange = await server.NextExchange();
        Assert.Equal("HEAD", exchange.Method);
        await exchange.WriteHead(200, "OK", ("Content-Length", "12345"));
        exchange.Finish();
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Empty(data);
        Assert.Equal(12345L, Assert.IsType<HTTPURLResponse>(response).ExpectedContentLength);
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

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public void A_URLError_has_no_invented_localized_description()
    {
        // R-ERR-2: a Foundation NSError's localized text cannot be reproduced on Windows, so the helper refuses it and
        // each site that shows one (MCPProxy.swift:302, OpenAICompatibleSession.swift:702) needs its own decision.
        Assert.Throws<NotSupportedException>(() => SwiftErrors.LocalizedDescription(new URLError(URLError.Code.TimedOut)));
    }

    // ---- Helpers ----

    private static async Task<byte[]> ReadExactly(IAsyncEnumerator<byte> reader, int count)
    {
        var read = new List<byte>(count);
        while (read.Count < count)
        {
            Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(Guard), "the stream ended early");
            read.Add(reader.Current);
        }
        return [.. read];
    }

    private static async Task<byte[]> ReadToEnd(IAsyncEnumerator<byte> reader)
    {
        var read = new List<byte>();
        while (await reader.MoveNextAsync().AsTask().WaitAsync(Guard)) read.Add(reader.Current);
        return [.. read];
    }

    private static byte[] Compress(byte[] plain, string coding)
    {
        using var output = new MemoryStream();
        using (Stream compressor = coding switch
        {
            "gzip" => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true),
            "deflate" => new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true),
            "br" => new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(coding)),
        })
        {
            compressor.Write(plain);
        }
        return output.ToArray();
    }
}
