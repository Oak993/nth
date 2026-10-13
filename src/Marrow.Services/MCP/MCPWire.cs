using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/MCP/MCPWire.swift (B3 52bc7926), :1-246. New at 1.7.0 (B3_PLAN.md N5): the wire
// pieces MCPHub and MCPProbe share, extracted from B2 MCPHub.swift:161-172,193-221,256-310,393-418,455-533 and
// MCPProbe.swift:64-107,134-236,287-348,381-430. MCPProbe now calls this type (A16, this lane); MCPHub does
// not yet (A15 switches it over without otherwise changing its behaviour, B3_PLAN.md row N5).

namespace Marrow.Services.MCP;

/// <summary>
/// :6-104 MCPWire: static helpers shared by the hub and the probe. The HTTP POST returns at the first SSE
/// message with a matching id, so a stream the server holds open does not hang it.
/// </summary>
public static class MCPWire
{
    // :7
    public const string ProtocolVersion = "2025-06-18";

    // :9-15. DEVIATION OD-0012 F1: clientInfo.name is "Marrow", not the Swift literal "Droppy Code"; every
    // other member (protocolVersion, the empty capabilities object, version "1.0") is verbatim.
    public static JSONValue InitializeParams() => JSONValue.FromDictionaryLiteral(
        ("protocolVersion", new JSONValue.String(ProtocolVersion)),
        ("capabilities", JSONValue.FromDictionaryLiteral()),
        ("clientInfo", JSONValue.FromDictionaryLiteral(
            ("name", new JSONValue.String("nth")), // DEVIATION OD-0012 F1: Swift literal is "Droppy Code".
            ("version", new JSONValue.String("1.0")))));

    // :17-25. params is added only when non-null (a present JSON null is never sent here: the Swift `if let
    // params` skips a nil params entirely, unlike JSONRPCConnection.Write's `.null` removal rule).
    public static JSONValue Request(int id, string method, JSONValue? @params)
    {
        var fields = new Dictionary<string, JSONValue>
        {
            ["jsonrpc"] = new JSONValue.String("2.0"),
            ["id"] = new JSONValue.Int(id),
            ["method"] = new JSONValue.String(method),
        };
        if (@params is { } value) fields["params"] = value;
        return new JSONValue.Object(new ValueMap<string, JSONValue>(fields, SwiftStringComparer.Instance));
    }

    // :27-29
    public static JSONValue Notification(string method) => JSONValue.FromDictionaryLiteral(
        ("jsonrpc", new JSONValue.String("2.0")), ("method", new JSONValue.String(method)));

    // :31-34
    public static string? ErrorMessage(JSONValue value)
    {
        if (value["error"] is not { } error || error.JSONValueObject is null) return null;
        return error["message"]?.JSONValueString ?? "unknown error";
    }

    // :36-51. Paging for tools/list: ten pages at most, each cursor fed back until the server stops handing
    // one out. The caller owns the request and its error type (page may throw whatever the transport throws).
    public static async Task<IReadOnlyList<JSONValue>> ListTools(Func<JSONValue, Task<JSONValue>> page)
    {
        var tools = new List<JSONValue>();
        string? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var fields = new Dictionary<string, JSONValue>();
            if (cursor is { } c) fields["cursor"] = new JSONValue.String(c);
            var reply = await page(new JSONValue.Object(new ValueMap<string, JSONValue>(fields, SwiftStringComparer.Instance)));
            var list = reply["result"];
            if (list?["tools"]?.JSONValueArray is { } page_tools) tools.AddRange(page_tools);
            var next = list?["nextCursor"]?.JSONValueString;
            if (string.IsNullOrEmpty(next)) break;
            cursor = next;
        }
        return tools;
    }

    // MARK: - Streamable HTTP (:53-103)

    // :55-103. DEVIATION OD-0022/ED-152, now enforced by ED-209: F.HTTP (Marrow.Core.Port.Http.cs) drops the
    // Authorization header across an origin-changing redirect (ED-152, Http.cs:Redirected); ED-209 extends it
    // with a per-request marking (URLRequest.MarkOriginBound) for an arbitrary named header. Each of this call's
    // caller-supplied `headers` (a server's own custom headers, e.g. X-Api-Key) is marked below, so a redirect
    // that changes scheme, host or port drops it the same way Authorization is dropped, and a same-server
    // redirect keeps it, exactly as OD-0022 requires. MCPProbe.cs's own Post (:150-174 as of A16, this batch)
    // now calls this same MCPWire.Post rather than carrying an inline copy, so it is covered by this same
    // marking with no separate fix needed there; its comment is updated alongside this merge (B3 batch 4) to
    // stop describing the gap as still open.
    public static async Task<MCPHTTPReply> Post(
        Uri to, IReadOnlyDictionary<string, string> headers, string? sessionID, JSONValue payload, int? id, double timeout)
    {
        // :63-70
        var request = new URLRequest(to, timeout) with { HttpMethod = "POST", HttpBody = payload.Data() };
        foreach (var (field, value) in headers) request = request.SetValue(value, field).MarkOriginBound(field);
        request = request.SetValue("application/json", "Content-Type");
        request = request.SetValue("application/json, text/event-stream", "Accept");
        request = request.SetValue(ProtocolVersion, "MCP-Protocol-Version");
        if (sessionID is { } sid) request = request.SetValue(sid, "Mcp-Session-Id");

        // :72
        var (bytes, response) = await URLSession.Shared.Bytes(request);
        // :73-75
        if (response is not HTTPURLResponse http) throw new MCPHTTPError(null, "");
        // :76
        var session = http.Value("Mcp-Session-Id");
        // :77-84
        if (http.StatusCode is < 200 or >= 300)
        {
            var data = new List<byte>();
            await foreach (var b in bytes)
            {
                data.Add(b);
                if (data.Count >= 2_000) break;
            }
            throw new MCPHTTPError(http.StatusCode, SwiftText.DecodeUtf8Repairing(data.ToArray()).Trimmed());
        }
        // :85-90
        var contentType = http.Value("Content-Type") ?? "";
        if (!contentType.Contains("text/event-stream", StringComparison.Ordinal))
        {
            var data = new List<byte>();
            await foreach (var b in bytes) data.Add(b);
            if (data.Count == 0) return new MCPHTTPReply(session, null);
            return new MCPHTTPReply(session, JSONValue.Parse(data.ToArray()));
        }
        // :92. A notification (no id) never reads the event stream: the AsyncBytes is released instead of
        // left to drain forever, closing the exchange behind it (Core/Port/Http.cs:930-1038's Release, reached
        // by disposing an enumerator that never advanced).
        if (id is not { } wantedID)
        {
            await using var _ = bytes.GetAsyncEnumerator();
            return new MCPHTTPReply(session, null);
        }
        // :93-101
        await foreach (var line in bytes.Lines)
        {
            var trimmed = FoundationText.Trim(line);
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal)) continue;
            var text = FoundationText.Trim(trimmed[5..]);
            if (JSONValue.Parse(text) is not { } value || value.JSONValueObject is null || value["id"]?.JSONValueInt != wantedID) continue;
            return new MCPHTTPReply(session, value);
        }
        // :102
        return new MCPHTTPReply(session, null);
    }
}

/// <summary>:106-109 MCPHTTPReply (R-VAL-1: a Sendable struct -> an immutable record).</summary>
public sealed record MCPHTTPReply(string? SessionID, JSONValue? Body);

/// <summary>:111-114 MCPHTTPError, a plain Error (not LocalizedError; no ErrorDescription), R-VAL-1/R-NAME-5.</summary>
public sealed class MCPHTTPError(int? status, string body) : Exception
{
    public int? Status { get; } = status;
    public string Body { get; } = body;
}

/// <summary>:116-119 MCPWireError, an associated-value enum kept as a Case union (R-ENUM-2), not LocalizedError.</summary>
public sealed class MCPWireError : Exception
{
    private MCPWireError(Case errorCase) => ErrorCase = errorCase;

    public abstract record Case
    {
        private Case() { }

        // :117
        public sealed record Exited(int? Status) : Case;

        // :118
        public sealed record TimedOut : Case;
    }

    public Case ErrorCase { get; }

    public static MCPWireError Exited(int? status) => new(new Case.Exited(status));

    public static MCPWireError TimedOut { get; } = new(new Case.TimedOut());
}

/// <summary>
/// :121-246 A stdio MCP server; each request waits on a continuation for the reply with its id. Sendable over
/// a Mutex&lt;State&gt; (R-ISO-5); the class itself has no isolation (nonisolated members throughout).
/// </summary>
public sealed class MCPStdioConnection
{
    // :125-131
    private struct State
    {
        public int NextID = 1;
        public Dictionary<RPCID, CheckedContinuation<JSONValue>> Pending = [];
        public Dictionary<RPCID, JSONValue> Buffered = [];
        public int? ExitStatus;
        public bool Closed;

        public State() { }
    }

    // :123
    public StdioProcess Process { get; }

    // :133
    private readonly Mutex<State> state = new(new State());

    // :135-137
    private MCPStdioConnection(StdioProcess process) => Process = process;

    // :139-150. ED-194 (B3 amendment of ED-047 (b)/ED-126): StdioProcess already spawns every child inside a
    // Job Object with no limits (kill-tree at quit only; stop stays the pid-only ladder). ProcessControl is not
    // exposed here: this connection's only lifetime hooks are Stop (below) and the exit report Read() gives
    // Close(); a caller that needs MCPLiveProcesses-style tracking reaches it through Process.ProcessControl.
    public static MCPStdioConnection Launch(string executable, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var process = new StdioProcess(executable, arguments, LoginEnvironment.HomeDirectory, environment);
        var connection = new MCPStdioConnection(process);
        process.Start();
        connection.Read();
        return connection;
    }

    /// <summary>
    /// PORT-ONLY (ED-NEW-perf-ui-thread-7). <see cref="Launch"/> with the child created on the pool (<see cref="StdioProcess.StartAsync"/>), for a caller on the UI thread's turn
    /// (the connection probe). A launch failure reaches the awaiting caller as the same exception. The hub's own clients run on an actor strand over the pool, not the UI thread, and keep Launch.
    /// </summary>
    public static async Task<MCPStdioConnection> LaunchAsync(string executable, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var process = new StdioProcess(executable, arguments, LoginEnvironment.HomeDirectory, environment);
        var connection = new MCPStdioConnection(process);
        await process.StartAsync();
        connection.Read();
        return connection;
    }

    // :152-154. ED-126/ED-194: the stop ladder (close stdin, terminate, +3s pid-only kill); the job object is
    // never used here, only at quit (MCPLiveProcesses, lane A15).
    public void Stop() => Process.Terminate();

    // :156
    public string ErrorTail => Process.ErrorTail;

    // :158-160
    public void Notify(string method) => Process.Send(MCPWire.Notification(method));

    // :162-187
    public async Task<JSONValue> Request(string method, JSONValue? @params, double timeout)
    {
        // :163
        if (!Process.IsRunning) throw MCPWireError.Exited(await Process.WaitForExit());
        // :164-168
        var id = state.WithLock((ref State s) =>
        {
            var value = s.NextID;
            s.NextID += 1;
            return value;
        });
        var key = new RPCID.Int(id);
        // :170-186. ENGINEERING DECISION: withThrowingTaskGroup has no port type (SwiftTask.cs's TaskGroup<T>,
        // :243-353, only supports non-throwing children); the race between "a reply arrives" (AwaitReply) and
        // "the timer fires" (a per-request sleep) is ported over two TaskHandle.DetachedThrowing children
        // (R-TASK-1), each with its own cancellation, bridged to the ambient Swift task's own cancellation
        // (Register below) so a cancelled caller still ends both, matching R-TASK-5's "children see the
        // parent's cancellation". Whichever finishes first (a value, or either side's thrown error) decides; on
        // any failure both handles are cancelled and the id is abandoned before the error reaches the caller,
        // exactly as :178-184 does.
        var reply = TaskHandle.DetachedThrowing(() => AwaitReply(key));
        var timer = TaskHandle.DetachedThrowing<JSONValue>(async () =>
        {
            await SwiftTask.Sleep(SwiftTask.Seconds(timeout));
            throw MCPWireError.TimedOut;
        });
        using var propagateCancel = SwiftTask.CancellationToken.Register(() =>
        {
            reply.Cancel();
            timer.Cancel();
        });
        // :176
        Process.Send(MCPWire.Request(id, method, @params));
        try
        {
            // :178-181
            var winner = await Task.WhenAny(reply.Value, timer.Value);
            reply.Cancel();
            timer.Cancel();
            return await winner;
        }
        catch
        {
            // :182-184
            reply.Cancel();
            timer.Cancel();
            Abandon(key);
            throw;
        }
    }

    // :189-196. R-TASK-1: nonisolated `Task {}` -> TaskHandle.Detached (this class carries no isolation).
    private void Read()
    {
        var messages = Process.Messages;
        TaskHandle.Detached(async () =>
        {
            // :192
            await foreach (var message in messages) Handle(message);
            // :193-194
            var status = await Process.WaitForExit();
            Close(status);
        });
    }

    // :198-217
    private async Task<JSONValue> AwaitReply(RPCID id)
    {
        return await SwiftTask.WithCancellationHandler(async () =>
        {
            // :200
            SwiftTask.CheckCancellation();
            // :201-213
            return await CheckedContinuation<JSONValue>.With(continuation =>
            {
                var immediate = state.WithLock((ref State s) =>
                {
                    // :203
                    if (s.Buffered.Remove(id, out var buffered)) return new Immediate(ImmediateKind.Value, buffered, null);
                    // :204
                    if (s.Closed) return new Immediate(ImmediateKind.Error, null, MCPWireError.Exited(s.ExitStatus));
                    // :205
                    s.Pending[id] = continuation;
                    return new Immediate(ImmediateKind.None, null, null);
                });
                // :208-211
                switch (immediate.Kind)
                {
                    case ImmediateKind.Value: continuation.Resume(immediate.Value!); break;
                    case ImmediateKind.Error: continuation.Resume(immediate.Error!); break;
                }
            });
            // :214-215 onCancel below
        }, () => Abandon(id));
    }

    private enum ImmediateKind { None, Value, Error }

    private readonly record struct Immediate(ImmediateKind Kind, JSONValue? Value, Exception? Error);

    // :219-228
    private void Handle(JSONValue message)
    {
        // :220
        if (message.JSONValueObject is not { } @object) return;
        // :221
        if (!@object.TryGetValue("id", out var rawID) || RPCID.Create(rawID) is not { } id) return;
        // :222-226
        var continuation = state.WithLock((ref State s) =>
        {
            if (s.Pending.Remove(id, out var found)) return found;
            s.Buffered[id] = message;
            return null;
        });
        // :227
        continuation?.Resume(message);
    }

    // :230-233
    private void Abandon(RPCID id)
    {
        var continuation = state.WithLock((ref State s) => s.Pending.Remove(id, out var found) ? found : null);
        continuation?.Resume(new CancellationError());
    }

    // :235-245
    private void Close(int? status)
    {
        var pending = state.WithLock((ref State s) =>
        {
            // :237
            if (s.Closed) return System.Array.Empty<CheckedContinuation<JSONValue>>();
            // :238-241
            s.Closed = true;
            s.ExitStatus = status;
            var values = s.Pending.Values.ToArray();
            s.Pending.Clear();
            return values;
        });
        // :244
        foreach (var continuation in pending) continuation.Resume(MCPWireError.Exited(status));
    }
}
