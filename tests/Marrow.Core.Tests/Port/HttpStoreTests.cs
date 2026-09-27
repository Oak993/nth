using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Core.Tests.Fixtures.HttpReading;

namespace Marrow.Core.Tests.Port;

// Foundation unit F.HTTP, fourth part of the tests (R-TEST-3 suffix): the rules of the two stores ED-118 gives the
// sessions, the RFC 6265 cookie store (item 3, O-28) and the response cache (item 4, O-33), each case on its own.
// Every test here uses sessions made from URLSessionConfiguration.Ephemeral, whose cookie store and cache belong to
// that session or configuration alone, so the tests touch no process-wide state and may run in parallel; the
// URLSession.Shared cases are in HttpProcessStateTests (R-TEST-5). The API, the timer contract and the oracle items
// O-26 to O-33 (IMPLEMENTING.md Appendix B) are listed at the top of HttpTests.cs. Loopback server, manual clock:
// TestRuntime's ManualTimeProvider starts at 2026-09-22 12:00:00 UTC and moves only when a test advances it, so an
// expiry that happens on it and not on the wall clock (or the reverse) fails these tests.
//
// UNVERIFIED O-28 and O-33: the parked choices are the ones ED-118 states; the Swift call sites are the
// URLSession.shared calls (for example PlanLimits.swift:247, LinkTitles.swift:47, MarkdownView.swift:949 and
// MCPHub.swift:465) and UpdateChecker.swift:101-110.
public sealed class HttpStoreTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    // ---- Response cache (ED-118 item 4, O-33) ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task An_answer_fresh_by_Expires_is_served_from_the_cache_until_that_lifetime_elapses_on_SwiftClock()
    {
        // ED-118 item 4: explicit freshness is max-age or Expires; with Expires the lifetime is Expires minus Date
        // (RFC 9111 4.2.1), here 600 s. The age is measured on SwiftClock.Provider: 599 s later the answer is fresh,
        // 600 s later it is stale and the server is asked again.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        var url = server.Url("/favicon.ico");
        var first = session.Data(url);
        await (await arrivals.Next()).Respond(200, "OK", Ascii("v1"),
            ("Date", "Tue, 22 Sep 2026 12:00:00 GMT"), ("Expires", "Tue, 22 Sep 2026 12:10:00 GMT"));
        Assert.Equal(Ascii("v1"), (await first.WaitAsync(Guard)).Data);

        runtime.Advance(TimeSpan.FromSeconds(599));
        var fresh = session.Data(url);
        Assert.False(await arrivals.Reached(fresh));
        Assert.Equal(Ascii("v1"), (await fresh).Data);

        runtime.Advance(TimeSpan.FromSeconds(1));
        var stale = session.Data(url);
        Assert.True(await arrivals.Reached(stale));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("v2"));
        Assert.Equal(Ascii("v2"), (await stale.WaitAsync(Guard)).Data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task An_answer_without_explicit_freshness_is_not_stored()
    {
        // ED-118 item 4 and O-33: no heuristic freshness, so an answer with Last-Modified and no max-age or Expires is
        // not kept and the next GET reaches the server.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        var url = server.Url("/page");
        var first = session.Data(url);
        await (await arrivals.Next()).Respond(200, "OK", Ascii("v1"),
            ("Date", "Tue, 22 Sep 2026 12:00:00 GMT"), ("Last-Modified", "Mon, 01 Jan 2024 00:00:00 GMT"));
        await first.WaitAsync(Guard);

        var again = session.Data(url);
        Assert.True(await arrivals.Reached(again));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("v2"));
        Assert.Equal(Ascii("v2"), (await again.WaitAsync(Guard)).Data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task A_no_store_answer_is_not_stored_even_with_a_max_age()
    {
        // ED-118 item 4: an answer marked no-store is never stored.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        var url = server.Url("/usage");
        var first = session.Data(url);
        await (await arrivals.Next()).Respond(200, "OK", Ascii("v1"), ("Cache-Control", "max-age=600, no-store"));
        await first.WaitAsync(Guard);

        var again = session.Data(url);
        Assert.True(await arrivals.Reached(again));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("v2"));
        Assert.Equal(Ascii("v2"), (await again.WaitAsync(Guard)).Data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task An_answer_to_a_POST_is_not_stored_and_a_POST_is_never_answered_from_the_cache()
    {
        // ED-118 item 4: only GET answers are stored. A POST answered with max-age leaves nothing for the next POST
        // or the next GET of the same URL; both reach the server.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        var url = server.Url("/mcp");
        var post = session.Data(new URLRequest(url) { HttpMethod = "POST", HttpBody = Ascii("{}") });
        var one = await arrivals.Next();
        Assert.Equal("POST", one.Method);
        await one.Respond(200, "OK", Ascii("p1"), ("Cache-Control", "max-age=600"));
        await post.WaitAsync(Guard);

        var postAgain = session.Data(new URLRequest(url) { HttpMethod = "POST", HttpBody = Ascii("{}") });
        Assert.True(await arrivals.Reached(postAgain));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("p2"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("p2"), (await postAgain.WaitAsync(Guard)).Data);

        var get = session.Data(url);
        Assert.True(await arrivals.Reached(get));
        var three = await arrivals.Next();
        Assert.Equal("GET", three.Method);
        await three.Respond(200, "OK", Ascii("g1"));
        Assert.Equal(Ascii("g1"), (await get.WaitAsync(Guard)).Data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task A_non_2xx_answer_is_not_stored_even_with_a_max_age()
    {
        // ED-118 item 4: only status 200-299 is stored. A 404 with max-age leaves the next GET to the server, which
        // then answers 200.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        var url = server.Url("/missing");
        var first = session.Data(url);
        await (await arrivals.Next()).Respond(404, "Not Found", Ascii("no"), ("Cache-Control", "max-age=600"));
        Assert.Equal(404, Assert.IsType<HTTPURLResponse>((await first.WaitAsync(Guard)).Response).StatusCode);

        var again = session.Data(url);
        Assert.True(await arrivals.Reached(again));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("yes"));
        var (data, response) = await again.WaitAsync(Guard);
        Assert.Equal(Ascii("yes"), data);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task A_configuration_whose_cache_is_nil_has_no_cache_under_the_protocol_cache_policy()
    {
        // ED-118 item 4: UrlCache = null means no cache at all, whatever the policy. UpdateChecker.swift:104 sets it
        // together with :103's policy; here the policy stays .useProtocolCachePolicy, so the cache being nil alone
        // sends the second GET to the server.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var configuration = URLSessionConfiguration.Ephemeral;
        configuration.UrlCache = null;
        Assert.Equal(URLRequest.CachePolicy.UseProtocolCachePolicy, configuration.RequestCachePolicy);
        var session = new URLSession(configuration);
        var url = server.Url("/releases");
        var first = session.Data(url);
        await (await arrivals.Next()).Respond(200, "OK", Ascii("[1]"), ("Cache-Control", "max-age=600"));
        await first.WaitAsync(Guard);

        var again = session.Data(url);
        Assert.True(await arrivals.Reached(again));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("[2]"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("[2]"), (await again.WaitAsync(Guard)).Data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task ReloadIgnoringLocalCacheData_never_reads_the_cache_even_when_one_is_present()
    {
        // ED-118 item 4: .reloadIgnoringLocalCacheData never reads the cache. The configuration keeps the cache
        // Ephemeral gives it (UpdateChecker.swift:103 without :104), and every GET still reaches the server.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var configuration = URLSessionConfiguration.Ephemeral;
        configuration.RequestCachePolicy = URLRequest.CachePolicy.ReloadIgnoringLocalCacheData;
        Assert.NotNull(configuration.UrlCache);
        var session = new URLSession(configuration);
        var url = server.Url("/releases");
        var first = session.Data(url);
        await (await arrivals.Next()).Respond(200, "OK", Ascii("[1]"), ("Cache-Control", "max-age=600"));
        await first.WaitAsync(Guard);

        var again = session.Data(url);
        Assert.True(await arrivals.Reached(again));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("[2]"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("[2]"), (await again.WaitAsync(Guard)).Data);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task Two_ephemeral_configurations_do_not_share_cached_answers()
    {
        // ED-118 item 4: Ephemeral's default UrlCache is a new memory-only cache private to that configuration. A
        // session of configuration A stores an answer; a session of configuration B asks the server for the same URL
        // and keeps its own answer; each is then served its own answer from its own cache.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var a = new URLSession(URLSessionConfiguration.Ephemeral);
        var b = new URLSession(URLSessionConfiguration.Ephemeral);
        var url = server.Url("/shared-url");
        var fromA = a.Data(url);
        await (await arrivals.Next()).Respond(200, "OK", Ascii("A"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("A"), (await fromA.WaitAsync(Guard)).Data);

        var fromB = b.Data(url);
        Assert.True(await arrivals.Reached(fromB));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("B"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("B"), (await fromB.WaitAsync(Guard)).Data);

        var againA = a.Data(url);
        Assert.False(await arrivals.Reached(againA));
        Assert.Equal(Ascii("A"), (await againA).Data);
        var againB = b.Data(url);
        Assert.False(await arrivals.Reached(againB));
        Assert.Equal(Ascii("B"), (await againB).Data);
    }

    [Fact]
    [Trait("Decision", "ED-118")]
    public void URLCache_records_the_memory_and_disk_capacities_it_is_constructed_with()
    {
        // AppDelegate.swift:13 (B3 52bc792): URLCache(memoryCapacity: 8 * 1024 * 1024, diskCapacity: 0). ED-118 item
        // 4 parks "no size cut-off" (IMPLEMENTING.md Appendix B, O-33), so the unit does not evict on either number;
        // it only has to accept and report them, matching Foundation's URLCache(memoryCapacity:diskCapacity:).
        var cache = new URLCache(memoryCapacity: 8 * 1024 * 1024, diskCapacity: 0);
        Assert.Equal(8 * 1024 * 1024, cache.MemoryCapacity);
        Assert.Equal(0, cache.DiskCapacity);
    }

    // ---- Cookie store (ED-118 item 3, O-28: RFC 6265) ----

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task A_cookie_goes_only_to_paths_its_Path_matches()
    {
        // RFC 6265 5.1.4: "/jar/in" matches "/jar/in" and "/jar/in/deeper/x", not "/jar/inside" or "/jar".
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        Assert.Empty(await CookiesSent(session, server, "/jar/in/set", ("Set-Cookie", "a=1; Path=/jar/in")));
        Assert.Empty(await CookiesSent(session, server, "/jar/inside"));
        Assert.Empty(await CookiesSent(session, server, "/jar"));
        Assert.Equal(["a=1"], await CookiesSent(session, server, "/jar/in"));
        Assert.Equal(["a=1"], await CookiesSent(session, server, "/jar/in/deeper/x?q=1"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task A_cookie_without_a_valid_Path_gets_the_directory_of_the_request_path()
    {
        // RFC 6265 5.2.4 and 5.1.4: no Path, or one not starting with "/", gives the default-path, here "/dir/sub".
        // Two cookies of equal path length go out in creation order (5.4 step 2).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        await CookiesSent(session, server, "/dir/sub/set", ("Set-Cookie", "b=2"), ("Set-Cookie", "c=3; Path=relative"));
        Assert.Equal(["b=2; c=3"], await CookiesSent(session, server, "/dir/sub/x"));
        Assert.Equal(["b=2; c=3"], await CookiesSent(session, server, "/dir/sub"));
        Assert.Empty(await CookiesSent(session, server, "/dir/subway"));
        Assert.Empty(await CookiesSent(session, server, "/dir/x"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task A_cookie_with_a_longer_path_goes_first()
    {
        // RFC 6265 5.4 step 2: cookies with longer paths are listed before cookies with shorter paths.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        await CookiesSent(session, server, "/o/deep/set", ("Set-Cookie", "short=1; Path=/o"), ("Set-Cookie", "long=2; Path=/o/deep"));
        Assert.Equal(["long=2; short=1"], await CookiesSent(session, server, "/o/deep/x"));
        Assert.Equal(["short=1"], await CookiesSent(session, server, "/o/x"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task A_cookie_set_again_replaces_the_value_and_keeps_its_place()
    {
        // RFC 6265 5.3 step 11: a cookie with the same name, domain and path replaces the old one and keeps the old
        // creation-time, so it still goes out before a cookie created after the original.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        await CookiesSent(session, server, "/r/set", ("Set-Cookie", "a=1; Path=/r"), ("Set-Cookie", "b=1; Path=/r"));
        await CookiesSent(session, server, "/r/again", ("Set-Cookie", "a=2; Path=/r"));
        Assert.Equal(["a=2; b=1"], await CookiesSent(session, server, "/r/x"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task A_cookie_with_Max_Age_expires_after_that_many_seconds_on_SwiftClock()
    {
        // RFC 6265 5.2.2 and 5.3 step 3: Max-Age=60 expires 60 s after it is received; the store's time is SwiftClock.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        await CookiesSent(session, server, "/m/set", ("Set-Cookie", "m=1; Path=/m; Max-Age=60"));
        runtime.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(["m=1"], await CookiesSent(session, server, "/m/x"));
        runtime.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(await CookiesSent(session, server, "/m/x"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task A_cookie_with_Expires_expires_at_that_date_on_SwiftClock()
    {
        // RFC 6265 5.2.1: Expires is a cookie-date. The manual clock starts at 12:00:00, so a cookie expiring at
        // 12:01:00 goes out now and not after 61 s. On the wall clock (a later day) it would never go out.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        await CookiesSent(session, server, "/e/set", ("Set-Cookie", "e=1; Path=/e; Expires=Tue, 22 Sep 2026 12:01:00 GMT"));
        Assert.Equal(["e=1"], await CookiesSent(session, server, "/e/x"));
        runtime.Advance(TimeSpan.FromSeconds(61));
        Assert.Empty(await CookiesSent(session, server, "/e/x"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task Max_Age_zero_and_a_past_Expires_remove_a_cookie_and_Max_Age_wins_over_Expires()
    {
        // RFC 6265 5.2.2 (Max-Age <= 0 is the earliest time), 5.3 step 3 (Max-Age has precedence over Expires) and
        // 5.3 step 11 (an expired cookie that replaces an old one removes it).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        await CookiesSent(session, server, "/k/set", ("Set-Cookie", "z=1; Path=/k"), ("Set-Cookie", "y=1; Path=/k"));
        Assert.Equal(["z=1; y=1"], await CookiesSent(session, server, "/k/x"));
        await CookiesSent(session, server, "/k/clear",
            ("Set-Cookie", "z=1; Path=/k; Max-Age=0"),
            ("Set-Cookie", "y=1; Path=/k; Expires=Thu, 01 Jan 1970 00:00:00 GMT"),
            ("Set-Cookie", "p=1; Path=/k; Expires=Thu, 01 Jan 1970 00:00:00 GMT"),
            ("Set-Cookie", "q=1; Path=/k; Expires=Thu, 01 Jan 1970 00:00:00 GMT; Max-Age=600"));
        Assert.Equal(["q=1"], await CookiesSent(session, server, "/k/x"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task A_Secure_cookie_is_not_sent_over_http()
    {
        // RFC 6265 5.4 step 1: a cookie whose secure-only-flag is set goes only over a secure channel.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        await CookiesSent(session, server, "/sec/set", ("Set-Cookie", "s=1; Secure; Path=/sec"), ("Set-Cookie", "a=1; Path=/sec"));
        Assert.Equal(["a=1"], await CookiesSent(session, server, "/sec/x"));
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-28")]
    public async Task A_cookie_whose_Domain_the_host_does_not_match_is_ignored()
    {
        // RFC 6265 5.3 step 6: a Domain the request host does not domain-match drops the cookie; a Domain identical to
        // the host (5.1.3) is kept.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var session = new URLSession(URLSessionConfiguration.Ephemeral);
        await CookiesSent(session, server, "/dom/set",
            ("Set-Cookie", "d=1; Domain=example.com; Path=/dom"), ("Set-Cookie", "e=1; Domain=127.0.0.1; Path=/dom"));
        Assert.Equal(["e=1"], await CookiesSent(session, server, "/dom/x"));
    }

    /// <summary>
    /// One GET of <paramref name="pathAndQuery"/> on <paramref name="session"/>: the Cookie lines the server saw,
    /// after it answered 204 with <paramref name="headers"/>.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CookiesSent(
        URLSession session, LoopbackHttpServer server, string pathAndQuery, params (string Name, string Value)[] headers)
    {
        var pending = session.Data(server.Url(pathAndQuery));
        var exchange = await server.NextExchange();
        var cookies = exchange.HeaderValues("Cookie");
        await exchange.Respond(204, "No Content", [], headers);
        await pending.WaitAsync(Guard);
        return cookies;
    }
}
