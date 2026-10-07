using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Store;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Port.Skills.Shims;

// Source: DroppyCode/Services/Providers/CopilotSession.swift (B2 1fbf81e), :1-1072. The whole file: the
// lifecycle, the session parameters, modes and selection, send, slash commands, interrupt, compact and rewind,
// approvals and answers, stop, the catalog and account reads, the session.event routing, errors, server requests,
// permission prompts, close and the tool mapping (:27-1072).

namespace Marrow.Services.Providers;

// :27-1072. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed partial class CopilotSession : ProviderSession, ISkillSessionLifetime
{
    public Action<ProviderEvent>? OnEvent { get; set; } // :29
    // DEVIATION ED-363: DL-12 observations, independent of timeline events and RPC decisions.
    public Action<ToolEvent>? OnSkillsEvent { get; set; }
    public Exception? LastSkillEventFailure { get; private set; }
    private void EmitSkillEvent(ToolEvent value)
    {
        try { OnSkillsEvent?.Invoke(value); }
        catch (Exception error) { LastSkillEventFailure = error; } // DL-12: an observer cannot interrupt the existing timeline or permission protocol.
    }
    private readonly CopilotSkillEvents skillEvents = new();
    private readonly Generation? skillGeneration;

    // :31-38. R-ENUM-2: an associated-value enum -> abstract record with nested sealed records.
    private abstract record PendingRequest
    {
        private PendingRequest() { }

        /// <summary>A `permission.requested` event, answered by request id.</summary>
        public sealed record Permission(JSONValue Request, string? ToolCallID) : PendingRequest; // :32-33

        /// <summary>A `userInput.request` server request from the `ask_user` tool.</summary>
        public sealed record Question(RPCID Id, ValueList<string> Choices) : PendingRequest; // :34-35

        /// <summary>An `exitPlanMode.request` server request from the `exit_plan_mode` tool.</summary>
        public sealed record PlanExit(RPCID Id) : PendingRequest; // :36-37
    }

    private readonly SessionConfiguration configuration; // :40
    private JSONRPCConnection? skillsReader;
    public Task SkillReadersExited => skillsReader?.ChildExited ?? Task.CompletedTask;
    private JSONRPCConnection? connection; // :41
    private string? sessionID; // :42
    private RuntimeMode runtimeMode; // :43
    private InteractionMode interactionMode = InteractionMode.Build; // :44
    /// <summary>The CLI's own permission mode: `manual`, `assisted` or `allow-all`.</summary>
    private string permissionMode = "manual"; // :45-46
    private string? model; // :47
    private string? effort; // :48
    private readonly Dictionary<string, PendingRequest> pendingRequests = new(SwiftStringComparer.Instance); // :49; R-COLL-2
    private bool turnActive; // :50
    private string? turnError; // :51
    private bool interruptRequested; // :52
    private bool isStopping; // :53
    /// <summary>Tool names by call id, so a permission or a result knows what it belongs to.</summary>
    private readonly Dictionary<string, string> toolNames = new(SwiftStringComparer.Instance); // :54-55
    /// <summary>Tools the user declined, so their failed result reads as declined.</summary>
    private readonly HashSet<string> declinedTools = new(SwiftStringComparer.Instance); // :56-57
    /// <summary>Messages that streamed deltas, so an empty final message still ends the row.</summary>
    private readonly HashSet<string> streamedMessages = new(SwiftStringComparer.Instance); // :58-59
    /// <summary>Reasoning already shown from `assistant.reasoning` events (trimmed), so the copy a message carries in
    /// `reasoningText` is not shown a second time.</summary>
    // DEVIATION ED-025: keyed on the trimmed string itself, held in memory only, instead of its hashValue (:62, :1064-1066).
    private readonly HashSet<string> shownReasoning = new(SwiftStringComparer.Instance); // :60-62
    /// <summary>The lead's event sink, made once rather than once per event.</summary>
    private Action<ProviderEvent>? leadSink; // :63-64 lazy var (R-LIFE-2)
    /// <summary>The CLI's slash commands and their aliases, which `send` invokes instead of prompting with.</summary>
    private HashSet<string> commandNames = new(SwiftStringComparer.Instance); // :65-66
    /// <summary>The brief each `task` call gave its head, by call id, so a head that starts can be named by what it
    /// was sent to do.</summary>
    private readonly Dictionary<string, (string Description, string? Prompt)> taskBriefs = new(SwiftStringComparer.Instance); // :67-69

    /// <summary>Commands the app answers with its own controls: permission modes, the model picker, plan mode, the
    /// session list and the working directory.</summary>
    private static readonly HashSet<string> OwnCommands = new(
        ["allow-all", "yolo", "permissions", "model", "models", "plan", "session", "sessions", "cwd", "cd"], SwiftStringComparer.Instance); // :71-73

    // :75-80
    public CopilotSession(SessionConfiguration configuration) : this(configuration, null) { }

    // PORT-ONLY: receives the SK-9 lease from app wiring; caller keeps it until the connection closes.
    public CopilotSession(SessionConfiguration configuration, Generation? generation)
    {
        skillGeneration = generation;
        this.configuration = configuration;
        runtimeMode = configuration.RuntimeMode;
        model = configuration.Model;
        effort = configuration.Effort;
    }

    // :82-85
    public bool IsRunning => connection is { } c && !c.IsClosed;

    private string WorkingDirectory => configuration.WorkingDirectory.Path; // :87

    // :64. `lazy var leadSink = { [weak self] event in self?.onEvent?(event) }`.
    private Action<ProviderEvent> LeadSink => leadSink ??= e => OnEvent?.Invoke(e);

    // MARK: - Lifecycle

    // :91-115
    public async Task<string> Start()
    {
        if (configuration.Executable is not { } executable) throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider)); // :92
        var weak = new WeakReference<CopilotSession>(this);
        var connection = await Connect( // :93-101
            executable,
            configuration.WorkingDirectory.Path,
            configuration.Environment,
            connection =>
            {
                skillsReader = connection; // RR-15: retained through failed initialization and Stop
                connection.OnNotification = (method, parameters) => { if (weak.TryGetTarget(out var self)) self.HandleNotification(method, parameters); }; // :98
                connection.OnRequest = (id, method, parameters) => { if (weak.TryGetTarget(out var self)) self.HandleRequest(id, method, parameters); }; // :99
                connection.OnClose = tail => { if (weak.TryGetTarget(out var self)) self.HandleClose(tail); }; // :100
            });
        this.connection = connection; // :102

        var id = configuration.ResumeID ?? SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid())); // :104
        // DEVIATION ED-363: same create/resume payload, plus WU-8's optional root and single retry.
        _ = await OpenSkillsSession(id, connection.Request);
        sessionID = id; // :109
        // A resumed session comes back in interactive mode with manual permissions (verified live), so the thread's
        // modes are applied either way. :110-112
        await ApplyModes(configuration.RuntimeMode, configuration.InteractionMode);
        LoadCommands(); // :113
        return id; // :114
    }

    /// <summary>What every session asks for: streaming deltas, permission prompts, questions and plan exits routed to
    /// the app. Auto mode needs the CLI's experimental flag for its assisted-approval judge, which is a session
    /// setting, so the thread restarts the session when it switches into or out of Auto.</summary>
    private Dictionary<string, JSONValue> SessionParameters() // :117-158
    {
        var parameters = new Dictionary<string, JSONValue>(SwiftStringComparer.Instance) // :122-131
        {
            ["clientName"] = "marrow", // DEVIATION OD-0012 F1: upstream sends "droppy-code" (:123).
            ["workingDirectory"] = WorkingDirectory,
            ["streaming"] = true,
            ["requestPermission"] = true,
            ["requestUserInput"] = true,
            ["requestExitPlanMode"] = true,
            ["includeSubAgentStreamingEvents"] = configuration.Hydra?.RunsNatively == true,
            ["isExperimentalMode"] = configuration.RuntimeMode == RuntimeMode.Auto,
        };
        if (model is { } m && m.Length != 0) parameters["model"] = m; // :132
        if (effort is { } e && e.Length != 0) parameters["reasoningEffort"] = e; // :133
        // The native or fallback Hydra policy, when Hydra is on: the system message below is assembled fresh, once,
        // with the anti-slop guidance alongside it. :134-136
        string? hydraPolicy = null;
        if (configuration.Hydra is { } hydra) // :137
        {
            if (hydra.RunsNatively) // :138-140
            {
                parameters["customAgents"] = new JSONValue.Array(HydraPrompts.CopilotAgents(hydra));
                hydraPolicy = HydraPrompts.Policy(ProviderKind.Copilot, hydra);
            }
            else
            {
                // Heads on another provider are app-run: the lead asks for them with the delegation block, and no
                // agents of the CLI's own are defined. :141-145
                hydraPolicy = HydraPrompts.FallbackPolicy(hydra);
            }
        }
        // Exactly one appended system message for every session, Hydra or not. The off notice goes out too, so a
        // resumed thread cannot keep earlier guidance alive. :147-156
        parameters["systemMessage"] = JSONValue.FromDictionaryLiteral(
            ("mode", "append"),
            ("content", string.Join("\n\n", new[] { hydraPolicy, AntiSlopPolicy.Instructions(configuration.AntiSlopEnabled) }.OfType<string>())));
        CopilotSkillWiring.AddDirectories(parameters, skillGeneration, configuration.Executable?.Path);
        return parameters; // :157
    }

    // PORT-ONLY: explicit offline request seam for WU-8; production uses the existing connection.
    private Task<JSONValue> OpenSkillsSession(string id, Func<string, JSONValue, Task<JSONValue>> request)
    {
        var parameters = SessionParameters();
        parameters["sessionId"] = id;
        return CopilotSkillWiring.Open(configuration.ResumeID is null ? "session.create" : "session.resume",
            parameters, configuration.Executable?.Path, request);
    }

    // :160-177
    public async Task Send(TurnInput input)
    {
        if (connection is not { } conn || conn.IsClosed) throw new ProviderError(new ProviderError.Case.NotRunning()); // :161
        await ApplyModes(input.RuntimeMode, input.InteractionMode); // :162
        await ApplySelection(input.Model, input.Effort); // :163
        BeginTurn(); // :164
        try
        {
            // Slash commands are the terminal UI's, not the model's: the CLI runs them and answers with text to show
            // or a prompt to send. :166-172
            if (SlashCommand(input.Text) is { } command && commandNames.Contains(command.Name))
            {
                await InvokeCommand(command.Name, command.Input);
            }
            else
            {
                await SendPrompt(input.Text, input.Images);
            }
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) // :173-176
        {
            turnActive = false;
            throw;
        }
    }

    // :179-186
    private void BeginTurn()
    {
        turnActive = true;
        turnError = null;
        interruptRequested = false;
        shownReasoning.Clear();
        streamedMessages.Clear();
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null));
    }

    // :188-200
    private async Task SendPrompt(string text, IReadOnlyList<Attachment> images)
    {
        if (connection is not { } conn || sessionID is not { } sid) throw new ProviderError(new ProviderError.Case.NotRunning()); // :189
        var parameters = new Dictionary<string, JSONValue>(SwiftStringComparer.Instance) // :190-193
        {
            ["sessionId"] = sid,
            ["prompt"] = text,
        };
        // The CLI reads image files itself and hands them to vision models. :194-197
        var attachments = images.Where(image => image.IsImage).Select(image => JSONValue.FromDictionaryLiteral(
            ("type", "file"), ("path", image.Path), ("displayName", image.Name), ("mimeType", image.MimeType))).ToArray();
        if (attachments.Length != 0) parameters["attachments"] = JSONValue.FromArrayLiteral(attachments); // :198
        _ = await conn.Request("session.send", ObjectOf(parameters)); // :199
    }

    // :202-227
    private async Task InvokeCommand(string name, string input)
    {
        if (connection is not { } conn || sessionID is not { } sid) throw new ProviderError(new ProviderError.Case.NotRunning()); // :203
        var result = await conn.Request("session.commands.invoke", JSONValue.FromDictionaryLiteral( // :204-208
            ("sessionId", sid),
            ("name", name),
            ("input", input)));
        if (result["mode"]?.JSONValueString is { } mode) // :209-212
        {
            interactionMode = SwiftString.Equal(mode, "plan") ? InteractionMode.Plan : InteractionMode.Build;
            OnEvent?.Invoke(new ProviderEvent.ModeChanged(interactionMode));
        }
        switch (result["kind"]?.JSONValueString) // :213
        {
            case "agent-prompt": // :214-215
                await SendPrompt(result["prompt"]?.JSONValueString ?? input, ValueList<Attachment>.Empty);
                break;
            case "text":
            case "completed": // :216-222
            {
                var text = TextCleanup.StripANSI(result["text"]?.JSONValueString ?? result["message"]?.JSONValueString ?? "");
                if (text.Trimmed().Length != 0)
                {
                    var body = result["markdown"]?.JSONValueBool == true ? text : "```\n" + text + "\n```";
                    OnEvent?.Invoke(new ProviderEvent.MessageCompleted("command-" + SwiftUUID.UuidString(Guid.NewGuid()), body));
                }
                FinishTurn(aborted: false);
                break;
            }
            default: // :223-225
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
                {
                    NoticeLevel = Notice.Level.Warning,
                    Message = $"/{name} opens a dialog that only Copilot's own terminal UI can show.",
                }));
                FinishTurn(aborted: false);
                break;
        }
    }

    /// <summary>`/name rest...` at the start of a prompt, or nil for ordinary text.</summary>
    // :230. `$` -> `\z` (R-RE-1): the text is trimmed first, so it never ends in a newline. UNVERIFIED O-13
    [GeneratedRegex(@"^/([A-Za-z][\w-]*)(?:\s+([\s\S]*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlashCommandPattern();

    // :232-236. Swift: slashCommand(in:).
    public static (string Name, string Input)? SlashCommand(string text)
    {
        var trimmed = text.Trimmed(); // :233
        var match = SlashCommandPattern().Match(trimmed); // :234
        if (!match.Success) return null;
        return (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value.Trimmed() : ""); // :235
    }

    // :238-243
    public async Task Interrupt()
    {
        if (!turnActive || connection is not { } conn || sessionID is not { } sid) return; // :239
        interruptRequested = true; // :240
        CancelPendingRequests(); // :241
        await SwiftTry.IgnoreAsync(async () => await conn.Request("session.abort", JSONValue.FromDictionaryLiteral( // :242
            ("sessionId", sid), ("reason", "user_initiated"))));
    }

    // :245-248
    public async Task Compact()
    {
        if (connection is not { } conn || sessionID is not { } sid) throw new ProviderError(new ProviderError.Case.NotRunning()); // :246
        _ = await conn.Request("session.history.compact", JSONValue.FromDictionaryLiteral(("sessionId", sid), ("trigger", "manual"))); // :247
    }

    /// <summary>Drops the most recent turns from Copilot's own history. Files stay as they are: the app restores
    /// those from its checkpoints.</summary>
    // :250-268. Swift: rollback(turns:).
    public async Task Rollback(long turns)
    {
        if (connection is not { } conn || sessionID is not { } sid || turns <= 0) return; // :253
        var listed = await conn.Request("session.history.listRewindPoints", JSONValue.FromDictionaryLiteral(("sessionId", sid))); // :254
        var points = listed["points"]?.JSONValueArray ?? []; // :255
        // One point per user message; rewinding to a point drops that message and everything after it. :256-259
        if (points.Count < turns || points[checked((int)(points.Count - turns))]["eventId"]?.JSONValueString is not { } eventID)
        {
            throw new ProviderError(new ProviderError.Case.Failed("Copilot has no rewind point for that turn."));
        }
        var result = await conn.Request("session.history.rewind", JSONValue.FromDictionaryLiteral( // :260-264
            ("sessionId", sid),
            ("eventId", eventID),
            ("mode", "conversation")));
        if (!(result["outcome"]?.JSONValueString is { } outcome && SwiftString.Equal(outcome, "success"))) // :265-267
        {
            throw new ProviderError(new ProviderError.Case.Failed(result["error"]?.JSONValueString ?? "Copilot could not rewind the conversation."));
        }
    }

    // :270-303
    public void ResolveApproval(string requestID, string optionID)
    {
        if (!pendingRequests.Remove(requestID, out var pending)) return; // :271
        switch (pending) // :272
        {
            case PendingRequest.Permission(var request, var toolCallID): // :273
            {
                JSONValue decision; // :274
                switch (optionID) // :275
                {
                    case "allow": // :276-277
                        decision = JSONValue.FromDictionaryLiteral(("kind", "approve-once"), ("approvedInteractively", true));
                        break;
                    case "always": // :278-279
                        decision = SessionApproval(request) ?? JSONValue.FromDictionaryLiteral(("kind", "approve-once"), ("approvedInteractively", true));
                        break;
                    default: // :280-286
                        decision = JSONValue.FromDictionaryLiteral(("kind", "reject"), ("feedback", "The user declined this action."));
                        if (toolCallID is { } callID)
                        {
                            declinedTools.Add(callID);
                            OnEvent?.Invoke(new ProviderEvent.ToolUpdated(callID, new ToolUpdate { Status = global::Marrow.Core.Models.ToolCall.Status.Declined }));
                        }
                        break;
                }
                RespondPermission(requestID, decision); // :287
                break;
            }
            case PendingRequest.Question(var id, _): // :288-289
                connection?.Respond(id, JSONValue.FromDictionaryLiteral(("answer", ""), ("wasFreeform", true)));
                break;
            case PendingRequest.PlanExit(var id): // :290
                if (SwiftString.Equal(optionID, "allow")) // :291
                {
                    // The CLI leaves plan mode itself and says so in `session.mode_changed`. :292
                    connection?.Respond(id, JSONValue.FromDictionaryLiteral(("approved", true), ("selectedAction", "interactive"))); // :293
                    OnEvent?.Invoke(new ProviderEvent.ModeChanged(InteractionMode.Build)); // :294
                }
                else
                {
                    connection?.Respond(id, JSONValue.FromDictionaryLiteral( // :296-299
                        ("approved", false),
                        ("feedback", "The user wants to keep planning. Wait for their feedback before changing anything.")));
                }
                break;
            default:
                throw new System.Diagnostics.UnreachableException();
        }
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :302
    }

    // :305-310
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers)
    {
        // :306. The pending entry is removed whatever its case.
        if (!pendingRequests.Remove(requestID, out var pending) || pending is not PendingRequest.Question(var id, var choices)) return;
        // :307. `answers.values.first`: dictionary order is unspecified (R-COLL-2); Copilot asks one question at a time.
        var answer = answers.Count > 0 ? string.Join(", ", answers.Values.First()) : "";
        connection?.Respond(id, JSONValue.FromDictionaryLiteral(("answer", answer), ("wasFreeform", !choices.Contains(answer, SwiftStringComparer.Instance)))); // :308
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :309
    }

    // :312-324
    public void Stop()
    {
        isStopping = true; // :313
        skillEvents.Clear();
        if (connection is not { } conn || conn.IsClosed) return; // :314
        // A shutdown lets the CLI flush the session's event log; the close after it is the backstop. :315
        MainActor.Spawn(async () => // :316-319
        {
            await SwiftTry.IgnoreAsync(async () => await conn.Request("runtime.shutdown"));
            conn.Close();
        });
        MainActor.Spawn(async () => // :320-323
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(3));
            conn.Close();
        });
    }

    // MARK: - Modes and selection

    // :328-349
    private async Task ApplyModes(RuntimeMode runtime, InteractionMode interaction)
    {
        if (connection is not { } conn || sessionID is not { } sid) return; // :329
        runtimeMode = runtime; // :330
        var wanted = PermissionModeName(runtime); // :331
        if (!SwiftString.Equal(wanted, permissionMode)) // :332
        {
            // `assisted` is refused outside experimental mode; the reply says which mode holds, and in `manual`
            // every request reaches the app's own rules instead. :333-340
            var result = await SwiftTry.OptionalAsync<JSONValue>(async () => await conn.Request("session.permissions.setMode", JSONValue.FromDictionaryLiteral(
                ("sessionId", sid),
                ("mode", wanted),
                ("source", "rpc"))));
            permissionMode = result?["mode"]?.JSONValueString ?? permissionMode;
        }
        if (interaction != interactionMode) // :342-348
        {
            await SwiftTry.IgnoreAsync(async () => await conn.Request("session.mode.set", JSONValue.FromDictionaryLiteral(
                ("sessionId", sid),
                ("mode", interaction == InteractionMode.Plan ? "plan" : "interactive"))));
            interactionMode = interaction;
        }
    }

    // :351-369
    private async Task ApplySelection(string? model, string? effort)
    {
        if (connection is not { } conn || sessionID is not { } sid) return; // :352
        if (model is { } m && m.Length != 0 && !(this.model is { } current && SwiftString.Equal(m, current))) // :353
        {
            var parameters = new Dictionary<string, JSONValue>(SwiftStringComparer.Instance) { ["sessionId"] = sid, ["modelId"] = m }; // :354
            if (effort is { } e && e.Length != 0) parameters["reasoningEffort"] = e; // :355
            if (await SwiftTry.OptionalAsync<JSONValue>(async () => await conn.Request("session.model.switchTo", ObjectOf(parameters))) is not null) // :356
            {
                this.model = m; // :357
                if (effort is { } sent && sent.Length != 0) this.effort = sent; // :358
            }
        }
        if (effort is { } wanted && wanted.Length != 0 && !(this.effort is { } held && SwiftString.Equal(wanted, held))) // :361
        {
            if (await SwiftTry.OptionalAsync<JSONValue>(async () => await conn.Request("session.model.setReasoningEffort", JSONValue.FromDictionaryLiteral( // :362-365
                ("sessionId", sid),
                ("reasoningEffort", wanted)))) is not null)
            {
                this.effort = wanted; // :366
            }
        }
    }

    /// <summary>Full access lets the CLI approve everything itself. Auto asks its assisted-approval judge; the other
    /// modes keep every prompt in the app, where `automaticDecision` waves through what the mode allows.</summary>
    // :371-380
    public static string PermissionModeName(RuntimeMode mode) => mode switch
    {
        RuntimeMode.Supervised or RuntimeMode.AutoAcceptEdits => "manual", // :376
        RuntimeMode.Auto => "assisted", // :377
        RuntimeMode.FullAccess => "allow-all", // :378
        _ => throw new System.Diagnostics.UnreachableException(),
    };

    // :382-397
    private void LoadCommands()
    {
        if (connection is not { } conn || sessionID is not { } sid) return; // :383
        var weak = new WeakReference<CopilotSession>(this);
        MainActor.Spawn(async () => // :384
        {
            if (!weak.TryGetTarget(out var self)) return; // :385
            if (await SwiftTry.OptionalAsync<JSONValue>(async () => await conn.Request("session.commands.list", JSONValue.FromDictionaryLiteral(("sessionId", sid)))) is not { } result) return;
            var names = new HashSet<string>(SwiftStringComparer.Instance); // :386
            var commands = new List<SlashCommand>(); // :387
            foreach (var entry in result["commands"]?.JSONValueArray ?? []) // :388
            {
                if (entry["name"]?.JSONValueString is not { } name || OwnCommands.Contains(name)) continue; // :389
                names.Add(name); // :390
                names.UnionWith((entry["aliases"]?.JSONValueArray ?? []).Select(alias => alias.JSONValueString).OfType<string>()); // :391
                commands.Add(new SlashCommand { Name = name, Detail = entry["description"]?.JSONValueString ?? "" }); // :392
            }
            self.commandNames = names; // :394
            if (commands.Count != 0) self.OnEvent?.Invoke(new ProviderEvent.Commands(new ValueList<SlashCommand>(commands))); // :395
        });
    }

    // MARK: - Catalog and account

    /// <summary>`models.list`: every model the account may use, with its efforts and premium-request multiplier.</summary>
    // :401-425
    public static async Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var connection = await Connect(executable, TemporaryDirectory, environment, _ => { }); // :403
        try
        {
            var result = await connection.Request("models.list", EmptyObject); // :405
            var models = new List<ModelOption> { ProviderRegistry.CopilotAuto }; // :406
            foreach (var model in result["models"]?.JSONValueArray ?? []) // :407
            {
                if (model["id"]?.JSONValueString is not { } id || Is(model["policy"]?["state"]?.JSONValueString, "disabled")) continue; // :408
                var details = new List<string>(); // :409
                if (model["billing"]?["multiplier"]?.JSONValueDouble is { } multiplier) // :410-412
                {
                    details.Add(multiplier == SwiftNumber.Rounded(multiplier)
                        ? SwiftNumber.TruncatingToInt(multiplier).ToString(System.Globalization.CultureInfo.InvariantCulture) + Times + " premium requests"
                        : SwiftNumber.Description(multiplier) + Times + " premium requests");
                }
                if (Is(model["policy"]?["state"]?.JSONValueString, "unconfigured")) // :413-415
                {
                    details.Add("Enable it in your GitHub Copilot settings first");
                }
                models.Add(new ModelOption( // :416-422
                    id,
                    model["name"]?.JSONValueString ?? id,
                    detail: details.Count == 0 ? null : string.Join(" " + Dot + " ", details),
                    efforts: new ValueList<string>((model["supportedReasoningEfforts"]?.JSONValueArray ?? []).Select(effort => effort.JSONValueString).OfType<string>()),
                    defaultEffort: model["defaultReasoningEffort"]?.JSONValueString));
            }
            return new ValueList<ModelOption>(models); // :424
        }
        finally
        {
            connection.Close(); // :404
        }
    }

    /// <summary>`account.getCurrentAuth`: the GitHub login behind the CLI, from `copilot login` or `gh auth`.</summary>
    // :427-441
    public static async Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        if (await SwiftTry.OptionalAsync<JSONRPCConnection>(async () => await Connect(executable, TemporaryDirectory, environment, _ => { })) is not { } connection) // :429-431
        {
            return new ProviderStatus.Auth.Unknown();
        }
        try
        {
            if (await SwiftTry.OptionalAsync<JSONValue>(async () => await connection.Request("account.getCurrentAuth", EmptyObject)) is not { } result) return new ProviderStatus.Auth.Unknown(); // :433
            if (result["authInfo"] is not { } auth || auth.IsNull) return new ProviderStatus.Auth.SignedOut(); // :434
            var account = auth["login"]?.JSONValueString; // :435
            // Signed in to GitHub without a Copilot plan: the turn would fail with HTTP 403. :436-439
            if (Is(auth["copilotUser"]?["access_type_sku"]?.JSONValueString, "no_access"))
            {
                account = (account ?? "GitHub") + " " + Dot + " no Copilot access";
            }
            return new ProviderStatus.Auth.SignedIn(account); // :440
        }
        finally
        {
            connection.Close(); // :432
        }
    }

    /// <summary>`account.getQuota`: the monthly premium-request, chat and completion allowances.</summary>
    // :443-468
    public static async Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var connection = await Connect(executable, TemporaryDirectory, environment, _ => { }); // :445
        try
        {
            var quota = await connection.Request("account.getQuota", EmptyObject); // :447
            (string Key, string Title)[] titles = [("premium_interactions", "Premium requests"), ("chat", "Chat messages"), ("completions", "Code completions")]; // :448
            var windows = new List<PlanLimits.Window>(); // :449
            foreach (var (key, title) in titles) // :450
            {
                if (quota["quotaSnapshots"]?[key] is not { } snapshot || snapshot.IsNull // :451-453
                    || snapshot["isUnlimitedEntitlement"]?.JSONValueBool == true
                    || snapshot["entitlementRequests"]?.JSONValueDouble is not { } entitlement || !(entitlement > 0))
                {
                    continue;
                }
                var remaining = snapshot["remainingPercentage"]?.JSONValueDouble; // :454
                var used = snapshot["usedRequests"]?.JSONValueDouble ?? 0; // :455
                // UPSTREAM DEFECT 3, reproduced: the used branch has no lower bound, so a negative usedRequests gives a
                // negative percent. :456
                var percent = remaining is { } left ? Max(0, Min(100, 100 - left)) : Min(100, used / entitlement * 100);
                windows.Add(new PlanLimits.Window // :457-462
                {
                    Id = key,
                    Title = title,
                    Percent = percent,
                    ResetsAt = snapshot["resetDate"]?.JSONValueString is { } reset ? ParseDate(reset) : null,
                });
            }
            if (windows.Count == 0) return null; // :464
            var auth = await SwiftTry.OptionalAsync<JSONValue>(async () => await connection.Request("account.getCurrentAuth", EmptyObject)); // :465
            var plan = auth?["authInfo"]?["copilotUser"]?["copilot_plan"]?.JSONValueString; // :466
            return new PlanLimits // :467
            {
                PlanName = PlanLimitsReader.PlanName(plan) is { } name ? "Copilot " + name : null,
                Windows = new ValueList<PlanLimits.Window>(windows),
            };
        }
        finally
        {
            connection.Close(); // :446
        }
    }

    // :470-472
    private static DateTimeOffset? ParseDate(string text) => ISO8601.Date(text) ?? ISO8601.DateOnly(text);

    // :474-500
    private static async Task<JSONRPCConnection> Connect(
        SwiftURL executable,
        string directory,
        IReadOnlyDictionary<string, string> environment,
        Action<JSONRPCConnection> configure)
    {
        var arguments = LaunchArguments();
        var childEnvironment = ChildEnvironment(environment); // DL-6; the sole environment change.
        var process = new StdioProcess( // :484-490
            executable.Path,
            arguments,
            directory,
            childEnvironment,
            StdioFramer.Framing.ContentLength);
        var connection = new JSONRPCConnection(process, sendsVersion: true); // :491
        configure(connection); // :492
        connection.Start(); // :493
        // DEVIATION OD-0012 F1: upstream pings with the message "droppy-code" (:494).
        var pong = await connection.Request("ping", JSONValue.FromDictionaryLiteral(("message", "marrow"))); // :494
        if (pong["protocolVersion"]?.JSONValueInt is null) // :495-498
        {
            connection.Close();
            throw new ProviderError(new ProviderError.Case.Failed("This `copilot` does not speak the SDK protocol. Update it with `copilot update`."));
        }
        return connection; // :499
    }

    // PORT-ONLY: DL-6 child-only PATH insertion; shared by all Copilot connections (sessions and probes).
    private static Dictionary<string, string> ChildEnvironment(IReadOnlyDictionary<string, string> environment) =>
        ShimPath.ForChild(environment, SkillsProviderDataRoot.Resolve(Storage.Locations.Current, Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")));

    // :480-483, extracted unchanged for the DL-11 byte-identity test.
    private static List<string> LaunchArguments()
    {
        var arguments = new List<string> { "--headless", "--stdio", "--no-auto-update", "--log-level", "error" }; // :480
        // Connected servers from Settings are the only MCP servers the session sees; the user's own are switched
        // off for this session by name, their file untouched. :481-483
        arguments.AddRange(MCPSessionOverrides.CopilotArguments());
        return arguments;
    }

    // MARK: - Notifications

    // :504-640
    private void HandleNotification(string method, JSONValue parameters)
    {
        if (!SwiftString.Equal(method, "session.event") || parameters["event"] is not { } @event || @event["type"]?.JSONValueString is not { } type) return; // :505
        if (parameters["sessionId"]?.JSONValueString is { } eventSession && sessionID is { } sid && !SwiftString.Equal(eventSession, sid)) return; // :506
        // Sub-agents stream under their own agent id. With Hydra on they are heads and their transcripts go to the
        // head's own timeline; otherwise their parent `task` row stands for them. Their permission prompts still need
        // answering and their spend still counts, so those pass either way. :507-510
        var agentID = @event["agentId"]?.JSONValueString; // :511
        if (agentID is not null && configuration.Hydra is null && !SwiftString.Equal(type, "assistant.usage") // :512
            && (SwiftString.HasPrefix(type, "assistant.") || SwiftString.HasPrefix(type, "tool.")))
        {
            return;
        }
        Action<ProviderEvent> sink = agentID is { } head // :513-517
            ? providerEvent => OnEvent?.Invoke(new ProviderEvent.AgentEvent(head, providerEvent))
            : LeadSink;
        var data = @event["data"] ?? new JSONValue.Null(); // :518
        switch (type) // :519
        {
            case "assistant.turn_start": // :520-526
                if (agentID is not null)
                {
                    sink(new ProviderEvent.TurnStarted(data["turnId"]?.JSONValueString));
                }
                else if (!turnActive)
                {
                    turnActive = true;
                    OnEvent?.Invoke(new ProviderEvent.TurnStarted(data["turnId"]?.JSONValueString));
                }
                break;
            case "assistant.message_delta": // :527-531
                if (data["messageId"]?.JSONValueString is { } messageID && data["deltaContent"]?.JSONValueString is { } messageDelta)
                {
                    streamedMessages.Add(messageID);
                    sink(new ProviderEvent.MessageDelta(messageID, messageDelta));
                }
                break;
            case "assistant.message": // :532-533
                HandleMessage(data, sink);
                break;
            case "assistant.reasoning_delta": // :534-537
                if (data["reasoningId"]?.JSONValueString is { } deltaID && data["deltaContent"]?.JSONValueString is { } reasoningDelta)
                {
                    sink(new ProviderEvent.ReasoningDelta(deltaID, reasoningDelta));
                }
                break;
            case "assistant.reasoning": // :538-543
                if (data["reasoningId"]?.JSONValueString is { } reasoningID)
                {
                    var text = data["content"]?.JSONValueString ?? "";
                    shownReasoning.Add(ReasoningKey(text));
                    sink(new ProviderEvent.ReasoningCompleted(reasoningID, text));
                }
                break;
            case "assistant.usage": // :544-560
            {
                var spend = checked((data["inputTokens"]?.JSONValueInt ?? 0) + (data["outputTokens"]?.JSONValueInt ?? 0));
                if (spend > 0)
                {
                    TokenLedger.Shared.Current.Record(spend);
                    // One report per message, so it is already the amount that message consumed. A head's report
                    // carries its id: it is the account's spend, and the head's alone, not the lead's. :548-550
                    OnEvent?.Invoke(new ProviderEvent.TokenSpend(new TokenSpend(
                        ProviderKind.Copilot,
                        spend,
                        inputTokens: data["inputTokens"]?.JSONValueInt,
                        outputTokens: data["outputTokens"]?.JSONValueInt,
                        cachedInputTokens: data["cacheReadTokens"]?.JSONValueInt,
                        model: data["model"]?.JSONValueString ?? model,
                        agentID: agentID)));
                }
                break;
            }
            case "session.usage_info": // :561-564
                if (data["currentTokens"]?.JSONValueInt is { } used)
                {
                    OnEvent?.Invoke(new ProviderEvent.Usage(new ContextUsage { UsedTokens = used, WindowTokens = data["tokenLimit"]?.JSONValueInt }));
                }
                break;
            case "tool.execution_start": // :565-568
                if (data["toolCallId"]?.JSONValueString is { } startID && data["toolName"]?.JSONValueString is { } toolName)
                {
                    ToolRequested(startID, toolName, data["arguments"] ?? new JSONValue.Null(), data["mcpServerName"]?.JSONValueString, data["mcpToolName"]?.JSONValueString, sink);
                }
                break;
            case "tool.execution_partial_result": // :569-572
                if (data["toolCallId"]?.JSONValueString is { } outputID && data["partialOutput"]?.JSONValueString is { } output)
                {
                    sink(new ProviderEvent.ToolOutput(outputID, output));
                }
                break;
            case "tool.execution_progress": // :573-576
                if (data["toolCallId"]?.JSONValueString is { } progressID && data["progressMessage"]?.JSONValueString is { } message)
                {
                    sink(new ProviderEvent.ToolUpdated(progressID, new ToolUpdate { Detail = message }));
                }
                break;
            case "tool.execution_complete": // :577-578
                HandleToolResult(data, sink);
                break;
            case "subagent.started": // :579-591
            {
                if (agentID is not { } startedID || configuration.Hydra is null) break;
                (string Description, string? Prompt)? brief = data["toolCallId"]?.JSONValueString is { } callID && taskBriefs.Remove(callID, out var found) ? found : null;
                OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn
                {
                    Id = startedID,
                    TaskID = null,
                    ToolUseID = data["toolCallId"]?.JSONValueString,
                    Description = brief?.Description ?? data["agentDisplayName"]?.JSONValueString ?? data["agentName"]?.JSONValueString ?? "Subagent",
                    Prompt = brief?.Prompt,
                    Model = data["model"]?.JSONValueString,
                    Profile = HydraPrompts.ProfileName(data["agentName"]?.JSONValueString),
                }));
                break;
            }
            case "subagent.completed": // :592-594
                if (agentID is not { } completedID || configuration.Hydra is null) break;
                OnEvent?.Invoke(new ProviderEvent.AgentFinished(completedID, data["cancelled"]?.JSONValueBool == true ? TurnStatus.Interrupted : TurnStatus.Completed, null));
                break;
            case "subagent.failed": // :595-597
                if (agentID is not { } failedID || configuration.Hydra is null) break;
                OnEvent?.Invoke(new ProviderEvent.AgentFinished(failedID, TurnStatus.Failed, data["error"]?.JSONValueString));
                break;
            case "permission.requested": // :598-599
                HandlePermission(data);
                break;
            case "permission.completed": // :600-603
                if (data["requestId"]?.JSONValueString is { } requestID && pendingRequests.Remove(requestID))
                {
                    OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID));
                }
                break;
            case "session.mode_changed": // :604-613
                switch (data["newMode"]?.JSONValueString)
                {
                    case "plan":
                        interactionMode = InteractionMode.Plan;
                        OnEvent?.Invoke(new ProviderEvent.ModeChanged(InteractionMode.Plan));
                        break;
                    case "interactive":
                    case "autopilot":
                        interactionMode = InteractionMode.Build;
                        OnEvent?.Invoke(new ProviderEvent.ModeChanged(InteractionMode.Build));
                        break;
                    default:
                        break;
                }
                break;
            case "session.title_changed": // :614-615
                if (data["title"]?.JSONValueString is { } title && title.Length != 0) OnEvent?.Invoke(new ProviderEvent.Title(title));
                break;
            case "session.error": // :616-622
            {
                var errorMessage = ErrorMessage(data);
                if (turnActive)
                {
                    turnError = errorMessage;
                }
                else
                {
                    OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = errorMessage }));
                }
                break;
            }
            case "session.warning": // :623-624
                if (data["message"]?.JSONValueString is { } warning) OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = warning }));
                break;
            case "session.compaction_complete": // :625-630
                if (data["success"]?.JSONValueBool == true)
                {
                    OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." }));
                }
                else if (data["error"]?.JSONValueString is { } compactionError)
                {
                    OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = compactionError }));
                }
                break;
            case "session.idle": // :631-632
                FinishTurn(aborted: data["aborted"]?.JSONValueBool == true);
                break;
            case "session.shutdown": // :633-636
                if (data["errorReason"]?.JSONValueString is { } reason && Is(data["shutdownType"]?.JSONValueString, "error"))
                {
                    OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = reason }));
                }
                break;
            default: // :637-638
                break;
        }
    }

    // :642-658
    private void HandleMessage(JSONValue data, Action<ProviderEvent> sink)
    {
        if (data["messageId"]?.JSONValueString is not { } id) return; // :643
        // Models that think without reasoning events carry the text on the message instead. :644-648
        if (data["reasoningText"]?.JSONValueString is { } reasoning && SwiftString.Characters(reasoning).Any(character => !SwiftCharacter.IsWhitespace(character))
            && shownReasoning.Add(ReasoningKey(reasoning)))
        {
            sink(new ProviderEvent.ReasoningCompleted(id + "-reasoning", reasoning));
        }
        var content = data["content"]?.JSONValueString ?? ""; // :649
        if (content.Length != 0 || streamedMessages.Contains(id)) // :650-652
        {
            sink(new ProviderEvent.MessageCompleted(id, content));
        }
        // The calls this message asks for, so a permission prompt has its row to point at. :653-657
        foreach (var request in data["toolRequests"]?.JSONValueArray ?? [])
        {
            if (request["toolCallId"]?.JSONValueString is not { } callID || request["name"]?.JSONValueString is not { } name) continue;
            ToolRequested(callID, name, request["arguments"] ?? new JSONValue.Null(), request["mcpServerName"]?.JSONValueString, request["mcpToolName"]?.JSONValueString, sink);
        }
    }

    // :660-678
    private void ToolRequested(string id, string name, JSONValue arguments, string? server, string? mcpTool, Action<ProviderEvent> sink)
    {
        if (toolNames.ContainsKey(id)) return; // :661
        toolNames[id] = name; // :662
        if (skillEvents.Started(id, name, arguments, server) is { } used) EmitSkillEvent(used);
        if (SwiftString.Equal(name, "update_todo")) // :663-666
        {
            sink(new ProviderEvent.Todos(Todos(arguments["todos"]?.JSONValueString ?? "")));
            return;
        }
        if (SwiftString.Equal(name, "task")) // :667-672
        {
            taskBriefs[id] = (
                arguments["description"]?.JSONValueString ?? arguments["name"]?.JSONValueString ?? "Subagent",
                arguments["prompt"]?.JSONValueString ?? arguments["task"]?.JSONValueString);
        }
        if (ToolCall(name, arguments, server, mcpTool) is not { } call) return; // :673
        sink(new ProviderEvent.ToolStarted(id, call)); // :674
        if (call.Edits.Count != 0) // :675-677
        {
            sink(new ProviderEvent.ToolUpdated(id, new ToolUpdate { Edits = call.Edits }));
        }
    }

    // :680-693
    private void HandleToolResult(JSONValue data, Action<ProviderEvent> sink)
    {
        if (data["toolCallId"]?.JSONValueString is not { } id) return; // :681
        if (skillEvents.Finished(id, data, declinedTools.Contains(id)) is { } finished) EmitSkillEvent(finished);
        var name = toolNames.TryGetValue(id, out var recorded) ? recorded : ""; // :682
        if (HiddenTools.Contains(name)) return; // :683
        var succeeded = data["success"]?.JSONValueBool ?? true; // :684
        string? output = null; // :685
        if (data["error"]?["message"]?.JSONValueString is { } error && !succeeded) // :686-690
        {
            output = error;
        }
        else if (data["result"] is { } result)
        {
            output = result["detailedContent"]?.JSONValueString ?? result["content"]?.JSONValueString;
        }
        var status = succeeded ? global::Marrow.Core.Models.ToolCall.Status.Completed : (declinedTools.Contains(id) ? global::Marrow.Core.Models.ToolCall.Status.Declined : global::Marrow.Core.Models.ToolCall.Status.Failed); // :691
        sink(new ProviderEvent.ToolUpdated(id, new ToolUpdate { Output = output, Status = status })); // :692
    }

    // :695-715
    private void FinishTurn(bool aborted)
    {
        if (!turnActive) return; // :696
        turnActive = false; // :697
        // Every call of the turn has had its result; the names, declines, briefs and shown reasoning were for those.
        // :698-703
        skillEvents.Clear();
        toolNames.Clear();
        declinedTools.Clear();
        taskBriefs.Clear();
        shownReasoning.Clear();
        CancelPendingRequests(); // :704
        var error = turnError; // :705
        turnError = null; // :706
        if (aborted || interruptRequested) // :707-713
        {
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
        }
        else if (error is not null)
        {
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, error));
        }
        else
        {
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        }
        interruptRequested = false; // :714
    }

    /// <summary>The CLI's message, with the way out when the account itself is the problem.</summary>
    // :717-732
    public static string ErrorMessage(JSONValue data)
    {
        var message = data["message"]?.JSONValueString ?? "Copilot reported an error."; // :719
        var marker = message.IndexOf("GenericFailure, ", StringComparison.Ordinal); // :720. An ASCII literal: ordinal finds what Foundation finds.
        if (marker >= 0)
        {
            // The model-list failure wraps the HTTP reply as JSON after this marker. :721-722
            var json = JSONValue.Parse(message[(marker + "GenericFailure, ".Length)..]);
            if (json?["status"]?.JSONValueInt is { } status && json["body"]?.JSONValueString is { } body) // :723-725
            {
                message = "HTTP " + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + body.Trimmed();
            }
        }
        if (data["statusCode"]?.JSONValueInt == 403 || Is(data["remediation"]?.JSONValueString, "sign_in") // :727-730
            || SwiftString.LocalizedCaseInsensitiveContains(message, "not authorized to use this Copilot feature"))
        {
            message += " Check that your GitHub account has Copilot access, or sign in again with `copilot login`.";
        }
        return message; // :731
    }

    // MARK: - Requests

    // :735-774
    private void HandleRequest(RPCID id, string method, JSONValue parameters)
    {
        switch (method) // :736
        {
            case "userInput.request": // :737-752
            {
                var choices = new ValueList<string>((parameters["choices"]?.JSONValueArray ?? []).Select(choice => choice.JSONValueString).OfType<string>());
                var key = "question-" + id.Key;
                pendingRequests[key] = new PendingRequest.Question(id, choices);
                OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(key, new ValueList<QuestionRequest.Question>([
                    new QuestionRequest.Question(
                        "answer",
                        "",
                        parameters["question"]?.JSONValueString ?? "",
                        new ValueList<QuestionRequest.Choice>(choices.Select(choice => new QuestionRequest.Choice(choice, null))),
                        AllowsMultiple: false,
                        AllowsOther: parameters["allowFreeform"]?.JSONValueBool ?? true,
                        IsSecret: false),
                ]))));
                break;
            }
            case "exitPlanMode.request": // :753-767
            {
                var key = "plan-" + id.Key;
                pendingRequests[key] = new PendingRequest.PlanExit(id);
                var plan = (parameters["planContent"]?.JSONValueString is { Length: > 0 } content ? content : null) ?? parameters["summary"]?.JSONValueString ?? "";
                OnEvent?.Invoke(new ProviderEvent.PlanCompleted(key, plan));
                OnEvent?.Invoke(new ProviderEvent.Approval(new ApprovalRequest(
                    key,
                    ApprovalRequest.Kind.Plan,
                    "Implement this plan",
                    new ValueList<ApprovalRequest.Option>([
                        new ApprovalRequest.Option("allow", "Implement plan", ApprovalRequest.Option.Role.Approve),
                        new ApprovalRequest.Option("deny", "Keep planning", ApprovalRequest.Option.Role.Decline),
                    ]),
                    ToolItemID: key)));
                break;
            }
            case "autoModeSwitch.request": // :768-770
                // A rate limit offered a switch to the Auto model tier: keep the model the user chose.
                connection?.Respond(id, JSONValue.FromDictionaryLiteral(("response", "no")));
                break;
            default: // :771-772
                // DEVIATION OD-0012 F1: upstream says "Droppy Code does not support <method>."
                connection?.Respond(id, errorCode: -32601, message: "Marrow does not support " + method + ".");
                break;
        }
    }

    // :776-804
    private void HandlePermission(JSONValue data)
    {
        if (data["requestId"]?.JSONValueString is not { } requestID || data["permissionRequest"] is not { } request) return; // :777
        if (data["resolvedByHook"]?.JSONValueBool == true) return; // :778
        var toolCallID = request["toolCallId"]?.JSONValueString; // :779
        if (AutomaticDecision(request, data["promptRequest"]) is { } decision) // :780-783
        {
            RespondPermission(requestID, decision);
            return;
        }
        pendingRequests[requestID] = new PendingRequest.Permission(request, toolCallID); // :784
        // A write prompt carries the CLI's own patch, which beats the row's reconstruction. :785-790
        if (Is(request["kind"]?.JSONValueString, "write") && toolCallID is { } callID && request["diff"]?.JSONValueString is { Length: > 0 } diff)
        {
            var path = ToolTitles.RelativePath(request["resolvedPath"]?.JSONValueString ?? request["fileName"]?.JSONValueString ?? "", WorkingDirectory);
            var stats = ToolTitles.DiffStats(diff);
            OnEvent?.Invoke(new ProviderEvent.ToolUpdated(callID, new ToolUpdate { Edits = new ValueList<FileEdit>([new FileEdit(path, diff, stats.Additions, stats.Deletions)]) }));
        }
        var options = new List<ApprovalRequest.Option> { new("allow", "Approve", ApprovalRequest.Option.Role.Approve) }; // :791
        if (SessionApproval(request) is not null) // :792-794
        {
            options.Add(new ApprovalRequest.Option("always", "Approve for session", ApprovalRequest.Option.Role.ApproveAlways));
        }
        options.Add(new ApprovalRequest.Option("deny", "Decline", ApprovalRequest.Option.Role.Decline)); // :795
        var approval = DescribePermission(request) with // :796-799
        {
            Id = requestID,
            Options = new ValueList<ApprovalRequest.Option>(options),
            ToolItemID = toolCallID,
        };
        if (approval.Reason is null && data["promptRequest"]?["assistedApproval"]?["reason"]?.JSONValueString is { Length: > 0 } judged) // :800-802
        {
            approval = approval with { Reason = judged };
        }
        OnEvent?.Invoke(new ProviderEvent.Approval(approval)); // :803
        if (skillEvents.Held(request) is { } held) EmitSkillEvent(held);
    }

    /// <summary>What the thread's mode approves without asking. Full access approves it all (the CLI normally does
    /// that itself in `allow-all`), accept-edits waves file reads and writes through, and Auto adds whatever the
    /// CLI's judge vouched for.</summary>
    // :806-824
    private JSONValue? AutomaticDecision(JSONValue request, JSONValue? prompt)
    {
        var kind = request["kind"]?.JSONValueString ?? ""; // :810
        var approve = JSONValue.FromDictionaryLiteral(("kind", "approve-once")); // :811
        var fileAccess = SwiftString.Equal(kind, "write") || SwiftString.Equal(kind, "read");
        switch (runtimeMode) // :812
        {
            case RuntimeMode.FullAccess: // :813-814
                return approve;
            case RuntimeMode.AutoAcceptEdits: // :815-816
                return fileAccess ? approve : null;
            case RuntimeMode.Auto: // :817-820
            {
                if (fileAccess) return approve;
                var recommendation = prompt?["assistedApproval"]?["recommendation"]?.JSONValueString ?? request["assistedApproval"]?["recommendation"]?.JSONValueString;
                return Is(recommendation, "approve") ? approve : null;
            }
            case RuntimeMode.Supervised: // :821-822
                return null;
            default:
                throw new System.Diagnostics.UnreachableException();
        }
    }

    // :826-871
    private ApprovalRequest DescribePermission(JSONValue request)
    {
        var intention = request["intention"]?.JSONValueString is { Length: > 0 } stated ? stated : null; // :827
        switch (request["kind"]?.JSONValueString) // :828
        {
            case "shell": // :829-838
            {
                var command = ToolTitles.UnwrapShell(request["fullCommandText"]?.JSONValueString ?? "Command");
                var details = new List<string>();
                // windows_path (PR-PROVCLI-191): a plain string compare (Swift's `directory != workingDirectory`)
                // would tolerate neither the case nor the separator differences between the resolved directory the
                // CLI reports and our own WorkingDirectory, so the detail would always appear; SamePath (ED-137/141)
                // normalizes both before comparing.
                if (request["resolvedWorkingDirectory"]?.JSONValueString is { } directory && !SwiftPath.SamePath(directory, WorkingDirectory))
                {
                    details.Add(ToolTitles.RelativePath(directory, WorkingDirectory));
                }
                if (request["requestSandboxBypass"]?.JSONValueBool == true)
                {
                    details.Add(request["requestSandboxBypassReason"]?.JSONValueString ?? "Runs outside the sandbox.");
                }
                return new ApprovalRequest("", ApprovalRequest.Kind.Command, command, ValueList<ApprovalRequest.Option>.Empty,
                    Detail: details.Count == 0 ? null : string.Join(" " + Dot + " ", details), Reason: intention);
            }
            case "write": // :839-841
            {
                var path = request["resolvedPath"]?.JSONValueString ?? request["fileName"]?.JSONValueString ?? "file";
                return new ApprovalRequest("", ApprovalRequest.Kind.FileChange, ToolTitles.RelativePath(path, WorkingDirectory), ValueList<ApprovalRequest.Option>.Empty, Reason: intention);
            }
            case "read": // :842-844
            {
                var path = request["resolvedPath"]?.JSONValueString ?? request["path"]?.JSONValueString ?? "file";
                return new ApprovalRequest("", ApprovalRequest.Kind.Permissions, "Read " + ToolTitles.RelativePath(path, WorkingDirectory), ValueList<ApprovalRequest.Option>.Empty, Reason: intention);
            }
            case "url": // :845-848
            {
                var url = request["url"]?.JSONValueString ?? "a URL";
                var detail = request["redirectedFrom"]?.JSONValueString is { } origin ? "Redirected from " + origin : null;
                return new ApprovalRequest("", ApprovalRequest.Kind.Permissions, "Fetch " + url, ValueList<ApprovalRequest.Option>.Empty, Detail: detail, Reason: intention);
            }
            case "mcp": // :849-853
            {
                var server = request["serverName"]?.JSONValueString ?? "MCP";
                var tool = request["toolTitle"]?.JSONValueString ?? request["toolName"]?.JSONValueString ?? "tool";
                var arguments = request["args"]?.CompactString;
                return new ApprovalRequest("", ApprovalRequest.Kind.Tool, server + " " + Dot + " " + tool, ValueList<ApprovalRequest.Option>.Empty,
                    Detail: arguments is { } shown && SwiftString.Equal(shown, "null") ? null : arguments);
            }
            case "memory": // :854-856
            {
                var fact = request["fact"]?.JSONValueString ?? "something about this project";
                return new ApprovalRequest("", ApprovalRequest.Kind.Tool, "Remember: " + fact, ValueList<ApprovalRequest.Option>.Empty, Reason: request["reason"]?.JSONValueString);
            }
            case "custom-tool": // :857-859
            {
                var tool = request["toolName"]?.JSONValueString ?? "Tool";
                return new ApprovalRequest("", ApprovalRequest.Kind.Tool, tool, ValueList<ApprovalRequest.Option>.Empty, Detail: request["toolDescription"]?.JSONValueString);
            }
            case "hook": // :860-862
            {
                var tool = request["toolName"]?.JSONValueString ?? "Tool";
                return new ApprovalRequest("", ApprovalRequest.Kind.Tool, request["hookMessage"]?.JSONValueString ?? tool, ValueList<ApprovalRequest.Option>.Empty,
                    Detail: request["hookMessage"] is null ? null : tool);
            }
            case "extension-management": // :863-866
            {
                var operation = request["operation"]?.JSONValueString ?? "manage";
                var extensionName = request["extensionName"]?.JSONValueString ?? "an extension";
                return new ApprovalRequest("", ApprovalRequest.Kind.Tool, Capitalized(operation) + " " + extensionName, ValueList<ApprovalRequest.Option>.Empty);
            }
            default: // :867-869
            {
                var kind = request["kind"]?.JSONValueString ?? "action";
                return new ApprovalRequest("", ApprovalRequest.Kind.Tool, Humanize(kind), ValueList<ApprovalRequest.Option>.Empty, Reason: intention);
            }
        }
    }

    /// <summary>The approve-for-session decision for a request, or nil when the CLI cannot remember this kind.</summary>
    // :873-902. Swift: sessionApproval(for:).
    public static JSONValue? SessionApproval(JSONValue request)
    {
        JSONValue approval; // :875
        switch (request["kind"]?.JSONValueString) // :876
        {
            case "shell": // :877-881
            {
                if (request["canOfferSessionApproval"]?.JSONValueBool != true) return null;
                var identifiers = (request["commands"]?.JSONValueArray ?? []).Select(command => command["identifier"]?.JSONValueString).OfType<string>().ToList();
                if (identifiers.Count == 0) return null;
                approval = JSONValue.FromDictionaryLiteral(("kind", "commands"), ("commandIdentifiers", new JSONValue.Array(new ValueList<JSONValue>(identifiers.Select(identifier => (JSONValue)identifier)))));
                break;
            }
            case "write": // :882-884
                if (request["canOfferSessionApproval"]?.JSONValueBool != true) return null;
                approval = JSONValue.FromDictionaryLiteral(("kind", "write"));
                break;
            case "read": // :885-886
                approval = JSONValue.FromDictionaryLiteral(("kind", "read"));
                break;
            case "url": // :887-889
                if (request["url"]?.JSONValueString is not { } url || SwiftURL.Create(url)?.Host is not { } host || host.Length == 0) return null;
                return JSONValue.FromDictionaryLiteral(("kind", "approve-for-session"), ("domain", host));
            case "mcp": // :890-892
                if (request["serverName"]?.JSONValueString is not { } server) return null;
                approval = JSONValue.FromDictionaryLiteral(("kind", "mcp"), ("serverName", server), ("toolName", JSONValue.Optional(request["toolName"]?.JSONValueString)));
                break;
            case "custom-tool": // :893-895
                if (request["toolName"]?.JSONValueString is not { } tool) return null;
                approval = JSONValue.FromDictionaryLiteral(("kind", "custom-tool"), ("toolName", tool));
                break;
            case "memory": // :896-897
                approval = JSONValue.FromDictionaryLiteral(("kind", "memory"));
                break;
            default: // :898-899
                return null;
        }
        return JSONValue.FromDictionaryLiteral(("kind", "approve-for-session"), ("approval", approval)); // :901
    }

    // :904-913
    private void RespondPermission(string requestID, JSONValue decision)
    {
        if (connection is not { } conn || sessionID is not { } sid) return; // :905
        MainActor.Spawn(async () => // :906-912
        {
            await SwiftTry.IgnoreAsync(async () => await conn.Request("session.permissions.handlePendingPermissionRequest", JSONValue.FromDictionaryLiteral(
                ("sessionId", sid),
                ("requestId", requestID),
                ("result", decision))));
        });
    }

    // :915-929
    private void CancelPendingRequests()
    {
        var waiting = pendingRequests.ToList(); // :916
        pendingRequests.Clear(); // :917
        foreach (var (key, pending) in waiting) // :918
        {
            switch (pending)
            {
                case PendingRequest.Permission: // :920-921
                    RespondPermission(key, JSONValue.FromDictionaryLiteral(("kind", "cancelled"), ("reason", "The user stopped the turn.")));
                    break;
                case PendingRequest.Question(var id, _): // :922-923
                    connection?.Respond(id, errorCode: -32800, message: "The user stopped the turn.");
                    break;
                case PendingRequest.PlanExit(var id): // :924-925
                    connection?.Respond(id, JSONValue.FromDictionaryLiteral(("approved", false), ("feedback", "The user stopped the turn.")));
                    break;
                default:
                    throw new System.Diagnostics.UnreachableException();
            }
            OnEvent?.Invoke(new ProviderEvent.RequestResolved(key)); // :927
        }
    }

    // :931-938
    private void HandleClose(string tail)
    {
        skillEvents.Clear();
        var resolved = pendingRequests.Keys.ToList(); // :932
        pendingRequests.Clear(); // :933
        foreach (var key in resolved) OnEvent?.Invoke(new ProviderEvent.RequestResolved(key)); // :934
        turnActive = false; // :935
        if (isStopping) return; // :936
        OnEvent?.Invoke(new ProviderEvent.Exited(TextCleanup.LastLines(tail))); // :937
    }

    // MARK: - Mapping

    /// <summary>Tools that never get a row: questions, plans and to-dos have their own surfaces.</summary>
    private static readonly HashSet<string> HiddenTools = new(
        ["ask_user", "exit_plan_mode", "update_todo", "task_complete", "report_intent"], SwiftStringComparer.Instance); // :942-943

    // :945-1027. Swift: toolCall(name:arguments:server:mcpTool:).
    private global::Marrow.Core.Models.ToolCall? ToolCall(string name, JSONValue arguments, string? server, string? mcpTool)
    {
        if (server is not null) // :946-949
        {
            var tool = mcpTool ?? name;
            return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Mcp, Title = server + " " + Dot + " " + tool, Detail = arguments.IsNull ? null : arguments.CompactString };
        }
        string? PathOf(string key) => arguments[key]?.JSONValueString is { } value ? ToolTitles.RelativePath(value, WorkingDirectory) : null; // :950-952
        if (HiddenTools.Contains(name)) return null; // :954-955
        switch (name) // :953
        {
            case "bash":
            case "powershell": // :956-957
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Command, arguments["command"]?.JSONValueString ?? "Shell command", arguments["description"]?.JSONValueString);
            case "read_bash":
            case "write_bash":
            case "stop_bash":
            case "list_bash": // :958-959
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Command, Humanize(name), arguments["shellId"]?.JSONValueString);
            case "view": // :960-961
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Read, PathOf("path") ?? "Read file", LineRange(arguments["view_range"]));
            case "create": // :962-967
                return Create();
            case "edit": // :968-973
                return Replace();
            case "str_replace_editor": // :974-996
                switch (arguments["command"]?.JSONValueString)
                {
                    case "view": // :976-977
                        return Call(global::Marrow.Core.Models.ToolCall.Kind.Read, PathOf("path") ?? "Read file", LineRange(arguments["view_range"]));
                    case "create": // :978-983
                        return Create();
                    case "insert": // :984-989
                    {
                        var call = Call(global::Marrow.Core.Models.ToolCall.Kind.Edit, PathOf("path") ?? "Edit file");
                        if (PathOf("path") is { } file) call = call with { Edits = new ValueList<FileEdit>([new FileEdit(file, "", arguments["new_str"]?.JSONValueString ?? "")]) };
                        return call;
                    }
                    default: // :990-995
                        return Replace();
                }
            case "apply_patch": // :997-1002
            {
                var patch = arguments.JSONValueString ?? arguments["input"]?.JSONValueString ?? arguments["patch"]?.JSONValueString ?? "";
                var paths = PatchPaths(patch).Select(path => ToolTitles.RelativePath(path, WorkingDirectory)).ToList();
                var call = Call(global::Marrow.Core.Models.ToolCall.Kind.Edit, paths.Count == 1 ? paths[0] : "Edit " + paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " files");
                return call with { Edits = new ValueList<FileEdit>(paths.Select(path => new FileEdit(path))) };
            }
            case "grep": // :1003-1005
            {
                var paths = RelativePaths();
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Search, arguments["pattern"]?.JSONValueString ?? "Search", paths.Count == 0 ? null : string.Join(", ", paths));
            }
            case "glob": // :1006-1008
            {
                var paths = RelativePaths();
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Search, arguments["pattern"]?.JSONValueString ?? "Find files", paths.Count == 0 ? null : string.Join(", ", paths));
            }
            case "web_fetch": // :1009-1010
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Web, arguments["url"]?.JSONValueString ?? "Fetch page");
            case "web_search": // :1011-1012
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Web, arguments["query"]?.JSONValueString ?? "Search the web");
            case "task": // :1013-1015
            {
                var detail = string.Join(" " + Dot + " ", new[] { arguments["name"]?.JSONValueString, arguments["agent_type"]?.JSONValueString }.OfType<string>());
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Agent, arguments["description"]?.JSONValueString ?? "Subagent", detail.Length == 0 ? null : detail);
            }
            case "read_agent":
            case "write_agent":
            case "list_agents": // :1016-1017
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Agent, Humanize(name), arguments["agent_id"]?.JSONValueString ?? arguments["message"]?.JSONValueString);
            case "skill": // :1018-1019
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Other, "Skill " + Dot + " " + (arguments["skill"]?.JSONValueString ?? ""));
            case "sql": // :1020-1021
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Other, arguments["description"]?.JSONValueString ?? "SQL query", arguments["query"]?.JSONValueString);
            case "create_pull_request": // :1022-1023
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Other, arguments["title"]?.JSONValueString ?? "Create pull request");
            default: // :1024-1025
                return Call(global::Marrow.Core.Models.ToolCall.Kind.Other, Humanize(name), arguments.IsNull ? null : TextCleanup.SingleLine(arguments.CompactString));
        }

        // :962-967 and :978-983: the create row, its single edit adding the whole file.
        global::Marrow.Core.Models.ToolCall Create()
        {
            var call = Call(global::Marrow.Core.Models.ToolCall.Kind.Edit, PathOf("path") ?? "Create file");
            if (PathOf("path") is { } file) call = call with { Edits = new ValueList<FileEdit>([new FileEdit(file, "", arguments["file_text"]?.JSONValueString ?? "")]) };
            return call;
        }

        // :968-973 and :990-995: the replace row, old_str to new_str.
        global::Marrow.Core.Models.ToolCall Replace()
        {
            var call = Call(global::Marrow.Core.Models.ToolCall.Kind.Edit, PathOf("path") ?? "Edit file");
            if (PathOf("path") is { } file) call = call with { Edits = new ValueList<FileEdit>([new FileEdit(file, arguments["old_str"]?.JSONValueString ?? "", arguments["new_str"]?.JSONValueString ?? "")]) };
            return call;
        }

        // :1004 and :1007
        List<string> RelativePaths() =>
            (arguments["paths"]?.JSONValueArray ?? []).Select(path => path.JSONValueString).OfType<string>().Select(path => ToolTitles.RelativePath(path, WorkingDirectory)).ToList();
    }

    // `ToolCall(kind:title:detail:)` (Timeline.swift:181-192): every other member at its default.
    private static global::Marrow.Core.Models.ToolCall Call(global::Marrow.Core.Models.ToolCall.Kind kind, string title, string? detail = null) =>
        new() { ToolCallKind = kind, Title = title, Detail = detail };

    // :1029-1032
    private static string? LineRange(JSONValue? value)
    {
        if (value?.JSONValueArray?.Select(element => element.JSONValueInt).Where(element => element is not null).Select(element => element!.Value).ToList() is not { Count: 2 } range) return null;
        var first = range[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
        return range[1] < 0 ? "from line " + first : "lines " + first + EnDash + range[1].ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The files an `apply_patch` envelope touches: its `*** Update|Add|Delete File:` headers.</summary>
    // :1034-1045
    public static ValueList<string> PatchPaths(string patch)
    {
        var paths = new List<string>(); // :1036
        var seen = new HashSet<string>(SwiftStringComparer.Instance); // :1037
        foreach (var line in SplitOnNewlines(patch)) // :1038
        {
            foreach (var prefix in (string[])["*** Update File: ", "*** Add File: ", "*** Delete File: "]) // :1039
            {
                if (!SwiftString.HasPrefix(line, prefix)) continue;
                var path = FoundationText.Trim(line[prefix.Length..]); // :1040. The prefixes are ASCII: one Character per unit.
                if (path.Length != 0 && seen.Add(path)) paths.Add(path); // :1041
            }
        }
        return new ValueList<string>(paths); // :1044
    }

    /// <summary>`update_todo` hands over a markdown checklist: `- [x]` done, `- [ ]` pending, anything else in the
    /// brackets in progress.</summary>
    // :1049. The part before the bracket content; the rest of `^(?:[-*+]|\d+[.)])\s*\[(.?)\]\s*(.+)$` is matched
    // Character by Character in TodoLine, because `.` in a Swift Regex is one Character (R-RE-2).
    [GeneratedRegex(@"\A(?:[-*+]|\d+[.)])\s*\[", RegexOptions.CultureInvariant)]
    private static partial Regex TodoLinePrefixPattern();

    // :1051-1062. Swift: todos(from:).
    public static ValueList<TodoStep> Todos(string markdown)
    {
        var steps = new List<TodoStep>();
        foreach (var line in SplitOnNewlines(markdown)) // :1052
        {
            var trimmed = FoundationText.Trim(line); // :1053
            if (TodoLine(trimmed) is not var (mark, text)) continue; // :1054
            var status = SwiftString.Lowercased(mark) switch // :1055-1059
            {
                "x" => TodoStep.Status.Done,
                " " or "" => TodoStep.Status.Pending,
                _ => TodoStep.Status.Active,
            };
            steps.Add(new TodoStep { Text = text, TodoStepStatus = status }); // :1060
        }
        return new ValueList<TodoStep>(steps);
    }

    // :1049 `firstMatch(of: todoLinePattern)`: the bracket content (`(.?)`, greedy, so one Character is tried before
    // none) and the text (`\s*(.+)$`: at least one Character, leading whitespace dropped greedily but never the last
    // Character). The lines carry no newline, so `.` and `$` see the whole rest of the line.
    private static (string Mark, string Text)? TodoLine(string line)
    {
        var prefix = TodoLinePrefixPattern().Match(line);
        if (!prefix.Success || !SwiftString.IsCharacterBoundary(line, prefix.Length)) return null;
        var rest = SwiftString.Characters(line[prefix.Length..]).ToList();
        if (rest.Count >= 2 && rest[1] == "]" && Text(rest.Skip(2).ToList()) is { } withMark) return (rest[0], withMark);
        if (rest.Count >= 1 && rest[0] == "]" && Text(rest.Skip(1).ToList()) is { } withoutMark) return ("", withoutMark);
        return null;

        static string? Text(List<string> characters)
        {
            if (characters.Count == 0) return null;
            var start = 0;
            while (start < characters.Count - 1 && SwiftCharacter.IsWhitespace(characters[start])) start++;
            return string.Concat(characters.Skip(start));
        }
    }

    // :1064-1066. DEVIATION ED-025: the trimmed text itself is the key, not its hashValue.
    private static string ReasoningKey(string text) => text.Trimmed();

    // :1068-1071
    private static string Humanize(string name)
    {
        var words = name.Replace("-", " ", StringComparison.Ordinal).Replace("_", " ", StringComparison.Ordinal);
        return SwiftString.Uppercased(SwiftString.Prefix(words, 1)) + SwiftString.DropFirst(words);
    }

    // Foundation `String.capitalized` (:866 `operation.capitalized`): the first character of each word upper-cased
    // and every other character lower-cased, a word being delimited by whitespace (NSString capitalizedString).
    // UNVERIFIED O-07: Foundation's own word breaking and case tables are not reproduced beyond this.
    private static string Capitalized(string text)
    {
        var result = new System.Text.StringBuilder();
        var atWordStart = true;
        foreach (var character in SwiftString.Characters(text))
        {
            if (SwiftCharacter.IsWhitespace(character))
            {
                result.Append(character);
                atWordStart = true;
                continue;
            }
            result.Append(atWordStart ? SwiftString.Uppercased(character) : SwiftString.Lowercased(character));
            atWordStart = false;
        }
        return result.ToString();
    }

    // `split(whereSeparator: \.isNewline)` (:1038, :1052): Character by Character, "\r\n" one separator, empty
    // pieces dropped (the Git.cs port of the same call).
    private static List<string> SplitOnNewlines(string text)
    {
        var pieces = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var character in SwiftString.Characters(text))
        {
            if (character == "\r\n" || (character.Length == 1 && FoundationText.IsNewline(character[0])))
            {
                if (current.Length != 0) pieces.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(character);
        }
        if (current.Length != 0) pieces.Add(current.ToString());
        return pieces;
    }

    // FileManager.default.temporaryDirectory (:403, :429, :445): the Windows user temporary directory, the same
    // mapping PlanLimits.cs and TextGeneration.cs give it.
    private static string TemporaryDirectory =>
        Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? System.IO.Path.GetTempPath();

    // `[:]` as request params (:405, :433, :447, :465).
    private static JSONValue EmptyObject => new JSONValue.Object(new ValueMap<string, JSONValue>([], SwiftStringComparer.Instance));

    // Swift `==` between an optional string and a literal (R-STR-2).
    private static bool Is(string? value, string literal) => value is not null && SwiftString.Equal(value, literal);

    // Swift `max(x, y)` and `min(x, y)` on Double (R-NUM-4).
    private static double Max(double x, double y) => y >= x ? y : x;
    private static double Min(double x, double y) => y < x ? y : x;

    private const string Dot = "\U000000B7"; // U+00B7 MIDDLE DOT
    private const string Times = "\U000000D7"; // U+00D7 MULTIPLICATION SIGN
    private const string EnDash = "\U00002013"; // U+2013 EN DASH

    private static JSONValue ObjectOf(Dictionary<string, JSONValue> members) =>
        new JSONValue.Object(new ValueMap<string, JSONValue>(members, SwiftStringComparer.Instance));
}
