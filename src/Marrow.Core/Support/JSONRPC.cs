using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/JSONRPC.swift (B3 52bc792), :1-157.

/// <summary>:3-29 A JSON-RPC id: an integer or a string (associated-value enum, R-ENUM-2).</summary>
public abstract record RPCID
{
    private RPCID() { }

    // :4
    public sealed record Int(long Value) : RPCID;

    // :5. Hashable over Swift String equality (R-STR-2).
    public sealed record String(string Value) : RPCID
    {
        public bool Equals(String? other) => other is not null && SwiftString.Equal(Value, other.Value);

        public override int GetHashCode() => SwiftStringComparer.Instance.GetHashCode(Value);
    }

    // :7-14. init?(_ value: JSONValue); a double is truncated toward zero (Int(id)).
    public static RPCID? Create(JSONValue value) => value switch
    {
        JSONValue.Int id => new Int(id.Value),
        JSONValue.Double id => new Int(SwiftNumber.TruncatingToInt(id.Value)),
        JSONValue.String id => new String(id.Value),
        _ => null,
    };

    // :16-21
    public JSONValue Json => this switch
    {
        Int id => new JSONValue.Int(id.Value),
        String id => new JSONValue.String(id.Value),
        _ => throw new System.Diagnostics.UnreachableException(),
    };

    // :23-28
    public string Key => this switch
    {
        Int id => id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        String id => id.Value,
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}

/// <summary>:31-37 RPCError, a LocalizedError whose errorDescription is its message.</summary>
public sealed class RPCError : SwiftError
{
    // :31-37 memberwise init(code:message:data:)
    public RPCError(long code, string message, JSONValue? data)
    {
        Code = code;
        message_ = message;
        Data = data;
    }

    // :153-157 init(code:message:) with data nil
    public RPCError(long code, string message) : this(code, message, null) { }

    private readonly string message_;

    // :32
    public long Code { get; }

    // :33. Exception.Message is overridden to the Swift member (it equals errorDescription, :36).
    public override string Message => message_;

    // :34. Hides Exception.Data with the Swift member.
    public new JSONValue? Data { get; }

    // :36
    public override string? ErrorDescription => message_;
}

/// <summary>:39-151 JSON-RPC 2.0 over a stdio process. Codex omits the <c>jsonrpc</c> member, ACP agents require it.</summary>
public sealed class JSONRPCConnection
{
    // :46
    public bool IsClosed { get; private set; }

    private readonly StdioProcess process;
    private bool childStarted;
    // PORT-ONLY RR-15: protocol EOF/Close is not process exit.
    public Task ChildExited => childStarted ? process.WaitForExit() : Task.CompletedTask;
    private readonly bool sendsVersion;
    private long nextID = 1;
    private readonly Dictionary<RPCID, CheckedContinuation<JSONValue>> pending = [];
    private TaskHandle? readTask;

    // :53-56
    public JSONRPCConnection(StdioProcess process, bool sendsVersion)
    {
        this.process = process;
        this.sendsVersion = sendsVersion;
    }

    // :42
    public Action<string, JSONValue>? OnNotification { get; set; }

    // :43
    public Action<RPCID, string, JSONValue>? OnRequest { get; set; }

    // :44
    public Action<string>? OnClose { get; set; }

    // :58-68
    public void Start()
    {
        process.Start();
        BeginReading();
    }

    /// <summary>
    /// PORT-ONLY (ED-NEW-perf-ui-thread-6). <see cref="Start"/> with the child created on the pool (<see cref="StdioProcess.StartAsync"/>), so a session started from the UI thread's turn does
    /// not hold a frame for CreateProcessW. A <see cref="Close"/> that came while the child was being created found nothing running to end, so the child is ended once it exists.
    /// </summary>
    public async Task StartAsync()
    {
        await process.StartAsync();
        BeginReading();
        if (closeRequested) process.Terminate();
    }

    private bool closeRequested;

    private void BeginReading()
    {
        childStarted = true;
        var messages = process.Messages;
        var weak = new WeakReference<JSONRPCConnection>(this);
        readTask = MainActor.Spawn(async () =>
        {
            await foreach (var message in messages)
            {
                if (!weak.TryGetTarget(out var self)) return;
                self.Handle(message);
            }
            if (weak.TryGetTarget(out var owner)) owner.DidClose();
        });
    }

    // :70 request(_ method:) with params defaulting to .null
    public Task<JSONValue> Request(string method) => Request(method, new JSONValue.Null());

    // :70-87
    public async Task<JSONValue> Request(string method, JSONValue @params)
    {
        // :71-73
        if (IsClosed)
        {
            throw new RPCError(code: -32000, message: "The agent process is not running.");
        }
        // :74-75
        var id = new RPCID.Int(nextID);
        nextID = checked(nextID + 1);
        var weak = new WeakReference<JSONRPCConnection>(this);
        return await SwiftTask.WithCancellationHandler(async () =>
        {
            // :77-81
            SwiftTask.CheckCancellation();
            return await CheckedContinuation<JSONValue>.With(continuation =>
            {
                pending[id] = continuation;
                Write(new Dictionary<string, JSONValue>
                {
                    ["id"] = id.Json,
                    ["method"] = new JSONValue.String(method),
                    ["params"] = @params,
                });
            });
        }, () =>
        {
            // :82-85 Task { @MainActor [weak self] in ... }
            MainActor.Spawn(() =>
            {
                if (weak.TryGetTarget(out var self) && self.pending.Remove(id, out var continuation))
                {
                    continuation.Resume(new CancellationError());
                }
                return Task.CompletedTask;
            });
        });
    }

    // :89 notify(_ method:) with params defaulting to .null
    public void Notify(string method) => Notify(method, new JSONValue.Null());

    // :89-91
    public void Notify(string method, JSONValue @params)
    {
        Write(new Dictionary<string, JSONValue>
        {
            ["method"] = new JSONValue.String(method),
            ["params"] = @params,
        });
    }

    // :93-95
    public void Respond(RPCID to, JSONValue result)
    {
        Write(new Dictionary<string, JSONValue>
        {
            ["id"] = to.Json,
            ["result"] = result,
        });
    }

    // :97-99
    public void Respond(RPCID to, long errorCode, string message)
    {
        Write(new Dictionary<string, JSONValue>
        {
            ["id"] = to.Json,
            ["error"] = JSONValue.FromDictionaryLiteral(("code", new JSONValue.Int(errorCode)), ("message", new JSONValue.String(message))),
        });
    }

    // :101-103
    public void Close()
    {
        closeRequested = true;
        process.Terminate();
    }

    // :105-111
    private void Write(Dictionary<string, JSONValue> message)
    {
        if (IsClosed) return;
        // :108 message["params"] == .null: only a present JSON null is removed.
        if (message.TryGetValue("params", out var parameters) && parameters is JSONValue.Null) message.Remove("params");
        if (sendsVersion) message["jsonrpc"] = new JSONValue.String("2.0");
        process.Send(new JSONValue.Object(new ValueMap<string, JSONValue>(message, SwiftStringComparer.Instance)));
    }

    // :113-139
    private void Handle(JSONValue message)
    {
        // :114
        if (message.JSONValueObject is not { } @object) return;
        // :115-127
        if (Lookup(@object, "method")?.JSONValueString is { } method)
        {
            var @params = Lookup(@object, "params") ?? new JSONValue.Null();
            if (Lookup(@object, "id") is { } rawID && RPCID.Create(rawID) is { } id)
            {
                if (OnRequest is { } onRequest)
                {
                    onRequest(id, method, @params);
                }
                else
                {
                    Respond(to: id, errorCode: -32601, message: "Method not found");
                }
            }
            else
            {
                OnNotification?.Invoke(method, @params);
            }
            return;
        }
        // :128-129
        if (Lookup(@object, "id") is not { } replyID || RPCID.Create(replyID) is not { } key
            || !pending.Remove(key, out var continuation)) return;
        // :130-138
        if (Lookup(@object, "error") is { } error && !error.IsNull)
        {
            continuation.Resume(new RPCError(
                code: error["code"]?.JSONValueInt ?? -32603,
                message: error["message"]?.JSONValueString ?? "The agent returned an error.",
                data: error["data"]
            ));
        }
        else
        {
            continuation.Resume(Lookup(@object, "result") ?? new JSONValue.Null());
        }
    }

    // :141-150
    private void DidClose()
    {
        if (IsClosed) return;
        IsClosed = true;
        var tail = process.ErrorTail.Trimmed();
        var failure = new RPCError(code: -32000, message: tail.Length == 0 ? "The agent process exited." : tail);
        var waiting = pending.Values.ToArray();
        pending.Clear();
        foreach (var continuation in waiting) continuation.Resume(failure);
        OnClose?.Invoke(tail);
    }

    // object[key] on [String: JSONValue] (R-COLL-3).
    private static JSONValue? Lookup(IReadOnlyDictionary<string, JSONValue> @object, string key) =>
        @object.TryGetValue(key, out var value) ? value : null;
}
