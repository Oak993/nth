using System.Diagnostics;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;

// Source: DroppyCode/Services/MCP/MCPHub.swift (B2 1fbf81e), :1-558.

namespace Marrow.Services.MCP;

// :4-16. An error enum keeps its cases as a nested record union (R-ERR-3).
public sealed class MCPHubError : SwiftError
{
    private MCPHubError(Case errorCase) => ErrorCase = errorCase;

    public abstract record Case
    {
        private Case() { }

        // :5
        public sealed record UnknownTool(string Name) : Case;

        // :6
        public sealed record Server(string Message) : Case;
    }

    public Case ErrorCase { get; }

    // :8-15
    public override string? ErrorDescription => ErrorCase switch
    {
        Case.UnknownTool unknownTool => $"Unknown tool: {unknownTool.Name}.", // :10-11
        Case.Server server => server.Message,                                 // :12-13
        _ => throw new UnreachableException(),
    };

    public static MCPHubError UnknownTool(string name) => new(new Case.UnknownTool(name));

    public static MCPHubError Server(string message) => new(new Case.Server(message));
}

// :18-150
public sealed class MCPHub : Actor
{
    // :19
    public static readonly MCPHub Shared = new();

    // :21-24. Every server process started this run, so the app can end them as it quits: a Process child
    // outlives its parent, and an npx server left behind would keep running until the machine restarts.
    private static readonly MCPLiveProcesses liveProcesses = new();

    // :26
    public static void Track(IProcessControl process) => liveProcesses.Add(process);

    // :28-30. Ends every running server at once. Called from the app-quit path, where nothing asynchronous
    // would get to run.
    public static void TerminateAll() => liveProcesses.TerminateAll();

    // :32-39
    public sealed record Tool(string Server, string Name, string Description, JSONValue InputSchema)
    {
        // :38
        public string CallName => "mcp__" + Server + "__" + Name;
    }

    // :41-44
    public sealed record Result(string Text, bool IsError);

    private Dictionary<string, MCPServerSpec>? specs;
    private readonly Dictionary<string, MCPClient> clients = new(SwiftStringComparer.Instance);
    private readonly Dictionary<string, List<Tool>> toolCache = new(SwiftStringComparer.Instance);
    private readonly HashSet<string> failed = new(SwiftStringComparer.Instance);

    // :51-77
    public Task<IReadOnlyList<Tool>> Tools() => Isolated(async () =>
    {
        specs ??= ReadSpecs();
        var current = specs;
        var all = new List<Tool>();
        foreach (var id in current.Keys.OrderBy(key => key, SwiftStringComparer.Instance))
        {
            if (toolCache.TryGetValue(id, out var cached))
            {
                all.AddRange(cached);
                continue;
            }
            if (failed.Contains(id)) continue;
            if (!current.TryGetValue(id, out var spec)) continue;
            var client = Client(id, spec);
            try
            {
                var listed = await client.ListTools();
                var listedList = listed.ToList();
                toolCache[id] = listedList;
                all.AddRange(listedList);
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            {
                // :68
                Console.WriteLine($"MCPHub: {id}: {SwiftErrors.LocalizedDescription(error)}");
                failed.Add(id);
                await client.Stop();
            }
        }
        return (IReadOnlyList<Tool>)all
            .OrderBy(tool => tool.Server, SwiftStringComparer.Instance)
            .ThenBy(tool => tool.Name, SwiftStringComparer.Instance)
            .ToList();
    });

    // :79-98
    public Task<Result> Call(string callName, JSONValue arguments) => Isolated(async () =>
    {
        specs ??= ReadSpecs();
        if (Parse(callName) is not { } parsed || !specs.TryGetValue(parsed.Server, out var spec))
            throw MCPHubError.UnknownTool(callName);
        if (toolCache.TryGetValue(parsed.Server, out var cached) &&
            !cached.Any(tool => SwiftString.Equal(tool.Name, parsed.Tool)))
        {
            throw MCPHubError.UnknownTool(callName);
        }
        var client = Client(parsed.Server, spec);
        try
        {
            return await client.CallTool(parsed.Tool, arguments);
        }
        catch (MCPHubError)
        {
            throw;
        }
        catch (CancellationError)
        {
            throw new CancellationError();
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            throw MCPHubError.Server(SwiftErrors.LocalizedDescription(error));
        }
    });

    // The tests' seam (as LoginEnvironment.Which, Shell.cs:701): seeds specs directly, bypassing
    // MCPPaths.ClaudeConfigURL, so tools()/call()/reload() can be tested without depending on
    // Services/Store/Storage.swift (an unbuilt unit as of this writing, PORTING/work/packets4/Services.Store.Storage).
    // Production code never calls this; it always goes through ReadSpecs() above.
    internal Task Seed(Dictionary<string, MCPServerSpec> testSpecs) => Isolated(() => { specs = testSpecs; });

    // :100-107
    public Task Reload() => Isolated(() =>
    {
        var existing = new Dictionary<string, MCPClient>(clients, SwiftStringComparer.Instance);
        clients.Clear();
        toolCache.Clear();
        failed.Clear();
        specs = null;
        foreach (var client in existing.Values) Spawn(async () => await client.Stop());
    });

    // :109-117
    internal static (string Server, string Tool)? Parse(string callName)
    {
        const string prefix = "mcp__";
        if (!SwiftString.HasPrefix(callName, prefix)) return null;
        var rest = SwiftString.DropFirst(callName, prefix.Length);
        var separator = rest.IndexOf("__", StringComparison.Ordinal);
        if (separator < 0) return null;
        var server = rest[..separator];
        var tool = rest[(separator + 2)..];
        if (server.Length == 0 || tool.Length == 0) return null;
        return (server, tool);
    }

    // :119-124
    private MCPClient Client(string id, MCPServerSpec spec)
    {
        if (clients.TryGetValue(id, out var existing)) return existing;
        var created = new MCPClient(id, spec);
        clients[id] = created;
        return created;
    }

    // :126-149
    private static Dictionary<string, MCPServerSpec> ReadSpecs() => ReadSpecs(MCPPaths.ClaudeConfigURL);

    // The tests' seam (as LoginEnvironment.Which, Shell.cs:701); the parameterless overload above passes
    // MCPPaths.ClaudeConfigURL, the production path.
    internal static Dictionary<string, MCPServerSpec> ReadSpecs(string configPath)
    {
        var output = new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance);
        var data = SwiftTry.Optional(() => File.ReadAllBytes(configPath));
        if (data is null) return output;
        if (JSONValue.Parse(data) is not { } parsed) return output;
        if (parsed["mcpServers"] is not JSONValue.Object serversValue) return output;
        foreach (var (id, entry) in serversValue.Value)
        {
            if (entry["command"] is JSONValue.String { Value.Length: > 0 } commandValue)
            {
                var args = new List<string>();
                if (entry["args"] is JSONValue.Array argsArray)
                {
                    foreach (var item in argsArray.Value)
                        if (item is JSONValue.String argument) args.Add(argument.Value);
                }
                var env = new Dictionary<string, string>(SwiftStringComparer.Instance);
                if (entry["env"] is JSONValue.Object envObject)
                {
                    foreach (var (key, value) in envObject.Value)
                        if (value is JSONValue.String envValue) env[key] = envValue.Value;
                }
                output[id] = new MCPServerSpec.Stdio(commandValue.Value, new ValueList<string>(args),
                    new ValueMap<string, string>(env, SwiftStringComparer.Instance));
            }
            else if (entry["url"] is JSONValue.String { Value.Length: > 0 } urlValue)
            {
                var headers = new Dictionary<string, string>(SwiftStringComparer.Instance);
                if (entry["headers"] is JSONValue.Object headersObject)
                {
                    foreach (var (key, value) in headersObject.Value)
                        if (value is JSONValue.String headerValue) headers[key] = headerValue.Value;
                }
                output[id] = new MCPServerSpec.Http(urlValue.Value,
                    new ValueMap<string, string>(headers, SwiftStringComparer.Instance));
            }
            // else: a member with neither command nor url is skipped (:146-147 falls through with no assignment).
        }
        return output;
    }
}

// :152-155
internal abstract record MCPServerSpec
{
    private MCPServerSpec() { }

    // :153
    public sealed record Stdio(string Command, ValueList<string> Args, ValueMap<string, string> Env) : MCPServerSpec;

    // :154
    public sealed record Http(string Url, ValueMap<string, string> Headers) : MCPServerSpec;
}

// :157-493
internal sealed class MCPClient : Actor
{
    // :158
    public string Id { get; }

    // :159
    private readonly MCPServerSpec spec;

    private ProcessLauncher? process;
    private HubConversation? conversation;
    private ProcessPipe? stdinHandle;

    // PORT-ONLY: Swift reads (process.standardOutput as? Pipe) fresh in stop(); the port keeps the pipes it
    // created so their readability handlers can be detached the same way.
    private ProcessPipe? stdoutPipe;
    private ProcessPipe? stderrPipe;

    private int nextID = 1;
    private bool initialized;
    private bool didRestart;
    private bool dead;
    private string? sessionID;

    // :169-172. Replies read off stdout for a request other than the one being waited on: two calls in
    // flight on one server each get their own, rather than the first reader dropping the other's.
    private readonly Dictionary<int, JSONValue> replies = [];

    // :174-177
    public MCPClient(string id, MCPServerSpec spec)
    {
        Id = id;
        this.spec = spec;
    }

    // :179-191
    public Task Stop() => Isolated(() =>
    {
        if (process is { } proc)
        {
            if (stdoutPipe is { } stdout) stdout.ReadabilityHandler = null;
            if (stderrPipe is { } stderr) stderr.ReadabilityHandler = null;
            TerminationLadder.Shell(proc);
        }
        process = null;
        conversation = null;
        stdinHandle = null;
        stdoutPipe = null;
        stderrPipe = null;
        initialized = false;
        sessionID = null;
        replies.Clear();
    });

    // :193-221
    public Task<IReadOnlyList<MCPHub.Tool>> ListTools() => Isolated(async () =>
    {
        var output = new List<MCPHub.Tool>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var parameters = new Dictionary<string, JSONValue>();
            if (cursor is { } current) parameters["cursor"] = new JSONValue.String(current);
            var reply = await Rpc("tools/list",
                new JSONValue.Object(new ValueMap<string, JSONValue>(parameters, SwiftStringComparer.Instance)),
                TimeSpan.FromSeconds(60));
            var list = reply["result"];
            if (list?["tools"] is JSONValue.Array toolsArray)
            {
                foreach (var item in toolsArray.Value)
                {
                    if (item["name"] is not JSONValue.String { Value: { } name }) continue;
                    var rawDescription = item["description"] is JSONValue.String { Value: { } description }
                        ? description
                        : "";
                    JSONValue schema = item["inputSchema"] is JSONValue.Object found
                        ? found
                        : new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                        {
                            ["type"] = new JSONValue.String("object"),
                            ["properties"] = new JSONValue.Object(ValueMap<string, JSONValue>.Empty),
                        }, SwiftStringComparer.Instance));
                    output.Add(new MCPHub.Tool(Id, name, SwiftString.Prefix(rawDescription, 400), schema));
                }
            }
            if (list?["nextCursor"] is not JSONValue.String { Value.Length: > 0, Value: { } next }) break;
            cursor = next;
        }
        return (IReadOnlyList<MCPHub.Tool>)output;
    });

    // :223-245
    public Task<MCPHub.Result> CallTool(string name, JSONValue arguments) => Isolated(async () =>
    {
        var args = arguments.IsNull ? new JSONValue.Object(ValueMap<string, JSONValue>.Empty) : arguments;
        var reply = await Rpc("tools/call",
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["name"] = new JSONValue.String(name),
                ["arguments"] = args,
            }, SwiftStringComparer.Instance)),
            TimeSpan.FromSeconds(120));
        var result = reply["result"];
        var lines = new List<string>();
        if (result?["content"] is JSONValue.Array contentArray)
        {
            foreach (var item in contentArray.Value)
            {
                if (item["text"] is JSONValue.String { Value: { } text })
                {
                    lines.Add(text);
                }
                else if (item["type"] is JSONValue.String { Value: "image" })
                {
                    lines.Add("[image]");
                }
                else if (item["type"] is JSONValue.String { Value: "resource" })
                {
                    var uri = item["resource"]?["uri"] is JSONValue.String { Value: { } resourceUri }
                        ? resourceUri
                        : "";
                    lines.Add($"[resource: {uri}]");
                }
            }
        }
        var isError = result?["isError"] is JSONValue.Bool { Value: true };
        return new MCPHub.Result(string.Join("\n", lines), isError);
    });

    // :247-254
    private Task<JSONValue> Rpc(string method, JSONValue? parameters, TimeSpan timeout) => spec switch
    {
        MCPServerSpec.Stdio stdio => StdioRpc(stdio.Command, stdio.Args, stdio.Env, method, parameters, timeout),
        MCPServerSpec.Http http => HttpRpc(http.Url, http.Headers, method, parameters, timeout),
        _ => throw new UnreachableException(),
    };

    // :258-264
    private static JSONValue InitializeParams() => new JSONValue.Object(new ValueMap<string, JSONValue>(
        new Dictionary<string, JSONValue>
        {
            ["protocolVersion"] = new JSONValue.String("2025-06-18"),
            ["capabilities"] = new JSONValue.Object(ValueMap<string, JSONValue>.Empty),
            ["clientInfo"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                // DEVIATION OD-0012 F1: MCPHub.swift:262 sends "Droppy Code"; every remaining occurrence the
                // app sends out becomes "Marrow".
                ["name"] = new JSONValue.String("Marrow"),
                ["version"] = new JSONValue.String("1.0"),
            }, SwiftStringComparer.Instance)),
        }, SwiftStringComparer.Instance));

    // :266-274
    private static JSONValue Request(int id, string method, JSONValue? parameters)
    {
        var fields = new Dictionary<string, JSONValue>
        {
            ["jsonrpc"] = new JSONValue.String("2.0"),
            ["id"] = new JSONValue.Int(id),
            ["method"] = new JSONValue.String(method),
        };
        if (parameters is { } value) fields["params"] = value;
        return new JSONValue.Object(new ValueMap<string, JSONValue>(fields, SwiftStringComparer.Instance));
    }

    // :276-278
    private static JSONValue Notification(string method) => new JSONValue.Object(new ValueMap<string, JSONValue>(
        new Dictionary<string, JSONValue>
        {
            ["jsonrpc"] = new JSONValue.String("2.0"),
            ["method"] = new JSONValue.String(method),
        }, SwiftStringComparer.Instance));

    // :280-284
    private static void CheckReply(JSONValue value)
    {
        if (value["error"] is not JSONValue.Object errorValue) return;
        var message = errorValue["message"] is JSONValue.String { Value: { } text } ? text : "unknown error";
        throw MCPHubError.Server(message);
    }

    // :299-310
    private async Task<JSONValue> StdioRpc(string command, ValueList<string> args, ValueMap<string, string> env,
        string method, JSONValue? parameters, TimeSpan timeout)
    {
        await EnsureStdio(command, args, env);
        if (process is not { } proc || stdinHandle is not { } stdin)
            throw MCPHubError.Server("The server quit.");
        var id = nextID;
        nextID += 1;
        var line = AppendNewline(Request(id, method, parameters).Data());
        SwiftTry.Ignore(() => stdin.Write(line));
        return await WaitStdio(proc, id, timeout);
    }

    // :312-323
    private async Task EnsureStdio(string command, ValueList<string> args, ValueMap<string, string> env)
    {
        if (dead) throw MCPHubError.Server("The server quit.");
        if (process is { IsRunning: true } && initialized) return;
        if (process is not null)
        {
            if (didRestart)
            {
                dead = true;
                throw MCPHubError.Server("The server quit.");
            }
            didRestart = true;
        }
        await StartStdio(command, args, env);
    }

    // :325-391
    private async Task StartStdio(string command, ValueList<string> args, ValueMap<string, string> env)
    {
        var loginEnvironment = LoginEnvironment.Current;
        if (LoginEnvironment.Which(command, loginEnvironment) is not { } executable)
            throw MCPHubError.Server($"{command} isn't installed.");
        // ED-128: environment dictionaries are OrdinalIgnoreCase; a name set again in another case keeps the
        // first spelling and takes the last value.
        var environment = new Dictionary<string, string>(loginEnvironment, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in env) environment[key] = value;

        var proc = new ProcessLauncher
        {
            ExecutableURL = executable,
            Arguments = args,
            Environment = environment,
            CurrentDirectoryURL = LoginEnvironment.HomeDirectory,
        };
        var stdinPipe = new ProcessPipe();
        var stdoutPipeLocal = new ProcessPipe();
        var stderrPipeLocal = new ProcessPipe();
        proc.StandardInput = stdinPipe;
        proc.StandardOutput = stdoutPipeLocal;
        proc.StandardError = stderrPipeLocal;

        var conv = new HubConversation();
        stdoutPipeLocal.ReadabilityHandler = data =>
        {
            if (data.Length == 0) stdoutPipeLocal.ReadabilityHandler = null;
            else conv.AppendStdout(data);
        };
        stderrPipeLocal.ReadabilityHandler = data =>
        {
            // :355 stderr is drained and discarded, unlike the probe, which keeps it for error messages.
            if (data.Length == 0) stderrPipeLocal.ReadabilityHandler = null;
        };
        proc.TerminationHandler = finished => conv.SetExit(finished.TerminationStatus);

        try
        {
            proc.Run();
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            stdoutPipeLocal.ReadabilityHandler = null;
            stderrPipeLocal.ReadabilityHandler = null;
            TerminationLadder.Shell(proc);
            // ENGINEERING DECISION: the record (PR-MCP-185) asks for "the system error's description"; on
            // Windows that is the launch exception's own message, since there is no NSError text to reproduce.
            throw MCPHubError.Server(error.Message);
        }
        process = proc;
        conversation = conv;
        stdinHandle = stdinPipe;
        stdoutPipe = stdoutPipeLocal;
        stderrPipe = stderrPipeLocal;
        MCPHub.Track(proc);
        try
        {
            var id = nextID;
            nextID += 1;
            var line = AppendNewline(Request(id, "initialize", InitializeParams()).Data());
            SwiftTry.Ignore(() => stdinPipe.Write(line));
            var reply = await WaitStdio(proc, id, TimeSpan.FromSeconds(60));
            if (reply["result"] is not JSONValue.Object)
                throw MCPHubError.Server("The server sent something unexpected: missing result.");
            var initializedLine = AppendNewline(Notification("notifications/initialized").Data());
            SwiftTry.Ignore(() => stdinPipe.Write(initializedLine));
            initialized = true;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            await Stop();
            if (error is MCPHubError or CancellationError) throw;
            throw MCPHubError.Server(error.Message);
        }
    }

    // :393-418
    private async Task<JSONValue> WaitStdio(ProcessLauncher proc, int id, TimeSpan timeout)
    {
        var deadline = SwiftDate.AddingTimeInterval(SwiftDate.Now, timeout.TotalSeconds);
        while (true)
        {
            if (conversation is { } conv)
            {
                foreach (var line in conv.TakeLines())
                {
                    if (JSONValue.Parse(line) is not JSONValue.Object value) continue;
                    if (value["id"]?.JSONValueInt is not { } replyId) continue;
                    replies[(int)replyId] = value;
                }
                if (replies.Remove(id, out var found))
                {
                    CheckReply(found);
                    return found;
                }
                if (conv.ExitCode() is { } code)
                    throw MCPHubError.Server($"The server quit (exit {code}).");
            }
            if (!proc.IsRunning)
                throw MCPHubError.Server("The server quit.");
            if (SwiftDate.Now >= deadline)
            {
                replies.Remove(id);
                throw MCPHubError.Server("The server didn't answer in time.");
            }
            await SwiftTask.Sleep(TimeSpan.FromMilliseconds(10));
        }
    }

    // :422-453
    private async Task<JSONValue> HttpRpc(string urlString, ValueMap<string, string> headers, string method,
        JSONValue? parameters, TimeSpan timeout)
    {
        if (SwiftURL.Create(urlString) is not { } parsed || !Uri.TryCreate(parsed.AbsoluteString, UriKind.Absolute, out var url))
            throw MCPHubError.Server("The server address is invalid.");
        if (!initialized)
        {
            var initID = nextID;
            nextID += 1;
            var posted = await Post(url, headers, Request(initID, "initialize", InitializeParams()),
                TimeSpan.FromSeconds(60));
            if (posted.Body is not { } body)
                throw MCPHubError.Server("The server sent something unexpected: empty reply.");
            CheckReply(body);
            if (body["result"] is not JSONValue.Object)
                throw MCPHubError.Server("The server sent something unexpected: missing result.");
            sessionID = posted.SessionID ?? sessionID;
            initialized = true;
            try
            {
                _ = await Post(url, headers, Notification("notifications/initialized"), TimeSpan.FromSeconds(30));
            }
            catch (CancellationError)
            {
                throw;
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            {
                // swallowed: the client carries on, initialized, even though the notification failed.
            }
        }
        var id = nextID;
        nextID += 1;
        var posted2 = await Post(url, headers, Request(id, method, parameters), timeout);
        if (posted2.Body is not { } replyBody)
            throw MCPHubError.Server("The server sent something unexpected: empty reply.");
        CheckReply(replyBody);
        return replyBody;
    }

    // :455-479
    private async Task<(string? SessionID, JSONValue? Body)> Post(Uri url, ValueMap<string, string> headers,
        JSONValue payload, TimeSpan timeout)
    {
        var request = new URLRequest(url, timeout.TotalSeconds) { HttpMethod = "POST", HttpBody = payload.Data() };
        foreach (var (field, value) in headers) request = request.SetValue(value, field);
        request = request
            .SetValue("application/json", "Content-Type")
            .SetValue("application/json, text/event-stream", "Accept")
            .SetValue("2025-06-18", "MCP-Protocol-Version");
        if (sessionID is { } sid) request = request.SetValue(sid, "Mcp-Session-Id");

        var (data, response) = await URLSession.Shared.Data(request);
        if (response is not HTTPURLResponse http)
            throw MCPHubError.Server("The server sent something unexpected: invalid reply.");
        var session = http.Value("Mcp-Session-Id");
        if (http.StatusCode is < 200 or >= 300)
            throw MCPHubError.Server($"The server answered {http.StatusCode}.");
        var contentType = http.Value("Content-Type") ?? "";
        if (contentType.Contains("text/event-stream", StringComparison.Ordinal))
            return (session, FirstSSEMessage(data));
        if (data.Length == 0) return (session, null);
        return (session, JSONValue.Parse(data));
    }

    // :481-493
    private static JSONValue? FirstSSEMessage(byte[] data)
    {
        var text = SwiftText.DecodeUtf8Repairing(data);
        foreach (var line in SwiftString.ComponentsSeparatedByNewlines(text))
        {
            var trimmed = FoundationText.Trim(line);
            if (!SwiftString.HasPrefix(trimmed, "data:")) continue;
            var payload = FoundationText.Trim(SwiftString.DropFirst(trimmed, 5));
            if (JSONValue.Parse(payload) is not { } value) continue;
            if (value is not JSONValue.Object || value["id"] is null) continue;
            return value;
        }
        return null;
    }

    private static byte[] AppendNewline(byte[] data)
    {
        var result = new byte[data.Length + 1];
        data.CopyTo(result, 0);
        result[^1] = 0x0A;
        return result;
    }
}

// :496-533. Lock-guarded buffer fed by the stdio readability handler: stdout bytes are split on newlines into
// complete lines, and the exit status is kept so a reply loop can tell the process went away. Stderr is
// drained and discarded.
internal sealed class HubConversation
{
    private struct State
    {
        public List<byte> Buffer;
        public List<byte[]> Pending;
        public int? ExitStatus;
    }

    private readonly Mutex<State> state = new(new State { Buffer = [], Pending = [] });

    public void AppendStdout(byte[] data)
    {
        state.WithLock((ref State current) =>
        {
            current.Buffer.AddRange(data);
            int newline;
            while ((newline = current.Buffer.IndexOf((byte)0x0A)) >= 0)
            {
                current.Pending.Add(current.Buffer.GetRange(0, newline).ToArray());
                current.Buffer.RemoveRange(0, newline + 1);
            }
        });
    }

    public void SetExit(int status) => state.WithLock((ref State current) => current.ExitStatus = status);

    public List<byte[]> TakeLines() => state.WithLock((ref State current) =>
    {
        var lines = current.Pending;
        current.Pending = [];
        return lines;
    });

    public int? ExitCode() => state.WithLock((ref State current) => current.ExitStatus);
}

// :535-557. The server processes alive this run, behind a lock so the quit path can reach them from any
// thread without an actor hop.
internal sealed class MCPLiveProcesses
{
    private readonly Mutex<List<IProcessControl>> state = new([]);

    public void Add(IProcessControl process)
    {
        state.WithLock((ref List<IProcessControl> processes) =>
        {
            processes.RemoveAll(candidate => !candidate.IsRunning);
            processes.Add(process);
        });
    }

    public void TerminateAll()
    {
        var running = state.WithLock((ref List<IProcessControl> processes) =>
            processes.Where(candidate => candidate.IsRunning).ToList());
        TerminationLadder.TerminateAll(running);
    }
}
