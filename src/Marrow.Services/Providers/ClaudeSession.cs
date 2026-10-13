using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Store;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/ClaudeSession.swift (B3 52bc7926), :1-829.
//
// B3 (1.7.0) delta from B2 (R3-48, ClaudeSession #1 folded with R3-13 #3; PR-PROVCLI-442 new): pendingControl
// (:37) becomes a PendingReplies<JSONValue>, so Control (:301-309) registers through it instead of a bare
// continuation dictionary, is now cancellable (a caller already cancelled throws before anything is sent, and
// one cancelled while waiting throws with the late reply dropped), and Handle's control_response case
// (:334-341) and DidClose (:678-692) resolve/fail/failAll through it. TodoWrite parsing (:534-535) now goes
// through the shared ACPSession.ParseTodoItems instead of this file's own todos(from:), which is deleted along
// with resultText(_:), replaced by the shared ToolText.blocks (already ported at lane A7).
//
// Unit Services.Providers.ClaudeSession part 1 of 4 (PR-PROVCLI-001 to 024) implemented the
// launch path: the executable guard, the session id, the fixed and conditional argument
// vector, the --help option probe, the Hydra argument blocks, the always-present
// --append-system-prompt, the resume/session-id tail, spawning the process and attaching its
// reader, the initialize handshake (and the control-request wire it rides: :299-309, :330-341
// so far as control_response resolves it), and the static command-discovery probe (:183-212).
//
// Part 2 (PR-PROVCLI-025 to 048) adds: Send (:214-236), Interrupt (:238-247), Compact
// (:249-252), ResolveApproval (:254-275) with sessionScoped (:814-818), AnswerQuestion
// (:277-287), Stop (:289-292), StopAgent (:294-297), writeUserMessage (:311-321),
// sendToolResponse (:323-328), the rest of Handle's dispatch switch (:330-383, control_request,
// control_cancel_request, system, stream_event, assistant, user, result, rate_limit_event) and
// handleSystem's init/compact_boundary/task_started/task_progress/task_notification cases
// (:386-423, handleTaskStarted :425-445).
//
// handleControlRequest's own body (:609-678ish: building an ApprovalRequest/QuestionRequest
// from a live can_use_tool request) and the four per-event-type transcript handlers
// (handleStreamEvent :455-495, handleAssistant :503-544, handleToolResults :546-560, handleResult
// :562-607) are later parts of this unit: Handle's switch (this part) routes to them by name, but
// their bodies are no-op placeholders here, never invoked by this part's own tests, so the type
// keeps compiling while those parts are pending. Because handleControlRequest does not populate
// pendingTools yet, this part's own tests seed it directly through SeedPendingTool (Swift
// `private`, kept internal for tests only, see the seam note on it below).
//
// DidClose (:678-690) is self-contained (process.ErrorTail, TextCleanup.LastLines,
// pendingControl, pendingTools, OnEvent, turnActive, isStopping, isStarted only) and was ported
// in part 1 because Control (:301-309, needed by PR-PROVCLI-019) and the readTask
// (PR-PROVCLI-017) both depend on it; PR-PROVCLI-061, which fully exercises it, belongs to a
// later part.
public sealed partial class ClaudeSession : ProviderSession, Marrow.Services.Port.Skills.Providers.ISkillSessionLifetime
{
    private Task skillReadersExited = Task.CompletedTask;
    public Task SkillReadersExited => skillReadersExited;

    // :15-19
    private sealed record PendingTool
    {
        public required string Name { get; init; }
        public required JSONValue Input { get; init; }
        public JSONValue? Suggestions { get; init; }
    }

    // :22-27. The open blocks of one streamed transcript: the lead's, or one head's. A mutable value-type
    // struct, so Handle's "copy out of the dict, mutate by ref, write back" (:350-368, PR-PROVCLI-049)
    // reproduces the Swift inout discipline exactly: no reference into the map is held open across a sink
    // callback that can re-enter this session.
    internal struct StreamState
    {
        public string? CurrentMessageID;                        // :23
        public Dictionary<long, string> BlockIDs = new();        // :24 Swift Int is 64-bit
        public Dictionary<string, List<string>> OpenText = new(SwiftStringComparer.Instance);     // :25
        public Dictionary<string, List<string>> OpenThinking = new(SwiftStringComparer.Instance); // :26
        /// <summary>PORT-ONLY (web surfaces W24, ED-NEW-web-w24-3): the arguments so far of a live-answer tool call being streamed, by content block index.</summary>
        public Dictionary<long, (string ToolId, System.Text.StringBuilder Json)> LiveInputs = new();

        public StreamState() { }
    }

    private readonly SessionConfiguration configuration; // :29
    private IClaudeProcess? process;                        // :30
    // PORT-ONLY (Ignis Advisor, ED-NEW-ignisadvisor-2): this launch's ask_advisor server; null for a chat with no advisor.
    private MCPResolvedServer? advisorServer;
    // PORT-ONLY (web surfaces W12, ED-NEW-web-w12-1): this launch's nth-ui server (Tabula's artifact tools); null for a session of no chat.
    private MCPResolvedServer? uiServer;
    private MCPResolvedServer? pcServer; // PORT-ONLY (Tactus, ED-NEW-pcctl-41): the chat's nth-pc server while Tactus is on.
    private static readonly string AdvisorToolName = "mcp__" + Port.Advisor.AdvisorMcpServer.ServerName + "__" + Port.Advisor.AdvisorTool.Name;
    private TaskHandle? readTask;                          // :31
    private string? sessionID;                             // :32
    private string permissionMode;                         // :33
    private RuntimeMode runtimeMode;                        // :34
    private string? model;                                  // :35
    private long controlCounter;                            // :36
    // DEVIATION OD-0012 F1: request ids are "marrow-<n>" (see Control below), matching PendingReplies' own
    // TValue : class constraint (JSONValue is a reference-typed record, ENGINEERING DECISION recorded on
    // PendingReplies<TValue> itself). :37
    private readonly PendingReplies<JSONValue> pendingControl = new();
    private readonly Dictionary<string, PendingTool> pendingTools = new(SwiftStringComparer.Instance); // :38
    private readonly Dictionary<string, string> toolNames = new(SwiftStringComparer.Instance); // :39
    // The lead's own stream state and each head's, by the tool call that spawned it. Their inner block
    // bookkeeping (currentMessageID, blockIDs, openText, openThinking, Swift :22-27) is added by the later
    // part that implements handleStreamEvent/handleAssistant; this part only needs the dictionary shape to
    // add and remove a head's entry.
    private StreamState mainStream = new(); // :40
    private readonly Dictionary<string, StreamState> agentStreams = new(SwiftStringComparer.Instance); // :42
    // Heads by their task id, for progress and completion, and their task ids by head, for stopping. :44-45
    private readonly Dictionary<string, string> agentsByTask = new(SwiftStringComparer.Instance); // :44
    private readonly Dictionary<string, string> tasksByAgent = new(SwiftStringComparer.Instance); // :45
    private bool turnActive;                                // :46, read by Interrupt (:239) and written by writeUserMessage (:312) and DidClose
    private bool interruptRequested;                        // :47, written by Interrupt (:240)/writeUserMessage (:313), read by HandleResult (:595)
    private bool isStopping;                                // :48, written by Stop (:290); DidClose (ported in part 1) reads it.
    private bool isStarted;                                 // :51
    private bool hasClosed;                                 // :52
    private string? launchError;                            // :55
    private static readonly Dictionary<string, HashSet<string>> knownOptions = new(SwiftStringComparer.Instance); // :56

    // DEVIATION OD-0099: SK-16 (DL-19) supplies a factory for this chat's SK-9 generation.
    // Null/no content means no additional skills; unsupported CLIs never call the factory.
    private readonly Func<Generation?>? acquireSkills;
    private Generation? generation;
    private readonly ClaudeSkillEvents skillEvents = new();
    private readonly Dictionary<string, string> managedSkillItems = new(StringComparer.Ordinal);
    private bool skillWarningShown;

    public ClaudeSession(SessionConfiguration configuration, Func<Generation?>? acquireSkills = null) // :58-63
    {
        this.configuration = configuration;
        this.acquireSkills = acquireSkills;
        runtimeMode = configuration.RuntimeMode;
        permissionMode = ModeName(configuration.RuntimeMode, configuration.InteractionMode);
        model = configuration.Model;
    }

    public Action<ToolEvent>? OnSkillEvent { get; set; }
    public Exception? LastSkillEventFailure => skillEvents.LastFailure;

    public Action<ProviderEvent>? OnEvent { get; set; } // :13

    public bool IsRunning => process?.IsRunning ?? false; // :65

    private string WorkingDirectory => configuration.WorkingDirectory.Path; // :67

    /// <summary>PR-PROVCLI-004: plan is tested first, overriding the runtime mode. Swift `private`; kept
    /// internal for this part's own tests (see the file header note).</summary>
    internal static string ModeName(RuntimeMode runtime, InteractionMode interaction) // :820-828
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
        var result = await SwiftTry.OptionalAsync<ShellResult>(async () => await ClaudeProviderRuntime.Help.Current(executable, environment));
        if (result is null) return [];
        var help = TextCleanup.StripANSI(result.Output + result.ErrorOutput);
        var found = new[] { "--system-prompt-snapshot", "--forward-subagent-text" }
            .Where(option => help.Contains(option, StringComparison.Ordinal));
        var options = new HashSet<string>(found, SwiftStringComparer.Instance);
        if (result.Succeeded && System.Text.RegularExpressions.Regex.IsMatch(help, @"(?<![\w-])--plugin-dir(?![\w-])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) options.Add("--plugin-dir");
        knownOptions[executable.Path] = options;
        return options;
    }

    public async Task<string> Start()
    {
        try { return await StartCore(); }
        catch
        {
            // Keep a lease until the reader observes process exit, including a failed initialize.
            if (generation is not null && process?.IsRunning == true)
            {
                process.Terminate();
                RetainSkillsUntilExit(process);
            }
            else ReleaseSkills();
            throw;
        }
    }

    /// <summary>PORT-ONLY (ED-NEW-web-w26-1): what a no-tools launch adds: no built-in tools, only the (absent) --mcp-config servers, no slash commands.
    /// + ED-NEW-web-sec-artifacts-1: and it is never saved as a session on disk.</summary>
    internal static readonly string[] NoToolsArguments = ["--tools", "", "--strict-mcp-config", "--disable-slash-commands", "--no-session-persistence"];

    /// <summary>
    /// PORT-ONLY (ED-NEW-web-sec-artifacts-1, security review M4): a no-tools launch (an artifact's model call) loads no setting source
    /// (the CLI reads "" as none: no user, project or local settings, so no CLAUDE.md, hooks, plugins or permission rules from them),
    /// and these switches turn every CLAUDE.md file and auto memory off by name as well, whatever the account's environment says.
    /// </summary>
    internal static readonly (string Name, string Value)[] NoToolsEnvironment = [("CLAUDE_CODE_DISABLE_CLAUDE_MDS", "1"), ("CLAUDE_CODE_DISABLE_AUTO_MEMORY", "1")];

    private async Task<string> StartCore() // :82-167
    {
        if (configuration.Executable is not { } executable) throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider)); // :83
        var id = configuration.ResumeID ?? SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid())); // :84
        List<string> arguments = // :85-90
        [
            "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--include-partial-messages", "--permission-prompt-tool", "stdio",
            "--setting-sources", configuration.NoTools ? "" : "user,project,local", // ED-NEW-web-sec-artifacts-1: none for an artifact's call
            "--allow-dangerously-skip-permissions", "--permission-mode", permissionMode,
        ];
        // DEVIATION (ED-NEW-ignisadvisor-1): a picker entry that pairs a main model with an advisor launches on its main model, with
        // `--advisor` only when the installed CLI can take it (AdvisorLaunch); every other model launches exactly as before.
        var advisorLaunch = Marrow.Core.Port.Advisor.ClaudeAdvisorModels.Find(configuration.Model) is { } advisorEntry ? await AdvisorLaunch(advisorEntry, executable) : null;
        if ((advisorLaunch?.MainModel ?? configuration.Model) is { } configuredModel && !SwiftString.Equal(configuredModel, "default")) // :91
        {
            arguments.Add("--model");
            arguments.Add(configuredModel);
        }
        if (advisorLaunch?.Advisor is { } advisor)
        {
            arguments.Add(Marrow.Core.Port.Advisor.ClaudeAdvisorModels.Flag);
            arguments.Add(advisor);
        }
        if (configuration.Effort is { } effort && effort.Length != 0) // :92
        {
            arguments.Add("--effort");
            arguments.Add(effort);
        }
        if (handshakeAgent is not null || configuration.FastMode) // :93; OD-0114 temporary flag layer, never a settings file.
        {
            arguments.Add("--settings");
            arguments.Add(HandshakeLaunchSettings());
        }
        // Connected servers from Settings are the only MCP servers the session sees. :94-96
        // PORT-ONLY (Ignis Advisor, ED-NEW-ignisadvisor-2): a Hydra head's ask_advisor server is merged into that one
        // config; a chat with no advisor gets exactly the connected servers' file, as before.
        if (configuration.NoTools)
        {
            // PORT-ONLY (web surfaces W26, ED-NEW-web-w26-1): no built-in tool, no MCP server (not even the user's own
            // config files) and no slash command, as TextGeneration launches its text-only calls.
            arguments.AddRange(NoToolsArguments);
        }
        else
        {
            advisorServer = MCPProviderConfig.AdvisorServer(configuration);
            uiServer = MCPProviderConfig.UiServer(configuration); // + ED-NEW-web-w12-1: a chat's nth-ui server joins the same file
            // PORT-ONLY (Tactus, ED-NEW-pcctl-41): the nth-pc server joins the same file while Tactus is on.
            pcServer = MCPProviderConfig.PcServer(configuration);
            if (MCPProviderConfig.ClaudeBuiltInConfig(MCPProviderConfig.BuiltInServers(advisorServer, pcServer, uiServer), configuration.IgnisThreadID) is { } mcpConfig)
            {
                arguments.Add("--mcp-config");
                arguments.Add(mcpConfig.Path);
                if (mcpConfig.Strict) arguments.Add("--strict-mcp-config");
            }
        }
        var environment = new ValueMap<string, string>(ProviderChildEnvironment.Apply(configuration.Environment), SwiftStringComparer.Instance); // DEVIATION OD-0099 DL-6: only PATH changes; an unbound Storage (a test) leaves the environment as it came.
        if (configuration.NoTools) // ED-NEW-web-sec-artifacts-1
            environment = environment.Mutating(map => { foreach (var (name, value) in NoToolsEnvironment) map[name] = value; });
        var options = await KnownOptions(executable, environment); // :98
        if (!configuration.NoTools && options.Contains("--plugin-dir") && acquireSkills is not null) // ED-NEW-web-w26-1: no plugins without tools
        {
            generation = await Task.Run(acquireSkills);
            if (generation is not null)
            {
                if (!await Task.Run(generation.Verify)) throw new IOException("The generation failed whole-tree verification.");
                if (generation.Skills.Count == 0 && generation.PluginFolders.Count == 0) ReleaseSkills();
                else
                {
                    foreach (var skill in generation.Skills)
                        if (Marrow.Services.Port.Skills.Store.StorePaths.Comparer.Equals(skill.Folder, Path.Combine(generation.Folder, "skills", skill.Name)))
                            managedSkillItems["marrow:" + skill.Name] = skill.Id.Split('/')[0];
                    arguments.Add("--plugin-dir"); arguments.Add(generation.Folder);
                    foreach (var folder in generation.PluginFolders) { arguments.Add("--plugin-dir"); arguments.Add(folder); }
                }
            }
        }
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
                arguments.Add(HandshakeAgents(WithIgnisHelperModel(HydraPrompts.ClaudeAgents(hydra), Marrow.Core.Port.Advisor.ClaudeAdvisorModels.MainModel(configuration.Model))).CompactString); // DEVIATION (OD-0134 Ignis): Smart helpers moves only the scout entry to the helper model.
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
        // DEVIATION (OD-0134 Ignis, ED-NEW-ignis-runtime): no Swift counterpart. The prompt-cache lifetimes Ignis chose
        // for this launch go on the process environment once, so they cover every request outside the main
        // conversation of this CLI process: subagents at every depth, workflows and forks included.
        environment = WithIgnisCacheLifetimes(environment, configuration);
        if (handshakeAgent is not null && configuration.Hydra?.RunsNatively != true)
        {
            arguments.Add("--agents");
            arguments.Add(HandshakeAgents(JSONValue.FromDictionaryLiteral()).CompactString);
        }
        // Exactly one combined system prompt segment for every session, Hydra or not. :121-128
        arguments.Add("--append-system-prompt");
        arguments.Add(string.Join("\n\n", new[] { hydraPolicy, AntiSlopPolicy.Instructions(configuration.AntiSlopEnabled), SkillsMoved() }.OfType<string>()));
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

        var process = ClaudeProviderRuntime.Process.Current(executable.Path, arguments, configuration.WorkingDirectory.Path, environment); // :136-141
        this.process = process;
        process.Start(); // :143
        skillReadersExited = process.WaitForExit(); // RR-15: EOF is not exit
        if (generation is not null) RetainSkillsUntilExit(process);
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
        SwiftURL executable, SwiftURL directory, IReadOnlyDictionary<string, string> environment,
        Func<ClaudeSkillProbe?>? acquireSkills = null)
    {
        using var skills = acquireSkills is null ? null : await Task.Run(acquireSkills);
        environment = ProviderChildEnvironment.Apply(environment); // DL-6; fails closed with no bound Storage
        List<string> arguments = ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--setting-sources", "user,project,local"];
        if (skills?.HasContent == true && (await KnownOptions(executable, environment)).Contains("--plugin-dir"))
        {
            if (!await Task.Run(skills.Verify)) throw new IOException("A listing generation failed verification.");
            arguments.Add("--plugin-dir"); arguments.Add(skills.Folder);
            foreach (var folder in skills.PluginFolders) { arguments.Add("--plugin-dir"); arguments.Add(folder); }
        }
        var process = ClaudeProviderRuntime.Process.Current(
            executable.Path,
            arguments,
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
            if (skills is not null) await process.WaitForExit();
        }
    }

    // MARK: - Wire

    // Swift `private`; kept internal for this part's own tests (see the file header note): PR-PROVCLI-019's
    // "no process" half is proven by calling this directly on a fresh session, since start() itself cannot
    // reach the guard without first passing the executable check (PR-PROVCLI-001).
    //
    // PR-PROVCLI-442 (B3, new): the process/hasClosed guards run first, then controlCounter always advances and
    // the id is built, whether or not the caller ends up cancelled - so a cancelled-before-send caller still
    // uses up that id number (PendingReplies<JSONValue>.Register checks cancellation, :17 in the Swift source,
    // before storing the waiter or running send()). A caller cancelled while waiting has its entry removed and
    // is resumed with CancellationError; a control_response that arrives later for that id then finds nothing
    // waiting (Handle's control_response case, PR-PROVCLI-042) and is silently dropped, exactly like any other
    // unmatched id.
    internal async Task<JSONValue> Control(JSONValue request) // :301-309
    {
        if (process is not { } activeProcess) throw new ProviderError(new ProviderError.Case.NotRunning());
        if (hasClosed) throw new ProviderError(new ProviderError.Case.Failed(launchError ?? "Claude exited."));
        controlCounter += 1;
        // DEVIATION OD-0012 F1: upstream "droppy-code-<n>"; the port sends "marrow-<n>".
        var requestID = $"marrow-{controlCounter}";
        return await pendingControl.Register(requestID, () =>
        {
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
    internal void Handle(JSONValue message) // :330-384
    {
        if (RejectHandshakeAction(message)) return;
        var normalized = new List<ToolEvent>();
        skillEvents.Handle(message, normalized.Add, SkillItemId);
        // A head's message names the tool call that spawned it; the lead's carries null. :332
        var parent = message["parent_tool_use_id"]?.JSONValueString;
        switch (message["type"]?.JSONValueString)
        {
            case "control_response": // :334-341
                var response = message["response"] ?? new JSONValue.Null();
                if (response["request_id"]?.JSONValueString is not { } requestID) return;
                if (SwiftString.Equal(response["subtype"]?.JSONValueString ?? "", "error"))
                    pendingControl.Fail(requestID, new ProviderError(new ProviderError.Case.Failed(response["error"]?.JSONValueString ?? "Claude rejected the request.")));
                else
                    pendingControl.Resolve(requestID, response["response"] ?? new JSONValue.Null());
                break;
            case "control_request": // :342-343
                HandleControlRequest(message);
                break;
            case "control_cancel_request": // :344-347
                if (message["request_id"]?.JSONValueString is { } cancelID && pendingTools.Remove(cancelID))
                    OnEvent?.Invoke(new ProviderEvent.RequestResolved(cancelID));
                break;
            case "system": // :348-349
                HandleSystem(message);
                break;
            case "stream_event": // :350-360
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
            case "assistant": // :361-368
                if (parent is { } assistantParent)
                {
                    var stream = agentStreams.TryGetValue(assistantParent, out var headStream) ? headStream : new StreamState();
                    HandleAssistant(message, ref stream, AgentSink(assistantParent));
                    agentStreams[assistantParent] = stream;
                    IgnisHeadUsage(message, assistantParent); // DEVIATION (OD-0134 Ignis): the helper request's cache usage
                }
                else
                {
                    var stream = mainStream;
                    HandleAssistant(message, ref stream, LeadSink);
                    mainStream = stream;
                }
                break;
            case "user": // :369-370
                HandleToolResults(message, parent is { } toolResultParent ? AgentSink(toolResultParent) : LeadSink);
                break;
            case "result": // :371-372
                HandleResult(message);
                break;
            case "rate_limit_event": // :373-380
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
                else if (OverageHint(info) is { } onCredits) // DEVIATION (OD-0134 Ignis): paid-credits hint
                {
                    OnEvent?.Invoke(new ProviderEvent.CacheBilling(onCredits));
                }
                break;
            default:
                break;
        }
        // Preserve original provider event order; a held-command observer sees a registered request.
        foreach (var value in normalized) skillEvents.Emit(value, OnSkillEvent);
    }

    /// <summary>Where a stream's events go: straight out for the lead, wrapped for a head. Swift's lazily
    /// created leadSink (:449) is a single delegate invoke here; no laziness is needed.</summary>
    private void LeadSink(ProviderEvent providerEvent) => OnEvent?.Invoke(providerEvent);

    private Action<ProviderEvent> AgentSink(string agentID) => providerEvent => OnEvent?.Invoke(new ProviderEvent.AgentEvent(agentID, providerEvent)); // :451-453

    /// <summary>Swift `private`; kept internal for this part's own tests (see the file header note).</summary>
    internal void HandleControlRequest(JSONValue message) // :609-675
    {
        if (message["request_id"]?.JSONValueString is not { } requestID || message["request"] is not { } request) return; // :610
        if (!SwiftString.Equal(request["subtype"]?.JSONValueString ?? "", "can_use_tool")) // :611-616
        {
            process?.Send(JSONValue.FromDictionaryLiteral(
                ("type", "control_response"),
                ("response", JSONValue.FromDictionaryLiteral(
                    ("subtype", "error"),
                    ("request_id", requestID),
                    // DEVIATION OD-0012 F1: upstream "Droppy Code does not support this request."; the port
                    // renames the app to "Marrow" here, everywhere else unchanged.
                    ("error", "nth does not support this request.")))));
            return;
        }
        var name = request["tool_name"]?.JSONValueString ?? "Tool"; // :618
        var input = request["input"] ?? JSONValue.FromDictionaryLiteral(); // :619
        var toolUseID = request["tool_use_id"]?.JSONValueString; // :620
        pendingTools[requestID] = new PendingTool { Name = name, Input = input, Suggestions = request["permission_suggestions"] }; // :621
        // PORT-ONLY (Ignis Advisor, ED-NEW-ignisadvisor-2): nth's own ask_advisor runs without a card, as the skills relay's
        // read-only tools do on ACP (SkillsMcpBridge.AutomaticOption): it only asks the lead's model.
        if (advisorServer is not null && SwiftString.Equal(name, AdvisorToolName))
        {
            ResolveApproval(requestID, "allow");
            return;
        }
        // PORT-ONLY (web surfaces W12, ED-NEW-web-w12-1): nth-ui's artifact tools write only into this chat's own artifacts
        // (sandboxed, versioned, nothing lost), so they run without a card as the advisor's does.
        if (uiServer is not null && Web.UiMcpServer.IsOwnTool(name))
        {
            ResolveApproval(requestID, "allow");
            return;
        }
        // PORT-ONLY (Tactus, ED-NEW-pcctl-41): nth's own per-app approval governs the nth-pc tools (DESIGN.md 3.4, 4.1).
        if (pcServer is not null && Port.PcControl.PcProviderWiring.IsClaudeTool(name))
        {
            ResolveApproval(requestID, "allow");
            return;
        }

        switch (name)
        {
            case "AskUserQuestion": // :624-639
            {
                var questions = new List<QuestionRequest.Question>();
                foreach (var question in input["questions"]?.JSONValueArray ?? [])
                {
                    if (question["question"]?.JSONValueString is not { } prompt) continue;
                    var choices = new List<QuestionRequest.Choice>();
                    foreach (var option in question["options"]?.JSONValueArray ?? [])
                    {
                        if (option["label"]?.JSONValueString is { } label)
                            choices.Add(new QuestionRequest.Choice(label, option["description"]?.JSONValueString));
                    }
                    questions.Add(new QuestionRequest.Question(
                        prompt,
                        question["header"]?.JSONValueString ?? "",
                        prompt,
                        new ValueList<QuestionRequest.Choice>(choices),
                        question["multiSelect"]?.JSONValueBool ?? false,
                        true,
                        false));
                }
                OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(requestID, new ValueList<QuestionRequest.Question>(questions))));
                break;
            }
            case "ExitPlanMode": // :640-651
                OnEvent?.Invoke(new ProviderEvent.PlanCompleted(toolUseID ?? requestID, input["plan"]?.JSONValueString ?? ""));
                OnEvent?.Invoke(new ProviderEvent.Approval(new ApprovalRequest(
                    requestID,
                    ApprovalRequest.Kind.Plan,
                    "Implement this plan",
                    new ValueList<ApprovalRequest.Option>(
                    [
                        new ApprovalRequest.Option("allow", "Implement plan", ApprovalRequest.Option.Role.Approve),
                        new ApprovalRequest.Option("deny", "Keep planning", ApprovalRequest.Option.Role.Decline),
                    ]),
                    ToolItemID: toolUseID)));
                break;
            default: // :652-675 - the card shows the kind, title and detail; the edits' diffs are built once,
                     // when the assistant message lands, so withEdits is false here.
            {
                var call = ToolCall(name, input, withEdits: false) ?? new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Other, Title = name };
                var kind = call.ToolCallKind switch
                {
                    global::Marrow.Core.Models.ToolCall.Kind.Command => ApprovalRequest.Kind.Command,
                    global::Marrow.Core.Models.ToolCall.Kind.Edit => ApprovalRequest.Kind.FileChange,
                    _ => ApprovalRequest.Kind.Tool,
                };
                var options = new List<ApprovalRequest.Option> { new("allow", "Approve", ApprovalRequest.Option.Role.Approve) };
                if (request["permission_suggestions"]?.JSONValueArray is { Count: > 0 })
                    options.Add(new ApprovalRequest.Option("always", "Approve for session", ApprovalRequest.Option.Role.ApproveAlways));
                options.Add(new ApprovalRequest.Option("deny", "Decline", ApprovalRequest.Option.Role.Decline));
                OnEvent?.Invoke(new ProviderEvent.Approval(new ApprovalRequest(
                    requestID,
                    kind,
                    call.Title,
                    new ValueList<ApprovalRequest.Option>(options),
                    Detail: call.ToolCallKind == global::Marrow.Core.Models.ToolCall.Kind.Command ? request["description"]?.JSONValueString : call.Detail,
                    Reason: request["decision_reason"]?.JSONValueString,
                    ToolItemID: toolUseID)));
                break;
            }
        }
    }

    /// <summary>Swift `private`; kept internal for this part's own tests (see the file header note).</summary>
    internal void HandleStreamEvent(JSONValue @event, ref StreamState stream, Action<ProviderEvent> sink) // :455-495
    {
        switch (@event["type"]?.JSONValueString)
        {
            case "message_start": // :457-459
                stream.CurrentMessageID = @event["message"]?["id"]?.JSONValueString ?? SwiftUUID.UuidString(Guid.NewGuid());
                stream.BlockIDs.Clear();
                stream.LiveInputs.Clear();
                break;
            case "content_block_start": // :460-481
            {
                if (@event["index"]?.JSONValueInt is not { } indexLong || @event["content_block"] is not { } block) return;
                var index = indexLong;
                var messageID = stream.CurrentMessageID ?? "message";
                var id = $"{messageID}-{index}";
                switch (block["type"]?.JSONValueString)
                {
                    case "text": // :465-468
                        stream.BlockIDs[index] = id;
                        if (!stream.OpenText.TryGetValue(messageID, out var openTextList)) { openTextList = []; stream.OpenText[messageID] = openTextList; }
                        openTextList.Add(id);
                        sink(new ProviderEvent.MessageDelta(id, block["text"]?.JSONValueString ?? ""));
                        break;
                    case "thinking": // :469-472
                        stream.BlockIDs[index] = id;
                        if (!stream.OpenThinking.TryGetValue(messageID, out var openThinkingList)) { openThinkingList = []; stream.OpenThinking[messageID] = openThinkingList; }
                        openThinkingList.Add(id);
                        sink(new ProviderEvent.ReasoningDelta(id, block["thinking"]?.JSONValueString ?? ""));
                        break;
                    case "tool_use": // :473-478
                        if (block["id"]?.JSONValueString is not { } toolID || block["name"]?.JSONValueString is not { } name) return;
                        toolNames[toolID] = name;
                        // PORT-ONLY (W24): the arguments of a live-answer call are followed as they stream.
                        if (Web.UiMcpServer.IsOwnTool(name) && Web.LiveAnswerTools.IsName(name[("mcp__" + Web.UiMcpServer.ServerName + "__").Length..])) stream.LiveInputs[index] = (toolID, new System.Text.StringBuilder());
                        if (ToolCall(name, JSONValue.FromDictionaryLiteral()) is { } call)
                            sink(new ProviderEvent.ToolStarted(toolID, call) { McpTool = Web.UiMcpServer.ClaudeTool(name, uiServer is not null) }); // + ED-NEW-web-sec-rev-6
                        break;
                    default:
                        break;
                }
                break;
            }
            case "content_block_stop":
                if (@event["index"]?.JSONValueInt is { } stoppedIndex) stream.LiveInputs.Remove(stoppedIndex);
                break;
            case "content_block_delta": // :482-491
            {
                // PORT-ONLY (web surfaces W24, ED-NEW-web-w24-3): a piece of a live-answer call's arguments is reported on the call's row.
                if (@event["index"]?.JSONValueInt is { } liveIndex && stream.LiveInputs.TryGetValue(liveIndex, out var live)
                    && @event["delta"] is { } liveDelta && liveDelta["type"]?.JSONValueString == "input_json_delta")
                {
                    if (liveDelta["partial_json"]?.JSONValueString is { Length: > 0 } piece)
                    {
                        live.Json.Append(piece);
                        sink(new ProviderEvent.ToolUpdated(live.ToolId, new ToolUpdate { Detail = live.Json.ToString() }));
                    }
                    return;
                }
                if (@event["index"]?.JSONValueInt is not { } deltaIndexLong || !stream.BlockIDs.TryGetValue(deltaIndexLong, out var deltaID) || @event["delta"] is not { } delta) return;
                switch (delta["type"]?.JSONValueString)
                {
                    case "text_delta":
                        if (delta["text"]?.JSONValueString is { } text) sink(new ProviderEvent.MessageDelta(deltaID, text));
                        break;
                    case "thinking_delta":
                        if (delta["thinking"]?.JSONValueString is { } thinkingText) sink(new ProviderEvent.ReasoningDelta(deltaID, thinkingText));
                        break;
                    default:
                        break;
                }
                break;
            }
            default:
                break;
        }
    }

    // Text Claude Code puts in the transcript itself, under the model name "<synthetic>", where the model said
    // nothing. :497-501
    private const string SyntheticModel = "<synthetic>";
    private static readonly HashSet<string> SyntheticPlaceholders = new(SwiftStringComparer.Instance) { "No response requested.", "(no content)" }; // :501

    /// <summary>Swift `private`; kept internal for this part's own tests (see the file header note).</summary>
    internal void HandleAssistant(JSONValue message, ref StreamState stream, Action<ProviderEvent> sink) // :503-544
    {
        if (message["message"] is not { } body || body["content"]?.JSONValueArray is not { } content) return; // :504
        // Claude Code's own bookkeeping is never the model's words: a resumed session's synthetic
        // "No response requested." reply, or a synthetic API error, never becomes a chat message. :505-517
        if (body["model"]?.JSONValueString is { } modelName && SwiftString.Equal(modelName, SyntheticModel))
        {
            var texts = content
                .Where(item => SwiftString.Equal(item["type"]?.JSONValueString ?? "", "text"))
                .Select(item => item["text"]?.JSONValueString)
                .OfType<string>();
            var text = string.Join("\n", texts).Trimmed(); // :511-512
            if (message["isApiErrorMessage"]?.JSONValueBool == true && text.Length != 0 && !SyntheticPlaceholders.Contains(text)) // :513
                sink(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = text })); // :514
            return; // :516
        }
        var messageID = body["id"]?.JSONValueString ?? SwiftUUID.UuidString(Guid.NewGuid()); // :518
        if (message["uuid"]?.JSONValueString is { } uuid) sink(new ProviderEvent.AssistantMessageID(uuid)); // :519
        foreach (var block in content) // :520-543
        {
            switch (block["type"]?.JSONValueString)
            {
                case "text": // :522-525
                {
                    var text = block["text"]?.JSONValueString ?? "";
                    string id;
                    if (stream.OpenText.TryGetValue(messageID, out var openTextList) && openTextList.Count != 0)
                    {
                        id = openTextList[0];
                        openTextList.RemoveAt(0);
                    }
                    else
                    {
                        id = $"{messageID}-text-{SwiftUUID.UuidString(Guid.NewGuid())}";
                    }
                    sink(new ProviderEvent.MessageCompleted(id, text));
                    break;
                }
                case "thinking": // :526-529
                {
                    var text = block["thinking"]?.JSONValueString ?? "";
                    string id;
                    if (stream.OpenThinking.TryGetValue(messageID, out var openThinkingList) && openThinkingList.Count != 0)
                    {
                        id = openThinkingList[0];
                        openThinkingList.RemoveAt(0);
                    }
                    else
                    {
                        id = $"{messageID}-thinking-{SwiftUUID.UuidString(Guid.NewGuid())}";
                    }
                    sink(new ProviderEvent.ReasoningCompleted(id, text));
                    break;
                }
                case "tool_use": // :530-539
                {
                    if (block["id"]?.JSONValueString is not { } toolID || block["name"]?.JSONValueString is not { } name) continue;
                    var input = block["input"] ?? JSONValue.FromDictionaryLiteral();
                    toolNames[toolID] = name;
                    if (SwiftString.Equal(name, "TodoWrite"))
                    {
                        // PR-PROVCLI-063 (B3): the shared ACPSession.ParseTodoItems parser, not this file's own.
                        sink(new ProviderEvent.Todos(ACPSession.ParseTodoItems(input["todos"])));
                    }
                    else if (ToolCall(name, input) is { } call)
                    {
                        sink(new ProviderEvent.ToolStarted(toolID, call) { McpTool = Web.UiMcpServer.ClaudeTool(name, uiServer is not null) }); // + ED-NEW-web-sec-rev-6
                        sink(new ProviderEvent.ToolUpdated(toolID, new ToolUpdate { Title = call.Title, Detail = call.Detail, Edits = call.Edits, Kind = call.ToolCallKind }));
                    }
                    break;
                }
                default:
                    break;
            }
        }
    }

    /// <summary>Swift `private`; kept internal for this part's own tests (see the file header note).</summary>
    internal void HandleToolResults(JSONValue message, Action<ProviderEvent> sink) // :546-560
    {
        foreach (var block in message["message"]?["content"]?.JSONValueArray ?? [])
        {
            if (!SwiftString.Equal(block["type"]?.JSONValueString ?? "", "tool_result")) continue;
            if (block["tool_use_id"]?.JSONValueString is not { } toolID) continue;
            // The result closes the call, so its name is not needed again.
            var name = toolNames.Remove(toolID, out var removedName) ? removedName : "";
            if (HiddenResultTools.Contains(name)) continue;
            var isError = block["is_error"]?.JSONValueBool ?? false;
            var output = ToolText.Blocks(block["content"]); // PR-PROVCLI-053 (re-cited): the shared helper, not this file's own resultText(_:) (deleted at B3)
            var status = isError ? global::Marrow.Core.Models.ToolCall.Status.Failed : global::Marrow.Core.Models.ToolCall.Status.Completed;
            if (isError && (SwiftString.LocalizedCaseInsensitiveContains(output, "declined") || SwiftString.LocalizedCaseInsensitiveContains(output, "doesn't want")))
                status = global::Marrow.Core.Models.ToolCall.Status.Declined;
            sink(new ProviderEvent.ToolUpdated(toolID, new ToolUpdate { Output = output, Status = status }));
        }
    }

    /// <summary>Swift `private`; kept internal for this part's own tests (see the file header note).</summary>
    internal void HandleResult(JSONValue message) // :562-607
    {
        // Before the initialize is answered, a result is the launch's failure, not a turn's. :563-571
        if (!isStarted)
        {
            var launchErrors = string.Join("\n", (message["errors"]?.JSONValueArray ?? []).Select(item => item.JSONValueString).OfType<string>());
            if (launchErrors.Length != 0) launchError = launchErrors;
            return;
        }
        turnActive = false; // :572
        if (ContextUsageOf(message) is { } usage) // :573-585
        {
            OnEvent?.Invoke(new ProviderEvent.Usage(usage)); // :574
            // No head usage is added to it (:575-580): a head's own account stays on its panel.
            if (TurnSpendEvent(message, Marrow.Core.Port.Advisor.ClaudeAdvisorModels.MainModel(model)) is { } spend) // :581-584; ED-NEW-ignisadvisor-1: the model that ran
            {
                TokenLedger.Shared.Current.Record(spend.TotalTokens);
                OnEvent?.Invoke(new ProviderEvent.TokenSpend(spend));
            }
        }
        // DEVIATION (OD-0134 Ignis): the turn's cache-aware usage, iterations summed, for Ignis; no Swift counterpart.
        if (CacheUsageOf(message, Marrow.Core.Port.Advisor.ClaudeAdvisorModels.MainModel(model)) is { } cacheUsage) OnEvent?.Invoke(new ProviderEvent.CacheUsage(cacheUsage));
        foreach (var requestID in pendingTools.Keys.ToList()) OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :586
        pendingTools.Clear(); // :587
        toolNames.Clear(); // :589
        mainStream = new StreamState(); // :590 - the heads' streams stay; each goes with its own task_notification.
        var isError = message["is_error"]?.JSONValueBool ?? !SwiftString.Equal(message["subtype"]?.JSONValueString ?? "", "success"); // :594
        if (interruptRequested) // :595-596
        {
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
        }
        else if (isError) // :597-602
        {
            var errors = string.Join("\n", (message["errors"]?.JSONValueArray ?? []).Select(item => item.JSONValueString).OfType<string>());
            var text = message["result"]?.JSONValueString ?? errors;
            if (UsageLimitSignal.Matches(text)) OnEvent?.Invoke(new ProviderEvent.UsageLimit(UsageLimitSignal.ResetTime(text)));
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, text.Length == 0 ? "Claude stopped before finishing." : text));
        }
        else // :603-604
        {
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        }
        interruptRequested = false; // :606
    }

    /// <summary>Swift `private`; kept internal for this part's own tests. handleControlRequest, which would
    /// populate pendingTools from a live can_use_tool request, is a later part of this unit (see the file
    /// header note), so ResolveApproval/AnswerQuestion/Interrupt's own tests seed a pending tool directly.</summary>
    internal void SeedPendingTool(string requestID, string name, JSONValue input, JSONValue? suggestions = null) =>
        pendingTools[requestID] = new PendingTool { Name = name, Input = input, Suggestions = suggestions };

    /// <summary>Test seam for PR-PROVCLI-054/055's isStarted guard (:567, :49-51): Start() itself spawns a real
    /// child process, so HandleResult's own tests flip this flag directly instead.</summary>
    internal void MarkStarted() => isStarted = true;

    /// <summary>Test seam for PR-PROVCLI-055's interrupted branch (:595-596): turnActive (:46) is otherwise set
    /// only by writeUserMessage, which needs a live process.</summary>
    internal void MarkTurnActive() => turnActive = true;

    /// <summary>Test seam: launchError (:55) read back by PR-PROVCLI-054/061's own tests.</summary>
    internal string? LaunchError => launchError;

    internal void HandleSystem(JSONValue message) // :386-423
    {
        switch (message["subtype"]?.JSONValueString)
        {
            case "init": // :388-392
                if (handshakeAgent is not null && !VerifyHandshakeIdentity(message)) return;
                if (message["session_id"]?.JSONValueString is { } newSessionID &&
                    (sessionID is not { } currentSessionID || !SwiftString.Equal(newSessionID, currentSessionID)))
                {
                    sessionID = newSessionID;
                    OnEvent?.Invoke(new ProviderEvent.SessionReady(newSessionID));
                }
                break;
            case "compact_boundary": // :393-394
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." }));
                break;
            case "task_started": // :395-396
                HandleTaskStarted(message);
                break;
            case "task_progress": // :397-410
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
            case "task_notification": // :411-419
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
    private void HandleTaskStarted(JSONValue message) // :425-445
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

    private void WriteUserMessage(JSONValue content) // :311-321
    {
        turnActive = true; // :312
        interruptRequested = false; // :313
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null)); // :314
        process?.Send(JSONValue.FromDictionaryLiteral( // :315-320
            ("type", "user"),
            ("session_id", ""),
            ("message", JSONValue.FromDictionaryLiteral(("role", "user"), ("content", content))),
            ("parent_tool_use_id", JSONValue.FromNilLiteral())));
    }

    private void SendToolResponse(string requestID, JSONValue response) // :323-328
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

    private static JSONValue SessionScoped(JSONValue suggestion) // :814-818
    {
        if (suggestion is not JSONValue.Object suggestionObject) return suggestion;
        return new JSONValue.Object(suggestionObject.Value.Mutating(map => map["destination"] = "session"));
    }

    private void DidClose() // :678-690
    {
        hasClosed = true;
        if (process?.IsRunning != true) ReleaseSkills();
        skillEvents.Clear();
        var tail = process?.ErrorTail ?? "";
        // The result's own error names the failure better than the stderr tail can.
        var reason = launchError ?? TextCleanup.LastLines(tail);
        launchError = reason;
        pendingControl.FailAll(new ProviderError(new ProviderError.Case.Failed(reason ?? "Claude exited.")));
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
        if (generation is not null && !await Task.Run(generation.Verify) && !skillWarningShown)
        {
            skillWarningShown = true;
            OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = "An nth skill file changed during this chat; new chats are not affected" }));
        }
        if (handshakeAgent is not null) await CheckHandshakePolicyAsync();
        runtimeMode = input.RuntimeMode; // :216
        var mode = ModeName(input.RuntimeMode, input.InteractionMode); // :217
        if (!SwiftString.Equal(mode, permissionMode)) // :218-221
        {
            _ = await SwiftTry.OptionalAsync(async () => (JSONValue?)await Control(JSONValue.FromDictionaryLiteral(("subtype", "set_permission_mode"), ("mode", mode))));
            permissionMode = mode;
        }
        if (!NullableModelEqual(input.Model, model)) // :222-226
        {
            var wire = input.Model is { } requestedModel && SwiftString.Equal(requestedModel, "default") ? null : Marrow.Core.Port.Advisor.ClaudeAdvisorModels.MainModel(input.Model); // ED-NEW-ignisadvisor-1: an advisor entry switches to its main model (the advisor itself comes with the next launch)
            if (handshakeAgent is not null)
                await Control(JSONValue.FromDictionaryLiteral(("subtype", "set_model"), ("model", JSONValue.Optional(wire))));
            else
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
        if (handshakeAgent is not null && CanTransferContext?.Invoke() == false)
            throw new InvalidOperationException("Permission to transfer this conversation changed.");
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

    public Task Compact() => Compact(null); // :249-252

    // DEVIATION (OD-0134 Ignis): Swift's compact() takes no argument. Ignis's tidy-up passes its keep-N request as the
    // /compact instructions (KeepTurnsInstructions); null or empty writes the plain "/compact" exactly as Swift does.
    public Task Compact(string? instructions)
    {
        if (process is not { IsRunning: true }) throw new ProviderError(new ProviderError.Case.NotRunning()); // :250
        var command = instructions?.Trim() is { Length: > 0 } text ? "/compact " + text : "/compact";
        WriteUserMessage(new JSONValue.String(command)); // :251
        return Task.CompletedTask;
    }

    /// <summary>DEVIATION (OD-0134 Ignis): the /compact instructions for an Ignis tidy-up that keeps the last
    /// <paramref name="keepTurns"/> turns. The CLI decides the summary itself, so this is a request, not a guarantee.</summary>
    public static string KeepTurnsInstructions(int keepTurns) =>
        $"Keep the last {Math.Max(1, keepTurns).ToString(CultureInfo.InvariantCulture)} turns (each user message and everything after it) word for word, including their tool calls and results. Summarize only what came before them, keeping decisions, file paths and open tasks.";

    public void ResolveApproval(string requestID, string optionID) => ResolveApproval(requestID, optionID, null);

    // PORT-ONLY: SK-12 / DL-13 R1; the caller supplies the S1 denial reason, legacy declines keep their words.
    public void ResolveApproval(string requestID, string optionID, string? reason) // :254-275
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
            var message = reason ?? (SwiftString.Equal(pending.Name, "ExitPlanMode")
                ? "The user wants to keep planning. Wait for their feedback before changing anything."
                : "The user declined this action.");
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
        if (process is null) ReleaseSkills();
    }

    public async Task<bool> StopAgent(string id) // :294-297
    {
        if (!tasksByAgent.TryGetValue(id, out var taskID)) return false;
        return await SwiftTry.OptionalAsync(async () => (JSONValue?)await Control(JSONValue.FromDictionaryLiteral(("subtype", "stop_task"), ("task_id", taskID)))) is not null;
    }

    // Keep this immutable name/id snapshot after lease disposal: buffered transcript events may arrive after child exit.
    private string? SkillItemId(string skill) => managedSkillItems.GetValueOrDefault(skill);

    private string? SkillsMoved() => generation?.PreviousFolder is { } old
        ? $"nth's skills for this chat are now in {generation.Folder}. Paths under {old} earlier in this conversation no longer exist; use the same relative path under the new folder."
        : null;

    private void RetainSkillsUntilExit(IClaudeProcess child)
    {
        var leased = generation;
        MainActor.Spawn(async () =>
        {
            // stdout EOF is not process exit. Never let GC delete a root still read by a live child.
            await child.WaitForExit();
            leased?.Dispose();
            if (ReferenceEquals(generation, leased)) generation = null;
        });
    }

    private void ReleaseSkills() { generation?.Dispose(); generation = null; }

    // MARK: - Mapping

    /// <summary>Tools whose results are shown by the app itself, never as a tool row. :696-697</summary>
    private static readonly HashSet<string> HiddenResultTools = new(SwiftStringComparer.Instance)
        { "AskUserQuestion", "ExitPlanMode", "EnterPlanMode", "TodoWrite", "ToolSearch" };

    /// <summary>Swift `private`; kept internal for this part's own tests (see the file header note).</summary>
    internal global::Marrow.Core.Models.ToolCall? ToolCall(string name, JSONValue input, bool withEdits = true) // :699-757
    {
        string? Path(string key) => input[key]?.JSONValueString is { } value ? ToolTitles.RelativePath(value, WorkingDirectory) : null; // :700-702

        switch (name)
        {
            case "TodoWrite": case "AskUserQuestion": case "ExitPlanMode": case "EnterPlanMode": case "ToolSearch": // :704-705 (PR-PROVCLI-354)
                return null;
            case "Bash": // :706-707 (PR-PROVCLI-355)
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Command, Title = input["command"]?.JSONValueString ?? "Shell command", Detail = input["description"]?.JSONValueString };
            case "BashOutput": case "KillShell": case "KillBash": case "Monitor": case "TaskOutput": case "TaskStop": // :708-709 (PR-PROVCLI-356)
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Command, Title = name };
            case "Read": // :710-711 (PR-PROVCLI-357)
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Read, Title = Path("file_path") ?? "Read file" };
            case "Write": // :712-717 (PR-PROVCLI-358)
            {
                var call = new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Edit, Title = Path("file_path") ?? "Write file" };
                if (withEdits && Path("file_path") is { } writeFile && input["content"]?.JSONValueString is { } writeContent)
                    call = call with { Edits = new ValueList<FileEdit>([new FileEdit(writeFile, "", writeContent)]) };
                return call;
            }
            case "Edit": // :718-723 (PR-PROVCLI-359)
            {
                var call = new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Edit, Title = Path("file_path") ?? "Edit file" };
                if (withEdits && Path("file_path") is { } editFile)
                    call = call with { Edits = new ValueList<FileEdit>([new FileEdit(editFile, input["old_string"]?.JSONValueString ?? "", input["new_string"]?.JSONValueString ?? "")]) };
                return call;
            }
            case "MultiEdit": // :724-731 (PR-PROVCLI-360)
            {
                var call = new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Edit, Title = Path("file_path") ?? "Edit file" };
                if (withEdits && Path("file_path") is { } multiEditFile)
                {
                    var edits = (input["edits"]?.JSONValueArray ?? []).Select(entry =>
                        new FileEdit(multiEditFile, entry["old_string"]?.JSONValueString ?? "", entry["new_string"]?.JSONValueString ?? ""));
                    call = call with { Edits = new ValueList<FileEdit>(edits) };
                }
                return call;
            }
            case "NotebookEdit": // :732-733 (PR-PROVCLI-361)
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Edit, Title = Path("notebook_path") ?? "Edit notebook" };
            case "Glob": // :734-735
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Search, Title = input["pattern"]?.JSONValueString ?? "Find files", Detail = Path("path") };
            case "Grep": // :736-737
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Search, Title = input["pattern"]?.JSONValueString ?? "Search", Detail = Path("path") };
            case "LS": // :738-739
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Search, Title = Path("path") ?? "List files" };
            case "WebFetch": // :740-741
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Web, Title = input["url"]?.JSONValueString ?? "Fetch page" };
            case "WebSearch": // :742-743
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Web, Title = input["query"]?.JSONValueString ?? "Search the web" };
            case "Task": case "Agent": // :744-745
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Agent, Title = input["description"]?.JSONValueString ?? "Subagent", Detail = input["subagent_type"]?.JSONValueString };
            case "Skill": // :746-747
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Other, Title = $"Skill · {input["skill"]?.JSONValueString ?? input["command"]?.JSONValueString ?? ""}" }; // U+00B7 MIDDLE DOT
            default: // :748-755
                if (name.StartsWith("mcp__", StringComparison.Ordinal)) // :749
                {
                    // "__" is a two-character literal separator, outside SwiftString.Split's one-Character
                    // contract; string.Split on the literal substring is Swift's split(separator:) over a
                    // StringProtocol separator (component-wise, empty pieces dropped), same as R-STR-3's
                    // components(separatedBy:) mapping. UNVERIFIED O-13-style: no settled oracle item names
                    // this exact call, but no server or tool name in the fixtures needs a Unicode grapheme
                    // split here.
                    var parts = name.Split("__", StringSplitOptions.RemoveEmptyEntries); // :750
                    var server = parts.Length > 1 ? parts[1] : "MCP"; // :751
                    var tool = parts.Length > 2 ? string.Join("__", parts.Skip(2)) : name; // :752
                    return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Mcp, Title = $"{server} · {tool}", Detail = input.IsNull ? null : input.CompactString }; // :753, U+00B7 MIDDLE DOT
                }
                return new global::Marrow.Core.Models.ToolCall { ToolCallKind = global::Marrow.Core.Models.ToolCall.Kind.Other, Title = name }; // :755
        }
    }

    // Todos(JSONValue) and ResultText(JSONValue?) deleted at B3: todos(from:) and resultText(_:) left the Swift
    // source (old :765-775, :777-781) for the shared ACPSession.parseTodoItems and ToolText.blocks respectively
    // (PR-PROVCLI-063, PR-PROVCLI-053 re-cited); see the TodoWrite and HandleToolResults call sites above.

    /// <summary>The tokens the turn sent and received across its API calls, cache reads included, which is
    /// what Claude Code itself reports. Swift `private`; kept internal for this part's own tests.</summary>
    internal static long TurnSpend(JSONValue message) // :759-773
    {
        if (message["usage"] is not { } usage) return 0;
        static long Tokens(JSONValue? value) =>
            (value?["input_tokens"]?.JSONValueInt ?? 0)
            + (value?["cache_read_input_tokens"]?.JSONValueInt ?? 0)
            + (value?["cache_creation_input_tokens"]?.JSONValueInt ?? 0)
            + (value?["output_tokens"]?.JSONValueInt ?? 0);
        if (usage["iterations"]?.JSONValueArray is { Count: > 0 } iterations)
            return iterations.Aggregate(0L, (total, iteration) => total + Tokens(iteration));
        return Tokens(usage);
    }

    /// <summary>The turn's usage as spend. Swift `private`; kept internal for this part's own tests.</summary>
    internal static TokenSpend? TurnSpendEvent(JSONValue message, string? model) // :780-800
    {
        if (message["usage"] is not { } usage) return null;
        var total = TurnSpend(message);
        if (total <= 0) return null;
        var iterations = usage["iterations"]?.JSONValueArray ?? [];
        if (iterations.Count != 0) return new TokenSpend(ProviderKind.Claude, total, model: model);
        var cached = usage["cache_read_input_tokens"]?.JSONValueInt;
        long? input = usage["input_tokens"]?.JSONValueInt is { } inputTokens
            ? inputTokens + (cached ?? 0) + (usage["cache_creation_input_tokens"]?.JSONValueInt ?? 0)
            : null;
        return new TokenSpend(ProviderKind.Claude, total, inputTokens: input, outputTokens: usage["output_tokens"]?.JSONValueInt, cachedInputTokens: cached, model: model);
    }

    // DEVIATION (OD-0134 Ignis): no Swift counterpart. The result's usage as Ignis buckets: iterations[] summed when
    // present (as TurnSpend does), else the top-level usage. cache_creation's ephemeral_5m/1h_input_tokens fill the
    // 5-minute and 1-hour write buckets; any write the stream gave no TTL for stays in CacheWriteUnknownTtl. A usage
    // with neither cache_read_input_tokens nor cache_creation_input_tokens anywhere is "not reported".
    // ED-NEW-ignisadvisor-1: iterations of type "advisor_message" are the advisor's own requests (platform.claude.com advisor tool,
    // "Usage and billing": billed at the advisor model's rates and never inside the top-level usage). They are kept out of the
    // chat's buckets and handed on one by one, each with the model that advised, so Ignis prices them at that model's rates.
    internal const string AdvisorIteration = "advisor_message";

    internal static global::Marrow.Core.Port.Ignis.IgnisTurnUsage? CacheUsageOf(JSONValue message, string? model)
    {
        if (message["usage"] is not { IsNull: false } usage) return null;
        var iterations = usage["iterations"]?.JSONValueArray is { Count: > 0 } all ? all.ToList() : null;
        var executor = iterations?.Where(part => !IsAdvisorIteration(part)).ToList();
        var parts = executor is { Count: > 0 } ? executor : [usage];
        var context = ContextUsageOf(message);
        var (uncached, read, write5m, write1h, writeUnknown, output, reported) = UsageBuckets(parts);
        var advice = new ValueList<global::Marrow.Core.Port.Ignis.IgnisTurnUsage>((iterations ?? []).Where(IsAdvisorIteration).Select(AdvisorUsage));
        if (!reported)
            return global::Marrow.Core.Port.Ignis.IgnisTurnUsage.NotReported(ProviderKind.Claude, model, null, context?.UsedTokens, context?.WindowTokens)
                with { AdvisorCalls = advice };
        return new global::Marrow.Core.Port.Ignis.IgnisTurnUsage(ProviderKind.Claude, model, null, uncached, read, write5m, write1h, writeUnknown, output,
            context?.UsedTokens, context?.WindowTokens, true) { AdvisorCalls = advice };
    }

    private static bool IsAdvisorIteration(JSONValue part) => part["type"]?.JSONValueString is { } type && SwiftString.Equal(type, AdvisorIteration);

    private static global::Marrow.Core.Port.Ignis.IgnisTurnUsage AdvisorUsage(JSONValue part)
    {
        var (uncached, read, write5m, write1h, writeUnknown, output, reported) = UsageBuckets([part]);
        return new global::Marrow.Core.Port.Ignis.IgnisTurnUsage(ProviderKind.Claude, part["model"]?.JSONValueString, null, uncached, read, write5m, write1h, writeUnknown, output,
            null, null, reported);
    }

    // DEVIATION (ED-NEW-ignisadvisor-1): the launch an advisor entry gets. The version is read with the same `claude --version` probe
    // the context transfer uses, only for an advisor entry (every other launch starts no extra process); a failed or unreadable
    // probe passes no flag.
    private async Task<Marrow.Core.Port.Advisor.ClaudeAdvisorLaunch> AdvisorLaunch(Marrow.Core.Port.Advisor.ClaudeAdvisorModel entry, SwiftURL executable)
    {
        var environment = ProviderChildEnvironment.Apply(configuration.Environment);
        var probe = await SwiftTry.OptionalAsync<ShellResult>(async () => await ClaudeProviderRuntime.Version.Current(executable, environment));
        var version = probe is { Succeeded: true } ? System.Text.Encoding.UTF8.GetString(probe.Stdout) : null;
        var launch = Marrow.Core.Port.Advisor.ClaudeAdvisorModels.Launch(entry, version, environment);
        AdvisorGate = launch.Gate;
        return launch;
    }

    /// <summary>ED-NEW-ignisadvisor-1: the advisor gate of this session's launch; null when its model is not an advisor entry.</summary>
    internal Marrow.Core.Port.Advisor.ClaudeAdvisorGate? AdvisorGate { get; private set; }

    // DEVIATION (OD-0134 Ignis): no Swift counterpart. With Smart helpers on (IgnisSeams.HelperAgents bound and
    // answering a model), only the read-only scout entry of the --agents JSON gets the helper model; the worker and
    // every profile variant keep theirs. Unbound or null: the agents are returned unchanged.
    internal static JSONValue WithIgnisHelperModel(JSONValue agents, string? mainModel)
    {
        var helper = global::Marrow.Core.Port.Ignis.IgnisSeams.HelperAgents.Current(
            new global::Marrow.Core.Port.Ignis.IgnisHelperQuery(ProviderKind.Claude, mainModel));
        if (helper?.Trim() is not { Length: > 0 } helperModel) return agents;
        if (agents is not JSONValue.Object { Value: { } entries }
            || !entries.TryGetValue(HydraPrompts.ScoutAgentName, out var scoutValue)
            || scoutValue is not JSONValue.Object { Value: { } scoutFields }) return agents;
        var scout = new Dictionary<string, JSONValue>(scoutFields, SwiftStringComparer.Instance) { ["model"] = helperModel };
        var all = new Dictionary<string, JSONValue>(entries, SwiftStringComparer.Instance)
        {
            [HydraPrompts.ScoutAgentName] = new JSONValue.Object(new ValueMap<string, JSONValue>(scout, SwiftStringComparer.Instance)),
        };
        return new JSONValue.Object(new ValueMap<string, JSONValue>(all, SwiftStringComparer.Instance));
    }

    // DEVIATION (OD-0134 Ignis, ED-NEW-ignis-runtime): no Swift counterpart. Asks Ignis (IgnisSeams.CacheLifetimes) for this
    // launch's prompt-cache lifetimes and adds the variables it chose to the environment the CLI inherits. Unbound, no
    // choice, or a variable already present in any letter case: the environment is returned unchanged.
    internal static ValueMap<string, string> WithIgnisCacheLifetimes(ValueMap<string, string> environment, SessionConfiguration configuration)
    {
        var choice = global::Marrow.Core.Port.Ignis.IgnisSeams.CacheLifetimes.Current(new global::Marrow.Core.Port.Ignis.IgnisLifetimeQuery(
            ProviderKind.Claude, configuration.Model, configuration.IgnisThreadID, environment, configuration.WorkingDirectory.Path));
        if (choice is null) return environment;
        var add = choice.Environment
            .Where(pair => !environment.Keys.Any(key => string.Equals(key, pair.Key, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return add.Count == 0 ? environment : environment.Mutating(map => { foreach (var (key, value) in add) map[key] = value; });
    }

    // DEVIATION (OD-0134 Ignis, ED-NEW-ignis-runtime): no Swift counterpart. One helper request's cache usage, from an
    // assistant message whose parent_tool_use_id names the agent (subagents of subagents carry their own spawning tool
    // call, so every depth is its own agent). A message with several content blocks arrives once per block with the same
    // usage, so each message id is counted once. The message's own usage is that single request's.
    private readonly HashSet<string> ignisHeadMessages = new(StringComparer.Ordinal);
    private readonly Queue<string> ignisHeadMessageOrder = new();

    /// <summary>ENGINEERING DECISION: the message ids remembered for de-duplication per session.</summary>
    internal const int IgnisHeadMessageMemory = 4096;

    private void IgnisHeadUsage(JSONValue message, string agentID)
    {
        if (message["message"] is not { } body || body["id"]?.JSONValueString is not { Length: > 0 } id) return;
        if (HeadCacheUsageOf(message, agentID) is not { } usage) return;
        if (!ignisHeadMessages.Add(id)) return;
        ignisHeadMessageOrder.Enqueue(id);
        if (ignisHeadMessageOrder.Count > IgnisHeadMessageMemory) ignisHeadMessages.Remove(ignisHeadMessageOrder.Dequeue());
        OnEvent?.Invoke(new ProviderEvent.CacheUsage(usage));
    }

    internal static global::Marrow.Core.Port.Ignis.IgnisTurnUsage? HeadCacheUsageOf(JSONValue message, string agentID)
    {
        if (message["message"] is not { } body || body["usage"] is not { IsNull: false } usage) return null;
        var model = body["model"]?.JSONValueString;
        if (model is not null && SwiftString.Equal(model, SyntheticModel)) return null;
        var (uncached, read, write5m, write1h, writeUnknown, output, reported) = UsageBuckets([usage]);
        if (!reported) return null;
        var prompt = uncached + read + write5m + write1h + writeUnknown;
        return new global::Marrow.Core.Port.Ignis.IgnisTurnUsage(ProviderKind.Claude, model, agentID, uncached, read, write5m, write1h, writeUnknown, output,
            prompt, null, true);
    }

    private static (long Uncached, long Read, long Write5m, long Write1h, long WriteUnknown, long Output, bool Reported) UsageBuckets(IReadOnlyList<JSONValue> parts)
    {
        long uncached = 0, read = 0, write5m = 0, write1h = 0, writeUnknown = 0, output = 0;
        var reported = false;
        foreach (var part in parts)
        {
            uncached += part["input_tokens"]?.JSONValueInt ?? 0;
            output += part["output_tokens"]?.JSONValueInt ?? 0;
            if (part["cache_read_input_tokens"]?.JSONValueInt is { } partRead) { read += partRead; reported = true; }
            var written = part["cache_creation_input_tokens"]?.JSONValueInt;
            if (written is not null) reported = true;
            var fiveMinutes = part["cache_creation"]?["ephemeral_5m_input_tokens"]?.JSONValueInt ?? 0;
            var oneHour = part["cache_creation"]?["ephemeral_1h_input_tokens"]?.JSONValueInt ?? 0;
            if (fiveMinutes != 0 || oneHour != 0) reported = true;
            write5m += fiveMinutes;
            write1h += oneHour;
            writeUnknown += Math.Max(0, (written ?? 0) - fiveMinutes - oneHour);
        }
        return (uncached, read, write5m, write1h, writeUnknown, output, reported);
    }

    // DEVIATION (OD-0134 Ignis, ED-NEW-ignis-runtime): no Swift counterpart. Whether an allowed rate_limit_event says the
    // subscription now draws on paid usage credits (overage): rateLimitType / rate_limit_type "overage", or
    // isUsingOverage true. null: the event says nothing either way. UNRESOLVED: no single documented "on credits" flag;
    // these are the closest stream fields (SDK RateLimitInfo), so Ignis treats them as a hint the observed writes override.
    internal static bool? OverageHint(JSONValue info)
    {
        if (info["isUsingOverage"]?.JSONValueBool is { } usingOverage) return usingOverage;
        if (info["is_using_overage"]?.JSONValueBool is { } usingOverageSnake) return usingOverageSnake;
        var type = info["rateLimitType"]?.JSONValueString ?? info["rate_limit_type"]?.JSONValueString;
        return type is not null && SwiftString.Equal(type, "overage") ? true : null; // another limit type says nothing about credits
    }

    /// <summary>Swift `private`; kept internal for this part's own tests.</summary>
    internal static ContextUsage? ContextUsageOf(JSONValue message) // :802-812
    {
        if (message["usage"] is not { } usage) return null;
        // DEVIATION (ED-NEW-ignisadvisor-1): an advisor's own request is not the chat's size; the last executor request is.
        var last = usage["iterations"]?.JSONValueArray is { Count: > 0 } iterations ? iterations.LastOrDefault(part => !IsAdvisorIteration(part)) ?? usage : usage;
        var used = (last["input_tokens"]?.JSONValueInt ?? 0)
            + (last["cache_read_input_tokens"]?.JSONValueInt ?? 0)
            + (last["cache_creation_input_tokens"]?.JSONValueInt ?? 0)
            + (last["output_tokens"]?.JSONValueInt ?? 0);
        if (used <= 0) return null;
        var windows = message["modelUsage"]?.JSONValueObject?.Values
            .Select(value => value["contextWindow"]?.JSONValueInt)
            .OfType<long>()
            .ToList();
        long? window = windows is { Count: > 0 } ? windows.Max() : null;
        return new ContextUsage { UsedTokens = used, WindowTokens = window };
    }
}
