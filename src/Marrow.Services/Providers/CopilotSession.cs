using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

// Source: DroppyCode/Services/Providers/CopilotSession.swift (B2 1fbf81e), :1-1072. Part 1 of 4: the lifecycle,
// the session parameters, modes and selection, send, slash commands, interrupt, compact and rewind (:27-397,
// :474-500, :695-715, :904-929). The notification, request, approval, catalog and mapping members (:270-324,
// :399-472, :502-693, :717-903, :931-1072) are still MARROW_STUB and are ported by parts 2 to 4.

namespace Marrow.Services.Providers;

// :27-1072. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed partial class CopilotSession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; } // :29

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
#pragma warning disable CS0169 // read and written by Stop and HandleClose: part 2 and part 3
    private bool isStopping; // :53
#pragma warning restore CS0169
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
    public CopilotSession(SessionConfiguration configuration)
    {
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
            configuration.WorkingDirectory,
            configuration.Environment,
            connection =>
            {
                connection.OnNotification = (method, parameters) => { if (weak.TryGetTarget(out var self)) self.HandleNotification(method, parameters); }; // :98
                connection.OnRequest = (id, method, parameters) => { if (weak.TryGetTarget(out var self)) self.HandleRequest(id, method, parameters); }; // :99
                connection.OnClose = tail => { if (weak.TryGetTarget(out var self)) self.HandleClose(tail); }; // :100
            });
        this.connection = connection; // :102

        var id = configuration.ResumeID ?? SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid())); // :104
        var parameters = SessionParameters(); // :105
        parameters["sessionId"] = id; // :106
        // Resume fails for an id the CLI no longer has; the thread then starts a fresh session. :107-108
        _ = await connection.Request(configuration.ResumeID is null ? "session.create" : "session.resume", ObjectOf(parameters));
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
        return parameters; // :157
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
    public void ResolveApproval(string requestID, string optionID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:270");

    // :305-310
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:305");

    // :312-324
    public void Stop() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:312");

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

    // :402-425
    public static Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:402");

    // :428-441
    public static Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:428");

    // :444-468
    public static Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:444");

    // :474-500
    private static async Task<JSONRPCConnection> Connect(
        SwiftURL executable,
        SwiftURL directory,
        IReadOnlyDictionary<string, string> environment,
        Action<JSONRPCConnection> configure)
    {
        var arguments = new List<string> { "--headless", "--stdio", "--no-auto-update", "--log-level", "error" }; // :480
        // Connected servers from Settings are the only MCP servers the session sees; the user's own are switched
        // off for this session by name, their file untouched. :481-483
        arguments.AddRange(MCPSessionOverrides.CopilotArguments());
        var process = new StdioProcess( // :484-490
            executable.Path,
            arguments,
            directory.Path,
            environment,
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

    // MARK: - Notifications

    // :504-640
    private void HandleNotification(string method, JSONValue parameters) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:504");

    // :695-715
    private void FinishTurn(bool aborted)
    {
        if (!turnActive) return; // :696
        turnActive = false; // :697
        // Every call of the turn has had its result; the names, declines, briefs and shown reasoning were for those.
        // :698-703
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

    // :718-732
    public static string ErrorMessage(JSONValue data) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:718");

    // MARK: - Requests

    // :736-774
    private void HandleRequest(RPCID id, string method, JSONValue parameters) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:736");

    // :874-902. Swift: sessionApproval(for:).
    public static JSONValue? SessionApproval(JSONValue request) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:874");

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
    private void HandleClose(string tail) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:931");

    // MARK: - Mapping

    // :1035-1045
    public static ValueList<string> PatchPaths(string patch) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:1035");

    // :1051-1062. Swift: todos(from:).
    public static ValueList<TodoStep> Todos(string markdown) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CopilotSession.swift:1051");

    private static JSONValue ObjectOf(Dictionary<string, JSONValue> members) =>
        new JSONValue.Object(new ValueMap<string, JSONValue>(members, SwiftStringComparer.Instance));
}
