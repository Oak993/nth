using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// Expectations come from DroppyCode/Core/Support/JSONRPC.swift (B2 1fbf81e). The child processes are
// powershell.exe scripts (system binary, R-TEST-10): they log every stdin frame to stderr, so a frame the
// connection wrote is read back from StdioProcess.ErrorTail, and they write scripted inbound frames to stdout.
// Swift dictionary order is unspecified (R-COLL-2), so frames are compared by member set, never key order.
public sealed class JSONRPCTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    private const string Pipes = "$i=[Console]::In; $e=[Console]::Error; $o=[Console]::Out; ";

    private const string LogUntilBye =
        "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); if($l.Contains('\"method\":\"bye\"')){ break } }";

    private static StdioProcess Child(string script) =>
        new(SystemTools.PowerShell, SystemTools.PowerShellEncoded(script), Environment.CurrentDirectory,
            Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase));

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

    private static List<JSONValue> Frames(TestRuntime runtime, StdioProcess process, int count)
    {
        runtime.Loop.RunUntil(() => Lines(process.ErrorTail).Count >= count, Bound);
        return Lines(process.ErrorTail).Select(l => JSONValue.Parse(l)!).ToList();
    }

    private static string[] Members(JSONValue frame) =>
        frame.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    private static JSONValue Obj(params (string Key, JSONValue Value)[] members) => JSONValue.FromDictionaryLiteral(members);

    private static JSONValue Resolved(Task<JSONValue> task)
    {
        Assert.True(task.IsCompletedSuccessfully, "the request did not resolve");
        return task.Result;
    }

    private static RPCError RpcFailure(Task task)
    {
        Assert.True(task.IsFaulted, "the request did not fail");
        return Assert.IsType<RPCError>(task.Exception!.InnerException);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-140")] // Core/Support/JSONRPC.swift:49,74-75
    [Trait("Req", "PR-PROVACP-143")] // Core/Support/JSONRPC.swift:105-110
    [Trait("Req", "PR-PROVACP-144")] // Core/Support/JSONRPC.swift:89-99
    public void Requests_notifications_and_responses_are_written_with_exactly_the_source_members()
    {
        using var runtime = new TestRuntime();
        var process = Child(Pipes + LogUntilBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        runtime.OnMain(connection.Start);
        TaskHandle<JSONValue>? first = null, second = null;
        runtime.OnMain(() =>
        {
            first = MainActor.SpawnThrowing(() => connection.Request("session/new", Obj(("cwd", "C:\\work"))));
            second = MainActor.SpawnThrowing(() => connection.Request("ping"));
        });
        runtime.OnMain(() =>
        {
            connection.Notify("session/cancel", Obj(("sessionId", "s-1")));
            connection.Notify("tick");
            connection.Respond(to: new RPCID.Int(7), result: Obj(("outcome", "selected")));
            connection.Respond(to: new RPCID.String("abc"), result: new JSONValue.Null());
            connection.Respond(to: new RPCID.String("q"), errorCode: -32601, message: "Method not found");
            connection.Notify("bye", Obj());
        });
        var frames = Frames(runtime, process, 8);

        // :74-80 first request id 1, params kept, jsonrpc added (:109).
        Assert.Equal(["id", "jsonrpc", "method", "params"], Members(frames[0]));
        Assert.Equal(new JSONValue.Int(1), frames[0]["id"]);
        Assert.Equal(new JSONValue.String("session/new"), frames[0]["method"]);
        Assert.Equal(Obj(("cwd", "C:\\work")), frames[0]["params"]);
        Assert.Equal(new JSONValue.String("2.0"), frames[0]["jsonrpc"]);
        // :75 the next request increments; :108 a null params is removed.
        Assert.Equal(["id", "jsonrpc", "method"], Members(frames[1]));
        Assert.Equal(new JSONValue.Int(2), frames[1]["id"]);
        Assert.Equal(new JSONValue.String("ping"), frames[1]["method"]);
        // :89-91 a notification has method and params and no id.
        Assert.Equal(["jsonrpc", "method", "params"], Members(frames[2]));
        Assert.Equal(Obj(("sessionId", "s-1")), frames[2]["params"]);
        Assert.Equal(["jsonrpc", "method"], Members(frames[3]));
        Assert.Equal(new JSONValue.String("tick"), frames[3]["method"]);
        // :93-95 a success response: id and result only; an integer id stays an integer.
        Assert.Equal(["id", "jsonrpc", "result"], Members(frames[4]));
        Assert.Equal(new JSONValue.Int(7), frames[4]["id"]);
        Assert.Equal(Obj(("outcome", "selected")), frames[4]["result"]);
        // A null result is NOT elided: only "params" is (:108).
        Assert.Equal(["id", "jsonrpc", "result"], Members(frames[5]));
        Assert.Equal(new JSONValue.String("abc"), frames[5]["id"]);
        Assert.Equal(new JSONValue.Null(), frames[5]["result"]);
        // :97-99 an error response: id and an error of exactly code and message.
        Assert.Equal(["error", "id", "jsonrpc"], Members(frames[6]));
        Assert.Equal(new JSONValue.String("q"), frames[6]["id"]);
        Assert.Equal(["code", "message"], Members(frames[6]["error"]!));
        Assert.Equal(new JSONValue.Int(-32601), frames[6]["error"]!["code"]);
        Assert.Equal(new JSONValue.String("Method not found"), frames[6]["error"]!["message"]);
        // An explicit empty object params is kept (only JSON null is removed).
        Assert.Equal(["jsonrpc", "method", "params"], Members(frames[7]));

        runtime.Loop.RunUntil(() => connection.IsClosed, Bound);
        runtime.Loop.RunUntil(() => first!.Value.IsCompleted && second!.Value.IsCompleted, Bound);
        Assert.Equal(-32000, RpcFailure(first!.Value).Code);
        Assert.Equal(-32000, RpcFailure(second!.Value).Code);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-140")] // Core/Support/JSONRPC.swift:49,74-75
    [Trait("Req", "PR-PROVACP-143")] // Core/Support/JSONRPC.swift:105-110
    public void The_id_counter_is_per_connection_and_sendsVersion_false_omits_jsonrpc()
    {
        using var runtime = new TestRuntime();
        var firstProcess = Child(Pipes + LogUntilBye);
        var secondProcess = Child(Pipes + LogUntilBye);
        var codex = new JSONRPCConnection(firstProcess, sendsVersion: false);
        var acp = new JSONRPCConnection(secondProcess, sendsVersion: true);
        runtime.OnMain(codex.Start);
        runtime.OnMain(acp.Start);
        runtime.OnMain(() =>
        {
            MainActor.SpawnThrowing(() => codex.Request("initialize", Obj(("a", 1L))));
            MainActor.SpawnThrowing(() => codex.Request("thread/start", Obj(("b", 2L))));
            MainActor.SpawnThrowing(() => acp.Request("initialize", Obj(("c", 3L))));
        });
        runtime.OnMain(() =>
        {
            codex.Notify("bye");
            acp.Notify("bye");
        });
        var codexFrames = Frames(runtime, firstProcess, 3);
        var acpFrames = Frames(runtime, secondProcess, 2);

        Assert.Equal(["id", "method", "params"], Members(codexFrames[0]));
        Assert.Equal(new JSONValue.Int(1), codexFrames[0]["id"]);
        Assert.Equal(new JSONValue.Int(2), codexFrames[1]["id"]);
        Assert.Equal(["method"], Members(codexFrames[2]));
        // A second connection restarts at 1.
        Assert.Equal(["id", "jsonrpc", "method", "params"], Members(acpFrames[0]));
        Assert.Equal(new JSONValue.Int(1), acpFrames[0]["id"]);
        Assert.Equal(["jsonrpc", "method"], Members(acpFrames[1]));
        runtime.Loop.RunUntil(() => codex.IsClosed && acp.IsClosed, Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-141")] // Core/Support/JSONRPC.swift:70-73
    [Trait("Req", "PR-PROVACP-143")] // Core/Support/JSONRPC.swift:105-106
    [Trait("Req", "UI-PROVACP-033")] // Core/Support/JSONRPC.swift:72
    public void A_request_on_a_closed_connection_fails_with_the_fixed_not_running_message()
    {
        using var runtime = new TestRuntime();
        var process = Child("exit 0");
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        runtime.OnMain(connection.Start);
        runtime.Loop.RunUntil(() => connection.IsClosed, Bound);

        var error = runtime.Loop.Run(async () => await Assert.ThrowsAsync<RPCError>(() => connection.Request("session/prompt", Obj())), Bound);
        Assert.Equal(-32000, error.Code);
        Assert.Equal("The agent process is not running.", error.Message);
        Assert.Null(error.Data);
        Assert.Equal("The agent process is not running.", SwiftErrors.LocalizedDescription(error));
        Assert.NotEqual("The agent session is not running.", error.Message);
        // :106 writing on a closed connection is a silent no-op.
        runtime.OnMain(() =>
        {
            connection.Notify("x");
            connection.Respond(to: new RPCID.Int(1), result: Obj());
            connection.Respond(to: new RPCID.Int(1), errorCode: -1, message: "m");
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-142")] // Core/Support/JSONRPC.swift:74-86
    public void A_cancelled_request_fails_with_CancellationError_and_its_late_reply_is_dropped()
    {
        using var runtime = new TestRuntime();
        var process = Child(Pipes +
            "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"go\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":\"late\"}'); $o.Flush() } " +
            "if($l.Contains('\"method\":\"d\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":4,\"result\":\"ok\"}'); $o.Flush() } " +
            "if($l.Contains('\"method\":\"bye\"')){ break } }");
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        runtime.OnMain(connection.Start);

        // Already cancelled: :77 checkCancellation throws before anything is written, but :74-75 consumed id 1.
        TaskHandle<JSONValue>? early = null;
        runtime.OnMain(() =>
        {
            early = MainActor.SpawnThrowing(() => connection.Request("a", Obj()));
            early.Cancel();
        });
        runtime.Loop.RunUntil(() => early!.Value.IsCompleted, Bound);
        Assert.True(early!.Value.IsFaulted);
        Assert.IsType<CancellationError>(early.Value.Exception!.InnerException);

        TaskHandle<JSONValue>? parked = null, inFlight = null;
        runtime.OnMain(() =>
        {
            parked = MainActor.SpawnThrowing(() => connection.Request("b", Obj()));
            inFlight = MainActor.SpawnThrowing(() => connection.Request("c", Obj()));
        });
        Frames(runtime, process, 2);
        // In flight: :82-85 the pending entry is removed and the await fails with CancellationError.
        inFlight!.Cancel();
        runtime.Loop.RunUntil(() => inFlight.Value.IsCompleted, Bound);
        Assert.True(inFlight.Value.IsFaulted);
        Assert.IsType<CancellationError>(inFlight.Value.Exception!.InnerException);

        // The agent's later reply for id 3 is dropped (no entry); id 4 still resolves.
        TaskHandle<JSONValue>? after = null;
        runtime.OnMain(() => connection.Notify("go", Obj()));
        runtime.OnMain(() => after = MainActor.SpawnThrowing(() => connection.Request("d", Obj())));
        runtime.Loop.RunUntil(() => after!.Value.IsCompleted, Bound);
        Assert.Equal(new JSONValue.String("ok"), Resolved(after!.Value));
        Assert.False(parked!.Value.IsCompleted);

        runtime.OnMain(() => connection.Notify("bye"));
        var frames = Frames(runtime, process, 5);
        // Nothing was written for "a", and no cancel frame was sent for "c".
        Assert.Equal(5, frames.Count);
        Assert.Equal(["b", "c", "go", "d", "bye"], frames.Select(f => f["method"]!.JSONValueString!).ToArray());
        Assert.Equal(new JSONValue.Int(2), frames[0]["id"]);
        Assert.Equal(new JSONValue.Int(3), frames[1]["id"]);
        Assert.Equal(new JSONValue.Int(4), frames[3]["id"]);
        runtime.Loop.RunUntil(() => connection.IsClosed && parked.Value.IsCompleted, Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-145")] // Core/Support/JSONRPC.swift:3-29
    public void Inbound_ids_accept_integers_truncated_doubles_and_strings_only()
    {
        Assert.Equal(new RPCID.Int(5), RPCID.Create(new JSONValue.Int(5)));
        Assert.Equal(new RPCID.Int(2), RPCID.Create(new JSONValue.Double(2.0)));
        Assert.Equal(new RPCID.Int(2), RPCID.Create(new JSONValue.Double(2.7)));
        Assert.Equal(new RPCID.Int(-2), RPCID.Create(new JSONValue.Double(-2.7)));
        Assert.Equal(new RPCID.String("abc"), RPCID.Create(new JSONValue.String("abc")));
        Assert.Null(RPCID.Create(new JSONValue.Bool(true)));
        Assert.Null(RPCID.Create(new JSONValue.Null()));
        Assert.Null(RPCID.Create(JSONValue.FromArrayLiteral(new JSONValue.Int(1))));
        Assert.Null(RPCID.Create(Obj(("id", 1L))));
        // 2.0 and 2.7 collide with the integer id 2 (Hashable).
        Assert.Equal(RPCID.Create(new JSONValue.Int(2)), RPCID.Create(new JSONValue.Double(2.7)));
        Assert.Equal(RPCID.Create(new JSONValue.Int(2))!.GetHashCode(), RPCID.Create(new JSONValue.Double(2.7))!.GetHashCode());
        // An integer and a string id of the same text are different ids.
        Assert.NotEqual<RPCID>(new RPCID.Int(2), new RPCID.String("2"));
        // :16-21 json keeps the type; :23-28 key is the text.
        Assert.Equal(new JSONValue.Int(12), new RPCID.Int(12).Json);
        Assert.Equal(new JSONValue.String("12"), new RPCID.String("12").Json);
        Assert.Equal("12", new RPCID.Int(12).Key);
        Assert.Equal("-3", new RPCID.Int(-3).Key);
        Assert.Equal("ab", new RPCID.String("ab").Key);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-145")] // Core/Support/JSONRPC.swift:7-13,117,128
    [Trait("Req", "PR-PROVACP-146")] // Core/Support/JSONRPC.swift:113-127
    public void Inbound_messages_dispatch_to_the_request_and_notification_handlers()
    {
        using var runtime = new TestRuntime();
        string[] inbound =
        [
            "[1,2]",
            "\"bare\"",
            "{\"jsonrpc\":\"2.0\",\"method\":\"n1\"}",
            "{\"jsonrpc\":\"2.0\",\"method\":\"n2\",\"params\":{\"a\":1}}",
            "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"r1\",\"params\":[1]}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"s\",\"method\":\"r2\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":2.7,\"method\":\"r3\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":true,\"method\":\"n3\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":null,\"method\":\"n4\",\"params\":null}",
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":5}",
            "{\"jsonrpc\":\"2.0\",\"method\":\"done\"}",
        ];
        var writes = string.Concat(inbound.Select(l => "$o.WriteLine('" + l + "'); "));
        var process = Child(Pipes + writes + "$o.Flush(); " + LogUntilBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var requests = new List<(RPCID Id, string Method, JSONValue Params)>();
        var notifications = new List<(string Method, JSONValue Params)>();
        connection.OnRequest = (id, method, parameters) => { MainActor.AssumeIsolated(); requests.Add((id, method, parameters)); };
        connection.OnNotification = (method, parameters) => { MainActor.AssumeIsolated(); notifications.Add((method, parameters)); };
        runtime.OnMain(connection.Start);
        runtime.Loop.RunUntil(() => notifications.Any(n => n.Method == "done"), Bound);

        Assert.Equal(["n1", "n2", "n3", "n4", "done"], notifications.Select(n => n.Method).ToArray());
        Assert.Equal(new JSONValue.Null(), notifications[0].Params);
        Assert.Equal(Obj(("a", 1L)), notifications[1].Params);
        Assert.Equal(new JSONValue.Null(), notifications[2].Params);
        Assert.Equal(new JSONValue.Null(), notifications[3].Params);
        Assert.Equal(3, requests.Count);
        Assert.Equal((RPCID)new RPCID.Int(7), requests[0].Id);
        Assert.Equal("r1", requests[0].Method);
        Assert.Equal(JSONValue.FromArrayLiteral(new JSONValue.Int(1)), requests[0].Params);
        Assert.Equal((RPCID)new RPCID.String("s"), requests[1].Id);
        Assert.Equal(new JSONValue.Null(), requests[1].Params);
        Assert.Equal((RPCID)new RPCID.Int(2), requests[2].Id);
        Assert.Equal("r3", requests[2].Method);

        // With a handler installed nothing was answered.
        runtime.OnMain(() => connection.Notify("bye"));
        runtime.Loop.RunUntil(() => Lines(process.ErrorTail).Any(l => l.Contains("\"bye\"", StringComparison.Ordinal)), Bound);
        var frames = Frames(runtime, process, 1);
        Assert.Single(frames);
        Assert.Equal(new JSONValue.String("bye"), frames[0]["method"]);
        runtime.Loop.RunUntil(() => connection.IsClosed, Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-144")] // Core/Support/JSONRPC.swift:97-99
    [Trait("Req", "PR-PROVACP-146")] // Core/Support/JSONRPC.swift:117-122
    public void An_inbound_request_with_no_handler_is_answered_method_not_found_with_its_id_type_kept()
    {
        using var runtime = new TestRuntime();
        var process = Child(Pipes +
            "$o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":\"perm-1\",\"method\":\"session/request_permission\",\"params\":{}}'); " +
            "$o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"fs/read_text_file\"}'); $o.Flush(); " + LogUntilBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var notified = 0;
        connection.OnNotification = (_, _) => notified++;
        runtime.OnMain(connection.Start);
        var frames = Frames(runtime, process, 2);

        Assert.Equal(["error", "id", "jsonrpc"], Members(frames[0]));
        Assert.Equal(new JSONValue.String("perm-1"), frames[0]["id"]);
        Assert.Equal(Obj(("code", -32601L), ("message", "Method not found")), frames[0]["error"]);
        Assert.Equal(new JSONValue.String("2.0"), frames[0]["jsonrpc"]);
        Assert.Equal(new JSONValue.Int(9), frames[1]["id"]);
        Assert.Equal(Obj(("code", -32601L), ("message", "Method not found")), frames[1]["error"]);
        Assert.Equal(0, notified);
        runtime.OnMain(() => connection.Notify("bye"));
        runtime.Loop.RunUntil(() => connection.IsClosed, Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-147")] // Core/Support/JSONRPC.swift:128-138
    [Trait("Req", "UI-PROVACP-033")] // Core/Support/JSONRPC.swift:133
    public void Replies_resolve_their_pending_request_and_unmatched_or_duplicate_replies_are_dropped()
    {
        using var runtime = new TestRuntime();
        string[] replies =
        [
            "{\"jsonrpc\":\"2.0\",\"id\":99,\"result\":1}",
            "{\"jsonrpc\":\"2.0\",\"result\":1}",
            "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":\"string id\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}",
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"duplicate\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"error\":{\"code\":-32602,\"message\":\"bad params\",\"data\":{\"x\":1}}}",
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"error\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"error\":null,\"result\":5}",
            "{\"jsonrpc\":\"2.0\",\"id\":5}",
            "{\"jsonrpc\":\"2.0\",\"id\":6.9,\"error\":{\"code\":7,\"message\":\"truncated id\"}}",
        ];
        var writes = string.Concat(replies.Select(l => "$o.WriteLine('" + l + "'); "));
        var process = Child(Pipes + "for($n=0; $n -lt 6; $n++){ $l=$i.ReadLine(); $e.WriteLine($l); $e.Flush() } " +
            writes + "$o.Flush(); " + LogUntilBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        runtime.OnMain(connection.Start);
        var calls = new List<TaskHandle<JSONValue>>();
        runtime.OnMain(() =>
        {
            for (var n = 1; n <= 6; n++) calls.Add(MainActor.SpawnThrowing(() => connection.Request("m", Obj())));
        });
        runtime.Loop.RunUntil(() => calls.All(c => c.Value.IsCompleted), Bound);

        Assert.Equal(Obj(("ok", true)), Resolved(calls[0].Value));
        var bad = RpcFailure(calls[1].Value);
        Assert.Equal(-32602, bad.Code);
        Assert.Equal("bad params", bad.Message);
        Assert.Equal(Obj(("x", 1L)), bad.Data);
        var bare = RpcFailure(calls[2].Value);
        Assert.Equal(-32603, bare.Code);
        Assert.Equal("The agent returned an error.", bare.Message);
        Assert.Equal("The agent returned an error.", SwiftErrors.LocalizedDescription(bare));
        Assert.Null(bare.Data);
        Assert.Equal(new JSONValue.Int(5), Resolved(calls[3].Value));
        Assert.Equal(new JSONValue.Null(), Resolved(calls[4].Value));
        var truncated = RpcFailure(calls[5].Value);
        Assert.Equal(7, truncated.Code);
        Assert.Equal("truncated id", truncated.Message);

        runtime.OnMain(() => connection.Notify("bye"));
        runtime.Loop.RunUntil(() => connection.IsClosed, Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-148")] // Core/Support/JSONRPC.swift:141-150
    public void Closing_fails_every_pending_request_with_one_error_carrying_the_trimmed_stderr_tail()
    {
        using var runtime = new TestRuntime();
        var process = Child(Pipes + "$e.Write(\"`r`n  boom: agent crashed `r`n`t\"); $e.Flush(); " +
            "for($n=0; $n -lt 3; $n++){ $l=$i.ReadLine() }");
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var closes = new List<string>();
        var closedFirst = false;
        Exception? lateRequest = null;
        connection.OnClose = tail =>
        {
            closes.Add(tail);
            closedFirst = connection.IsClosed;
            var late = connection.Request("late", Obj());
            lateRequest = late.Exception?.InnerException;
        };
        runtime.OnMain(connection.Start);
        TaskHandle<JSONValue>? first = null, second = null;
        runtime.OnMain(() =>
        {
            first = MainActor.SpawnThrowing(() => connection.Request("a", Obj()));
            second = MainActor.SpawnThrowing(() => connection.Request("b", Obj()));
        });
        runtime.Loop.RunUntil(() => process.ErrorTail.Contains("boom", StringComparison.Ordinal), Bound);
        Assert.False(connection.IsClosed);
        runtime.OnMain(() => connection.Notify("exit"));
        runtime.Loop.RunUntil(() => first!.Value.IsCompleted && second!.Value.IsCompleted && closes.Count > 0, Bound);

        var a = RpcFailure(first!.Value);
        var b = RpcFailure(second!.Value);
        Assert.Same(a, b);
        Assert.Equal(-32000, a.Code);
        Assert.Equal("boom: agent crashed", a.Message);
        Assert.Null(a.Data);
        Assert.Equal(["boom: agent crashed"], closes);
        Assert.True(closedFirst);
        var notRunning = Assert.IsType<RPCError>(lateRequest);
        Assert.Equal("The agent process is not running.", notRunning.Message);
        Assert.True(connection.IsClosed);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-148")] // Core/Support/JSONRPC.swift:144-149
    [Trait("Req", "UI-PROVACP-033")] // Core/Support/JSONRPC.swift:145
    public void A_blank_stderr_tail_fails_pending_requests_with_the_agent_process_exited()
    {
        using var runtime = new TestRuntime();
        var process = Child(Pipes + "$e.Write(\" `r`n`t \"); $e.Flush(); for($n=0; $n -lt 2; $n++){ $l=$i.ReadLine() }");
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var closes = new List<string>();
        connection.OnClose = closes.Add;
        runtime.OnMain(connection.Start);
        TaskHandle<JSONValue>? pending = null;
        runtime.OnMain(() => pending = MainActor.SpawnThrowing(() => connection.Request("a", Obj())));
        runtime.Loop.RunUntil(() => process.ErrorTail.Length > 0, Bound);
        runtime.OnMain(() => connection.Notify("exit"));
        runtime.Loop.RunUntil(() => pending!.Value.IsCompleted && closes.Count > 0, Bound);

        var error = RpcFailure(pending!.Value);
        Assert.Equal(-32000, error.Code);
        Assert.Equal("The agent process exited.", error.Message);
        Assert.Equal("The agent process exited.", SwiftErrors.LocalizedDescription(error));
        Assert.Equal([""], closes);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-148")] // Core/Support/JSONRPC.swift:101-103,141-150
    public void Close_terminates_the_process_and_the_connection_closes_once()
    {
        using var runtime = new TestRuntime();
        var process = Child(Pipes + LogUntilBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var closes = 0;
        connection.OnClose = _ => closes++;
        runtime.OnMain(connection.Start);
        runtime.OnMain(() => connection.Notify("hello"));
        Frames(runtime, process, 1);
        runtime.OnMain(connection.Close);
        runtime.Loop.RunUntil(() => connection.IsClosed, Bound);
        runtime.RunUntilIdle();
        Assert.Equal(1, closes);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-141")] // Core/Support/JSONRPC.swift:31-37,153-157
    public void RPCError_description_is_its_message_and_the_short_init_has_no_data()
    {
        var error = new RPCError(code: -32000, message: "The agent process is not running.");
        Assert.Equal(-32000, error.Code);
        Assert.Null(error.Data);
        Assert.Equal("The agent process is not running.", error.ErrorDescription);
        var full = new RPCError(code: 1, message: "m", data: new JSONValue.Int(2));
        Assert.Equal(new JSONValue.Int(2), full.Data);
        Assert.Equal("m", SwiftErrors.LocalizedDescription(full));
    }
}
