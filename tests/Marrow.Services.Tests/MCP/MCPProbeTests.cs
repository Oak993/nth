using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPProbe.cs, the port of DroppyCode/Services/MCP/MCPProbe.swift (B2 1fbf81e),
// :1-431, and the records PR-MCP-100 to PR-MCP-118 and PR-MCP-120. Remote-path expectations are proven against a
// real loopback HTTP/1.1 server (Marrow.Testing.LoopbackHttpServer, R-TEST-10); stdio-path expectations are proven
// against small scripted PowerShell "MCP servers" (Fixtures/ProbeProcessScripts.cs), never a real AI CLI.
//
// Not tested here, with the reason:
//  - PR-MCP-106's exact 90 s / 240 s deadlines, and the timedOut branch of PR-MCP-109/120: reaching either deadline
//    needs either 90 real seconds or driving a live child process's I/O against a virtualized clock, which this
//    unit judges too fragile for an automated suite (TestRuntime's manual clock only reaches code that reads
//    SwiftClock.Provider; a real child process's own scheduling does not). isOAuth's effect on classification is
//    proven instead through Unauthorized_and_invalid_grant_in_stderr_are_unauthorizedOAuth_when_isOAuth below, which
//    reaches the same isOAuth flag through the exit-code path.
//  - PR-MCP-107's "a launch failure rethrows the system error": ProcessLauncher.Run's own launch-failure exception
//    is F.ProcessLauncher's concern and has its own tests; MCPProbe's cleanup on that path is proven by code
//    structure (the same Terminate/detach call in the catch and the finally) rather than by re-triggering a launch
//    failure here.
//  - PR-MCP-111's precise "terminate, then kill the tree one second later": that ladder is TerminationLadder.Shell,
//    proven by its own unit; MCPProbe.probeStdio does not expose the ProcessLauncher it creates, so this unit
//    cannot observe IsRunning after Probe returns without inventing a seam the Swift does not have.
//  - The exact 64,000/16,000-byte stderr cap of PR-MCP-112: proven indirectly by
//    A_stderr_line_that_survives_the_64000_byte_cap_is_still_reported below (a stderr well past the cap whose last
//    line is still reported correctly), not by asserting the two byte constants against a private buffer.
//  - PostJSON's "a non-HTTPURLResponse throws malformed(invalid reply)" (part of PR-MCP-116): unreachable over
//    HTTP/HTTPS from this harness (F.HTTP's URLSession only ever returns HTTPURLResponse for http/https).
public sealed class MCPProbeTests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;

    private static byte[] Ascii(string text) => Encoding.UTF8.GetBytes(text);

    private static MCPResolvedServer RemoteServer(Uri url, string? oauthUpstream = null, IReadOnlyDictionary<string, string>? headers = null) =>
        new("srv", "Server") { Url = url.ToString(), OauthUpstream = oauthUpstream, Headers = headers is null ? ValueMap<string, string>.Empty : new ValueMap<string, string>(headers) };

    private static async Task RespondInitializeAndTools(LoopbackHttpServer server, string toolsResultJson)
    {
        var init = await server.NextExchange();
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{\"name\":\"n\",\"version\":\"v\"}}}"),
            ("Content-Type", "application/json"));
        var notified = await server.NextExchange();
        await notified.Respond(202, "Accepted", []);
        var list = await server.NextExchange();
        await list.Respond(200, "OK", Ascii(toolsResultJson), ("Content-Type", "application/json"));
    }

    // ---- PR-MCP-100: dispatch precedence --------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-100")] // DroppyCode/Services/MCP/MCPProbe.swift:53-62
    public async Task Command_wins_over_url_even_when_both_are_set()
    {
        var server = new MCPResolvedServer("s", "S") { Command = "does-not-exist-mcp-probe-test", Url = "http://127.0.0.1:1/never" };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server));
        Assert.Equal(new MCPProbeError.Case.CommandMissing("does-not-exist-mcp-probe-test"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-100")] // :61
    public async Task Neither_command_nor_url_throws_malformed_missing_command_or_url()
    {
        var server = new MCPResolvedServer("s", "S");
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server));
        Assert.Equal(new MCPProbeError.Case.Malformed("missing command or URL"), error.ErrorCase);
        Assert.Equal("The server sent something unexpected: missing command or URL", error.ErrorDescription);
    }

    // ---- PR-MCP-101/102: the initialize params, the envelope and the id sequence ----------------------------

    [Fact]
    [Trait("Req", "PR-MCP-101")] // :66-72
    [Trait("Req", "PR-MCP-102")] // :74-86, :193, :208
    public async Task Initialize_sends_the_exact_params_envelope_and_id_1_then_tools_list_sends_id_2()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));

        var init = await loop.NextExchange();
        var initBody = Assert.IsType<JSONValue.Object>(JSONValue.Parse(init.Body));
        Assert.Equal("2.0", initBody["jsonrpc"]?.JSONValueString);
        Assert.Equal(1, initBody["id"]?.JSONValueInt);
        Assert.Equal("initialize", initBody["method"]?.JSONValueString);
        var parameters = initBody["params"];
        Assert.Equal("2025-06-18", parameters?["protocolVersion"]?.JSONValueString);
        Assert.Equal(0, parameters?["capabilities"]?.JSONValueObject?.Count);
        Assert.Equal("Marrow", parameters?["clientInfo"]?["name"]?.JSONValueString);
        Assert.Equal("1.0", parameters?["clientInfo"]?["version"]?.JSONValueString);
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{}}}"), ("Content-Type", "application/json"));

        var notified = await loop.NextExchange();
        var notifiedBody = Assert.IsType<JSONValue.Object>(JSONValue.Parse(notified.Body));
        Assert.Equal("notifications/initialized", notifiedBody["method"]?.JSONValueString);
        Assert.Null(notifiedBody["id"]);
        Assert.Null(notifiedBody["params"]);
        await notified.Respond(202, "Accepted", []);

        var list = await loop.NextExchange();
        var listBody = Assert.IsType<JSONValue.Object>(JSONValue.Parse(list.Body));
        Assert.Equal(2, listBody["id"]?.JSONValueInt);
        Assert.Equal("tools/list", listBody["method"]?.JSONValueString);
        await list.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[]}}"), ("Content-Type", "application/json"));

        await probing.WaitAsync(Guard);
    }

    // ---- PR-MCP-103: an error reply aborts the probe --------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-103")] // :88-92
    public async Task An_error_reply_aborts_the_probe_with_the_servers_message()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"message\":\"nope\"}}"), ("Content-Type", "application/json"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Malformed("nope"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-103")] // :90 error.message absent -> "unknown error"
    public async Task An_error_reply_with_no_message_uses_unknown_error()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{}}"), ("Content-Type", "application/json"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Malformed("unknown error"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-103")] // :89 an "error" that is not an object is ignored
    public async Task An_error_field_that_is_not_an_object_is_ignored()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":\"boom\",\"result\":{\"serverInfo\":{}}}"), ("Content-Type", "application/json"));
        var notified = await loop.NextExchange();
        await notified.Respond(202, "Accepted", []);
        var list = await loop.NextExchange();
        await list.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[]}}"), ("Content-Type", "application/json"));
        var result = await probing.WaitAsync(Guard);
        Assert.Empty(result.Tools);
    }

    // ---- PR-MCP-104: tool summaries ---------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-104")] // :94-107
    public async Task Tool_summaries_are_the_first_nonempty_trimmed_description_line_and_a_nameless_tool_is_dropped()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        await RespondInitializeAndTools(loop,
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[" +
            "{\"name\":\"alpha\",\"description\":\"  first line  \\nsecond line\"}," +
            "{\"description\":\"no name here\"}," +
            "{\"name\":\"beta\",\"description\":\"\\n\\nonly blank first line\"}," +
            "{\"name\":\"gamma\"}]}}");
        var result = await probing.WaitAsync(Guard);
        Assert.Equal(3, result.Tools.Count);
        Assert.Equal(new MCPToolSummary("alpha", "first line"), result.Tools[0]);
        Assert.Equal(new MCPToolSummary("beta", null), result.Tools[1]);
        Assert.Equal(new MCPToolSummary("gamma", null), result.Tools[2]);
    }

    [Fact]
    [Trait("Req", "PR-MCP-104")] // :102 a missing "tools" array yields an empty list, not an error
    public async Task A_missing_tools_array_yields_an_empty_list()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        await RespondInitializeAndTools(loop, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}");
        var result = await probing.WaitAsync(Guard);
        Assert.Empty(result.Tools);
    }

    // ---- PR-MCP-105/120: commandMissing --------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-105")] // :111-115
    public async Task An_unresolvable_command_throws_commandMissing_with_the_exact_name()
    {
        var server = new MCPResolvedServer("s", "S") { Command = "marrow-probe-test-nonexistent-cmd-93a1" };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server));
        Assert.Equal(new MCPProbeError.Case.CommandMissing("marrow-probe-test-nonexistent-cmd-93a1"), error.ErrorCase);
    }

    [Theory]
    [Trait("Req", "PR-MCP-120")] // :17-24 the three commandMissing messages
    [InlineData("npx", "npx isn't installed. Install Node.js from nodejs.org, then try again.")]
    [InlineData("uvx", "uvx isn't installed. Run `winget install astral-sh.uv` in Terminal, then try again.")]
    [InlineData("some-tool", "some-tool isn't installed. Install it, then try again.")]
    public void CommandMissing_renders_the_exact_message_for_npx_uvx_and_any_other_name(string name, string expected)
    {
        var error = new MCPProbeError(new MCPProbeError.Case.CommandMissing(name));
        Assert.Equal(expected, error.ErrorDescription);
    }

    // ---- Stdio handshake, args, env, cwd (PR-MCP-107, PR-MCP-108) --------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-107")] // :119-132
    [Trait("Req", "PR-MCP-108")] // :193-216
    public async Task Stdio_handshake_completes_env_layers_over_login_and_the_child_runs_in_the_home_directory()
    {
        var extra = new[] { "an arg with spaces", "an-arg-with-\"a-quote" };
        var args = ProbeProcessScripts.Args(ProbeProcessScripts.EchoArgsAndEnvAndCwd(), extra);
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(args),
            Env = new ValueMap<string, string>(new Dictionary<string, string> { ["MCP_PROBE_TEST_VAR"] = "override-value" }),
        };
        var result = await MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60));
        // The two elements above arrive at the script as $args[0] and $args[1], unsplit and unmangled, proving
        // Arguments is passed as a real vector (R-PROC-1), never a joined and re-parsed command line.
        Assert.Equal(string.Join('|', extra) + "|override-value", result.ServerName);
        Assert.Equal(LoginEnvironment.HomeDirectory, result.ServerVersion);
    }

    [Fact]
    [Trait("Req", "PR-MCP-108")] // :202-215 the ten-page cap
    public async Task Pagination_stops_after_ten_pages_even_when_the_server_always_returns_a_cursor()
    {
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.EndlessPages())),
        };
        var result = await MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(10, result.Tools.Count);
        Assert.Equal("t1", result.Tools[0].Name);
        Assert.Equal("t10", result.Tools[9].Name);
    }

    // ---- PR-MCP-109/110/120: exit classification --------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-109")] // :175-191 exit ends the wait at once
    [Trait("Req", "PR-MCP-110")] // :219-225
    [Trait("Req", "PR-MCP-120")] // :25-31 the exited message
    public async Task Exited_message_uses_the_last_nonempty_trimmed_stderr_line()
    {
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.ExitsWithStderr(3, "first problem", "  second problem  ", "", "   "))),
        };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60)));
        var exited = Assert.IsType<MCPProbeError.Case.Exited>(error.ErrorCase);
        Assert.Equal(3, exited.Code);
        Assert.Equal("The server quit (exit 3). second problem", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-106")] // :116 isOAuth = args.contains("mcp-remote")
    [Trait("Req", "PR-MCP-110")] // :220-222 precedence over the exit-code branch
    public async Task Unauthorized_or_invalid_grant_in_stderr_is_unauthorizedOAuth_when_isOAuth()
    {
        var args = ProbeProcessScripts.Args(ProbeProcessScripts.ExitsWithStderr(1, "401 Unauthorized"), ["mcp-remote"]);
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(args), // "mcp-remote" is an element of Args, an exact match (:116)
        };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.Equal(new MCPProbeError.Case.UnauthorizedOAuth(), error.ErrorCase);
        Assert.Equal("Sign-in was rejected. Sign in again in your browser.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-110")] // :219 isOAuth false -> the exited branch even with the same stderr text
    public async Task The_same_stderr_text_is_a_plain_exit_when_isOAuth_is_false()
    {
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.ExitsWithStderr(1, "401 Unauthorized"))),
        };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.IsType<MCPProbeError.Case.Exited>(error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-112")] // :404-409 the 64,000/16,000-byte stderr cap still reports the meaningful tail
    public async Task A_stderr_line_that_survives_the_64000_byte_cap_is_still_reported()
    {
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.ExitsWithOversizedStderr(9, "final line"))),
        };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.Equal("The server quit (exit 9). final line", error.ErrorDescription);
    }

    // ---- PR-MCP-113: session id, params carried, initialized errors swallowed except cancellation ------------

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :241-243
    public async Task An_unparseable_remote_url_throws_malformed_invalid_URL()
    {
        var server = new MCPResolvedServer("s", "S") { Url = "not a url at all" };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server));
        Assert.Equal(new MCPProbeError.Case.Malformed("invalid URL"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :248-250 the session id is carried forward and kept when a later reply has none
    public async Task The_session_id_is_adopted_and_kept_across_calls()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));

        var init = await loop.NextExchange();
        Assert.Empty(init.HeaderValues("Mcp-Session-Id"));
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{}}}"),
            ("Content-Type", "application/json"), ("Mcp-Session-Id", "sess-abc"));

        var notified = await loop.NextExchange();
        Assert.Equal(["sess-abc"], notified.HeaderValues("Mcp-Session-Id"));
        await notified.Respond(202, "Accepted", []); // no Mcp-Session-Id: the held one is kept

        var list = await loop.NextExchange();
        Assert.Equal(["sess-abc"], list.HeaderValues("Mcp-Session-Id"));
        await list.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[]}}"), ("Content-Type", "application/json"));

        await probing.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :281-285 a thrown error on notifications/initialized is swallowed
    public async Task An_error_answering_notifications_initialized_is_swallowed_and_the_probe_continues()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{}}}"), ("Content-Type", "application/json"));
        var notified = await loop.NextExchange();
        await notified.Respond(500, "Internal Server Error", Ascii("boom"));
        var list = await loop.NextExchange();
        await list.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[]}}"), ("Content-Type", "application/json"));
        var result = await probing.WaitAsync(Guard);
        Assert.Empty(result.Tools);
    }

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :269 an empty reply body throws malformed(empty reply)
    public async Task An_empty_initialize_reply_body_throws_malformed_empty_reply()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", []);
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Malformed("empty reply"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :271 a non-object body throws malformed(invalid reply)
    public async Task A_non_object_initialize_reply_throws_malformed_invalid_reply()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("[1,2,3]"), ("Content-Type", "application/json"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Malformed("invalid reply"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :277 a result that is not an object throws malformed(missing result)
    public async Task A_non_object_result_throws_malformed_missing_result()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"nope\"}"), ("Content-Type", "application/json"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Malformed("missing result"), error.ErrorCase);
    }

    // ---- PR-MCP-114: 401/403 OAuth handling -----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-114")] // :251, :255 a plain 401/403 with an OAuth upstream is unauthorizedOAuth
    public async Task A_401_from_an_OAuth_upstream_is_unauthorizedOAuth()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp"), oauthUpstream: "https://gitlab.example/api/v4/mcp"));
        var init = await loop.NextExchange();
        await init.Respond(401, "Unauthorized", Ascii("nope"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.UnauthorizedOAuth(), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-114")] // a 401/403 with no OAuth upstream is a plain http error, not caught specially
    public async Task A_401_without_an_OAuth_upstream_is_a_plain_http_error()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(401, "Unauthorized", Ascii("nope"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Http(401, "nope"), error.ErrorCase);
        Assert.Equal("That key was refused. Press Try again and paste a new one.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-114")] // :252-254 403 "MCP server disabled" (case-insensitive) is the GitLab setup guide
    public async Task A_403_MCP_server_disabled_body_is_the_GitLab_setup_guide()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp"), oauthUpstream: "https://gitlab.example/api/v4/mcp"));
        var init = await loop.NextExchange();
        await init.Respond(403, "Forbidden", Ascii("The MCP Server Disabled for this group."));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        var needsSetup = Assert.IsType<MCPProbeError.Case.NeedsSetup>(error.ErrorCase);
        Assert.Equal("Turn on GitLab's MCP server", needsSetup.Guide.Title);
        Assert.Equal(MCPProbe.GitLabServerDisabled("https://gitlab.example/api/v4/mcp"), needsSetup.Guide);
    }

    // ---- PR-MCP-115: 404 handling -----------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-115")] // :261-262 a 404 GitLab MCP path is the setup case, not unavailable
    public async Task A_404_at_a_GitLab_MCP_path_is_the_setup_guide()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var upstream = "https://gitlab.example/group/api/v4/mcp";
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp"), oauthUpstream: upstream));
        var init = await loop.NextExchange();
        await init.Respond(404, "Not Found", Ascii("nope"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        var needsSetup = Assert.IsType<MCPProbeError.Case.NeedsSetup>(error.ErrorCase);
        Assert.Equal(MCPProbe.GitLabServerDisabled(upstream), needsSetup.Guide);
    }

    [Fact]
    [Trait("Req", "PR-MCP-115")] // :264 a 404 at a non-GitLab OAuth upstream is unavailable(host)
    public async Task A_404_at_a_non_GitLab_OAuth_upstream_is_unavailable_with_its_host()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp"), oauthUpstream: "https://example.com/somewhere"));
        var init = await loop.NextExchange();
        await init.Respond(404, "Not Found", Ascii("nope"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Unavailable("example.com"), error.ErrorCase);
        Assert.Equal("example.com didn't answer (404). If the address is right, try again in a few minutes.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-115")] // a 404 without an OAuth upstream is a plain http error
    public async Task A_404_without_an_OAuth_upstream_is_a_plain_http_error()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(404, "Not Found", Ascii("nope"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Http(404, "nope"), error.ErrorCase);
        Assert.Equal("Nothing answered at that address. Check the address and try again.", error.ErrorDescription);
    }

    // ---- PR-MCP-116: headers, order, timeout status, body truncation ------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-116")] // :312-316 server headers first, then the three fixed ones, any overwritten
    public async Task Server_headers_are_applied_first_then_the_three_fixed_headers_overwrite_a_matching_name()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var headers = new Dictionary<string, string> { ["X-Api-Key"] = "secret", ["Content-Type"] = "text/plain" };
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp"), headers: headers));
        var init = await loop.NextExchange();
        Assert.Equal(["secret"], init.HeaderValues("X-Api-Key"));
        Assert.Equal(["application/json"], init.HeaderValues("Content-Type")); // overwritten, not duplicated
        Assert.Equal(["application/json, text/event-stream"], init.HeaderValues("Accept"));
        Assert.Equal(["2025-06-18"], init.HeaderValues("MCP-Protocol-Version"));
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{}}}"), ("Content-Type", "application/json"));
        var notified = await loop.NextExchange();
        await notified.Respond(202, "Accepted", []);
        var list = await loop.NextExchange();
        await list.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[]}}"), ("Content-Type", "application/json"));
        await probing.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-MCP-116")] // :324-326 the body is truncated to its first 2000 characters, then trimmed
    public async Task A_non_2xx_bodys_message_is_truncated_to_2000_characters_and_trimmed()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var longBody = new string('z', 2500) + "   ";
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(500, "Internal Server Error", Ascii(longBody));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        var http = Assert.IsType<MCPProbeError.Case.Http>(error.ErrorCase);
        Assert.Equal(500, http.Status);
        Assert.Equal(2000, http.Body.Length);
        Assert.Equal(new string('z', 2000), http.Body);
        Assert.Equal("The server answered 500.", error.ErrorDescription);
    }

    // ---- PR-MCP-117: the SSE first message --------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-117")] // :336-348 the first "data:" line that parses as an object carrying an id
    public async Task An_SSE_reply_yields_the_first_data_line_that_is_an_object_with_an_id()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        var sse = "event: message\r\n" +
                   "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/x\"}\r\n\r\n" + // no id: skipped
                   "data:   {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{\"name\":\"sse\"}}}  \r\n\r\n";
        await init.Respond(200, "OK", Ascii(sse), ("Content-Type", "text/event-stream"));
        var notified = await loop.NextExchange();
        await notified.Respond(202, "Accepted", []);
        var list = await loop.NextExchange();
        await list.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[]}}"), ("Content-Type", "application/json"));
        var result = await probing.WaitAsync(Guard);
        Assert.Equal("sse", result.ServerName);
    }

    [Fact]
    [Trait("Req", "PR-MCP-117")] // no matching "data:" line -> nil -> malformed(empty reply)
    public async Task An_SSE_reply_with_no_id_bearing_data_line_throws_malformed_empty_reply()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/x\"}\r\n\r\n"), ("Content-Type", "text/event-stream"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Malformed("empty reply"), error.ErrorCase);
    }

    // ---- PR-MCP-118: the fixed GitLab setup guide -----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-118")] // :354-357
    public void IsGitLabMCP_matches_a_path_ending_in_api_v4_mcp_case_insensitively()
    {
        Assert.True(MCPProbe.IsGitLabMCP("https://gitlab.com/api/v4/mcp"));
        Assert.True(MCPProbe.IsGitLabMCP("https://gitlab.example.com/group/API/V4/MCP"));
        Assert.False(MCPProbe.IsGitLabMCP("https://gitlab.com/api/v4/other"));
        Assert.False(MCPProbe.IsGitLabMCP("not a url"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-118")] // :359-378, exact strings including U+203A; apostrophes are ASCII U+0027
    public void GitLabServerDisabled_builds_the_fixed_four_step_guide_from_the_upstreams_origin()
    {
        var guide = MCPProbe.GitLabServerDisabled("https://gitlab.example.com:8443/group/api/v4/mcp");
        Assert.Equal("Turn on GitLab's MCP server", guide.Title);
        Assert.Contains("'", guide.Title, StringComparison.Ordinal); // "GitLab's" apostrophe is ASCII U+0027, not U+2019
        Assert.Equal("You're signed in, but GitLab keeps its MCP server off until a group owner turns it on. Until then, the address answers not found.", guide.Reason);
        Assert.Equal(new ValueList<string>([
            "Open your groups and pick the top-level group your projects live in",
            "Go to Settings › General and expand Permissions and group features",
            "Under MCP client access, tick Allow connection to GitLab and save",
            "Come back here and press Check again",
        ]), guide.Steps);
        Assert.Equal("Open my groups", guide.PageLabel);
        Assert.Equal("https://gitlab.example.com/dashboard/groups", guide.PageURL); // the port is dropped
        Assert.Equal("GitLab can take a minute to apply the change.", guide.Note);
    }

    [Fact]
    [Trait("Req", "PR-MCP-118")] // :362 no host -> the literal "https://gitlab.com" origin
    public void GitLabServerDisabled_falls_back_to_gitlab_dot_com_when_the_upstream_has_no_host()
    {
        var guide = MCPProbe.GitLabServerDisabled("not a url");
        Assert.Equal("https://gitlab.com/dashboard/groups", guide.PageURL);
    }

    // ---- PR-MCP-120: the remaining rendered messages, direct ------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-120")] // :32-33
    public void TimedOut_renders_the_fixed_message()
    {
        Assert.Equal("The server didn't answer in time.", new MCPProbeError(new MCPProbeError.Case.TimedOut()).ErrorDescription);
    }

    [Theory]
    [Trait("Req", "PR-MCP-120")] // :36-38
    [InlineData(401, "That key was refused. Press Try again and paste a new one.")]
    [InlineData(403, "That key was refused. Press Try again and paste a new one.")]
    [InlineData(404, "Nothing answered at that address. Check the address and try again.")]
    [InlineData(500, "The server answered 500.")]
    public void Http_status_renders_the_three_message_branches(int status, string expected)
    {
        Assert.Equal(expected, new MCPProbeError(new MCPProbeError.Case.Http(status, "ignored")).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-120")] // :26-31 no non-empty stderr line -> no trailing sentence
    public void Exited_with_only_blank_stderr_omits_the_trailing_sentence()
    {
        Assert.Equal("The server quit (exit 2).", new MCPProbeError(new MCPProbeError.Case.Exited(2, "\n  \n")).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-120")] // :47 malformed has no trailing full stop
    public void Malformed_has_no_trailing_full_stop()
    {
        var message = new MCPProbeError(new MCPProbeError.Case.Malformed("weird")).ErrorDescription;
        Assert.Equal("The server sent something unexpected: weird", message);
        Assert.False(message!.EndsWith('.'));
    }
}
