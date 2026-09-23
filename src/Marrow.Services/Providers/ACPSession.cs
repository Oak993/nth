using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/ACPSession.swift (B2 1fbf81e), :1-921. Part 1 of 8: the launch gate,
// launchArguments, and start() through the initialize reply (PR-PROVACP-001..024). Members reached only past
// that point (openSession onward) depend on Services.MCP.MCPProviderConfig, not yet in the tree, and on later
// parts' own records; they are MARROW_STUB pending those parts (R-FILE-1: one file per Swift file).

namespace Marrow.Services.Providers;

// :7-21. Spaces out ACP agent process launches so two starting in the same instant do not trip over a store
// each keeps of its own (OpenCode's is SQLite): "database is locked". R-ACTOR-1: actor -> sealed class : Actor.
internal sealed class ACPLaunchGate : Actor
{
    private readonly TimeSpan spacing;
    private DateTimeOffset nextSlot = SwiftDate.DistantPast; // :9

    public ACPLaunchGate(TimeSpan spacing) => this.spacing = spacing; // :11-13

    public Task Wait() => Isolated(async () =>
    {
        var now = SwiftDate.Now; // :16
        var slot = now > nextSlot ? now : nextSlot; // :17 max(now, nextSlot)
        nextSlot = slot + spacing; // :18
        if (slot > now) await SwiftTask.TrySleep(slot - now); // :19
    });
}

// :23-896. Drives Agent Client Protocol agents: Cursor, OpenCode Zen, OpenCode Go, Grok and Devin.
// R-ISO-1: @MainActor -> plain class, no per-call MainActor check; every member is only ever called on main.
public sealed class ACPSession : ProviderSession
{
    private static readonly ACPLaunchGate LaunchGate = new(TimeSpan.FromSeconds(1.5)); // :26

    public Action<ProviderEvent>? OnEvent { get; set; } // :28

    private readonly SessionConfiguration configuration; // :30
    private JSONRPCConnection? connection; // :31
    private bool canLoadSessions; // :34
    private bool supportsHTTPMCP; // :35
    private ValueList<string> authMethods = ValueList<string>.Empty; // :33
#pragma warning disable CS0169, CS0414, CS0649 // set/read by OpenSession, Send, Interrupt, HandleClose: part 2+
    private string? sessionID; // :32
    private bool isReplaying; // :36
    private bool isStopping; // :37
    private bool promptActive; // :43
    private bool cancelRequested; // :44
#pragma warning restore CS0169, CS0414, CS0649
    private RuntimeMode runtimeMode; // :38

    public ACPSession(SessionConfiguration configuration) // :66-69
    {
        this.configuration = configuration;
        runtimeMode = configuration.RuntimeMode;
    }

    public bool IsRunning => connection is { } c && !c.IsClosed; // :71-74

    private string WorkingDirectory => configuration.WorkingDirectory.Path; // :76

    /// <summary>Cursor only exposes effort/fast selectors when the client opts into its parameterized picker.</summary>
    private bool UsesParameterizedModels => configuration.Provider == ProviderKind.Cursor; // :64

    // :78-106. Read once at launch time (configuration.runtimeMode), never the mutable runtimeMode property
    // (PR-PROVACP-017): a later mode change never relaunches the process or its argv.
    private ValueList<string> LaunchArguments => configuration.Provider switch
    {
        ProviderKind.Cursor => configuration.RuntimeMode switch
        {
            RuntimeMode.Auto => new ValueList<string>(["--auto-review", "acp"]), // :82
            RuntimeMode.FullAccess => new ValueList<string>(["--force", "acp"]), // :83
            _ => new ValueList<string>(["acp"]), // :84 .supervised, .autoAcceptEdits
        },
        ProviderKind.Grok => configuration.RuntimeMode switch
        {
            RuntimeMode.Supervised => new ValueList<string>(["--permission-mode", "default", "agent", "stdio"]), // :88
            RuntimeMode.AutoAcceptEdits => new ValueList<string>(["--permission-mode", "acceptEdits", "agent", "stdio"]), // :89
            RuntimeMode.Auto => new ValueList<string>(["--permission-mode", "auto", "agent", "stdio"]), // :90
            RuntimeMode.FullAccess => new ValueList<string>(["agent", "--always-approve", "stdio"]), // :91
            _ => throw new UnreachableException(),
        },
        ProviderKind.Devin => configuration.RuntimeMode switch
        {
            // Devin's modes at a glance: auto approves read-only tools, accept-edits adds workspace edits,
            // smart adds model-judged-safe actions, dangerous approves everything. Passed before the
            // subcommand, like its CLI expects. :94-96
            RuntimeMode.Supervised => new ValueList<string>(["--permission-mode", "auto", "acp"]), // :98
            RuntimeMode.AutoAcceptEdits => new ValueList<string>(["--permission-mode", "accept-edits", "acp"]), // :99
            RuntimeMode.Auto => new ValueList<string>(["--permission-mode", "smart", "acp"]), // :100
            RuntimeMode.FullAccess => new ValueList<string>(["--permission-mode", "dangerous", "acp"]), // :101
            _ => throw new UnreachableException(),
        },
        _ => new ValueList<string>(["acp"]), // :103-104 .opencode, .opencodeGo (and any other ACP kind)
    };

    public async Task<string> Start() // :108-152
    {
        if (configuration.Executable is not { } executable) throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider)); // :109
        await LaunchGate.Wait(); // :110
        var process = new StdioProcess(executable.Path, LaunchArguments, WorkingDirectory, configuration.Environment); // :111-116
        var connection = new JSONRPCConnection(process, sendsVersion: true); // :117
        connection.OnNotification = (method, parameters) => HandleNotification(method, parameters); // :118
        connection.OnRequest = (id, method, parameters) => HandleRequest(id, method, parameters); // :119
        connection.OnClose = tail => HandleClose(tail); // :120
        this.connection = connection; // :121
        connection.Start(); // :122

        var initialized = await connection.Request("initialize", InitializeParams); // :124-128
        ApplyInitializeReply(initialized); // :129-132

        JSONValue session; // :134
        try
        {
            session = await OpenSession(connection); // :136
        }
        catch (RPCError error) when (SwiftString.LocalizedCaseInsensitiveContains(error.Message, "auth") && authMethods.Count > 0) // :137
        {
            _ = await connection.Request("authenticate", JSONValue.FromDictionaryLiteral(("methodId", authMethods[0]))); // :138
            session = await OpenSession(connection); // :139
        }
        ApplySessionState(session); // :141
        await ApplySelection( // :142-147
            configuration.Model, configuration.Effort, configuration.FastMode, configuration.InteractionMode);
        if (sessionID is not { } id) throw new ProviderError(new ProviderError.Case.Failed($"{configuration.Provider.DisplayName} did not start a session.")); // :148-150
        return id; // :151
    }

    // :124-128. Factored out of Start() (behaviourally identical) so the exact initialize payload can be
    // proven without a live agent round trip. DEVIATION OD-0008 B11: upstream sends clientInfo name
    // "droppy-code", title "Droppy Code" (:127).
    private JSONValue InitializeParams => JSONValue.FromDictionaryLiteral(
        ("protocolVersion", 1L),
        ("clientCapabilities", ClientCapabilities),
        ("clientInfo", JSONValue.FromDictionaryLiteral(
            ("name", "marrow"),
            ("title", "Marrow"),
            ("version", AppInfo.Current.Version))));

    // :129-132. Factored out of Start() (behaviourally identical) so the reply parsing can be proven
    // without a live agent round trip.
    private void ApplyInitializeReply(JSONValue initialized)
    {
        canLoadSessions = initialized["agentCapabilities"]?["loadSession"]?.JSONValueBool ?? false; // :129
        supportsHTTPMCP = initialized["agentCapabilities"]?["mcpCapabilities"]?["http"]?.JSONValueBool ?? false; // :130
        authMethods = new ValueList<string>((initialized["authMethods"]?.JSONValueArray ?? [])
            .Select(m => m["id"]?.JSONValueString).OfType<string>()); // :131
        if (initialized["_meta"]?["modelState"] is { } state) ApplyModels(state); // :132
    }

    /// <summary>Cursor hides effort and Fast behind an undocumented `_meta` flag; without it the agent only
    /// returns one baked-in variant per model and rejects any other effort.</summary>
    private JSONValue ClientCapabilities // :156-165
    {
        get
        {
            var capabilities = new Dictionary<string, JSONValue> // :157-160
            {
                ["fs"] = JSONValue.FromDictionaryLiteral(("readTextFile", false), ("writeTextFile", false)),
                ["terminal"] = false,
            };
            if (UsesParameterizedModels) capabilities["_meta"] = JSONValue.FromDictionaryLiteral(("parameterizedModelPicker", true)); // :161-163
            return new JSONValue.Object(new ValueMap<string, JSONValue>(capabilities)); // :164
        }
    }

    public Task Send(TurnInput input) => throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:167-196, part 2+"); // :167

    public Task Interrupt() => throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:198-207, part 2+"); // :198

    public Task Compact() => throw new ProviderError(new ProviderError.Case.Failed($"{configuration.Provider.DisplayName} compacts its own context.")); // :209-211

    public void ResolveApproval(string requestID, string optionID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:213-217, part 2+"); // :213

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { } // :219

    public void Stop() // :221-224
    {
        isStopping = true;
        connection?.Close();
    }

    // MARK: - Catalog (part 2+: uses OpenSession/ApplySelection/ProbeModelEfforts)
    public static Task<ValueList<ModelOption>> ProbeModels(ProviderKind provider, SwiftURL executable, ValueMap<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:228-247, part 2+"); // :228

    // MARK: - Session state (part 2+: depends on Services.MCP.MCPProviderConfig, not yet in the tree)
    private Task<JSONValue> OpenSession(JSONRPCConnection connection) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:251-269, part 2+: depends on Services.MCP.MCPProviderConfig"); // :251

    private void ApplySessionState(JSONValue state) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:271-279, part 2+"); // :271

    private void ApplyModels(JSONValue state) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:306-325, part 2+"); // :306

    private Task ApplySelection(string? model, string? effort, bool fastMode, InteractionMode interaction) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:443-475, part 2+"); // :443

    // MARK: - Updates (part 2+)
    private void HandleNotification(string method, JSONValue parameters) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:489-553, part 2+"); // :489

    private void HandleRequest(RPCID id, string method, JSONValue parameters) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:555-590, part 2+"); // :555

    private void HandleClose(string tail) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:625-631, part 2+"); // :625
}
