using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

// Source: DroppyCode/Services/Providers/ACPSession.swift (B2 1fbf81e), :1-921. Part 1 of 8: the launch gate,
// launchArguments, and start() through the initialize reply (PR-PROVACP-001..024). Part 2 of 8: openSession,
// applySessionState, resolveModes, applyModels, applyConfigOptions and its static helpers (flatten,
// isEffortScale, orderedEfforts, baseModelID, sameModel) (PR-PROVACP-025..047, 049). Part 3 of 8 (this change):
// applySelection, setConfigOption, send, interrupt, resolveApproval (PR-PROVACP-053..073; compact and
// answerQuestion were already implemented in part 1). probeModelEfforts, probeModels and the update/request/close
// handlers belong to later parts' own records; they stay MARROW_STUB pending those parts (R-FILE-1: one file
// per Swift file, but a function whose OWN records sit in a later part is left unimplemented until that part).

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
    private string? sessionID; // :32
#pragma warning disable CS0414 // set here, read by HandleNotification/HandleClose: part 4+
    private bool isReplaying; // :36
    private bool isStopping; // :37
#pragma warning restore CS0414
    private bool promptActive; // :43
#pragma warning disable CS0414 // set here (Send, Interrupt), read by FinishPrompt: part 4+
    private bool cancelRequested; // :44
    private string? messageID; // :39
    private string? thoughtID; // :40
#pragma warning restore CS0414
    private readonly Dictionary<string, RPCID> pendingPermissions = new(); // :42; R-COLL-2: iteration order unspecified
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
        isStopping = true;
        connection?.Close();
    }

    // MARK: - Catalog (part 6+: uses ApplySelection/ProbeModelEfforts)
    public static Task<ValueList<ModelOption>> ProbeModels(ProviderKind provider, SwiftURL executable, ValueMap<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:228-247, part 6+"); // :228

    // MARK: - Session state

    // :251-269. Part 2 of 8 (PR-PROVACP-026, 028-031).
    private async Task<JSONValue> OpenSession(JSONRPCConnection connection)
    {
        // :252-253. The connected servers ride into every new session through ACP's own field, HTTP ones
        // directly where the agent says it speaks HTTP MCP, else through mcp-remote.
        var baseParams = new Dictionary<string, JSONValue>
        {
            ["cwd"] = WorkingDirectory, // :254
            ["mcpServers"] = MCPProviderConfig.AcpServers(supportsHTTPMCP), // :254
        };
        if (configuration.ResumeID is { } resumeID && canLoadSessions) // :255
        {
            var parameters = new Dictionary<string, JSONValue>(baseParams) { ["sessionId"] = resumeID }; // :256-257
            isReplaying = true; // :258
            var result = await SwiftTry.OptionalAsync<JSONValue>(async () =>
                await connection.Request("session/load", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters)))); // :259
            isReplaying = false; // :260
            if (result is { } r) // :261
            {
                sessionID = resumeID; // :262
                return r; // :263
            }
        }
        var newResult = await connection.Request("session/new", new JSONValue.Object(new ValueMap<string, JSONValue>(baseParams))); // :266
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
    private Task ProbeModelEfforts() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:287-298, part 6+"); // :287

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
                    var knownByID = new Dictionary<string, ModelOption>();
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

    // :604-623. Part 4 of 8's own records; called from Send's floating task (part 3) and, later, HandleClose.
    private void FinishPrompt(string? stopReason, Exception? error) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:604-623, part 4+"); // :604

    // MARK: - Updates (part 4+)
    private void HandleNotification(string method, JSONValue parameters) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:489-553, part 4+"); // :489

    private void HandleRequest(RPCID id, string method, JSONValue parameters) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:555-590, part 4+"); // :555

    private void HandleClose(string tail) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ACPSession.swift:625-631, part 4+"); // :625
}
