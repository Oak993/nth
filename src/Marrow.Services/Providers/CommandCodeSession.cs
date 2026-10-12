using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Port;
using Marrow.Services.Store;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/CommandCodeSession.swift (B3 52bc792), :1-723. Line citations
// below that are B2 (1fbf81e) parts 1-3's own history are left as B2 references for that history; every
// citation this B3 pass touched is re-pinned to the B3 file (:1-723) it now describes.
//
// Unit Services.Providers.CommandCodeSession part 1 of 3 (PR-PROVCLI-204 to 227) implemented the
// launch path: transcriptExists (:115-124), Start (:98-113), Send's argument vector and environment
// build (:126-193), the prompt builder (:209-218), takesEffort (:220-227), Interrupt (:229-235),
// Compact (:237-239), AnswerQuestion (:253), Stop (:255-263), Handle's two-way dispatch (:267-284)
// and HandleEvent's run_start/message_start/text_delta/thinking_start/thinking_delta/thinking_end/
// message_end cases (:286-337).
//
// Part 2 (PR-PROVCLI-228 to 249, 394, 395) added the rest of HandleEvent's switch
// (model_request_start/end, tool_queued/update/completed/errored/tool_hook_blocked/tool_denied,
// subagent_progress, session_titled, compaction_start/done, notice, run_error, interrupted, run_end,
// :338-431), DidClose's result/exit-code routing (:434-491), noteTurnCap/isRateLimit/friendlyError
// (:492-513), the whole of MakeToolCall/Humanize/FinishEdit/Snapshot (:548-649, including part 3's
// cases below), the approval watcher and ScanApprovals/Decide/AutoApproves/MakeApproval/GrantKey/
// Reply/ClearApprovalFiles (:650-780) and ResolveApproval's body (:241-251).
//
// Part 3 (PR-PROVCLI-396 to 412, UI-PROVCLI-033) is MakeToolCall's shell_output through the default
// mcp__/humanized-fallback cases (:560-618), already implemented as part of part 2's MakeToolCall
// above; this part adds the tests proving each of those cases against the Swift, plus the
// UI-PROVCLI-033 (OD-0012 F1 "Marrow" decline wording) coverage on Reply's deny branch (:749-762).
//
// B3 lane A24 (Services.Providers.CommandCodeSession: SessionSupport N7, ProviderSession A7) adopted the
// shared SessionSupport.cs types in place of this file's own copies (re-cites PR-PROVCLI-225/226/227/
// 229/230, behaviour unchanged): the launch/message/thinking id bookkeeping is now the shared
// StreamedTextTracker (:43, :78-81), and the write_file-before-snapshot bookkeeping is now the shared
// EditSnapshots (:47-49). tool_queued's todo_write case now calls the shared
// ACPSession.ParseTodoItems(input["todos"]) (:348; ACPSession.swift:688-699) instead of this file's own
// deleted todos(from:), re-citing PR-PROVCLI-243 (SPECIFIED -> IMPLEMENTED): a wider parser (four text
// keys incl. "step"; "complete"/"inProgress"/"in-progress" added). GrantKey moved to the shared
// ApprovalRequest.Kind.GrantKey extension (ProviderSession.cs :221-230, lane A7), re-citing
// PR-PROVCLI-248. FinishEdit now delegates to the shared EditSnapshots.Finish, which also fixes a B2
// defect: before/after were compared with C# ordinal == instead of the Swift `!=`'s canonical
// (NFC/NFD) equivalence (SwiftString.Equal).
public sealed class CommandCodeSession : ProviderSession, Marrow.Services.Port.Skills.Providers.ISkillSessionLifetime
{
    private Task skillReadersExited = Task.CompletedTask;
    public Task SkillReadersExited => skillReadersExited;

    /// <summary>The result line, kept until the process is gone so the turn closes in order. :60-64</summary>
    private sealed record RunResult
    {
        public required string Subtype { get; init; }
        public string? StopReason { get; init; }
        public string? Error { get; init; }
    }

    private readonly SessionConfiguration configuration; // :27
    private StdioProcess? process;                        // :28
    private TaskHandle? readTask;                          // :29
    private string? sessionID;                             // :30
    private bool started;                                  // :31
    private bool stopped;                                  // :32
    private RuntimeMode runtimeMode;                        // :33
    private InteractionMode interactionMode;                // :34
    private string? model;                                  // :35
    private string? effort;                                 // :36

    private bool turnActive;                                // :38
    // interruptRequested (:39) is set here (PR-PROVCLI-209, -219) and by the "interrupted" HandleEvent
    // case (part 2); DidClose (part 2, PR-PROVCLI-... :445) is its only reader, so it is unread by this
    // part's own code.
#pragma warning disable CS0414
    private bool interruptRequested;
#pragma warning restore CS0414
    private bool isFinalReport;                              // :40
    /// <summary>Namespaces ids per launch, so a resumed thread never streams into an old entry. :41-42</summary>
    private readonly string launchID = SwiftUUID.UuidString(Guid.NewGuid())[..8];
    /// <summary>Message and thinking ids for the current turn (SessionSupport.cs, shared with Pi and
    /// Antigravity; B3, re-cites PR-PROVCLI-225/226/227). Built in the constructor, not as a field
    /// initializer, because its closures capture <see cref="launchID"/>. :43, :78-81</summary>
    private readonly StreamedTextTracker stream;
    private int turnCounter;                                // :44
    /// <summary>The model the CLI reported for the run, for the context meter's ceiling. :45-46</summary>
    private string? activeModel;
    /// <summary>Files a write_file is about to replace, snapshotted as the call is queued (SessionSupport.cs,
    /// shared with Antigravity; B3, re-cites PR-PROVCLI-229/230). :47-49</summary>
    private readonly EditSnapshots editSnapshots = new();
    private readonly Dictionary<string, string> toolNames = new(SwiftStringComparer.Instance);
    private bool pcTools; // PORT-ONLY (Tactus, ED-NEW-pcctl-41): this launch was given the nth-pc server.
    private readonly Dictionary<string, JSONValue> toolInputs = new(SwiftStringComparer.Instance);

    private RunResult? runResult;                            // :66
    // runError (:67) is cleared here (PR-PROVCLI-209) and set by the "run_error" HandleEvent case (part
    // 2); DidClose (part 2) is its only reader, so it is unread by this part's own code.
#pragma warning disable CS0414
    private string? runError;
#pragma warning restore CS0414

    /// <summary>The gate: where the mod and the run's request files live. :70-75</summary>
    private SwiftURL? modURL;
    private SwiftURL? approvalDirectory;
    private FileWatcher? approvalWatcher;
    private readonly Dictionary<string, ApprovalRequest> pendingApprovals = new(SwiftStringComparer.Instance);
    /// <summary>Kinds approved for the rest of the session ("Approve for session"). :74-75</summary>
    private readonly HashSet<string> sessionGrants = new(SwiftStringComparer.Instance);

    /// <summary>Round trips one headless run may make before the CLI stops it. The CLI's own default is
    /// 100, short for a long agentic job. :77-79</summary>
    private const int MaxTurns = 400;

    // PORT-ONLY: SK-11e / WU-10 and DL-12. With a null launch, a null probe or an executable whose `--help` did not list --skill, the
    // argv is exactly BuildArguments' and the environment exactly today's.
    public Marrow.Services.Port.Skills.Providers.ProviderSkillLaunch? SkillLaunch { get; init; }
    public Marrow.Services.Port.Skills.Providers.CommandCodeSkillFlagProbe? SkillFlagProbe { get; init; }
    public Action<Marrow.Core.Port.Skills.Detect.ToolEvent>? ToolEvents { get; set; }
    private Marrow.Services.Port.Skills.Providers.CommandCodeToolEventTap? toolTap;
    private Marrow.Services.Port.Skills.Providers.CommandCodeToolEventTap Tap => toolTap ??= new(ToolEvents);

    /// <summary>The turn's argv with the bundle appended when allowed. Reads the probe's stored answer only; never runs a process.</summary>
    internal List<string> WithSkills(List<string> arguments, string executablePath) =>
        SkillLaunch is { } launch && SkillFlagProbe is { } probe && probe.Listed(executablePath)
            ? Marrow.Services.Port.Skills.Providers.ProviderSkillArguments.CommandCode(arguments, launch)
            : arguments;

    public CommandCodeSession(SessionConfiguration configuration) // :75-88
    {
        stream = new StreamedTextTracker( // :78-81
            messageID: n => $"cc-{launchID}-m{n}",
            thinkingID: n => $"cc-{launchID}-r{n}");
        this.configuration = configuration;
        runtimeMode = configuration.RuntimeMode;
        interactionMode = configuration.InteractionMode;
        model = configuration.Model;
        effort = configuration.Effort;
        sessionID = configuration.ResumeID is { Length: > 0 } resumeID ? resumeID : null;
    }

    public Action<ProviderEvent>? OnEvent { get; set; } // :25

    /// <summary>A session is a conversation, not a process: it stays up between turns and each turn
    /// spawns the CLI anew. :90-92</summary>
    public bool IsRunning => started && !stopped;

    private string WorkingDirectory => configuration.WorkingDirectory.Path; // :94

    // MARK: - Lifecycle (:96)

    public Task<string> Start() // :98-113
    {
        // Task-returning throws are deferred into the Task (Task.FromException), matching how every
        // caller (always through await) and this part's own tests observe them, rather than throwing
        // synchronously out of the call to Start() itself.
        if (configuration.Executable is null) // :99
            return Task.FromException<string>(new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider)));
        if (sessionID is { } id && !TranscriptExists(id, configuration.Environment)) // :100-102
        {
            return Task.FromException<string>(new ProviderError(new ProviderError.Case.Failed("Command Code no longer has this conversation; starting a new one.")));
        }
        // :98 `async throws`: a failure installing the mod or creating the approvals directory also reaches the caller
        // through the Task, as the guards above do, never as a throw out of the call itself.
        try { return Task.FromResult(Launch()); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return Task.FromException<string>(error); }
    }

    // :103-112 the part of start() after its guards.
    private string Launch()
    {
        var mod = CommandCodeAPI.InstallMod(LocalApplicationData()); // :103
        modURL = mod;
        // ENGINEERING DECISION, matching ProviderRegistry.FileUrl's own comment: CommandCodeAPI.SupportDirectory
        // is a "file://" URL, whose .Path forces the POSIX FileSystemPath() branch that prepends a slash before
        // a Windows drive letter ("/C:/x"), which Directory.CreateDirectory/Delete reject
        // (NotSupportedException). The directory this method actually creates and Stop deletes is built and
        // read as a plain Windows path string; ApprovalDirectoryPath below duplicates SupportDirectoryPath's
        // "Marrow Code/CommandCode" literal (DEVIATION OD-0008 C7, PR-PROVCLI-207), since CommandCodeAPI keeps
        // that string helper private.
        var directoryPath = SwiftPath.AppendingPathComponent(
            SwiftPath.AppendingPathComponent(ApprovalDirectoryRoot(), "approvals"),
            SwiftUUID.UuidString(Guid.NewGuid())); // :104-106
        Directory.CreateDirectory(directoryPath); // :107
        approvalDirectory = NativePathUrl(directoryPath); // :108
        StartWatcher(approvalDirectory); // :109 (part 2/3 body)
        started = true; // :110
        stopped = false; // :111
        return sessionID ?? ""; // :112
    }

    /// <summary>~/.commandcode/projects/&lt;project&gt;/&lt;session&gt;.jsonl, the transcript a resume
    /// reads. A session the CLI no longer has starts over instead of failing every turn. :115-124</summary>
    private static bool TranscriptExists(string sessionID, IReadOnlyDictionary<string, string> environment)
    {
        var home = environment.TryGetValue("HOME", out var raw) && raw.Length != 0 ? raw : LoginEnvironment.HomeDirectory; // :119
        var projects = SwiftPath.AppendingPathComponent(home, ".commandcode/projects"); // :120
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(projects); } // :122
        catch { return false; }
        var target = sessionID + ".jsonl";
        return entries.Any(entry => File.Exists(SwiftPath.AppendingPathComponent(entry, target))); // :123
    }

    public async Task Send(TurnInput input) // :126-207
    {
        if (!IsRunning || configuration.Executable is not { } executable) throw new ProviderError(new ProviderError.Case.NotRunning()); // :127
        if (turnActive) throw new ProviderError(new ProviderError.Case.Failed("Command Code is still on the last turn.")); // :128
        runtimeMode = input.RuntimeMode; // :129
        interactionMode = input.InteractionMode; // :130
        if (input.Model is { Length: > 0 } inputModel) model = inputModel; // :131
        effort = input.Effort; // :132
        isFinalReport = input.IsFinalReport; // :133
        turnCounter += 1; // :134
        turnActive = true; // :135
        interruptRequested = false; // :136
        runResult = null; // :137
        runError = null; // :138
        stream.ResetForTurn(); // :139
        editSnapshots.RemoveAll(); // :140
        toolNames.Clear(); // :141
        toolInputs.Clear(); // :142

        var arguments = WithSkills(BuildArguments(isFinalReport, model, effort, interactionMode, runtimeMode, modURL, sessionID), executable.Path); // :144-166 (+ PORT-ONLY SK-11e)

        var environment = ApprovalsEnvironment(Marrow.Services.Port.Skills.Providers.ProviderSkillEnvironment.Apply(configuration.Environment, SkillLaunch), runtimeMode, approvalDirectory); // :168-173 (+ PORT-ONLY SK-11e DL-6 PATH)
        if (SwiftTry.Optional(MCPBridge.InstallBridge) is { } bridge) // :174-176
        {
            environment = SetEnvironment(environment, "DROPPY_CODE_MCP_BRIDGE", bridge.LocalPath);
        }
        // PORT-ONLY (Tactus, ED-NEW-pcctl-41; DESIGN.md 3.4): with built-in servers (nth-pc while Tactus is on, a head's
        // advisor) the bridge reads the chat's own merged file; without, exactly as before.
        var builtIns = MCPProviderConfig.BuiltInServers(configuration);
        pcTools = builtIns.Any(server => server.Id == Port.PcControl.PcProviderWiring.ServerName);
        if (builtIns.Count > 0 && MCPProviderConfig.ClaudeBuiltInConfig(builtIns, configuration.IgnisThreadID) is { } merged && merged.Path != MCPPaths.ClaudeConfigURL)
        {
            environment = SetEnvironment(environment, "DROPPY_CODE_MCP_CONFIG", merged.Path);
        }
        else if (File.Exists(MCPPaths.ClaudeConfigURL)) // :177-179
        {
            environment = SetEnvironment(environment, "DROPPY_CODE_MCP_CONFIG", MCPPaths.ClaudeConfigURL);
        }

        var process = new StdioProcess(executable.Path, arguments, WorkingDirectory, environment); // :181-186
        this.process = process;
        try { process.Start(); } // :189
        catch (Exception error) // :190-192
        {
            turnActive = false;
            throw new ProviderError(new ProviderError.Case.Failed($"Command Code could not start: {error.Message}"));
        }
        skillReadersExited = Task.WhenAll(skillReadersExited, process.WaitForExit()); // RR-15: hold every per-turn reader
        process.Write(Encoding.UTF8.GetBytes(Prompt(input))); // :194
        process.CloseInput(); // :195
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null)); // :196

        var messages = process.Messages; // :198
        readTask = MainActor.Spawn(async () => // :199-206
        {
            await foreach (var message in messages) Handle(message);
            var status = await process.WaitForExit();
            DidClose(status, process);
        });
        await Task.CompletedTask;
    }

    /// <summary>The per-turn argument vector: the fixed base, the model/effort pair, plan mode or
    /// --yolo, the mod gate and the resume tail, in that order. :144-166</summary>
    private static List<string> BuildArguments(bool isFinalReport, string? model, string? effort,
        InteractionMode interactionMode, RuntimeMode runtimeMode, SwiftURL? modURL, string? sessionID)
    {
        var arguments = new List<string> // :144-152
        {
            "-p",
            "--output-format", "json",
            "--skip-onboarding",
            "--no-auto-update",
            "--trust",
            "--tools-enable", "todo_write",
            "--max-turns", (isFinalReport ? 1 : MaxTurns).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (model is { Length: > 0 } m) { arguments.Add("--model"); arguments.Add(m); } // :153
        if (effort is { Length: > 0 } e && TakesEffort(e, model)) { arguments.Add("--effort"); arguments.Add(e); } // :154
        if (interactionMode == InteractionMode.Plan) // :155-161
        {
            arguments.Add("--permission-mode");
            arguments.Add("plan");
        }
        else
        {
            // Print mode withholds writes and commands unless told otherwise; the mod below is what
            // asks the user.
            arguments.Add("--yolo");
        }
        if (modURL is { } mod &&
            (runtimeMode != RuntimeMode.FullAccess || interactionMode == InteractionMode.Plan || File.Exists(MCPPaths.ClaudeConfigURL))) // :162-165
        {
            arguments.Add("--mod");
            arguments.Add(mod.Path);
        }
        if (sessionID is { Length: > 0 } resume) { arguments.Add("--resume"); arguments.Add(resume); } // :166
        return arguments;
    }

    /// <summary>DROPPY_CODE_APPROVALS: the approval directory outside Full access, explicitly cleared
    /// (never left standing from an inherited value) in Full access, where the mod loads no gate.
    /// :168-173</summary>
    private static ValueMap<string, string> ApprovalsEnvironment(ValueMap<string, string> baseEnvironment,
        RuntimeMode runtimeMode, SwiftURL? approvalDirectory) =>
        runtimeMode != RuntimeMode.FullAccess && approvalDirectory is { } dir
            ? SetEnvironment(baseEnvironment, "DROPPY_CODE_APPROVALS", dir.Path)
            : RemoveEnvironment(baseEnvironment, "DROPPY_CODE_APPROVALS");

    /// <summary>Sets a key on a copy of the environment, first clearing any differently-cased inherited
    /// key so the removal below cannot be shadowed by one Windows treats as the same variable.</summary>
    private static ValueMap<string, string> SetEnvironment(ValueMap<string, string> environment, string key, string value) =>
        environment.Mutating(map =>
        {
            foreach (var existing in map.Keys.Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList()) map.Remove(existing);
            map[key] = value;
        });

    /// <summary>Removes a key from a copy of the environment. Windows environment keys are case
    /// insensitive, so a differently-cased inherited key is cleared too (windows_path, PR-PROVCLI-214).</summary>
    private static ValueMap<string, string> RemoveEnvironment(ValueMap<string, string> environment, string key) =>
        environment.Mutating(map =>
        {
            foreach (var existing in map.Keys.Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList()) map.Remove(existing);
        });

    /// <summary>The user's words, with any attachments named for read_file, which reads images. :209-218</summary>
    private static string Prompt(TurnInput input)
    {
        var text = input.Text; // :211
        var files = input.Images.Where(image => File.Exists(image.Path)).ToList(); // :212
        if (files.Count != 0) // :213-216
        {
            text += "\n\nAttached files (read them with read_file):\n";
            text += string.Join("\n", files.Select(file => $"- {file.Path}"));
        }
        return text; // :217
    }

    /// <summary>Whether the effort is one the model takes, per the CLI's table. The CLI refuses a run
    /// outright for a model with no adjustable effort ("LongCat 2.0 has no adjustable reasoning
    /// effort", exit 1), so an effort the table does not list for the model stays off the command
    /// line. :220-227</summary>
    private static bool TakesEffort(string effort, string? model)
    {
        if (model is null) return false; // :225
        return CommandCodeAPI.Efforts(model).Contains(effort); // :226
    }

    public async Task Interrupt() // :229-235
    {
        if (!turnActive) return; // :230
        interruptRequested = true; // :231
        // SIGTERM ends the run at once (exit 130); the CLI has already persisted every turn it
        // finished, so the next --resume picks up from there.
        process?.Terminate(); // :234
        await Task.CompletedTask;
    }

    public Task Compact() => // :237-239
        Task.FromException(new ProviderError(new ProviderError.Case.Failed("Command Code compacts its context on its own.")));

    public void ResolveApproval(string requestID, string optionID) // :241-251
    {
        if (!pendingApprovals.Remove(requestID, out var request)) return; // :242
        switch (optionID) // :243-249
        {
            case "allow":
            case "always":
                if (optionID == "always") sessionGrants.Add(request.RequestKind.GrantKey()); // :245
                Reply(requestID, allow: true); // :246
                break;
            default:
                Reply(requestID, allow: false); // :248
                break;
        }
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :250
    }

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers)
    {
        // The file-based gate protocol carries approvals only, so no question ever reaches the user
        // through this provider. :253
    }

    public void Stop() // :255-263
    {
        stopped = true; // :256
        turnActive = false; // :257
        process?.Terminate(); // :258
        StopWatcher(); // :259 (part 2/3 body)
        if (approvalDirectory is { } dir) SwiftTry.Ignore(() => Directory.Delete(dir.Path, recursive: true)); // :260-262
    }

    // MARK: - Stream (:265)

    private void Handle(JSONValue message) // :267-284
    {
        switch (message["type"]?.JSONValueString)
        {
            case "event": // :269-270
                HandleEvent(message["event"] ?? new JSONValue.Null());
                break;
            case "result": // :271-280
                runResult = new RunResult
                {
                    Subtype = message["subtype"]?.JSONValueString ?? "success",
                    StopReason = message["stopReason"]?.JSONValueString,
                    Error = ErrorText(message["error"]),
                };
                if (message["sessionId"]?.JSONValueString is { Length: > 0 } id && id != sessionID)
                {
                    sessionID = id;
                    OnEvent?.Invoke(new ProviderEvent.SessionReady(id));
                }
                break;
            default: break; // :281-282
        }
    }

    private void HandleEvent(JSONValue @event) // :286-431
    {
        switch (@event["type"]?.JSONValueString)
        {
            case "run_start": // :288-292
                if (@event["sessionId"]?.JSONValueString is { Length: > 0 } id && id != sessionID)
                {
                    sessionID = id;
                    OnEvent?.Invoke(new ProviderEvent.SessionReady(id));
                }
                break;
            case "message_start": // :293-297
                stream.StartMessage();
                break;
            case "text_delta": // :298-306
            {
                var delta = @event["delta"]?.JSONValueString;
                if (string.IsNullOrEmpty(delta)) return;
                OnEvent?.Invoke(new ProviderEvent.MessageDelta(stream.AppendMessage(delta), delta));
                break;
            }
            case "thinking_start": // :307-310
                stream.StartThinking();
                break;
            case "thinking_delta": // :311-318
            {
                var delta = @event["delta"]?.JSONValueString;
                if (string.IsNullOrEmpty(delta)) return;
                OnEvent?.Invoke(new ProviderEvent.ReasoningDelta(stream.AppendThinking(delta, resetsText: false), delta));
                break;
            }
            case "thinking_end": // :319-325
            {
                if (stream.CurrentThinkingID is not { } id2) return;
                var text = @event["text"]?.JSONValueString ?? stream.ThinkingText;
                if (text.Trimmed().Length != 0) OnEvent?.Invoke(new ProviderEvent.ReasoningCompleted(id2, text));
                stream.EndThinking();
                break;
            }
            case "message_end": // :326-337
            {
                var text = ContentText(@event["content"]);
                if (stream.CurrentMessageID is { } id3)
                {
                    if (stream.StreamedText)
                    {
                        OnEvent?.Invoke(new ProviderEvent.MessageCompleted(id3, stream.MessageText.Length == 0 ? text : stream.MessageText));
                    }
                    else if (text.Trimmed().Length != 0)
                    {
                        // A reply that arrived whole rather than as deltas.
                        OnEvent?.Invoke(new ProviderEvent.MessageCompleted(id3, text));
                    }
                }
                stream.EndMessage();
                stream.EndThinking();
                break;
            }
            case "model_request_start": // :338-339
                if (@event["model"]?.JSONValueString is { } startModel) activeModel = startModel;
                break;
            case "model_request_end": // :340-357
            {
                if (@event["model"]?.JSONValueString is { } endModel) activeModel = endModel;
                var usage = @event["usage"] ?? new JSONValue.Null();
                var input = usage["inputTokens"]?.JSONValueInt ?? 0;
                var output = usage["outputTokens"]?.JSONValueInt ?? 0;
                var used = input + output;
                if (used > 0)
                {
                    OnEvent?.Invoke(new ProviderEvent.Usage(new ContextUsage
                    {
                        UsedTokens = used,
                        WindowTokens = CommandCodeAPI.ContextWindow(activeModel ?? model),
                    }));
                    TokenLedger.Shared.Current.Record(used);
                    // One model request's own usage, so the amount needs no baseline.
                    OnEvent?.Invoke(new ProviderEvent.TokenSpend(new TokenSpend(
                        provider: ProviderKind.Commandcode,
                        totalTokens: used,
                        inputTokens: input,
                        outputTokens: output,
                        model: activeModel)));
                }
                break;
            }
            case "tool_queued": // :358-372
            {
                if (@event["toolCallId"]?.JSONValueString is not { } callId || @event["toolName"]?.JSONValueString is not { } name) return;
                var input = @event["input"] ?? new JSONValue.Null();
                toolNames[callId] = name;
                toolInputs[callId] = input;
                Tap.Queued(callId, name, input); // PORT-ONLY: SK-11e / DL-12
                if (name == "todo_write")
                {
                    OnEvent?.Invoke(new ProviderEvent.Todos(ACPSession.ParseTodoItems(input["todos"])));
                    return;
                }
                if (name == "write_file" && input["file_path"]?.JSONValueString is { } filePath)
                {
                    editSnapshots.Capture(callId, filePath);
                }
                OnEvent?.Invoke(new ProviderEvent.ToolStarted(callId, MakeToolCall(name, input, editSnapshots.Text(callId))));
                // The gate's request may already be on disk by the time the row exists.
                ScanApprovals();
                break;
            }
            case "tool_update": // :373-376
            {
                if (@event["toolCallId"]?.JSONValueString is not { } callId || IsTodoWrite(callId)) return;
                var text = ContentText(@event["partial"]);
                if (text.Length != 0) OnEvent?.Invoke(new ProviderEvent.ToolUpdated(callId, new ToolUpdate { Output = text }));
                break;
            }
            case "tool_completed": // :377-383
            {
                if (@event["toolCallId"]?.JSONValueString is not { } callId || IsTodoWrite(callId)) return;
                var text = ContentText(@event["result"]);
                Tap.Completed(callId, @event); // PORT-ONLY: SK-11e / DL-12
                OnEvent?.Invoke(new ProviderEvent.ToolUpdated(callId, new ToolUpdate
                {
                    Status = ToolCall.Status.Completed,
                    Output = text.Length == 0 ? null : text,
                    Edits = FinishEdit(callId),
                }));
                break;
            }
            case "tool_errored": // :384-387
            {
                if (@event["toolCallId"]?.JSONValueString is not { } callId || IsTodoWrite(callId)) return;
                var error = ErrorText(@event["error"]) ?? "The tool failed.";
                Tap.Errored(callId, @event); // PORT-ONLY: SK-11e / DL-12
                OnEvent?.Invoke(new ProviderEvent.ToolUpdated(callId, new ToolUpdate
                {
                    Output = error,
                    Status = ToolCall.Status.Failed,
                    Edits = FinishEdit(callId),
                }));
                break;
            }
            case "tool_hook_blocked": // :388-393
            case "tool_denied":
            {
                if (@event["toolCallId"]?.JSONValueString is not { } callId || IsTodoWrite(callId)) return;
                var reason = @event["hookOutput"]?.JSONValueString ?? @event["denyMessage"]?.JSONValueString ?? "Declined.";
                editSnapshots.Discard(callId);
                OnEvent?.Invoke(new ProviderEvent.ToolUpdated(callId, new ToolUpdate { Output = reason, Status = ToolCall.Status.Declined }));
                if (pendingApprovals.Remove(callId)) OnEvent?.Invoke(new ProviderEvent.RequestResolved(callId));
                break;
            }
            case "subagent_progress": // :394-399
            {
                if (@event["toolCallId"]?.JSONValueString is not { } callId || @event["toolName"]?.JSONValueString is not { } tool) return;
                var input = @event["toolInput"] ?? new JSONValue.Null();
                var subject = input["command"]?.JSONValueString ?? input["file_path"]?.JSONValueString ??
                    input["pattern"]?.JSONValueString ?? input["query"]?.JSONValueString ?? input["url"]?.JSONValueString;
                var line = subject is { } s ? $"{tool}  {TextCleanup.SingleLine(s, 100)}" : tool;
                OnEvent?.Invoke(new ProviderEvent.ToolOutput(callId, line + "\n"));
                break;
            }
            case "session_titled": // :400-401
                if (@event["title"]?.JSONValueString is { Length: > 0 } title) OnEvent?.Invoke(new ProviderEvent.Title(title));
                break;
            case "compaction_start": // :402-403
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Compacting context." }));
                break;
            case "compaction_done": // :404-407
            {
                var saved = @event["tokensSaved"]?.JSONValueInt ?? 0;
                // Int.formatted() (R-FMT-2): the ICU integer style in the current locale, not a fixed
                // .NET format string, so the grouping separator follows the platform locale as :406 does.
                var message = saved > 0
                    ? $"Compacted context, freeing {SwiftFormatters.Formatted(saved)} tokens."
                    : "Compacted context.";
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = message }));
                break;
            }
            case "notice": // :408-415
            {
                if (@event["message"]?.JSONValueString is not { Length: > 0 } message) return;
                var level = @event["level"]?.JSONValueString switch
                {
                    "error" => Notice.Level.Error,
                    "warning" or "warn" => Notice.Level.Warning,
                    _ => Notice.Level.Info,
                };
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = level, Message = message }));
                break;
            }
            case "run_error": // :416-417
                runError = ErrorText(@event["error"]);
                break;
            case "interrupted": // :418-419
                interruptRequested = true;
                break;
            case "run_end": // :420-425
            {
                var result = @event["result"] ?? new JSONValue.Null();
                var finalText = result["finalText"]?.JSONValueString ?? "";
                if (interactionMode == InteractionMode.Plan && finalText.Trimmed().Length != 0)
                {
                    OnEvent?.Invoke(new ProviderEvent.PlanCompleted($"cc-{launchID}-plan{turnCounter}", finalText));
                }
                break;
            }
            // turn_start, turn_end, message_update, model_trace, tool_running, tool_hooks,
            // tool_input_repaired, subagent_start and subagent_stop carry nothing more than what the
            // frames above already said. :427-429
            default: break;
        }
    }

    /// <summary>A frame for a call id whose recorded name is exactly "todo_write". :373 (shared guard)</summary>
    private bool IsTodoWrite(string id) => toolNames.TryGetValue(id, out var name) && name == "todo_write";

    /// <summary>PR-PROVCLI-235 to 238: turn completion routing from the result line or the exit code,
    /// approval-file cleanup. :434-489</summary>
    private void DidClose(int status, StdioProcess process)
    {
        if (!ReferenceEquals(this.process, process)) return; // :435
        this.process = null; // :436
        turnActive = false; // :437
        stream.ResetIDs(); // :438-439
        foreach (var id in pendingApprovals.Keys) OnEvent?.Invoke(new ProviderEvent.RequestResolved(id)); // :440
        pendingApprovals.Clear(); // :441
        ClearApprovalFiles(); // :442
        if (stopped) return; // :443

        if (interruptRequested || status == 130) // :445-448
        {
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
            return;
        }
        if (runResult is { } result) // :449-463
        {
            switch (result.Subtype)
            {
                case "success":
                    if (result.StopReason == "max_turns") NoteTurnCap();
                    OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                    break;
                case "max_turns":
                    NoteTurnCap();
                    OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                    break;
                default:
                {
                    var error = result.Error ?? runError ?? TextCleanup.LastLines(process.ErrorTail) ?? "Command Code stopped before finishing.";
                    if (IsRateLimit(error)) OnEvent?.Invoke(new ProviderEvent.UsageLimit(null));
                    OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, FriendlyError(error)));
                    break;
                }
            }
            return;
        }
        // No result line: the CLI died before it could write one. Its exit codes say why. :464-489
        var tail = TextCleanup.LastLines(process.ErrorTail);
        switch (status)
        {
            case 0:
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                break;
            case 3:
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed,
                    "Command Code is not signed in. Run `cmd login` in Terminal, or add a Studio API key in Settings → Providers → Command Code."));
                break;
            case 4:
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, tail ?? "Command Code was denied permission."));
                break;
            case 5:
                OnEvent?.Invoke(new ProviderEvent.UsageLimit(null));
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed,
                    tail ?? "Command Code is rate limited. Wait for the window to reset, or buy on-demand credits, which are never throttled."));
                break;
            case 6:
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "Could not reach Command Code. Check the connection and try again."));
                break;
            case 7:
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, tail ?? "Command Code's API returned a server error. Try again in a moment."));
                break;
            case 8:
                NoteTurnCap();
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                break;
            case 9:
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "The model produced no response."));
                break;
            case 10:
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed,
                    "Command Code is out of credits. Top up at commandcode.ai/billing, or wait for the monthly credits to reset."));
                break;
            default:
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed,
                    runError ?? tail ?? $"Command Code exited with code {status.ToString(System.Globalization.CultureInfo.InvariantCulture)}."));
                break;
        }
    }

    private void NoteTurnCap() // :492-495
    {
        if (isFinalReport) return;
        OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
        {
            NoticeLevel = Notice.Level.Warning,
            Message = $"Command Code stopped at its cap of {MaxTurns.ToString(System.Globalization.CultureInfo.InvariantCulture)} round trips for one message. Send another to carry on.",
        }));
    }

    private static bool IsRateLimit(string text) // :497-500
    {
        var lowered = SwiftString.Lowercased(text);
        return lowered.Contains("rate limit", StringComparison.Ordinal) ||
            lowered.Contains("usage exceeded", StringComparison.Ordinal) ||
            lowered.Contains("window limit", StringComparison.Ordinal);
    }

    /// <summary>The CLI's wording for the account errors, made actionable. :502-512</summary>
    private static string FriendlyError(string text)
    {
        var lowered = SwiftString.Lowercased(text);
        if (lowered.Contains("not authenticated", StringComparison.Ordinal) ||
            lowered.Contains("unauthorized", StringComparison.Ordinal) ||
            lowered.Contains("session expired", StringComparison.Ordinal))
        {
            return "Command Code is not signed in. Run `cmd login` in Terminal, or add a Studio API key in Settings → Providers → Command Code.";
        }
        if (lowered.Contains("insufficient credits", StringComparison.Ordinal) || lowered.Contains("out of credits", StringComparison.Ordinal))
        {
            return "Command Code is out of credits. Top up at commandcode.ai/billing, or wait for the monthly credits to reset.";
        }
        return text;
    }

    // Deleted at B3: todos(from:) is gone from the Swift; tool_queued's todo_write case now calls the
    // shared ACPSession.ParseTodoItems(input["todos"]) directly (see the "tool_queued" case above,
    // PR-PROVCLI-243).

    // MARK: - Tools (:546)

    /// <summary>:548-620</summary>
    private ToolCall MakeToolCall(string name, JSONValue input, string? snapshot)
    {
        string? Path(string key) => input[key]?.JSONValueString is { } path ? ToolTitles.RelativePath(path, WorkingDirectory) : null;

        switch (name)
        {
            case "shell_command":
            case "powershell":
            {
                var command = input["command"]?.JSONValueString is { } raw ? ToolTitles.UnwrapShell(raw) : "Shell command";
                var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = command, Detail = input["description"]?.JSONValueString };
                if (input["run_in_background"]?.JSONValueBool == true)
                {
                    call = call with
                    {
                        Detail = string.Join(" · ", new[] { call.Detail, "in the background" }.Where(part => part is not null)),
                    };
                }
                return call;
            }
            case "monitor_command":
                return new ToolCall
                {
                    ToolCallKind = ToolCall.Kind.Command,
                    Title = input["command"]?.JSONValueString is { } monitor ? ToolTitles.UnwrapShell(monitor) : "Monitor command",
                };
            case "shell_output":
                return new ToolCall
                {
                    ToolCallKind = ToolCall.Kind.Command, Title = "Shell output",
                    Detail = input["id"]?.JSONValueString ?? input["task_id"]?.JSONValueString,
                };
            case "shell_tasks":
                return new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Shell tasks" };
            case "kill_shell":
                return new ToolCall
                {
                    ToolCallKind = ToolCall.Kind.Command, Title = "Stop shell",
                    Detail = input["id"]?.JSONValueString ?? input["task_id"]?.JSONValueString ??
                        input["pid"]?.JSONValueInt?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
            case "read_file":
            {
                var call = new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = Path("file_path") ?? Path("path") ?? "Read file" };
                if (input["offset"]?.JSONValueInt is { } offset && input["limit"]?.JSONValueInt is { } limit)
                {
                    call = call with { Detail = $"lines {offset}–{offset + limit}" };
                }
                return call;
            }
            case "read_directory":
                return new ToolCall { ToolCallKind = ToolCall.Kind.Search, Title = Path("path") ?? "List files" };
            case "write_file":
            {
                var call = new ToolCall { ToolCallKind = ToolCall.Kind.Edit, Title = Path("file_path") ?? "Write file" };
                if (Path("file_path") is { } file && input["content"]?.JSONValueString is { } content)
                {
                    call = call with { Edits = new ValueList<FileEdit>([new FileEdit(file, snapshot ?? "", content)]) };
                }
                return call;
            }
            case "edit_file":
            {
                var call = new ToolCall { ToolCallKind = ToolCall.Kind.Edit, Title = Path("file_path") ?? Path("path") ?? "Edit file" };
                if ((Path("file_path") ?? Path("path")) is { } file)
                {
                    var oldText = input["old_string"]?.JSONValueString ?? input["old_text"]?.JSONValueString ?? input["old"]?.JSONValueString ?? "";
                    var newText = input["new_string"]?.JSONValueString ?? input["new_text"]?.JSONValueString ?? input["new"]?.JSONValueString ?? "";
                    call = call with { Edits = new ValueList<FileEdit>([new FileEdit(file, oldText, newText)]) };
                }
                return call;
            }
            case "glob":
                return new ToolCall { ToolCallKind = ToolCall.Kind.Search, Title = input["pattern"]?.JSONValueString ?? "Find files", Detail = Path("path") };
            case "grep":
            {
                var scope = string.Join(" · ", new[] { Path("path"), input["glob"]?.JSONValueString }.Where(part => part is not null));
                return new ToolCall
                {
                    ToolCallKind = ToolCall.Kind.Search, Title = input["pattern"]?.JSONValueString ?? "Search",
                    Detail = scope.Length == 0 ? null : scope,
                };
            }
            case "web_fetch":
                return new ToolCall { ToolCallKind = ToolCall.Kind.Web, Title = input["url"]?.JSONValueString ?? "Fetch page" };
            case "web_search":
                return new ToolCall { ToolCallKind = ToolCall.Kind.Web, Title = input["query"]?.JSONValueString ?? "Search the web" };
            case "agent":
            {
                var description = input["description"]?.JSONValueString ??
                    (input["prompt"]?.JSONValueString is { } prompt ? TextCleanup.SingleLine(prompt, 96) : "Sub-agent");
                return new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = description, Detail = input["subagent_type"]?.JSONValueString };
            }
            case "agent_output":
                return new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Agent output", Detail = input["agent_id"]?.JSONValueString };
            case "activate_skill":
                return new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = $"Skill · {input["name"]?.JSONValueString ?? ""}" };
            case "enter_worktree":
            case "exit_worktree":
                return new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = Humanize(name), Detail = input["name"]?.JSONValueString };
            case "run_command":
                return new ToolCall
                {
                    ToolCallKind = ToolCall.Kind.Other,
                    Title = $"Command · {input["command"]?.JSONValueString ?? input["name"]?.JSONValueString ?? ""}",
                };
            default:
                if (name.StartsWith("mcp__", StringComparison.Ordinal))
                {
                    var parts = name.Split("__", StringSplitOptions.None);
                    var nonEmpty = parts.Where(part => part.Length != 0).ToArray();
                    var server = nonEmpty.Length > 1 ? nonEmpty[1] : "MCP";
                    var tool = nonEmpty.Length > 2 ? string.Join("__", nonEmpty.Skip(2)) : name;
                    return new ToolCall
                    {
                        ToolCallKind = ToolCall.Kind.Mcp, Title = $"{server} · {tool}",
                        Detail = input.IsNull ? null : input.CompactString,
                    };
                }
                var fallback = new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = Humanize(name) };
                var detail = input.IsNull ? null : input.CompactString;
                if (detail is { Length: > 0 } && detail != "{}" && detail.Length <= 300) fallback = fallback with { Detail = detail };
                return fallback;
        }
    }

    /// <summary>"Task create" from "task_create". :622-627</summary>
    private static string Humanize(string name)
    {
        var words = name.Split('_').Where(word => word.Length != 0).ToList();
        if (words.Count == 0) return name;
        var first = words[0];
        return string.Join(" ", new[] { SwiftString.Uppercased(first[..1]) + first[1..] }.Concat(words.Skip(1)));
    }

    /// <summary>A finished write as the file before against after, from the snapshot taken as the call was
    /// queued; nil for anything else, so the row's edits stay as queued. Delegates to the shared
    /// EditSnapshots.Finish (SessionSupport.cs), which also fixes the B2 defect where this compared
    /// before/after with ordinal ==: it now uses SwiftString.Equal's canonical equivalence, matching the
    /// Swift `!=`. :600-605</summary>
    private ValueList<FileEdit>? FinishEdit(string id)
    {
        if (toolInputs.GetValueOrDefault(id)?["file_path"]?.JSONValueString is not { } path) return null;
        return editSnapshots.Finish(id, path, WorkingDirectory);
    }

    // MARK: - The gate (:648)

    private void StartWatcher(SwiftURL directory) // :650-661
    {
        StopWatcher();
        approvalWatcher = FileWatcher.WatchDirectory(directory.Path, ScanApprovals);
    }

    private void StopWatcher() // :663-666
    {
        approvalWatcher?.Cancel();
        approvalWatcher = null;
    }

    /// <summary>Picks up every request the mod has written and either answers it from the mode and the
    /// session's grants or asks the user. :668-681</summary>
    private void ScanApprovals()
    {
        if (approvalDirectory is not { } directory || !turnActive) return;
        string[] files;
        try { files = Directory.GetFiles(directory.Path); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var file in files
            .Where(file => SwiftPath.LastPathComponent(file).EndsWith(".request.json", StringComparison.Ordinal))
            .OrderBy(SwiftPath.LastPathComponent, StringComparer.Ordinal))
        {
            JSONValue? request;
            try { request = JSONValue.Parse(File.ReadAllBytes(file)); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            if (request is null) continue;
            SwiftTry.Ignore(() => FileOps.RemoveItem(file));
            var fileName = SwiftPath.LastPathComponent(file);
            var id = request["toolCallId"]?.JSONValueString ?? fileName[..^".request.json".Length];
            if (pendingApprovals.ContainsKey(id)) continue;
            Decide(id, request);
        }
    }

    private void Decide(string id, JSONValue request) // :683-693
    {
        var name = request["toolName"]?.JSONValueString ?? "";
        var input = request["input"] ?? new JSONValue.Null();
        var approval = MakeApproval(id, name, input);
        // PORT-ONLY (Tactus, ED-NEW-pcctl-41): nth's own per-app approval governs the nth-pc tools (DESIGN.md 3.4, 4.1).
        if (AutoApproves(name, input) || sessionGrants.Contains(approval.RequestKind.GrantKey()) || (pcTools && Port.PcControl.PcProviderWiring.IsClaudeTool(name)))
        {
            Reply(id, allow: true);
            return;
        }
        pendingApprovals[id] = approval;
        Tap.Held(name, input); // PORT-ONLY: SK-11e / DL-12
        OnEvent?.Invoke(new ProviderEvent.Approval(approval));
    }

    /// <summary>What the mode lets through without asking. Full access never reaches here: it loads no
    /// gate. :695-717</summary>
    private bool AutoApproves(string name, JSONValue input)
    {
        var isEdit = name == "write_file" || name == "edit_file";
        switch (runtimeMode)
        {
            case RuntimeMode.FullAccess: return true;
            case RuntimeMode.Supervised: return false;
            case RuntimeMode.AutoAcceptEdits: return isEdit;
            case RuntimeMode.Auto:
                if (isEdit) return true;
                return name switch
                {
                    "shell_command" or "powershell" or "monitor_command" => !ShellCommandKind.MayWriteFiles(input["command"]?.JSONValueString ?? ""),
                    "kill_shell" or "cron_create" or "cron_delete" or "task_stop" => true,
                    _ => false,
                };
            default: return false;
        }
    }

    private ApprovalRequest MakeApproval(string id, string name, JSONValue input) // :719-739
    {
        var call = MakeToolCall(name, input, editSnapshots.Text(id));
        var kind = call.ToolCallKind switch
        {
            ToolCall.Kind.Command => ApprovalRequest.Kind.Command,
            ToolCall.Kind.Edit => ApprovalRequest.Kind.FileChange,
            _ => ApprovalRequest.Kind.Tool,
        };
        var options = new ValueList<ApprovalRequest.Option>(
        [
            new("allow", "Approve", ApprovalRequest.Option.Role.Approve),
            new("always", "Approve for session", ApprovalRequest.Option.Role.ApproveAlways),
            new("deny", "Decline", ApprovalRequest.Option.Role.Decline),
        ]);
        return new ApprovalRequest(id, kind, call.Title, options, Detail: call.Detail)
        {
            ToolItemID = toolNames.ContainsKey(id) ? id : null,
        };
    }

    // Deleted at B3: grantKey moved out to the shared ApprovalRequest.Kind.GrantKey extension
    // (ProviderSession.cs :221-230, lane A7), used at ResolveApproval and Decide above.

    private void Reply(string id, bool allow) // :749-762
    {
        if (approvalDirectory is not { } directory) return;
        JSONValue reply = allow
            ? JSONValue.FromDictionaryLiteral(("decision", "allow"))
            : JSONValue.FromDictionaryLiteral(
                ("decision", "deny"),
                // DEVIATION OD-0012 F1: upstream reads "The user declined this in Droppy Code. ...".
                ("reason", "The user declined this in nth. Do not retry it; take another approach or explain what you would need."));
        var file = SwiftPath.AppendingPathComponent(directory.Path, $"{id}.reply.json");
        var temporary = SwiftPath.AppendingPathComponent(directory.Path, $"{id}.reply.json.tmp");
        try
        {
            FileOps.WriteAtomic(temporary, reply.Data());
            FileOps.ReplaceItemAt(file, temporary);
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            SwiftTry.Ignore(() => FileOps.WriteAtomic(file, reply.Data()));
        }
    }

    /// <summary>Requests and replies a finished run left behind, so a stale one never answers a later
    /// call. :764-770</summary>
    private void ClearApprovalFiles()
    {
        if (approvalDirectory is not { } directory) return;
        string[] files;
        try { files = Directory.GetFiles(directory.Path); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        foreach (var file in files) SwiftTry.Ignore(() => FileOps.RemoveItem(file));
    }

    /// <summary>The text of a content-block array, as tool results and messages carry it. :523-533</summary>
    private static string ContentText(JSONValue? value)
    {
        if (value is null || value.IsNull) return "";
        if (value.JSONValueString is { } text) return text;
        if (value.JSONValueArray is not { } blocks) return "";
        return string.Join("\n", blocks
            .Select(block => block["text"]?.JSONValueString ?? (block["type"]?.JSONValueString == "image" ? "[image]" : null))
            .Where(line => line is not null));
    }

    private static string? ErrorText(JSONValue? value) // :514-521
    {
        if (value is null || value.IsNull) return null;
        if (value.JSONValueString is { } text) return text.Length == 0 ? null : text;
        if (value["message"]?.JSONValueString is { Length: > 0 } message) return message;
        var compact = value.CompactString;
        return compact == "{}" ? null : compact;
    }

    /// <summary>CommandCodeAPI.SupportDirectoryPath's own literal (:443, DEVIATION OD-0008 C7), duplicated
    /// here as a plain string because that helper is private. PORT-ONLY test seam.</summary>
    internal static string ApprovalDirectoryRoot() =>
        SwiftPath.AppendingPathComponent(LocalApplicationData(), AppInfo.DataFolderName + "/CommandCode"); // ED-212; OD-0135

    /// <summary>PORT-ONLY test seam (R-TEST-9): the OS app-data folder the mod and the approval folders go under. Empty
    /// (always, outside tests) is DevDataRoot's own answer. App tests start Command Code sessions through ThreadRuntime,
    /// where MARROW_DEV_DATA_ROOT is not set, so AppModelHarness points this at its scratch folder; without it a test
    /// wrote %LOCALAPPDATA%\nth\CommandCode on the developer's PC.</summary>
    internal static Ambient<string> LocalApplicationDataForTesting { get; } = new(string.Empty);

    private static string LocalApplicationData() =>
        LocalApplicationDataForTesting.Current is { Length: > 0 } root ? root : DevDataRoot.LocalApplicationData();

    /// <summary>A SwiftURL that round trips a real Windows path through .Path unchanged (ProviderRegistry's
    /// own FileUrl, same reasoning): a "file://" URL's .Path takes swift-foundation's POSIX branch, which
    /// this directory's own filesystem operations (Directory.CreateDirectory/Delete) cannot accept.</summary>
    private static SwiftURL NativePathUrl(string path) =>
        SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/')) ?? throw SwiftTraps.Fatal("Invalid Command Code approval path.");
}
