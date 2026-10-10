using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPOAuth.cs, the port of DroppyCode/Services/MCP/MCPOAuth.swift (B2 1fbf81e),
// part 1: PR-MCP-139 to PR-MCP-162, part 2: PR-MCP-163 and PR-MCP-164. Every expectation is transcribed from that file and those records, never from the
// C# implementation.
//
// The whole OAuth flow runs for real against a scripted loopback HTTP server (LoopbackHttpServer), which stands for the
// MCP address, its protected-resource metadata, the authorization server and its token and registration endpoints.
// The test plays the browser: the default-browser seam records the authorization address (MCPOAuth.swift:434), and
// CallbackClient connects to the loopback callback listener the address names. Secrets go to an in-memory Credential
// Manager backend (OD-0008 B3), under the account strings "MCP <id> oauth" and "MCP <id> oauth-client"
// (MCPKeychain.swift:7-9). The clock is the TestRuntime's, so the 300-second callback timeout and the 30-second request
// limits are driven by the test, never waited for.
//
// Stored JSON is compared byte for byte. JSONSerialization with [.sortedKeys] (MCPOAuth.swift:394, :709) sorts the
// keys and escapes "/" as "\/" (no .withoutEscapingSlashes). expires_at is a Double; the test clock starts on a whole
// second, so the value is integral and JSONSerialization writes it without a fraction.
//
// Where a value is percent-encoded by URLComponents (the authorization address and the form bodies), the tests compare
// the decoded names and values in order, which does not rest on the open encoding question O-57.
//
// Not tested automatically, and why:
// - network("Couldn't listen for the sign-in reply.") (:415, :426) and network("The connection to the browser broke.")
//   (:440): a loopback listener on an OS-assigned port cannot be made to fail construction or fail after it started
//   from a test without replacing the socket layer.
// - callbackFailed("Sign-in didn't finish.") after the success page (:555-558): the redirect URI and client are stored
//   before the browser is opened (:433-434), so a callback cannot arrive before them.
// - fetchJSON's and post's "The server didn't answer properly." (:730, :758, :782): URLSession over http always
//   answers with an HTTPURLResponse.
// - The authorization address being nil (:147-149): it needs an endpoint URLComponents cannot parse, which rests on
//   the open item O-58/O-60 behaviour of URLComponents.
public sealed class MCPOAuthTests
{
    private const string ServerID = "linear";
    private const string ServerName = "Linear & Co";
    private const string SessionAccount = "MCP linear oauth";
    private const string ClientAccount = "MCP linear oauth-client";
    private const string Registered = """{"client_id":"cid-1","client_secret":"sec-1"}""";
    private const string TokenAnswer = """{"access_token":"at-1","refresh_token":"rt-1","expires_in":3600,"token_type":"Bearer"}""";

    private static TimeSpan Guard => OAuthHarness.Guard;

    private static string Base(LoopbackHttpServer server) => "http://127.0.0.1:" + server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Slashed(string text) => text.Replace("/", "\\/", StringComparison.Ordinal);

    /// <summary>PR-MCP-159 b3_note oracle, transcribed independently from the record rather than from formEncode
    /// (MCPOAuth.swift:790-793): percent-encodes every UTF-8 byte not in "A-Za-z0-9-._~" as %XX with uppercase hex
    /// digits (ED-201), so ":", "/", "+", "=", "&" and a space are all escaped and nothing is turned into "+".</summary>
    private static string FormEncoded(string value)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var unreserved = (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z')
                || (b >= (byte)'0' && b <= (byte)'9') || b == (byte)'-' || b == (byte)'.' || b == (byte)'_' || b == (byte)'~';
            if (unreserved) sb.Append((char)b);
            else sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static Task StartSignIn(LoopbackHttpServer server, string path = "/mcp") =>
        Task.Run(() => MCPOAuth.SignIn(ServerID, ServerName, Base(server) + path));

    private static string ResourceMetadata(string b) =>
        $$"""{"resource":"{{b}}/mcp","authorization_servers":["{{b}}/auth"],"scopes_supported":["mcp:tools"]}""";

    private static string AuthMetadata(string b) =>
        $$"""{"issuer":"{{b}}/auth","authorization_endpoint":"{{b}}/authorize?prompt=consent","token_endpoint":"{{b}}/token","registration_endpoint":"{{b}}/register"}""";

    private static async Task<LoopbackExchange> Next(LoopbackHttpServer server, Task flow) => await OAuthHarness.Next(server, flow);

    private static Task Json(LoopbackExchange exchange, int status, string json) => OAuthHarness.RespondJson(exchange, status, json);

    private static Task Challenge(LoopbackExchange exchange, string header) =>
        exchange.Respond(401, "Unauthorized", [], ("WWW-Authenticate", header));

    // The usual discovery: the 401 challenge naming the resource metadata, that metadata, then the authorization
    // server metadata at the first candidate for the issuer "<base>/auth" (MCPOAuth.swift:207-216, :317-321).
    private static async Task Discover(LoopbackHttpServer server, Task flow, string? resourceMetadata = null, string? authMetadata = null)
    {
        var b = Base(server);
        var probe = await Next(server, flow);
        Assert.Equal(("GET", "/mcp"), (probe.Method, probe.Target));
        await Challenge(probe, $"Bearer resource_metadata=\"{b}/prm\"");
        var metadata = await Next(server, flow);
        Assert.Equal(("GET", "/prm"), (metadata.Method, metadata.Target));
        await Json(metadata, 200, resourceMetadata ?? ResourceMetadata(b));
        var auth = await Next(server, flow);
        Assert.Equal(("GET", "/.well-known/oauth-authorization-server/auth"), (auth.Method, auth.Target));
        await Json(auth, 200, authMetadata ?? AuthMetadata(b));
    }

    private static async Task<LoopbackExchange> Register(LoopbackHttpServer server, Task flow, string answer = Registered)
    {
        var register = await Next(server, flow);
        Assert.Equal(("POST", "/register"), (register.Method, register.Target));
        await Json(register, 201, answer);
        return register;
    }

    private static int CallbackPort(string authorizationURL) =>
        CallbackClient.PortOf(OAuthHarness.Value(OAuthHarness.QueryPairs(authorizationURL), "redirect_uri"));

    private static string StateOf(string authorizationURL) => OAuthHarness.Value(OAuthHarness.QueryPairs(authorizationURL), "state");

    // The browser returns with a code and the right state; the token endpoint answers; the flow finishes.
    private static async Task<LoopbackExchange> Finish(LoopbackHttpServer server, Task flow, string authorizationURL, string tokenAnswer = TokenAnswer)
    {
        var answer = await CallbackClient.Get(CallbackPort(authorizationURL), "/callback?code=code-1&state=" + StateOf(authorizationURL));
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", CallbackClient.Split(answer).Head, StringComparison.Ordinal);
        var token = await Next(server, flow);
        Assert.Equal(("POST", "/token"), (token.Method, token.Target));
        await Json(token, 200, tokenAnswer);
        await flow.WaitAsync(Guard);
        return token;
    }

    private static async Task<MCPOAuthError> Fails(Task flow) =>
        await Assert.ThrowsAsync<MCPOAuthError>(() => flow.WaitAsync(Guard));

    private static List<(string Name, string Value)> Form(LoopbackExchange exchange) =>
        OAuthHarness.Pairs(Encoding.UTF8.GetString(exchange.Body));

    private static string Page(byte[] answer) => Encoding.UTF8.GetString(CallbackClient.Split(answer).Body);

    // ---- PR-MCP-160: the eleven error texts ----

    [Fact]
    [Trait("Req", "PR-MCP-160")] // Services/MCP/MCPOAuth.swift:20-45
    public void ErrorDescription_puts_the_base_sentence_before_the_detail_for_four_cases_and_alone_when_the_detail_is_empty()
    {
        Assert.Equal("Couldn't find the sign-in page.", new MCPOAuthError(new MCPOAuthError.Case.DiscoveryFailed("")).ErrorDescription);
        Assert.Equal("Couldn't find the sign-in page. Why", new MCPOAuthError(new MCPOAuthError.Case.DiscoveryFailed("Why")).ErrorDescription);
        Assert.Equal("The server's sign-in details didn't make sense.", new MCPOAuthError(new MCPOAuthError.Case.MetadataFailed("")).ErrorDescription);
        Assert.Equal("The server's sign-in details didn't make sense. Why", new MCPOAuthError(new MCPOAuthError.Case.MetadataFailed("Why")).ErrorDescription);
        Assert.Equal("Sign-in setup failed.", new MCPOAuthError(new MCPOAuthError.Case.RegistrationFailed("")).ErrorDescription);
        Assert.Equal("Sign-in setup failed. Why", new MCPOAuthError(new MCPOAuthError.Case.RegistrationFailed("Why")).ErrorDescription);
        Assert.Equal("Couldn't finish signing in.", new MCPOAuthError(new MCPOAuthError.Case.TokenFailed("")).ErrorDescription);
        Assert.Equal("Couldn't finish signing in. Why", new MCPOAuthError(new MCPOAuthError.Case.TokenFailed("Why")).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-160")] // Services/MCP/MCPOAuth.swift:32-43
    public void ErrorDescription_of_callbackFailed_and_network_is_the_bare_detail_or_a_base_sentence_when_the_detail_is_empty()
    {
        Assert.Equal("Sign-in didn't finish.", new MCPOAuthError(new MCPOAuthError.Case.CallbackFailed("")).ErrorDescription);
        Assert.Equal("Why", new MCPOAuthError(new MCPOAuthError.Case.CallbackFailed("Why")).ErrorDescription);
        Assert.Equal("The network didn't answer.", new MCPOAuthError(new MCPOAuthError.Case.Network("")).ErrorDescription);
        Assert.Equal("Why", new MCPOAuthError(new MCPOAuthError.Case.Network("Why")).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-160")] // Services/MCP/MCPOAuth.swift:26-41
    public void ErrorDescription_of_the_five_cases_without_a_detail_is_fixed_and_is_the_error_message()
    {
        Assert.Equal("This server doesn't let apps sign in on their own.", new MCPOAuthError(new MCPOAuthError.Case.RegistrationUnsupported()).ErrorDescription);
        Assert.Equal("Sign-in didn't finish. Try again.", new MCPOAuthError(new MCPOAuthError.Case.StateMismatch()).ErrorDescription);
        Assert.Equal("Sign-in timed out. Try again and complete it in the browser.", new MCPOAuthError(new MCPOAuthError.Case.TimedOut()).ErrorDescription);
        Assert.Equal("Sign-in was cancelled.", new MCPOAuthError(new MCPOAuthError.Case.Cancelled()).ErrorDescription);
        var signedOut = new MCPOAuthError(new MCPOAuthError.Case.SignedOut());
        Assert.Equal("You're signed out. Sign in again to continue.", signedOut.ErrorDescription);
        Assert.Equal("You're signed out. Sign in again to continue.", SwiftErrors.LocalizedDescription(signedOut));
    }

    [Fact]
    [Trait("Req", "PR-MCP-160")] // Services/MCP/MCPOAuth.swift:7-45
    [Trait("Rule", "R-ENUM-3")]
    public void ErrorDescription_is_non_empty_for_every_one_of_the_eleven_cases()
    {
        MCPOAuthError.Case[] samples =
        [
            new MCPOAuthError.Case.DiscoveryFailed(""), new MCPOAuthError.Case.MetadataFailed(""),
            new MCPOAuthError.Case.RegistrationUnsupported(), new MCPOAuthError.Case.RegistrationFailed(""),
            new MCPOAuthError.Case.TokenFailed(""), new MCPOAuthError.Case.CallbackFailed(""),
            new MCPOAuthError.Case.StateMismatch(), new MCPOAuthError.Case.TimedOut(), new MCPOAuthError.Case.Cancelled(),
            new MCPOAuthError.Case.SignedOut(), new MCPOAuthError.Case.Network(""),
        ];
        foreach (var errorCase in UnionCoverage.AssertCovers(samples))
            Assert.NotEqual("", new MCPOAuthError(errorCase).ErrorDescription);
    }

    // ---- PR-MCP-162: the success and failure pages ----

    [Fact]
    [Trait("Req", "PR-MCP-162")] // Services/MCP/MCPOAuth.swift:841-847
    public void SuccessPage_titles_and_heads_the_page_with_the_escaped_server_name_and_sends_the_user_back_to_Marrow()
    {
        var page = MCPOAuth.SuccessPage("A & B <\"c\"> 'd'");
        const string name = "A &amp; B &lt;&quot;c&quot;&gt; &#39;d&#39;";
        Assert.Contains("\n<title>Signed in to " + name + "</title>\n", page, StringComparison.Ordinal);
        Assert.Contains("\n<h1>Signed in to " + name + "</h1>\n", page, StringComparison.Ordinal);
        // DEVIATION OD-0012 F1: upstream says "head back to Droppy Code." (:845).
        Assert.Contains("\n<p>You can close this tab and head back to Nth.</p>\n", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Droppy", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-162")] // Services/MCP/MCPOAuth.swift:849-855
    public void FailurePage_titles_the_page_with_an_em_dash_and_the_escaped_name_and_shows_the_escaped_message_under_a_fixed_heading()
    {
        var page = MCPOAuth.FailurePage("A & B", "Code <expired> & \"gone\"");
        Assert.Contains("\n<title>Sign-in didn't finish \U00002014 A &amp; B</title>\n", page, StringComparison.Ordinal); // U+2014 EM DASH
        Assert.Contains("\n<h1>Sign-in didn't finish</h1>\n", page, StringComparison.Ordinal);
        Assert.Contains("\n<p>Code &lt;expired&gt; &amp; &quot;gone&quot;</p>\n", page, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 0xE2, 0x80, 0x94 }, Encoding.UTF8.GetBytes("\U00002014")); // U+2014 EM DASH
    }

    // ---- PR-MCP-157: isSignedIn is a presence check ----

    [Fact]
    [Trait("Req", "PR-MCP-157")] // Services/MCP/MCPOAuth.swift:185-187
    public void IsSignedIn_is_true_for_any_stored_session_even_one_that_is_not_JSON_or_is_expired()
    {
        using var h = new OAuthHarness();
        h.Seed(SessionAccount, "not json at all");
        Assert.True(MCPOAuth.IsSignedIn(ServerID));
        h.Seed("MCP other oauth", """{"access_token":"","expires_at":1}""");
        Assert.True(MCPOAuth.IsSignedIn("other"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-157")] // Services/MCP/MCPOAuth.swift:185-187
    public void IsSignedIn_is_false_without_a_stored_session_even_when_a_client_is_registered()
    {
        using var h = new OAuthHarness();
        h.Seed(ClientAccount, """{"client_id":"cid-1","issuer":"x"}""");
        Assert.False(MCPOAuth.IsSignedIn(ServerID));
    }

    // ---- PR-MCP-158: signOut deletes both items ----

    [Fact]
    [Trait("Req", "PR-MCP-158")] // Services/MCP/MCPOAuth.swift:189-192
    public void SignOut_deletes_the_session_and_the_registered_client_and_sends_no_request()
    {
        using var h = new OAuthHarness();
        h.Seed(SessionAccount, """{"access_token":"at-1"}""");
        h.Seed(ClientAccount, """{"client_id":"cid-1"}""");
        h.Seed("MCP other oauth", """{"access_token":"keep"}""");
        Assert.True(MCPOAuth.SignOut(ServerID));
        Assert.Null(h.Stored(SessionAccount));
        Assert.Null(h.Stored(ClientAccount));
        Assert.Equal("""{"access_token":"keep"}""", h.Stored("MCP other oauth"));
        Assert.False(MCPOAuth.IsSignedIn(ServerID));
    }

    [Fact]
    [Trait("Req", "PR-MCP-158")] // Services/MCP/MCPOAuth.swift:189-192
    public void SignOut_with_nothing_stored_is_a_success()
    {
        using var h = new OAuthHarness();
        Assert.True(MCPOAuth.SignOut(ServerID));
    }

    [Fact]
    [Trait("Req", "PR-MCP-158")] // Services/MCP/MCPOAuth.swift:189-192
    public void SignOut_attempts_both_deletions_and_reports_a_refused_one()
    {
        // DEVIATION OD-0008 A12: upstream discards the deletion results; the port reports a refused deletion so the
        // disconnect path can say so. The second deletion still runs after the first is refused.
        using var h = new OAuthHarness();
        h.Seed(SessionAccount, """{"access_token":"at-1"}""");
        h.Seed(ClientAccount, """{"client_id":"cid-1"}""");
        h.Backend.RefusedDeleteTargets.Add(SecretStore.TargetName(OAuthHarness.Service, SessionAccount));
        Assert.False(MCPOAuth.SignOut(ServerID));
        Assert.NotNull(h.Stored(SessionAccount));
        Assert.Null(h.Stored(ClientAccount));
    }

    // ---- PR-MCP-155: accessToken ----

    [Theory]
    [Trait("Req", "PR-MCP-155")] // Services/MCP/MCPOAuth.swift:164-171
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"refresh_token":"rt-1"}""")]
    [InlineData("""{"access_token":""}""")]
    [InlineData("""{"access_token":7}""")]
    public async Task AccessToken_throws_signedOut_without_a_stored_non_empty_access_token(string? stored)
    {
        using var h = new OAuthHarness();
        if (stored is not null) h.Seed(SessionAccount, stored);
        var error = await Assert.ThrowsAsync<MCPOAuthError>(() => MCPOAuth.AccessToken(ServerID));
        Assert.IsType<MCPOAuthError.Case.SignedOut>(error.ErrorCase);
        Assert.Equal("You're signed out. Sign in again to continue.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-155")] // Services/MCP/MCPOAuth.swift:172-176
    public async Task AccessToken_without_expires_at_returns_the_stored_token_as_never_expiring()
    {
        using var h = new OAuthHarness();
        h.Seed(SessionAccount, """{"access_token":"at-1","refresh_token":"rt-1","token_endpoint":"http://127.0.0.1:1/token","client_id":"cid-1"}""");
        Assert.Equal("at-1", await MCPOAuth.AccessToken(ServerID));
        h.Runtime.Advance(TimeSpan.FromDays(3650));
        Assert.Equal("at-1", await MCPOAuth.AccessToken(ServerID));
    }

    [Fact]
    [Trait("Req", "PR-MCP-155")] // Services/MCP/MCPOAuth.swift:172-176
    public async Task AccessToken_returns_the_stored_token_while_more_than_90_seconds_remain()
    {
        using var h = new OAuthHarness();
        var expiresAt = h.NowUnixSeconds + 91;
        h.Seed(SessionAccount, $$"""{"access_token":"at-1","expires_at":{{expiresAt}}}""");
        Assert.Equal("at-1", await MCPOAuth.AccessToken(ServerID));
    }

    [Fact]
    [Trait("Req", "PR-MCP-155")] // Services/MCP/MCPOAuth.swift:172-179
    public async Task AccessToken_needs_a_refresh_at_exactly_90_seconds_before_expiry_and_without_a_refresh_token_is_signed_out()
    {
        using var h = new OAuthHarness();
        var expiresAt = h.NowUnixSeconds + 90;
        h.Seed(SessionAccount, $$"""{"access_token":"at-1","expires_at":{{expiresAt}}}""");
        var error = await Assert.ThrowsAsync<MCPOAuthError>(() => MCPOAuth.AccessToken(ServerID));
        Assert.IsType<MCPOAuthError.Case.SignedOut>(error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-155")] // Services/MCP/MCPOAuth.swift:177-179
    public async Task AccessToken_past_expiry_with_an_empty_refresh_token_is_signed_out()
    {
        using var h = new OAuthHarness();
        var expiresAt = h.NowUnixSeconds - 10;
        h.Seed(SessionAccount, $$"""{"access_token":"at-1","expires_at":{{expiresAt}},"refresh_token":""}""");
        var error = await Assert.ThrowsAsync<MCPOAuthError>(() => MCPOAuth.AccessToken(ServerID));
        Assert.IsType<MCPOAuthError.Case.SignedOut>(error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-155")] // Services/MCP/MCPOAuth.swift:194-196
    public async Task RefreshIfNeeded_swallows_a_signed_out_failure()
    {
        using var h = new OAuthHarness();
        await MCPOAuth.RefreshIfNeeded(ServerID);
    }

    // ---- PR-MCP-153 and PR-MCP-154: the refresh and the stored session ----

    [Theory]
    [Trait("Req", "PR-MCP-153")] // Services/MCP/MCPOAuth.swift:643-649
    [InlineData("""{"access_token":"old","expires_at":EXP,"refresh_token":"rt-1","client_id":"cid-1"}""")]
    [InlineData("""{"access_token":"old","expires_at":EXP,"refresh_token":"rt-1","client_id":"cid-1","token_endpoint":""}""")]
    [InlineData("""{"access_token":"old","expires_at":EXP,"refresh_token":"rt-1","token_endpoint":"http://127.0.0.1:1/token"}""")]
    [InlineData("""{"access_token":"old","expires_at":EXP,"refresh_token":"rt-1","token_endpoint":"http://127.0.0.1:1/token","client_id":""}""")]
    public async Task A_due_refresh_without_a_token_endpoint_or_client_id_is_signed_out_before_any_request(string stored)
    {
        using var h = new OAuthHarness();
        h.Seed(SessionAccount, stored.Replace("EXP", (h.NowUnixSeconds - 10).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var error = await Assert.ThrowsAsync<MCPOAuthError>(() => MCPOAuth.AccessToken(ServerID).WaitAsync(Guard));
        Assert.IsType<MCPOAuthError.Case.SignedOut>(error.ErrorCase);
    }

    private static string DueSession(OAuthHarness h, LoopbackHttpServer server, string extra = ""","client_secret":"sec-1","resource":"res-1" """) =>
        $$"""{"access_token":"old","client_id":"cid-1","expires_at":{{h.NowUnixSeconds - 10}},"refresh_token":"rt-1","token_endpoint":"{{Base(server)}}/token"{{extra}}}""";

    [Fact]
    [Trait("Req", "PR-MCP-153")] // Services/MCP/MCPOAuth.swift:650-661
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:768-776
    public async Task A_refresh_posts_grant_refresh_token_client_id_secret_and_resource_in_order_as_a_form()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        h.Seed(SessionAccount, DueSession(h, server));
        var flow = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        var post = await Next(server, flow);
        Assert.Equal(("POST", "/token"), (post.Method, post.Target));
        Assert.Equal(["application/x-www-form-urlencoded"], post.HeaderValues("Content-Type"));
        Assert.Equal(["application/json"], post.HeaderValues("Accept"));
        Assert.Equal(
            [("grant_type", "refresh_token"), ("refresh_token", "rt-1"), ("client_id", "cid-1"), ("client_secret", "sec-1"), ("resource", "res-1")],
            Form(post));
        await Json(post, 200, """{"access_token":"new","expires_in":60}""");
        Assert.Equal("new", await flow.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-MCP-153")] // Services/MCP/MCPOAuth.swift:650-660
    public async Task A_refresh_leaves_out_an_empty_client_secret_and_resource()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        h.Seed(SessionAccount, DueSession(h, server, ""","client_secret":"","resource":"" """));
        var flow = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        var post = await Next(server, flow);
        Assert.Equal([("grant_type", "refresh_token"), ("refresh_token", "rt-1"), ("client_id", "cid-1")], Form(post));
        await Json(post, 200, """{"access_token":"new"}""");
        Assert.Equal("new", await flow.WaitAsync(Guard));
        // No expires_in: no expires_at; the empty secret and resource are not stored (:697-708).
        Assert.Equal(
            $$"""{"access_token":"new","client_id":"cid-1","refresh_token":"rt-1","token_endpoint":"{{Slashed(Base(server))}}\/token"}""",
            h.Stored(SessionAccount));
    }

    [Fact]
    [Trait("Req", "PR-MCP-153")] // Services/MCP/MCPOAuth.swift:669-679
    [Trait("Req", "PR-MCP-154")] // Services/MCP/MCPOAuth.swift:682-713
    public async Task A_refresh_keeps_the_old_refresh_token_when_none_is_returned_and_rewrites_the_session_sorted()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        h.Seed(SessionAccount, DueSession(h, server));
        var flow = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        var post = await Next(server, flow);
        await Json(post, 200, """{"access_token":"new","expires_in":60}""");
        Assert.Equal("new", await flow.WaitAsync(Guard));
        var expiresAt = h.NowUnixSeconds + 60;
        Assert.Equal(
            $$"""{"access_token":"new","client_id":"cid-1","client_secret":"sec-1","expires_at":{{expiresAt}},"refresh_token":"rt-1","resource":"res-1","token_endpoint":"{{Slashed(Base(server))}}\/token"}""",
            h.Stored(SessionAccount));
    }

    [Fact]
    [Trait("Req", "PR-MCP-153")] // Services/MCP/MCPOAuth.swift:672
    [Trait("Req", "PR-MCP-154")] // Services/MCP/MCPOAuth.swift:697-699
    public async Task A_refresh_stores_a_new_refresh_token_and_drops_an_empty_one()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        h.Seed(SessionAccount, DueSession(h, server));
        var flow = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        await Json(await Next(server, flow), 200, """{"access_token":"new","refresh_token":"rt-2","expires_in":3600}""");
        Assert.Equal("new", await flow.WaitAsync(Guard));
        Assert.Contains("\"refresh_token\":\"rt-2\"", h.Stored(SessionAccount), StringComparison.Ordinal);

        h.Seed("MCP second oauth", DueSession(h, server));
        var second = Task.Run(() => MCPOAuth.AccessToken("second"));
        await Json(await Next(server, second), 200, """{"access_token":"newer","refresh_token":""}""");
        Assert.Equal("newer", await second.WaitAsync(Guard));
        Assert.DoesNotContain("refresh_token", h.Stored("MCP second oauth"), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PR-MCP-153")] // Services/MCP/MCPOAuth.swift:661-668
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:795-801
    [InlineData(400, """{"error":"invalid_grant","error_description":"Refresh token revoked"}""", "Couldn't finish signing in. Refresh token revoked")]
    [InlineData(400, """{"error":"invalid_grant","error_description":""}""", "Couldn't finish signing in. invalid_grant")]
    [InlineData(401, "", "Couldn't finish signing in. The sign-in expired.")]
    [InlineData(200, """{"token_type":"Bearer"}""", "Couldn't finish signing in. The sign-in expired.")]
    [InlineData(200, """{"access_token":""}""", "Couldn't finish signing in. The sign-in expired.")]
    [InlineData(400, """{"error":""}""", "Couldn't finish signing in.")]
    public async Task A_refused_refresh_throws_tokenFailed_with_the_server_reason_or_the_sign_in_expired(int status, string body, string expected)
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var stored = DueSession(h, server);
        h.Seed(SessionAccount, stored);
        var flow = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        await Json(await Next(server, flow), status, body);
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.TokenFailed>(error.ErrorCase);
        Assert.Equal(expected, error.ErrorDescription);
        Assert.Equal(stored, h.Stored(SessionAccount));
    }

    // ---- PR-MCP-156: one refresh in flight per server ----

    [Fact]
    [Trait("Req", "PR-MCP-156")] // Services/MCP/MCPOAuth.swift:62-74
    public async Task Concurrent_refreshes_for_one_server_share_one_token_request()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        h.Seed(SessionAccount, DueSession(h, server));
        var first = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        var post = await Next(server, first);
        // The second caller reads the session and reaches the gate while the first request is still unanswered.
        var reads = h.ReadsOf(SessionAccount);
        var second = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        h.Runtime.Loop.RunUntil(() => h.ReadsOf(SessionAccount) > reads || second.IsCompleted, Guard);
        await Json(post, 200, """{"access_token":"shared","expires_in":3600}""");
        // A second request would never be answered, so both finishing proves they shared the one request.
        Assert.Equal("shared", await first.WaitAsync(Guard));
        Assert.Equal("shared", await second.WaitAsync(Guard));
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-MCP-156")] // Services/MCP/MCPOAuth.swift:62-74
    public async Task A_failed_shared_refresh_reaches_every_waiter_and_a_later_caller_starts_a_new_request()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        h.Seed(SessionAccount, DueSession(h, server));
        var first = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        var post = await Next(server, first);
        var reads = h.ReadsOf(SessionAccount);
        var second = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        h.Runtime.Loop.RunUntil(() => h.ReadsOf(SessionAccount) > reads || second.IsCompleted, Guard);
        await Json(post, 400, """{"error":"invalid_grant"}""");
        Assert.Equal("Couldn't finish signing in. invalid_grant", (await Fails(first)).ErrorDescription);
        Assert.Equal("Couldn't finish signing in. invalid_grant", (await Fails(second)).ErrorDescription);

        var third = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        var again = await Next(server, third);
        Assert.Equal(("POST", "/token"), (again.Method, again.Target));
        await Json(again, 200, """{"access_token":"later"}""");
        Assert.Equal("later", await third.WaitAsync(Guard));
    }

    [Fact]
    [Trait("Req", "PR-MCP-156")] // Services/MCP/MCPOAuth.swift:62-74
    public async Task Refreshes_for_different_servers_never_share_a_request()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        h.Seed("MCP one oauth", DueSession(h, server));
        h.Seed("MCP two oauth", DueSession(h, server));
        var one = Task.Run(() => MCPOAuth.AccessToken("one"));
        var two = Task.Run(() => MCPOAuth.AccessToken("two"));
        var both = Task.WhenAll(one, two);
        var a = await Next(server, both);
        var b = await Next(server, both);
        await Json(a, 200, """{"access_token":"token-a"}""");
        await Json(b, 200, """{"access_token":"token-b"}""");
        var results = new[] { await one.WaitAsync(Guard), await two.WaitAsync(Guard) };
        Assert.Equal(["token-a", "token-b"], results.Order(StringComparer.Ordinal));
    }

    // ---- PR-MCP-139: the whole sign-in ----

    [Fact]
    [Trait("Req", "PR-MCP-139")] // Services/MCP/MCPOAuth.swift:134-160
    [Trait("Req", "PR-MCP-154")] // Services/MCP/MCPOAuth.swift:682-713
    public async Task SignIn_discovers_registers_opens_the_browser_takes_the_callback_then_exchanges_the_code_and_stores_the_session()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        Assert.StartsWith(b + "/authorize?", url, StringComparison.Ordinal);
        Assert.False(MCPOAuth.IsSignedIn(ServerID));
        await Finish(server, flow, url);

        Assert.Single(h.Opened);
        var expiresAt = h.NowUnixSeconds + 3600;
        Assert.Equal(
            $$"""{"access_token":"at-1","client_id":"cid-1","client_secret":"sec-1","expires_at":{{expiresAt}},"refresh_token":"rt-1","resource":"{{Slashed(b)}}\/mcp","token_endpoint":"{{Slashed(b)}}\/token"}""",
            h.Stored(SessionAccount));
        Assert.True(MCPOAuth.IsSignedIn(ServerID));
        Assert.Equal("at-1", await MCPOAuth.AccessToken(ServerID));
    }

    [Fact]
    [Trait("Req", "PR-MCP-154")] // Services/MCP/MCPOAuth.swift:682-713
    public async Task A_token_answer_without_expires_in_or_refresh_token_stores_neither_key()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow, """{"client_id":"cid-1"}""");
        await Finish(server, flow, h.AwaitBrowser(flow), """{"access_token":"at-1"}""");
        Assert.Equal(
            $$"""{"access_token":"at-1","client_id":"cid-1","resource":"{{Slashed(b)}}\/mcp","token_endpoint":"{{Slashed(b)}}\/token"}""",
            h.Stored(SessionAccount));
    }

    [Fact]
    [Trait("Req", "PR-MCP-139")] // Services/MCP/MCPOAuth.swift:418-458
    [Trait("Req", "PR-MCP-145")] // Services/MCP/MCPOAuth.swift:441-442
    public async Task Cancelling_the_sign_in_closes_the_listener_and_throws_cancelled()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var handle = TaskHandle.DetachedThrowing(async () =>
        {
            await MCPOAuth.SignIn(ServerID, ServerName, Base(server) + "/mcp");
            return true;
        });
        Task flow = handle.Value;
        await Discover(server, flow);
        await Register(server, flow);
        var port = CallbackPort(h.AwaitBrowser(flow));
        handle.Cancel();
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.Cancelled>(error.ErrorCase);
        Assert.Equal("Sign-in was cancelled.", error.ErrorDescription);
        await Assert.ThrowsAnyAsync<SocketException>(() => CallbackClient.Connect(port));
        Assert.Null(h.Stored(SessionAccount));
    }

    // ---- PR-MCP-140 and PR-MCP-141: discovery and the challenge parameters ----

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:200-203
    public async Task SignIn_of_an_address_that_is_not_a_URL_throws_discoveryFailed()
    {
        using var h = new OAuthHarness();
        var error = await Assert.ThrowsAsync<MCPOAuthError>(() => MCPOAuth.SignIn(ServerID, ServerName, "").WaitAsync(Guard));
        Assert.IsType<MCPOAuthError.Case.DiscoveryFailed>(error.ErrorCase);
        Assert.Equal("Couldn't find the sign-in page. That address doesn't look like a URL.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:207-216
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:250-254
    public async Task Discovery_probes_the_address_with_Accept_json_and_follows_the_challenge_to_the_first_authorization_server()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        var probe = await Next(server, flow);
        Assert.Equal(("GET", "/mcp"), (probe.Method, probe.Target));
        Assert.Equal(["application/json"], probe.HeaderValues("Accept"));
        await Challenge(probe, $"Bearer realm=\"mcp\", resource_metadata=\"{b}/prm\"");
        var metadata = await Next(server, flow);
        Assert.Equal(("GET", "/prm"), (metadata.Method, metadata.Target));
        Assert.Equal(["application/json"], metadata.HeaderValues("Accept"));
        await Json(metadata, 200, $$"""{"resource":"{{b}}/mcp","authorization_servers":["{{b}}/first","{{b}}/second"]}""");
        var auth = await Next(server, flow);
        Assert.Equal(("GET", "/.well-known/oauth-authorization-server/first"), (auth.Method, auth.Target));
        Assert.Equal(["application/json"], auth.HeaderValues("Accept"));
        await Json(auth, 404, "{}");
        Assert.Equal("/.well-known/openid-configuration/first", (await Next(server, flow)).Target);
    }

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:217-237
    public async Task Without_a_401_challenge_discovery_tries_the_path_candidate_then_the_bare_candidate_and_uses_a_resource_only_document()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server, "/v1/mcp");
        var probe = await Next(server, flow);
        await Json(probe, 200, "{}");
        var first = await Next(server, flow);
        Assert.Equal(("GET", "/.well-known/oauth-protected-resource/v1/mcp"), (first.Method, first.Target));
        await Json(first, 404, "{}");
        var second = await Next(server, flow);
        Assert.Equal(("GET", "/.well-known/oauth-protected-resource"), (second.Method, second.Target));
        await Json(second, 200, $$"""{"resource":"{{b}}/v1/mcp"}""");
        // No authorization server listed: the issuer is the address's origin, which has no path (:242-243, :317).
        var auth = await Next(server, flow);
        Assert.Equal("/.well-known/oauth-authorization-server", auth.Target);
        await Json(auth, 200, AuthMetadata(b));
        var register = await Register(server, flow);
        await Finish(server, flow, h.AwaitBrowser(flow));
        Assert.Contains($"\"resource\":\"{Slashed(b)}\\/v1\\/mcp\"", h.Stored(SessionAccount), StringComparison.Ordinal);
        Assert.Contains($"\"issuer\":\"{Slashed(b)}\"", h.Stored(ClientAccount), StringComparison.Ordinal);
        Assert.False(JsonDocument.Parse(register.Body).RootElement.TryGetProperty("scope", out _));
    }

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:224-236
    public async Task A_candidate_with_neither_authorization_servers_nor_resource_is_skipped_scopes_and_all()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Json(await Next(server, flow), 404, "{}");
        var first = await Next(server, flow);
        Assert.Equal("/.well-known/oauth-protected-resource/mcp", first.Target);
        await Json(first, 200, """{"scopes_supported":["skipped"]}""");
        var second = await Next(server, flow);
        Assert.Equal("/.well-known/oauth-protected-resource", second.Target);
        await Json(second, 200, $$"""{"authorization_servers":["{{b}}/auth"]}""");
        var auth = await Next(server, flow);
        Assert.Equal("/.well-known/oauth-authorization-server/auth", auth.Target);
        await Json(auth, 200, AuthMetadata(b));
        var register = await Register(server, flow);
        Assert.False(JsonDocument.Parse(register.Body).RootElement.TryGetProperty("scope", out _));
        var url = h.AwaitBrowser(flow);
        var names = OAuthHarness.QueryPairs(url).Select(pair => pair.Name).ToList();
        Assert.DoesNotContain("scope", names);
        Assert.DoesNotContain("resource", names);
    }

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:208-216
    public async Task A_resource_metadata_address_that_fails_does_not_fall_back_to_the_candidates_and_the_origin_is_the_issuer()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm\"");
        await Json(await Next(server, flow), 404, "{}");
        var auth = await Next(server, flow);
        Assert.Equal("/.well-known/oauth-authorization-server", auth.Target);
        await Json(auth, 404, "{}");
        var openid = await Next(server, flow);
        Assert.Equal("/.well-known/openid-configuration", openid.Target);
        await Json(openid, 404, "{}");
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.MetadataFailed>(error.ErrorCase);
        // :348 keeps the caught error's whole errorDescription as lastError and :351 wraps it in metadataFailed again, so
        // fetchJSON's own "The server's sign-in details didn't make sense." prefix (:739) appears twice.
        Assert.Equal("The server's sign-in details didn't make sense. The server's sign-in details didn't make sense. The server answered 404.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:242-246
    public async Task An_authorization_server_that_trims_to_nothing_throws_discoveryFailed()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm\"");
        await Json(await Next(server, flow), 200, """{"authorization_servers":["//"]}""");
        var error = await Fails(flow);
        Assert.Equal("Couldn't find the sign-in page. That address doesn't look like a URL.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:212-215
    [Trait("Req", "PR-MCP-141")] // Services/MCP/MCPOAuth.swift:305-309
    public async Task Challenge_scopes_are_used_when_the_metadata_lists_no_non_empty_scope()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm\", scope=\"  files:read  files:write \"");
        await Json(await Next(server, flow), 200, $$"""{"resource":"{{b}}/mcp","authorization_servers":["{{b}}/auth"],"scopes_supported":["",""]}""");
        await Json(await Next(server, flow), 200, AuthMetadata(b));
        var register = await Register(server, flow);
        Assert.Equal("files:read files:write", JsonDocument.Parse(register.Body).RootElement.GetProperty("scope").GetString());
        Assert.Equal("files:read files:write", OAuthHarness.Value(OAuthHarness.QueryPairs(h.AwaitBrowser(flow)), "scope"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:212-215
    public async Task Metadata_scopes_win_over_challenge_scopes()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer scope=\"challenge\", resource_metadata=\"{b}/prm\"");
        await Json(await Next(server, flow), 200, $$"""{"authorization_servers":["{{b}}/auth"],"scopes_supported":["a","",7,"b"]}""");
        await Json(await Next(server, flow), 200, AuthMetadata(b));
        var register = await Register(server, flow);
        Assert.Equal("a b", JsonDocument.Parse(register.Body).RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    [Trait("Req", "PR-MCP-141")] // Services/MCP/MCPOAuth.swift:291-303
    public async Task A_bare_resource_metadata_value_runs_up_to_the_next_comma_after_leading_spaces()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=  {b}/bare,scope=x");
        Assert.Equal("/bare", (await Next(server, flow)).Target);
    }

    [Fact]
    [Trait("Req", "PR-MCP-141")] // Services/MCP/MCPOAuth.swift:291-303
    public async Task A_bare_resource_metadata_value_stops_at_a_space()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata={b}/spaced realm=x");
        Assert.Equal("/spaced", (await Next(server, flow)).Target);
    }

    [Fact]
    [Trait("Req", "PR-MCP-141")] // Services/MCP/MCPOAuth.swift:292
    public async Task The_first_occurrence_of_the_parameter_name_wins_even_inside_a_longer_name()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer old_resource_metadata=\"{b}/wrong\", resource_metadata=\"{b}/right\"");
        Assert.Equal("/wrong", (await Next(server, flow)).Target);
    }

    [Fact]
    [Trait("Req", "PR-MCP-141")] // Services/MCP/MCPOAuth.swift:295-298
    public async Task An_unterminated_quoted_value_is_no_value_and_discovery_falls_back_to_the_candidates()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm");
        Assert.Equal("/.well-known/oauth-protected-resource/mcp", (await Next(server, flow)).Target);
    }

    [Fact]
    [Trait("Req", "PR-MCP-141")] // Services/MCP/MCPOAuth.swift:302-309
    public async Task An_empty_value_is_no_value_and_an_empty_scope_gives_no_scopes()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), "Bearer resource_metadata=\"\", scope=\"   \"");
        var first = await Next(server, flow);
        Assert.Equal("/.well-known/oauth-protected-resource/mcp", first.Target);
        await Json(first, 200, $$"""{"authorization_servers":["{{b}}/auth"]}""");
        await Json(await Next(server, flow), 200, AuthMetadata(b));
        var register = await Register(server, flow);
        Assert.False(JsonDocument.Parse(register.Body).RootElement.TryGetProperty("scope", out _));
    }

    [Fact]
    [Trait("Req", "PR-MCP-140")] // Services/MCP/MCPOAuth.swift:259-280
    [Trait("Req", "PR-MCP-141")] // Services/MCP/MCPOAuth.swift:285-287
    public async Task Parameters_are_gathered_across_every_WWW_Authenticate_header()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        var probe = await Next(server, flow);
        await probe.Respond(401, "Unauthorized", [], ("WWW-Authenticate", "Bearer scope=\"one two\""),
            ("www-authenticate", $"Bearer resource_metadata=\"{b}/prm\""));
        var metadata = await Next(server, flow);
        Assert.Equal("/prm", metadata.Target);
        await Json(metadata, 200, $$"""{"authorization_servers":["{{b}}/auth"]}""");
        await Json(await Next(server, flow), 200, AuthMetadata(b));
        var register = await Register(server, flow);
        Assert.Equal("one two", JsonDocument.Parse(register.Body).RootElement.GetProperty("scope").GetString());
    }

    // ---- PR-MCP-142: authorization server metadata ----

    [Fact]
    [Trait("Req", "PR-MCP-142")] // Services/MCP/MCPOAuth.swift:311-352
    public async Task An_issuer_with_a_path_is_sought_at_four_candidates_in_order_and_only_the_last_failure_is_reported()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm\"");
        await Json(await Next(server, flow), 200, $$"""{"authorization_servers":["{{b}}/tenant/auth/"]}""");
        var targets = new List<string>();
        var answers = new (int Status, string Body)[]
        {
            (404, "{}"),
            (200, """{"authorization_endpoint":"x"}"""),
            (500, "{}"),
            (200, """{"authorization_endpoint":"","token_endpoint":"y"}"""),
        };
        foreach (var (status, body) in answers)
        {
            var exchange = await Next(server, flow);
            targets.Add(exchange.Target);
            await Json(exchange, status, body);
        }
        Assert.Equal(
            [
                "/.well-known/oauth-authorization-server/tenant/auth",
                "/.well-known/openid-configuration/tenant/auth",
                "/tenant/auth/.well-known/oauth-authorization-server",
                "/tenant/auth/.well-known/openid-configuration",
            ],
            targets);
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.MetadataFailed>(error.ErrorCase);
        Assert.Equal("The server's sign-in details didn't make sense. It's missing the addresses needed to sign in.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-142")] // Services/MCP/MCPOAuth.swift:323-351
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:738-740
    public async Task An_issuer_without_a_path_has_two_candidates_and_the_last_failure_is_the_detail()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm\"");
        await Json(await Next(server, flow), 200, $$"""{"authorization_servers":["{{b}}/"]}""");
        var first = await Next(server, flow);
        Assert.Equal("/.well-known/oauth-authorization-server", first.Target);
        await Json(first, 200, """{"token_endpoint":"y"}""");
        var second = await Next(server, flow);
        Assert.Equal("/.well-known/openid-configuration", second.Target);
        await second.Respond(200, "OK", Encoding.UTF8.GetBytes("<html>not json</html>"), ("Content-Type", "text/html"));
        var error = await Fails(flow);
        // :348 then :351: the caught metadataFailed's whole description becomes the detail, so the prefix appears twice.
        Assert.Equal("The server's sign-in details didn't make sense. The server's sign-in details didn't make sense. The server answered 200.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-142")] // Services/MCP/MCPOAuth.swift:328-346
    public async Task The_first_candidate_with_both_endpoints_wins_and_its_registration_endpoint_is_used()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm\"");
        await Json(await Next(server, flow), 200, $$"""{"authorization_servers":["{{b}}/auth"]}""");
        await Json(await Next(server, flow), 404, "{}");
        await Json(await Next(server, flow), 404, "{}");
        var third = await Next(server, flow);
        Assert.Equal("/auth/.well-known/oauth-authorization-server", third.Target);
        await Json(third, 200, $$"""{"authorization_endpoint":"{{b}}/authorize","token_endpoint":"{{b}}/token","registration_endpoint":"{{b}}/other-register"}""");
        var register = await Next(server, flow);
        Assert.Equal(("POST", "/other-register"), (register.Method, register.Target));
    }

    // ---- PR-MCP-143: reusing a registered client ----

    [Fact]
    [Trait("Req", "PR-MCP-143")] // Services/MCP/MCPOAuth.swift:356-364
    public async Task A_stored_client_for_the_same_issuer_is_reused_with_its_secret_and_nothing_is_registered()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var stored = $$"""{"client_id":"stored-1","client_secret":"stored-secret","issuer":"{{b}}/auth"}""";
        h.Seed(ClientAccount, stored);
        var flow = StartSignIn(server);
        await Discover(server, flow);
        var url = h.AwaitBrowser(flow);
        Assert.Equal("stored-1", OAuthHarness.Value(OAuthHarness.QueryPairs(url), "client_id"));
        var token = await Finish(server, flow, url);
        var form = Form(token);
        Assert.Equal("stored-1", OAuthHarness.Value(form, "client_id"));
        Assert.Equal("stored-secret", OAuthHarness.Value(form, "client_secret"));
        Assert.Equal(stored, h.Stored(ClientAccount));
    }

    [Theory]
    [Trait("Req", "PR-MCP-143")] // Services/MCP/MCPOAuth.swift:356-367
    [InlineData("""{"client_id":"stored-1","issuer":"http://127.0.0.1:1/auth"}""")]
    [InlineData("""{"client_id":"stored-1","issuer":"BASE/auth/"}""")]
    [InlineData("""{"client_id":"stored-1"}""")]
    [InlineData("""{"client_id":"","issuer":"BASE/auth"}""")]
    [InlineData("""not json""")]
    public async Task A_stored_client_that_does_not_match_the_issuer_exactly_is_replaced_by_a_new_registration(string stored)
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        h.Seed(ClientAccount, stored.Replace("BASE", b, StringComparison.Ordinal));
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        Assert.Equal("cid-1", OAuthHarness.Value(OAuthHarness.QueryPairs(h.AwaitBrowser(flow)), "client_id"));
        Assert.Equal($$"""{"client_id":"cid-1","client_secret":"sec-1","issuer":"{{Slashed(b)}}\/auth"}""", h.Stored(ClientAccount));
    }

    // ---- PR-MCP-144: dynamic client registration ----

    [Fact]
    [Trait("Req", "PR-MCP-144")] // Services/MCP/MCPOAuth.swift:368-378
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:744-754
    public async Task Registration_posts_exactly_the_six_members_as_JSON_and_stores_the_client_with_sorted_keys()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow);
        var register = await Register(server, flow);
        Assert.Equal(["application/json"], register.HeaderValues("Content-Type"));
        Assert.Equal(["application/json"], register.HeaderValues("Accept"));
        var url = h.AwaitBrowser(flow);
        var redirect = OAuthHarness.Value(OAuthHarness.QueryPairs(url), "redirect_uri");
        var body = JsonDocument.Parse(register.Body).RootElement;
        Assert.Equal(
            ["client_name", "grant_types", "redirect_uris", "response_types", "scope", "token_endpoint_auth_method"],
            body.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        // DEVIATION OD-0012 F1: upstream registers as "Droppy Code" (:369).
        Assert.Equal("Nth", body.GetProperty("client_name").GetString());
        Assert.Equal([redirect], body.GetProperty("redirect_uris").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["authorization_code", "refresh_token"], body.GetProperty("grant_types").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["code"], body.GetProperty("response_types").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("none", body.GetProperty("token_endpoint_auth_method").GetString());
        Assert.Equal("mcp:tools", body.GetProperty("scope").GetString());
        // JSONSerialization escapes "/" by default (:746 passes no options).
        Assert.Contains("\"redirect_uris\":[\"" + Slashed(redirect) + "\"]", Encoding.UTF8.GetString(register.Body), StringComparison.Ordinal);
        Assert.Equal($$"""{"client_id":"cid-1","client_secret":"sec-1","issuer":"{{Slashed(b)}}\/auth"}""", h.Stored(ClientAccount));
    }

    [Fact]
    [Trait("Req", "PR-MCP-144")] // Services/MCP/MCPOAuth.swift:389-399
    [Trait("Req", "PR-MCP-152")] // Services/MCP/MCPOAuth.swift:616-618
    public async Task A_client_registered_with_an_empty_secret_is_stored_and_used_without_one()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow, """{"client_id":"cid-1","client_secret":""}""");
        var token = await Finish(server, flow, h.AwaitBrowser(flow));
        Assert.Equal($$"""{"client_id":"cid-1","issuer":"{{Slashed(b)}}\/auth"}""", h.Stored(ClientAccount));
        Assert.DoesNotContain("client_secret", Form(token).Select(pair => pair.Name));
        Assert.DoesNotContain("client_secret", h.Stored(SessionAccount), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PR-MCP-144")] // Services/MCP/MCPOAuth.swift:365-367
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_registration_endpoint_registration_is_unsupported_and_no_browser_opens(bool emptyEndpoint)
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var endpoint = emptyEndpoint ? ""","registration_endpoint":"" """ : "";
        var flow = StartSignIn(server);
        await Discover(server, flow, authMetadata: $$"""{"authorization_endpoint":"{{b}}/authorize","token_endpoint":"{{b}}/token"{{endpoint}}}""");
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.RegistrationUnsupported>(error.ErrorCase);
        Assert.Equal("This server doesn't let apps sign in on their own.", error.ErrorDescription);
        Assert.Empty(h.Opened);
    }

    [Theory]
    [Trait("Req", "PR-MCP-144")] // Services/MCP/MCPOAuth.swift:378-388
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:795-801
    [InlineData(403, "", "Sign-in setup failed. The server only signs in apps it has approved (403), so this one can't yet.")]
    [InlineData(403, """{"error":"access_denied"}""", "Sign-in setup failed. access_denied")]
    [InlineData(400, """{"error":"invalid_redirect_uri","error_description":"Loopback is not allowed"}""", "Sign-in setup failed. Loopback is not allowed")]
    [InlineData(500, """{"message":"boom"}""", "Sign-in setup failed. The server refused to set things up (500).")]
    [InlineData(201, """{"client_secret":"x"}""", "Sign-in setup failed. The server refused to set things up (201).")]
    [InlineData(201, """{"client_id":""}""", "Sign-in setup failed. The server refused to set things up (201).")]
    public async Task A_refused_registration_throws_registrationFailed_and_stores_nothing(int status, string body, string expected)
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        var register = await Next(server, flow);
        await Json(register, status, body);
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.RegistrationFailed>(error.ErrorCase);
        Assert.Equal(expected, error.ErrorDescription);
        Assert.Null(h.Stored(ClientAccount));
        Assert.Empty(h.Opened);
    }

    [Fact]
    [Trait("Oracle", "O-38")] // Services/MCP/MCPOAuth.swift:745-749: URL(string:) is nil for a non-digit port
    [Trait("Rule", "R-PATH-1")]
    public async Task A_registration_endpoint_that_is_not_a_URL_throws_the_address_is_not_valid()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow, authMetadata: $$"""{"authorization_endpoint":"{{b}}/authorize","token_endpoint":"{{b}}/token","registration_endpoint":"http://127.0.0.1:port/register"}""");
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.Network>(error.ErrorCase);
        Assert.Equal("That address isn't valid.", error.ErrorDescription);
    }

    // ---- PR-MCP-145 and PR-MCP-146: the callback listener and its timeout ----

    [Fact]
    [Trait("Req", "PR-MCP-145")] // Services/MCP/MCPOAuth.swift:409-438
    public async Task The_callback_listens_on_a_loopback_port_redirects_to_its_callback_path_and_closes_when_done()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        var register = await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var redirect = OAuthHarness.Value(OAuthHarness.QueryPairs(url), "redirect_uri");
        Assert.Matches("^http://127\\.0\\.0\\.1:[0-9]+/callback$", redirect);
        Assert.Equal(redirect, JsonDocument.Parse(register.Body).RootElement.GetProperty("redirect_uris")[0].GetString());
        var port = CallbackClient.PortOf(redirect);
        Assert.NotEqual(server.Port, port);
        var token = await Finish(server, flow, url);
        Assert.Equal(redirect, OAuthHarness.Value(Form(token), "redirect_uri"));
        Assert.Single(h.Opened);
        await Assert.ThrowsAnyAsync<SocketException>(() => CallbackClient.Connect(port));
    }

    [Fact]
    [Trait("Req", "PR-MCP-146")] // Services/MCP/MCPOAuth.swift:452-454
    public async Task A_sign_in_not_completed_in_the_browser_times_out_at_300_seconds()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var port = CallbackPort(h.AwaitBrowser(flow));
        h.Runtime.Advance(TimeSpan.FromSeconds(299));
        Assert.False(flow.IsCompleted);
        h.Runtime.Advance(TimeSpan.FromSeconds(1));
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.TimedOut>(error.ErrorCase);
        Assert.Equal("Sign-in timed out. Try again and complete it in the browser.", error.ErrorDescription);
        await Assert.ThrowsAnyAsync<SocketException>(() => CallbackClient.Connect(port));
    }

    [Fact]
    [Trait("Req", "PR-MCP-146")] // Services/MCP/MCPOAuth.swift:112-121
    public async Task The_timeout_still_fires_after_a_finished_sign_in_and_changes_nothing()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        await Finish(server, flow, h.AwaitBrowser(flow));
        var stored = h.Stored(SessionAccount);
        h.Runtime.Advance(TimeSpan.FromSeconds(300));
        Assert.True(flow.IsCompletedSuccessfully);
        Assert.Equal(stored, h.Stored(SessionAccount));
    }

    // ---- PR-MCP-147 and PR-MCP-148: reading and routing the callback request ----

    [Fact]
    [Trait("Req", "PR-MCP-147")] // Services/MCP/MCPOAuth.swift:479-483
    public async Task A_request_that_ends_before_its_head_is_closed_without_a_reply_and_the_sign_in_keeps_waiting()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        using (var client = await CallbackClient.Connect(CallbackPort(url)))
        {
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes("GET /callback?code=x&state=" + StateOf(url) + " HTTP/1.1\r\n"));
            client.Client.Shutdown(SocketShutdown.Send);
            Assert.Empty(await CallbackClient.ReadAnswer(stream));
        }
        Assert.False(flow.IsCompleted);
        await Finish(server, flow, url);
    }

    [Fact]
    [Trait("Req", "PR-MCP-147")] // Services/MCP/MCPOAuth.swift:479-483
    public async Task A_head_growing_past_one_MiB_without_its_end_is_closed_without_a_reply()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var oversized = new byte[1024 * 1024 + 1];
        Array.Fill(oversized, (byte)'a');
        Assert.Empty(await CallbackClient.Exchange(CallbackPort(url), oversized));
        Assert.False(flow.IsCompleted);
        await Finish(server, flow, url);
    }

    [Fact]
    [Trait("Req", "PR-MCP-147")] // Services/MCP/MCPOAuth.swift:470-474
    public async Task A_connection_still_reading_when_the_sign_in_finishes_is_closed_without_a_reply()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        using var early = await CallbackClient.Connect(CallbackPort(url));
        var stream = early.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET /callback?code=late"));
        await Finish(server, flow, url);
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("&state=" + StateOf(url) + " HTTP/1.1\r\n\r\n"));
        }
        catch (Exception error) when (error is IOException or SocketException)
        {
            // Already closed.
        }
        Assert.Empty(await CallbackClient.ReadAnswer(stream));
        Assert.Contains("\"access_token\":\"at-1\"", h.Stored(SessionAccount), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-148")] // Services/MCP/MCPOAuth.swift:506-525
    [Trait("Req", "PR-MCP-150")] // Services/MCP/MCPOAuth.swift:564-570
    public async Task Other_methods_get_405_other_paths_get_404_and_neither_ends_the_sign_in()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var port = CallbackPort(url);
        const string methodBody = "<html><body>Method not allowed.</body></html>";
        const string pathBody = "<html><body>Not found.</body></html>";
        var methodAnswer = "HTTP/1.1 405 Method Not Allowed\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: 45\r\nConnection: close\r\n\r\n" + methodBody;
        var pathAnswer = "HTTP/1.1 404 Not Found\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: 36\r\nConnection: close\r\n\r\n" + pathBody;
        Assert.Equal(45, Encoding.UTF8.GetByteCount(methodBody));
        Assert.Equal(36, Encoding.UTF8.GetByteCount(pathBody));
        var state = StateOf(url);

        Assert.Equal(methodAnswer, Encoding.UTF8.GetString(await CallbackClient.Exchange(port, Encoding.ASCII.GetBytes($"POST /callback?code=c&state={state} HTTP/1.1\r\n\r\n"))));
        Assert.Equal(methodAnswer, Encoding.UTF8.GetString(await CallbackClient.Exchange(port, Encoding.ASCII.GetBytes($"get /callback?code=c&state={state} HTTP/1.1\r\n\r\n"))));
        Assert.Equal(methodAnswer, Encoding.UTF8.GetString(await CallbackClient.Exchange(port, "GET\r\n\r\n"u8.ToArray())));
        Assert.Equal(pathAnswer, Encoding.UTF8.GetString(await CallbackClient.Get(port, "/favicon.ico")));
        Assert.Equal(pathAnswer, Encoding.UTF8.GetString(await CallbackClient.Get(port, $"/callbacks?code=c&state={state}")));
        Assert.Equal(pathAnswer, Encoding.UTF8.GetString(await CallbackClient.Get(port, $"/Callback?code=c&state={state}")));

        Assert.False(flow.IsCompleted);
        await Finish(server, flow, url);
    }

    // ---- PR-MCP-149 and PR-MCP-150: the callback outcome ----

    [Theory]
    [Trait("Req", "PR-MCP-149")] // Services/MCP/MCPOAuth.swift:530-551
    [InlineData("error=access_denied&error_description=User%20said%20no&code=c&state=STATE", "User said no", "User said no")]
    [InlineData("error=access_denied&state=STATE", "The sign-in was refused (access_denied).", "The sign-in was refused (access_denied).")]
    [InlineData("error=access_denied&error_description=&code=c&state=STATE", "The sign-in was refused (access_denied).", "The sign-in was refused (access_denied).")]
    [InlineData("error=&state=STATE", "The browser didn&#39;t send back a code.", "The browser didn't send back a code.")]
    [InlineData("state=STATE", "The browser didn&#39;t send back a code.", "The browser didn't send back a code.")]
    [InlineData("code=&state=wrong", "The browser didn&#39;t send back a code.", "The browser didn't send back a code.")]
    [InlineData("code=c&state=wrong", "Sign-in didn&#39;t finish. Try again.", "Sign-in didn't finish. Try again.")]
    [InlineData("code=c", "Sign-in didn&#39;t finish. Try again.", "Sign-in didn't finish. Try again.")]
    public async Task The_callback_checks_the_provider_error_then_the_code_then_the_state_and_shows_the_reason_on_a_200_page(string query, string pageMessage, string errorText)
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var answer = await CallbackClient.Get(CallbackPort(url), "/callback?" + query.Replace("STATE", StateOf(url), StringComparison.Ordinal));
        var (head, _) = CallbackClient.Split(answer);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", head, StringComparison.Ordinal);
        var page = Page(answer);
        Assert.Contains("\n<title>Sign-in didn't finish \U00002014 Linear &amp; Co</title>\n", page, StringComparison.Ordinal); // U+2014 EM DASH
        Assert.Contains("\n<p>" + pageMessage + "</p>\n", page, StringComparison.Ordinal);
        var error = await Fails(flow);
        Assert.Equal(errorText, error.ErrorDescription);
        if (errorText == "Sign-in didn't finish. Try again.") Assert.IsType<MCPOAuthError.Case.StateMismatch>(error.ErrorCase);
        else Assert.IsType<MCPOAuthError.Case.CallbackFailed>(error.ErrorCase);
        Assert.Null(h.Stored(SessionAccount));
    }

    [Fact]
    [Trait("Req", "PR-MCP-149")] // Services/MCP/MCPOAuth.swift:553-560
    [Trait("Req", "PR-MCP-150")] // Services/MCP/MCPOAuth.swift:564-570
    public async Task The_success_page_is_sent_with_a_four_header_head_counting_UTF8_bytes_before_the_code_is_exchanged()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var answer = await CallbackClient.Get(CallbackPort(url), "/callback?code=code-1&state=" + StateOf(url));
        var (head, body) = CallbackClient.Split(answer);
        Assert.Equal(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "
            + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n",
            head);
        Assert.Contains("\n<title>Signed in to Linear &amp; Co</title>\n", Encoding.UTF8.GetString(body), StringComparison.Ordinal);
        // The page reached the browser before the token request was made.
        var token = await Next(server, flow);
        Assert.Equal("code-1", OAuthHarness.Value(Form(token), "code"));
        await Json(token, 200, TokenAnswer);
        await flow.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-MCP-150")] // Services/MCP/MCPOAuth.swift:565-566
    public async Task The_failure_page_Content_Length_counts_the_em_dash_as_three_bytes()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var answer = await CallbackClient.Get(CallbackPort(url), "/callback?code=c&state=wrong");
        var (head, body) = CallbackClient.Split(answer);
        var text = Encoding.UTF8.GetString(body);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), body.Length);
        Assert.Equal(text.Length + 2, body.Length);
        Assert.Contains("\r\nContent-Length: " + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n", head, StringComparison.Ordinal);
        await Fails(flow);
    }

    // ---- PR-MCP-151 and PR-MCP-161: the authorization address and PKCE ----

    [Fact]
    [Trait("Req", "PR-MCP-151")] // Services/MCP/MCPOAuth.swift:574-599
    public async Task The_authorization_address_keeps_the_endpoint_query_then_appends_six_items_resource_and_scope_in_order()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow, resourceMetadata:
            $$"""{"resource":"{{b}}/mcp","authorization_servers":["{{b}}/auth"],"scopes_supported":["mcp:tools","offline access"]}""");
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        Assert.StartsWith(b + "/authorize?", url, StringComparison.Ordinal);
        var pairs = OAuthHarness.QueryPairs(url);
        Assert.Equal(
            ["prompt", "response_type", "client_id", "redirect_uri", "code_challenge", "code_challenge_method", "state", "resource", "scope"],
            pairs.Select(pair => pair.Name));
        Assert.Equal("consent", pairs[0].Value);
        Assert.Equal("code", pairs[1].Value);
        Assert.Equal("cid-1", pairs[2].Value);
        Assert.Matches("^http://127\\.0\\.0\\.1:[0-9]+/callback$", pairs[3].Value);
        Assert.Equal("S256", pairs[5].Value);
        Assert.Equal(b + "/mcp", pairs[7].Value);
        Assert.Equal("mcp:tools offline access", pairs[8].Value);
    }

    [Fact]
    [Trait("Req", "PR-MCP-151")] // Services/MCP/MCPOAuth.swift:583-596
    public async Task Without_a_resource_or_scopes_the_authorization_address_ends_with_the_state()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow,
            resourceMetadata: $$"""{"resource":"","authorization_servers":["{{b}}/auth"]}""",
            authMetadata: $$"""{"authorization_endpoint":"{{b}}/authorize","token_endpoint":"{{b}}/token","registration_endpoint":"{{b}}/register"}""");
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        Assert.Equal(
            ["response_type", "client_id", "redirect_uri", "code_challenge", "code_challenge_method", "state"],
            OAuthHarness.QueryPairs(url).Select(pair => pair.Name));
        var token = await Finish(server, flow, url);
        Assert.DoesNotContain("resource", Form(token).Select(pair => pair.Name));
    }

    [Fact]
    [Trait("Req", "PR-MCP-161")] // Services/MCP/MCPOAuth.swift:819-837
    public async Task The_verifier_is_32_bytes_and_the_state_24_bytes_base64url_unpadded_and_the_challenge_is_their_S256_digest()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var pairs = OAuthHarness.QueryPairs(url);
        var challenge = OAuthHarness.Value(pairs, "code_challenge");
        var state = OAuthHarness.Value(pairs, "state");
        Assert.Equal("S256", OAuthHarness.Value(pairs, "code_challenge_method"));
        var verifier = OAuthHarness.Value(Form(await Finish(server, flow, url)), "code_verifier");
        // 32 bytes are 43 base64url characters, 24 bytes are 32, with "+" as "-", "/" as "_" and no "=".
        Assert.Matches("^[A-Za-z0-9_-]{43}$", verifier);
        Assert.Matches("^[A-Za-z0-9_-]{32}$", state);
        var digest = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)))
            .Replace("+", "-", StringComparison.Ordinal).Replace("/", "_", StringComparison.Ordinal).Replace("=", "", StringComparison.Ordinal);
        Assert.Equal(digest, challenge);
        Assert.Equal(32, Convert.FromBase64String(verifier.Replace("-", "+", StringComparison.Ordinal).Replace("_", "/", StringComparison.Ordinal) + "=").Length);
        Assert.Equal(24, Convert.FromBase64String(state.Replace("-", "+", StringComparison.Ordinal).Replace("_", "/", StringComparison.Ordinal)).Length);
    }

    // ---- PR-MCP-152: the code exchange ----

    [Fact]
    [Trait("Req", "PR-MCP-152")] // Services/MCP/MCPOAuth.swift:601-622
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:768-776
    public async Task The_code_exchange_posts_seven_form_fields_in_order()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var token = await Finish(server, flow, url);
        Assert.Equal(["application/x-www-form-urlencoded"], token.HeaderValues("Content-Type"));
        Assert.Equal(["application/json"], token.HeaderValues("Accept"));
        var form = Form(token);
        Assert.Equal(
            ["grant_type", "code", "redirect_uri", "client_id", "code_verifier", "client_secret", "resource"],
            form.Select(pair => pair.Name));
        Assert.Equal("authorization_code", form[0].Value);
        Assert.Equal("code-1", form[1].Value);
        Assert.Equal(OAuthHarness.Value(OAuthHarness.QueryPairs(url), "redirect_uri"), form[2].Value);
        Assert.Equal("cid-1", form[3].Value);
        Assert.Equal("sec-1", form[5].Value);
        Assert.Equal(b + "/mcp", form[6].Value);
    }

    [Theory]
    [Trait("Req", "PR-MCP-152")] // Services/MCP/MCPOAuth.swift:622-629
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:795-801
    [InlineData(400, """{"error":"invalid_grant","error_description":"Code expired"}""", "Couldn't finish signing in. Code expired")]
    [InlineData(400, """{"error":"invalid_grant"}""", "Couldn't finish signing in. invalid_grant")]
    [InlineData(400, """{"error":"invalid_grant","error_description":""}""", "Couldn't finish signing in. invalid_grant")]
    [InlineData(200, """{"token_type":"Bearer"}""", "Couldn't finish signing in. The server didn't hand back a token.")]
    [InlineData(200, """{"access_token":""}""", "Couldn't finish signing in. The server didn't hand back a token.")]
    [InlineData(500, "not json", "Couldn't finish signing in. The server didn't hand back a token.")]
    [InlineData(400, """{"error":""}""", "Couldn't finish signing in.")]
    public async Task A_refused_code_exchange_throws_tokenFailed_and_stores_no_session(int status, string body, string expected)
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        await CallbackClient.Get(CallbackPort(url), "/callback?code=code-1&state=" + StateOf(url));
        var token = await Next(server, flow);
        await token.Respond(status, OAuthHarness.Reason(status), Encoding.UTF8.GetBytes(body), ("Content-Type", "application/json"));
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.TokenFailed>(error.ErrorCase);
        Assert.Equal(expected, error.ErrorDescription);
        Assert.Null(h.Stored(SessionAccount));
        Assert.False(MCPOAuth.IsSignedIn(ServerID));
    }

    // ---- PR-MCP-159: the HTTP helpers ----

    [Fact]
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:717-737
    [Trait("Req", "PR-MCP-142")] // Services/MCP/MCPOAuth.swift:347-349
    public async Task A_metadata_request_unanswered_for_30_seconds_collapses_to_the_network_did_not_answer()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm\"");
        await Json(await Next(server, flow), 200, $$"""{"authorization_servers":["{{b}}"]}""");
        var first = await Next(server, flow);
        Assert.Equal("/.well-known/oauth-authorization-server", first.Target);
        h.Runtime.Advance(TimeSpan.FromSeconds(30));
        var second = await Next(server, flow);
        Assert.Equal("/.well-known/openid-configuration", second.Target);
        h.Runtime.Advance(TimeSpan.FromSeconds(30));
        var error = await Fails(flow);
        Assert.IsType<MCPOAuthError.Case.MetadataFailed>(error.ErrorCase);
        Assert.Equal("The server's sign-in details didn't make sense. The network didn't answer.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:721
    public async Task A_metadata_request_answered_after_29_seconds_is_still_read()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Challenge(await Next(server, flow), $"Bearer resource_metadata=\"{b}/prm\"");
        await Json(await Next(server, flow), 200, $$"""{"authorization_servers":["{{b}}"]}""");
        var first = await Next(server, flow);
        h.Runtime.Advance(TimeSpan.FromSeconds(29));
        await Json(first, 404, "{}");
        var second = await Next(server, flow);
        h.Runtime.Advance(TimeSpan.FromSeconds(29));
        await Json(second, 404, "{}");
        var error = await Fails(flow);
        // :348 keeps the caught error's whole errorDescription as lastError and :351 wraps it in metadataFailed again, so
        // fetchJSON's own "The server's sign-in details didn't make sense." prefix (:739) appears twice.
        Assert.Equal("The server's sign-in details didn't make sense. The server's sign-in details didn't make sense. The server answered 404.", error.ErrorDescription);
    }

    // B3 (A12, PR-MCP-159 b3_note): postForm's raw body bytes, not just the decoded pairs the tests above compare
    // (:768-778, :790-793). A code exchange's redirect_uri always has ":" and "/" (it is "http://127.0.0.1:<port>/callback"),
    // and this seeds a due refresh whose stored refresh_token has a "+", so both reserved characters are exercised.
    [Fact]
    [Trait("Req", "PR-MCP-159")] // Services/MCP/MCPOAuth.swift:768-793
    public async Task PostForm_percent_encodes_colon_slash_and_plus_and_leaves_the_verifier_bare()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var b = Base(server);
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var redirect = OAuthHarness.Value(OAuthHarness.QueryPairs(url), "redirect_uri"); // e.g. http://127.0.0.1:PORT/callback
        Assert.Contains(':', redirect);
        Assert.Contains('/', redirect);
        var token = await Finish(server, flow, url);
        var rawExchange = Encoding.UTF8.GetString(token.Body);
        var beforeVerifier = "grant_type=authorization_code&code=code-1&redirect_uri=" + FormEncoded(redirect) + "&client_id=cid-1&code_verifier=";
        var afterVerifierAt = rawExchange.IndexOf("&client_secret=", StringComparison.Ordinal);
        Assert.True(afterVerifierAt > 0 && rawExchange.StartsWith(beforeVerifier, StringComparison.Ordinal));
        var verifier = rawExchange[beforeVerifier.Length..afterVerifierAt];
        Assert.Matches("^[A-Za-z0-9_-]{43}$", verifier); // base64url(32 random bytes), all unreserved: formEncode leaves it untouched
        Assert.Equal(beforeVerifier + verifier + "&client_secret=sec-1&resource=" + FormEncoded(b + "/mcp"), rawExchange);
        // ":" and "/" never survive unescaped, and nothing was turned into "+".
        Assert.DoesNotContain(":", rawExchange.Replace("%3A", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("/", rawExchange.Replace("%2F", "", StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.True(MCPOAuth.SignOut(ServerID)); // clears the session the exchange above just stored, so Seed below is not a duplicate
        h.Seed(SessionAccount, $$"""{"access_token":"old","client_id":"cid-1","client_secret":"sec-1","expires_at":{{h.NowUnixSeconds - 10}},"refresh_token":"rt+1","token_endpoint":"{{b}}/token","resource":"{{b}}/res"}""");
        var refreshFlow = Task.Run(() => MCPOAuth.AccessToken(ServerID));
        var post = await Next(server, refreshFlow);
        var rawRefresh = Encoding.UTF8.GetString(post.Body);
        Assert.Equal(
            "grant_type=refresh_token&refresh_token=rt%2B1&client_id=cid-1&client_secret=sec-1&resource=" + FormEncoded(b + "/res"),
            rawRefresh);
        Assert.Contains("rt%2B1", rawRefresh, StringComparison.Ordinal); // "+" becomes %2B (uppercase, ED-201), never left bare and never turned into a literal "+" query "+"
        await Json(post, 200, """{"access_token":"new","expires_in":60}""");
        Assert.Equal("new", await refreshFlow.WaitAsync(Guard));
    }

    // ---- Part 2, PR-MCP-163: the callback page document ----
    //
    // The expected document is transcribed from the multi-line literal at MCPOAuth.swift:858-915: Swift strips the
    // closing delimiter's eight-space indentation from every line, joins the lines with "\n" and ends the string at
    // "</html>" with no newline after it. Each line below cites the Swift line it copies.

    // MCPOAuth.swift:907, verbatim: the logo, one line. Its UTF-8 bytes (1,448) hash to the SHA-256 of that Swift line
    // with its eight-space indentation removed, f6a4a2f8b88336526475f9d59dedb1c73c515ced89456e2485514bbac303272d.
    private const string Logo = "<svg width=\"56\" height=\"56\" viewBox=\"0 0 100 100\" opacity=\"0.9\" xmlns=\"http://www.w3.org/2000/svg\"><path fill=\"#FFFFFF\" fill-rule=\"nonzero\" d=\"M16.00 94.00 C16.00 94.00 16.17 80.33 17.00 74.00 C17.83 67.67 19.50 61.00 21.00 56.00 C22.50 51.00 25.50 47.33 26.00 44.00 C26.50 40.67 26.17 38.83 24.00 36.00 C21.83 33.17 15.83 30.17 13.00 27.00 C10.17 23.83 7.00 17.00 7.00 17.00 C7.00 17.00 16.50 24.50 20.00 27.00 C23.50 29.50 27.50 33.17 28.00 32.00 C28.50 30.83 24.33 24.50 23.00 20.00 C21.67 15.50 20.00 5.00 20.00 5.00 C20.00 5.00 27.50 12.67 31.00 16.00 C34.50 19.33 37.17 23.67 41.00 25.00 C44.83 26.33 50.17 23.33 54.00 24.00 C57.83 24.67 61.00 26.67 64.00 29.00 C67.00 31.33 68.67 34.50 72.00 38.00 C75.33 41.50 80.33 46.33 84.00 50.00 C87.67 53.67 92.17 57.33 94.00 60.00 C95.83 62.67 95.33 64.33 95.00 66.00 C94.67 67.67 93.83 69.00 92.00 70.00 C90.17 71.00 87.33 71.67 84.00 72.00 C80.67 72.33 75.33 71.50 72.00 72.00 C68.67 72.50 67.33 72.67 64.00 75.00 C60.67 77.33 55.33 83.17 52.00 86.00 C48.67 88.83 46.00 90.67 44.00 92.00 C42.00 93.33 40.00 94.00 40.00 94.00 C40.00 94.00 16.00 94.00 16.00 94.00 Z M63.00 38.00 C60.24 38.00 58.00 40.24 58.00 43.00 C58.00 45.76 60.24 48.00 63.00 48.00 C65.76 48.00 68.00 45.76 68.00 43.00 C68.00 40.24 65.76 38.00 63.00 38.00 Z M86.00 61.20 C85.01 61.20 84.20 62.01 84.20 63.00 C84.20 63.99 85.01 64.80 86.00 64.80 C86.99 64.80 87.80 63.99 87.80 63.00 C87.80 62.01 86.99 61.20 86.00 61.20 Z\"/></svg>";

    private const string LogoSha256 = "f6a4a2f8b88336526475f9d59dedb1c73c515ced89456e2485514bbac303272d";

    private static string Document(string headTitle, string heading, string message) => string.Join("\n",
    [
        "<!DOCTYPE html>", // :859
        "<html lang=\"en\">", // :860
        "<head>", // :861
        "<meta charset=\"utf-8\">", // :862
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">", // :863
        "<meta name=\"color-scheme\" content=\"dark\">", // :864
        "<title>" + headTitle + "</title>", // :865
        "<style>", // :866
        "* { box-sizing: border-box; }", // :867
        "html, body { margin: 0; padding: 0; }", // :868
        "body {", // :869
        "  background: #0B0B0F;", // :870
        "  color: #F5F5F7;", // :871
        "  font-family: -apple-system, \"SF Pro Text\", \"Helvetica Neue\", Helvetica, sans-serif;", // :872
        "  min-height: 100vh;", // :873
        "  display: flex;", // :874
        "  align-items: center;", // :875
        "  justify-content: center;", // :876
        "}", // :877
        ".card {", // :878
        "  width: 420px;", // :879
        "  max-width: calc(100vw - 48px);", // :880
        "  padding: 40px;", // :881
        "  border-radius: 22px;", // :882
        "  background: rgba(255, 255, 255, 0.06);", // :883
        "  text-align: center;", // :884
        "}", // :885
        "h1 {", // :886
        "  font-size: 22px;", // :887
        "  font-weight: 600;", // :888
        "  color: #F5F5F7;", // :889
        "  margin: 20px 0 8px;", // :890
        "}", // :891
        "p {", // :892
        "  font-size: 14px;", // :893
        "  color: rgba(255, 255, 255, 0.62);", // :894
        "  line-height: 1.5;", // :895
        "  margin: 0 0 24px;", // :896
        "}", // :897
        ".wordmark {", // :898
        "  font-size: 12px;", // :899
        "  color: rgba(255, 255, 255, 0.4);", // :900
        "  letter-spacing: 0.04em;", // :901
        "}", // :902
        "</style>", // :903
        "</head>", // :904
        "<body>", // :905
        "<div class=\"card\">", // :906
        Logo, // :907
        "<h1>" + heading + "</h1>", // :908
        "<p>" + message + "</p>", // :909
        "<div class=\"wordmark\">Nth</div>", // :910 DEVIATION OD-0012 F1: upstream reads "Droppy Code"
        "</div>", // :911
        "<script>setTimeout(() => window.close(), 1500);</script>", // :912
        "</body>", // :913
        "</html>", // :914
    ]);

    [Fact]
    [Trait("Req", "PR-MCP-163")] // Services/MCP/MCPOAuth.swift:841-847, :857-916
    public void SuccessPage_is_the_whole_fixed_document_with_the_Marrow_wordmark()
    {
        Assert.Equal(
            Document("Signed in to Linear", "Signed in to Linear", "You can close this tab and head back to Nth."),
            MCPOAuth.SuccessPage("Linear"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-163")] // Services/MCP/MCPOAuth.swift:849-855, :857-916
    public void FailurePage_is_the_whole_fixed_document_with_the_Marrow_wordmark()
    {
        Assert.Equal(
            Document("Sign-in didn't finish \U00002014 Linear", "Sign-in didn't finish", "The token expired."), // U+2014 EM DASH
            MCPOAuth.FailurePage("Linear", "The token expired."));
    }

    [Fact]
    [Trait("Req", "PR-MCP-163")] // Services/MCP/MCPOAuth.swift:858-915
    public void The_page_starts_at_the_doctype_ends_at_the_closing_html_tag_and_uses_bare_line_feeds()
    {
        foreach (var page in new[] { MCPOAuth.SuccessPage("Linear"), MCPOAuth.FailurePage("Linear", "Why") })
        {
            Assert.StartsWith("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n", page, StringComparison.Ordinal);
            Assert.EndsWith("\n</div>\n<script>setTimeout(() => window.close(), 1500);</script>\n</body>\n</html>", page, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", page, StringComparison.Ordinal);
            Assert.DoesNotContain("\t", page, StringComparison.Ordinal);
            // Only the CSS declarations are indented, by exactly two spaces (:870-901).
            Assert.DoesNotContain("\n ", page.Replace("\n  ", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Equal(56, page.Split('\n').Length); // :859-914
            Assert.Contains("\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<meta name=\"color-scheme\" content=\"dark\">\n", page, StringComparison.Ordinal);
            Assert.Contains("\n<div class=\"wordmark\">Nth</div>\n", page, StringComparison.Ordinal); // DEVIATION OD-0012 F1
            Assert.DoesNotContain("Droppy", page, StringComparison.Ordinal);
            Assert.Equal(2, page.Split("<script>").Length);
            Assert.Contains("\n<script>setTimeout(() => window.close(), 1500);</script>\n", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-163")] // Services/MCP/MCPOAuth.swift:866-906
    public void The_stylesheet_is_the_exact_dark_card_rules()
    {
        var page = MCPOAuth.SuccessPage("Linear");
        const string css =
            "\n<style>\n"
            + "* { box-sizing: border-box; }\n"
            + "html, body { margin: 0; padding: 0; }\n"
            + "body {\n  background: #0B0B0F;\n  color: #F5F5F7;\n"
            + "  font-family: -apple-system, \"SF Pro Text\", \"Helvetica Neue\", Helvetica, sans-serif;\n"
            + "  min-height: 100vh;\n  display: flex;\n  align-items: center;\n  justify-content: center;\n}\n"
            + ".card {\n  width: 420px;\n  max-width: calc(100vw - 48px);\n  padding: 40px;\n  border-radius: 22px;\n"
            + "  background: rgba(255, 255, 255, 0.06);\n  text-align: center;\n}\n"
            + "h1 {\n  font-size: 22px;\n  font-weight: 600;\n  color: #F5F5F7;\n  margin: 20px 0 8px;\n}\n"
            + "p {\n  font-size: 14px;\n  color: rgba(255, 255, 255, 0.62);\n  line-height: 1.5;\n  margin: 0 0 24px;\n}\n"
            + ".wordmark {\n  font-size: 12px;\n  color: rgba(255, 255, 255, 0.4);\n  letter-spacing: 0.04em;\n}\n"
            + "</style>\n</head>\n<body>\n<div class=\"card\">\n";
        Assert.Contains(css, page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-163")] // Services/MCP/MCPOAuth.swift:906-911
    public void The_card_holds_the_logo_line_then_the_heading_then_the_message_then_the_wordmark()
    {
        var lines = MCPOAuth.FailurePage("Linear", "Why").Split('\n');
        Assert.Equal(
            ["<div class=\"card\">", Logo, "<h1>Sign-in didn't finish</h1>", "<p>Why</p>", "<div class=\"wordmark\">Nth</div>", "</div>"],
            lines[47..53]);
        Assert.Equal(1448, Encoding.UTF8.GetByteCount(Logo));
        Assert.Equal(LogoSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Logo))));
        Assert.Equal(LogoSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines[48]))));
    }

    [Fact]
    [Trait("Req", "PR-MCP-163")] // Services/MCP/MCPOAuth.swift:907
    public void The_logo_is_one_svg_with_a_single_white_path_of_three_closed_two_decimal_subpaths()
    {
        var logo = MCPOAuth.SuccessPage("Linear").Split('\n')[48];
        Assert.StartsWith("<svg width=\"56\" height=\"56\" viewBox=\"0 0 100 100\" opacity=\"0.9\" xmlns=\"http://www.w3.org/2000/svg\"><path fill=\"#FFFFFF\" fill-rule=\"nonzero\" d=\"", logo, StringComparison.Ordinal);
        Assert.EndsWith(" Z\"/></svg>", logo, StringComparison.Ordinal);
        Assert.Equal(2, logo.Split("<svg").Length);
        Assert.Equal(2, logo.Split("<path").Length);
        var start = logo.IndexOf(" d=\"", StringComparison.Ordinal) + 4;
        var d = logo[start..logo.LastIndexOf("\"/></svg>", StringComparison.Ordinal)];
        Assert.EndsWith(" Z", d, StringComparison.Ordinal);
        var subpaths = d.Split(" Z", StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()).ToArray();
        Assert.Equal(3, subpaths.Length);
        Assert.StartsWith("M16.00 94.00 C16.00 94.00 16.17 80.33 17.00 74.00 ", subpaths[0], StringComparison.Ordinal);
        Assert.StartsWith("M63.00 38.00 C60.24 38.00 58.00 40.24 58.00 43.00 ", subpaths[1], StringComparison.Ordinal);
        Assert.StartsWith("M86.00 61.20 C85.01 61.20 84.20 62.01 84.20 63.00 ", subpaths[2], StringComparison.Ordinal);
        Assert.EndsWith(" C87.80 62.01 86.99 61.20 86.00 61.20", subpaths[2], StringComparison.Ordinal);
        foreach (var token in d.Split(' '))
        {
            if (token == "Z") continue;
            Assert.Matches("^[MC]?[0-9]+\\.[0-9]{2}$", token);
        }
        Assert.Equal(Logo, logo);
    }

    [Fact]
    [Trait("Req", "PR-MCP-163")] // Services/MCP/MCPOAuth.swift:546-551, :857-916
    public async Task The_page_the_callback_listener_serves_is_the_document()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var answer = await CallbackClient.Get(CallbackPort(url), "/callback?code=c&state=wrong");
        Assert.Equal(
            Document("Sign-in didn't finish \U00002014 Linear &amp; Co", "Sign-in didn't finish", "Sign-in didn&#39;t finish. Try again."), // U+2014 EM DASH
            Page(answer));
        await Fails(flow);
    }

    // ---- Part 2, PR-MCP-164: escapeHTML ----

    [Theory]
    [Trait("Req", "PR-MCP-164")] // Services/MCP/MCPOAuth.swift:918-932
    [InlineData("&", "&amp;")]
    [InlineData("<", "&lt;")]
    [InlineData(">", "&gt;")]
    [InlineData("\"", "&quot;")]
    [InlineData("'", "&#39;")]
    public void Each_of_the_five_characters_is_replaced_in_the_server_name_of_both_pages_and_in_the_failure_message(string character, string entity)
    {
        var name = "a" + character + "b" + character;
        var escaped = "a" + entity + "b" + entity;
        Assert.Equal(Document("Signed in to " + escaped, "Signed in to " + escaped, "You can close this tab and head back to Nth."), MCPOAuth.SuccessPage(name));
        Assert.Equal(Document("Sign-in didn't finish \U00002014 " + escaped, "Sign-in didn't finish", escaped), MCPOAuth.FailurePage(name, name)); // U+2014 EM DASH
    }

    [Theory]
    [Trait("Req", "PR-MCP-164")] // Services/MCP/MCPOAuth.swift:921-929
    [InlineData("")]
    [InlineData("Linear")]
    [InlineData("a/b=c;d#e`f%g")]
    [InlineData("Café \U0001F642 é")]
    [InlineData("line\nbreak\ttab")]
    [InlineData("<́>́")] // two Characters, "<" and ">" each with a combining acute, so neither equals "<" or ">"
    public void Every_other_character_is_copied_unchanged(string text)
    {
        Assert.Equal(Document("Signed in to " + text, "Signed in to " + text, "You can close this tab and head back to Nth."), MCPOAuth.SuccessPage(text));
        Assert.Equal(Document("Sign-in didn't finish \U00002014 " + text, "Sign-in didn't finish", text), MCPOAuth.FailurePage(text, text)); // U+2014 EM DASH
    }

    [Fact]
    [Trait("Req", "PR-MCP-164")] // Services/MCP/MCPOAuth.swift:918-932
    public void Escaping_is_one_pass_so_an_existing_entity_is_escaped_again()
    {
        Assert.Equal(
            Document("Sign-in didn't finish \U00002014 &amp;amp; &amp;#39;", "Sign-in didn't finish", "&amp;lt;b&amp;gt;"), // U+2014 EM DASH
            MCPOAuth.FailurePage("&amp; &#39;", "&lt;b&gt;"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-164")] // Services/MCP/MCPOAuth.swift:849-855, :918-932
    public void A_hostile_message_cannot_add_markup_to_the_failure_page()
    {
        var page = MCPOAuth.FailurePage("x\"><script>", "<script>alert('x')</script><img src=\"y\" onerror=\"z\">");
        Assert.Equal(
            Document(
                "Sign-in didn't finish \U00002014 x&quot;&gt;&lt;script&gt;", // U+2014 EM DASH
                "Sign-in didn't finish",
                "&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;&lt;img src=&quot;y&quot; onerror=&quot;z&quot;&gt;"),
            page);
        Assert.Equal(2, page.Split("<script>").Length); // only the page's own close script (:912)
        Assert.DoesNotContain("<img", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-164")] // Services/MCP/MCPOAuth.swift:530-545, :918-932
    public async Task A_hostile_error_description_from_the_browser_is_escaped_on_the_served_page()
    {
        using var h = new OAuthHarness();
        await using var server = new LoopbackHttpServer();
        var flow = StartSignIn(server);
        await Discover(server, flow);
        await Register(server, flow);
        var url = h.AwaitBrowser(flow);
        var answer = await CallbackClient.Get(
            CallbackPort(url),
            "/callback?error=access_denied&error_description=%3Cb%20onclick%3D%22a%27%22%3E%26&state=" + StateOf(url));
        Assert.Equal(
            Document("Sign-in didn't finish \U00002014 Linear &amp; Co", "Sign-in didn't finish", "&lt;b onclick=&quot;a&#39;&quot;&gt;&amp;"), // U+2014 EM DASH
            Page(answer));
        var error = await Fails(flow);
        Assert.Equal("<b onclick=\"a'\">&", error.ErrorDescription);
    }
}
