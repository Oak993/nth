using System.Diagnostics;
using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/ClaudeSession.swift (B2 1fbf81e), :1-693.
//
// Unit Services.Providers.ClaudeSession part 1 of 4 (PR-PROVCLI-001 to 024) implements the
// launch path: the executable guard, the session id, the fixed and conditional argument
// vector, the --help option probe, the Hydra argument blocks, the always-present
// --append-system-prompt, the resume/session-id tail, spawning the process and attaching its
// reader, the initialize handshake (and the control-request wire it rides: :299-310, :331-343
// so far as control_response resolves it), and the static command-discovery probe (:183-212).
// Send, Interrupt, ResolveApproval, AnswerQuestion, Compact, Stop, the rest of Handle's switch
// (control_request, control_cancel_request, system, stream_event, assistant, user, result,
// rate_limit_event: :344-385) and everything those reach are later parts of this unit; their
// interface members are placeholders here (never invoked by this part's own tests) so the type
// satisfies ProviderSession. DidClose (:680-696) is self-contained (process.ErrorTail,
// TextCleanup.LastLines, pendingControl, pendingTools, OnEvent, turnActive, isStopping,
// isStarted only) and is ported here in full because Control (:301-310, needed by PR-PROVCLI-019)
// and the readTask (PR-PROVCLI-017) both depend on it; PR-PROVCLI-061, which fully exercises it,
// belongs to a later part.
public sealed class ClaudeSession : ProviderSession
{
    // :15-19
    private sealed record PendingTool
    {
        public required string Name { get; init; }
        public required JSONValue Input { get; init; }
        public JSONValue? Suggestions { get; init; }
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
    // turnActive is read only by Send/Interrupt/writeUserMessage (:214-236, :238-247, :312-314), a later
    // part of this unit; DidClose (:680-696, self-contained and ported here) already writes it.
#pragma warning disable CS0414
    private bool turnActive;                                // :46
#pragma warning restore CS0414
    // isStopping is written only by Stop (:289-292), a later part of this unit; DidClose (ported here)
    // already reads it, so it needs an explicit initializer to avoid CS0649 until Stop lands.
    private bool isStopping = false;                        // :48
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

    private void Handle(JSONValue message) // :331-386
    {
        switch (message["type"]?.JSONValueString)
        {
            case "control_response": // :335-343
                var response = message["response"] ?? new JSONValue.Null();
                if (response["request_id"]?.JSONValueString is not { } requestID || !pendingControl.Remove(requestID, out var continuation)) return;
                if (response["subtype"]?.JSONValueString == "error")
                    continuation.Resume(new ProviderError(new ProviderError.Case.Failed(response["error"]?.JSONValueString ?? "Claude rejected the request.")));
                else
                    continuation.Resume(response["response"] ?? new JSONValue.Null());
                break;
            default:
                // control_request, control_cancel_request, system, stream_event, assistant,
                // user, result and rate_limit_event (:344-385) are later parts of this unit.
                break;
        }
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

    // The remaining ProviderSession members (Send :214-236, Interrupt :238-247, Compact :249-252,
    // ResolveApproval :254-275, AnswerQuestion :277-287, Stop :289-292) are later parts of this
    // unit and are not exercised by this part's tests.
    public Task Send(TurnInput input) => throw new NotImplementedException("ClaudeSession.Send: a later part of unit Services.Providers.ClaudeSession (Swift :214-236).");

    public Task Interrupt() => throw new NotImplementedException("ClaudeSession.Interrupt: a later part of unit Services.Providers.ClaudeSession (Swift :238-247).");

    public void ResolveApproval(string requestID, string optionID) => throw new NotImplementedException("ClaudeSession.ResolveApproval: a later part of unit Services.Providers.ClaudeSession (Swift :254-275).");

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) => throw new NotImplementedException("ClaudeSession.AnswerQuestion: a later part of unit Services.Providers.ClaudeSession (Swift :277-287).");

    public Task Compact() => throw new NotImplementedException("ClaudeSession.Compact: a later part of unit Services.Providers.ClaudeSession (Swift :249-252).");

    public void Stop() => throw new NotImplementedException("ClaudeSession.Stop: a later part of unit Services.Providers.ClaudeSession (Swift :289-292).");
}
