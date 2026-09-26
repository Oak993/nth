using System.Diagnostics;
using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;

// Test-only seam (InternalsVisibleTo, this lane): App.AppDelegate's quit-order tests (tests/Marrow.App.Tests/
// AppDelegateTests.cs, a different assembly, predates this lane) inject deterministic IProcessControl doubles
// (RecordingServerProcess) into MCPHub's live-process list to assert exact termination call order without
// spinning up real child processes (R-TEST-5/R-TEST-10). ED-194 narrowed the PUBLIC Track(StdioProcess) to
// match the B3 Swift signature, so those doubles now go through the internal Track(IProcessControl) overload
// below instead. Marrow.Services.Tests already had this access (MetaAPI.cs, PlanLimits.cs, WorkingTreeWatch.cs).
[assembly: InternalsVisibleTo("Marrow.App.Tests")]

// Source: DroppyCode/Services/MCP/MCPHub.swift (B3 52bc7926), :1-377. Lane A15 (B3_PLAN.md row A15, depends on
// N5/Services.MCP.MCPWire and A2/Core.Support.StdioProcess): the hub's stdio and HTTP transports move onto
// MCPWire's shared pieces (MCPStdioConnection, MCPWire.Post/Request/Notification/InitializeParams/ErrorMessage/
// ListTools). B2's own copies of those helpers, WaitStdio's 10 ms polling loop, HubConversation and the raw
// Process/Pipe fields are deleted (:377-400, :531-561, :657-693, :605-645 at the B2 baseline; see
// work/b3/lanes/A15.md's 1.6.2 -> 1.7.0 diff). ED-194 (B3 amendment of ED-047 (b)/ED-126): StdioProcess (A2)
// spawns every child inside a Job Object with no limits, so MCPLiveProcesses can KillTree() it at quit; Track
// now takes the StdioProcess itself and reaches that handle through its port-only ProcessControl property.

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

    // :21-24. Every server process started this run, so the app can end them as it quits: a child outlives its
    // parent, and an npx server left behind would keep running until the machine restarts.
    private static readonly MCPLiveProcesses liveProcesses = new();

    // :26. PORT-ONLY signature (ED-194): the Swift member takes the StdioProcess (formerly Process) directly;
    // this port reaches MCPLiveProcesses through StdioProcess.ProcessControl, the Job-Object-backed handle A2
    // exposes for exactly this caller. MCPLiveProcesses itself is unchanged (IProcessControl, R-PROC-1..3).
    public static void Track(StdioProcess process) => liveProcesses.Add(process.ProcessControl);

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests and, this lane, Marrow.App.Tests): the same
    // shape as B2's Track(IProcessControl), for a caller that wants a deterministic double (FakeProcessControl,
    // AppDelegateTests' RecordingServerProcess) in the live-process list without launching a real child.
    // Production code never calls this overload, only the StdioProcess one above.
    internal static void Track(IProcessControl process) => liveProcesses.Add(process);

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

// :157-353
internal sealed class MCPClient : Actor
{
    // :158
    public string Id { get; }

    // :159
    private readonly MCPServerSpec spec;

    // :161. Replaces process/conversation/stdinHandle/stdoutPipe/stderrPipe/replies (B2): one MCPStdioConnection
    // holds the child, the message reader and the id-matched reply map (MCPWire.cs, N5).
    private MCPStdioConnection? stdio;

    // :162. PR-MCP-237: this counter numbers only the http path's requests (:315-316, :333-334); the stdio path
    // never reads or advances it, because each MCPStdioConnection carries its own counter that restarts at 1 on
    // every launch, including a relaunch (:164-168 in MCPWire.swift; MCPStdioConnection.Request above).
    private int nextID = 1;
    private bool initialized;
    private bool didRestart;
    private bool dead;
    private string? sessionID;

    // :168-171
    public MCPClient(string id, MCPServerSpec spec)
    {
        Id = id;
        this.spec = spec;
    }

    // :173-178. Nothing else is cleared: didRestart and dead stay set, so a client that exhausted its restart
    // stays dead until reload replaces it, and the http id counter keeps counting (PR-MCP-190).
    public Task Stop() => Isolated(() =>
    {
        stdio?.Stop();
        stdio = null;
        initialized = false;
        sessionID = null;
    });

    // :180-200
    public Task<IReadOnlyList<MCPHub.Tool>> ListTools() => Isolated(async () =>
    {
        var items = await MCPWire.ListTools(parameters => Rpc("tools/list", parameters, TimeSpan.FromSeconds(60)));
        var output = new List<MCPHub.Tool>();
        foreach (var item in items)
        {
            if (item["name"] is not JSONValue.String { Value: { } name }) continue;
            var rawDescription = item["description"] is JSONValue.String { Value: { } description } ? description : "";
            JSONValue schema = item["inputSchema"] is JSONValue.Object found
                ? found
                : new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["type"] = new JSONValue.String("object"),
                    ["properties"] = new JSONValue.Object(ValueMap<string, JSONValue>.Empty),
                }, SwiftStringComparer.Instance));
            output.Add(new MCPHub.Tool(Id, name, SwiftString.Prefix(rawDescription, 400), schema));
        }
        return (IReadOnlyList<MCPHub.Tool>)output;
    });

    // :202-224
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

    // :226-233
    private Task<JSONValue> Rpc(string method, JSONValue? parameters, TimeSpan timeout) => spec switch
    {
        MCPServerSpec.Stdio stdioSpec => StdioRpc(stdioSpec.Command, stdioSpec.Args, stdioSpec.Env, method, parameters, timeout),
        MCPServerSpec.Http http => HttpRpc(http.Url, http.Headers, method, parameters, timeout),
        _ => throw new UnreachableException(),
    };

    // :235-239
    private static void CheckReply(JSONValue value)
    {
        if (MCPWire.ErrorMessage(value) is { } message) throw MCPHubError.Server(message);
    }

    // MARK: - Local servers over stdio (:241-306)

    // :243-249
    private async Task<JSONValue> StdioRpc(string command, ValueList<string> args, ValueMap<string, string> env,
        string method, JSONValue? parameters, TimeSpan timeout)
    {
        await EnsureStdio(command, args, env);
        if (stdio is not { } connection) throw MCPHubError.Server("The server quit.");
        return await StdioReply(connection, method, parameters, timeout);
    }

    // :251-264
    private static async Task<JSONValue> StdioReply(MCPStdioConnection connection, string method, JSONValue? parameters, TimeSpan timeout)
    {
        try
        {
            var reply = await connection.Request(method, parameters, timeout.TotalSeconds);
            CheckReply(reply);
            return reply;
        }
        catch (MCPWireError error)
        {
            throw error.ErrorCase switch
            {
                MCPWireError.Case.Exited exited => MCPHubError.Server(
                    exited.Status is { } code ? $"The server quit (exit {code})." : "The server quit."),
                MCPWireError.Case.TimedOut => MCPHubError.Server("The server didn't answer in time."),
                _ => throw new UnreachableException(),
            };
        }
    }

    // :266-277
    private async Task EnsureStdio(string command, ValueList<string> args, ValueMap<string, string> env)
    {
        if (dead) throw MCPHubError.Server("The server quit.");
        if (stdio is { } connection && connection.Process.IsRunning && initialized) return;
        if (stdio is not null)
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

    // :279-306
    private async Task StartStdio(string command, ValueList<string> args, ValueMap<string, string> env)
    {
        var loginEnvironment = LoginEnvironment.Current;
        if (LoginEnvironment.Which(command, loginEnvironment) is not { } executable)
            throw MCPHubError.Server($"{command} isn't installed.");
        // ED-128: environment dictionaries are OrdinalIgnoreCase; a name set again in another case keeps the
        // first spelling and takes the last value.
        var environment = new Dictionary<string, string>(loginEnvironment, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in env) environment[key] = value;

        MCPStdioConnection connection;
        try
        {
            connection = MCPStdioConnection.Launch(executable, args, environment);
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            // ENGINEERING DECISION: the record (PR-MCP-185) asks for "the system error's description"; on
            // Windows that is the launch exception's own message, since there is no NSError text to reproduce.
            // :290: no terminate call on a launch failure (StdioProcess.Start already detached both handlers
            // and finished the message stream before rethrowing; there is nothing for the hub to clean up).
            throw MCPHubError.Server(error.Message);
        }
        stdio = connection;
        MCPHub.Track(connection.Process);
        try
        {
            var reply = await StdioReply(connection, "initialize", MCPWire.InitializeParams(), TimeSpan.FromSeconds(60));
            if (reply["result"] is not JSONValue.Object)
                throw MCPHubError.Server("The server sent something unexpected: missing result.");
            connection.Notify("notifications/initialized");
            initialized = true;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            await Stop();
            if (error is MCPHubError or CancellationError) throw;
            throw MCPHubError.Server(error.Message);
        }
    }

    // MARK: - Remote servers over streamable HTTP (:310-352)

    // :310-341
    private async Task<JSONValue> HttpRpc(string urlString, ValueMap<string, string> headers, string method,
        JSONValue? parameters, TimeSpan timeout)
    {
        if (SwiftURL.Create(urlString) is not { } parsed || !Uri.TryCreate(parsed.AbsoluteString, UriKind.Absolute, out var url))
            throw MCPHubError.Server("The server address is invalid.");
        if (!initialized)
        {
            var initID = nextID;
            nextID += 1;
            var posted = await Post(url, headers, MCPWire.Request(initID, "initialize", MCPWire.InitializeParams()),
                initID, TimeSpan.FromSeconds(60));
            if (posted.Body is not { } body)
                throw MCPHubError.Server("The server sent something unexpected: empty reply.");
            CheckReply(body);
            if (body["result"] is not JSONValue.Object)
                throw MCPHubError.Server("The server sent something unexpected: missing result.");
            sessionID = posted.SessionID ?? sessionID;
            initialized = true;
            try
            {
                _ = await Post(url, headers, MCPWire.Notification("notifications/initialized"), null, TimeSpan.FromSeconds(30));
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
        var posted2 = await Post(url, headers, MCPWire.Request(id, method, parameters), id, timeout);
        if (posted2.Body is not { } replyBody)
            throw MCPHubError.Server("The server sent something unexpected: empty reply.");
        CheckReply(replyBody);
        return replyBody;
    }

    // :343-352. DEVIATION OD-0022 (ED-152, ED-209): `headers` here is this client's spec.headers, the server's own
    // custom headers (e.g. X-Api-Key). MCPWire.Post marks each one origin-bound before sending, so F.HTTP's
    // Redirected drops it the moment a redirect changes scheme, host or port, and keeps it across a same-server
    // hop; this call site does nothing further, it inherits the marking from MCPWire.Post's own headers loop.
    private async Task<MCPHTTPReply> Post(Uri url, ValueMap<string, string> headers, JSONValue payload, int? id, TimeSpan timeout)
    {
        try
        {
            return await MCPWire.Post(url, headers, sessionID, payload, id, timeout.TotalSeconds);
        }
        catch (MCPHTTPError error)
        {
            if (error.Status is not { } status)
                throw MCPHubError.Server("The server sent something unexpected: invalid reply.");
            throw MCPHubError.Server($"The server answered {status}.");
        }
    }
}

// :355-376. The server processes alive this run, behind a lock so the quit path can reach them from any thread
// without an actor hop. Unchanged by this lane: it still holds IProcessControl handles (R-PROC-1..3); only the
// caller (MCPHub.Track, above) now hands it a StdioProcess's ProcessControl instead of a ProcessLauncher.
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
