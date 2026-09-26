using System.Runtime.CompilerServices;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPHub.cs, the port of DroppyCode/Services/MCP/MCPHub.swift (B3 52bc7926),
// :1-377, and the records PR-MCP-177 to PR-MCP-192, PR-MCP-225 to PR-MCP-227 and PR-MCP-237 (new at B3). Every
// expectation is transcribed from the Swift source and the records, never from the C# implementation.
//
// Lane A15 (work/b3/lanes/A15.md) moved the stdio and HTTP transports onto MCPWire's shared pieces
// (MCPStdioConnection, MCPWire.Post) and deleted the hub's own WaitStdio polling loop, HubConversation and
// pipe fields; MCPWireTests.cs (lane N5) covers MCPStdioConnection's and MCPWire.Post's own mechanics end to
// end (id matching, buffered replies, the exit and timeout races, SSE framing, the notification's un-read
// event stream), including, since review finding L3a-2's fix, the Stop ladder's real timing (graceful step
// ignored, a pid-only kill at exactly 3 s, a grandchild left alive) - the earlier claim of "the stop ladder"
// here was true only of TerminationLadder.Stdio's own unit tests (ProcessLauncherLadderTests.cs, against a
// FakeProcessControl), never of MCPStdioConnection.Stop() itself, until that test existed. The tests below
// re-derive only what is observable through MCPHub itself and specific to this lane: the initialize id
// restarting at 1 on every relaunch (PR-MCP-237, new at B3, since MCPClient.nextID no longer touches the
// stdio path at all), a string-typed reply id never satisfying an int-keyed request (PR-MCP-186 b3_note), the
// literal "quit (exit N)" message reaching the hub's caller, Track/MCPLiveProcesses now carrying a
// StdioProcess (ED-194), a server's custom header surviving or not surviving a redirect (PR-MCP-188/OD-0022,
// ED-208, review finding L3a-1), and stop()'s own http-side effects (PR-MCP-190, review finding L3a-2).
//
// The stdio path is exercised against a real child process, powershell.exe running
// Fixtures/mcp-stdio-fixture.ps1 (R-TEST-10: a system binary and a scripted stand-in, never a real AI CLI).
// The http path is exercised against tests/Marrow.Testing's LoopbackHttpServer, never the internet.
//
// NOT exercised end to end here, out of this lane's scope: MCPHub.tools()/call()'s own MCPPaths.claudeConfigURL
// resolution (:127) through the real Storage.Root ambient (Services.Store.Storage is implemented now, ED-177,
// unlike when this note was first written). MCPHub carries an internal test seam, Seed(...), that sets the
// actor's private specs map directly, the same shape as LoginEnvironment.Which's tested seam (Shell.cs:701);
// production code never calls it, only ReadSpecs() (the parameterless overload, which does call
// MCPPaths.ClaudeConfigURL) does. ReadSpecs(string) itself, the file-reading and parsing logic PR-MCP-181
// actually describes, is tested directly below against real files.
public sealed class MCPHubTests
{
    private static string FixtureScript([CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "..", "Fixtures", "mcp-stdio-fixture.ps1");

    private static MCPServerSpec.Stdio StdioFixture(IReadOnlyDictionary<string, string>? env = null) =>
        new("powershell",
            new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", FixtureScript()]),
            new ValueMap<string, string>(env ?? new Dictionary<string, string>(), SwiftStringComparer.Instance));

    private static JSONValue Json(string key, string value) => new JSONValue.Object(
        new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { [key] = new JSONValue.String(value) },
            SwiftStringComparer.Instance));

    private static Dictionary<string, MCPServerSpec> OneServer(string id, MCPServerSpec spec) =>
        new(SwiftStringComparer.Instance) { [id] = spec };

    // ---- PR-MCP-192: MCPHubError renders two forms ----

    [Fact]
    [Trait("Req", "PR-MCP-192")] // Services/MCP/MCPHub.swift:8-11
    public void UnknownTool_description_is_Unknown_tool_colon_the_name_period()
    {
        Assert.Equal("Unknown tool: mcp__x__y.", MCPHubError.UnknownTool("mcp__x__y").ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-192")] // Services/MCP/MCPHub.swift:12-13
    public void Server_description_is_the_message_verbatim()
    {
        Assert.Equal("The server quit.", MCPHubError.Server("The server quit.").ErrorDescription);
    }

    // ---- PR-MCP-178: mcp__ + server + __ + name, and the first "__" wins on parse ----

    [Fact]
    [Trait("Req", "PR-MCP-178")] // Services/MCP/MCPHub.swift:38, :109-117
    public void CallName_is_mcp_double_underscore_server_double_underscore_name()
    {
        var tool = new MCPHub.Tool("github", "search_issues", "", new JSONValue.Null());
        Assert.Equal("mcp__github__search_issues", tool.CallName);
    }

    [Fact]
    [Trait("Req", "PR-MCP-178")] // Services/MCP/MCPHub.swift:109-117
    public void Parse_splits_a_well_formed_call_name()
    {
        Assert.Equal(("github", "search_issues"), MCPHub.Parse("mcp__github__search_issues"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-178")] // Services/MCP/MCPHub.swift:112 rest.range(of: "__") finds the FIRST occurrence
    public void Parse_uses_only_the_first_double_underscore_so_a_tool_name_may_contain_one()
    {
        Assert.Equal(("srv", "too__l"), MCPHub.Parse("mcp__srv__too__l"));
    }

    [Theory]
    [Trait("Req", "PR-MCP-178")] // Services/MCP/MCPHub.swift:110-116
    [InlineData("github__search_issues")] // no mcp__ prefix
    [InlineData("mcp__no_separator")] // no "__" in the remainder
    [InlineData("mcp____search_issues")] // empty server
    [InlineData("mcp__github__")] // empty tool
    public void Parse_returns_null_when_the_grammar_is_not_met(string callName)
    {
        Assert.Null(MCPHub.Parse(callName));
    }

    // ---- PR-MCP-181: readSpecs prefers command over url and drops non-string values ----

    private static string TempConfig(string json)
    {
        var dir = Path.Join(Path.GetTempPath(), "marrow-mcphub-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Join(dir, "claude.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    [Trait("Req", "PR-MCP-181")] // Services/MCP/MCPHub.swift:127-130
    public void ReadSpecs_of_a_missing_file_is_empty()
    {
        var path = Path.Join(Path.GetTempPath(), "marrow-mcphub-tests-missing-" + Guid.NewGuid().ToString("N") + ".json");
        Assert.Empty(MCPHub.ReadSpecs(path));
    }

    [Fact]
    [Trait("Req", "PR-MCP-181")] // Services/MCP/MCPHub.swift:127-130
    public void ReadSpecs_of_unparseable_JSON_is_empty()
    {
        Assert.Empty(MCPHub.ReadSpecs(TempConfig("not json")));
    }

    [Fact]
    [Trait("Req", "PR-MCP-181")] // Services/MCP/MCPHub.swift:129-130
    public void ReadSpecs_with_no_mcpServers_object_is_empty()
    {
        Assert.Empty(MCPHub.ReadSpecs(TempConfig("""{"other": 1}""")));
    }

    [Fact]
    [Trait("Req", "PR-MCP-181")] // Services/MCP/MCPHub.swift:133-139
    public void ReadSpecs_prefers_command_over_url_when_both_are_present()
    {
        var specs = MCPHub.ReadSpecs(TempConfig("""
            {"mcpServers": {"s": {"command": "notes-server", "url": "https://example.com/mcp"}}}
            """));

        var stdio = Assert.IsType<MCPServerSpec.Stdio>(specs["s"]);
        Assert.Equal("notes-server", stdio.Command);
    }

    [Fact]
    [Trait("Req", "PR-MCP-181")] // Services/MCP/MCPHub.swift:133-139
    public void ReadSpecs_reads_the_stdio_command_args_and_env_dropping_non_string_values()
    {
        var specs = MCPHub.ReadSpecs(TempConfig("""
            {"mcpServers": {"s": {"command": "notes-server", "args": ["--stdio", 1, "--verbose"],
              "env": {"A": "1", "B": 2, "C": true}}}}
            """));

        var stdio = Assert.IsType<MCPServerSpec.Stdio>(specs["s"]);
        Assert.Equal(["--stdio", "--verbose"], stdio.Args);
        Assert.Equal(new Dictionary<string, string> { ["A"] = "1" }, stdio.Env);
    }

    [Fact]
    [Trait("Req", "PR-MCP-181")] // Services/MCP/MCPHub.swift:140-144
    public void ReadSpecs_reads_the_url_and_headers_when_there_is_no_command()
    {
        var specs = MCPHub.ReadSpecs(TempConfig("""
            {"mcpServers": {"s": {"url": "https://example.com/mcp",
              "headers": {"Authorization": "Bearer x", "N": 1}}}}
            """));

        var http = Assert.IsType<MCPServerSpec.Http>(specs["s"]);
        Assert.Equal("https://example.com/mcp", http.Url);
        Assert.Equal(new Dictionary<string, string> { ["Authorization"] = "Bearer x" }, http.Headers);
    }

    [Fact]
    [Trait("Req", "PR-MCP-181")] // Services/MCP/MCPHub.swift:133-146
    public void ReadSpecs_skips_a_member_with_neither_a_nonempty_command_nor_a_nonempty_url()
    {
        var specs = MCPHub.ReadSpecs(TempConfig("""
            {"mcpServers": {"empty-command": {"command": ""}, "empty-url": {"url": ""}, "neither": {"other": 1}}}
            """));

        Assert.Empty(specs);
    }

    // ---- PR-MCP-177, PR-MCP-182: tools() lists, sorts, caches and caps descriptions ----

    [Fact]
    [Trait("Req", "PR-MCP-177")] // Services/MCP/MCPHub.swift:51-77
    [Trait("Req", "PR-MCP-182")] // Services/MCP/MCPHub.swift:193-221
    public async Task Tools_paginates_caps_descriptions_at_400_and_falls_back_to_an_empty_object_schema()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        var tools = await hub.Tools();

        Assert.Equal(3, tools.Count);
        var echo = tools.Single(t => t.Name == "echo");
        Assert.Equal(400, echo.Description.Length);
        Assert.Equal("s", echo.Server);
        Assert.IsType<JSONValue.Object>(echo.InputSchema);

        var blank = tools.Single(t => t.Name == "blank");
        Assert.Equal("", blank.Description); // :203 description ?? ""
        Assert.Equal(
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["type"] = new JSONValue.String("object"),
                ["properties"] = new JSONValue.Object(ValueMap<string, JSONValue>.Empty),
            }, SwiftStringComparer.Instance)),
            blank.InputSchema); // :207-208 the literal fallback schema

        Assert.Contains(tools, t => t.Name == "second-page"); // :216-219 nextCursor "page-2" fetched a second page
    }

    [Fact]
    [Trait("Req", "PR-MCP-177")] // Services/MCP/MCPHub.swift:72-76
    public async Task Tools_sorts_by_server_then_by_name()
    {
        var hub = new MCPHub();
        var env = new Dictionary<string, string> { ["MCP_FIXTURE_TOOL"] = "zzz" };
        await hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
        {
            ["b-server"] = StdioFixture(env),
            ["a-server"] = StdioFixture(env),
        });

        var tools = await hub.Tools();

        Assert.Equal(["a-server", "b-server"], tools.Select(t => t.Server).ToList());
    }

    [Fact]
    [Trait("Req", "PR-MCP-177")] // Services/MCP/MCPHub.swift:59-60, :67-70
    public async Task Tools_blacklists_a_server_whose_listing_failed_and_never_retries_it()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("bad", new MCPServerSpec.Stdio("marrow-mcphub-tests-no-such-binary",
            ValueList<string>.Empty, ValueMap<string, string>.Empty)));

        var first = await hub.Tools();
        var second = await hub.Tools();

        Assert.Empty(first); // :60 failed.contains(id) skips it, so a failing server contributes nothing
        Assert.Empty(second);
    }

    // ---- PR-MCP-179, PR-MCP-183: call() routes and flattens content; the asymmetry with tools() ----

    [Fact]
    [Trait("Req", "PR-MCP-183")] // Services/MCP/MCPHub.swift:230-239
    public async Task Call_flattens_text_image_and_resource_content_in_order()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        var result = await hub.Call("mcp__s__mixed-content", new JSONValue.Null());

        Assert.Equal("first line\n[image]\n[resource: file:///r.txt]\n[resource: ]", result.Text);
        Assert.False(result.IsError);
    }

    [Fact]
    [Trait("Req", "PR-MCP-183")] // Services/MCP/MCPHub.swift:243
    public async Task Call_reports_isError_from_the_result()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        var result = await hub.Call("mcp__s__error-tool", new JSONValue.Null());

        Assert.True(result.IsError);
    }

    [Fact]
    [Trait("Req", "PR-MCP-179")] // Services/MCP/MCPHub.swift:81-83
    public async Task Call_of_an_unparseable_or_unknown_server_name_throws_unknownTool()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        var error = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("not-mcp", new JSONValue.Null()));
        Assert.Equal("Unknown tool: not-mcp.", error.ErrorDescription);

        var error2 = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__unknown-server__x", new JSONValue.Null()));
        Assert.Equal("Unknown tool: mcp__unknown-server__x.", error2.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-179")] // Services/MCP/MCPHub.swift:84-87
    public async Task Call_of_a_tool_absent_from_a_cached_list_throws_unknownTool()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));
        await hub.Tools(); // populates the tool cache with "echo", "blank", "second-page"

        var error = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__nonexistent", new JSONValue.Null()));
        Assert.Equal("Unknown tool: mcp__s__nonexistent.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-179")] // Services/MCP/MCPHub.swift:84-87 the guard is skipped when no cache entry exists
    public async Task Call_of_a_server_with_no_cached_list_is_not_checked_against_a_tool_name_and_relaunches()
    {
        // The asymmetry: a server tools() never listed (so toolCache has no entry for it) is not rejected by
        // call()'s cache guard; the client is created fresh and the call reaches the server.
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        var result = await hub.Call("mcp__s__anything-goes", Json("k", "v"));

        Assert.Contains("echo:", result.Text, StringComparison.Ordinal);
    }

    // ---- PR-MCP-180: reload drops every client, cache and failure ----

    [Fact]
    [Trait("Req", "PR-MCP-180")] // Services/MCP/MCPHub.swift:100-107
    public async Task Reload_clears_the_tool_cache_so_a_changed_spec_is_reread()
    {
        var hub = new MCPHub();
        var envAlpha = new Dictionary<string, string> { ["MCP_FIXTURE_TOOL"] = "alpha" };
        var envBeta = new Dictionary<string, string> { ["MCP_FIXTURE_TOOL"] = "beta" };
        await hub.Seed(OneServer("s", StdioFixture(envAlpha)));

        var before = await hub.Tools();
        Assert.Equal(["alpha"], before.Select(t => t.Name).ToList());

        await hub.Reload();
        await hub.Seed(OneServer("s", StdioFixture(envBeta)));
        var after = await hub.Tools();

        // Had the cache survived reload, "alpha" (the old client's cached listing) would still come back.
        Assert.Equal(["beta"], after.Select(t => t.Name).ToList());
    }

    // ---- PR-MCP-184: a stdio client restarts once, then is permanently dead ----

    [Fact]
    [Trait("Req", "PR-MCP-184")] // Services/MCP/MCPHub.swift:266-277 ensureStdio (B3)
    public async Task A_stdio_client_restarts_once_after_the_process_quits_then_stays_dead()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        // First call succeeds and leaves a healthy process.
        var first = await hub.Call("mcp__s__anything", new JSONValue.Null());
        Assert.Contains("echo:", first.Text, StringComparison.Ordinal);

        // Kill the child; the next call (:268 connection.process.isRunning is now false) uses the one allowed
        // restart (:269-274 didRestart) and succeeds again against a freshly launched process. PR-MCP-186's
        // b3_note: the exit is always reported with a code by MCPStdioConnection, so the literal message is
        // the "exit (N)" form, never the bare "The server quit." fallback (:258-259).
        var firstExit = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__exit-server", new JSONValue.Null()));
        Assert.Equal("The server quit (exit 7).", firstExit.ErrorDescription);
        var second = await hub.Call("mcp__s__anything", new JSONValue.Null());
        Assert.Contains("echo:", second.Text, StringComparison.Ordinal);

        // Kill it again; the restart was already spent, so ensureStdio marks the client dead and every
        // further call fails at once with the literal message, no relaunch attempted (:270-273).
        await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__exit-server", new JSONValue.Null()));
        var error = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__anything", new JSONValue.Null()));
        Assert.Equal("The server quit.", error.ErrorDescription);
    }

    // ---- PR-MCP-237 (new at B3): a relaunched connection's ids restart at 1 ----

    [Fact]
    [Trait("Req", "PR-MCP-237")] // Services/MCP/MCPHub.swift:279-295; MCPWire.swift:126,164-168 (B3)
    public async Task A_relaunched_stdio_connections_initialize_carries_id_1_again()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        // First launch: initialize is id 1, so the first tools/call ("which-id") is id 2.
        var firstId = await hub.Call("mcp__s__which-id", new JSONValue.Null());
        Assert.Equal("id:2", firstId.Text);

        // Kill the child (id 3) and use the one allowed restart. A NEW MCPStdioConnection is created, whose
        // own counter starts at 1 again (:164-168 in MCPWire.swift): the relaunch's initialize is id 1 and
        // its first tools/call is id 2 again, exactly like the first launch, never continuing at 4/5.
        await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__exit-server", new JSONValue.Null()));
        var secondId = await hub.Call("mcp__s__which-id", new JSONValue.Null());
        Assert.Equal("id:2", secondId.Text);
    }

    // ---- PR-MCP-192, PR-MCP-185: the literal launch-failure and exit messages ----

    [Fact]
    [Trait("Req", "PR-MCP-185")] // Services/MCP/MCPHub.swift:280-281 (B3)
    public async Task An_unresolvable_command_throws_the_isnt_installed_message()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Stdio("marrow-mcphub-tests-no-such-binary",
            ValueList<string>.Empty, ValueMap<string, string>.Empty)));

        var error = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__x", new JSONValue.Null()));

        Assert.Equal("marrow-mcphub-tests-no-such-binary isn't installed.", error.ErrorDescription);
    }

    // ---- PR-MCP-186: replies for other ids are kept, so two concurrent calls each get their own answer ----

    [Fact]
    [Trait("Req", "PR-MCP-186")] // Services/MCP/MCPHub.swift:243-264 (B3); MCPWire.swift:198-228,329-344 (B3)
    public async Task Two_concurrent_calls_on_one_server_each_receive_their_own_reply()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));
        await hub.Call("mcp__s__warm-up", new JSONValue.Null()); // completes the handshake before the race below

        var callA = hub.Call("mcp__s__tool-a", Json("k", "a"));
        var callB = hub.Call("mcp__s__tool-b", Json("k", "b"));
        var results = await Task.WhenAll(callA, callB);

        // The fixture echoes back the arguments it received; the value (not the tool name) proves which reply
        // matched which request.
        Assert.Contains("\"a\"", results[0].Text, StringComparison.Ordinal);
        Assert.Contains("\"b\"", results[1].Text, StringComparison.Ordinal);
    }

    // ---- PR-MCP-186 b3_note: a string-typed reply id no longer matches an int-keyed request ----

    [Fact]
    [Trait("Req", "PR-MCP-186")] // MCPWire.swift:219-228,334 RPCID.Create/Handle distinguish String from Int (B3)
    public async Task A_reply_whose_id_is_a_JSON_string_never_satisfies_the_waiting_request()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        // The fixture answers this request twice for the same id: first with the digits as a JSON string
        // (which RPCID.Create parses as RPCID.String, never equal to the RPCID.Int the request is keyed
        // under), then, after a short delay, with the real numeric-id reply. If the string-id reply had
        // wrongly satisfied the wait, the result would be "WRONG-STRING-ID" instead of the echoed arguments.
        var result = await hub.Call("mcp__s__string-id-then-real", Json("k", "real"));

        Assert.Contains("\"real\"", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("WRONG-STRING-ID", result.Text, StringComparison.Ordinal);
    }

    // ---- PR-MCP-190: stop (here, from a failed tools/list) forgets the session but keeps counting ----

    [Fact]
    [Trait("Req", "PR-MCP-190")] // Services/MCP/MCPHub.swift:173-178 stop() (B3)
    public async Task A_stop_triggered_by_a_failed_listing_forgets_the_session_but_the_id_counter_keeps_counting()
    {
        // Review finding L3a-2: PR-MCP-190 had no test of its own anywhere; this is the http-side half (the
        // stdio-side half - the stop ladder itself, wired all the way from this same MCPClient.Stop() call - is
        // MCPWireTests.Stop_ignores_the_graceful_step..._leaving_a_grandchild_alive). Tools() failing to list a
        // server's tools is one of stop()'s three real callers (the other two are a failed handshake and
        // reload()); the failure here leaves the client in MCPHub.clients (only toolCache/failed record it), so
        // a later Call reuses THIS SAME client instance, not a fresh one, and can observe what stop() left behind.
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty)));

        var serverTask = Task.Run(async () =>
        {
            // Tools(): initialize (id 1) succeeds and adopts a session id; tools/list (id 2) answers an error,
            // which ListTools/checkReply throws - Tools()'s catch runs stop(): :176 initialized = false,
            // :177 sessionID = nil. nextID (already at 3) is untouched: stop() never assigns it (:174-177).
            var init = await server.NextExchange();
            await init.Respond(200, "OK", """{"jsonrpc":"2.0","id":1,"result":{}}"""u8.ToArray(),
                ("Content-Type", "application/json"), ("Mcp-Session-Id", "sess-1"));
            var notified = await server.NextExchange();
            await notified.Respond(202, "Accepted", []);
            var list = await server.NextExchange();
            await list.Respond(200, "OK",
                """{"jsonrpc":"2.0","id":2,"error":{"message":"boom"}}"""u8.ToArray(),
                ("Content-Type", "application/json"));

            // Call(): HttpRpc sees initialized == false and re-handshakes with the counter's NEXT id (3, not 1
            // again), and this initialize carries no Mcp-Session-Id header, because stop() cleared it.
            var init2 = await server.NextExchange();
            Assert.Empty(init2.HeaderValues("Mcp-Session-Id"));
            Assert.Equal(3L, System.Text.Json.JsonDocument.Parse(init2.Body).RootElement.GetProperty("id").GetInt64());
            await init2.Respond(200, "OK", """{"jsonrpc":"2.0","id":3,"result":{}}"""u8.ToArray(),
                ("Content-Type", "application/json"));
            var notified2 = await server.NextExchange();
            await notified2.Respond(202, "Accepted", []);
            var call = await server.NextExchange();
            Assert.Equal(4L, System.Text.Json.JsonDocument.Parse(call.Body).RootElement.GetProperty("id").GetInt64());
            await call.Respond(200, "OK",
                """{"jsonrpc":"2.0","id":4,"result":{"content":[{"type":"text","text":"ok"}]}}"""u8.ToArray(),
                ("Content-Type", "application/json"));
        });

        var listed = await hub.Tools(); // swallowed: :67-71 catches, adds "s" to failed, no throw
        Assert.Empty(listed);

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null());

        await serverTask;
        Assert.Equal("ok", result.Text);
    }

    // ---- PR-MCP-187, PR-MCP-188, PR-MCP-189, PR-MCP-225, PR-MCP-226: the http transport ----

    [Fact]
    [Trait("Req", "PR-MCP-187")] // Services/MCP/MCPHub.swift:422-453
    [Trait("Req", "PR-MCP-188")] // Services/MCP/MCPHub.swift:455-479
    [Trait("Req", "PR-MCP-226")] // Services/MCP/MCPHub.swift:429 initialize, 60s (not measured; the call site)
    public async Task Http_client_initializes_once_sends_the_four_headers_and_adopts_the_session_id()
    {
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty)));

        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            Assert.Equal("application/json", Single(init, "Content-Type"));
            Assert.Equal("application/json, text/event-stream", Single(init, "Accept"));
            Assert.Equal("2025-06-18", Single(init, "MCP-Protocol-Version"));
            Assert.Empty(init.HeaderValues("Mcp-Session-Id")); // :463 only when one is already held
            await init.Respond(200, "OK", """{"jsonrpc":"2.0","id":1,"result":{}}"""u8.ToArray(),
                ("Content-Type", "application/json"), ("Mcp-Session-Id", "sess-1"));

            var notified = await server.NextExchange();
            Assert.Equal("notifications/initialized",
                System.Text.Json.JsonDocument.Parse(notified.Body).RootElement.GetProperty("method").GetString());
            await notified.Respond(202, "Accepted", []);

            var call = await server.NextExchange();
            Assert.Equal(["sess-1"], call.HeaderValues("Mcp-Session-Id")); // :463 held from the initialize reply
            await call.Respond(200, "OK",
                """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"ok"}]}}"""u8.ToArray(),
                ("Content-Type", "application/json"));
        });

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null());

        await serverTask;
        Assert.Equal("ok", result.Text);
    }

    [Fact]
    [Trait("Req", "PR-MCP-188")] // Services/MCP/MCPHub.swift:470-471
    public async Task Http_client_reports_a_non_2xx_status_by_number_alone_without_the_body()
    {
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty)));

        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            await init.Respond(500, "Internal Server Error", "the body text nobody should see"u8.ToArray());
        });

        var error = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__anything", new JSONValue.Null()));

        await serverTask;
        Assert.Equal("The server answered 500.", error.ErrorDescription);
        Assert.DoesNotContain("nobody", error.ErrorDescription, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-MCP-187")] // Services/MCP/MCPHub.swift:431
    public async Task Http_client_reports_an_empty_body_as_the_empty_reply_message()
    {
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty)));

        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            await init.Respond(200, "OK", [], ("Content-Type", "application/json"));
        });

        var error = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__anything", new JSONValue.Null()));

        await serverTask;
        Assert.Equal("The server sent something unexpected: empty reply.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-MCP-227")] // Services/MCP/MCPHub.swift:439-443
    public async Task A_failed_notifications_initialized_post_is_swallowed_and_the_client_stays_initialized()
    {
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty)));

        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            await init.Respond(200, "OK", """{"jsonrpc":"2.0","id":1,"result":{}}"""u8.ToArray(),
                ("Content-Type", "application/json"));

            var notified = await server.NextExchange();
            await notified.Respond(503, "Service Unavailable", []); // :441-442 caught and swallowed

            var call = await server.NextExchange(); // the real request still goes out (:438 initialized was set first)
            await call.Respond(200, "OK",
                """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"ok"}]}}"""u8.ToArray(),
                ("Content-Type", "application/json"));
        });

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null());

        await serverTask;
        Assert.Equal("ok", result.Text);
    }

    [Fact]
    [Trait("Req", "PR-MCP-188")] // Services/MCP/MCPWire.swift:85-101 (B3), reached through MCPHub.post (:343-352)
    public async Task Http_client_reads_the_first_usable_SSE_message()
    {
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty)));

        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            var sse = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n\n"u8.ToArray();
            await init.Respond(200, "OK", sse, ("Content-Type", "text/event-stream"));

            var notified = await server.NextExchange();
            await notified.Respond(202, "Accepted", []);

            var call = await server.NextExchange();
            // The real call's own id is 2: initialize consumed 1 and the notification consumes none (it posts
            // with id: nil), so the next request nextID hands out is 2, not 3 (PR-MCP-237's http-side sibling:
            // MCPHub.swift:315-316,333-334 is the only place nextID still counts, unchanged by this lane).
            var reply = "data: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"sse-ok\"}]}}\n\n"u8.ToArray();
            await call.Respond(200, "OK", reply, ("Content-Type", "text/event-stream"));
        });

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null());

        await serverTask;
        Assert.Equal("sse-ok", result.Text);
    }

    // ---- PR-MCP-188: SSE replies are matched by id, so a frame for another id in the stream is skipped ----

    [Fact]
    [Trait("Req", "PR-MCP-188")] // Services/MCP/MCPWire.swift:93-101 (B3): the loop skips a frame whose id mismatches
    public async Task Http_client_skips_an_SSE_frame_for_another_id_and_returns_the_matching_one()
    {
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty)));

        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n\n"u8.ToArray();
            await init.Respond(200, "OK", sse, ("Content-Type", "text/event-stream"));

            var notified = await server.NextExchange();
            await notified.Respond(202, "Accepted", []);

            var call = await server.NextExchange();
            // The real call's own id is 2 (see the sibling test above). A decoy frame for a foreign id (99, no
            // request of ours ever carries it) precedes it: MCPWire.Post's line loop must skip the decoy rather
            // than returning it or stopping at it.
            var replyBytes = Encoding.UTF8.GetBytes(
                "data: {\"jsonrpc\":\"2.0\",\"id\":99,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"decoy\"}]}}\n\n" +
                "data: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"real\"}]}}\n\n");
            await call.Respond(200, "OK", replyBytes, ("Content-Type", "text/event-stream"));
        });

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null());

        await serverTask;
        Assert.Equal("real", result.Text);
    }

    // ---- PR-MCP-188/PR-MCP-117: a notification post never reads a stream the server holds open ----
    // (review finding L3a-2: this section previously carried an "OD-0022 note" label, but neither test below
    // exercises a redirect; the OD-0022/ED-208 header-dropping coverage is the pair of tests right after this one.)

    [Fact]
    [Trait("Req", "PR-MCP-188")] // Services/MCP/MCPWire.swift:92 (B3): "guard let id else { return ... }" - no read
    public async Task Http_client_does_not_hang_when_the_notification_reply_is_a_held_open_event_stream()
    {
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(), ValueMap<string, string>.Empty)));

        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            await init.Respond(200, "OK", """{"jsonrpc":"2.0","id":1,"result":{}}"""u8.ToArray(),
                ("Content-Type", "application/json"));

            // The notification's reply announces an event stream and never sends a terminating chunk: a
            // caller that tried to read this body (the way a normal request's SSE reply is read) would hang
            // forever. MCPWire.Post's notification branch (id is null) must release the stream instead.
            var notified = await server.NextExchange();
            await notified.WriteChunkedHead(200, "OK", ("Content-Type", "text/event-stream"));
            // Deliberately never call EndChunks/Finish: the stream stays open until the test disposes the server.

            var call = await server.NextExchange();
            await call.Respond(200, "OK",
                """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"ok"}]}}"""u8.ToArray(),
                ("Content-Type", "application/json"));
        });

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null()).WaitAsync(LoopbackHttpServer.Guard);

        await serverTask.WaitAsync(LoopbackHttpServer.Guard);
        Assert.Equal("ok", result.Text);
    }

    private static string Single(LoopbackExchange exchange, string header) => Assert.Single(exchange.HeaderValues(header));

    // ---- PR-MCP-188 (OD-0022, enforced by ED-208): a server's custom header follows a same-origin redirect and
    // is dropped the moment scheme, host or port changes, matching Authorization (ED-152). Review finding L3a-1:
    // this was tracked as an unresolved gap (MCPWire.cs, MCPHub.cs had no marker at all) with no test covering a
    // redirect; ED-208 (Marrow.Core.Port.Http.cs, URLRequest.MarkOriginBound/Hop.OriginBoundFields) closes it, and
    // MCPWire.Post (:90) now marks every one of this call's caller-supplied `headers` before sending.

    [Fact]
    [Trait("Req", "PR-MCP-188")] // Services/MCP/MCPWire.swift:66 (B3); DEVIATION OD-0022, enforced by ED-208
    [Trait("Decision", "ED-208")]
    public async Task Http_client_keeps_the_servers_custom_header_across_a_same_origin_redirect()
    {
        await using var server = new LoopbackHttpServer();
        var hub = new MCPHub();
        var headers = new Dictionary<string, string> { ["X-Api-Key"] = "secret-key" };
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(),
            new ValueMap<string, string>(headers, SwiftStringComparer.Instance))));

        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            Assert.Equal(["secret-key"], init.HeaderValues("X-Api-Key"));
            await init.Respond(307, "Temporary Redirect", [], ("Location", "/mcp2")); // same server: same origin

            var init2 = await server.NextExchange();
            Assert.Equal("/mcp2", init2.Target);
            Assert.Equal(["secret-key"], init2.HeaderValues("X-Api-Key")); // kept: no origin change
            await init2.Respond(200, "OK", """{"jsonrpc":"2.0","id":1,"result":{}}"""u8.ToArray(),
                ("Content-Type", "application/json"));

            var notified = await server.NextExchange();
            await notified.Respond(202, "Accepted", []);

            var call = await server.NextExchange();
            await call.Respond(200, "OK",
                """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"ok"}]}}"""u8.ToArray(),
                ("Content-Type", "application/json"));
        });

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null());

        await serverTask;
        Assert.Equal("ok", result.Text);
    }

    [Fact]
    [Trait("Req", "PR-MCP-188")] // Services/MCP/MCPWire.swift:66,72 (B3); DEVIATION OD-0022, enforced by ED-208
    [Trait("Decision", "ED-208")]
    public async Task Http_client_drops_the_servers_custom_header_across_a_cross_origin_redirect()
    {
        await using var server = new LoopbackHttpServer();
        await using var other = new LoopbackHttpServer();
        var hub = new MCPHub();
        var headers = new Dictionary<string, string> { ["X-Api-Key"] = "secret-key" };
        await hub.Seed(OneServer("s", new MCPServerSpec.Http(server.Url("/mcp").ToString(),
            new ValueMap<string, string>(headers, SwiftStringComparer.Instance))));

        // Every POST this client makes targets the same configured URL (MCPHub.swift:317, :328, :335 all read
        // `url`, never a redirect target from an earlier leg), so each of the three legs below - initialize,
        // notifications/initialized, the real call - redirects from `server` to `other` in turn.
        var serverTask = Task.Run(async () =>
        {
            var init = await server.NextExchange();
            Assert.Equal(["secret-key"], init.HeaderValues("X-Api-Key")); // set before the redirect is seen
            await init.Respond(307, "Temporary Redirect", [], ("Location", other.Url("/init2").AbsoluteUri));

            var notified = await server.NextExchange();
            await notified.Respond(307, "Temporary Redirect", [], ("Location", other.Url("/notify2").AbsoluteUri));

            var call = await server.NextExchange();
            await call.Respond(307, "Temporary Redirect", [], ("Location", other.Url("/call2").AbsoluteUri));
        });

        var otherTask = Task.Run(async () =>
        {
            var init2 = await other.NextExchange();
            Assert.Equal("/init2", init2.Target);
            Assert.Empty(init2.HeaderValues("X-Api-Key")); // OD-0022/ED-208: dropped, the origin changed
            await init2.Respond(200, "OK", """{"jsonrpc":"2.0","id":1,"result":{}}"""u8.ToArray(),
                ("Content-Type", "application/json"));

            var notify2 = await other.NextExchange();
            Assert.Empty(notify2.HeaderValues("X-Api-Key"));
            await notify2.Respond(202, "Accepted", []);

            var call2 = await other.NextExchange();
            Assert.Empty(call2.HeaderValues("X-Api-Key"));
            await call2.Respond(200, "OK",
                """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"ok"}]}}"""u8.ToArray(),
                ("Content-Type", "application/json"));
        });

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null());

        await serverTask;
        await otherTask;
        Assert.Equal("ok", result.Text);
    }

    // ---- PR-MCP-191: every server process started this run is terminated at quit ----

    [Fact]
    [Trait("Req", "PR-MCP-191")] // Services/MCP/MCPHub.swift:540-545
    public void MCPLiveProcesses_prunes_already_exited_entries_when_a_new_one_is_added()
    {
        var live = new MCPLiveProcesses();
        var exited = FakeProcessControl.Exited("exited");
        var running = new FakeProcessControl("running");
        live.Add(exited);

        live.Add(running); // :542 processes.removeAll { !$0.isRunning } runs before the append

        live.TerminateAll();
        // Pruned from the list on Add (which reads IsRunning to filter), so TerminateAll never reaches it.
        Assert.DoesNotContain(exited.Calls, call => call is "terminate" or "killTree" or "killProcess");
        Assert.Contains("terminate", running.Calls);
    }

    [Fact]
    [Trait("Req", "PR-MCP-191")] // Services/MCP/MCPHub.swift:547-556
    public void MCPLiveProcesses_terminates_then_kills_every_still_running_process()
    {
        var live = new MCPLiveProcesses();
        var terminates = new FakeProcessControl("terminates") { ExitsOnTerminate = true };
        var survives = new FakeProcessControl("survives");
        live.Add(terminates);
        live.Add(survives);

        live.TerminateAll();

        Assert.Equal(["terminate"], terminates.Calls.Where(c => c is "terminate" or "killTree" or "killProcess").ToList());
        Assert.Equal(["terminate", "killTree"], survives.Calls.Where(c => c is "terminate" or "killTree" or "killProcess").ToList());
    }

    [Fact]
    [Trait("Req", "PR-MCP-191")] // Services/MCP/MCPHub.swift:26, :30 (B3: Track now takes a StdioProcess, ED-194)
    public async Task Track_and_TerminateAll_delegate_to_the_shared_live_process_list()
    {
        // PORT-ONLY (ED-194): Track's own parameter type changed from IProcessControl to StdioProcess, so this
        // coverage moves from a FakeProcessControl to a real child (R-TEST-10: powershell.exe, never a real AI
        // CLI) that only exits when told to. MCPLiveProcesses' own pruning/termination-order logic is still
        // covered directly above against FakeProcessControl, decoupled from any StdioProcess.
        var process = new StdioProcess(ACPSessionFixtures.PowerShell,
            ACPSessionFixtures.PowerShellEncoded("$i=[Console]::In; while(($l=$i.ReadLine()) -ne $null){}"),
            LoginEnvironment.HomeDirectory, LoginEnvironment.Current);
        process.Start();
        try
        {
            MCPHub.Track(process);

            MCPHub.TerminateAll();

            var status = await process.WaitForExit().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(process.IsRunning);
            _ = status;
        }
        finally
        {
            if (process.IsRunning) process.Terminate();
        }
    }
}
