using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPHub.cs, the port of DroppyCode/Services/MCP/MCPHub.swift (B2 1fbf81e),
// :1-558, and the records PR-MCP-177 to PR-MCP-192 and PR-MCP-225 to PR-MCP-227. Every expectation is
// transcribed from the Swift source and the records, never from the C# implementation.
//
// The stdio path is exercised against a real child process, powershell.exe running
// Fixtures/mcp-stdio-fixture.ps1 (R-TEST-10: a system binary and a scripted stand-in, never a real AI CLI).
// The http path is exercised against tests/Marrow.Testing's LoopbackHttpServer, never the internet.
//
// UNTESTABLE, by design: MCPHub.tools()/call()'s own MCPPaths.claudeConfigURL resolution (:127) is not
// exercised end to end, because MCPPaths (Services/Models/MCP.cs) still throws MARROW_STUB pending
// Services/Store/Storage.swift (work/packets4/Services.Store.Storage, not yet built). MCPHub carries an
// internal test seam, Seed(...), that sets the actor's private specs map directly, the same shape as
// LoginEnvironment.Which's tested seam (Shell.cs:701); production code never calls it, only ReadSpecs()
// (the parameterless overload, which does call MCPPaths.ClaudeConfigURL) does. ReadSpecs(string) itself, the
// file-reading and parsing logic PR-MCP-181 actually describes, is tested directly below against real files.
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
    [Trait("Req", "PR-MCP-184")] // Services/MCP/MCPHub.swift:312-323
    public async Task A_stdio_client_restarts_once_after_the_process_quits_then_stays_dead()
    {
        var hub = new MCPHub();
        await hub.Seed(OneServer("s", StdioFixture()));

        // First call succeeds and leaves a healthy process.
        var first = await hub.Call("mcp__s__anything", new JSONValue.Null());
        Assert.Contains("echo:", first.Text, StringComparison.Ordinal);

        // Kill the child; the next call (:314 proc.isRunning is now false) uses the one allowed restart
        // (:316-321 didRestart) and succeeds again against a freshly launched process.
        await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__exit-server", new JSONValue.Null()));
        var second = await hub.Call("mcp__s__anything", new JSONValue.Null());
        Assert.Contains("echo:", second.Text, StringComparison.Ordinal);

        // Kill it again; the restart was already spent, so ensureStdio marks the client dead and every
        // further call fails at once with the literal message, no relaunch attempted (:317-319).
        await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__exit-server", new JSONValue.Null()));
        var error = await Assert.ThrowsAsync<MCPHubError>(() => hub.Call("mcp__s__anything", new JSONValue.Null()));
        Assert.Equal("The server quit.", error.ErrorDescription);
    }

    // ---- PR-MCP-192, PR-MCP-185: the literal launch-failure and exit messages ----

    [Fact]
    [Trait("Req", "PR-MCP-185")] // Services/MCP/MCPHub.swift:326-328
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
    [Trait("Req", "PR-MCP-186")] // Services/MCP/MCPHub.swift:393-418, :169-172
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
    [Trait("Req", "PR-MCP-188")] // Services/MCP/MCPHub.swift:474-476
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
            var reply = "data: {\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"sse-ok\"}]}}\n\n"u8.ToArray();
            await call.Respond(200, "OK", reply, ("Content-Type", "text/event-stream"));
        });

        var result = await hub.Call("mcp__s__anything", new JSONValue.Null());

        await serverTask;
        Assert.Equal("sse-ok", result.Text);
    }

    private static string Single(LoopbackExchange exchange, string header) => Assert.Single(exchange.HeaderValues(header));

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
    [Trait("Req", "PR-MCP-191")] // Services/MCP/MCPHub.swift:26, :30
    public void Track_and_TerminateAll_delegate_to_the_shared_live_process_list()
    {
        var process = new FakeProcessControl("tracked");

        MCPHub.Track(process);
        MCPHub.TerminateAll();

        Assert.Contains("terminate", process.Calls);
    }
}
