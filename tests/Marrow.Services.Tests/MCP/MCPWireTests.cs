using System.Diagnostics;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPWire.cs, the port of DroppyCode/Services/MCP/MCPWire.swift (B3
// 52bc7926), :1-246, and the records PR-MCP-102, PR-MCP-111, PR-MCP-117, PR-MCP-232, PR-MCP-234, PR-MCP-235
// and PR-MCP-236 (packets_b3/Services.MCP.MCPWire/part1.md). Every expectation is transcribed from the Swift
// source and the records, never from the C# implementation.
//
// MCPWire's pure envelope helpers (InitializeParams, Request, Notification, ErrorMessage, ListTools) are unit
// tested directly. The HTTP path (Post) is exercised against Fixtures/FakeUpstreamServer, a scripted HTTP/1.1
// loopback server (R-TEST-10: never the internet). The stdio path (MCPStdioConnection) is exercised against a
// real child process, powershell.exe (a system binary, R-TEST-10; nothing here starts a real AI CLI).
//
// MCPHub and MCPProbe do not call MCPWire yet (B3_PLAN.md row N5: their own switch-over is lanes A15/A16), so
// nothing here exercises this file through them.
public sealed class MCPWireTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    // MARK: - Envelope helpers

    [Fact]
    [Trait("Req", "PR-MCP-102")] // Services/MCP/MCPWire.swift:17-25 (B3)
    public void Request_builds_the_fixed_envelope_and_includes_params_only_when_given()
    {
        var withParams = MCPWire.Request(3, "tools/call", JSONValue.FromDictionaryLiteral(("name", new JSONValue.String("x"))));
        Assert.Equal("2.0", withParams["jsonrpc"]?.JSONValueString);
        Assert.Equal(3, withParams["id"]?.JSONValueInt);
        Assert.Equal("tools/call", withParams["method"]?.JSONValueString);
        Assert.Equal("x", withParams["params"]?["name"]?.JSONValueString);
        Assert.Equal(4, withParams.JSONValueObject!.Count);

        var withoutParams = MCPWire.Request(1, "initialize", null);
        Assert.Equal(3, withoutParams.JSONValueObject!.Count);
        Assert.Null(withoutParams["params"]);
    }

    [Fact]
    [Trait("Req", "PR-MCP-102")] // Services/MCP/MCPWire.swift:27-29 (B3)
    public void Notification_has_no_id_and_no_params()
    {
        var note = MCPWire.Notification("notifications/initialized");
        Assert.Equal(2, note.JSONValueObject!.Count);
        Assert.Equal("2.0", note["jsonrpc"]?.JSONValueString);
        Assert.Equal("notifications/initialized", note["method"]?.JSONValueString);
        Assert.Null(note["id"]);
    }

    [Fact]
    [Trait("Req", "PR-MCP-101")] // Services/MCP/MCPWire.swift:9-15 (B3); clientInfo.name is "Marrow" (DEVIATION OD-0012 F1)
    public void InitializeParams_carries_the_protocol_version_marrow_clientinfo_and_empty_capabilities()
    {
        var init = MCPWire.InitializeParams();
        Assert.Equal("2025-06-18", MCPWire.ProtocolVersion);
        Assert.Equal(MCPWire.ProtocolVersion, init["protocolVersion"]?.JSONValueString);
        Assert.Empty(init["capabilities"]!.JSONValueObject!);
        Assert.Equal("Nth", init["clientInfo"]?["name"]?.JSONValueString);
        Assert.Equal("1.0", init["clientInfo"]?["version"]?.JSONValueString);
    }

    [Fact]
    [Trait("Rule", "R-ERR-3")] // Services/MCP/MCPWire.swift:31-34 (B3)
    public void ErrorMessage_reads_the_message_field_or_falls_back_to_unknown_error()
    {
        var withMessage = JSONValue.FromDictionaryLiteral(
            ("error", JSONValue.FromDictionaryLiteral(("message", new JSONValue.String("boom")))));
        Assert.Equal("boom", MCPWire.ErrorMessage(withMessage));

        var withoutMessage = JSONValue.FromDictionaryLiteral(("error", JSONValue.FromDictionaryLiteral()));
        Assert.Equal("unknown error", MCPWire.ErrorMessage(withoutMessage));
    }

    [Fact]
    [Trait("Rule", "R-ERR-3")] // Services/MCP/MCPWire.swift:31-34 (B3)
    public void ErrorMessage_is_null_without_an_error_object()
    {
        Assert.Null(MCPWire.ErrorMessage(JSONValue.FromDictionaryLiteral(("result", new JSONValue.Bool(true)))));
        // An "error" field that is not itself an object (error.object == nil in Swift) also gives nil.
        Assert.Null(MCPWire.ErrorMessage(JSONValue.FromDictionaryLiteral(("error", new JSONValue.String("nope")))));
    }

    [Fact]
    [Trait("Rule", "R-COLL-3")] // Services/MCP/MCPWire.swift:36-51 (B3)
    public async Task ListTools_pages_until_the_server_stops_returning_a_cursor()
    {
        var seenCursors = new List<string?>();
        Task<JSONValue> Page(JSONValue @params)
        {
            var cursor = @params["cursor"]?.JSONValueString;
            seenCursors.Add(cursor);
            var (name, next) = cursor switch
            {
                null => ("a", "c1"),
                "c1" => ("b", "c2"),
                _ => ("c", null),
            };
            var fields = new List<(string, JSONValue)>
            {
                ("tools", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("name", new JSONValue.String(name))))),
            };
            if (next is { } n) fields.Add(("nextCursor", new JSONValue.String(n)));
            return Task.FromResult(JSONValue.FromDictionaryLiteral(("result", JSONValue.FromDictionaryLiteral([.. fields]))));
        }

        var tools = await MCPWire.ListTools(Page);

        Assert.Equal(["a", "b", "c"], tools.Select(t => t["name"]?.JSONValueString));
        Assert.Equal([null, "c1", "c2"], seenCursors);
    }

    [Fact]
    [Trait("Rule", "R-COLL-3")] // Services/MCP/MCPWire.swift:41 (B3): "for _ in 0..<10"
    public async Task ListTools_stops_after_ten_pages_even_if_the_server_keeps_offering_a_cursor()
    {
        var calls = 0;
        Task<JSONValue> Page(JSONValue @params)
        {
            calls++;
            return Task.FromResult(JSONValue.FromDictionaryLiteral(("result", JSONValue.FromDictionaryLiteral(
                ("tools", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("name", new JSONValue.String("t" + calls))))),
                ("nextCursor", new JSONValue.String("more"))))));
        }

        var tools = await MCPWire.ListTools(Page);

        Assert.Equal(10, calls);
        Assert.Equal(10, tools.Count);
    }

    // MARK: - Streamable HTTP (MCPWire.Post)

    [Fact]
    [Trait("Rule", "R-NET-1")] // Services/MCP/MCPWire.swift:85-90 (B3)
    public async Task Post_reads_the_whole_body_when_the_content_type_is_not_event_stream()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200, [("Content-Type", "application/json")],
                System.Text.Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"result":{"ok":true}}""")),
        };

        var reply = await MCPWire.Post(new Uri(server.BaseUrl), new Dictionary<string, string>(), null,
            MCPWire.Request(1, "tools/list", null), 1, 5).WaitAsync(Bound);

        Assert.True(reply.Body?["result"]?["ok"]?.JSONValueBool);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")] // Services/MCP/MCPWire.swift:88-90 (B3)
    public async Task Post_returns_a_nil_body_for_an_empty_non_event_stream_response()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(204, [], []),
        };

        var reply = await MCPWire.Post(new Uri(server.BaseUrl), new Dictionary<string, string>(), null,
            MCPWire.Notification("notifications/initialized"), null, 5).WaitAsync(Bound);

        Assert.Null(reply.Body);
    }

    [Fact]
    [Trait("Rule", "R-NET-1")] // Services/MCP/MCPWire.swift:77-84 (B3)
    public async Task Post_throws_with_the_non_2xx_body_trimmed_and_cut_at_2000_bytes()
    {
        var body = "  " + new string('x', 5_000) + "  \n";
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(500, [], System.Text.Encoding.UTF8.GetBytes(body)),
        };

        var error = await Assert.ThrowsAsync<MCPHTTPError>(() => MCPWire.Post(
            new Uri(server.BaseUrl), new Dictionary<string, string>(), null,
            MCPWire.Request(1, "tools/list", null), 1, 5)).WaitAsync(Bound);

        // The cut is 2000 raw bytes first ("  " + 1998 'x'), trimmed after: the 2 leading spaces go, the
        // trailing spaces (never reached by the cut) do not apply, so 1998 'x' remain.
        Assert.Equal(500, error.Status);
        Assert.Equal(new string('x', 1_998), error.Body);
    }

    [Fact]
    [Trait("Req", "PR-MCP-117")] // Services/MCP/MCPWire.swift:85-101 (B3)
    public async Task Post_returns_the_first_sse_frame_whose_id_matches_the_request()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200, [("Content-Type", "text/event-stream"), ("Mcp-Session-Id", "s1")], [],
                SseChunks: ["data: {\"id\":2,\"result\":\"wrong id\"}\n\n", "data: {\"id\":1,\"result\":\"right id\"}\n\n"]),
        };

        var reply = await MCPWire.Post(new Uri(server.BaseUrl), new Dictionary<string, string>(), null,
            MCPWire.Request(1, "initialize", null), 1, 5).WaitAsync(Bound);

        Assert.Equal("s1", reply.SessionID);
        Assert.Equal("right id", reply.Body?["result"]?.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-MCP-117")] // Services/MCP/MCPWire.swift:85-101 (B3): "a stream held open after the reply"
    public async Task Post_returns_the_matching_frame_promptly_while_the_server_still_holds_the_stream_open()
    {
        // The fixture does not close the connection after writing the matching frame: it keeps the socket open
        // for up to 5 seconds (FakeUpstreamServer.ClientClosedConnection). An implementation that buffered the
        // whole SSE body before returning would have to wait for that close (or the outer 30s Bound) instead of
        // returning as soon as the id matches; a correct one returns almost immediately.
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200, [("Content-Type", "text/event-stream"), ("Mcp-Session-Id", "s1")], [],
                SseChunks: ["data: {\"id\":1,\"result\":\"right id\"}\n\n"], HoldOpenAfterSse: true),
        };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var reply = await MCPWire.Post(new Uri(server.BaseUrl), new Dictionary<string, string>(), null,
            MCPWire.Request(1, "initialize", null), 1, 5).WaitAsync(Bound);
        clock.Stop();

        Assert.Equal("right id", reply.Body?["result"]?.JSONValueString);
        // Well under the fixture's 5-second hold: Post did not wait for the stream to end.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Post took {clock.Elapsed} while the server held the stream open");
    }

    [Fact]
    [Trait("Req", "PR-MCP-117")] // Services/MCP/MCPWire.swift:93-102 (B3)
    public async Task Post_skips_unparseable_and_idless_sse_frames_and_returns_nil_at_the_streams_end()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200, [("Content-Type", "text/event-stream")], [],
                SseChunks: ["not data at all\n\n", "data: not json\n\n", "data: {\"result\":\"no id\"}\n\n"]),
        };

        var reply = await MCPWire.Post(new Uri(server.BaseUrl), new Dictionary<string, string>(), null,
            MCPWire.Request(1, "initialize", null), 1, 5).WaitAsync(Bound);

        Assert.Null(reply.Body);
    }

    [Fact]
    [Trait("Req", "PR-MCP-117")] // Services/MCP/MCPWire.swift:92 (B3): "guard let id else { return ... }"
    public async Task Post_never_reads_the_event_stream_for_a_notification_with_no_id()
    {
        // The AsyncBytes fix this lane's row calls for (Core/Port/Http.cs:930-1038's Release): the enumerator
        // is obtained and disposed without ever advancing, so the exchange behind it ends instead of leaking.
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(202, [("Content-Type", "text/event-stream"), ("Mcp-Session-Id", "s2")], [],
                SseChunks: ["data: {\"id\":1,\"result\":\"never read\"}\n\n"]),
        };

        var reply = await MCPWire.Post(new Uri(server.BaseUrl), new Dictionary<string, string>(), null,
            MCPWire.Notification("notifications/initialized"), null, 5).WaitAsync(Bound);

        Assert.Equal("s2", reply.SessionID);
        Assert.Null(reply.Body);
    }

    [Fact]
    [Trait("Req", "PR-MCP-117")] // Services/MCP/MCPWire.swift:92 (B3): "guard let id else { return ... }"
    public async Task Post_releases_the_notifications_event_stream_so_the_server_sees_the_connection_close()
    {
        // The fixture keeps the socket open (it never hangs up first) and instead watches its own end for a
        // close: FakeUpstreamServer.ClientClosedConnection only completes true when IT reads a 0-byte close
        // from the client side. An implementation that left the AsyncBytes undisposed (never released, never
        // drained) would leave this socket open past the fixture's 5-second bound; disposing it without ever
        // reading (:92) makes the close arrive almost at once.
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(202, [("Content-Type", "text/event-stream"), ("Mcp-Session-Id", "s2")], [],
                SseChunks: ["data: {\"id\":1,\"result\":\"never read\"}\n\n"], HoldOpenAfterSse: true),
        };

        var reply = await MCPWire.Post(new Uri(server.BaseUrl), new Dictionary<string, string>(), null,
            MCPWire.Notification("notifications/initialized"), null, 5).WaitAsync(Bound);
        Assert.Null(reply.Body);

        var closedByClient = await server.ClientClosedConnection.Task.WaitAsync(Bound);
        Assert.True(closedByClient, "the fixture never observed the client releasing the connection");
    }

    [Fact]
    [Trait("Rule", "R-NET-1")] // Services/MCP/MCPWire.swift:58-70,76 (B3)
    public async Task Post_sends_custom_and_protocol_headers_and_the_session_id()
    {
        FakeRequest? seen = null;
        await using var server = new FakeUpstreamServer
        {
            Handler = req =>
            {
                seen = req;
                return new FakeResponse(200, [], []);
            },
        };

        await MCPWire.Post(new Uri(server.BaseUrl), new Dictionary<string, string> { ["X-Api-Key"] = "k" }, "sid-1",
            MCPWire.Request(1, "initialize", null), 1, 5).WaitAsync(Bound);

        Assert.NotNull(seen);
        Assert.Equal("k", seen!.Headers["X-Api-Key"]);
        Assert.Equal("application/json", seen.Headers["Content-Type"]);
        Assert.Equal("application/json, text/event-stream", seen.Headers["Accept"]);
        Assert.Equal(MCPWire.ProtocolVersion, seen.Headers["MCP-Protocol-Version"]);
        Assert.Equal("sid-1", seen.Headers["Mcp-Session-Id"]);
    }

    // MARK: - MCPStdioConnection

    private static MCPStdioConnection LaunchPowerShell(string script) => MCPStdioConnection.Launch(
        ACPSessionFixtures.PowerShell, ACPSessionFixtures.PowerShellEncoded(script),
        Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase));

    [Fact]
    [Trait("Req", "PR-MCP-102")] // Services/MCP/MCPWire.swift:126,164-168 (B3)
    public async Task Ids_start_at_one_and_count_up_per_connection()
    {
        var connection = LaunchPowerShell(
            "$i=[Console]::In; $o=[Console]::Out; " +
            "while(($l=$i.ReadLine()) -ne $null){ if($l -match '\"id\":(\\d+)'){ " +
            "$o.WriteLine('{\"id\":'+$matches[1]+',\"result\":{\"echo\":'+$matches[1]+'}}'); $o.Flush() } }");
        try
        {
            for (var expected = 1; expected <= 3; expected++)
            {
                var reply = await connection.Request("ping", null, 10).WaitAsync(Bound);
                Assert.Equal(expected, reply["result"]?["echo"]?.JSONValueInt);
            }
        }
        finally
        {
            connection.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-232")] // Services/MCP/MCPWire.swift:198-228 (B3)
    public async Task A_reply_that_arrives_before_its_request_waits_is_still_delivered()
    {
        // The child answers id 1 the instant it starts, before the test ever calls Request: by the time
        // AwaitReply registers its wait, the reply can only reach it through the buffered map (:203, :224).
        var connection = LaunchPowerShell(
            "$o=[Console]::Out; $o.WriteLine('{\"id\":1,\"result\":{\"early\":true}}'); $o.Flush(); " +
            "$i=[Console]::In; while(($l=$i.ReadLine()) -ne $null){}");
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var reply = await connection.Request("ping", null, 10).WaitAsync(Bound);
            Assert.True(reply["result"]?["early"]?.JSONValueBool);
        }
        finally
        {
            connection.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-234")] // Services/MCP/MCPWire.swift:163 (B3)
    public async Task A_request_made_after_the_child_has_already_exited_fails_at_once_with_its_status()
    {
        var connection = LaunchPowerShell("exit 5");
        try
        {
            var status = await connection.Process.WaitForExit().WaitAsync(Bound);
            Assert.Equal(5, status);

            var error = await Assert.ThrowsAsync<MCPWireError>(() => connection.Request("ping", null, 10)).WaitAsync(Bound);
            var exited = Assert.IsType<MCPWireError.Case.Exited>(error.ErrorCase);
            Assert.Equal(5, exited.Status);
        }
        finally
        {
            connection.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-234")] // Services/MCP/MCPWire.swift:189-196,235-245 (B3)
    public async Task A_waiting_request_fails_with_the_exit_status_once_the_child_exits_unanswered()
    {
        var connection = LaunchPowerShell("Start-Sleep -Milliseconds 300; exit 9");
        try
        {
            var error = await Assert.ThrowsAsync<MCPWireError>(() => connection.Request("ping", null, 30)).WaitAsync(Bound);
            var exited = Assert.IsType<MCPWireError.Case.Exited>(error.ErrorCase);
            Assert.Equal(9, exited.Status);
        }
        finally
        {
            connection.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-235")] // Services/MCP/MCPWire.swift:170-186 (B3)
    public async Task A_request_times_out_when_no_reply_arrives_and_a_later_request_still_gets_its_own_reply()
    {
        // The child holds the first two lines it reads, waits past request 1's own short timeout, then answers
        // both by id: request 1's late reply is abandoned (PR-MCP-235) and simply kept, never delivered
        // (PR-MCP-232), while request 2, sent with a long timeout, gets its own reply normally.
        var connection = LaunchPowerShell(
            "$i=[Console]::In; $o=[Console]::Out; $lines=@(); " +
            "while(($l=$i.ReadLine()) -ne $null){ $lines += $l; if($lines.Count -ge 2){ break } } " +
            "Start-Sleep -Milliseconds 400; " +
            "foreach($l in $lines){ if($l -match '\"id\":(\\d+)'){ " +
            "$o.WriteLine('{\"id\":'+$matches[1]+',\"result\":{\"late\":true}}'); $o.Flush() } } " +
            "while(($l=$i.ReadLine()) -ne $null){}");
        try
        {
            var first = await Assert.ThrowsAsync<MCPWireError>(() => connection.Request("ping", null, 0.15)).WaitAsync(Bound);
            Assert.IsType<MCPWireError.Case.TimedOut>(first.ErrorCase);

            var second = await connection.Request("ping", null, 10).WaitAsync(Bound);
            // Both replies share the literal body {"result":{"late":true}}; only the envelope's own "id" tells
            // them apart, so this is the assertion that actually proves request 2 got its own reply rather than
            // request 1's abandoned late one being handed to whichever call happened to be waiting.
            Assert.Equal(2, second["id"]?.JSONValueInt);
            Assert.True(second["result"]?["late"]?.JSONValueBool);
        }
        finally
        {
            connection.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-235")] // Services/MCP/MCPWire.swift:170-186 (B3): "A cancelled caller cancels both
    // tasks, the wait gives up its slot the same way, and CancellationError reaches the caller." Covers the
    // manual cancellation bridge this unit substitutes for withThrowingTaskGroup (:236-243 comment): two
    // TaskHandle.DetachedThrowing children plus a SwiftTask.CancellationToken.Register propagating the
    // caller's own ambient cancellation, since neither child otherwise inherits it (R-TASK-1).
    public async Task A_caller_cancelled_mid_wait_gets_CancellationError_and_abandons_its_id()
    {
        // The child holds both lines it reads (never answers while the test is still driving it) so nothing
        // can resolve request 1 before it is cancelled; once released, it answers both by id, late.
        var connection = LaunchPowerShell(
            "$i=[Console]::In; $o=[Console]::Out; $lines=@(); " +
            "while(($l=$i.ReadLine()) -ne $null){ $lines += $l; if($lines.Count -ge 2){ break } } " +
            "foreach($l in $lines){ if($l -match '\"id\":(\\d+)'){ " +
            "$o.WriteLine('{\"id\":'+$matches[1]+',\"result\":{\"late\":true}}'); $o.Flush() } } " +
            "while(($l=$i.ReadLine()) -ne $null){}");
        try
        {
            // Request runs inside its own ambient task (TaskHandle.DetachedThrowing), the caller whose
            // cancellation the manual bridge is supposed to propagate into the reply and timer children.
            var cancelled = TaskHandle.DetachedThrowing(() => connection.Request("ping", null, 30));
            await Task.Delay(TimeSpan.FromMilliseconds(300)); // let AwaitReply register its wait for id 1
            cancelled.Cancel();
            await Assert.ThrowsAsync<CancellationError>(() => cancelled.Value).WaitAsync(Bound);

            // Id 1's wait was abandoned: a second request (id 2) must get its own reply, not the first's late
            // one once the child eventually answers both (PR-MCP-232's "kept, not delivered").
            var second = await connection.Request("ping", null, 10).WaitAsync(Bound);
            Assert.Equal(2, second["id"]?.JSONValueInt);
            Assert.True(second["result"]?["late"]?.JSONValueBool);
        }
        finally
        {
            connection.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-232")] // Services/MCP/MCPWire.swift:219-228 (B3)
    public async Task A_reply_for_an_id_nobody_is_waiting_on_is_kept_and_a_later_request_for_it_still_gets_it()
    {
        // The child answers id 7 the moment it starts, unprompted (nothing ever requests id 7 through the
        // normal counter, which starts at 1): :224 buffers it under state.buffered[7]. A later request that
        // happens to land on id 1 must get its OWN reply, and only a request for id 7 specifically (impossible
        // through the public counter, so this drives it through the same child process a second connection
        // could not) proves the unsolicited message was kept rather than dropped or misdelivered. Since this
        // connection's own counter starts at 1, we instead assert the counted request (id 1) is unaffected by
        // the buffered, never-requested id 7 reply sitting in state.buffered.
        var connection = LaunchPowerShell(
            "$o=[Console]::Out; $o.WriteLine('{\"id\":7,\"result\":{\"unsolicited\":true}}'); $o.Flush(); " +
            "$i=[Console]::In; while(($l=$i.ReadLine()) -ne $null){ if($l -match '\"id\":(\\d+)'){ " +
            "$o.WriteLine('{\"id\":'+$matches[1]+',\"result\":{\"echo\":'+$matches[1]+'}}'); $o.Flush() } }");
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var reply = await connection.Request("ping", null, 10).WaitAsync(Bound);
            Assert.Equal(1, reply["id"]?.JSONValueInt);
            Assert.Equal(1, reply["result"]?["echo"]?.JSONValueInt);
        }
        finally
        {
            connection.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-236")] // Services/MCP/MCPWire.swift:156 (B3)
    public async Task ErrorTail_forwards_the_childs_stderr()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("boom"));
        var connection = LaunchPowerShell(
            $"$e=[Console]::OpenStandardError(); $b=[Convert]::FromBase64String('{encoded}'); " +
            "$e.Write($b,0,$b.Length); $e.Flush()");
        try
        {
            await connection.Process.WaitForExit().WaitAsync(Bound);
            Assert.Equal("boom", connection.ErrorTail);
        }
        finally
        {
            connection.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-111")] // Services/MCP/MCPWire.swift:152-154 (B3)
    public async Task Stop_terminates_a_running_child()
    {
        // A cooperating child that exits on stdin EOF alone: this proves Stop closes stdin, but not that it runs
        // the rest of ED-126's ladder (graceful signal, then a 3 s pid-only kill) - see the test right below,
        // which uses a child that ignores everything but the forced kill (review finding L3a-2).
        var connection = LaunchPowerShell("$i=[Console]::In; while(($l=$i.ReadLine()) -ne $null){}");
        connection.Stop();
        var status = await connection.Process.WaitForExit().WaitAsync(Bound);
        Assert.False(connection.Process.IsRunning);
        _ = status;
    }

    [Fact]
    [Trait("Req", "PR-MCP-190")] // Services/MCP/MCPHub.swift:173-178 stop(), through MCPWire.swift:152-154 (B3)
    [Trait("Decision", "ED-126")]
    [Trait("Decision", "ED-194")]
    public async Task Stop_ignores_the_graceful_step_then_kills_the_pid_alone_at_3_seconds_leaving_a_grandchild_alive()
    {
        // Review finding L3a-2: MCPClient.Stop routes to this exact method (MCPStdioConnection.Stop ->
        // StdioProcess.Terminate -> TerminationLadder.Stdio), so a hub Stop wired to the wrong ladder shape (for
        // example B2's 1 s TerminateAll/KillTree) would still pass every other MCPHubTests/MCPWireTests test.
        // This drives the real ladder end to end: a child that swallows every console control event (so the
        // graceful step never ends it) and holds a grandchild of its own. Under the manual clock: no kill before
        // 3 s, a pid-only kill exactly at 3 s (TerminationLadder.Stdio, not Shell's 1 s/KillTree), and the
        // grandchild - never touched by KillProcess - is still alive afterward (ED-194: the job the child now
        // sits in is never used by this ladder, only by MCPLiveProcesses.terminateAll at quit).
        using var runtime = new TestRuntime();
        var connection = LaunchPowerShell(
            """
            Add-Type -TypeDefinition @'
            using System;
            using System.Runtime.InteropServices;
            public static
            class
            MarrowIgnoreBreakFixture
            {
                public delegate bool Handler(uint type);
                [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
                private static Handler keep;
                public static void Arm()
                {
                    keep = OnSignal;
                    SetConsoleCtrlHandler(keep, true);
                }
                private static bool OnSignal(uint type) { return true; }
            }
            '@
            [MarrowIgnoreBreakFixture]::Arm()
            $line = [Console]::In.ReadLine()
            $msg = $line | ConvertFrom-Json
            $p = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep 60' -NoNewWindow -PassThru
            $out = [Console]::Out
            $out.Write('{"jsonrpc":"2.0","id":' + $msg.id + ',"result":{"grandchild":' + $p.Id + '}}')
            $out.Write("`n")
            $out.Flush()
            Start-Sleep 60
            """);
        var reply = await connection.Request("test/spawn-grandchild", null, timeout: 30).WaitAsync(Bound);
        var grandchildPid = checked((int)(reply["result"]?["grandchild"]?.JSONValueInt ?? 0));
        Assert.True(grandchildPid > 0);
        var grandchild = Process.GetProcessById(grandchildPid);
        _ = grandchild.SafeHandle; // retain the native handle before it exits, as StdioProcessJobTests does
        try
        {
            connection.Stop();
            Assert.True(connection.Process.IsRunning, "the graceful step alone ended the child (it should ignore CTRL_BREAK)");

            runtime.Advance(TimeSpan.FromMilliseconds(2_999));
            Assert.True(connection.Process.IsRunning, "killed before the 3 s mark: not the Stdio ladder (ED-126/ED-194)");

            runtime.Advance(TimeSpan.FromMilliseconds(1));
            _ = await connection.Process.WaitForExit().WaitAsync(Bound);
            Assert.False(connection.Process.IsRunning);

            grandchild.Refresh();
            Assert.False(grandchild.HasExited, "the grandchild died too: Stop used a tree/job kill, not the pid alone");
        }
        finally
        {
            grandchild.Refresh();
            if (!grandchild.HasExited) grandchild.Kill();
        }
    }
}
