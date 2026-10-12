using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Store;

// Source: DroppyCode/Services/Providers/ACPSession.swift (B3 52bc792), :1-921. Part 1 of 8: the launch gate,
// launchArguments, and start() through the initialize reply (PR-PROVACP-001..024). Part 2 of 8: openSession,
// applySessionState, resolveModes, applyModels, applyConfigOptions and its static helpers (flatten,
// isEffortScale, orderedEfforts, baseModelID, sameModel) (PR-PROVACP-025..047, 049). Part 3 of 8: applySelection,
// setConfigOption, send, interrupt, resolveApproval (PR-PROVACP-053..073; compact and answerQuestion were
// already implemented in part 1). Part 4 of 8: finishPrompt, handleNotification, handleRequest, handleClose and
// their static mapping helpers (PR-PROVACP-074..097). Part 5 of 8: makeToolCall/makeToolUpdate's title
// synthesis machinery up through the command, read and edit branches (PR-PROVACP-098..121). Part 6 of 8 (this
// change): the rest of makeToolCall/makeToolUpdate's title and detail synthesis (search, web, mcp/agent/other,
// the fallback and isFallbackTitle tables, diffs()) and the catalog probe, probeModels and probeModelEfforts
// (PR-PROVACP-122..138).

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
public sealed class ACPSession : ProviderSession, Port.Skills.Providers.ISkillSessionLifetime
{
    private static readonly ACPLaunchGate LaunchGate = new(TimeSpan.FromSeconds(1.5)); // :26

    public Action<ProviderEvent>? OnEvent { get; set; } // :28
    // DEVIATION ED-363: DL-12 observations; no new ACP protocol fields.
    public Action<ToolEvent>? OnSkillsEvent { get; set; }
    public Exception? LastSkillEventFailure { get; private set; }
    private void EmitSkillEvent(ToolEvent value)
    {
        try { OnSkillsEvent?.Invoke(value); }
        catch (Exception error) { LastSkillEventFailure = error; } // DL-12: an observer cannot interrupt the existing timeline or permission protocol.
    }
    private readonly ACPSkillEvents skillEvents = new();

    private readonly SessionConfiguration configuration; // :30
    private JSONRPCConnection? connection; // :31
    private StdioProcess? readersProcess; // SK-17: hold the runtime generation until the actual ACP child exits.
    private Port.Skills.Providers.SkillsMcpBridge? skillsMcp;
    // PORT-ONLY (Ignis Advisor, ED-NEW-ignisadvisor-2): the session's ask_advisor server, registered once; null for a chat with no advisor.
    private MCPResolvedServer? advisorServer;
    private MCPResolvedServer? dextraServer; // PORT-ONLY (Dextra, ED-NEW-dextra-as4-7): the chat's nth-dextra server; null for a chat that is not Dextra's.
    private bool canLoadSessions; // :34
    private bool supportsHTTPMCP; // :35
    private ValueList<string> authMethods = ValueList<string>.Empty; // :33
    private string? sessionID; // :32
    private bool isReplaying; // :36
    private bool isStopping; // :37
    private bool promptActive; // :43
    private bool cancelRequested; // :44
    private string? messageID; // :39
    private string? thoughtID; // :40
    private int counter; // :41
    private readonly Dictionary<string, RPCID> pendingPermissions = new(); // :42; R-COLL-2: iteration order unspecified
    private readonly HashSet<string> todoToolCallIDs = new(SwiftStringComparer.Instance); // :45-48; R-COLL-2: iteration order unspecified; R-STR-2: Set<String> is canonical
    private RuntimeMode runtimeMode; // :38

    // :50-62. Model/mode/effort state discovered from the session reply and config options.
    private ValueList<ModelOption> models = ValueList<ModelOption>.Empty; // :50
    private string? modelConfigID; // :51
    private string? modeConfigID; // :52
    private string? effortConfigID; // :53
    private string? fastConfigID; // :55, Cursor's Fast toggle (model_config / fast)
#pragma warning disable CS0414 // read by ApplySelection: part 3+
    private bool hasModeAPI; // :56
#pragma warning restore CS0414
    private string? planMode; // :57
    private string? buildMode; // :58
    private string? currentMode; // :59
    private string? currentModel; // :60
    private string? currentEffort; // :61
    private string? currentFast; // :62

    // PORT-ONLY: SK-11e / WU-10 and DL-12 (Grok). With a null launch the argv is exactly LaunchArguments and the environment exactly today's.
    public Marrow.Services.Port.Skills.Providers.ProviderSkillLaunch? SkillLaunch { get; init; }
    public Action<Marrow.Core.Port.Skills.Detect.ToolEvent>? ToolEvents { get; init; }
    private Marrow.Services.Port.Skills.Providers.AcpToolEventTap? toolTap;
    private Marrow.Services.Port.Skills.Providers.AcpToolEventTap Tap => toolTap ??= new(ToolEvents);

    /// <summary>LaunchArguments, plus --plugin-dir before `agent` for Grok when a launch is set. Any other ACP provider is untouched.</summary>
    internal ValueList<string> EffectiveLaunchArguments =>
        SkillLaunch is { } launch && configuration.Provider == ProviderKind.Grok
            ? new ValueList<string>(Marrow.Services.Port.Skills.Providers.ProviderSkillArguments.Grok(LaunchArguments, launch))
            : LaunchArguments;

    internal ValueMap<string, string> EffectiveEnvironment =>
        configuration.Provider == ProviderKind.Grok
            ? Marrow.Services.Port.Skills.Providers.ProviderSkillEnvironment.Apply(configuration.Environment, SkillLaunch)
            : configuration.Environment;

    public ACPSession(SessionConfiguration configuration) // :66-69
    {
        this.configuration = configuration;
        runtimeMode = configuration.RuntimeMode;
    }

    public bool IsRunning => connection is { } c && !c.IsClosed; // :71-74
    public Task SkillReadersExited => readersProcess is { IsRunning: true } child ? child.WaitForExit() : Task.CompletedTask;

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

    // PORT-ONLY: DL-6 applies to every ACP child, including the catalog probe's session.
    private Dictionary<string, string> ChildEnvironment => ProviderCustomCommands.ForChild(configuration.Environment, SkillsProviderDataRoot.Resolve(Storage.Locations.Current, Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")));

    // PORT-ONLY: SK-11c's DL-6 rule (addonsin on PATH of every ACP child) and SK-11e's Grok launch (the same rule from the generation's data root) both
    // insert the folder, so a Grok session with a launch takes SK-11e's environment and every other child takes SK-11c's: the folder goes on PATH exactly once.
    private IReadOnlyDictionary<string, string> LaunchEnvironment =>
        SkillLaunch is not null && configuration.Provider == ProviderKind.Grok ? EffectiveEnvironment : ChildEnvironment;

    public async Task<string> Start() // :108-152
    {
        if (configuration.Executable is not { } executable) throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider)); // :109
        if (configuration.Provider == ProviderKind.Cursor && configuration.SkillGeneration is { } skills)
        {
            skillsMcp = new(skills, configuration.SkillMcpExecutable ?? Path.Combine(AppContext.BaseDirectory, "Marrow.App.exe"));
        }
        try
        {
            await LaunchGate.Wait(); // :110
            // SK-17: the gate suspends. Stop or damage during that wait must not launch a late reader.
            if (configuration.Provider == ProviderKind.Cursor && configuration.SkillGeneration is { } current)
            {
                if (isStopping || SwiftTask.IsCancelled) throw new CancellationError();
                Port.Skills.Providers.SessionSkillOverlay.RequireFolder(current, "cursor");
            }
            var process = new StdioProcess(executable.Path, EffectiveLaunchArguments, WorkingDirectory, LaunchEnvironment); // :111-116
            readersProcess = process;
            var connection = new JSONRPCConnection(process, sendsVersion: true); // :117
            connection.OnNotification = (method, parameters) => HandleNotification(method, parameters); // :118
            connection.OnRequest = (id, method, parameters) => HandleRequest(id, method, parameters); // :119
            connection.OnClose = tail => HandleClose(tail); // :120
            this.connection = connection; // :121
            connection.Start(); // :122

            var initialized = await connection.Request("initialize", InitializeParams); // :124-128
            ApplyInitializeReply(initialized); // :129-132

            var session = await OpenSessionWithAuthRetry(connection); // :134-140
            return await SelectOpenedSession(session); // :141-151
        }
        catch { skillsMcp?.Dispose(); throw; }
    }

    // :134-140 factored verbatim so a scripted transport can verify the single auth retry.
    private async Task<JSONValue> OpenSessionWithAuthRetry(JSONRPCConnection connection)
    {
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
        return session;
    }

    // :141-151 discovery must precede selection: discovered config IDs choose the request API.
    private async Task<string> SelectOpenedSession(JSONValue session)
    {
        ApplySessionState(session); // :141
        await ApplySelection( // :142-147
            configuration.Model, configuration.Effort, configuration.FastMode, configuration.InteractionMode);
        return RequireSessionID(); // :148-151
    }

    // :148-150. Factored out of Start() (behaviourally identical) so the exact per-provider guard and message
    // can be proven (UI-PROVACP-030) without a live agent round trip, the same technique InitializeParams and
    // ApplyInitializeReply already use (part 1's header note): a real provider's launchArguments cannot drive
    // a scripted stand-in agent through Start() itself (part 1 header, part 2's PR-PROVACP-033 note).
    private string RequireSessionID()
    {
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
            ("name", "nth"),
            ("title", "nth"),
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

    // :167-196. Part 3 of 8 (PR-PROVACP-063..067).
    public async Task Send(TurnInput input)
    {
        if (connection is not { } conn || conn.IsClosed || sessionID is not { } sid) throw new ProviderError(new ProviderError.Case.NotRunning()); // :168
        runtimeMode = input.RuntimeMode; // :169
        await ApplySelection(input.Model, input.Effort, input.FastMode, input.InteractionMode); // :170-175
        var prompt = new List<JSONValue> { JSONValue.FromDictionaryLiteral(("type", "text"), ("text", input.Text)) }; // :176
        foreach (var (image, base64) in await Attachment.Base64Images(input.Images)) // :177
            prompt.Add(JSONValue.FromDictionaryLiteral(("type", "image"), ("mimeType", image.MimeType), ("data", base64))); // :178
        messageID = null; // :180
        thoughtID = null; // :181
        promptActive = true; // :182
        cancelRequested = false; // :183
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null)); // :184
        var promptArray = new ValueList<JSONValue>(prompt);
        MainActor.Spawn(async () => // :185-195. A floating task: send() does not await it.
        {
            try
            {
                var result = await conn.Request("session/prompt", JSONValue.FromDictionaryLiteral( // :187-190
                    ("sessionId", sid), ("prompt", new JSONValue.Array(promptArray))));
                FinishPrompt(result["stopReason"]?.JSONValueString, null); // :191
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) // :192
            {
                FinishPrompt(null, error); // :193
            }
        });
    }

    // :198-207. Part 3 of 8 (PR-PROVACP-068..070). No await in the Swift body: nothing here interleaves.
    public Task Interrupt()
    {
        if (!promptActive || connection is not { } conn || sessionID is not { } sid) return Task.CompletedTask; // :199
        cancelRequested = true; // :200
        foreach (var (key, id) in pendingPermissions) // :201
        {
            conn.Respond(id, JSONValue.FromDictionaryLiteral(("outcome", JSONValue.FromDictionaryLiteral(("outcome", "cancelled"))))); // :202
            OnEvent?.Invoke(new ProviderEvent.RequestResolved(key)); // :203
        }
        pendingPermissions.Clear(); // :205
        conn.Notify("session/cancel", JSONValue.FromDictionaryLiteral(("sessionId", sid))); // :206
        return Task.CompletedTask;
    }

    public Task Compact() => throw new ProviderError(new ProviderError.Case.Failed($"{configuration.Provider.DisplayName} compacts its own context.")); // :209-211

    // :213-217. Part 3 of 8 (PR-PROVACP-072).
    public void ResolveApproval(string requestID, string optionID)
    {
        if (!pendingPermissions.Remove(requestID, out var id)) return; // :214
        connection?.Respond(id, JSONValue.FromDictionaryLiteral( // :215
            ("outcome", JSONValue.FromDictionaryLiteral(("outcome", "selected"), ("optionId", optionID)))));
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :216
    }

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { } // :219

    public void Stop() // :221-224
    {
        skillEvents.Clear();
        isStopping = true;
        skillsMcp?.Dispose();
        connection?.Close();
    }

    // MARK: - Catalog

    /// <summary>:229-230. Creates the probe's throwaway working directory under the system temp folder,
    /// swallowing any failure (try?), and returns its path. Factored out (behaviourally identical to inline
    /// code) so it can be proven without a live agent round trip, the same technique InitializeParams and
    /// ApplyInitializeReply already use (part 1's header note). DEVIATION OD-0008 (OD-PENDING-17): named
    /// "marrow-catalog", not upstream's "droppy-code-catalog".</summary>
    private static string PrepareProbeCatalogDirectory()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marrow-catalog"); // :229
        try { System.IO.Directory.CreateDirectory(path); } catch { } // :230
        return path;
    }

    /// <summary>ENGINEERING DECISION mirroring Core/Models/Library.cs Url and Core/Models/Timeline.cs
    /// Attachment.Url: F.URL (SwiftURL) only builds URL(string:); there is no ported fileURLWithPath
    /// initializer, so a plain directory path is percent-encoded by hand. DEFECT FIX (PR-PROVREG-246, 248): this built
    /// a "file://" URL, whose .Path reproduces macOS's POSIX url.path and turns "C:/Users/x" into "/C:/Users/x"; that
    /// working directory is rejected by CreateProcessW ("The directory name is invalid"), so every catalog probe
    /// (Cursor, Grok, Devin, OpenCode Zen and Go) failed before its process started. CodexSession.DirectoryFileUrl
    /// already avoids this with a non-"file" scheme, which takes SwiftURL.Path's plain percent-decode branch.</summary>
    private static SwiftURL DirectoryFileUrl(string path)
    {
        var normalized = path.Replace('\\', '/');
        var builder = new System.Text.StringBuilder();
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(normalized))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~/:".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        // A non-"file" scheme, as CodexSession.DirectoryFileUrl does (see the note above): the path round-trips unchanged.
        return SwiftURL.Create("marrow-directory-path:" + builder) ?? throw SwiftTraps.Fatal("Invalid probe directory path.");
    }

    /// <summary>:231-238. The throwaway probe session's configuration: always supervised/build, so the probe
    /// never launches with a permissive flag or opens a session in the user's project. :898-920's convenience
    /// init (resumeID, resumeAt, model, effort all nil) is reached directly here, since those properties
    /// already default to null on the record.</summary>
    private static SessionConfiguration ProbeConfiguration(ProviderKind provider, SwiftURL executable, string directoryPath, ValueMap<string, string> environment) =>
        new()
        {
            Provider = provider, // :232
            Executable = executable, // :233
            WorkingDirectory = DirectoryFileUrl(directoryPath), // :234
            Environment = environment, // :235
            RuntimeMode = RuntimeMode.Supervised, // :236
            InteractionMode = InteractionMode.Build, // :237
        };

    // :228-247. Part 6 of 8 (PR-PROVACP-136, 137, 138).
    public static async Task<ValueList<ModelOption>> ProbeModels(ProviderKind provider, SwiftURL executable, ValueMap<string, string> environment)
    {
        var directoryPath = PrepareProbeCatalogDirectory(); // :229-230
        var session = new ACPSession(ProbeConfiguration(provider, executable, directoryPath, environment)); // :231-238
        var models = ValueList<ModelOption>.Empty; // :239
        session.OnEvent = evt => { if (evt is ProviderEvent.Models list) models = list.Value; }; // :240-242, only .models events; the last one wins
        try
        {
            _ = await session.Start(); // :244
            await session.ProbeModelEfforts(); // :245
        }
        finally
        {
            session.Stop(); // :243 defer { session.stop() }, runs on every exit path
        }
        return models; // :246
    }

    // MARK: - Session state

    // :251-269. Part 2 of 8 (PR-PROVACP-026, 028-031).
    private Task<JSONValue> OpenSession(JSONRPCConnection connection) => OpenSessionCore(connection.Request);

    // PORT-ONLY: explicit offline request seam; body is the existing OpenSession protocol unchanged.
    private async Task<JSONValue> OpenSessionCore(Func<string, JSONValue, Task<JSONValue>> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // :252-253. The connected servers ride into every new session through ACP's own field, HTTP ones
        // directly where the agent says it speaks HTTP MCP, else through mcp-remote.
        var baseParams = new Dictionary<string, JSONValue>
        {
            ["cwd"] = WorkingDirectory, // :254
            ["mcpServers"] = MCPProviderConfig.AcpBuiltIns(skillsMcp is { } bridge
                ? new JSONValue.Array(new ValueList<JSONValue>((MCPProviderConfig.AcpServers(supportsHTTPMCP).JSONValueArray ?? []).Append(bridge.Configuration)))
                : MCPProviderConfig.AcpServers(supportsHTTPMCP), MCPProviderConfig.BuiltInServers(advisorServer ??= MCPProviderConfig.AdvisorServer(configuration),
                    dextraServer ??= MCPProviderConfig.DextraServer(configuration))), // :254 + SK-17 live-proven per-session server + ED-NEW-ignisadvisor-2 a Hydra head's ask_advisor + ED-NEW-dextra-as4-7 a Dextra chat's nth-dextra
        };
        if (configuration.ResumeID is { } resumeID && canLoadSessions) // :255
        {
            var parameters = new Dictionary<string, JSONValue>(baseParams) { ["sessionId"] = resumeID }; // :256-257
            isReplaying = true; // :258
            var result = await SwiftTry.OptionalAsync<JSONValue>(async () =>
                await request("session/load", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters)))); // :259
            isReplaying = false; // :260
            if (result is { } r) // :261
            {
                sessionID = resumeID; // :262
                return r; // :263
            }
        }
        var newResult = await request("session/new", new JSONValue.Object(new ValueMap<string, JSONValue>(baseParams))); // :266
        sessionID = newResult["sessionId"]?.JSONValueString; // :267
        return newResult; // :268
    }

    // :271-279. Part 2 of 8 (PR-PROVACP-034).
    private void ApplySessionState(JSONValue state)
    {
        if (state["modes"] is { } modes && !modes.IsNull) // :272
        {
            hasModeAPI = true; // :273
            currentMode = modes["currentModeId"]?.JSONValueString; // :274
            ResolveModes(new ValueList<string>((modes["availableModes"]?.JSONValueArray ?? [])
                .Select(m => m["id"]?.JSONValueString).OfType<string>())); // :275
        }
        if (state["models"] is { } modelState && !modelState.IsNull) ApplyModels(modelState); // :277
        if (state["configOptions"]?.JSONValueArray is { } options) ApplyConfigOptions(new ValueList<JSONValue>(options)); // :278
    }

    /// <summary>Cursor and OpenCode report the effort scale of the current model only (the thought_level
    /// config option in each reply), so the catalog probe walks the list: each model without a scale yet is
    /// made current in turn and its scale read off the reply, and the model the session started on is put
    /// back. Models that arrived with a scale (Grok lists them in _meta.reasoningEfforts) are skipped by the
    /// where filter. :281-286. Part 6 of 8.</summary>
    private async Task ProbeModelEfforts()
    {
        if (modelConfigID is not { } modelConfigIDValue) return; // :288
        var original = currentModel; // :289
        // :290 `for model in models where ...` iterates a snapshot of the array as it stood when the loop
        // started (Swift array value semantics); `models` is reassigned inside the loop body (SetConfigOption
        // -> ApplyConfigOptions), so the walk is taken over a materialized copy, never the live field.
        var toWalk = models.Where(model => model.Efforts.Count == 0).ToList();
        foreach (var model in toWalk)
        {
            await SetConfigOption(modelConfigIDValue, model.Id); // :291
            currentModel = model.Id; // :292
        }
        if (original is { } originalValue && !SameModel(originalValue, currentModel)) // :294
        {
            await SetConfigOption(modelConfigIDValue, originalValue); // :295
            currentModel = originalValue; // :296
        }
    }

    // :300-304. Part 2 of 8 (PR-PROVACP-035, PR-PROVACP-036).
    private void ResolveModes(IReadOnlyList<string> ids)
    {
        planMode = ids.FirstOrDefault(id => id == "plan"); // :301
        buildMode = ids.FirstOrDefault(id => id is "agent" or "build" or "code" or "default") // :302
            ?? ids.FirstOrDefault(id => id != "plan" && id != "ask"); // :303
    }

    // :306-325. Part 2 of 8 (PR-PROVACP-025, 037, 038).
    private void ApplyModels(JSONValue state)
    {
        var current = state["currentModelId"]?.JSONValueString is { } rawCurrent ? BaseModelID(rawCurrent) : null; // :307
        currentModel = current ?? currentModel; // :308
        var list = new ValueList<ModelOption>((state["availableModels"]?.JSONValueArray ?? [])
            .Select(entry =>
            {
                if (entry["modelId"]?.JSONValueString is not { } rawID) return null; // :310
                var id = UsesParameterizedModels ? BaseModelID(rawID) : rawID; // :311
                var efforts = entry["_meta"]?["reasoningEfforts"]?.JSONValueArray ?? []; // :312
                return (ModelOption?)new ModelOption( // :313-320
                    id: id,
                    name: entry["name"]?.JSONValueString ?? id,
                    detail: entry["description"]?.JSONValueString,
                    efforts: OrderedEfforts(new ValueList<string>(efforts.Select(e => e["value"]?.JSONValueString).OfType<string>())),
                    defaultEffort: efforts.FirstOrDefault(e => e["default"]?.JSONValueBool == true)?["value"]?.JSONValueString,
                    isDefault: current is { } cur && SwiftString.Equal(id, cur));
            })
            .OfType<ModelOption>());
        if (list.Count == 0) return; // :322
        models = list; // :323
        OnEvent?.Invoke(new ProviderEvent.Models(list, currentModel)); // :324
    }

    // :327-398. Part 2 of 8 (PR-PROVACP-039..047, 049).
    private void ApplyConfigOptions(IReadOnlyList<JSONValue> options)
    {
        var efforts = ValueList<string>.Empty; // :328
        var hasFast = false; // :329
        effortConfigID = null; // :330
        fastConfigID = null; // :331
        foreach (var option in options) // :332
        {
            if (option["id"]?.JSONValueString is not { } id) continue; // :333
            var values = Flatten(new ValueList<JSONValue>(option["options"]?.JSONValueArray ?? [])); // :334
            var current = option["currentValue"]?.JSONValueString; // :335
            switch (option["category"]?.JSONValueString ?? id) // :336
            {
                case "model": // :337
                    modelConfigID = id; // :338
                    currentModel = current is { } cur ? (UsesParameterizedModels ? BaseModelID(cur) : cur) : currentModel; // :339
                    // :340-343. One lookup per value: the walk over OpenCode's four hundred models replies
                    // four hundred times, and a linear search per value on each reply stalled the main actor.
                    var knownByID = new Dictionary<string, ModelOption>(SwiftStringComparer.Instance); // R-STR-2: Dictionary<String, _> is canonical
                    foreach (var known in models)
                    {
                        var baseID = BaseModelID(known.Id);
                        if (!knownByID.ContainsKey(baseID)) knownByID[baseID] = known; // first wins on a duplicate
                    }
                    var list = new ValueList<ModelOption>(values.Select(value =>
                    {
                        if (value["value"]?.JSONValueString is not { } rawID) return null; // :345
                        var valueID = UsesParameterizedModels ? BaseModelID(rawID) : rawID; // :346
                        // :347-349. A rebuilt list keeps what earlier passes learned about each model (its
                        // scale, its Fast tier): Cursor reports them for the current model only, one pass at a time.
                        knownByID.TryGetValue(BaseModelID(valueID), out var knownOption); // :350
                        var built = new ModelOption( // :351-358
                            id: valueID,
                            name: value["name"]?.JSONValueString ?? valueID,
                            detail: value["description"]?.JSONValueString,
                            efforts: knownOption?.Efforts ?? ValueList<string>.Empty,
                            defaultEffort: knownOption?.DefaultEffort,
                            isDefault: currentModel is { } cm && SwiftString.Equal(valueID, cm));
                        built = built with { FastTier = knownOption?.FastTier }; // :359
                        return (ModelOption?)built;
                    }).OfType<ModelOption>());
                    if (list.Count > 0) models = list; // :362
                    break;
                case "mode": // :363
                    modeConfigID = id; // :364
                    currentMode = current ?? currentMode; // :365
                    ResolveModes(new ValueList<string>(values.Select(v => v["value"]?.JSONValueString).OfType<string>())); // :366
                    break;
                case "thought_level" or "effort": // :367
                    // :368-369. Cursor also advertises a boolean `thinking` thought_level; only the scale
                    // (low/medium/high/...) drives Marrow's effort slider.
                    var scale = new ValueList<string>(values.Select(v => v["value"]?.JSONValueString).OfType<string>()); // :370
                    if (!IsEffortScale(scale)) break; // :371
                    effortConfigID = id; // :372
                    currentEffort = current; // :373
                    efforts = OrderedEfforts(scale); // :374
                    break;
                case "model_config": // :375
                    if (SwiftString.Equal(id, "fast")) // :376
                    {
                        fastConfigID = id; // :377
                        currentFast = current; // :378
                        hasFast = true; // :379
                    }
                    break;
                default: break; // :381-382
            }
        }
        models = new ValueList<ModelOption>(models.Select(model => // :385-396
        {
            var isCurrent = SameModel(model.Id, currentModel); // :387
            if (isCurrent && efforts.Count > 0) // :388
                model = model with { Efforts = efforts, DefaultEffort = currentEffort ?? model.DefaultEffort }; // :389-390
            if (isCurrent) model = model with { FastTier = hasFast ? "true" : null }; // :392-393
            return model;
        }));
        if (models.Count > 0) OnEvent?.Invoke(new ProviderEvent.Models(models, currentModel)); // :397
    }

    // :400-405. Part 2 of 8 (PR-PROVACP-041). Exactly ONE level of nesting is unwrapped.
    private static ValueList<JSONValue> Flatten(IReadOnlyList<JSONValue> values) =>
        new(values.SelectMany(value => value["options"]?.JSONValueArray is { } nested ? nested : new[] { value })); // :401-403

    /// <summary>Values that belong on the effort slider, as opposed to a boolean thinking toggle. :408-411.
    /// Part 2 of 8 (PR-PROVACP-046).</summary>
    private static readonly ValueSet<string> KnownEffortScale =
        new(["none", "low", "medium", "high", "xhigh", "max", "extra-high"], SwiftStringComparer.Instance); // :409

    private static bool IsEffortScale(IReadOnlyList<string> values) =>
        values.Any(v => KnownEffortScale.Contains(SwiftString.Lowercased(v))); // :410

    /// <summary>The scale low to high, whatever order the agent lists it in: Grok's ACP advertises its levels
    /// from extra high down to low, and the slider reads the list left to right as low to high, so its knob
    /// sat at Low for the highest. A level the ranks do not know keeps its place among its neighbours.
    /// :417-430. Part 2 of 8 (PR-PROVACP-047). Exported (not duplicated) for ProviderRegistry's reuse
    /// (PR-PROVACP-048, a different unit).</summary>
    private static readonly ValueMap<string, int> EffortRanks = new(new Dictionary<string, int> // :419-421
    {
        ["none"] = 0, ["minimal"] = 1, ["low"] = 2, ["medium"] = 3, ["high"] = 4,
        ["xhigh"] = 5, ["extra-high"] = 5, ["extra_high"] = 5, ["extra high"] = 5, ["max"] = 6, ["ultra"] = 6,
    }, SwiftStringComparer.Instance);

    public static ValueList<string> OrderedEfforts(IReadOnlyList<string> values)
    {
        var ranked = values.Select((value, offset) => (Offset: offset, Value: value, // :422
            Rank: EffortRanks.TryGetValue(SwiftString.Lowercased(value), out var r) ? (int?)r : null)).ToList();
        if (ranked.Count(x => x.Rank is not null) <= 1) return new ValueList<string>(values); // :423
        return new ValueList<string>(ranked
            .OrderBy(x => x, Comparer<(int Offset, string Value, int? Rank)>.Create((lhs, rhs) =>
                lhs.Rank is { } l && rhs.Rank is { } r && l != r ? l.CompareTo(r) : lhs.Offset.CompareTo(rhs.Offset))) // :425-428
            .Select(x => x.Value)); // :429
    }

    /// <summary>R-STR-10: firstIndex(of:) is a UTF-16 offset over Characters, not Length arithmetic.</summary>
    private static int? FirstIndexOfOpenBracket(string text)
    {
        var offset = 0;
        foreach (var character in SwiftString.Characters(text))
        {
            if (SwiftString.Equal(character, "[")) return offset;
            offset += character.Length;
        }
        return null;
    }

    /// <summary>Cursor's parameterized IDs are `model[effort=high,...]`; the picker stores the base name.
    /// :432-436. Part 2 of 8 (used by PR-PROVACP-042, 043; PR-PROVACP-052 is a different unit's own record).</summary>
    private static string BaseModelID(string id)
    {
        if (FirstIndexOfOpenBracket(id) is not { } bracket) return id; // :434
        return id[..bracket]; // :435
    }

    // :438-441. Part 2 of 8 (used by PR-PROVACP-043; PR-PROVACP-053 is a different unit's own record).
    private static bool SameModel(string? lhs, string? rhs)
    {
        if (lhs is null || rhs is null) return false; // :439
        return SwiftString.Equal(BaseModelID(lhs), BaseModelID(rhs)); // :440
    }

    /// <summary>String comparison local to applySelection's three "differs from the current value" guards
    /// (effort, fast, mode): Swift's `x != optionalCurrent` is false-when-equal, true whenever the current
    /// value is nil. R-STR-2: SwiftString.Equal, not the CLR operator.</summary>
    private static bool Differs(string wanted, string? current) => current is null || !SwiftString.Equal(wanted, current);

    // :443-475. Part 3 of 8 (PR-PROVACP-054..061, 064).
    private async Task ApplySelection(string? model, string? effort, bool fastMode, InteractionMode interaction)
    {
        if (connection is not { } conn || sessionID is not { } sid) return; // :444
        if (model is { Length: > 0 } wantedModel) // :445
        {
            var wireID = UsesParameterizedModels ? BaseModelID(wantedModel) : wantedModel; // :446
            if (!SameModel(wireID, currentModel)) // :447
            {
                if (modelConfigID is { } modelConfigIDValue) await SetConfigOption(modelConfigIDValue, wireID); // :448-449
                else await SwiftTry.IgnoreAsync(async () => await conn.Request("session/set_model", // :450-451
                    JSONValue.FromDictionaryLiteral(("sessionId", sid), ("modelId", wireID))));
                currentModel = wireID; // :453, set even when the fallback request above was swallowed by try?
            }
        }
        if (effort is { Length: > 0 } wantedEffort && effortConfigID is { } effortConfigIDValue && Differs(wantedEffort, currentEffort)) // :456
        {
            await SetConfigOption(effortConfigIDValue, wantedEffort); // :457
            currentEffort = wantedEffort; // :458
        }
        if (fastConfigID is { } fastConfigIDValue) // :460
        {
            var wantedFast = fastMode ? "true" : "false"; // :461
            if (Differs(wantedFast, currentFast)) // :462
            {
                await SetConfigOption(fastConfigIDValue, wantedFast); // :463
                currentFast = wantedFast; // :464
            }
        }
        var wantedMode = interaction == InteractionMode.Plan ? planMode : buildMode; // :467
        if (wantedMode is { } modeValue && Differs(modeValue, currentMode)) // :467
        {
            if (modeConfigID is { } modeConfigIDValue) await SetConfigOption(modeConfigIDValue, modeValue); // :468-469
            else if (hasModeAPI) await SwiftTry.IgnoreAsync(async () => await conn.Request("session/set_mode", // :470-471
                JSONValue.FromDictionaryLiteral(("sessionId", sid), ("modeId", modeValue))));
            currentMode = modeValue; // :473, outside the transport branch: PR-PROVACP-061, reproduce as shipped.
        }
    }

    // :477-485. Part 3 of 8 (PR-PROVACP-062).
    private async Task SetConfigOption(string configID, string value)
    {
        if (connection is not { } conn || sessionID is not { } sid) return; // :478
        var result = await SwiftTry.OptionalAsync(async () => await conn.Request("session/set_config_option", // :479-483
            JSONValue.FromDictionaryLiteral(("sessionId", sid), ("configId", configID), ("value", value))));
        if (result?["configOptions"]?.JSONValueArray is { } options) ApplyConfigOptions(new ValueList<JSONValue>(options)); // :484
    }

    // :604-623. Part 4 of 8 (PR-PROVACP-074, 083). Called from Send's floating task (part 3) and HandleClose.
    private void FinishPrompt(string? stopReason, Exception? error)
    {
        if (!promptActive) return; // :605
        promptActive = false; // :606
        skillEvents.Clear();
        todoToolCallIDs.Clear(); // :607
        foreach (var (key, id) in pendingPermissions) // :608
        {
            connection?.Respond(id, JSONValue.FromDictionaryLiteral(("outcome", JSONValue.FromDictionaryLiteral(("outcome", "cancelled"))))); // :609
            OnEvent?.Invoke(new ProviderEvent.RequestResolved(key)); // :610
        }
        pendingPermissions.Clear(); // :612
        if (cancelRequested || stopReason == "cancelled") // :613
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null)); // :614
        else if (error is not null) // :615
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, SwiftErrors.LocalizedDescription(error))); // :616
        else if (stopReason == "refusal") // :617
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "The agent declined this request.")); // :618
        else
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)); // :620
        cancelRequested = false; // :622
    }

    // MARK: - Updates

    // :489-553. Part 4 of 8 (PR-PROVACP-076..093).
    private void HandleNotification(string method, JSONValue parameters)
    {
        if (method != "session/update" || isReplaying || parameters["update"] is not { } update) return; // :490
        switch (update["sessionUpdate"]?.JSONValueString) // :491
        {
            case "agent_message_chunk": // :492
            {
                if (update["content"]?["text"]?.JSONValueString is not { } text) return; // :493
                thoughtID = null; // :494
                var id = update["messageId"]?.JSONValueString is { } messageId ? $"message-{messageId}" : messageID ?? NextID("message"); // :495
                messageID = id; // :496
                OnEvent?.Invoke(new ProviderEvent.MessageDelta(id, text)); // :497
                break;
            }
            case "agent_thought_chunk": // :498
            {
                if (update["content"]?["text"]?.JSONValueString is not { } text) return; // :499
                var id = thoughtID ?? NextID("thought"); // :500
                thoughtID = id; // :501
                OnEvent?.Invoke(new ProviderEvent.ReasoningDelta(id, text)); // :502
                break;
            }
            case "tool_call": // :503
            {
                if (update["toolCallId"]?.JSONValueString is not { } id) return; // :504
                messageID = null; // :505
                thoughtID = null; // :506
                if (IsTodoCall(update)) // :507
                {
                    todoToolCallIDs.Add(id); // :508
                    var steps = TodoSteps(update); // :509
                    if (steps.Count > 0) OnEvent?.Invoke(new ProviderEvent.Todos(steps)); // :510
                    return; // :511
                }
                Tap.ToolCall(update); // PORT-ONLY: SK-11e / DL-12
                skillEvents.Observe(update); // PORT-ONLY: SK-11c / DL-12
                OnEvent?.Invoke(new ProviderEvent.ToolStarted(id, MakeToolCall(update))); // :513
                var initial = MakeToolUpdate(update); // :514
                if (skillEvents.Finished(update, initial) is { } finished) EmitSkillEvent(finished);
                if (initial.Output is not null || initial.Status is not null) OnEvent?.Invoke(new ProviderEvent.ToolUpdated(id, initial)); // :515
                break;
            }
            case "tool_call_update": // :516
            {
                if (update["toolCallId"]?.JSONValueString is not { } id) return; // :517
                if (todoToolCallIDs.Contains(id) || IsTodoCall(update)) // :518
                {
                    todoToolCallIDs.Add(id); // :519
                    var steps = TodoSteps(update); // :520
                    if (steps.Count > 0) OnEvent?.Invoke(new ProviderEvent.Todos(steps)); // :521
                    return; // :522
                }
                Tap.ToolCallUpdate(update); // PORT-ONLY: SK-11e / DL-12
                var result = MakeToolUpdate(update);
                if (skillEvents.Finished(update, result) is { } finished) EmitSkillEvent(finished); // PORT-ONLY: SK-11c / DL-12
                OnEvent?.Invoke(new ProviderEvent.ToolUpdated(id, result)); // :524
                break;
            }
            case "plan": // :525
                OnEvent?.Invoke(new ProviderEvent.Todos(ParseTodoItems(update["entries"]))); // :526-527
                break;
            case "available_commands_update": // :528
            {
                var commands = new ValueList<SlashCommand>((update["availableCommands"]?.JSONValueArray ?? []) // :529
                    .Select(command => command["name"]?.JSONValueString is { } name
                        ? new SlashCommand { Name = name, Detail = command["description"]?.JSONValueString ?? "" } // :531
                        : null)
                    .OfType<SlashCommand>());
                OnEvent?.Invoke(new ProviderEvent.Commands(commands)); // :533
                break;
            }
            case "current_mode_update": // :534
                currentMode = update["currentModeId"]?.JSONValueString; // :535
                if (planMode is not null) OnEvent?.Invoke(new ProviderEvent.ModeChanged(currentMode == planMode ? InteractionMode.Plan : InteractionMode.Build)); // :536
                break;
            case "config_option_update": // :537
                if (update["configOptions"]?.JSONValueArray is { } configOptions) ApplyConfigOptions(new ValueList<JSONValue>(configOptions)); // :538
                break;
            case "session_info_update": // :539
                if (update["title"]?.JSONValueString is { Length: > 0 } title) OnEvent?.Invoke(new ProviderEvent.Title(title)); // :540
                break;
            case "usage_update": // :541
                if (update["used"]?.JSONValueInt is { } used) // :542
                    // Occupancy, not spend: `used` of `size` is how full the context window is, so it feeds the
                    // context meter and nothing else. The deltas a session total would give are the growth of
                    // that meter, and a compaction or a new request drops them; charging the ledger here would
                    // bill the context, not the tokens. :543-547
                    OnEvent?.Invoke(new ProviderEvent.Usage(new ContextUsage { UsedTokens = used, WindowTokens = update["size"]?.JSONValueInt })); // :548
                break;
            default: break; // :550-551
        }
    }

    // :555-590. Part 4 of 8 (PR-PROVACP-094..097).
    private void HandleRequest(RPCID id, string method, JSONValue parameters)
    {
        if (method != "session/request_permission") // :556
        {
            connection?.Respond(id, -32601, "Method not found"); // :557
            return; // :558
        }
        var options = parameters["options"]?.JSONValueArray ?? []; // :560
        var toolCall = parameters["toolCall"] ?? new JSONValue.Null(); // :561
        var call = MakeToolCall(toolCall); // :562
        if ((skillsMcp?.AutomaticOption(toolCall, options)
            ?? (dextraServer is not null ? Marrow.Services.Assistant.AssistantProviderWiring.AcpAutomaticOption(toolCall, options) : null) // PORT-ONLY ED-NEW-dextra-as4-7: the server's policy check governs nth-dextra
            ?? AutomaticOption(call.ToolCallKind, options)) is { } optionID) // :563 + SK-17 owned read-only tools
        {
            connection?.Respond(id, JSONValue.FromDictionaryLiteral( // :564-565
                ("outcome", JSONValue.FromDictionaryLiteral(("outcome", "selected"), ("optionId", optionID)))));
            return; // :565
        }
        var mapped = new ValueList<ApprovalRequest.Option>(options // :567-575
            .Select(option =>
            {
                if (option["optionId"]?.JSONValueString is not { } optionId) return null; // :568
                var role = option["kind"]?.JSONValueString switch // :569-573
                {
                    "allow_always" => ApprovalRequest.Option.Role.ApproveAlways,
                    "reject_once" or "reject_always" => ApprovalRequest.Option.Role.Decline,
                    _ => ApprovalRequest.Option.Role.Approve,
                };
                return (ApprovalRequest.Option?)new ApprovalRequest.Option(optionId, option["name"]?.JSONValueString ?? optionId, role); // :574
            })
            .OfType<ApprovalRequest.Option>());
        pendingPermissions[id.Key] = id; // :576
        Tap.Held(toolCall); // PORT-ONLY: SK-11e / DL-12
        var kind = call.ToolCallKind switch // :577-581
        {
            ToolCall.Kind.Command => ApprovalRequest.Kind.Command,
            ToolCall.Kind.Edit => ApprovalRequest.Kind.FileChange,
            _ => ApprovalRequest.Kind.Tool,
        };
        OnEvent?.Invoke(new ProviderEvent.Approval(new ApprovalRequest( // :582-589
            id.Key, kind, call.Title, mapped, call.Detail, null, toolCall["toolCallId"]?.JSONValueString)));
        if (skillEvents.Held(toolCall) is { } held) EmitSkillEvent(held);
    }

    // :592-602. Part 4 of 8 (PR-PROVACP-095).
    private string? AutomaticOption(ToolCall.Kind kind, IReadOnlyList<JSONValue> options)
    {
        var automatic = runtimeMode switch // :593-597
        {
            RuntimeMode.FullAccess => true,
            RuntimeMode.AutoAcceptEdits => kind == ToolCall.Kind.Edit,
            _ => false,
        };
        if (!automatic) return null; // :598
        var allowOnce = options.FirstOrDefault(o => o["kind"]?.JSONValueString == "allow_once"); // :599
        var allowAlways = options.FirstOrDefault(o => o["kind"]?.JSONValueString == "allow_always"); // :600
        return (allowOnce ?? allowAlways)?["optionId"]?.JSONValueString; // :601
    }

    // :625-631. Part 4 of 8 (PR-PROVACP-074).
    private void HandleClose(string tail)
    {
        skillEvents.Clear();
        skillsMcp?.Dispose();
        if (promptActive) // :626
            FinishPrompt(null, new ProviderError(new ProviderError.Case.Failed(TextCleanup.LastLines(tail) ?? "The agent exited."))); // :627
        if (isStopping) return; // :629
        OnEvent?.Invoke(new ProviderEvent.Exited(TextCleanup.LastLines(tail))); // :630
    }

    // MARK: - Mapping (part 4 of 8: static helpers used by handleNotification/handleRequest; makeToolCall and
    // makeToolUpdate's own title-synthesis records sit in a later part, but both are implemented now so this
    // part's tests (PR-PROVACP-080..082) can exercise the real event flow through handleNotification.)

    /// <summary>True when a tool_call update belongs to a todo-list tool. :635-642.</summary>
    private static bool IsTodoCall(JSONValue update)
    {
        if (update["rawInput"]?["todos"] is not null) return true; // :639
        if (update["title"]?.JSONValueString is { } title && IsTodoTitle(title)) return true; // :640
        return false; // :641
    }

    private static readonly ValueSet<string> TodoExactTitles = // :647
        new(["todo", "todos", "todowrite", "todo write", "write todos"], SwiftStringComparer.Instance);

    private static bool IsTodoTitle(string title) // :644-659
    {
        var text = SwiftString.Lowercased(title.Trimmed()); // :645
        if (TodoExactTitles.Contains(text)) return true; // :646-648
        foreach (var suffix in new[] { " todos", " todo" }) // :653
        {
            if (!SwiftString.HasSuffix(text, suffix)) continue; // :654
            var count = FoundationText.Trim(SwiftString.DropLast(text, SwiftString.Count(suffix))); // :655
            if (count.Length > 0 && SwiftString.Characters(count).All(SwiftCharacter.IsNumber)) return true; // :656
        }
        return false; // :658
    }

    /// <summary>The checklist carried by a todo tool call: structured input first, then the JSON list the tool
    /// reports back as its result text. :661-683.</summary>
    private static ValueList<TodoStep> TodoSteps(JSONValue update)
    {
        var steps = ParseTodoItems(update["rawInput"]?["todos"]); // :664
        if (steps.Count > 0) return steps; // :665
        steps = ParseTodoItems(update["rawOutput"]?["todos"]); // :666
        if (steps.Count > 0) return steps; // :667
        foreach (var block in update["content"]?.JSONValueArray ?? []) // :668
        {
            if (block["type"]?.JSONValueString != "content") continue; // :669
            if (block["content"]?["text"]?.JSONValueString is not { } text) continue; // :670
            if (JSONValue.Parse(text) is not { } json) continue; // :671
            steps = ParseTodoItems(json["todos"] ?? json); // :672
            if (steps.Count > 0) return steps; // :673
        }
        if (update["rawOutput"] is { } raw && !raw.IsNull) // :675
        {
            foreach (var key in new[] { "output", "stdout", "text" }) // :676
            {
                if (raw[key]?.JSONValueString is not { } text) continue; // :677
                if (JSONValue.Parse(text) is not { } json) continue; // :677
                steps = ParseTodoItems(json["todos"] ?? json); // :678
                if (steps.Count > 0) return steps; // :679
            }
        }
        return ValueList<TodoStep>.Empty; // :682
    }

    /// <summary>Todo items across provider shapes. Unknown statuses stay pending so a new provider state never
    /// drops an item. :685-699.</summary>
    public static ValueList<TodoStep> ParseTodoItems(JSONValue? value) =>
        new((value?.JSONValueArray ?? [])
            .Select(item =>
            {
                var text = item["content"]?.JSONValueString ?? item["text"]?.JSONValueString ?? item["step"]?.JSONValueString ?? item["title"]?.JSONValueString; // :690
                if (text is null || text.Trimmed().Length == 0) return null; // :691
                var status = item["status"]?.JSONValueString switch // :692-696
                {
                    "completed" or "complete" or "done" => TodoStep.Status.Done,
                    "in_progress" or "inProgress" or "in-progress" or "active" => TodoStep.Status.Active,
                    _ => TodoStep.Status.Pending,
                };
                return (TodoStep?)new TodoStep { Text = text, TodoStepStatus = status }; // :697
            })
            .OfType<TodoStep>());

    // :701-704.
    private string NextID(string prefix)
    {
        counter += 1; // :702
        return $"{prefix}-{counter}-{SwiftUUID.UuidString(Guid.NewGuid())[..8]}"; // :703
    }

    private static readonly string[] PathKeys = // :773
        ["filePath", "file_path", "path", "file", "notebook_path", "target_file", "targetFile"];

    private static string? StringIn(JSONValue value, IReadOnlyList<string> keys) // :775-780
    {
        foreach (var key in keys) // :776
            if (value[key]?.JSONValueString?.Trimmed() is { Length: > 0 } text) return text; // :777
        return null; // :779
    }

    private static readonly string[] SubjectPhrases = // :788-792
    [
        "searched web for ", "search web for ", "searching web for ", "searched the web for ", "search the web for ",
        "searched for ", "searching for ", "search for ", "reading file ", "read file ", "listed directory ",
        "listing directory ", "list directory ", "listed files in ", "list files in ", "listing files in ",
    ];

    private static readonly ValueSet<string> SubjectVerbs = new([ // :793-799
        "read", "reading", "viewed", "viewing", "view", "fetched", "fetching", "fetch", "browsed", "browsing",
        "searched", "searching", "search", "grep", "glob", "listed", "listing", "list", "wrote", "writing",
        "write", "edited", "editing", "edit", "modified", "modifying", "modify", "updated", "updating", "update",
        "created", "creating", "create", "deleted", "deleting", "delete", "ran", "running", "run", "executed",
        "executing", "execute", "called", "calling", "call", "opened", "opening", "open",
    ], SwiftStringComparer.Instance);

    private static readonly (string Open, string Close)[] QuotePairs = // :821
    [
        ("'", "'"), ("\"", "\""), ("“", "”"), ("`", "`"), // U+201C LEFT / U+201D RIGHT DOUBLE QUOTATION MARK
    ];

    // :801-826.
    private static string Subject(string? title, ToolCall.Kind kind)
    {
        var text = CleanTitle(title); // :802
        if (text.Length == 0) return ""; // :803
        if (IsBareToolName(text)) return ""; // :804
        var rowHasVerb = kind is ToolCall.Kind.Read or ToolCall.Kind.Edit or ToolCall.Kind.Search or ToolCall.Kind.Web; // :809,:814
        var lowered = SwiftString.Lowercased(text); // :807
        var stripped = false; // :808
        if (rowHasVerb) // :809
        {
            foreach (var phrase in SubjectPhrases)
            {
                if (!SwiftString.HasPrefix(lowered, phrase)) continue; // :809
                text = SwiftString.DropFirst(text, SwiftString.Count(phrase)); // :810
                stripped = true; // :811
                break; // :812
            }
        }
        if (!stripped && rowHasVerb) // :814-815
        {
            var head = SwiftString.Split(lowered, " ", maxSplits: 1) is [var first, ..] ? first : null; // :815
            if (head is not null && SubjectVerbs.Contains(head)) // :815
            {
                var rest = FoundationText.Trim(SwiftString.DropFirst(text, SwiftString.Count(head))); // :816
                if (rest.Length > 0) text = rest; // :817
            }
        }
        text = text.Trimmed(); // :819
        foreach (var (open, close) in QuotePairs) // :820-824
            if (SwiftString.Count(text) >= 2 && SwiftString.HasPrefix(text, open) && SwiftString.HasSuffix(text, close))
                text = SwiftString.DropLast(SwiftString.DropFirst(text, 1), 1);
        return text; // :825
    }

    private static readonly ValueSet<string> BareToolNames = new([ // :831-836
        "read", "write", "edit", "multiedit", "patch", "bash", "shell", "list", "ls", "glob", "grep", "search",
        "webfetch", "websearch", "fetch", "todowrite", "todoread", "task", "tool", "apply_patch", "read_file",
        "write_file", "edit_file", "list_files", "search_files", "run_command", "run_terminal_cmd", "web_search",
        "web_fetch", "codebase_search", "file_search", "grep_search", "list_dir", "view_file", "replace_file_content",
    ], SwiftStringComparer.Instance);

    // :828-837.
    private static bool IsBareToolName(string title) => BareToolNames.Contains(SwiftString.Lowercased(title));

    // :840-849.
    private ValueList<FileEdit> Diffs(JSONValue? content) =>
        new((content?.JSONValueArray ?? [])
            .Select(block =>
            {
                if (block["type"]?.JSONValueString != "diff" || block["path"]?.JSONValueString is not { } path) return null; // :842
                return (FileEdit?)new FileEdit( // :843-847
                    ToolTitles.RelativePath(path, WorkingDirectory),
                    block["oldText"]?.JSONValueString ?? "",
                    block["newText"]?.JSONValueString ?? "");
            })
            .OfType<FileEdit>());

    // :851-853.
    private static string CleanTitle(string? title)
    {
        var text = title ?? "";
        var start = 0;
        var end = text.Length;
        while (start < end && IsCleanTitleTrimChar(text[start])) start++;
        while (end > start && IsCleanTitleTrimChar(text[end - 1])) end--;
        return text[start..end];
    }

    private static bool IsCleanTitleTrimChar(char c) => c is '`' or ' ' or '\n'; // :852, CharacterSet(charactersIn: "` \n")

    // :855-864.
    private static ToolCall.Kind Kind(string? raw) => raw switch
    {
        "read" => ToolCall.Kind.Read,
        "edit" or "delete" or "move" => ToolCall.Kind.Edit,
        "search" => ToolCall.Kind.Search,
        "execute" => ToolCall.Kind.Command,
        "fetch" => ToolCall.Kind.Web,
        _ => ToolCall.Kind.Other,
    };

    // :866-877.
    private static string FallbackTitle(ToolCall.Kind kind) => kind switch
    {
        ToolCall.Kind.Command => "Run command",
        ToolCall.Kind.Read => "Read file",
        ToolCall.Kind.Edit => "Edit file",
        ToolCall.Kind.Search => "Search",
        ToolCall.Kind.Web => "Fetch",
        ToolCall.Kind.Mcp => "Tool",
        ToolCall.Kind.Agent => "Agent",
        ToolCall.Kind.Other => "Tool",
        _ => throw new UnreachableException(),
    };

    /// <summary>True when a call's title is just the tool's display name, no subject. :879-887.</summary>
    private static bool IsFallbackTitle(string title) => SwiftString.Lowercased(title) switch
    {
        "run command" or "read file" or "edit file" or "write file" or "search" or "fetch" or "tool" or "agent" or "file" or "files" => true,
        _ => IsBareToolName(title),
    };

    // :889-895.
    private static ToolCall.Status Status(string raw) => raw switch
    {
        "completed" => ToolCall.Status.Completed,
        "failed" => ToolCall.Status.Failed,
        _ => ToolCall.Status.Running,
    };

    /// <summary>Same row shapes as Claude's: the subject is the command, the path, the pattern or the URL, and
    /// the detail only qualifies it. :706-737.</summary>
    private ToolCall MakeToolCall(JSONValue update)
    {
        var kind = Kind(update["kind"]?.JSONValueString); // :712
        var raw = update["rawInput"] ?? new JSONValue.Null(); // :713
        var location = update["locations"]?.JSONValueArray?.FirstOrDefault()?["path"]?.JSONValueString is { } locationPath
            ? ToolTitles.RelativePath(locationPath, WorkingDirectory) : null; // :714
        var path = StringIn(raw, PathKeys) is { } rawPath ? ToolTitles.RelativePath(rawPath, WorkingDirectory) : location; // :715
        var title = Subject(update["title"]?.JSONValueString, kind); // :716
        string callTitle = ""; // :717
        string? callDetail = null;
        switch (kind)
        {
            case ToolCall.Kind.Command: // :719
                callTitle = raw["command"]?.JSONValueString is { } command ? ToolTitles.UnwrapShell(command) : title; // :720
                break;
            case ToolCall.Kind.Read or ToolCall.Kind.Edit: // :721
                callTitle = path ?? title; // :722
                break;
            case ToolCall.Kind.Search: // :723
                var pattern = StringIn(raw, ["pattern", "query", "glob", "regex", "search"]); // :724
                callTitle = pattern ?? title.NilIfEmpty() ?? path ?? ""; // :725
                if ((pattern is not null || title.Length > 0) && path is { } searchPath && searchPath != WorkingDirectory && searchPath != callTitle)
                    callDetail = searchPath; // :726
                break;
            case ToolCall.Kind.Web: // :727
                callTitle = StringIn(raw, ["url", "query", "prompt"]) ?? title; // :728
                break;
            default: // :729, .mcp, .agent, .other
                callTitle = title; // :730
                if (path is { } otherPath && otherPath != WorkingDirectory && otherPath != callTitle) callDetail = otherPath; // :731
                break;
        }
        if (callTitle.Length == 0) callTitle = path ?? FallbackTitle(kind); // :733
        var edits = Diffs(update["content"]); // :734
        var status = update["status"]?.JSONValueString is { } statusText ? Status(statusText) : (ToolCall.Status?)null; // :735
        return new ToolCall // :717, :736
        {
            ToolCallKind = kind,
            Title = callTitle,
            Detail = callDetail,
            Edits = edits,
            ToolCallStatus = status ?? ToolCall.Status.Running,
        };
    }

    // :739-771.
    private ToolUpdate MakeToolUpdate(JSONValue update)
    {
        ToolCall.Kind? kind = update["kind"]?.JSONValueString is { } kindText ? Kind(kindText) : null; // :741
        var effectiveKind = kind ?? ToolCall.Kind.Other; // :742
        var title = Subject(update["title"]?.JSONValueString, effectiveKind); // :743
        string? resultTitle = null;
        if (title.Length > 0 && !IsFallbackTitle(title)) resultTitle = title; // :745
        if (update["rawInput"]?["command"]?.JSONValueString is { } command) resultTitle = ToolTitles.UnwrapShell(command); // :746
        if (effectiveKind is ToolCall.Kind.Read or ToolCall.Kind.Edit && resultTitle is null && // :748
            update["locations"]?.JSONValueArray?.FirstOrDefault()?["path"]?.JSONValueString is { } locationPath) // :749
        {
            var relative = ToolTitles.RelativePath(locationPath, WorkingDirectory); // :750
            if (relative != WorkingDirectory) resultTitle = relative; // :751
        }
        var status = update["status"]?.JSONValueString is { } statusText ? Status(statusText) : (ToolCall.Status?)null; // :753
        var edits = Diffs(update["content"]); // :754
        ValueList<FileEdit>? resultEdits = edits.Count > 0 ? edits : null; // :755
        var output = string.Join("\n", (update["content"]?.JSONValueArray ?? []) // :756-758
            .Select(block => block["type"]?.JSONValueString == "content" ? block["content"]?["text"]?.JSONValueString : null)
            .OfType<string>());
        long? exitCode = null;
        if (update["rawOutput"] is { } rawOutput && !rawOutput.IsNull) // :759
        {
            if (output.Length == 0) // :760
                output = string.Join("\n", new[] { rawOutput["stdout"]?.JSONValueString, rawOutput["stderr"]?.JSONValueString, rawOutput["output"]?.JSONValueString } // :761-764
                    .OfType<string>().Where(s => s.Length > 0));
            exitCode = rawOutput["exitCode"]?.JSONValueInt ?? rawOutput["metadata"]?["exit"]?.JSONValueInt; // :766
        }
        var resultOutput = output.Length > 0 && output != "(no output)" ? output : null; // :768
        var resultStatus = status;
        if (exitCode is { } exit && exit != 0 && resultStatus == ToolCall.Status.Completed) resultStatus = ToolCall.Status.Failed; // :769
        return new ToolUpdate { Title = resultTitle, Output = resultOutput, Status = resultStatus, ExitCode = exitCode, Edits = resultEdits, Kind = kind }; // :740,770
    }
}
