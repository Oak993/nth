using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Core.Tests.Fixtures.HttpReading;

namespace Marrow.Core.Tests.Port;

// Foundation unit F.HTTP, third part of the tests (R-TEST-3 suffix): the two tests that change process-wide state,
// the cookie store and the response cache of URLSession.Shared (ED-118 item 6). They run in the
// HttpProcessState collection, which xUnit never runs in parallel with anything (R-TEST-5). Their expectations are
// the ones they had in HttpSecondTests; the API, the timer contract and the oracle items are listed in HttpTests.cs.
[Collection(HttpProcessStateCollection.Name)]
public sealed class HttpProcessStateTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TEST-5")]
    [Trait("Oracle", "O-28")]
    public async Task A_cookie_set_by_a_server_goes_back_to_it_on_the_next_request_of_the_shared_session()
    {
        // R-NET-1: "cookie persistence [is] observable to servers". URLSession.shared stores Set-Cookie in
        // HTTPCookieStorage.shared and sends a matching cookie on later requests, from any call site.
        // UNVERIFIED O-28: the parked choice is the source's behaviour, the cookie comes back (ED-118: one
        // process-wide store for URLSession.Shared, in memory for the run). The cookie is scoped to a path no other
        // test uses, because cookies ignore the port and every test server is on 127.0.0.1.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var jar = "/jar-" + Guid.NewGuid().ToString("N");
        var setting = URLSession.Shared.Data(new URLRequest(server.Url(jar + "/set")));
        var first = await server.NextExchange();
        Assert.Empty(first.HeaderValues("Cookie"));
        await first.Respond(200, "OK", [], ("Set-Cookie", "a=1; Path=" + jar));
        await setting.WaitAsync(Guard);

        var next = URLSession.Shared.Data(new URLRequest(server.Url(jar + "/next")));
        var second = await server.NextExchange();
        Assert.Equal(["a=1"], second.HeaderValues("Cookie"));
        await second.Respond(204, "No Content", []);
        await next.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TEST-5")]
    [Trait("Oracle", "O-33")]
    public async Task A_cacheable_answer_to_a_GET_on_the_shared_session_is_served_again_from_the_cache()
    {
        // URLSession.shared uses URLCache.shared with .useProtocolCachePolicy, so a GET answered with a freshness
        // lifetime is answered from the cache while fresh, and the server sees no second request. PlanLimits.swift:247,
        // LinkTitles.swift:47 and MarkdownView.swift:949 all GET through it; UpdateChecker.swift:103-104 turns the
        // cache off for its own session, which shows the author relied on it being on elsewhere.
        // UNVERIFIED O-33: the parked choice is that behaviour (ED-118: a stored answer still fresh on SwiftClock is
        // returned without a request; memory only for the run).
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var url = server.Url("/api/oauth/usage");
        var first = URLSession.Shared.Data(new URLRequest(url, timeoutInterval: 15));
        var one = await server.NextExchange();
        await one.Respond(200, "OK", Ascii("{\"v\":1}"), ("Cache-Control", "max-age=600"), ("Content-Type", "application/json"));
        Assert.Equal(Ascii("{\"v\":1}"), (await first.WaitAsync(Guard)).Data);

        var again = URLSession.Shared.Data(new URLRequest(url, timeoutInterval: 15));
        var reachedServer = server.NextExchange();
        var winner = await Task.WhenAny(again, reachedServer).WaitAsync(Guard);
        Assert.Same(again, winner);
        var (data, response) = await again;
        Assert.Equal(Ascii("{\"v\":1}"), data);
        Assert.Equal(200, Assert.IsType<HTTPURLResponse>(response).StatusCode);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")]
    [Trait("Rule", "R-TEST-5")]
    [Trait("Decision", "ED-118")]
    [Trait("Oracle", "O-33")]
    public async Task A_cached_answer_on_the_shared_session_goes_stale_when_its_max_age_elapses_on_SwiftClock()
    {
        // ED-118 item 4: a stored answer is returned while fresh, its age measured with SwiftClock.Provider, never
        // the wall clock. max-age=600: 599 s later on the manual clock it is served from the cache; 600 s later it
        // is stale (RFC 9111 4.2: fresh while the lifetime is greater than the age) and the server is asked again.
        // The path is unique so no other test's entry in URLCache.shared can answer it.
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var arrivals = new Arrivals(server);
        var url = server.Url("/usage-" + Guid.NewGuid().ToString("N"));
        var first = URLSession.Shared.Data(url);
        await (await arrivals.Next()).Respond(200, "OK", Ascii("v1"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("v1"), (await first.WaitAsync(Guard)).Data);

        runtime.Advance(TimeSpan.FromSeconds(599));
        var fresh = URLSession.Shared.Data(url);
        Assert.False(await arrivals.Reached(fresh));
        Assert.Equal(Ascii("v1"), (await fresh).Data);

        runtime.Advance(TimeSpan.FromSeconds(1));
        var stale = URLSession.Shared.Data(url);
        Assert.True(await arrivals.Reached(stale));
        await (await arrivals.Next()).Respond(200, "OK", Ascii("v2"), ("Cache-Control", "max-age=600"));
        Assert.Equal(Ascii("v2"), (await stale.WaitAsync(Guard)).Data);
    }
}
