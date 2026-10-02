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
// Not reachable here, and why:
//  - the retry's SUCCESS (the second probe signing in through the browser and answering): the catalog has no OAuth
//    upstream on a plain-http address and URLSession has no handler seam, so no stand-in answers as an https MCP
//    server. The retry is proven up to and including its second attempt's sign-in reaching the upstream.
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

    public MCPStoreOAuthProbeTests(MCPProxySingletonFixture fixture)
    {
        this.fixture = fixture;
        secrets = SecretStore.Shared.Override(fixture.Store);
        browser = Workspace.Open.Override(_ => true);
        executor = MainActor.Executor.Override(new DirectMainExecutor());
    }

    public void Dispose()
    {
        MCPOAuth.SignOut("gitlab");
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
}
