using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/ClaudeSession.swift (B2 1fbf81e), :1-693.
//
// Unit Services.Providers.ClaudeSession part 1 of 4 (PR-PROVCLI-001 to 024) implemented the
// launch path: the executable guard, the session id, the fixed and conditional argument
// vector, the --help option probe, the Hydra argument blocks, the always-present
// --append-system-prompt, the resume/session-id tail, spawning the process and attaching its
// reader, the initialize handshake (and the control-request wire it rides: :299-310, :331-343
// so far as control_response resolves it), and the static command-discovery probe (:183-212).
//
// Part 2 (PR-PROVCLI-025 to 048) adds: Send (:214-236), Interrupt (:238-247), Compact
// (:249-252), ResolveApproval (:254-275) with sessionScoped (:838-842), AnswerQuestion
// (:277-287), Stop (:289-292), StopAgent (:294-297), writeUserMessage (:312-322),
// sendToolResponse (:324-329), the rest of Handle's dispatch switch (:331-385, control_request,
// control_cancel_request, system, stream_event, assistant, user, result, rate_limit_event) and
// handleSystem's init/compact_boundary/task_started/task_progress/task_notification cases
// (:388-425, handleTaskStarted :427-447).
//
// handleControlRequest's own body (:611-680ish: building an ApprovalRequest/QuestionRequest
// from a live can_use_tool request) and the four per-event-type transcript handlers
// (handleStreamEvent :457-497, handleAssistant :505-546, handleToolResults :548-562, handleResult
// :564-609) are later parts of this unit: Handle's switch (this part) routes to them by name, but
// their bodies are no-op placeholders here, never invoked by this part's own tests, so the type
// keeps compiling while those parts are pending. Because handleControlRequest does not populate
// pendingTools yet, this part's own tests seed it directly through SeedPendingTool (Swift
// `private`, kept internal for tests only, see the seam note on it below).
//
// DidClose (:680-696) is self-contained (process.ErrorTail, TextCleanup.LastLines,
// pendingControl, pendingTools, OnEvent, turnActive, isStopping, isStarted only) and was ported
// in part 1 because Control (:301-310, needed by PR-PROVCLI-019) and the readTask
// (PR-PROVCLI-017) both depend on it; PR-PROVCLI-061, which fully exercises it, belongs to a
// later part.
public sealed class ClaudeSession : ProviderSession
{
    // :15-19
    private sealed record PendingTool
    {
        public required string Name { get; init; }
        public required JSONValue Input { get; init; }
        public JSONValue? Suggestions { get; init; }
    }

    // :22-27. The open blocks of one streamed transcript: the lead's, or one head's. Its members
    // (currentMessageID, blockIDs, openText, openThinking) are added by the later part of this unit that
    // implements handleStreamEvent/handleAssistant; this part uses only the type's identity, to add and
    // remove a head's entry from agentStreams around handleStreamEvent's (still unwritten) body.
    private struct StreamState
    {
    }

    private readonly SessionConfiguration configuration; // :29
    private StdioProcess? process;                        // :30
    private TaskHandle? readTask;                          // :31
    private string? sessionID;                             // :32
    private string permissionMode;                         // :33
    private RuntimeMode runtimeMode;                        // :34
    private string? model;                                  // :35
    private long controlCounter;                            // :36
    private readonly Dictionary<string, CheckedContinuation<JSONValue>> pendingControl = new(SwiftStringComparer.Instance); // :37
    private readonly Dictionary<string, PendingTool> pendingTools = new(SwiftStringComparer.Instance); // :38
    // The lead's own stream state and each head's, by the tool call that spawned it. Their inner block
    // bookkeeping (currentMessageID, blockIDs, openText, openThinking, Swift :22-27) is added by the later
    // part that implements handleStreamEvent/handleAssistant; this part only needs the dictionary shape to
    // add and remove a head's entry.
    private StreamState mainStream; // :40
    private readonly Dictionary<string, StreamState> agentStreams = new(SwiftStringComparer.Instance); // :42
    // Heads by their task id, for progress and completion, and their task ids by head, for stopping. :44-45
    private readonly Dictionary<string, string> agentsByTask = new(SwiftStringComparer.Instance); // :44
    private readonly Dictionary<string, string> tasksByAgent = new(SwiftStringComparer.Instance); // :45
    private bool turnActive;                                // :46, read by Interrupt (:239) and written by writeUserMessage (:313) and DidClose
    // interruptRequested is read only by handleResult (:597), a later part of this unit.
#pragma warning disable CS0414
    private bool interruptRequested;                        // :47, written by Interrupt (:240) and writeUserMessage (:314)
#pragma warning restore CS0414
    private bool isStopping;                                // :48, written by Stop (:290); DidClose (ported in part 1) reads it.
    private bool isStarted;                                 // :51
    private bool hasClosed;                                 // :52
    private string? launchError;                            // :55
    private static readonly Dictionary<string, HashSet<string>> knownOptions = new(SwiftStringComparer.Instance); // :56

    public ClaudeSession(SessionConfiguration configuration) // :58-63
    {
        this.configuration = configuration;
        runtimeMode = configuration.RuntimeMode;
        permissionMode = ModeName(configuration.RuntimeMode, configuration.InteractionMode);
        model = configuration.Model;
    }

    public Action<ProviderEvent>? OnEvent { get; set; } // :13

    public bool IsRunning => process?.IsRunning ?? false; // :65

    private string WorkingDirectory => configuration.WorkingDirectory.Path; // :67

    /// <summary>PR-PROVCLI-004: plan is tested first, overriding the runtime mode. Swift `private`; kept
    /// internal for this part's own tests (see the file header note).</summary>
    internal static string ModeName(RuntimeMode runtime, InteractionMode interaction) // :844-852
    {
        if (interaction == InteractionMode.Plan) return "plan";
        return runtime switch
        {
            RuntimeMode.Supervised => "default",
            RuntimeMode.AutoAcceptEdits => "acceptEdits",
            RuntimeMode.Auto => "auto",
            RuntimeMode.FullAccess => "bypassPermissions",
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>PR-PROVCLI-009: cached per executable path; a failed probe is not cached. Swift `private`;
    /// kept internal for this part's own tests (see the file header note).</summary>
    internal static async Task<HashSet<string>> KnownOptions(SwiftURL executable, IReadOnlyDictionary<string, string> environment) // :72-80
    {
        if (knownOptions.TryGetValue(executable.Path, out var cached)) return cached;
        var result = await SwiftTry.OptionalAsync<ShellResult>(async () => await Shell.Run(executable.Path, ["--help"], environment: environment, timeout: 20));
        if (result is null) return [];
        var help = TextCleanup.StripANSI(result.Output + result.ErrorOutput);
        var found = new[] { "--system-prompt-snapshot", "--forward-subagent-text" }
            .Where(option => help.Contains(option, StringComparison.Ordinal));
        var options = new HashSet<string>(found, SwiftStringComparer.Instance);
        knownOptions[executable.Path] = options;
        return options;
    }

    public async Task<string> Start() // :82-167
    {
        if (configuration.Executable is not { } executable) throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider)); // :83
        var id = configuration.ResumeID ?? SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid())); // :84
        List<string> arguments = // :85-90
        [
            "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--include-partial-messages", "--permission-prompt-tool", "stdio",
            "--setting-sources", "user,project,local",
            "--allow-dangerously-skip-permissions", "--permission-mode", permissionMode,
        ];
        if (configuration.Model is { } configuredModel && !SwiftString.Equal(configuredModel, "default")) // :91
        {
            arguments.Add("--model");
            arguments.Add(configuredModel);
        }
        if (configuration.Effort is { } effort && effort.Length != 0) // :92
        {
            arguments.Add("--effort");
            arguments.Add(effort);
        }
        if (configuration.FastMode) // :93
        {
            arguments.Add("--settings");
            arguments.Add("{\"fastMode\":true}");
        }
        // Connected servers from Settings are the only MCP servers the session sees. :94-96
        if (MCPProviderConfig.ClaudeConfigPath() is { } mcpConfig)
        {
            arguments.Add("--mcp-config");
            arguments.Add(mcpConfig);
            arguments.Add("--strict-mcp-config");
        }
        var environment = configuration.Environment; // :97
        var options = await KnownOptions(executable, environment); // :98
        // Resumed chats must use the current setting, even with Hydra off. :99-100
        if (options.Contains("--system-prompt-snapshot"))
        {
            arguments.Add("--system-prompt-snapshot");
            arguments.Add("off");
        }
        string? hydraPolicy = null; // :101
        if (configuration.Hydra is { } hydra) // :102-120
        {
            if (hydra.RunsNatively) // :106-112
            {
                arguments.Add("--agents");
                arguments.Add(HydraPrompts.ClaudeAgents(hydra).CompactString);
                hydraPolicy = HydraPrompts.Policy(ProviderKind.Claude, hydra);
                if (options.Contains("--forward-subagent-text")) arguments.Add("--forward-subagent-text");
                // Uncapped, the CLI keeps its own limit on heads at once. :111-112
                if (hydra.MaxHeads is { } cap)
                    environment = environment.Mutating(map => map["CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS"] = cap.ToString(CultureInfo.InvariantCulture));
            }
            else // :113-119
            {
                hydraPolicy = HydraPrompts.FallbackPolicy(hydra);
                arguments.Add("--disallowedTools");
                arguments.Add("Agent");
                arguments.Add("Task");
            }
        }
        // Exactly one combined system prompt segment for every session, Hydra or not. :121-128
        arguments.Add("--append-system-prompt");
        arguments.Add(string.Join("\n\n", new[] { hydraPolicy, AntiSlopPolicy.Instructions(configuration.AntiSlopEnabled) }.OfType<string>()));
        if (configuration.ResumeID is { } resumeID) // :129-134
        {
            arguments.Add("--resume");
            arguments.Add(resumeID);
            if (configuration.ResumeAt is { } anchor)
            {
                arguments.Add("--resume-session-at");
                arguments.Add(anchor);
            }
        }
        else
        {
            arguments.Add("--session-id");
            arguments.Add(id);
        }

        var process = new StdioProcess(executable.Path, arguments, configuration.WorkingDirectory.Path, environment); // :136-141
        this.process = process;
        process.Start(); // :143
        var messages = process.Messages; // :144
        readTask = MainActor.Spawn(async () => // :145-151
        {
            await foreach (var message in messages) Handle(message);
            DidClose();
        });

        sessionID = id; // :153
        var initialize = new Dictionary<string, JSONValue> { ["subtype"] = "initialize", ["hooks"] = new JSONValue.Null() }; // :154
        if (configuration.Hydra?.RunsNatively == true) // :155-159
        {
            // One-line progress notes for the heads, on task_progress.
            initialize["agentProgressSummaries"] = true;
            initialize["forwardSubagentText"] = true;
        }
        var initialization = await Control(new JSONValue.Object(new ValueMap<string, JSONValue>(initialize, SwiftStringComparer.Instance))); // :160
        isStarted = true; // :161
        var models = Models(initialization); // :162
        if (models.Count != 0) OnEvent?.Invoke(new ProviderEvent.Models(models, configuration.Model)); // :163
        var commands = SlashCommand.ClaudeCommands(initialization["commands"]?.JSONValueArray ?? []); // :164
        if (commands.Count != 0) OnEvent?.Invoke(new ProviderEvent.Commands(commands)); // :165
        return id; // :166
    }

    private static ValueList<ModelOption> Models(JSONValue initialization) // :169-181
    {
        var entries = initialization["models"]?.JSONValueArray ?? [];
        var options = new List<ModelOption>();
        foreach (var entry in entries)
        {
            if (entry["value"]?.JSONValueString is not { } value) continue;
            var efforts = (entry["supportedEffortLevels"]?.JSONValueArray ?? []).Select(item => item.JSONValueString).OfType<string>();
            options.Add(new ModelOption(
                id: value,
                name: entry["displayName"]?.JSONValueString ?? value,
                detail: entry["description"]?.JSONValueString,
                efforts: new ValueList<string>(efforts),
                isDefault: SwiftString.Equal(value, "default"),
                fastTier: entry["supportsFastMode"]?.JSONValueBool == true ? "fast" : null));
        }
        return new ValueList<ModelOption>(options);
    }

    /// <summary>PR-PROVCLI-022 to 024: the static catalog probe. A bare process, not a session.</summary>
    public static async Task<(ValueList<ModelOption> Models, ValueList<SlashCommand> Commands)> Handshake( // :185-212
        SwiftURL executable, SwiftURL directory, IReadOnlyDictionary<string, string> environment)
    {
        var process = new StdioProcess(
            executable.Path,
            ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--setting-sources", "user,project,local"],
            directory.Path,
            environment);
        process.Start(); // :192
        var watchdog = MainActor.Spawn(async () => // :193-196
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(20));
            if (!SwiftTask.IsCancelled) process.Terminate();
        });
        try // :197
        {
            process.Send(new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> // :198
            {
                ["type"] = "control_request",
                ["request_id"] = "handshake",
                ["request"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["subtype"] = "initialize",
                    ["hooks"] = new JSONValue.Null(),
                }, SwiftStringComparer.Instance)),
            }, SwiftStringComparer.Instance)));

            return await SwiftTask.WithCancellationHandler(async () => // :199-208
            {
                await foreach (var message in process.Messages)
                {
                    if (message["type"]?.JSONValueString != "control_response" ||
                        message["response"]?["request_id"]?.JSONValueString != "handshake") continue;
                    if (message["response"]?["response"] is not { } response || response["commands"]?.JSONValueArray is not { } commands)
                        throw new ProviderError(new ProviderError.Case.Failed("Claude could not load its commands."));
                    return (Models(response), SlashCommand.ClaudeCommands(commands));
                }
                throw new ProviderError(new ProviderError.Case.Failed("Claude's command discovery did not finish. Try again."));
            }, () => process.Terminate()); // :209-211
        }
        finally // :197 defer
        {
            watchdog.Cancel();
            process.Terminate();
        }
    }

    // MARK: - Wire

    // Swift `private`; kept internal for this part's own tests (see the file header note): PR-PROVCLI-019's
    // "no process" half is proven by calling this directly on a fresh session, since start() itself cannot
    // reach the guard without first passing the executable check (PR-PROVCLI-001).
    internal async Task<JSONValue> Control(JSONValue request) // :301-310
    {
        if (process is not { } activeProcess) throw new ProviderError(new ProviderError.Case.NotRunning());
        if (hasClosed) throw new ProviderError(new ProviderError.Case.Failed(launchError ?? "Claude exited."));
        controlCounter += 1;
        // DEVIATION OD-0012 F1: upstream "droppy-code-<n>"; the port sends "marrow-<n>".
        var requestID = $"marrow-{controlCounter}";
        return await CheckedContinuation<JSONValue>.With(continuation =>
        {
            pendingControl[requestID] = continuation;
            activeProcess.Send(new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["type"] = "control_request",
                ["request_id"] = requestID,
                ["request"] = request,
            }, SwiftStringComparer.Instance)));
        });
    }

    /// <summary>Swift `private`; kept internal so this part's own tests can drive dispatch (and, for
    /// HandleSystem below, its subtypes) without a live child process (see the file header note).</summary>
    internal void Handle(JSONValue message) // :331-386
    {
        // A head's message names the tool call that spawned it; the lead's carries null. :333
        var parent = message["parent_tool_use_id"]?.JSONValueString;
        switch (message["type"]?.JSONValueString)
        {
            case "control_response": // :335-343
                var response = message["response"] ?? new JSONValue.Null();
                if (response["request_id"]?.JSONValueString is not { } requestID || !pendingControl.Remove(requestID, out var continuation)) return;
                if (SwiftString.Equal(response["subtype"]?.JSONValueString ?? "", "error"))
                    continuation.Resume(new ProviderError(new ProviderError.Case.Failed(response["error"]?.JSONValueString ?? "Claude rejected the request.")));
                else
                    continuation.Resume(response["response"] ?? new JSONValue.Null());
                break;
            case "control_request": // :344-345
                HandleControlRequest(message);
                break;
            case "control_cancel_request": // :346-349
                if (message["request_id"]?.JSONValueString is { } cancelID && pendingTools.Remove(cancelID))
                    OnEvent?.Invoke(new ProviderEvent.RequestResolved(cancelID));
                break;
            case "system": // :350-351
                HandleSystem(message);
                break;
            case "stream_event": // :352-362
                if (parent is { } streamParent)
                {
                    var stream = agentStreams.TryGetValue(streamParent, out var headStream) ? headStream : new StreamState();
                    HandleStreamEvent(message["event"] ?? new JSONValue.Null(), ref stream, AgentSink(streamParent));
                    agentStreams[streamParent] = stream;
                }
                else
                {
                    var stream = mainStream;
                    HandleStreamEvent(message["event"] ?? new JSONValue.Null(), ref stream, LeadSink);
                    mainStream = stream;
                }
                break;
            case "assistant": // :363-370
                if (parent is { } assistantParent)
                {
                    var stream = agentStreams.TryGetValue(assistantParent, out var headStream) ? headStream : new StreamState();
                    HandleAssistant(message, ref stream, AgentSink(assistantParent));
                    agentStreams[assistantParent] = stream;
                }
                else
                {
                    var stream = mainStream;
                    HandleAssistant(message, ref stream, LeadSink);
                    mainStream = stream;
                }
                break;
            case "user": // :371-372
                HandleToolResults(message, parent is { } toolResultParent ? AgentSink(toolResultParent) : LeadSink);
                break;
            case "result": // :373-374
                HandleResult(message);
                break;
            case "rate_limit_event": // :375-382
                var info = message["rate_limit_info"] ?? new JSONValue.Null();
                if (SwiftString.Equal(info["status"]?.JSONValueString ?? "", "rejected"))
                {
                    var resetsAt = (info["resetsAt"]?.JSONValueDouble ?? info["resetsAtSeconds"]?.JSONValueDouble) is { } seconds
                        ? SwiftDate.From1970Seconds(seconds)
                        : (DateTimeOffset?)null;
                    var when = resetsAt is { } r ? $" It resets at {UsageLimitSignal.Format(r)}." : "";
                    OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = $"Claude hit a usage limit.{when}" }));
                    OnEvent?.Invoke(new ProviderEvent.UsageLimit(resetsAt));
                }
                break;
            default:
                break;
        }
    }

    /// <summary>Where a stream's events go: straight out for the lead, wrapped for a head. Swift's lazily
    /// created leadSink (:451) is a single delegate invoke here; no laziness is needed.</summary>
    private void LeadSink(ProviderEvent providerEvent) => OnEvent?.Invoke(providerEvent);

    private Action<ProviderEvent> AgentSink(string agentID) => providerEvent => OnEvent?.Invoke(new ProviderEvent.AgentEvent(agentID, providerEvent)); // :453-455

    // handleControlRequest's own body (building an ApprovalRequest/QuestionRequest from a live can_use_tool
    // request, Swift :611-680ish) is a later part of this unit; not invoked by this part's own tests, which
    // seed pendingTools directly (see SeedPendingTool below).
    private void HandleControlRequest(JSONValue message)
    {
    }

    // handleStreamEvent's block bookkeeping (Swift :457-497) is a later part of this unit.
    private void HandleStreamEvent(JSONValue @event, ref StreamState stream, Action<ProviderEvent> sink)
    {
    }

    // handleAssistant's content-block parsing (Swift :505-546) is a later part of this unit.
    private void HandleAssistant(JSONValue message, ref StreamState stream, Action<ProviderEvent> sink)
    {
    }

    // handleToolResults' tool_result extraction (Swift :548-562) is a later part of this unit.
    private void HandleToolResults(JSONValue message, Action<ProviderEvent> sink)
    {
    }

    // handleResult's usage/turn-completion accounting (Swift :564-609) is a later part of this unit.
    private void HandleResult(JSONValue message)
    {
    }

    /// <summary>Swift `private`; kept internal for this part's own tests. handleControlRequest, which would
    /// populate pendingTools from a live can_use_tool request, is a later part of this unit (see the file
    /// header note), so ResolveApproval/AnswerQuestion/Interrupt's own tests seed a pending tool directly.</summary>
    internal void SeedPendingTool(string requestID, string name, JSONValue input, JSONValue? suggestions = null) =>
        pendingTools[requestID] = new PendingTool { Name = name, Input = input, Suggestions = suggestions };

    internal void HandleSystem(JSONValue message) // :388-425
    {
        switch (message["subtype"]?.JSONValueString)
        {
            case "init": // :390-394
                if (message["session_id"]?.JSONValueString is { } newSessionID &&
                    (sessionID is not { } currentSessionID || !SwiftString.Equal(newSessionID, currentSessionID)))
                {
                    sessionID = newSessionID;
                    OnEvent?.Invoke(new ProviderEvent.SessionReady(newSessionID));
                }
                break;
            case "compact_boundary": // :395-396
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." }));
                break;
            case "task_started": // :397-398
                HandleTaskStarted(message);
                break;
            case "task_progress": // :399-412
                if (message["task_id"]?.JSONValueString is not { } progressTaskID || !agentsByTask.TryGetValue(progressTaskID, out var progressAgentID)) return;
                var usage = message["usage"];
                // The head's own running total, for its panel. It is not added to the ledger: this total and
                // the turn result's usage are two accounts of overlapping work, and the CLI does not say how
                // much of the head's work the result already covers, so counting both would charge it twice.
                OnEvent?.Invoke(new ProviderEvent.AgentProgress(
                    progressAgentID,
                    message["summary"]?.JSONValueString,
                    message["last_tool_name"]?.JSONValueString,
                    usage?["total_tokens"]?.JSONValueInt,
                    usage?["tool_uses"]?.JSONValueInt));
                break;
            case "task_notification": // :413-421
                if (message["task_id"]?.JSONValueString is not { } notificationTaskID || !agentsByTask.TryGetValue(notificationTaskID, out var notificationAgentID)) return;
                var status = message["status"]?.JSONValueString switch
                {
                    "failed" => TurnStatus.Failed,
                    "stopped" => TurnStatus.Interrupted,
                    _ => TurnStatus.Completed,
                };
                // Each head's stream goes with its own task_notification.
                agentStreams.Remove(notificationAgentID);
                OnEvent?.Invoke(new ProviderEvent.AgentFinished(notificationAgentID, status, message["summary"]?.JSONValueString));
                break;
            default:
                break;
        }
    }

    /// <summary>A subagent task: the head it stands for is keyed by the spawning tool call, which its
    /// transcript messages carry. Background shell commands are tasks too and pass.</summary>
    private void HandleTaskStarted(JSONValue message) // :427-447
    {
        if (message["task_id"]?.JSONValueString is not { } taskID || message["skip_transcript"]?.JSONValueBool == true) return;
        var isAgent = message["subagent_type"]?.JSONValueString is not null ||
            (message["task_type"]?.JSONValueString is { } taskType && SwiftString.Equal(taskType, "local_agent"));
        if (!isAgent) return;
        var toolUseID = message["tool_use_id"]?.JSONValueString;
        var agentID = toolUseID ?? taskID;
        agentsByTask[taskID] = agentID;
        tasksByAgent[agentID] = taskID;
        OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn
        {
            Id = agentID,
            TaskID = taskID,
            ToolUseID = toolUseID,
            Description = message["description"]?.JSONValueString ?? "Subagent",
            Prompt = message["prompt"]?.JSONValueString,
            Model = null,
            Profile = HydraPrompts.ProfileName(message["subagent_type"]?.JSONValueString),
            IsBackground = message["is_backgrounded"]?.JSONValueBool ?? false,
        }));
    }

    private void WriteUserMessage(JSONValue content) // :312-322
    {
        turnActive = true; // :313
        interruptRequested = false; // :314
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null)); // :315
        process?.Send(JSONValue.FromDictionaryLiteral( // :316-321
            ("type", "user"),
            ("session_id", ""),
            ("message", JSONValue.FromDictionaryLiteral(("role", "user"), ("content", content))),
            ("parent_tool_use_id", JSONValue.FromNilLiteral())));
    }

    private void SendToolResponse(string requestID, JSONValue response) // :324-329
    {
        process?.Send(JSONValue.FromDictionaryLiteral(
            ("type", "control_response"),
            ("response", JSONValue.FromDictionaryLiteral(
                ("subtype", "success"),
                ("request_id", requestID),
                ("response", response)))));
    }

    private static bool NullableModelEqual(string? left, string? right) =>
        left is null ? right is null : right is not null && SwiftString.Equal(left, right);

    private static JSONValue SessionScoped(JSONValue suggestion) // :838-842
    {
        if (suggestion is not JSONValue.Object suggestionObject) return suggestion;
        return new JSONValue.Object(suggestionObject.Value.Mutating(map => map["destination"] = "session"));
    }

    private void DidClose() // :680-696
    {
        hasClosed = true;
        var tail = process?.ErrorTail ?? "";
        // The result's own error names the failure better than the stderr tail can.
        var reason = launchError ?? TextCleanup.LastLines(tail);
        launchError = reason;
        var waiting = pendingControl.Values.ToList();
        pendingControl.Clear();
        foreach (var continuation in waiting)
            continuation.Resume(new ProviderError(new ProviderError.Case.Failed(reason ?? "Claude exited.")));
        foreach (var requestID in pendingTools.Keys.ToList()) OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID));
        pendingTools.Clear();
        turnActive = false;
        // A launch that failed is reported by Start() throwing; only a session that ran says it exited.
        if (isStopping || !isStarted) return;
        OnEvent?.Invoke(new ProviderEvent.Exited(TextCleanup.LastLines(tail)));
    }

    public async Task Send(TurnInput input) // :214-236
    {
        if (process is not { IsRunning: true }) throw new ProviderError(new ProviderError.Case.NotRunning()); // :215
        runtimeMode = input.RuntimeMode; // :216
        var mode = ModeName(input.RuntimeMode, input.InteractionMode); // :217
        if (!SwiftString.Equal(mode, permissionMode)) // :218-221
        {
            _ = await SwiftTry.OptionalAsync(async () => (JSONValue?)await Control(JSONValue.FromDictionaryLiteral(("subtype", "set_permission_mode"), ("mode", mode))));
            permissionMode = mode;
        }
        if (!NullableModelEqual(input.Model, model)) // :222-226
        {
            var wire = input.Model is { } requestedModel && SwiftString.Equal(requestedModel, "default") ? null : input.Model;
            _ = await SwiftTry.OptionalAsync(async () => (JSONValue?)await Control(JSONValue.FromDictionaryLiteral(("subtype", "set_model"), ("model", JSONValue.Optional(wire)))));
            model = input.Model;
        }
        var content = new List<JSONValue>(); // :227
        foreach (var (image, base64) in await Attachment.Base64Images(input.Images)) // :228-233
        {
            content.Add(JSONValue.FromDictionaryLiteral(
                ("type", "image"),
                ("source", JSONValue.FromDictionaryLiteral(("type", "base64"), ("media_type", image.MimeType), ("data", base64)))));
        }
        content.Add(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", input.Text))); // :234
        WriteUserMessage(new JSONValue.Array(new ValueList<JSONValue>(content))); // :235
    }

    public async Task Interrupt() // :238-247
    {
        if (!turnActive) return; // :239
        interruptRequested = true; // :240
        foreach (var requestID in pendingTools.Keys.ToList()) // :241-244
        {
            SendToolResponse(requestID, JSONValue.FromDictionaryLiteral(
                ("behavior", "deny"), ("message", "The user stopped the turn."), ("interrupt", true)));
            OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID));
        }
        pendingTools.Clear(); // :245
        _ = await SwiftTry.OptionalAsync(async () => (JSONValue?)await Control(JSONValue.FromDictionaryLiteral(("subtype", "interrupt")))); // :246
    }

    public Task Compact() // :249-252
    {
        if (process is not { IsRunning: true }) throw new ProviderError(new ProviderError.Case.NotRunning()); // :250
        WriteUserMessage(new JSONValue.String("/compact")); // :251
        return Task.CompletedTask;
    }

    public void ResolveApproval(string requestID, string optionID) // :254-275
    {
        if (!pendingTools.Remove(requestID, out var pending)) return; // :255
        if (SwiftString.Equal(optionID, "allow") || SwiftString.Equal(optionID, "always")) // :256
        {
            var response = new Dictionary<string, JSONValue> { ["behavior"] = "allow", ["updatedInput"] = pending.Input }; // :257
            if (SwiftString.Equal(optionID, "always") && pending.Suggestions?.JSONValueArray is { } suggestions) // :258-260
                response["updatedPermissions"] = new JSONValue.Array(new ValueList<JSONValue>(suggestions.Select(SessionScoped)));
            SendToolResponse(requestID, new JSONValue.Object(new ValueMap<string, JSONValue>(response, SwiftStringComparer.Instance))); // :261
            if (SwiftString.Equal(pending.Name, "ExitPlanMode")) // :262-267
            {
                OnEvent?.Invoke(new ProviderEvent.ModeChanged(InteractionMode.Build)); // :263
                var mode = ModeName(runtimeMode, InteractionMode.Build); // :264
                permissionMode = mode; // :265
                MainActor.Spawn(async () => { _ = await SwiftTry.OptionalAsync(async () => (JSONValue?)await Control(JSONValue.FromDictionaryLiteral(("subtype", "set_permission_mode"), ("mode", mode)))); }); // :266
            }
        }
        else // :268-273
        {
            var message = SwiftString.Equal(pending.Name, "ExitPlanMode")
                ? "The user wants to keep planning. Wait for their feedback before changing anything."
                : "The user declined this action.";
            SendToolResponse(requestID, JSONValue.FromDictionaryLiteral(("behavior", "deny"), ("message", message)));
        }
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :274
    }

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) // :277-287
    {
        if (!pendingTools.Remove(requestID, out var pending)) return; // :278
        var input = pending.Input is JSONValue.Object pendingInputObject ? pendingInputObject.Value : ValueMap<string, JSONValue>.Empty; // :279
        var mapped = new Dictionary<string, JSONValue>(); // :280
        foreach (var (question, values) in answers) mapped[question] = string.Join(", ", values); // :281-283
        var updatedInput = input.Mutating(map => map["answers"] = new JSONValue.Object(new ValueMap<string, JSONValue>(mapped, SwiftStringComparer.Instance))); // :284
        SendToolResponse(requestID, JSONValue.FromDictionaryLiteral(("behavior", "allow"), ("updatedInput", new JSONValue.Object(updatedInput)))); // :285
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :286
    }

    public void Stop() // :289-292
    {
        isStopping = true; // :290
        process?.Terminate(); // :291
    }

    public async Task<bool> StopAgent(string id) // :294-297
    {
        if (!tasksByAgent.TryGetValue(id, out var taskID)) return false;
        return await SwiftTry.OptionalAsync(async () => (JSONValue?)await Control(JSONValue.FromDictionaryLiteral(("subtype", "stop_task"), ("task_id", taskID)))) is not null;
    }
}
