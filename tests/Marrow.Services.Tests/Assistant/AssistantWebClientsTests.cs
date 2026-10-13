using System.Net;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.1 "Isolation that is real", 16.2 AS-16 fallback; ED-NEW-dextra-as16-7): the Asturs' web reads while Visus is not here. Each assistant
// has its own client and no client keeps or sends a cookie, so nothing an Astur reads can sign it in, and Channel B's session is never Channel A's. A Cookie header a
// caller sets is dropped; a Set-Cookie header never reaches the caller. Against a server on the loopback.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-7")]
public sealed class AssistantWebClientsTests : IAsyncDisposable
{
    private readonly FakeUpstreamServer server = new();
    private readonly AssistantWebClients clients = new();

    public AssistantWebClientsTests() =>
        server.Handler = request => request.Path.StartsWith("/login", StringComparison.Ordinal)
            ? new FakeResponse(200, [("Set-Cookie", "session=abc123; Path=/; HttpOnly"), ("Content-Type", "text/plain")], "signed in"u8.ToArray())
            : new FakeResponse(200, [("Content-Type", "text/plain")], "page"u8.ToArray());

    public async ValueTask DisposeAsync()
    {
        clients.Dispose();
        await server.DisposeAsync();
    }

    private static IEnumerable<string> CookiesSent(FakeUpstreamServer server) =>
        server.Received.Select(r => r.Headers.TryGetValue("Cookie", out var cookie) ? cookie : "");

    [Fact]
    public async Task A_site_that_sets_a_cookie_gets_none_back_from_either_Astur()
    {
        foreach (var astur in new[] { "channel-a", "channel-b" })
        {
            var client = clients.For(astur);
            using var login = await client.GetAsync(server.BaseUrl + "/login");
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.False(login.Headers.Contains("Set-Cookie"));                                 // it never reaches the caller
            using var next = await client.GetAsync(server.BaseUrl + "/account");
            Assert.Equal("page", await next.Content.ReadAsStringAsync());
        }
        Assert.Equal(4, server.Received.Count);
        Assert.All(CookiesSent(server), cookie => Assert.Equal("", cookie));                    // the session was never carried
    }

    [Fact]
    public async Task A_cookie_a_caller_sets_by_hand_is_dropped_before_the_request_leaves()
    {
        var client = clients.For("channel-a");
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/account");
        request.Headers.TryAddWithoutValidation("Cookie", "session=stolen");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([""], CookiesSent(server));
    }

    [Fact]
    public void Each_assistant_has_its_own_client_made_once_and_never_another_assistants()
    {
        var a = clients.For("channel-a");
        var b = clients.For("channel-b");
        Assert.Same(a, clients.For("channel-a"));
        Assert.NotSame(a, b);
        clients.Forget("channel-a");
        Assert.NotSame(a, clients.For("channel-a"));                                            // a retired assistant's connections are closed; a new one starts clean
        Assert.Same(b, clients.For("channel-b"));
    }

    [Fact]
    public async Task A_client_let_go_cannot_be_used_and_the_set_refuses_unsafe_ids_and_use_after_dispose()
    {
        var gone = clients.For("channel-a");
        clients.Forget("channel-a");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => gone.GetAsync(server.BaseUrl + "/x"));
        Assert.Throws<ArgumentException>(() => clients.For("..\\x"));
        Assert.Throws<ArgumentException>(() => clients.For(""));
        clients.Dispose();
        Assert.Throws<ObjectDisposedException>(() => clients.For("channel-a"));
        clients.Dispose();                                                                       // a second dispose is quiet
    }

    [Fact]
    public void A_read_gives_up_after_thirty_seconds_and_follows_at_most_five_redirects()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), clients.For("channel-a").Timeout);
        Assert.Equal(5, AssistantWebClients.MaxRedirects);
    }
}
