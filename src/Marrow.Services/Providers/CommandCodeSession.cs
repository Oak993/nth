using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/CommandCodeSession.swift (B2 1fbf81e), :1-... (900 lines).
//
// Unit Services.Providers.CommandCodeSession part 1 of 3 (PR-PROVCLI-204 to 227) implemented the
// launch path: transcriptExists (:115-124), Start (:98-113), Send's argument vector and environment
// build (:126-193), the prompt builder (:209-218), takesEffort (:220-227), Interrupt (:229-235),
// Compact (:237-239), AnswerQuestion (:253), Stop (:255-263), Handle's two-way dispatch (:267-284)
// and HandleEvent's run_start/message_start/text_delta/thinking_start/thinking_delta/thinking_end/
// message_end cases (:286-337).
//
// Part 2 (PR-PROVCLI-228 to 249, 394, 395) and part 3 (PR-PROVCLI-396 to 412, UI-PROVCLI-033) add:
// the rest of HandleEvent's switch (model_request_start/end, tool_queued/update/completed/errored/
// tool_hook_blocked/tool_denied, subagent_progress, session_titled, compaction_start/done, notice,
// run_error, interrupted, run_end, :338-431), DidClose's result/exit-code routing (:434-491),
// noteTurnCap/isRateLimit/friendlyError (:492-513), makeToolCall/finishEdit/snapshot (:548-649), the
// approval watcher and scanApprovals/reply/clearApprovalFiles/grantKey (:650-780) and ResolveApproval's
// body (:241-251). Their call sites exist below where Send/HandleEvent/Stop/DidClose need them
// (StartWatcher/StopWatcher, the tool_* HandleEvent cases, ResolveApproval) but are no-op placeholders
// here, never exercised by this part's own tests, so the type keeps compiling while those parts are
// pending.
public sealed class CommandCodeSession : ProviderSession
{
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
    private int turnCounter;                                // :43
    private int messageCounter;                             // :44
    private int thinkingCounter;                             // :45
    private string? currentMessageID;                        // :46
    private string? currentThinkingID;                       // :47
    private string messageText = "";                         // :48
    private string thinkingText = "";                         // :49
    private bool streamedText;                                // :50
    // activeModel (:51-52, "the model the CLI reported for the run") is set by model_request_start/end
    // and read by model_request_end's usage event: part 2 (PR-PROVCLI-228+) adds the field with them.
    /// <summary>Files a write_file is about to replace (part 2: tool_queued). :53-56</summary>
    private readonly Dictionary<string, string> editSnapshots = new(SwiftStringComparer.Instance);
    private readonly Dictionary<string, string> toolNames = new(SwiftStringComparer.Instance);
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

    /// <summary>Round trips one headless run may make before the CLI stops it. The CLI's own default is
    /// 100, short for a long agentic job. :77-79</summary>
    private const int MaxTurns = 400;

    public CommandCodeSession(SessionConfiguration configuration) // :81-88
    {
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
        var mod = CommandCodeAPI.InstallMod(); // :103
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
        return Task.FromResult(sessionID ?? ""); // :112
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
        streamedText = false; // :139
        editSnapshots.Clear(); // :140
        toolNames.Clear(); // :141
        toolInputs.Clear(); // :142

        var arguments = BuildArguments(isFinalReport, model, effort, interactionMode, runtimeMode, modURL, sessionID); // :144-166

        var environment = ApprovalsEnvironment(configuration.Environment, runtimeMode, approvalDirectory); // :168-173
        if (SwiftTry.Optional(MCPBridge.InstallBridge) is { } bridge) // :174-176
        {
            environment = SetEnvironment(environment, "DROPPY_CODE_MCP_BRIDGE", bridge.LocalPath);
        }
        if (File.Exists(MCPPaths.ClaudeConfigURL)) // :177-179
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

    public void ResolveApproval(string requestID, string optionID)
    {
        // PR-PROVCLI-241/242 (part 2): pendingApprovals lookup, sessionGrants and reply(to:allow:).
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
                messageCounter += 1;
                currentMessageID = $"cc-{launchID}-m{messageCounter}";
                messageText = "";
                streamedText = false;
                break;
            case "text_delta": // :298-306
            {
                var delta = @event["delta"]?.JSONValueString;
                if (string.IsNullOrEmpty(delta)) return;
                if (currentMessageID is null)
                {
                    messageCounter += 1;
                    currentMessageID = $"cc-{launchID}-m{messageCounter}";
                }
                streamedText = true;
                messageText += delta;
                OnEvent?.Invoke(new ProviderEvent.MessageDelta(currentMessageID, delta));
                break;
            }
            case "thinking_start": // :307-310
                thinkingCounter += 1;
                currentThinkingID = $"cc-{launchID}-r{thinkingCounter}";
                thinkingText = "";
                break;
            case "thinking_delta": // :311-318
            {
                var delta = @event["delta"]?.JSONValueString;
                if (string.IsNullOrEmpty(delta)) return;
                if (currentThinkingID is null)
                {
                    thinkingCounter += 1;
                    currentThinkingID = $"cc-{launchID}-r{thinkingCounter}";
                }
                thinkingText += delta;
                OnEvent?.Invoke(new ProviderEvent.ReasoningDelta(currentThinkingID, delta));
                break;
            }
            case "thinking_end": // :319-325
            {
                if (currentThinkingID is not { } id2) return;
                var text = @event["text"]?.JSONValueString ?? thinkingText;
                if (text.Trimmed().Length != 0) OnEvent?.Invoke(new ProviderEvent.ReasoningCompleted(id2, text));
                currentThinkingID = null;
                break;
            }
            case "message_end": // :326-337
            {
                var text = ContentText(@event["content"]);
                if (currentMessageID is { } id3)
                {
                    if (streamedText)
                    {
                        OnEvent?.Invoke(new ProviderEvent.MessageCompleted(id3, messageText.Length == 0 ? text : messageText));
                    }
                    else if (text.Trimmed().Length != 0)
                    {
                        // A reply that arrived whole rather than as deltas.
                        OnEvent?.Invoke(new ProviderEvent.MessageCompleted(id3, text));
                    }
                }
                currentMessageID = null;
                currentThinkingID = null;
                break;
            }
            // model_request_start, model_request_end, tool_queued, tool_update, tool_completed,
            // tool_errored, tool_hook_blocked, tool_denied, subagent_progress, session_titled,
            // compaction_start, compaction_done, notice, run_error, interrupted, run_end: part 2
            // (PR-PROVCLI-228 to 249) and part 3 (PR-PROVCLI-396 to 412). Falls to default until then,
            // as do the nine names the source itself ignores (turn_start, turn_end, message_update,
            // model_trace, tool_running, tool_hooks, tool_input_repaired, subagent_start,
            // subagent_stop). :427-429
            default: break;
        }
    }

    /// <summary>PR-PROVCLI-228+ (part 2): turn completion routing from the result line or the exit code,
    /// approval-file cleanup. Only the state reset every close needs (:435-442) is ported here so Send's
    /// readTask has somewhere safe to land.</summary>
    private void DidClose(int status, StdioProcess process)
    {
        if (!ReferenceEquals(this.process, process)) return; // :435
        this.process = null; // :436
        turnActive = false; // :437
        currentMessageID = null; // :438
        currentThinkingID = null; // :439
    }

    private static void StartWatcher(SwiftURL directory)
    {
        // PR-PROVCLI-... (part 2/3): the approval-directory filesystem watcher (:650-663).
    }

    private static void StopWatcher()
    {
        // PR-PROVCLI-... (part 2/3): cancels the watcher started above (:663-669).
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
        SwiftPath.AppendingPathComponent(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Marrow Code/CommandCode");

    /// <summary>A SwiftURL that round trips a real Windows path through .Path unchanged (ProviderRegistry's
    /// own FileUrl, same reasoning): a "file://" URL's .Path takes swift-foundation's POSIX branch, which
    /// this directory's own filesystem operations (Directory.CreateDirectory/Delete) cannot accept.</summary>
    private static SwiftURL NativePathUrl(string path) =>
        SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/')) ?? throw SwiftTraps.Fatal("Invalid Command Code approval path.");
}
