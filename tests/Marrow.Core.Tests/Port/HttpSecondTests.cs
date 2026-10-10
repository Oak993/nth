using Marrow.Core.Support;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Core.Tests.Fixtures.HttpReading;

namespace Marrow.Core.Tests.Port;

// Foundation unit F.HTTP, second part of the tests (R-TEST-3 suffix): URLSession(configuration:) as
// UpdateChecker.swift:101-110 builds it, and the network defaults a server sees from URLSession.shared (cookies,
// the request headers URLSession adds, the response cache). The API, the timer contract and the oracle items O-26
// to O-33 (IMPLEMENTING.md Appendix B) are listed at the top of HttpTests.cs. Same determinism rules: loopback
// server, manual clock. The two tests that change process-wide state (the shared cookie store and the shared cache)
// are in HttpProcessStateTests, a collection that never runs in parallel (R-TEST-5, ED-118).
//
// AppUpdater.swift:472-484 also builds an ephemeral session (request 30 s, resource 1,800 s), but for a
// downloadTask with a progress delegate; platform_adaptations.md ("What does NOT carry over (App/AppUpdater.swift)")
// replaces that install path, so it is not a caller of this unit and is not tested here.
public sealed class HttpSecondTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    /// <summary>The session UpdateChecker.swift:101-110 builds, with the User-Agent value OD-0012 F1 gives it.</summary>
    private static URLSession UpdateCheckerSession()
    {
        var configuration = URLSessionConfiguration.Ephemeral;
        configuration.RequestCachePolicy = URLRequest.CachePolicy.ReloadIgnoringLocalCacheData;
        configuration.UrlCache = null;
        configuration.TimeoutIntervalForRequest = 15;
        configuration.TimeoutIntervalForResource = 30;
        configuration.WaitsForConnectivity = false;
        // DEVIATION OD-0012|ED-088: the source's "Droppy Code/\(AppInfo.version)" (UpdateChecker.swift:108) is
        // "Marrow/<version>"; UpdateChecker's own unit writes it, this unit only carries whatever it is given.
        configuration.HttpAdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["User-Agent"] = "nth/" + AppInfo.Current.Version,
            ["Accept"] = "application/json",
        };
        return new URLSession(configuration);
    }

    private static AppInfo AppVersion(string version, string build) =>
        AppInfo.Read(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CFBundleDisplayName"] = "Droppy Code",
            ["CFBundleShortVersionString"] = version,
            ["CFBundleVersion"] = build,
        }, isDevelopment: false);

    // ---- URLSessionConfiguration ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public void Ephemeral_gives_a_new_configuration_on_every_read_with_Foundation_s_defaults()
    {
        // SEMANTICS: `class var ephemeral` returns a new configuration object each time, which UpdateChecker.swift:102-109
        // then changes; its documented defaults are timeoutIntervalForRequest 60, timeoutIntervalForResource 7 days
        // (604,800 s), requestCachePolicy .useProtocolCachePolicy, waitsForConnectivity false and no
        // httpAdditionalHeaders.
        var configuration = URLSessionConfiguration.Ephemeral;
        Assert.NotSame(configuration, URLSessionConfiguration.Ephemeral);
        Assert.Equal(60.0, configuration.TimeoutIntervalForRequest);
        Assert.Equal(604_800.0, configuration.TimeoutIntervalForResource);
        Assert.Equal(URLRequest.CachePolicy.UseProtocolCachePolicy, configuration.RequestCachePolicy);
        Assert.False(configuration.WaitsForConnectivity);
        Assert.Null(configuration.HttpAdditionalHeaders);
    }

    [Theory]
    [Trait("Rule", "R-NET-1")]
    [InlineData(URLRequest.CachePolicy.UseProtocolCachePolicy, 0L)]
    [InlineData(URLRequest.CachePolicy.ReloadIgnoringLocalCacheData, 1L)]
    public void Cache_policies_carry_Foundations_raw_values(URLRequest.CachePolicy policy, long rawValue)
    {
        // SEMANTICS: NSURLRequestUseProtocolCachePolicy = 0, NSURLRequestReloadIgnoringLocalCacheData = 1
        // (UpdateChecker.swift:103 sets the second).
        Assert.Equal(rawValue, Convert.ToInt64(policy, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_configured_session_s_client_has_no_timeout_of_its_own_either()
    {
        // R-NET-1: the limits are the unit's own timers on the manual clock (the 15 s idle and 30 s resource limits of
        // UpdateChecker.swift:105-106), never HttpClient.Timeout; a default HttpClient's 100 s is the negative control
        // in HttpTests.The_client_underneath_has_no_timeout_of_its_own_unlike_a_default_HttpClient.
        Assert.Equal(Timeout.InfiniteTimeSpan, UpdateCheckerSession().HttpClientTimeout);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-088")]
    public async Task The_default_User_Agent_names_the_version_of_the_configuration_s_app_and_not_its_build()
    {
        // DEVIATION OD-0012|ED-088: the source's default User-Agent is "Droppy%20Code/<CFBundleVersion> CFNetwork/<v>
        // Darwin/<v>"; OD-0012 F1 names it "Marrow/<version>", the short version string (Text.swift:7), and drops the
        // CFNetwork and Darwin tokens. The version here is one the test controls (7.6.5, build 42), so a port that
        // writes a fixed version, the build number or an extra product token fails; the raw line is compared as sent.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var configuration = URLSessionConfiguration.Ephemeral;
        using var app = AppInfo.Running.Override(AppVersion("7.6.5", "42"));
        var pending = new URLSession(configuration).Data(new URLRequest(server.Url("/ua")));
        var exchange = await server.NextExchange();
        var lines = RawHeaderLines(exchange.RawHead)
            .Where(line => line.StartsWith("user-agent:", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(["User-Agent: nth/7.6.5"], lines);
        await exchange.Respond(204, "No Content", []);
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task Additional_headers_reach_the_wire_once_and_a_request_header_of_the_same_name_wins()
    {
        // UpdateChecker.swift:108 and :190 (session.data(from: Self.apiURL)): httpAdditionalHeaders User-Agent and
        // Accept go out on every request of the session, the User-Agent in place of the default one. SEMANTICS
        // (httpAdditionalHeaders): when the request itself sets a header of the same name, the request's value is sent.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = UpdateCheckerSession();
        var pending = session.Data(server.Url("/api/v4/projects/86415974/releases?per_page=20"));
        var exchange = await server.NextExchange();
        Assert.Equal(["nth/" + AppInfo.Current.Version], exchange.HeaderValues("User-Agent"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));
        await exchange.Respond(200, "OK", Ascii("[]"));
        await pending.WaitAsync(Guard);

        var own = session.Data(new URLRequest(server.Url("/other")).SetValue("text/html", forHTTPHeaderField: "Accept"));
        var second = await server.NextExchange();
        Assert.Equal(["text/html"], second.HeaderValues("Accept"));
        Assert.Equal(["nth/" + AppInfo.Current.Version], second.HeaderValues("User-Agent"));
        await second.Respond(200, "OK", []);
        await own.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-29")]
    [Trait("Decision", "ED-152")]
    public async Task A_configuration_Authorization_is_not_sent_again_once_the_chain_leaves_its_origin()
    {
        // UNVERIFIED O-29: ED-152's safety default keeps the drop sticky across later same-origin hops.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        await using var other = new LoopbackHttpServer();
        var configuration = URLSessionConfiguration.Ephemeral;
        configuration.HttpAdditionalHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer config-1",
            ["X-Custom"] = "k",
        };
        var pending = new URLSession(configuration).Data(new URLRequest(server.Url("/a")));
        var first = await server.NextExchange();
        Assert.Equal("/a", first.Target);
        Assert.Equal(["Bearer config-1"], first.HeaderValues("Authorization"));
        await first.Respond(302, "Found", [], ("Location", other.Url("/b").AbsoluteUri));
        var second = await other.NextExchange();
        Assert.Equal("/b", second.Target);
        Assert.Empty(second.HeaderValues("Authorization"));
        Assert.Equal(["k"], second.HeaderValues("X-Custom"));
        await second.Respond(302, "Found", [], ("Location", "/c"));
        var third = await other.NextExchange();
        Assert.Equal("/c", third.Target);
        Assert.Empty(third.HeaderValues("Authorization"));
        Assert.Equal(["k"], third.HeaderValues("X-Custom"));
        await third.Respond(302, "Found", [], ("Location", server.Url("/d").AbsoluteUri));
        var fourth = await server.NextExchange();
        Assert.Equal("/d", fourth.Target);
        Assert.Empty(fourth.HeaderValues("Authorization"));
        await fourth.Respond(200, "OK", Ascii("done"));
        Assert.Equal(Ascii("done"), (await pending.WaitAsync(Guard)).Data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-29")]
    [Trait("Decision", "ED-152")]
    public async Task A_configuration_Authorization_is_kept_on_a_same_origin_redirect()
    {
        // UNVERIFIED O-29: no origin change means the configuration field remains available.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var configuration = URLSessionConfiguration.Ephemeral;
        configuration.HttpAdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer config-1" };
        var pending = new URLSession(configuration).Data(new URLRequest(server.Url("/a")));
        var first = await server.NextExchange();
        Assert.Equal(["Bearer config-1"], first.HeaderValues("Authorization"));
        await first.Respond(302, "Found", [], ("Location", "/b"));
        var second = await server.NextExchange();
        Assert.Equal("/b", second.Target);
        Assert.Equal(["Bearer config-1"], second.HeaderValues("Authorization"));
        await second.Respond(200, "OK", []);
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    public async Task The_resource_timeout_ends_the_whole_task_at_30_seconds_even_while_data_keeps_arriving()
    {
        // UpdateChecker.swift:106: timeoutIntervalForResource = 30. SEMANTICS: the resource timer starts with the task
        // and runs until the task completes or the interval passes, whichever is first; it is a total, unlike the idle
        // limit, and the task fails with URLError(.timedOut). Here data arrives every 10 s, under the 15 s idle
        // limit, and the task still ends at 30 s: alive at 29.999 s, timed out at 30 s.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = UpdateCheckerSession().Bytes(new URLRequest(server.Url("/feed"), timeoutInterval: 15));
        var exchange = await server.NextExchange();
        runtime.Advance(TimeSpan.FromSeconds(10));
        await exchange.WriteChunkedHead(200, "OK", ("Content-Type", "application/json"));
        await exchange.WriteChunk(Ascii("["));
        var (bytes, _) = await pending.WaitAsync(Guard);
        await using var reader = bytes.GetAsyncEnumerator();
        Assert.Equal(Ascii("["), await ReadExactly(reader, 1));
        runtime.Advance(TimeSpan.FromSeconds(10));
        await exchange.WriteChunk(Ascii("{"));
        Assert.Equal(Ascii("{"), await ReadExactly(reader, 1));
        runtime.Advance(TimeSpan.FromMilliseconds(9_999));
        await exchange.WriteChunk(Ascii("}"));
        Assert.Equal(Ascii("}"), await ReadExactly(reader, 1));
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        var error = await Assert.ThrowsAsync<URLError>(() => reader.MoveNextAsync().AsTask().WaitAsync(Guard));
        Assert.Equal(URLError.Code.TimedOut, error.URLErrorCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-068")]
    [Trait("Oracle", "O-31")]
    public async Task A_request_made_from_a_URL_alone_on_the_update_checker_session_idles_out_after_the_configuration_s_15_seconds()
    {
        // UpdateChecker.swift:223 builds its HEAD request with URLRequest(url:), and :190 fetches the feed with
        // data(from:); both requests carry the default timeoutInterval 60, on a session whose timeoutIntervalForRequest
        // is 15 (:105).
        // UNVERIFIED O-31: which of the two limits URLSession applies is not documented. The parked choice
        // is 15 s, the configuration's, which is what the author set it for and what both readings in circulation
        // (the configuration wins over a request default; the shorter limit wins) give here.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = UpdateCheckerSession();
        var inTime = session.Data(new URLRequest(server.Url("/a")) { HttpMethod = "HEAD" });
        var first = await server.NextExchange();
        runtime.Advance(TimeSpan.FromMilliseconds(14_999));
        await first.WriteHead(200, "OK", ("Content-Length", "1"));
        first.Finish();
        await inTime.WaitAsync(Guard);

        var tooLate = session.Data(new URLRequest(server.Url("/b")) { HttpMethod = "HEAD" });
        await server.NextExchange();
        runtime.Advance(TimeSpan.FromSeconds(15));
        var error = await Assert.ThrowsAsync<URLError>(() => tooLate.WaitAsync(Guard));
        Assert.Equal(URLError.Code.TimedOut, error.URLErrorCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_HEAD_answer_on_the_update_checker_session_reports_its_Content_Length_as_the_expected_length()
    {
        // UpdateChecker.swift:221-228 (downloadSize(of:)): a HEAD request through the session of :101-110, then
        // expectedContentLength when the status is 2xx and the length is above zero. No body is read for HEAD.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = UpdateCheckerSession().Data(new URLRequest(server.Url("/Marrow.exe")) { HttpMethod = "HEAD" });
        var exchange = await server.NextExchange();
        Assert.Equal("HEAD", exchange.Method);
        await exchange.WriteHead(200, "OK", ("Content-Length", "12345"));
        exchange.Finish();
        var (data, response) = await pending.WaitAsync(Guard);
        Assert.Empty(data);
        var http = Assert.IsType<HTTPURLResponse>(response);
        Assert.Equal(200, http.StatusCode);
        Assert.Equal(12345L, http.ExpectedContentLength);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task The_update_checker_session_asks_the_server_every_time_even_for_a_cacheable_answer()
    {
        // UpdateChecker.swift:103-104 and :190: reloadIgnoringLocalCacheData and urlCache = nil, so a release feed answered with
        // a freshness lifetime is fetched again on the next check (every 6 hours and on demand, :59-64).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = UpdateCheckerSession();
        var url = server.Url("/api/v4/projects/86415974/releases?per_page=20");
        var first = session.Data(url);
        var one = await server.NextExchange();
        await one.Respond(200, "OK", Ascii("[1]"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("[1]"), (await first.WaitAsync(Guard)).Data);

        var again = session.Data(url);
        var two = await server.NextExchange();
        Assert.Equal("/api/v4/projects/86415974/releases?per_page=20", two.Target);
        await two.Respond(200, "OK", Ascii("[2]"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("[2]"), (await again.WaitAsync(Guard)).Data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    public async Task A_session_that_does_not_wait_for_connectivity_fails_a_refused_connection_with_cannotConnectToHost()
    {
        // UpdateChecker.swift:107: waitsForConnectivity = false, so a refused connection fails the task at once with
        // NSURLErrorCannotConnectToHost (-1004); :181-182 then records "Couldn't reach GitLab.".
        using var runtime = new TestRuntime();
        var pending = UpdateCheckerSession().Data(new URLRequest(LoopbackHttpServer.RefusedUrl("/releases")));
        var error = await Assert.ThrowsAsync<URLError>(() => pending.WaitAsync(Guard));
        Assert.Equal(URLError.Code.CannotConnectToHost, error.URLErrorCode);
    }

    // ---- Network defaults a server sees (ED-118) ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-28")]
    public async Task An_ephemeral_session_keeps_its_cookies_to_itself()
    {
        // SEMANTICS (URLSessionConfiguration.ephemeral): an ephemeral session stores no session data on disk and keeps
        // it in RAM instead, so its cookies live in a store of its own for the life of the session: the next request
        // of the same session carries the cookie, another ephemeral session and URLSession.shared do not.
        // UpdateChecker.swift:101 keeps one such session for the app's life.
        // UNVERIFIED O-28: the parked choice is that behaviour (ED-118: a memory store per ephemeral session).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var jar = "/jar-" + Guid.NewGuid().ToString("N");
        var own = new URLSession(URLSessionConfiguration.Ephemeral);
        var setting = own.Data(new URLRequest(server.Url(jar + "/set")));
        var first = await server.NextExchange();
        await first.Respond(200, "OK", [], ("Set-Cookie", "a=1; Path=" + jar));
        await setting.WaitAsync(Guard);

        var same = own.Data(new URLRequest(server.Url(jar + "/same")));
        var second = await server.NextExchange();
        Assert.Equal(["a=1"], second.HeaderValues("Cookie"));
        await second.Respond(204, "No Content", []);
        await same.WaitAsync(Guard);

        var other = new URLSession(URLSessionConfiguration.Ephemeral).Data(new URLRequest(server.Url(jar + "/other")));
        var third = await server.NextExchange();
        Assert.Empty(third.HeaderValues("Cookie"));
        await third.Respond(204, "No Content", []);
        await other.WaitAsync(Guard);

        var shared = URLSession.Shared.Data(new URLRequest(server.Url(jar + "/shared")));
        var fourth = await server.NextExchange();
        Assert.Empty(fourth.HeaderValues("Cookie"));
        await fourth.Respond(204, "No Content", []);
        await shared.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-27")]
    public async Task A_request_without_Accept_goes_out_with_the_headers_URLSession_adds()
    {
        // LinkTitles.swift:47 (data(from:)), MarkdownView.swift:946-949 (the favicon) and MCPProxy.swift:317-326 (when
        // the client sent no Accept) set no Accept, so the server sees what URLSession adds: Accept "*/*", one
        // Accept-Language, and Accept-Encoding "gzip, deflate, br".
        // UNVERIFIED O-27: the parked choice is the headers CFNetwork is believed to add (ED-118). Accept-Language is
        // the constant "en-US,en;q=0.9": the app ships English only (project.pbxproj:107-112), so CFNetwork is
        // believed to send an English tag whatever the system language, and Windows' language list is not used.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var pending = URLSession.Shared.Data(server.Url("/oembed?format=json"));
        var exchange = await server.NextExchange();
        Assert.Equal(["*/*"], exchange.HeaderValues("Accept"));
        Assert.Equal(["en-US,en;q=0.9"], exchange.HeaderValues("Accept-Language"));
        Assert.Equal(["gzip, deflate, br"], exchange.HeaderValues("Accept-Encoding"));
        await exchange.Respond(204, "No Content", []);
        await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Oracle", "O-27")]
    public async Task A_body_sent_without_a_Content_Type_goes_out_as_a_form()
    {
        // MCPProxy.swift:317-326 and :340-342: the client's body is forwarded, and Content-Type only when the client
        // sent one. URLSession then gives the body the type "application/x-www-form-urlencoded".
        // UNVERIFIED O-27: the parked choice is that CFNetwork default.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var body = Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}");
        var request = new URLRequest(server.Url("/mcp")) { HttpMethod = "POST", HttpBody = body, TimeoutInterval = 3_600 };
        var pending = URLSession.Shared.Bytes(request);
        var exchange = await server.NextExchange();
        Assert.Equal(["application/x-www-form-urlencoded"], exchange.HeaderValues("Content-Type"));
        Assert.Equal(body, exchange.Body);
        await exchange.Respond(202, "Accepted", []);
        await pending.WaitAsync(Guard);
    }
}
