using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPProbe.cs, the port of DroppyCode/Services/MCP/MCPProbe.swift (B3 52bc7926),
// :1-234, and the records PR-MCP-100 to PR-MCP-118 and PR-MCP-120. B3_PLAN.md N5 moved the request builders, the
// stdio conversation loop, the remote POST and the SSE parser into Services/MCP/MCPWire.cs (MCPWire, MCPHTTPError,
// MCPWireError, MCPStdioConnection): this file's tests exercise MCPProbe.Probe end to end, over both paths, so that
// wiring is proven at the seam even though the pieces it calls carry their own unit tests under MCPWireTests.cs.
// Remote-path expectations are proven against a real loopback HTTP/1.1 server (Marrow.Testing.LoopbackHttpServer,
// R-TEST-10); stdio-path expectations are proven against small scripted PowerShell "MCP servers"
// (Fixtures/ProbeProcessScripts.cs), never a real AI CLI.
//
// Not tested here, with the reason:
//  - PR-MCP-106's exact 90 s / 240 s deadlines, and the timedOut branch of PR-MCP-109/120: reaching either deadline
//    needs either 90 real seconds or driving a live child process's I/O against a virtualized clock, which this
//    unit judges too fragile for an automated suite (TestRuntime's manual clock only reaches code that reads
//    SwiftClock.Provider; a real child process's own scheduling does not). isOAuth's effect on classification is
//    proven instead through Unauthorized_or_invalid_grant_in_stderr_is_unauthorizedOAuth_when_isOAuth below, which
//    reaches the same isOAuth flag through the exit-code path.
//  - PR-MCP-107's "a launch failure rethrows the system error": ProcessLauncher.Run's own launch-failure exception
//    is F.ProcessLauncher's concern and has its own tests; MCPStdioConnection.Launch's cleanup on that path is
//    MCPWireTests.cs's concern, not this file's.
//  - The exact 64,000/16,000-byte internal stderr buffer cap (distinct from the 4,000-byte errorTail this file's
//    tests do cover): that cap is StdioProcess.AppendError's concern, proven by its own unit.
//  - MCPHTTPError's "a non-HTTPURLResponse throws malformed(invalid reply)" (part of PR-MCP-116): unreachable over
//    HTTP/HTTPS from this harness (F.HTTP's URLSession only ever returns HTTPURLResponse for http/https), and not
//    this file's call site in any case (MCPWire.Post's concern).
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
    [Trait("Req", "PR-MCP-100")] // DroppyCode/Services/MCP/MCPProbe.swift:52-61
    public async Task Command_wins_over_url_even_when_both_are_set()
    {
        var server = new MCPResolvedServer("s", "S") { Command = "does-not-exist-mcp-probe-test", Url = "http://127.0.0.1:1/never" };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server));
        Assert.Equal(new MCPProbeError.Case.CommandMissing("does-not-exist-mcp-probe-test"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-100")] // :60
    public async Task Neither_command_nor_url_throws_malformed_missing_command_or_url()
    {
        var server = new MCPResolvedServer("s", "S");
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server));
        Assert.Equal(new MCPProbeError.Case.Malformed("missing command or URL"), error.ErrorCase);
        Assert.Equal("The server sent something unexpected: missing command or URL", error.ErrorDescription);
    }

    // ---- PR-MCP-101/102: the initialize params, the envelope and the id sequence ----------------------------
    // B3: the params/request/notification envelopes moved to MCPWire (Services/MCP/MCPWire.swift:9-29), called
    // from MCPProbe.swift:183 (initialize, id 1) and :195-196 (tools/list, id 2); this test proves the wiring.

    [Fact]
    [Trait("Req", "PR-MCP-101")] // Services/MCP/MCPWire.swift:9-15, via MCPProbe.swift:183
    [Trait("Req", "PR-MCP-102")] // Services/MCP/MCPWire.swift:17-25, via MCPProbe.swift:183, :195-196
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
        Assert.Equal("Nth", parameters?["clientInfo"]?["name"]?.JSONValueString);
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

    // Every_tools_list_page_is_sent_as_id_2_not_an_incrementing_id below proves the B3 change that a SECOND (and
    // later) remote tools/list page is also sent as id 2, not id 3, 4, ... (PR-MCP-113).

    // ---- PR-MCP-103: an error reply aborts the probe --------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-103")] // :63-67, MCPWire.errorMessage Services/MCP/MCPWire.swift:31-34
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
    [Trait("Req", "PR-MCP-103")] // Services/MCP/MCPWire.swift:32 error.message absent -> "unknown error"
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
    [Trait("Req", "PR-MCP-103")] // Services/MCP/MCPWire.swift:31 an "error" that is not an object is ignored
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
    [Trait("Req", "PR-MCP-104")] // :69-79 (firstLine, toolSummary)
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
    [Trait("Req", "PR-MCP-104")] // Services/MCP/MCPWire.swift:37 a missing "tools" array yields an empty list, not an error
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
    [Trait("Req", "PR-MCP-105")] // :84-87
    public async Task An_unresolvable_command_throws_commandMissing_with_the_exact_name()
    {
        var server = new MCPResolvedServer("s", "S") { Command = "marrow-probe-test-nonexistent-cmd-93a1" };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server));
        Assert.Equal(new MCPProbeError.Case.CommandMissing("marrow-probe-test-nonexistent-cmd-93a1"), error.ErrorCase);
    }

    [Theory]
    [Trait("Req", "PR-MCP-120")] // :16-23 the three commandMissing messages
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
    [Trait("Req", "PR-MCP-107")] // :83-95 (launch), MCPWire.swift MCPStdioConnection.Launch :139-150
    [Trait("Req", "PR-MCP-108")] // :116-131
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
    [Trait("Req", "PR-MCP-108")] // :124-126, MCPWire.swift ListTools's ten-page cap :36-51
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

    [Fact]
    [Trait("Req", "PR-MCP-109")] // :97-114; MCPWire.swift MCPStdioConnection.Handle :219-228 buffers, never discards
    public async Task A_reply_for_an_id_the_probe_is_not_yet_waiting_on_is_buffered_not_discarded()
    {
        // The script answers "initialize" (id 1) with two lines: the id-1 reply, and the id-2 tools/list reply
        // ("early") sent before the probe has even asked for id 2. The script has no "tools/list" branch, so the
        // real tools/list request the probe sends next is never separately answered: the only way Probe can
        // complete it is from MCPStdioConnection's buffer (MCPWire.swift :219-228 Handle / :203 AwaitReply). Under
        // the old B2 loop (`guard value["id"]?.int == id else { continue }`), the stray id-2 line would be
        // discarded while probing id 1, the real tools/list request would go unanswered, and Probe would time out
        // instead of returning "early" (review finding L3b-2).
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.EchoWithBufferedExtraReply())),
        };
        var result = await MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal("real", result.ServerName);
        Assert.Equal("v1", result.ServerVersion);
        Assert.Equal(["early"], result.Tools.Select(t => t.Name));
    }

    [Fact]
    [Trait("Req", "PR-MCP-109")] // Core/Support/StdioProcess.swift Deliver/FinishOutput :142-157, via MCPStdioConnection.Read
    public async Task A_final_reply_line_with_no_trailing_newline_is_still_parsed_at_process_exit()
    {
        // The script's last write is the tools/list reply with no trailing 0x0A before the process exits, so the
        // reply can only reach the probe through StdioProcess's end-of-stream flush (framer.Finish), not a newline.
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.EchoThenOmitFinalNewlineBeforeExit())),
        };
        var result = await MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Empty(result.Tools);
    }

    [Fact]
    [Trait("Req", "PR-MCP-111")] // :95 defer { connection.stop() } -> MCPWire.swift MCPStdioConnection.Stop :152-154
    [Trait("Decision", "ED-194")] // the ED-126/ED-194 ladder is pid-only (terminate, +3s SIGKILL), never a job-object tree kill
    public async Task Probe_stops_the_child_pid_only_after_returning_leaving_a_grandchild_running()
    {
        // Review finding L3b-4. ProbeStdio does not expose the MCPStdioConnection it creates (:95's defer runs
        // inside ProbeStdio itself), so this test learns the child's own pid, and a grandchild's pid, only through
        // the channel MCPProbeResult actually carries: serverInfo.name/.version (ProbeProcessScripts
        // .ReportsOwnPidAndAGrandchildAsServerInfo). After Probe returns, ProbeStdio's `finally { connection.Stop();
        // }` has already run. If Stop (or the code path that calls it) were removed, or switched back to a
        // job-object tree kill, the child would still be running when this test checks it, or the grandchild would
        // already be dead - either way this test would fail.
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.ReportsOwnPidAndAGrandchildAsServerInfo())),
        };
        var result = await MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60));
        var childPid = int.Parse(result.ServerName!, CultureInfo.InvariantCulture);
        var grandchildPid = int.Parse(result.ServerVersion!, CultureInfo.InvariantCulture);

        var grandchild = Process.GetProcessById(grandchildPid);
        _ = grandchild.SafeHandle; // retain the native handle before exit, as StdioProcessTests does
        try
        {
            var stillRunning = true;
            try
            {
                using var child = Process.GetProcessById(childPid);
                stillRunning = !child.WaitForExit(4_000); // true = exited before the timeout
            }
            catch (ArgumentException)
            {
                stillRunning = false; // already gone: the pid no longer resolves to a running process
            }
            Assert.False(stillRunning, "the probed child did not stop after Probe returned");
            Assert.False(grandchild.HasExited, "the grandchild should survive a pid-only stop, not a tree kill");
        }
        finally
        {
            grandchild.Refresh();
            if (!grandchild.HasExited) grandchild.Kill();
        }
    }

    // ---- PR-MCP-109/110/120: exit classification --------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-109")] // :97-114 exit ends the wait at once
    [Trait("Req", "PR-MCP-110")] // :134-140
    [Trait("Req", "PR-MCP-120")] // :24-30 the exited message
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
    [Trait("Req", "PR-MCP-106")] // :88 isOAuth = server.args.contains("mcp-remote")
    [Trait("Req", "PR-MCP-110")] // :135-137 precedence over the exit-code branch
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
    [Trait("Req", "PR-MCP-110")] // :135 isOAuth false -> the exited branch even with the same stderr text
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
    [Trait("Req", "PR-MCP-110")] // Core/Support/StdioProcess.swift AppendError :159-166, ErrorTail :50-53
    public async Task A_stderr_line_that_survives_the_internal_buffer_cap_is_still_reported()
    {
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.ExitsWithOversizedStderr(9, "final line"))),
        };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.Equal("The server quit (exit 9). final line", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-110")] // Core/Support/StdioProcess.swift ErrorTail :50-53, 4,000-byte window (PR-MCP-236)
    public async Task An_Unauthorized_marker_more_than_4000_bytes_before_the_end_of_stderr_is_not_seen()
    {
        // isOAuth is true (mcp-remote is present) and the child's FULL stderr contains "Unauthorized", but that
        // marker is followed by more than 4,000 bytes of filler before exit, so errorTail's last-4000-byte window
        // no longer carries it: classification must fall through to a plain exit, not unauthorizedOAuth.
        var args = ProbeProcessScripts.Args(ProbeProcessScripts.ExitsWithStaleUnauthorizedMarker(1), ["mcp-remote"]);
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(args),
        };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server).WaitAsync(TimeSpan.FromSeconds(60)));
        var exited = Assert.IsType<MCPProbeError.Case.Exited>(error.ErrorCase);
        Assert.Equal(1, exited.Code);
    }

    // ---- PR-MCP-113: session id, params carried, initialized errors swallowed except cancellation ------------

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :145-147
    public async Task An_unparseable_remote_url_throws_malformed_invalid_URL()
    {
        var server = new MCPResolvedServer("s", "S") { Url = "not a url at all" };
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => MCPProbe.Probe(server));
        Assert.Equal(new MCPProbeError.Case.Malformed("invalid URL"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :153 the session id is carried forward and kept when a later reply has none
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
    [Trait("Req", "PR-MCP-113")] // :195-202 every tools/list page is sent and matched as id 2, never an incrementing id (B3 change)
    public async Task Every_tools_list_page_is_sent_as_id_2_not_an_incrementing_id()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));

        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{}}}"), ("Content-Type", "application/json"));
        var notified = await loop.NextExchange();
        await notified.Respond(202, "Accepted", []);

        var page1 = await loop.NextExchange();
        var page1Body = Assert.IsType<JSONValue.Object>(JSONValue.Parse(page1.Body));
        Assert.Equal(2, page1Body["id"]?.JSONValueInt);
        Assert.Null(page1Body["params"]?["cursor"]);
        await page1.Respond(200, "OK",
            Ascii("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"a\"}],\"nextCursor\":\"next\"}}"),
            ("Content-Type", "application/json"));

        var page2 = await loop.NextExchange();
        var page2Body = Assert.IsType<JSONValue.Object>(JSONValue.Parse(page2.Body));
        Assert.Equal(2, page2Body["id"]?.JSONValueInt); // still id 2, never 3
        Assert.Equal("next", page2Body["params"]?["cursor"]?.JSONValueString);
        await page2.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"b\"}]}}"), ("Content-Type", "application/json"));

        var result = await probing.WaitAsync(Guard);
        Assert.Equal(2, result.Tools.Count);
        Assert.Equal("a", result.Tools[0].Name);
        Assert.Equal("b", result.Tools[1].Name);
    }

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :195-202 every page is matched as id 2 even over SSE, across three pages
    [Trait("Req", "PR-MCP-117")] // MCPWire.swift:92-102 an SSE frame for a different id is skipped, not accepted
    public async Task Every_tools_list_page_is_matched_by_id_2_when_answered_as_SSE_with_a_mismatched_id_frame_first()
    {
        // Review finding L3b-3: the prior test above answers every page as application/json, so it never exercises
        // MCPWire.Post's SSE id-matching branch for a page reply, and MCPProbe.cs:275's `Post(..., 2)` argument
        // could be replaced by 1 or null without failing anything. Each page here answers as text/event-stream with
        // a "wrong" frame carrying id 1 (the initialize id, not the tools/list id) before the correct id-2 frame;
        // only threading the caller's own id (2) through to MCPWire.Post, and MCPWire.Post actually matching on it
        // (MCPWire.swift :98), keeps the "wrong*" tools out of the result.
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));

        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{}}}"), ("Content-Type", "application/json"));
        var notified = await loop.NextExchange();
        await notified.Respond(202, "Accepted", []);

        static string Sse(string wrongIdFrame, string correctFrame) =>
            "data: " + wrongIdFrame + "\r\n\r\n" + "data: " + correctFrame + "\r\n\r\n";

        var page1 = await loop.NextExchange();
        Assert.Equal(2, Assert.IsType<JSONValue.Object>(JSONValue.Parse(page1.Body))["id"]?.JSONValueInt);
        await page1.Respond(200, "OK", Ascii(Sse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[{\"name\":\"wrong1\"}]}}",
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"a\"}],\"nextCursor\":\"p2\"}}")),
            ("Content-Type", "text/event-stream"));

        var page2 = await loop.NextExchange();
        var page2Body = Assert.IsType<JSONValue.Object>(JSONValue.Parse(page2.Body));
        Assert.Equal(2, page2Body["id"]?.JSONValueInt);
        Assert.Equal("p2", page2Body["params"]?["cursor"]?.JSONValueString);
        await page2.Respond(200, "OK", Ascii(Sse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[{\"name\":\"wrong2\"}]}}",
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"b\"}],\"nextCursor\":\"p3\"}}")),
            ("Content-Type", "text/event-stream"));

        var page3 = await loop.NextExchange();
        var page3Body = Assert.IsType<JSONValue.Object>(JSONValue.Parse(page3.Body));
        Assert.Equal(2, page3Body["id"]?.JSONValueInt);
        Assert.Equal("p3", page3Body["params"]?["cursor"]?.JSONValueString);
        await page3.Respond(200, "OK", Ascii(Sse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[{\"name\":\"wrong3\"}]}}",
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"c\"}]}}")),
            ("Content-Type", "text/event-stream"));

        var result = await probing.WaitAsync(Guard);
        Assert.Equal(["a", "b", "c"], result.Tools.Select(t => t.Name));
    }

    [Fact]
    [Trait("Req", "PR-MCP-113")] // :189-193 a thrown error on notifications/initialized is swallowed
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
    [Trait("Req", "PR-MCP-113")] // :177 an empty reply body throws malformed(empty reply)
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
    [Trait("Req", "PR-MCP-113")] // :179 a non-object body throws malformed(invalid reply)
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
    [Trait("Req", "PR-MCP-113")] // :185 a result that is not an object throws malformed(missing result)
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
    [Trait("Req", "PR-MCP-114")] // :155, :160 a plain 401/403 with an OAuth upstream is unauthorizedOAuth
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
    [Trait("Req", "PR-MCP-114")] // :157-159 403 "MCP server disabled" (case-insensitive) is the GitLab setup guide
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
    [Trait("Req", "PR-MCP-115")] // :166-168 a 404 GitLab MCP path is the setup case, not unavailable
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
    [Trait("Req", "PR-MCP-115")] // :169 a 404 at a non-GitLab OAuth upstream is unavailable(host)
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
    [Trait("Req", "PR-MCP-116")] // Services/MCP/MCPWire.swift:63-70 server headers first, then the three fixed ones, any overwritten
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
    [Trait("Req", "PR-MCP-116")] // Services/MCP/MCPWire.swift:77-84 the body is truncated to its first 2000 bytes, then trimmed
    public async Task A_non_2xx_bodys_message_is_truncated_to_2000_bytes_and_trimmed()
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

    [Fact]
    [Trait("Req", "PR-MCP-116")] // Services/MCP/MCPWire.swift:77-84 the cut is at 2000 BYTES, not characters
    public async Task A_non_2xx_bodys_message_is_cut_at_2000_bytes_not_characters_leaving_a_replacement_character()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        // The euro sign is 3 UTF-8 bytes (E2 82 AC); 667 of them is 2001 bytes, so a 2000-byte cut lands one byte
        // into the 667th character. That trailing byte must decode as U+FFFD rather than the cut moving to land on
        // a character boundary (R-STR-8, SwiftText.DecodeUtf8Repairing).
        var longBody = string.Concat(Enumerable.Repeat("€", 667));
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(500, "Internal Server Error", Encoding.UTF8.GetBytes(longBody));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        var http = Assert.IsType<MCPProbeError.Case.Http>(error.ErrorCase);
        Assert.Equal(500, http.Status);
        Assert.EndsWith("�", http.Body, StringComparison.Ordinal);
    }

    // ---- PR-MCP-117: the SSE first message --------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-117")] // Services/MCP/MCPWire.swift:92-102, :98 value["id"]?.int == id: the first "data:" line whose id matches the request, not merely the first one carrying any id (B3 change from B2's "any id wins")
    public async Task An_SSE_reply_yields_the_first_data_line_whose_id_matches_the_request()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        // The request is id 1. A frame with a different id (99) precedes the matching one: under B2's "first
        // object carrying any id wins" rule this wrong frame would be accepted; B3 must skip it and keep reading.
        var sse = "event: message\r\n" +
                   "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/x\"}\r\n\r\n" + // no id: skipped
                   "data: {\"jsonrpc\":\"2.0\",\"id\":99,\"result\":{\"serverInfo\":{\"name\":\"wrong\"}}}\r\n\r\n" + // wrong id: skipped
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
    [Trait("Req", "PR-MCP-117")] // Services/MCP/MCPWire.swift:92-102 no line carrying the requested id -> nil -> malformed(empty reply); a frame present for a different id is skipped, not accepted
    public async Task An_SSE_reply_with_no_frame_for_the_requested_id_throws_malformed_empty_reply()
    {
        using var runtime = new TestRuntime();
        await using var loop = new LoopbackHttpServer();
        var probing = MCPProbe.Probe(RemoteServer(loop.Url("/mcp")));
        var init = await loop.NextExchange();
        await init.Respond(200, "OK", Ascii(
                "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/x\"}\r\n\r\n" + // no id
                "data: {\"jsonrpc\":\"2.0\",\"id\":99,\"result\":{\"serverInfo\":{\"name\":\"wrong\"}}}\r\n\r\n"), // present but wrong id
            ("Content-Type", "text/event-stream"));
        var error = await Assert.ThrowsAsync<MCPProbeError>(() => probing.WaitAsync(Guard));
        Assert.Equal(new MCPProbeError.Case.Malformed("empty reply"), error.ErrorCase);
    }

    // ---- PR-MCP-118: the fixed GitLab setup guide -----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-118")] // :209-212
    public void IsGitLabMCP_matches_a_path_ending_in_api_v4_mcp_case_insensitively()
    {
        Assert.True(MCPProbe.IsGitLabMCP("https://gitlab.com/api/v4/mcp"));
        Assert.True(MCPProbe.IsGitLabMCP("https://gitlab.example.com/group/API/V4/MCP"));
        Assert.False(MCPProbe.IsGitLabMCP("https://gitlab.com/api/v4/other"));
        Assert.False(MCPProbe.IsGitLabMCP("not a url"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-118")] // :215-233, exact strings including U+203A; apostrophes are ASCII U+0027
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
    [Trait("Req", "PR-MCP-118")] // :217 no host -> the literal "https://gitlab.com" origin
    public void GitLabServerDisabled_falls_back_to_gitlab_dot_com_when_the_upstream_has_no_host()
    {
        var guide = MCPProbe.GitLabServerDisabled("not a url");
        Assert.Equal("https://gitlab.com/dashboard/groups", guide.PageURL);
    }

    // ---- PR-MCP-120: the remaining rendered messages, direct ------------------------------------------------

    [Fact]
    [Trait("Req", "PR-MCP-120")] // :31-32
    public void TimedOut_renders_the_fixed_message()
    {
        Assert.Equal("The server didn't answer in time.", new MCPProbeError(new MCPProbeError.Case.TimedOut()).ErrorDescription);
    }

    [Theory]
    [Trait("Req", "PR-MCP-120")] // :35-37
    [InlineData(401, "That key was refused. Press Try again and paste a new one.")]
    [InlineData(403, "That key was refused. Press Try again and paste a new one.")]
    [InlineData(404, "Nothing answered at that address. Check the address and try again.")]
    [InlineData(500, "The server answered 500.")]
    public void Http_status_renders_the_three_message_branches(int status, string expected)
    {
        Assert.Equal(expected, new MCPProbeError(new MCPProbeError.Case.Http(status, "ignored")).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-120")] // :24-30 no non-empty stderr line -> no trailing sentence
    public void Exited_with_only_blank_stderr_omits_the_trailing_sentence()
    {
        Assert.Equal("The server quit (exit 2).", new MCPProbeError(new MCPProbeError.Case.Exited(2, "\n  \n")).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-120")] // :46 malformed has no trailing full stop
    public void Malformed_has_no_trailing_full_stop()
    {
        var message = new MCPProbeError(new MCPProbeError.Case.Malformed("weird")).ErrorDescription;
        Assert.Equal("The server sent something unexpected: weird", message);
        Assert.False(message!.EndsWith('.'));
    }
}
