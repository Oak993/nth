using System.Collections.Concurrent;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for the OAuth half of MCPStore.startProbe/probeOnce (DroppyCode/Services/MCP/MCPStore.swift:301-349,
// records PR-MCP-065 and PR-MCP-066) through the real MCPProxy.Shared listener MCPProxyTests drives.
//
// How a probe that signs in is reached with no network (R-TEST-9, R-TEST-10). The catalog's OAuth servers are all
// https, and GitLab's address is "https://{instance}/api/v4/mcp" with the instance a field the user types. The
// instance here is "127.0.0.1:<port>" of a CountingLoopbackListener, which counts every connection made to it and
// drops it at once, so the upstream is a loopback socket that never answers a TLS handshake and every connection
// to it is one observable attempt to reach the upstream. The probe itself goes to the proxy's own loopback
// listener (MCPProxy.url(for:), resolve pointed the url at it), which answers on its own with 401 whenever an
// OAuth route has no usable session (MCPProxy.swift:333-339, MCPOAuth.accessToken throwing signedOut): a session
// stored already expired and without a refresh token plays "the server refused the token" without a stub server.
// The proxy's accept loop runs with the secret store MCPProxySingletonFixture captured, so every test here binds
// that same store (the collection fixture) for SecretStore.Shared instead of an MCPStoreHarness store.
//
// An https upstream that answers (OD-0057). URLSession.Transport is the test seam on the HTTP client: FakeGitLabUpstream
// stands behind it as "https://gitlab.example.test/api/v4/mcp", answering in-process (no socket, no network). The
// handler is the one MCPProxySingletonFixture installed for the proxy's own flow (its forwarding runs there), and
// this class puts the same handler on its own flow, where the sign-in and the probe run, so one script answers both.
// That makes the retry's success reachable (PR-MCP-065) and PR-MCP-068's 403 and 404 answers from the upstream.
//
// Not reachable here, and why:
//  - the second catch clause (MCPProbeError.http 401/403 with an OAuth upstream, :322): MCPProbe.probeRemote turns a
//    401/403 from an OAuth server into unauthorizedOAuth or needsSetup before the store sees it, so nothing can
//    throw that pair there; only its guard (a plain server is not retried) is observable, below.
//  - the cancellation check before the sign-out (:318, :323): the first attempt's 401 and a cancellation cannot be
//    made to meet without a hook between the two statements.
[Collection(MCPProxyCollection.Name)]
public sealed class MCPStoreOAuthProbeTests : IDisposable
{
    private const string ExpiredSession = """{"access_token":"stale","expires_at":1}""";
    private const string ValidSession = """{"access_token":"fresh","expires_at":4102444800}""";
    private const string SignInDetailsPrefix = "The server's sign-in details didn't make sense."; // MCPOAuth.swift:24-25

    private readonly MCPProxySingletonFixture fixture;
    private readonly StorageScratchFolder folder = new();
    private readonly IDisposable secrets;
    private readonly IDisposable browser;
    private readonly IDisposable executor;
    private readonly IDisposable transport;
    private readonly ConcurrentQueue<string> opened = new();
    private Func<string, Task>? visitBrowser;

    public MCPStoreOAuthProbeTests(MCPProxySingletonFixture fixture)
    {
        this.fixture = fixture;
        secrets = SecretStore.Shared.Override(fixture.Store);
        // The browser: records the address it was sent to and, when a test sets visitBrowser, comes back to the
        // sign-in's callback the way the person would after approving.
        browser = Workspace.Open.Override(url =>
        {
            opened.Enqueue(url.AbsoluteString);
            if (visitBrowser is { } visit) _ = Task.Run(() => visit(url.AbsoluteString));
            return true;
        });
        executor = MainActor.Executor.Override(new DirectMainExecutor());
        transport = URLSession.Transport.Override(fixture.Https);
    }

    public void Dispose()
    {
        MCPOAuth.SignOut("gitlab");
        MCPProxy.Shared.Current.Unregister("gitlab");
        fixture.Https.Reset();
        transport.Dispose();
        executor.Dispose();
        browser.Dispose();
        secrets.Dispose();
        folder.Dispose();
    }

    private static async Task WaitWhileConnecting(MCPStore store, string id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (store.State(id) is MCPConnectionState.Connecting)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The probe did not finish.");
            await Task.Delay(20);
        }
    }

    /// Points GitLab at the listener and connects: the first probe finds no session, so it ends in the sign-in's
    /// failure against the listener (see the tests below for what that proves). The listener's count is reset.
    private static async Task<(MCPStore Store, MCPCatalogEntry Entry)> ConnectGitLab(CountingLoopbackListener upstream)
    {
        var store = new MCPStore();
        var entry = store.Entry("gitlab")!;
        store.SetDraft(upstream.HostAndPort, entry.Fields.Single(field => field.Key == "instance"), entry);
        store.Connect(entry);
        await WaitWhileConnecting(store, entry.Id);
        upstream.Reset();
        return (store, entry);
    }

    [Fact]
    [Trait("Req", "PR-MCP-066")] // MCPStore.swift:301-308 isSignedIn false -> signIn(upstream) runs first; :309 register only after it
    public async Task A_signed_out_probe_signs_in_against_the_upstream_address_before_it_registers_the_route()
    {
        await using var upstream = new CountingLoopbackListener();
        var store = new MCPStore();
        var entry = store.Entry("gitlab")!;
        store.SetDraft(upstream.HostAndPort, entry.Fields.Single(field => field.Key == "instance"), entry);
        Assert.False(MCPOAuth.IsSignedIn(entry.Id));

        store.Connect(entry);
        await WaitWhileConnecting(store, entry.Id);

        // The sign-in's discovery went to the entry's own upstream address (instance filled into the template).
        Assert.True(upstream.Accepted > 0, "the sign-in never reached the upstream address");
        // The sign-in failed before register ran: it throws out of probeOnce, an MCPOAuthError whose
        // errorDescription becomes the card's message (:345-347).
        var failed = Assert.IsType<MCPConnectionState.Failed>(store.State(entry.Id));
        Assert.StartsWith(SignInDetailsPrefix, failed.Message, StringComparison.Ordinal);
        // The order is sign in, THEN register: no route exists while the sign-in has not finished.
        var (status, body) = await MCPProxyRawClient.Get(MCPProxy.Port, "/mcp/gitlab");
        Assert.Equal(502, status);
        Assert.Contains("unknown server", body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-066")] // MCPStore.swift:302-310 signed in: no signIn, register(headers: [:], usesOAuth: true), then probe through the route
    public async Task A_signed_in_probe_skips_the_sign_in_registers_the_route_and_probes_through_the_proxy()
    {
        await using var upstream = new CountingLoopbackListener();
        var (store, entry) = await ConnectGitLab(upstream);
        Assert.True(MCPKeychain.Set(ValidSession, entry.Id, "oauth"));

        store.Refresh(entry);
        await WaitWhileConnecting(store, entry.Id);

        // The probe's request reached the proxy and came out of it as the proxy's own 502 (the upstream dropped the
        // connection): the message is the probe's rendering of status 502, not a sign-in error.
        var failed = Assert.IsType<MCPConnectionState.Failed>(store.State(entry.Id));
        Assert.Equal("The server answered 502.", failed.Message); // MCPProbe.swift:35-36 (default status text)
        // No sign-in ran (it would have made its own discovery requests before the proxy ever forwarded) and
        // nothing was signed out: exactly one connection reached the upstream, the proxy forwarding the probe.
        Assert.Equal(1, upstream.Accepted);
        Assert.True(MCPOAuth.IsSignedIn(entry.Id));
        // The route was registered with its upstream and usesOAuth: a request through it is forwarded upstream with
        // the stored session (the upstream sees one more connection) instead of "unknown server".
        var (status, body) = await MCPProxyRawClient.Get(MCPProxy.Port, "/mcp/gitlab");
        Assert.Equal(502, status);
        Assert.Contains("upstream request failed", body, StringComparison.Ordinal);
        Assert.Equal(2, upstream.Accepted);
    }

    [Fact]
    [Trait("Req", "PR-MCP-065")] // MCPStore.swift:317-321 unauthorizedOAuth: checkCancellation, signOut, unregister, probeOnce again, once
    public async Task A_refused_OAuth_probe_signs_out_unregisters_and_probes_a_second_time()
    {
        await using var upstream = new CountingLoopbackListener();
        var (store, entry) = await ConnectGitLab(upstream);
        // A session the proxy cannot use: it answers 401 on its own, which MCPProbe reads as unauthorizedOAuth.
        Assert.True(MCPKeychain.Set(ExpiredSession, entry.Id, "oauth"));
        Assert.True(MCPOAuth.IsSignedIn(entry.Id));

        store.Refresh(entry);
        await WaitWhileConnecting(store, entry.Id);

        // signOut: the refused session is gone, so the second attempt began with the sign-in.
        Assert.False(MCPOAuth.IsSignedIn(entry.Id));
        // The second probeOnce ran: its sign-in discovery is the only thing that can have reached the upstream (the
        // first attempt was answered by the proxy itself and never contacted it).
        Assert.True(upstream.Accepted > 0, "no second attempt reached the upstream");
        // Exactly one retry, and its failure propagates as the card's message: the sign-in's, not the 401's.
        var failed = Assert.IsType<MCPConnectionState.Failed>(store.State(entry.Id));
        Assert.StartsWith(SignInDetailsPrefix, failed.Message, StringComparison.Ordinal);
        Assert.NotEqual("Sign-in was rejected. Sign in again in your browser.", failed.Message);
        // unregister: the route the first attempt added is gone (the second attempt failed before registering anew).
        var (status, body) = await MCPProxyRawClient.Get(MCPProxy.Port, "/mcp/gitlab");
        Assert.Equal(502, status);
        Assert.Contains("unknown server", body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-065")] // MCPStore.swift:317, :322 both clauses guard on resolved.oauthUpstream != nil: a plain server answering 401 is not retried
    public async Task A_plain_http_server_answering_401_is_not_retried()
    {
        await using var server = new FakeUpstreamServer();
        server.Handler = _ => new FakeResponse(401, [("Content-Type", "text/plain")], "no"u8.ToArray());
        var store = new MCPStore();
        var custom = new MCPCustomServer(MCPCustomServer.MakeID("Plain Keyed"), "Plain Keyed", MCPCustomServer.Kind.Http)
        {
            Url = server.BaseUrl + "/mcp",
        };
        Assert.Null(store.SaveCustom(custom));
        var entry = store.Entry(custom.Id)!;

        store.Connect(entry);
        await WaitWhileConnecting(store, entry.Id);

        var failed = Assert.IsType<MCPConnectionState.Failed>(store.State(entry.Id));
        Assert.Equal("That key was refused. Press Try again and paste a new one.", failed.Message); // MCPProbe.swift:35
        // One initialize, no second probe: a retry would have posted again.
        Assert.Single(server.Received);
    }

    // ---- an https upstream that answers (OD-0057) ----

    private const string GoodSession = """{"access_token":"at-new","expires_at":4102444800}""";

    /// Points GitLab at the in-process https upstream and connects: nothing answers yet, so the first probe's sign-in is
    /// refused at its first request and the card fails; the connection (the address) is saved, as in ConnectGitLab.
    private async Task<(MCPStore Store, MCPCatalogEntry Entry)> ConnectGitLabOverHttps()
    {
        var store = new MCPStore();
        var entry = store.Entry("gitlab")!;
        store.SetDraft(FakeGitLabUpstream.Host, entry.Fields.Single(field => field.Key == "instance"), entry);
        store.Connect(entry);
        await WaitWhileConnecting(store, entry.Id);
        Assert.IsType<MCPConnectionState.Failed>(store.State(entry.Id));
        return (store, entry);
    }

    private IEnumerable<InProcessRequest> UpstreamCalls(string path) =>
        fixture.Https.Received.Where(request => request.Url.AbsolutePath == path);

    [Theory]
    [Trait("Req", "PR-MCP-068")] // MCPStore.swift:339-343 needsSetup: .needsSetup(guide), the OAuth session is NOT signed out; MCPProbe.swift:156-169 GitLab's 403 "MCP server disabled" and its 404
    [InlineData(403, "MCP server disabled")]
    [InlineData(404, "404 Not Found")]
    public async Task A_signed_in_probe_the_upstream_answers_as_switched_off_needs_setup_and_keeps_the_sign_in(int status, string answer)
    {
        var (store, entry) = await ConnectGitLabOverHttps();
        var savedBefore = File.ReadAllBytes(MCPPaths.ConnectionsURL);
        var savedAt = File.GetLastWriteTimeUtc(MCPPaths.ConnectionsURL);
        Assert.True(MCPKeychain.Set(GoodSession, entry.Id, "oauth"));
        new FakeGitLabUpstream { McpAnswer = _ => InProcessHttpHandler.Reply(status, answer, "text/plain") }.Install(fixture.Https);

        store.Refresh(entry);
        await WaitWhileConnecting(store, entry.Id);

        // The state is needsSetup with GitLab's guide, built from the upstream's own address (MCPProbe.swift:191-208).
        var needs = Assert.IsType<MCPConnectionState.NeedsSetup>(store.State(entry.Id));
        Assert.Equal("Turn on GitLab's MCP server", needs.Guide.Title);
        Assert.Equal(
            "You're signed in, but GitLab keeps its MCP server off until a group owner turns it on. Until then, the address answers not found.",
            needs.Guide.Reason);
        Assert.Equal("Open my groups", needs.Guide.PageLabel);
        Assert.Equal("https://gitlab.example.test/dashboard/groups", needs.Guide.PageURL);
        // The sign-in is kept (:341-342): the session is still the stored one, and no browser round trip ran.
        Assert.True(MCPOAuth.IsSignedIn(entry.Id));
        Assert.Equal("at-new", await MCPOAuth.AccessToken(entry.Id));
        Assert.Empty(opened);
        // The probe reached the upstream through the proxy with that session, once: needsSetup is not a rejection, so
        // no second attempt was made (a retry would have signed out first).
        var posted = Assert.Single(UpstreamCalls("/api/v4/mcp"), request => request.Method == "POST");
        Assert.Equal("Bearer at-new", posted.Header("Authorization"));
        Assert.Empty(UpstreamCalls("/register"));
        Assert.Empty(UpstreamCalls("/token"));
        // Nothing is saved (:339-343): the connections file is as Connect left it, not even rewritten with the same bytes.
        Assert.Equal(savedBefore, File.ReadAllBytes(MCPPaths.ConnectionsURL));
        Assert.Equal(savedAt, File.GetLastWriteTimeUtc(MCPPaths.ConnectionsURL));
        // Nothing is exported: exportAll would have replaced the proxy's routes with the connected servers' (none
        // yet, :40-49), dropping the route this probe registered. The route is still there and forwards upstream.
        await Task.Delay(300); // an export runs a moment after the state flips (the same pause as the other probe tests)
        var (routeStatus, routeBody) = await MCPProxyRawClient.Get(MCPProxy.Port, "/mcp/gitlab");
        Assert.DoesNotContain("unknown server", routeBody, StringComparison.Ordinal);
        Assert.Equal(401, routeStatus); // the upstream's own answer to a GET without a token
        Assert.Null(store.Connection(entry.Id)!.ConnectedAt);
    }

    [Fact]
    [Trait("Req", "PR-MCP-068")] // MCPStore.swift:341-342 "Keep the sign-in so Check again re-probes without another browser round trip"
    public async Task Pressing_Check_again_after_needs_setup_re_probes_with_the_kept_session_and_connects_without_the_browser()
    {
        var (store, entry) = await ConnectGitLabOverHttps();
        Assert.True(MCPKeychain.Set(GoodSession, entry.Id, "oauth"));
        var upstream = new FakeGitLabUpstream { McpAnswer = _ => InProcessHttpHandler.Reply(403, "MCP server disabled", "text/plain") };
        upstream.Install(fixture.Https);
        store.Refresh(entry);
        await WaitWhileConnecting(store, entry.Id);
        Assert.IsType<MCPConnectionState.NeedsSetup>(store.State(entry.Id));

        // The group owner turns the server on; the person presses Check again (MCPStore.refresh, :131-135).
        upstream.McpAnswer = FakeGitLabUpstream.Working;
        store.Refresh(entry);
        await WaitWhileConnecting(store, entry.Id);

        Assert.Equal(new MCPConnectionState.Connected(2), store.State(entry.Id)); // the two tools the server lists
        Assert.Empty(opened);
        Assert.Empty(UpstreamCalls("/register"));
        Assert.Empty(UpstreamCalls("/token"));
        Assert.Empty(UpstreamCalls("/prm"));
        Assert.All(UpstreamCalls("/api/v4/mcp").Where(request => request.Method == "POST"),
            request => Assert.Equal("Bearer at-new", request.Header("Authorization")));
    }

    [Fact]
    [Trait("Req", "PR-MCP-065")] // MCPStore.swift:317-321 unauthorizedOAuth: signOut, unregister, probeOnce again; its success is the result (:328-337)
    public async Task A_refused_OAuth_probe_signs_in_again_in_the_browser_and_the_second_probe_connects()
    {
        var (store, entry) = await ConnectGitLabOverHttps();
        new FakeGitLabUpstream().Install(fixture.Https);
        // The person approves in the browser: the callback comes back with a code and the state the sign-in sent.
        visitBrowser = async address =>
        {
            var query = OAuthHarness.QueryPairs(address);
            var port = CallbackClient.PortOf(OAuthHarness.Value(query, "redirect_uri"));
            await CallbackClient.Get(port, "/callback?code=code-1&state=" + OAuthHarness.Value(query, "state"));
        };
        // A session the proxy cannot use (expired, no refresh token): the proxy answers 401 on its own, which the
        // probe reads as unauthorizedOAuth.
        Assert.True(MCPKeychain.Set(ExpiredSession, entry.Id, "oauth"));

        store.Refresh(entry);
        await WaitWhileConnecting(store, entry.Id);

        // The second attempt succeeded: connected, with the tools the server listed, stamped as connected.
        Assert.Equal(new MCPConnectionState.Connected(2), store.State(entry.Id));
        var connection = store.Connection(entry.Id)!;
        Assert.NotNull(connection.ConnectedAt);
        Assert.Equal(["get_issue", "create_issue"], connection.Tools.Select(tool => tool.Name));
        // signOut ran and the second probeOnce signed in again, once, in the browser, against the upstream's own
        // authorization endpoint; the new session replaced the refused one.
        var visited = Assert.Single(opened);
        Assert.StartsWith(FakeGitLabUpstream.Base + "/authorize?", visited, StringComparison.Ordinal);
        Assert.Single(UpstreamCalls("/register"));
        Assert.Single(UpstreamCalls("/token"));
        Assert.True(MCPOAuth.IsSignedIn(entry.Id));
        Assert.Equal(FakeGitLabUpstream.Token, await MCPOAuth.AccessToken(entry.Id));
        // The probe went through the route the second attempt registered, with the new token: the three calls of a
        // probe, and the refused session never reached the upstream.
        var mcpCalls = UpstreamCalls("/api/v4/mcp").Where(request => request.Method == "POST").ToList();
        Assert.Equal(3, mcpCalls.Count);
        Assert.All(mcpCalls, request => Assert.Equal("Bearer " + FakeGitLabUpstream.Token, request.Header("Authorization")));
        Assert.DoesNotContain(fixture.Https.Received, request => request.Header("Authorization") == "Bearer stale");
    }

    [Fact]
    [Trait("Req", "PR-MCP-069")] // MCPStore.swift:344-347 any other probe error becomes .failed(message: error.localizedDescription); OD-0056: a URLError's is the Windows error's own message
    public async Task A_probe_of_an_https_server_that_cannot_be_reached_fails_with_the_Windows_error_s_message()
    {
        // The name does not resolve: the transport raises an HttpRequestException carrying Winsock's WSAHOST_NOT_FOUND.
        var windows = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
        fixture.Https.Answer = _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, windows.Message, windows);
        var store = new MCPStore();
        var custom = new MCPCustomServer(MCPCustomServer.MakeID("Unreachable"), "Unreachable", MCPCustomServer.Kind.Http)
        {
            Url = "https://plain.example.test/mcp",
        };
        Assert.Null(store.SaveCustom(custom));
        var entry = store.Entry(custom.Id)!;

        store.Connect(entry);
        await WaitWhileConnecting(store, entry.Id);

        var failed = Assert.IsType<MCPConnectionState.Failed>(store.State(entry.Id));
        Assert.Equal(windows.Message, failed.Message);
        Assert.NotEqual("A server with the specified hostname could not be found.", failed.Message); // Foundation's wording
        Assert.Single(fixture.Https.Received); // one initialize, no retry
    }
}
