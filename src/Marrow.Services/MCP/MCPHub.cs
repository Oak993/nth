using System.Diagnostics;
using System.Runtime.CompilerServices;
using Marrow.Core.Models;
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

    // :32-39. PORT-ONLY (W18): the members below the call name are optional and default to "a plain tool", so every
    // existing construction (name, description, schema) keeps working.
    public sealed record Tool(string Server, string Name, string Description, JSONValue InputSchema)
    {
        // :38
        public string CallName => "mcp__" + Server + "__" + Name;

        /// <summary>The tool's display title, when the server gives one.</summary>
        public string? Title { get; init; }

        /// <summary>The tool's whole <c>_meta</c> object, kept as the server sent it.</summary>
        public JSONValue? Meta { get; init; }

        /// <summary>The <c>ui://</c> resource that draws this tool's result (an MCP App), or null. See <see cref="MCPToolMeta"/>.</summary>
        public string? UiResourceUri { get; init; }

        /// <summary>Who may call the tool (<c>_meta.ui.visibility</c>); both by default.</summary>
        public MCPToolVisibility Visibility { get; init; } = MCPToolVisibility.Both;

        public bool IsVisibleToModel => (Visibility & MCPToolVisibility.Model) != 0;

        public bool IsVisibleToApp => (Visibility & MCPToolVisibility.App) != 0;
    }

    // :41-44. PORT-ONLY (W18, ED-NEW-web-w18-1): Text is still every text item joined (images as "[image]", a
    // resource as "[resource: uri]"), for a caller that can only read text. The rest of the answer is kept next to it.
    public sealed record Result(string Text, bool IsError)
    {
        /// <summary>The images of the result, base64 as sent.</summary>
        public ValueList<MCPResultImage> Images { get; init; } = ValueList<MCPResultImage>.Empty;

        /// <summary>Resource links and embedded resources of the result.</summary>
        public ValueList<MCPResultResource> Resources { get; init; } = ValueList<MCPResultResource>.Empty;

        /// <summary>The result's <c>structuredContent</c> object, or null.</summary>
        public JSONValue? StructuredContent { get; init; }

        /// <summary>The result's <c>_meta</c> object, or null.</summary>
        public JSONValue? Meta { get; init; }

        /// <summary>The server and tool the hub called (set by <see cref="MCPHub.Call"/>).</summary>
        public string? Server { get; init; }
        public string? ToolName { get; init; }

        /// <summary>The <c>ui://</c> resource the tool says draws this result, or null.</summary>
        public string? UiResourceUri { get; init; }

        /// <summary>
        /// The part of the result the text cannot carry, under the persistence caps of <see cref="ToolPayload"/>, or
        /// null when there is none (a plain text result). Images and the addresses of links are always kept. The
        /// structuredContent, the _meta and the text of embedded resources are kept only for a tool that names a
        /// view: the view needs them to be drawn again after a restart, while for any other tool the text already
        /// carries the data and the thread file must not store it twice.
        /// </summary>
        public ToolPayload? ToPayload()
        {
            var forView = UiResourceUri is not null;
            var payload = new ToolPayload { Server = Server, Tool = ToolName, UiResourceUri = UiResourceUri };
            foreach (var image in Images) payload = payload.WithImage(image.MimeType, image.Data, MCPResultReader.Base64Bytes(image.Data));
            foreach (var resource in Resources)
            {
                payload = payload.WithResourceLink(new ToolResourceLink
                {
                    Uri = resource.Uri,
                    Name = resource.Name,
                    MimeType = resource.MimeType,
                    Description = resource.Description,
                    Embedded = resource.Embedded,
                    Text = forView ? resource.Text : null,
                    ByteCount = resource.ByteCount,
                });
            }
            if (forView && StructuredContent is { } structured) payload = payload.WithStructuredContent(structured.CompactString);
            if (forView && Meta is { } meta) payload = payload.WithMeta(meta.CompactString);
            return payload.IsEmpty ? null : payload;
        }
    }

    private Dictionary<string, MCPServerSpec>? specs;
    private readonly Dictionary<string, MCPClient> clients = new(SwiftStringComparer.Instance);
    private readonly Dictionary<string, List<Tool>> toolCache = new(SwiftStringComparer.Instance);
    private readonly Dictionary<string, List<MCPResource>> resourceCache = new(SwiftStringComparer.Instance);
    private readonly Dictionary<string, List<MCPResourceTemplate>> templateCache = new(SwiftStringComparer.Instance);
    private readonly HashSet<string> failed = new(SwiftStringComparer.Instance);

    // PORT-ONLY (ED-NEW-web-sec-rev-2): per server, the tools its newest listing said the model may not call (app-only). Kept when a
    // list change drops the cache, and filled by a listing a change overtook too, so a model call made while the list is not cached is
    // still refused a tool the server keeps for its app view. Replaced by each newer listing; Reload forgets it with the rest.
    private readonly Dictionary<string, HashSet<string>> hiddenFromModel = new(SwiftStringComparer.Instance);

    // A server's lists changed this many times. A listing that began before a change is not cached: it may be stale.
    private readonly Dictionary<string, int> changeEpoch = new(SwiftStringComparer.Instance);

    private int Epoch(string server) => changeEpoch.GetValueOrDefault(server);

    private int toolsRevision;

    /// <summary>
    /// PORT-ONLY (W18): counts the times a server's tool list changed on this hub (a list-changed notification). A
    /// session that loaded the tools earlier reloads them when this moved. ED-NEW-web-sec-rev-5: a Reload moves it too (the servers,
    /// and so every list, may have changed), so a session never checks a model's call against a list read before the reload.
    /// </summary>
    public int ToolsRevision => Volatile.Read(ref toolsRevision);

    /// <summary>
    /// PORT-ONLY (W18): raised when a server says its tools, resources or prompts changed, after the hub dropped
    /// what it had cached for that server. It runs on the hub's executor: a handler must be quick and hop for any
    /// real work.
    /// </summary>
    public event Action<MCPListChange>? ListChanged;

    // :51-77. PORT-ONLY (W18): the model's list. A tool whose _meta.ui.visibility leaves out "model" (an app-only tool)
    // is not in it; AllTools() has every tool, for the MCP Apps host.
    public Task<IReadOnlyList<Tool>> Tools() => Isolated(async () =>
        (IReadOnlyList<Tool>)(await ListAll()).Where(tool => tool.IsVisibleToModel).ToList());

    /// <summary>PORT-ONLY (W18): every tool of every server, including the ones only an app view may call.</summary>
    public Task<IReadOnlyList<Tool>> AllTools() => Isolated(ListAll);

    // :51-77 (the body of tools()).
    private async Task<IReadOnlyList<Tool>> ListAll()
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
                var epoch = Epoch(id);
                var listed = await client.ListTools();
                var listedList = listed.ToList();
                if (Epoch(id) == epoch) toolCache[id] = listedList;
                hiddenFromModel[id] = new HashSet<string>(listedList.Where(tool => !tool.IsVisibleToModel).Select(tool => tool.Name), SwiftStringComparer.Instance);
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
        return all
            .OrderBy(tool => tool.Server, SwiftStringComparer.Instance)
            .ThenBy(tool => tool.Name, SwiftStringComparer.Instance)
            .ToList();
    }

    // :79-98. PORT-ONLY (W18): caller says who is calling. A model caller is refused an app-only tool, and an app
    // caller a model-only one, when the tool list is known; an app caller makes it known first. A model caller is also refused a
    // tool an earlier listing said is app-only (ED-NEW-web-sec-rev-2); the API session, the model's way here, lists first itself.
    public Task<Result> Call(string callName, JSONValue arguments, MCPCaller caller = MCPCaller.Model) => Isolated(async () =>
    {
        specs ??= ReadSpecs();
        if (Parse(callName) is not { } parsed || !specs.TryGetValue(parsed.Server, out var spec))
            throw MCPHubError.UnknownTool(callName);
        if (caller == MCPCaller.App && !toolCache.ContainsKey(parsed.Server))
            await ListAll();
        Tool? known = null;
        if (toolCache.TryGetValue(parsed.Server, out var cached))
        {
            known = cached.FirstOrDefault(tool => SwiftString.Equal(tool.Name, parsed.Tool));
            if (known is null) throw MCPHubError.UnknownTool(callName);
            if (caller == MCPCaller.Model ? !known.IsVisibleToModel : !known.IsVisibleToApp)
                throw MCPHubError.UnknownTool(callName);
        }
        else if (caller == MCPCaller.App)
        {
            throw MCPHubError.UnknownTool(callName);
        }
        else if (hiddenFromModel.TryGetValue(parsed.Server, out var hidden) && hidden.Contains(parsed.Tool))
        {
            throw MCPHubError.UnknownTool(callName); // PORT-ONLY (ED-NEW-web-sec-rev-2)
        }
        var client = Client(parsed.Server, spec);
        try
        {
            var result = await client.CallTool(parsed.Tool, arguments);
            return result with { Server = parsed.Server, ToolName = parsed.Tool, UiResourceUri = known?.UiResourceUri };
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

    // MARK: - Resources (PORT-ONLY, W18)

    /// <summary>The resources a server lists, cached until it says they changed. Empty when it has none.</summary>
    public Task<IReadOnlyList<MCPResource>> Resources(string server) => Isolated(async () =>
    {
        var spec = SpecFor(server);
        if (resourceCache.TryGetValue(server, out var cached)) return (IReadOnlyList<MCPResource>)cached;
        var epoch = Epoch(server);
        var listed = (await Guarded(() => Client(server, spec).ListResources())).ToList();
        if (Epoch(server) == epoch) resourceCache[server] = listed;
        return listed;
    });

    /// <summary>The resource templates a server lists, cached until it says its resources changed.</summary>
    public Task<IReadOnlyList<MCPResourceTemplate>> ResourceTemplates(string server) => Isolated(async () =>
    {
        var spec = SpecFor(server);
        if (templateCache.TryGetValue(server, out var cached)) return (IReadOnlyList<MCPResourceTemplate>)cached;
        var epoch = Epoch(server);
        var listed = (await Guarded(() => Client(server, spec).ListResourceTemplates())).ToList();
        if (Epoch(server) == epoch) templateCache[server] = listed;
        return listed;
    });

    /// <summary>resources/read: the items of one resource.</summary>
    public Task<IReadOnlyList<MCPResourceContent>> ReadResource(string server, string uri) => Isolated(async () =>
    {
        var spec = SpecFor(server);
        return await Guarded(() => Client(server, spec).ReadResource(uri));
    });

    /// <summary>
    /// Reads a <c>ui://</c> resource for the MCP Apps host: the page and the <c>_meta.ui</c> it carries (the domains
    /// it asks for, its permissions). Refuses an address that is not <c>ui://</c> and a page that is not the MCP App
    /// MIME type (<c>text/html;profile=mcp-app</c>).
    /// </summary>
    public Task<MCPUiResource> ReadUiResource(string server, string uri) => Isolated(async () =>
    {
        if (!MCPToolMeta.IsUiUri(uri)) throw MCPHubError.Server("That isn't an app page address.");
        var contents = await ReadResource(server, uri);
        foreach (var item in contents)
        {
            if (!MCPToolMeta.IsAppPageMime(item.MimeType)) continue;
            if (item.TextOrDecodedBlob() is not { } html) continue;
            return new MCPUiResource(server, item.Uri.Length > 0 ? item.Uri : uri, html, MCPUiMeta.Read(item.Meta), item.Meta);
        }
        throw MCPHubError.Server("The server's resource isn't an MCP App page.");
    });

    // The spec of a configured server; an unknown id is an error the caller can show.
    private MCPServerSpec SpecFor(string server)
    {
        specs ??= ReadSpecs();
        if (!specs.TryGetValue(server, out var spec)) throw MCPHubError.Server($"No MCP server named {server}.");
        return spec;
    }

    // The same error mapping Call() has: a hub error passes, cancellation stays cancellation, anything else is text.
    private static async Task<T> Guarded<T>(Func<Task<T>> body)
    {
        try
        {
            return await body();
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
    }

    // A server said (on its own) that a list changed. The change counts only if it comes from the client the hub
    // holds now: Reload() replaces every client, and a stopped one may still be saying goodbye.
    private void NoteListChanged(MCPClient client, MCPWire.ListKind kind) => _ = Isolated(() =>
    {
        if (!clients.TryGetValue(client.Id, out var current) || !ReferenceEquals(current, client)) return;
        changeEpoch[client.Id] = Epoch(client.Id) + 1;
        switch (kind)
        {
            case MCPWire.ListKind.Tools:
                toolCache.Remove(client.Id);
                Interlocked.Increment(ref toolsRevision);
                break;
            case MCPWire.ListKind.Resources:
                resourceCache.Remove(client.Id);
                templateCache.Remove(client.Id);
                break;
        }
        try { ListChanged?.Invoke(new MCPListChange(client.Id, kind)); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { /* a handler's failure never reaches the server loop */ }
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
        resourceCache.Clear();
        templateCache.Clear();
        changeEpoch.Clear();
        failed.Clear();
        hiddenFromModel.Clear();
        specs = null;
        Interlocked.Increment(ref toolsRevision); // PORT-ONLY (ED-NEW-web-sec-rev-5)
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
        MCPClient? created = null;
        created = new MCPClient(id, spec, kind => NoteListChanged(created!, kind));
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

    // PORT-ONLY (W18): the server's capabilities object from its initialize result (null before the handshake or when
    // it sent none), where list-changed is announced and what the server offers is named.
    private JSONValue? serverCapabilities;

    // PORT-ONLY (W18): told, on any thread, when the server says one of its lists changed.
    private readonly Action<MCPWire.ListKind>? onListChanged;

    // PORT-ONLY (W18): the http "listen" stream (a GET the server answers with events), when the server announced
    // list changes. Ended by Stop().
    private TaskHandle? listener;

    // :168-171
    public MCPClient(string id, MCPServerSpec spec, Action<MCPWire.ListKind>? onListChanged = null)
    {
        Id = id;
        this.spec = spec;
        this.onListChanged = onListChanged;
    }

    // :173-178. Nothing else is cleared: didRestart and dead stay set, so a client that exhausted its restart
    // stays dead until reload replaces it, and the http id counter keeps counting (PR-MCP-190).
    public Task Stop() => Isolated(() =>
    {
        listener?.Cancel();
        listener = null;
        stdio?.Stop();
        stdio = null;
        initialized = false;
        sessionID = null;
        serverCapabilities = null;
    });

    // PORT-ONLY (W18): what the server sends on its own. A list-changed notification reaches onListChanged; other
    // notifications (progress, logging, resource updates) are not used yet and are dropped.
    private void HandleNotification(JSONValue message)
    {
        if (MCPWire.ListChangedKind(MCPWire.MethodOf(message)) is { } kind) onListChanged?.Invoke(kind);
    }

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
            // PORT-ONLY (W18): _meta is kept whole; the UI resource and the visibility are read from it.
            var meta = item["_meta"] is JSONValue.Object foundMeta ? foundMeta : null;
            output.Add(new MCPHub.Tool(Id, name, SwiftString.Prefix(rawDescription, 400), schema)
            {
                Title = item["title"]?.JSONValueString,
                Meta = meta,
                UiResourceUri = MCPToolMeta.UiResourceUri(meta),
                Visibility = MCPToolMeta.Visibility(meta),
            });
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
        // PORT-ONLY (W18): the text is the same flattened text as before; images, resources, structuredContent and
        // _meta are kept next to it (MCPResultReader).
        return MCPResultReader.Read(reply["result"]);
    });

    // MARK: - Resources (PORT-ONLY, W18)

    // resources/list, paged. A server that named its capabilities and left "resources" out is not asked.
    public Task<IReadOnlyList<MCPResource>> ListResources() => Isolated(async () =>
    {
        await EnsureReady();
        if (!MCPWire.MaySupport(serverCapabilities, "resources")) return (IReadOnlyList<MCPResource>)[];
        var items = await MCPWire.List("resources", parameters => Rpc("resources/list", parameters, TimeSpan.FromSeconds(60)));
        var output = new List<MCPResource>();
        foreach (var item in items)
        {
            if (item["uri"]?.JSONValueString is not { Length: > 0 } uri) continue;
            output.Add(new MCPResource(Id, uri, item["name"]?.JSONValueString ?? uri)
            {
                Title = item["title"]?.JSONValueString,
                Description = item["description"]?.JSONValueString,
                MimeType = item["mimeType"]?.JSONValueString,
                Size = item["size"]?.JSONValueInt,
                Meta = item["_meta"] is JSONValue.Object meta ? meta : null,
            });
        }
        return (IReadOnlyList<MCPResource>)output;
    });

    // resources/templates/list, paged.
    public Task<IReadOnlyList<MCPResourceTemplate>> ListResourceTemplates() => Isolated(async () =>
    {
        await EnsureReady();
        if (!MCPWire.MaySupport(serverCapabilities, "resources")) return (IReadOnlyList<MCPResourceTemplate>)[];
        var items = await MCPWire.List("resourceTemplates", parameters => Rpc("resources/templates/list", parameters, TimeSpan.FromSeconds(60)));
        var output = new List<MCPResourceTemplate>();
        foreach (var item in items)
        {
            if (item["uriTemplate"]?.JSONValueString is not { Length: > 0 } template) continue;
            output.Add(new MCPResourceTemplate(Id, template, item["name"]?.JSONValueString ?? template)
            {
                Title = item["title"]?.JSONValueString,
                Description = item["description"]?.JSONValueString,
                MimeType = item["mimeType"]?.JSONValueString,
                Meta = item["_meta"] is JSONValue.Object meta ? meta : null,
            });
        }
        return (IReadOnlyList<MCPResourceTemplate>)output;
    });

    // resources/read.
    public Task<IReadOnlyList<MCPResourceContent>> ReadResource(string uri) => Isolated(async () =>
    {
        var reply = await Rpc("resources/read",
            JSONValue.FromDictionaryLiteral(("uri", new JSONValue.String(uri))), TimeSpan.FromSeconds(60));
        var output = new List<MCPResourceContent>();
        if (reply["result"]?["contents"]?.JSONValueArray is { } contents)
        {
            foreach (var item in contents)
            {
                output.Add(new MCPResourceContent(item["uri"]?.JSONValueString ?? uri)
                {
                    MimeType = item["mimeType"]?.JSONValueString,
                    Text = item["text"]?.JSONValueString,
                    Blob = item["blob"]?.JSONValueString,
                    Meta = item["_meta"] is JSONValue.Object meta ? meta : null,
                });
            }
        }
        return (IReadOnlyList<MCPResourceContent>)output;
    });

    // The handshake alone, so the capabilities are known before a list is asked for (the same step every Rpc takes
    // first; running it twice does nothing).
    private Task EnsureReady() => spec switch
    {
        MCPServerSpec.Stdio stdioSpec => EnsureStdio(stdioSpec.Command, stdioSpec.Args, stdioSpec.Env),
        MCPServerSpec.Http http => EnsureHttp(http.Url, http.Headers),
        _ => throw new UnreachableException(),
    };

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
            connection = MCPStdioConnection.Launch(executable, args, environment, HandleNotification);
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
            serverCapabilities = reply["result"]?["capabilities"] is JSONValue.Object named ? named : null;
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
        var url = await EnsureHttp(urlString, headers);
        var id = nextID;
        nextID += 1;
        var posted2 = await Post(url, headers, MCPWire.Request(id, method, parameters), id, timeout);
        if (posted2.Body is not { } replyBody)
            throw MCPHubError.Server("The server sent something unexpected: empty reply.");
        CheckReply(replyBody);
        return replyBody;
    }

    // :310-341 (the first half of httpRpc). PORT-ONLY (W18): split out so a list can wait for the handshake and read
    // the server's capabilities first; the requests and their order are exactly as before.
    private async Task<Uri> EnsureHttp(string urlString, ValueMap<string, string> headers)
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
            serverCapabilities = body["result"]?["capabilities"] is JSONValue.Object named ? named : null;
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
            if (MCPWire.AnnouncesListChanges(serverCapabilities)) StartListener(url, headers);
        }
        return url;
    }

    // PORT-ONLY (W18): a server that announced list changes may send them on a GET event stream. It is opened once
    // per handshake and read until the server closes it; a refusal (405 and the like) ends the attempt for good, a
    // dropped stream is retried a few times, slower each time, and then given up.
    private void StartListener(Uri url, ValueMap<string, string> headers)
    {
        listener?.Cancel();
        var session = sessionID;
        listener = TaskHandle.Detached(async () =>
        {
            var failures = 0;
            while (!SwiftTask.IsCancelled)
            {
                var delivered = 0;
                var outcome = await MCPWire.Listen(url, headers, session, message =>
                {
                    delivered += 1;
                    HandleInbound(url, headers, session, message);
                });
                if (SwiftTask.IsCancelled || outcome == MCPWire.ListenOutcome.Unsupported) return;
                failures = delivered > 0 ? 0 : failures + 1;
                if (failures >= 5) return;
                await SwiftTask.Sleep(SwiftTask.Seconds(Math.Min(30, 1 << failures)));
            }
        });
    }

    // PORT-ONLY (W18): a message the server sent over http. A notification is handled; a request is answered with a
    // POST of the answer (ping gets an empty result, everything else "method not found"), so the server is not left
    // waiting. A failed answer is not an error here.
    private void HandleInbound(Uri url, ValueMap<string, string> headers, string? session, JSONValue message)
    {
        switch (MCPWire.Classify(message))
        {
            case MCPWire.MessageKind.Notification:
                HandleNotification(message);
                break;
            case MCPWire.MessageKind.Request:
                _ = TaskHandle.Detached(async () =>
                {
                    try { _ = await MCPWire.Post(url, headers, session, MCPWire.Reply(message), null, 30); }
                    catch (Exception error) when (SwiftTry.IsThrownError(error)) { /* the server asked; nothing to tell the user */ }
                });
                break;
        }
    }

    // :343-352. DEVIATION OD-0022 (ED-152, ED-209): `headers` here is this client's spec.headers, the server's own
    // custom headers (e.g. X-Api-Key). MCPWire.Post marks each one origin-bound before sending, so F.HTTP's
    // Redirected drops it the moment a redirect changes scheme, host or port, and keeps it across a same-server
    // hop; this call site does nothing further, it inherits the marking from MCPWire.Post's own headers loop.
    private async Task<MCPHTTPReply> Post(Uri url, ValueMap<string, string> headers, JSONValue payload, int? id, TimeSpan timeout)
    {
        try
        {
            var session = sessionID;
            return await MCPWire.Post(url, headers, session, payload, id, timeout.TotalSeconds,
                message => HandleInbound(url, headers, session, message));
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
