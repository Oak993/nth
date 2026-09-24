using System.Diagnostics;
using System.Linq;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;

// Source: DroppyCode/Services/Providers/PiSession.swift (B2 1fbf81e), :1-654. The whole file: the lifecycle,
// argv/environment building, the get_state/get_commands handshake, send/interrupt/compact/stop, approvals and
// question answers, the request/response wire, the gate file, the message/tool event stream and its helpers.
namespace Marrow.Services.Providers;

// :19-20. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class PiSession : ProviderSession
{
    public Action<ProviderEvent>? OnEvent { get; set; } // :21

    private readonly SessionConfiguration configuration; // :23
    private readonly string? localApplicationData; // PORT-ONLY test seam for isolated per-session support files.
    private StdioProcess? process; // :24
    private TaskHandle? readTask; // :25
    /// <summary>The pi session file path, taken from <c>configuration.resumeID</c>.</summary>
    private string? sessionID; // :26-27
    private bool started; // :28
    private bool stopped; // :29
    private RuntimeMode runtimeMode; // :30
    private InteractionMode interactionMode; // :31
    private string? model; // :32
    private string? effort; // :33

    private bool turnActive; // :35
    private bool interruptRequested; // :36
    /// <summary>Namespaces ids per launch, so a resumed thread never streams into an old entry.</summary>
    private readonly string launchID = SwiftString.Prefix(SwiftUUID.UuidString(Guid.NewGuid()), 8); // :38
    private int messageCounter; // :39
    private int thinkingCounter; // :40
    private string? currentMessageID; // :41
    private string? currentThinkingID; // :42
    private string messageText = ""; // :43
    private string thinkingText = ""; // :44
    /// <summary>The model's context ceiling, from the session state's model.</summary>
    private long? contextWindow; // :46
    private string? gateURL; // :47
    private readonly Dictionary<string, ApprovalRequest> pendingApprovals = new(SwiftStringComparer.Instance); // :48
    /// <summary><c>extension_ui_request</c> ids by method, for the app's questions.</summary>
    private readonly Dictionary<string, string> pendingQuestions = new(SwiftStringComparer.Instance); // :49-50
    /// <summary>Gate kinds approved for the rest of the session ("Allow for session").</summary>
    private readonly HashSet<string> sessionGrants = new(SwiftStringComparer.Instance); // :51-52
    private readonly Dictionary<string, string> toolNames = new(SwiftStringComparer.Instance); // :53
    private readonly Dictionary<string, JSONValue> toolInputs = new(SwiftStringComparer.Instance); // :54
    /// <summary>Files a <c>write</c> is about to replace, read as the call starts, so the row's diff is the file
    /// before against the content written.</summary>
    private readonly Dictionary<string, string> editSnapshots = new(SwiftStringComparer.Instance); // :55-57
    private string? turnError; // :58
    /// <summary>The turn's last reply, which plan mode hands over as the plan.</summary>
    private string lastReplyText = ""; // :59-60
    private int turnCounter; // :61
    private readonly Dictionary<string, CheckedContinuation<JSONValue>> pendingResponses = new(SwiftStringComparer.Instance); // :62
    private int requestCounter; // :63

    public PiSession(SessionConfiguration configuration) : this(configuration, localApplicationData: null) // :65-72
    {
    }

    /// <summary>PORT-ONLY test seam: keeps the extension, gate and bridge under a throwaway app-data root.</summary>
    internal PiSession(SessionConfiguration configuration, string? localApplicationData)
    {
        this.configuration = configuration;
        this.localApplicationData = localApplicationData;
        runtimeMode = configuration.RuntimeMode;
        interactionMode = configuration.InteractionMode;
        model = configuration.Model;
        effort = configuration.Effort;
        sessionID = configuration.ResumeID is { Length: > 0 } resumeID ? resumeID : null; // :71
    }

    public bool IsRunning => started && !stopped && process?.IsRunning == true; // :74

    private string WorkingDirectory => configuration.WorkingDirectory.Path; // :76

    // MARK: - Lifecycle

    public async Task<string> Start() // :80-157
    {
        if (configuration.Executable is not { } executable) // :81
            throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider));

        // :82-84. R-FS-1: fileExists -> File.Exists || Directory.Exists. A plain existence test on the path,
        // not a directory scan, in contrast to Command Code (PR-PROVCLI-267).
        if (sessionID is { } existingSessionID && !(File.Exists(existingSessionID) || Directory.Exists(existingSessionID)))
            throw new ProviderError(new ProviderError.Case.Failed("Pi no longer has this conversation; starting a new one."));

        // :85-90
        var supportDirectory = localApplicationData is { } appData
            ? PiCLI.SupportDirectoryUnder(appData)
            : PiCLI.SupportDirectory;
        var extensionURL = localApplicationData is { } localAppData
            ? PiCLI.InstallExtension(localAppData)
            : PiCLI.InstallExtension();
        var gateDirectory = supportDirectory.Appending("gates");
        Directory.CreateDirectory(WindowsPath(gateDirectory));
        var gateFileURL = gateDirectory.Appending(SwiftUUID.UuidString(Guid.NewGuid()) + ".json");
        gateURL = WindowsPath(gateFileURL);
        WriteGate(finalReport: false);

        // :92-93
        var arguments = new List<string> { "--mode", "rpc", "-e", WindowsPath(extensionURL) };
        arguments.Add(runtimeMode is RuntimeMode.Auto or RuntimeMode.FullAccess ? "--approve" : "--no-approve");
        // :94-96. The split is on the FIRST slash; a slashless id or the literal "default" adds neither flag.
        if (model is { Length: > 0 } selectedModel && selectedModel != "default" && selectedModel.IndexOf('/', StringComparison.Ordinal) is var modelSlash && modelSlash >= 0)
        {
            arguments.Add("--provider");
            arguments.Add(selectedModel[..modelSlash]);
            arguments.Add("--model");
            arguments.Add(selectedModel[(modelSlash + 1)..]);
        }
        // :97-99
        if (effort is { Length: > 0 } selectedEffort && PiCLI.ThinkingLevels.Contains(selectedEffort))
        {
            arguments.Add("--thinking");
            arguments.Add(selectedEffort);
        }
        // :100. No emptiness check here: init already normalized an empty id to null.
        if (sessionID is { } resumeID)
        {
            arguments.Add("--session");
            arguments.Add(resumeID);
        }

        // :102-110. ED-128: environment dictionaries are case-insensitive.
        var environment = new Dictionary<string, string>(configuration.Environment, StringComparer.OrdinalIgnoreCase);
        environment["DROPPY_CODE_PI_GATE"] = gateURL;
        environment["PI_SKIP_VERSION_CHECK"] = "1";
        if (SwiftTry.Optional(() => localApplicationData is { } bridgeAppData
                ? MCPBridge.InstallBridge(bridgeAppData)
                : MCPBridge.InstallBridge()) is { } bridge)
            // DEVIATION ED-022: on Windows, Node's dynamic import() of a bare "C:\..." path throws
            // ERR_UNSUPPORTED_ESM_URL_SCHEME, so the port passes pathToFileURL(bridgePath).href; the Swift
            // source's `bridge.path` becomes the file:// URL string InstallBridge already returns.
            environment["DROPPY_CODE_MCP_BRIDGE"] = bridge.AbsoluteUri;
        if (File.Exists(MCPPaths.ClaudeConfigURL))
            environment["DROPPY_CODE_MCP_CONFIG"] = MCPPaths.ClaudeConfigURL;

        // :112-133
        var child = new StdioProcess(executable.Path, arguments, configuration.WorkingDirectory.Path, environment, StdioFramer.Framing.Lines);
        try
        {
            child.Start(); // :120
        }
        catch (Exception error)
        {
            // Per-site decision (R-ERR-2, O-14), matching PiCLI.StartFailureDescription (PiCLI.cs :135-147,
            // PR-PROVCLI-310): on Windows a failed process launch is a plain BCL Win32Exception, neither an
            // ILocalizedError nor an ISwiftErrorIdentity, so SwiftErrors.LocalizedDescription throws
            // NotSupportedException for it by design; this falls back to the exception's own Message instead.
            var description = error is ILocalizedError or ISwiftErrorIdentity ? SwiftErrors.LocalizedDescription(error) : error.Message;
            throw new ProviderError(new ProviderError.Case.Failed($"Pi could not start: {description}"));
        }
        process = child; // :124
        var messages = child.Messages; // :125
        readTask = MainActor.Spawn(async () => // :126-133
        {
            await foreach (var message in messages)
                Handle(message);
            var status = await child.WaitForExit();
            DidClose(status);
        });

        // :135-143
        var state = await Request(Obj(("type", "get_state")));
        var data = state["data"] ?? state;
        if (data["sessionFile"]?.JSONValueString is { Length: > 0 } sessionFile)
            sessionID = sessionFile;
        else if (data["sessionId"]?.JSONValueString is { Length: > 0 } stateSessionID)
            sessionID = stateSessionID;
        contextWindow = data["model"]?["contextWindow"]?.JSONValueInt;
        OnEvent?.Invoke(new ProviderEvent.SessionReady(sessionID ?? ""));

        // :145-153
        var commandsResponse = await Request(Obj(("type", "get_commands")));
        var commandsData = commandsResponse["data"] ?? commandsResponse;
        if (commandsData["commands"]?.JSONValueArray is { } entries)
        {
            var commands = new List<SlashCommand>();
            foreach (var entry in entries)
            {
                if (entry["name"]?.JSONValueString is not { } name) continue;
                commands.Add(new SlashCommand { Name = name, Detail = entry["description"]?.JSONValueString ?? "" });
            }
            if (commands.Count != 0)
                OnEvent?.Invoke(new ProviderEvent.Commands(new ValueList<SlashCommand>(commands)));
        }

        started = true; // :154
        stopped = false; // :155
        return sessionID ?? ""; // :156
    }

    public async Task Send(TurnInput input) // :159-200
    {
        if (!IsRunning || process is not { } child) throw new ProviderError(new ProviderError.Case.NotRunning()); // :160
        if (turnActive) throw new ProviderError(new ProviderError.Case.Failed("Pi is still on the last turn.")); // :161
        runtimeMode = input.RuntimeMode; // :162
        interactionMode = input.InteractionMode; // :163
        WriteGate(input.IsFinalReport); // :164
        // :165-174
        if (input.Model is { Length: > 0 } wantedModel && wantedModel != model && wantedModel.IndexOf('/', StringComparison.Ordinal) is var modelSlash && modelSlash >= 0)
        {
            child.Send(Obj(("type", "set_model"), ("provider", wantedModel[..modelSlash]), ("modelId", wantedModel[(modelSlash + 1)..])));
            model = wantedModel;
            // The context window was read with the model Pi started on; the usage panel's fraction wants the
            // one it runs on now.
            var state = await Request(Obj(("type", "get_state")));
            if ((state["data"] ?? state)["model"]?["contextWindow"]?.JSONValueInt is { } window)
                contextWindow = window;
        }
        // :175-178
        if (input.Effort is { Length: > 0 } wantedEffort && wantedEffort != effort && PiCLI.ThinkingLevels.Contains(wantedEffort))
        {
            child.Send(Obj(("type", "set_thinking_level"), ("level", wantedEffort)));
            effort = wantedEffort;
        }
        // :179-186
        turnActive = true;
        interruptRequested = false;
        turnError = null;
        turnCounter++;
        lastReplyText = "";
        toolNames.Clear();
        toolInputs.Clear();
        editSnapshots.Clear();

        // :187
        var fields = new Dictionary<string, JSONValue> { ["type"] = new JSONValue.String("prompt"), ["message"] = new JSONValue.String(input.Text) };
        // :188-191
        var images = new List<JSONValue>();
        foreach (var image in input.Images)
        {
            if (!image.IsImage) continue;
            // Attachment.Url's DEVIATION note: real file I/O reads Path directly, never .Url (SwiftURL round-trips
            // a Windows path incorrectly for file:// URLs; Core/Models/Timeline.cs's own Base64Images does the same).
            if (SwiftTry.Optional(() => File.ReadAllBytes(image.Path)) is not { } data) continue;
            images.Add(Obj(("type", "image"), ("data", Convert.ToBase64String(data)), ("mimeType", image.MimeType)));
        }
        // :192-197
        if (images.Count != 0)
            fields["images"] = new JSONValue.Array(new ValueList<JSONValue>(images));
        var prompt = new JSONValue.Object(new ValueMap<string, JSONValue>(fields, SwiftStringComparer.Instance));
        child.Send(prompt); // :198
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null)); // :199. After the write, unlike Claude and Command Code.
    }

    public async Task Interrupt() // :202-205
    {
        interruptRequested = true;
        process?.Send(Obj(("type", "abort")));
        await Task.CompletedTask;
    }

    public async Task Compact() // :207-210
    {
        if (!IsRunning || process is not { } child) throw new ProviderError(new ProviderError.Case.NotRunning());
        child.Send(Obj(("type", "compact")));
        await Task.CompletedTask;
    }

    public void Stop() // :212-222
    {
        stopped = true;
        process?.Terminate();
        process = null;
        readTask?.Cancel();
        readTask = null;
        if (gateURL is { } path)
        {
            SwiftTry.Ignore(() => FileOps.RemoveItem(path));
            gateURL = null;
        }
    }

    public Task<bool> StopAgent(string id) => Task.FromResult(false); // :224

    public void ResolveApproval(string requestID, string optionID) // :226-240
    {
        if (!pendingApprovals.Remove(requestID, out var request)) return;
        var option = request.Options.FirstOrDefault(candidate => candidate.Id == optionID);
        string value;
        if (option?.OptionRole == ApprovalRequest.Option.Role.ApproveAlways)
        {
            value = "Allow for session";
            sessionGrants.Add(GrantKey(request.RequestKind));
        }
        else if (option?.OptionRole == ApprovalRequest.Option.Role.Approve)
        {
            value = "Allow";
        }
        else // .decline, .cancel, nil
        {
            value = "Deny";
        }
        process?.Send(Obj(("type", "extension_ui_response"), ("id", requestID), ("value", value)));
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID));
    }

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) // :242-253
    {
        if (!pendingQuestions.Remove(requestID, out var method)) return;
        var texts = answers.Values.SelectMany(value => value).ToList();
        if (texts.Count == 0)
            process?.Send(Obj(("type", "extension_ui_response"), ("id", requestID), ("cancelled", true)));
        else if (method == "confirm")
            process?.Send(Obj(("type", "extension_ui_response"), ("id", requestID), ("confirmed", texts[0] == "Yes")));
        else
            process?.Send(Obj(("type", "extension_ui_response"), ("id", requestID), ("value", texts[0])));
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID));
    }

    // MARK: - Requests

    /// <summary>Writes a request line with a fresh id and resumes with the <c>response</c> that carries it, or
    /// <c>.null</c> after 30 seconds. Swift `private`; kept internal so this part's own tests can drive it
    /// without a live child process for the no-process case (see the file header note).</summary>
    internal async Task<JSONValue> Request(JSONValue fields) // :257-276
    {
        if (process is not { } child) return new JSONValue.Null(); // :260
        requestCounter++; // :261
        var id = $"droppy-{requestCounter}"; // :262. DEVIATION OD-0012 F1: "droppy-", not "marrow-" (kept, PR-PROVCLI-276).
        var line = new Dictionary<string, JSONValue>(fields.JSONValueObject ?? new Dictionary<string, JSONValue>(), SwiftStringComparer.Instance)
        {
            ["id"] = new JSONValue.String(id),
        };
        return await CheckedContinuation<JSONValue>.With(continuation => // :265-273
        {
            pendingResponses[id] = continuation;
            child.Send(new JSONValue.Object(new ValueMap<string, JSONValue>(line, SwiftStringComparer.Instance)));
            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(30));
                if (pendingResponses.Remove(id, out var pending))
                    pending.Resume(new JSONValue.Null());
            });
        });
    }

    /// <summary>The gate the extension reads before every tool call.</summary>
    private void WriteGate(bool finalReport) // :278-288
    {
        if (gateURL is not { } path) return;
        var gate = Obj(
            ("runtimeMode", SwiftEnum<RuntimeMode>.RawValue(runtimeMode)),
            ("interactionMode", SwiftEnum<InteractionMode>.RawValue(interactionMode)),
            ("finalReport", finalReport),
            ("systemPrompt", ""));
        SwiftTry.Ignore(() => FileOps.WriteAtomic(path, gate.Data()));
    }

    private static string GrantKey(ApprovalRequest.Kind kind) => kind switch // :290-296
    {
        ApprovalRequest.Kind.Command => "command",
        ApprovalRequest.Kind.FileChange => "fileChange",
        ApprovalRequest.Kind.Tool or ApprovalRequest.Kind.Permissions or ApprovalRequest.Kind.Plan => "tool",
        _ => throw new UnreachableException(),
    };

    // MARK: - Stream

    /// <summary>Swift `private`; kept internal so this part's own tests can drive dispatch without a live child
    /// process (see the file header note).</summary>
    internal void Handle(JSONValue message) // :300-344
    {
        switch (message["type"]?.JSONValueString)
        {
            case "response": // :302-309
                if (message["id"]?.JSONValueString is not { } responseID) return;
                if (pendingResponses.Remove(responseID, out var continuation))
                    continuation.Resume(message);
                else if (message["command"]?.JSONValueString == "prompt" && message["success"]?.JSONValueBool == false)
                {
                    turnError = message["error"]?.JSONValueString ?? message["message"]?.JSONValueString ?? "Pi reported an error.";
                    FinishTurn();
                }
                break;
            case "message_start": // :310-316
                if (message["message"]?["role"]?.JSONValueString != "assistant") return;
                messageCounter++;
                currentMessageID = $"{launchID}-m-{messageCounter}";
                messageText = "";
                thinkingText = "";
                currentThinkingID = null;
                break;
            case "message_update": // :317-318
                HandleUpdate(message);
                break;
            case "message_end": // :319-320
                HandleMessageEnd(message["message"] ?? message);
                break;
            case "tool_execution_start": // :321-322
                HandleToolStart(message);
                break;
            case "tool_execution_update": // :323-326
                if (message["toolCallId"]?.JSONValueString is { } toolID)
                {
                    var text = TextIn(message["partialResult"]?["content"]);
                    if (text.Length != 0) OnEvent?.Invoke(new ProviderEvent.ToolOutput(toolID, text));
                }
                break;
            case "tool_execution_end": // :327-328
                HandleToolEnd(message);
                break;
            case "agent_end": // :329-330
                FinishTurn();
                break;
            case "compaction_start": // :331-332
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Compacting the conversation\u2026" })); // U+2026 HORIZONTAL ELLIPSIS
                break;
            case "compaction_end": // :333-334
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Conversation compacted" }));
                break;
            case "auto_retry_start": // :335-336
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = message["error"]?.JSONValueString ?? "Pi is retrying." }));
                break;
            case "extension_error": // :337-338
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = message["error"]?.JSONValueString ?? "Pi reported an extension error." }));
                break;
            case "extension_ui_request": // :339-340
                HandleExtensionUI(message);
                break;
            default: // :341-343
                break;
        }
    }

    internal void HandleUpdate(JSONValue message) // :346-381
    {
        var eventValue = message["assistantMessageEvent"] ?? message["event"] ?? new JSONValue.Null();
        switch (eventValue["type"]?.JSONValueString)
        {
            case "text_delta": // :349-356
                if (eventValue["delta"]?.JSONValueString is not { Length: > 0 } delta) break;
                if (currentMessageID is null)
                {
                    messageCounter++;
                    currentMessageID = $"{launchID}-m-{messageCounter}";
                }
                messageText += delta;
                OnEvent?.Invoke(new ProviderEvent.MessageDelta(currentMessageID, delta));
                break;
            case "thinking_delta": // :357-365
                if (eventValue["delta"]?.JSONValueString is not { Length: > 0 } thinkingDelta) break;
                if (currentThinkingID is null)
                {
                    thinkingCounter++;
                    currentThinkingID = $"{launchID}-t-{thinkingCounter}";
                    thinkingText = "";
                }
                thinkingText += thinkingDelta;
                OnEvent?.Invoke(new ProviderEvent.ReasoningDelta(currentThinkingID, thinkingDelta));
                break;
            default: // :366-368
                break;
        }
        // :369-380. One sum of four fallback pairs, named separately: the swiftc type checker gives up on the
        // single-expression form ("unable to type-check this expression in reasonable time"); the arithmetic is
        // unchanged.
        var usage = message["usage"] ?? eventValue["usage"] ?? new JSONValue.Null();
        var total = usage["totalTokens"]?.JSONValueInt;
        var input = usage["input"]?.JSONValueInt ?? usage["inputTokens"]?.JSONValueInt ?? 0;
        var output = usage["output"]?.JSONValueInt ?? usage["outputTokens"]?.JSONValueInt ?? 0;
        var cacheRead = usage["cacheRead"]?.JSONValueInt ?? usage["cacheReadTokens"]?.JSONValueInt ?? 0;
        var cacheWrite = usage["cacheWrite"]?.JSONValueInt ?? usage["cacheWriteTokens"]?.JSONValueInt ?? 0;
        var used = total ?? input + output + cacheRead + cacheWrite;
        if (used > 0)
            OnEvent?.Invoke(new ProviderEvent.Usage(new ContextUsage { UsedTokens = used, WindowTokens = contextWindow }));
    }

    internal void HandleMessageEnd(JSONValue message) // :383-405
    {
        if (message["role"]?.JSONValueString != "assistant") return;
        var finalText = messageText;
        if (message["content"]?.JSONValueArray is { } blocks)
        {
            var joined = string.Concat(blocks
                .Where(block => block["type"]?.JSONValueString == "text")
                .Select(block => block["text"]?.JSONValueString)
                .Where(text => text is not null));
            if (joined.Length != 0) finalText = joined;
        }
        if (currentThinkingID is { } thinkingID && thinkingText.Trimmed().Length != 0)
            OnEvent?.Invoke(new ProviderEvent.ReasoningCompleted(thinkingID, thinkingText));
        currentThinkingID = null;
        if (currentMessageID is { } messageID)
        {
            OnEvent?.Invoke(new ProviderEvent.MessageCompleted(messageID, finalText));
            lastReplyText = finalText;
        }
        currentMessageID = null;
        if (message["stopReason"]?.JSONValueString == "error")
            turnError = message["errorMessage"]?.JSONValueString ?? "Pi reported an error.";
    }

    internal void HandleToolStart(JSONValue message) // :407-449
    {
        if (message["toolCallId"]?.JSONValueString is not { } id || message["toolName"]?.JSONValueString is not { } name) return;
        var args = message["args"] ?? message["input"] ?? new JSONValue.Null();
        toolNames[id] = name;
        toolInputs[id] = args;
        if (name == "write" && args["path"]?.JSONValueString is { } writePath)
            editSnapshots[id] = Snapshot(writePath) ?? "";

        var kind = name switch // :415-422
        {
            "bash" => ToolCall.Kind.Command,
            "read" => ToolCall.Kind.Read,
            "edit" or "write" => ToolCall.Kind.Edit,
            "grep" or "find" or "ls" => ToolCall.Kind.Search,
            _ when name.StartsWith("mcp__", StringComparison.Ordinal) => ToolCall.Kind.Mcp,
            _ => ToolCall.Kind.Other,
        };
        string title;
        string? detail;
        if (name == "bash") // :426-428
        {
            title = ToolTitles.UnwrapShell(args["command"]?.JSONValueString ?? "bash");
            detail = null;
        }
        else if (name is "read" or "edit" or "write") // :429-431
        {
            title = ToolTitles.RelativePath(args["path"]?.JSONValueString ?? name, WorkingDirectory);
            detail = null;
        }
        else if (name is "grep" or "find") // :432-434
        {
            title = args["pattern"]?.JSONValueString ?? name;
            detail = null;
        }
        else if (name == "ls") // :435-437
        {
            title = args["path"]?.JSONValueString ?? ".";
            detail = null;
        }
        else if (name.StartsWith("mcp__", StringComparison.Ordinal)) // :438-443
        {
            // SwiftString.Split takes a single Character separator; "__" is two, so Swift's
            // split(separator: "__", omittingEmptySubsequences: true) ports as a plain multi-char split (O-13).
            var parts = name.Split("__", StringSplitOptions.RemoveEmptyEntries);
            var server = parts.Length > 1 ? parts[1] : "MCP";
            var tool = parts.Length > 2 ? string.Join("__", parts.Skip(2)) : name;
            title = $"{server} \u00B7 {tool}"; // U+00B7 MIDDLE DOT
            detail = args.IsNull || args.CompactString == "{}" ? null : args.PrettyString;
        }
        else // :444-446
        {
            title = name;
            detail = args.IsNull || args.CompactString == "{}" ? null : args.PrettyString;
        }
        OnEvent?.Invoke(new ProviderEvent.ToolStarted(id, new ToolCall { ToolCallKind = kind, Title = title, Detail = detail })); // :448
    }

    internal void HandleToolEnd(JSONValue message) // :451-466
    {
        if (message["toolCallId"]?.JSONValueString is not { } id) return;
        var result = message["result"] ?? new JSONValue.Null();
        var output = TextIn(result["content"]);
        var isError = message["isError"]?.JSONValueBool == true || result["isError"]?.JSONValueBool == true;
        // A call the gate blocked on the user's word comes back as an error whose text is the gate's reason;
        // the row reads as declined rather than failed.
        var status = !isError ? ToolCall.Status.Completed
            : output.StartsWith("The user declined this", StringComparison.Ordinal) ? ToolCall.Status.Declined
            : ToolCall.Status.Failed;
        var exitCode = result["details"]?["exitCode"]?.JSONValueInt;
        ValueList<FileEdit>? edits = null;
        if (toolNames.TryGetValue(id, out var name) && toolInputs.TryGetValue(id, out var args))
            edits = EditDiff(name, args, id);
        OnEvent?.Invoke(new ProviderEvent.ToolUpdated(id, new ToolUpdate
        {
            Output = output.Length == 0 ? null : output,
            Status = status,
            ExitCode = exitCode,
            Edits = edits,
        }));
    }

    /// <summary>A minimal unified diff of the old text against the new: a `---`/`+++` header, then the old lines
    /// prefixed with `-` and the new lines prefixed with `+`.</summary>
    private ValueList<FileEdit>? EditDiff(string name, JSONValue args, string id) // :468-483
    {
        switch (name)
        {
            case "edit":
                if (args["path"]?.JSONValueString is not { } editPath) return null;
                var oldText = args["oldText"]?.JSONValueString ?? "";
                var newText = args["newText"]?.JSONValueString ?? "";
                return new ValueList<FileEdit>([UnifiedDiff(editPath, oldText, newText)]);
            case "write":
                if (args["path"]?.JSONValueString is not { } writePath || args["content"]?.JSONValueString is not { } content) return null;
                var before = editSnapshots.Remove(id, out var snapshot) ? snapshot : "";
                return new ValueList<FileEdit>([UnifiedDiff(writePath, before, content)]);
            default:
                return null;
        }
    }

    private static FileEdit UnifiedDiff(string path, string old, string @new) // :486-494
    {
        // path.hasPrefix("/") ? path : path: both branches are identical, so this is a no-op and the path is
        // NEVER made relative (upstream defect, reproduced: PR-PROVCLI-293).
        var relative = path;
        var header = $"--- a/{relative}\n+++ b/{relative}";
        var body = old.Split('\n').Where(line => line.Length != 0).Select(line => "-" + line)
            .Concat(@new.Split('\n').Where(line => line.Length != 0).Select(line => "+" + line));
        var diff = string.Join("\n", new[] { header }.Concat(body));
        var stats = ToolTitles.DiffStats(diff);
        return new FileEdit(relative, diff, stats.Additions, stats.Deletions);
    }

    private static string? Snapshot(string path) // :496-500
    {
        if (SwiftTry.Optional(() =>
            {
                var info = new FileInfo(path);
                return info.Length <= 1_000_000 ? File.ReadAllBytes(path) : null;
            }) is not { } bytes) return null;
        return SwiftText.DecodeUtf8OrNil(bytes);
    }

    /// <summary>The text of a content-block array, as tool results carry it.</summary>
    private static string TextIn(JSONValue? value) // :502-511
    {
        if (value is null || value.IsNull) return "";
        if (value.JSONValueString is { } text) return text;
        if (value.JSONValueArray is not { } blocks) return "";
        return string.Join("\n", blocks.Select(block => block["text"]?.JSONValueString).Where(text => text is not null));
    }

    private void FinishTurn() // :513-531
    {
        if (!turnActive) return;
        turnActive = false;
        var error = turnError;
        turnError = null;
        // Plan mode is the gate's read-only turn: its final reply is the plan.
        if (interactionMode == InteractionMode.Plan && error is null && !interruptRequested && lastReplyText.Trimmed().Length != 0)
            OnEvent?.Invoke(new ProviderEvent.PlanCompleted($"{launchID}-plan-{turnCounter}", lastReplyText));
        if (interruptRequested)
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
        else if (error is not null)
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, error));
        else
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        interruptRequested = false;
    }

    // MARK: - Extension UI

    private void HandleExtensionUI(JSONValue message) // :535-582
    {
        if ((message["id"]?.JSONValueString ?? message["requestId"]?.JSONValueString) is not { } id) return;
        if (message["method"]?.JSONValueString is not { } method) return;
        var parameters = message["params"] ?? message;
        switch (method)
        {
            case "select": // :540-541
                HandleSelect(id, parameters);
                break;
            case "confirm": // :542-556
                var title = parameters["title"]?.JSONValueString ?? "Pi";
                var prompt = string.Join("\n", new[] { title, parameters["message"]?.JSONValueString }.Where(part => !string.IsNullOrEmpty(part)));
                pendingQuestions[id] = "confirm";
                OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(id, new ValueList<QuestionRequest.Question>([
                    new QuestionRequest.Question(id, "Pi", prompt, new ValueList<QuestionRequest.Choice>([
                        new QuestionRequest.Choice("Yes"), new QuestionRequest.Choice("No"),
                    ]), AllowsMultiple: false, AllowsOther: false, IsSecret: false),
                ]))));
                break;
            case "input" or "editor": // :557-569
                pendingQuestions[id] = method;
                OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(id, new ValueList<QuestionRequest.Question>([
                    new QuestionRequest.Question(id, "Pi", parameters["title"]?.JSONValueString ?? "Pi", ValueList<QuestionRequest.Choice>.Empty,
                        AllowsMultiple: false, AllowsOther: true, IsSecret: false),
                ]))));
                break;
            case "notify": // :570-576
                var level = parameters["notifyType"]?.JSONValueString switch
                {
                    "warning" => Notice.Level.Warning,
                    "error" => Notice.Level.Error,
                    _ => Notice.Level.Info,
                };
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = level, Message = parameters["message"]?.JSONValueString ?? "Pi sent a notification." }));
                break;
            case "setStatus" or "setWidget" or "setTitle" or "set_editor_text": // :577-578
                break;
            default: // :579-580
                break;
        }
    }

    private void HandleSelect(string id, JSONValue parameters) // :584-636
    {
        var title = parameters["title"]?.JSONValueString ?? "";
        var options = (parameters["options"]?.JSONValueArray ?? []).Select(item => item.JSONValueString).Where(item => item is not null).Select(item => item!).ToList();
        if (!title.StartsWith("droppy-gate ", StringComparison.Ordinal) ||
            JSONValue.Parse(title["droppy-gate ".Length..]) is not { } payload)
        {
            pendingQuestions[id] = "select";
            OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(id, new ValueList<QuestionRequest.Question>([
                new QuestionRequest.Question(id, "Pi", title, new ValueList<QuestionRequest.Choice>(options.Select(option => new QuestionRequest.Choice(option))),
                    AllowsMultiple: false, AllowsOther: false, IsSecret: false),
            ]))));
            return;
        }
        var kindString = payload["kind"]?.JSONValueString ?? "tool";
        if (sessionGrants.Contains(kindString))
        {
            process?.Send(Obj(("type", "extension_ui_response"), ("id", id), ("value", "Allow")));
            return;
        }
        var toolName = payload["toolName"]?.JSONValueString ?? "tool";
        var input = payload["input"] ?? new JSONValue.Null();
        var kind = kindString == "command" ? ApprovalRequest.Kind.Command : kindString == "fileChange" ? ApprovalRequest.Kind.FileChange : ApprovalRequest.Kind.Tool;
        string callTitle;
        string? callDetail;
        if (kind == ApprovalRequest.Kind.Command) // :613-615
        {
            callTitle = ToolTitles.UnwrapShell(input["command"]?.JSONValueString ?? toolName);
            callDetail = input["command"]?.JSONValueString;
        }
        else if (kind == ApprovalRequest.Kind.FileChange) // :616-618
        {
            callTitle = ToolTitles.RelativePath(input["path"]?.JSONValueString ?? toolName, WorkingDirectory);
            callDetail = input["path"]?.JSONValueString;
        }
        else // :619-621
        {
            callTitle = toolName;
            callDetail = input.IsNull || input.CompactString == "{}" ? null : input.PrettyString;
        }
        var approval = new ApprovalRequest(id, kind, callTitle, new ValueList<ApprovalRequest.Option>([
            new ApprovalRequest.Option("allow", "Approve", ApprovalRequest.Option.Role.Approve),
            new ApprovalRequest.Option("always", "Approve for session", ApprovalRequest.Option.Role.ApproveAlways),
            new ApprovalRequest.Option("deny", "Decline", ApprovalRequest.Option.Role.Decline),
        ]), Detail: callDetail, ToolItemID: payload["toolCallId"]?.JSONValueString);
        pendingApprovals[id] = approval;
        OnEvent?.Invoke(new ProviderEvent.Approval(approval)); // :635
    }

    /// <summary>Test seam for this part's own tests: the turn-active state <c>Send()</c> sets at :179-186,
    /// without a live process (Send() itself requires IsRunning). No Swift twin.</summary>
    internal void TestBeginTurn(InteractionMode mode, RuntimeMode runtimeModeValue = RuntimeMode.Supervised)
    {
        interactionMode = mode;
        runtimeMode = runtimeModeValue;
        turnActive = true;
        interruptRequested = false;
        turnError = null;
        turnCounter++;
        lastReplyText = "";
    }

    // Swift `private`; kept internal so this part's own tests can drive it without tearing down a live process
    // (per the file's established seam pattern, e.g. Handle, HandleToolEnd).
    internal void DidClose(int status) // :638-653
    {
        var tail = TextCleanup.LastLines(process?.ErrorTail ?? "");
        process = null;
        if (turnActive)
        {
            turnActive = false;
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(interruptRequested ? TurnStatus.Interrupted : TurnStatus.Failed, tail is { Length: > 0 } ? tail : null));
        }
        if (stopped) return;
        started = false;
        if (status == 0)
        {
            OnEvent?.Invoke(new ProviderEvent.Exited(null));
        }
        else
        {
            var suffix = string.IsNullOrEmpty(tail) ? "" : "\n" + tail;
            OnEvent?.Invoke(new ProviderEvent.Exited($"Pi exited with status {status}{suffix}"));
        }
    }

    private static JSONValue Obj(params (string Key, JSONValue Value)[] fields) => JSONValue.FromDictionaryLiteral(fields);

    // ENGINEERING DECISION, empirical (matching PA-0015's method): SwiftURL.Path on a "file://" URL faithfully
    // reproduces macOS Foundation's POSIX-style url.path (SwiftURL.cs :137,:163-167; ClaudeSessionTests.cs's
    // header note that this shape is "out of scope"), which for a Windows absolute path gives "/C:/Users/..."
    // - not a path any Win32 API accepts. Confirmed directly: Directory.CreateDirectory("/C:/Users/...") throws
    // "The filename, directory name, or volume label syntax is incorrect." PiCLI.SupportDirectory and
    // InstallExtension() (already in the tree) return exactly such a URL, and PR-PROVCLI-268's own evidence
    // requires "a plain filesystem path, not a URL" at every site that consumes it. Until SwiftURL carries a
    // Windows-aware accessor, this converts that POSIX-mangled form back to a real Windows path (drive letter,
    // backslashes) at PiSession's own three call sites (the gate directory, the gate file, and the -e argv
    // token); PiCLI.cs itself is untouched, since it never calls .Path on these URLs for real I/O.
    private static string WindowsPath(SwiftURL url)
    {
        var path = url.Path;
        if (path.Length >= 3 && path[0] == '/' && (path[1] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') && path[2] == ':')
            path = path[1..];
        return path.Replace('/', '\\');
    }
}
