using System.Linq;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Core.Port.Skills.Detect;

// Source: DroppyCode/Services/Providers/PiSession.swift (B3 52bc7926), :1-610. The whole file: the lifecycle,
// argv/environment building, the get_state/get_commands handshake, send/interrupt/compact/stop, approvals and
// question answers, the request/response wire, the gate file, the message/tool event stream and its helpers.
// B3 delta (lane L3f, B3_PLAN.md N1/N7): the request/response wire now goes through the shared PendingReplies
// (Core/Support/PendingReplies.cs), and the streamed-text/edit-snapshot bookkeeping now goes through the shared
// SessionSupport types (StreamedTextTracker, EditSnapshots); the content-block joiner and the approve-for-session
// key both moved to ProviderSession.cs (ToolText.Blocks, ApprovalRequest.Kind.GrantKey), shared with Claude and
// Command Code.
namespace Marrow.Services.Providers;

// :19-20. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class PiSession : ProviderSession, IStandbyInjectionSession, Marrow.Services.Port.Skills.Providers.ISkillSessionLifetime
{
    private Task skillReadersExited = Task.CompletedTask;
    public Task SkillReadersExited => skillReadersExited;

    public Action<ProviderEvent>? OnEvent { get; set; } // :21

    // PORT-ONLY: SK-11e / WU-10 and DL-12. Null for both means today's argv, environment and behaviour exactly.
    public ProviderSkillLaunch? SkillLaunch { get; init; }
    public Action<ToolEvent>? ToolEvents { get; set; }
    private PiToolEventTap? toolTap;
    private PiToolEventTap Tap => toolTap ??= new PiToolEventTap(ToolEvents);

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
    private readonly string launchID; // :38
    // :39. B3: the message/thinking id and buffer bookkeeping moved to the shared SessionSupport type
    // (unit N7), replacing this file's own messageCounter/thinkingCounter/currentMessageID/currentThinkingID/
    // messageText/thinkingText fields.
    private readonly StreamedTextTracker stream; // :39
    /// <summary>The model's context ceiling, from the session state's model.</summary>
    private long? contextWindow; // :41
    private string? gateURL; // :42
    private readonly Dictionary<string, ApprovalRequest> pendingApprovals = new(SwiftStringComparer.Instance); // :43
    /// <summary><c>extension_ui_request</c> ids by method, for the app's questions.</summary>
    private readonly Dictionary<string, string> pendingQuestions = new(SwiftStringComparer.Instance); // :44-45
    /// <summary>Gate kinds approved for the rest of the session ("Allow for session").</summary>
    private readonly HashSet<string> sessionGrants = new(SwiftStringComparer.Instance); // :46-47
    private readonly Dictionary<string, string> toolNames = new(SwiftStringComparer.Instance); // :48
    private readonly Dictionary<string, JSONValue> toolInputs = new(SwiftStringComparer.Instance); // :49
    /// <summary>Files a <c>write</c> is about to replace, read as the call starts, so the row's diff is the file
    /// before against the content written. B3 (N7): the shared SessionSupport type, replacing this file's own
    /// dictionary-of-strings copy.</summary>
    private readonly EditSnapshots editSnapshots = new(); // :50-52
    private string? turnError; // :53
    /// <summary>The turn's last reply, which plan mode hands over as the plan.</summary>
    private string lastReplyText = ""; // :54-55
    private int turnCounter; // :56
    // :57. B3 (N1): the shared PendingReplies<JSONValue>, replacing this file's own
    // Dictionary<string, CheckedContinuation<JSONValue>>; see Request() and DidClose() below.
    private readonly PendingReplies<JSONValue> pendingResponses = new(); // :57
    private int requestCounter; // :58

    // PORT-ONLY: OD-0116, OD-0122, ED-439. A waiting standby loads context through the app's load extension with no turn; any agent or tool activity is a violation.
    private bool standbyActive;
    private bool standbyDiscarding;
    private bool loadCommandAvailable;
    public string? StandbyViolation { get; private set; }
    public Action<string>? OnStandbyViolation { get; set; }

    // PORT-ONLY RR-49: permanent no-tools mode; never used or adopted as a working session.
    private bool textOnlyHead;
    private bool textOnlyVerified;
    private bool textOnlyCommandAvailable;

    public PiSession(SessionConfiguration configuration) : this(configuration, localApplicationData: null) // :60-73
    {
    }

    /// <summary>PORT-ONLY test seam: keeps the extension, gate and bridge under a throwaway app-data root.</summary>
    internal PiSession(SessionConfiguration configuration, string? localApplicationData)
    {
        var launchID = SwiftString.Prefix(SwiftUUID.UuidString(Guid.NewGuid()), 8); // :61
        this.launchID = launchID; // :62
        stream = new StreamedTextTracker( // :63-66
            messageID: count => $"{launchID}-m-{count}",
            thinkingID: count => $"{launchID}-t-{count}");
        this.configuration = configuration; // :67
        this.localApplicationData = localApplicationData;
        runtimeMode = configuration.RuntimeMode; // :68
        interactionMode = configuration.InteractionMode; // :69
        model = configuration.Model; // :70
        effort = configuration.Effort; // :71
        sessionID = configuration.ResumeID is { Length: > 0 } resumeID ? resumeID : null; // :72
    }

    public bool IsRunning => started && !stopped && process?.IsRunning == true; // :75

    private string WorkingDirectory => configuration.WorkingDirectory.Path; // :77

    // MARK: - Lifecycle

    public async Task<string> Start() // :81-158
    {
        if (configuration.Executable is not { } executable) // :82
            throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider));

        // :83-85. R-FS-1: fileExists -> File.Exists || Directory.Exists. A plain existence test on the path,
        // not a directory scan, in contrast to Command Code (PR-PROVCLI-267).
        if (sessionID is { } existingSessionID && !(File.Exists(existingSessionID) || Directory.Exists(existingSessionID)))
            throw new ProviderError(new ProviderError.Case.Failed("Pi no longer has this conversation; starting a new one."));

        // :86-91
        var supportDirectory = localApplicationData is { } appData
            ? PiCLI.SupportDirectoryUnder(appData)
            : PiCLI.SupportDirectory;
        var extensionURL = textOnlyHead ? PiCLI.InstallTextOnlyExtension(localApplicationData)
            : localApplicationData is { } localAppData
            ? PiCLI.InstallExtension(localAppData)
            : PiCLI.InstallExtension();
        var gateDirectory = supportDirectory.Appending("gates");
        Directory.CreateDirectory(WindowsPath(gateDirectory));
        var gateFileURL = gateDirectory.Appending(SwiftUUID.UuidString(Guid.NewGuid()) + ".json");
        gateURL = WindowsPath(gateFileURL);
        WriteGate(finalReport: standbyActive); // PORT-ONLY (ED-439): a waiting standby blocks every tool

        // :93-94
        var arguments = textOnlyHead ? PiTextOnlyHeadPolicy.Arguments(WindowsPath(extensionURL), model, effort)
            : LiveArguments(WindowsPath(extensionURL), runtimeMode, model, effort, sessionID);
        if (standbyActive)
        {
            arguments.Add("-e");
            arguments.Add(WindowsPath(localApplicationData is { } loadAppData ? PiCLI.InstallLoadExtension(loadAppData) : PiCLI.InstallLoadExtension()));
        }
        // PORT-ONLY: SK-11e / WU-10. The chat's locked generation; the model-list probe (PiCLI) never gets this.
        if (!textOnlyHead) arguments = WithSkills(arguments);

        // :103-111. ED-128: environment dictionaries are case-insensitive.
        var environment = new Dictionary<string, string>(ProviderSkillEnvironment.Apply(configuration.Environment, SkillLaunch), StringComparer.OrdinalIgnoreCase); // PORT-ONLY: SK-11e (DL-6 PATH from launch or bound root)
        environment["DROPPY_CODE_PI_GATE"] = gateURL;
        environment["PI_SKIP_VERSION_CHECK"] = "1";
        if (!textOnlyHead && SwiftTry.Optional(() => localApplicationData is { } bridgeAppData
                ? MCPBridge.InstallBridge(bridgeAppData)
                : MCPBridge.InstallBridge()) is { } bridge)
            // DEVIATION ED-022: on Windows, Node's dynamic import() of a bare "C:\..." path throws
            // ERR_UNSUPPORTED_ESM_URL_SCHEME, so the port passes pathToFileURL(bridgePath).href; the Swift
            // source's `bridge.path` becomes the file:// URL string InstallBridge already returns.
            environment["DROPPY_CODE_MCP_BRIDGE"] = bridge.AbsoluteUri;
        if (!textOnlyHead && File.Exists(MCPPaths.ClaudeConfigURL))
            environment["DROPPY_CODE_MCP_CONFIG"] = MCPPaths.ClaudeConfigURL;

        // :113-134
        var child = new StdioProcess(executable.Path, arguments, configuration.WorkingDirectory.Path, environment, StdioFramer.Framing.Lines);
        try
        {
            child.Start(); // :121
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
        skillReadersExited = child.WaitForExit(); // RR-15: retained after Stop clears process
        process = child; // :125
        var messages = child.Messages; // :126
        readTask = MainActor.Spawn(async () => // :127-134
        {
            await foreach (var message in messages)
                Handle(message);
            var status = await child.WaitForExit();
            DidClose(status);
        });

        // :136-144
        var state = await Request(Obj(("type", "get_state")));
        var data = state["data"] ?? state;
        if (data["sessionFile"]?.JSONValueString is { Length: > 0 } sessionFile)
            sessionID = sessionFile;
        else if (data["sessionId"]?.JSONValueString is { Length: > 0 } stateSessionID)
            sessionID = stateSessionID;
        contextWindow = data["model"]?["contextWindow"]?.JSONValueInt;
        OnEvent?.Invoke(new ProviderEvent.SessionReady(sessionID ?? ""));

        // :146-154
        var commandsResponse = await Request(Obj(("type", "get_commands")));
        var commandsData = commandsResponse["data"] ?? commandsResponse;
        if (commandsData["commands"]?.JSONValueArray is { } entries)
        {
            var commands = new List<SlashCommand>();
            foreach (var entry in entries)
            {
                if (entry["name"]?.JSONValueString is not { } name) continue;
                if (name.TrimStart('/') == "droppy-load") loadCommandAvailable = true;
                if (name.TrimStart('/') == PiTextOnlyHeadPolicy.Command) textOnlyCommandAvailable = true;
                commands.Add(new SlashCommand { Name = name, Detail = entry["description"]?.JSONValueString ?? "" });
            }
            if (commands.Count != 0)
                OnEvent?.Invoke(new ProviderEvent.Commands(new ValueList<SlashCommand>(commands)));
        }

        started = true; // :155
        stopped = false; // :156
        return sessionID ?? ""; // :157
    }

    internal async Task<string> StartTextOnlyHeadAsync(CancellationToken ct)
    {
        if (started || configuration.ResumeID is not null || configuration.ResumeAt is not null || configuration.ResumePath is not null
            || configuration.Transcript.Count != 0 || configuration.Hydra is not null || configuration.IsHydraHead
            || configuration.SkillGeneration is not null || configuration.SkillMcpExecutable is not null || SkillLaunch is not null)
            throw new ArgumentException("A Pi text head requires a fresh private configuration.");
        textOnlyHead = true;
        try
        {
            var id = await Start().WaitAsync(ct);
            if (!textOnlyCommandAvailable || !IsRunning) throw new NotSupportedException("Pi did not load the private text-only guard.");
            var nonce = Guid.NewGuid().ToString("N");
            var ack = await Request(Obj(("type", "prompt"), ("message", "/" + PiTextOnlyHeadPolicy.Command + " " + nonce))).WaitAsync(ct);
            if (ack["success"]?.JSONValueBool != true || (ack["data"] ?? ack)["disposition"]?.JSONValueString != "handled")
                throw new NotSupportedException("Pi did not handle the text-only proof without a model turn.");
            var proof = await Request(Obj(("type", "get_messages"))).WaitAsync(ct);
            if (!IsRunning || !PiTextOnlyHeadPolicy.Verify(proof, nonce)) throw new NotSupportedException("Pi did not verify an empty tool set.");
            textOnlyVerified = true;
            return id;
        }
        catch { Stop(); throw; }
    }

    public async Task Send(TurnInput input) // :160-201
    {
        if (!IsRunning || process is not { } child) throw new ProviderError(new ProviderError.Case.NotRunning()); // :161
        if (standbyActive) throw new ProviderError(new ProviderError.Case.Failed("The standby is waiting for the switch and cannot run a turn."));
        if (textOnlyHead && (!textOnlyVerified || !input.IsFinalReport)) throw new ProviderError(new ProviderError.Case.Failed("The private Pi head requires a verified text-only turn."));
        if (turnActive) throw new ProviderError(new ProviderError.Case.Failed("Pi is still on the last turn.")); // :162
        runtimeMode = input.RuntimeMode; // :163
        interactionMode = input.InteractionMode; // :164
        WriteGate(input.IsFinalReport); // :165
        // :166-175
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
        // :176-179
        if (input.Effort is { Length: > 0 } wantedEffort && wantedEffort != effort && PiCLI.ThinkingLevels.Contains(wantedEffort))
        {
            child.Send(Obj(("type", "set_thinking_level"), ("level", wantedEffort)));
            effort = wantedEffort;
        }
        // :180-187
        turnActive = true;
        interruptRequested = false;
        turnError = null;
        turnCounter++;
        lastReplyText = "";
        toolNames.Clear();
        toolInputs.Clear();
        editSnapshots.RemoveAll();

        // :188
        var fields = new Dictionary<string, JSONValue> { ["type"] = new JSONValue.String("prompt"), ["message"] = new JSONValue.String(input.Text) };
        // :189-192
        var images = new List<JSONValue>();
        foreach (var image in input.Images)
        {
            if (!image.IsImage) continue;
            // Attachment.Url's DEVIATION note: real file I/O reads Path directly, never .Url (SwiftURL round-trips
            // a Windows path incorrectly for file:// URLs; Core/Models/Timeline.cs's own Base64Images does the same).
            if (SwiftTry.Optional(() => File.ReadAllBytes(image.Path)) is not { } data) continue;
            images.Add(Obj(("type", "image"), ("data", Convert.ToBase64String(data)), ("mimeType", image.MimeType)));
        }
        // :193-198
        if (images.Count != 0)
            fields["images"] = new JSONValue.Array(new ValueList<JSONValue>(images));
        var prompt = new JSONValue.Object(new ValueMap<string, JSONValue>(fields, SwiftStringComparer.Instance));
        child.Send(prompt); // :199
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null)); // :200. After the write, unlike Claude and Command Code.
    }

    public async Task Interrupt() // :203-206
    {
        interruptRequested = true;
        process?.Send(Obj(("type", "abort")));
        await Task.CompletedTask;
    }

    public async Task Compact() // :208-211
    {
        if (textOnlyHead || standbyActive || standbyDiscarding)
            throw new ProviderError(new ProviderError.Case.Failed("The standby is waiting for the switch and cannot run a turn."));
        if (!IsRunning || process is not { } child) throw new ProviderError(new ProviderError.Case.NotRunning());
        child.Send(Obj(("type", "compact")));
        await Task.CompletedTask;
    }

    public void Stop() // :213-223
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

    public Task<bool> StopAgent(string id) => Task.FromResult(false); // :225

    // PORT-ONLY: OD-0116, OD-0122, ED-439. Standby by injection through the app's load extension (`pi.sendMessage(..., { triggerTurn: false })`): no model turn.
    public Task<bool> SupportsStandbyInjection(CancellationToken ct) =>
        Task.FromResult(configuration.Executable is { } executable && !PiHistoryInjection.IsUnavailable(executable));

    public async Task<string> StartInjectedStandby(CancellationToken ct)
    {
        standbyActive = true;
        string id;
        try { id = await Start(); }
        catch
        {
            standbyActive = false;
            Stop();
            throw;
        }

        if (!loadCommandAvailable)
        {
            if (configuration.Executable is { } executable) PiHistoryInjection.MarkUnavailable(executable);
            await DiscardStandby();
            throw new StandbyUnavailableException("This Pi did not load the history command.");
        }

        return id;
    }

    public async Task InjectStandbyHistory(string text, CancellationToken ct)
    {
        if (!standbyActive || StandbyViolation is not null || !IsRunning || gateURL is not { } gate)
            throw new StandbyUnavailableException("The standby is no longer waiting.");
        ct.ThrowIfCancellationRequested();
        var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(gate)!, SwiftUUID.UuidString(Guid.NewGuid()) + ".load.txt");
        try
        {
            File.WriteAllText(file, text, new System.Text.UTF8Encoding(false));
            var handled = await Request(Obj(("type", "prompt"), ("message", "/droppy-load " + file)));
            var disposition = (handled["data"] ?? handled)["disposition"]?.JSONValueString;
            if (StandbyViolation is null && handled["success"]?.JSONValueBool == true && disposition == "handled")
            {
                var reply = await Request(Obj(("type", "get_messages")));
                var messages = (reply["data"] ?? reply)["messages"]?.JSONValueArray;
                if (StandbyViolation is null && messages?.Any(entry => entry["customType"]?.JSONValueString == "droppy-handoff"
                        && ToolText.Blocks(entry["content"]) == text) == true)
                    return;
            }
        }
        finally { SwiftTry.Ignore(() => File.Delete(file)); }

        if (configuration.Executable is { } executable && StandbyViolation is null) PiHistoryInjection.MarkUnavailable(executable);
        await DiscardStandby();
        throw new StandbyUnavailableException("Pi did not store the context in its history.");
    }

    public async Task DiscardStandby()
    {
        standbyActive = false;
        standbyDiscarding = true;
        var sessionFile = sessionID;
        var child = process;
        Stop();
        if (child is not null) _ = await Task.WhenAny(child.WaitForExit(), SwiftTask.TrySleep(TimeSpan.FromSeconds(2)));
        // The standby's own fresh session file goes with it; a resumed conversation is never touched.
        if (configuration.ResumeID is null && sessionFile is { Length: > 0 } && File.Exists(sessionFile))
            SwiftTry.Ignore(() => File.Delete(sessionFile));
    }

    public Task EndStandbyAsync(CancellationToken ct)
    {
        standbyActive = false;
        return Task.CompletedTask;
    }

    private void StandbyActivity(string kind)
    {
        if (StandbyViolation is not null) return;
        StandbyViolation = kind;
        OnStandbyViolation?.Invoke(kind);
        _ = DiscardStandby();
    }

    public void ResolveApproval(string requestID, string optionID) // :227-241
    {
        if (!pendingApprovals.Remove(requestID, out var request)) return;
        var option = request.Options.FirstOrDefault(candidate => candidate.Id == optionID);
        string value;
        if (option?.OptionRole == ApprovalRequest.Option.Role.ApproveAlways)
        {
            value = "Allow for session";
            sessionGrants.Add(request.RequestKind.GrantKey()); // :233. B3: the shared extension (ProviderSession.cs), not this file's own private GrantKey.
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

    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) // :243-254
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
    /// <c>.null</c> after 30 seconds, when pi exits, or when the caller is cancelled (PR-PROVCLI-275,
    /// PR-PROVCLI-276, PR-PROVCLI-299; B3 N1: through the shared PendingReplies, never throwing). Swift
    /// `private`; kept internal so this part's own tests can drive it without a live child process for the
    /// no-process case (see the file header note).</summary>
    internal async Task<JSONValue> Request(JSONValue fields) // :258-270
    {
        if (process is not { } child) return new JSONValue.Null(); // :261
        requestCounter++; // :262
        var id = $"droppy-{requestCounter}"; // :263. DEVIATION OD-0012 F1: "droppy-", not "marrow-" (kept, PR-PROVCLI-276).
        var line = new Dictionary<string, JSONValue>(fields.JSONValueObject ?? new Dictionary<string, JSONValue>(), SwiftStringComparer.Instance)
        {
            ["id"] = new JSONValue.String(id),
        };
        // :266-269. `try? await pendingResponses.register(...)`: PendingReplies.Register only ever throws
        // CancellationError (a hung timer resolves with the JSON null timeoutValue instead of throwing, and
        // DidClose's ResolveAll below also resolves rather than fails), so SwiftTry.OptionalAsync's `try?`
        // exists here for exactly the caller-cancellation path PR-PROVCLI-276 describes: an already-cancelled
        // caller writes no line at all (PendingReplies.Register checks cancellation before sending).
        var value = await SwiftTry.OptionalAsync(async () => (JSONValue?)await pendingResponses.Register(
            id,
            () => child.Send(new JSONValue.Object(new ValueMap<string, JSONValue>(line, SwiftStringComparer.Instance))),
            TimeSpan.FromSeconds(30),
            new JSONValue.Null()));
        return value ?? new JSONValue.Null(); // :269
    }

    /// <summary>The gate the extension reads before every tool call.</summary>
    private void WriteGate(bool finalReport) // :272-282
    {
        if (gateURL is not { } path) return;
        var gate = Obj(
            ("runtimeMode", SwiftEnum<RuntimeMode>.RawValue(runtimeMode)),
            ("interactionMode", SwiftEnum<InteractionMode>.RawValue(interactionMode)),
            ("finalReport", finalReport),
            ("systemPrompt", ""));
        SwiftTry.Ignore(() => FileOps.WriteAtomic(path, gate.Data()));
    }

    // MARK: - Stream

    /// <summary>Swift `private`; kept internal so this part's own tests can drive dispatch without a live child
    /// process (see the file header note).</summary>
    internal void Handle(JSONValue message) // :286-326
    {
        if (textOnlyHead && (message["type"]?.JSONValueString is "tool_execution_start" or "extension_ui_request"
            || !textOnlyVerified && message["type"]?.JSONValueString is "agent_start" or "turn_start"))
        {
            var report = OnEvent;
            Stop();
            report?.Invoke(new ProviderEvent.Exited("Pi violated the private text-only policy."));
            return;
        }
        // PORT-ONLY (ED-439): fail closed. Only replies to the app's own requests pass; any agent or tool activity on a waiting session stops and discards it.
        if ((standbyActive || standbyDiscarding) && message["type"]?.JSONValueString is { } waitingKind && waitingKind != "response")
        {
            if (standbyActive && (waitingKind is "agent_start" or "turn_start" or "tool_execution_start" or "extension_ui_request"
                || waitingKind == "message_start" && message["message"]?["role"]?.JSONValueString == "assistant"))
                StandbyActivity(waitingKind);
            return;
        }

        switch (message["type"]?.JSONValueString)
        {
            case "response": // :288-294
                if (message["id"]?.JSONValueString is not { } responseID) return;
                if (!pendingResponses.Resolve(responseID, message) &&
                    message["command"]?.JSONValueString == "prompt" && message["success"]?.JSONValueBool == false)
                {
                    turnError = message["error"]?.JSONValueString ?? message["message"]?.JSONValueString ?? "Pi reported an error.";
                    FinishTurn();
                }
                break;
            case "message_start": // :295-298
                if (message["message"]?["role"]?.JSONValueString != "assistant") return;
                stream.StartMessage();
                stream.ResetThinking();
                break;
            case "message_update": // :299-300
                HandleUpdate(message);
                break;
            case "message_end": // :301-302
                HandleMessageEnd(message["message"] ?? message);
                break;
            case "tool_execution_start": // :303-304
                HandleToolStart(message);
                break;
            case "tool_execution_update": // :305-308
                if (message["toolCallId"]?.JSONValueString is { } toolID)
                {
                    var text = ToolText.Blocks(message["partialResult"]?["content"]);
                    if (text.Length != 0) OnEvent?.Invoke(new ProviderEvent.ToolOutput(toolID, text));
                }
                break;
            case "tool_execution_end": // :309-310
                HandleToolEnd(message);
                break;
            case "agent_end": // :311-312
                FinishTurn();
                break;
            case "compaction_start": // :313-314
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Compacting the conversation\u2026" })); // U+2026 HORIZONTAL ELLIPSIS
                break;
            case "compaction_end": // :315-316
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Conversation compacted" }));
                break;
            case "auto_retry_start": // :317-318
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = message["error"]?.JSONValueString ?? "Pi is retrying." }));
                break;
            case "extension_error": // :319-320
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = message["error"]?.JSONValueString ?? "Pi reported an extension error." }));
                break;
            case "extension_ui_request": // :321-322
                HandleExtensionUI(message);
                break;
            default: // :323-324
                break;
        }
    }

    internal void HandleUpdate(JSONValue message) // :328-352
    {
        var eventValue = message["assistantMessageEvent"] ?? message["event"] ?? new JSONValue.Null();
        switch (eventValue["type"]?.JSONValueString)
        {
            case "text_delta": // :331-333. B3 (N7): stream.AppendMessage mints the id (a null current id gets a
                                // fresh one) and appends the delta, replacing this file's own counter/id/buffer.
                if (eventValue["delta"]?.JSONValueString is not { Length: > 0 } delta) break;
                OnEvent?.Invoke(new ProviderEvent.MessageDelta(stream.AppendMessage(delta), delta));
                break;
            case "thinking_delta": // :334-336. stream.AppendThinking's resetsText defaults to true, matching Pi's
                                    // buffer-emptying fallback (SessionSupport.cs :104-106).
                if (eventValue["delta"]?.JSONValueString is not { Length: > 0 } thinkingDelta) break;
                OnEvent?.Invoke(new ProviderEvent.ReasoningDelta(stream.AppendThinking(thinkingDelta), thinkingDelta));
                break;
            default: // :337-338
                break;
        }
        // :340-351. One sum of four fallback pairs, named separately: the swiftc type checker gives up on the
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

    internal void HandleMessageEnd(JSONValue message) // :354-376
    {
        if (message["role"]?.JSONValueString != "assistant") return;
        var finalText = stream.MessageText; // :356
        if (message["content"]?.JSONValueArray is { } blocks)
        {
            var joined = string.Concat(blocks
                .Where(block => block["type"]?.JSONValueString == "text")
                .Select(block => block["text"]?.JSONValueString)
                .Where(text => text is not null));
            if (joined.Length != 0) finalText = joined;
        }
        if (stream.CurrentThinkingID is { } thinkingID && stream.ThinkingText.Trimmed().Length != 0) // :364-365
            OnEvent?.Invoke(new ProviderEvent.ReasoningCompleted(thinkingID, stream.ThinkingText));
        stream.EndThinking(); // :367
        if (stream.CurrentMessageID is { } messageID) // :368-370
        {
            OnEvent?.Invoke(new ProviderEvent.MessageCompleted(messageID, finalText));
            lastReplyText = finalText;
        }
        stream.EndMessage(); // :372
        if (message["stopReason"]?.JSONValueString == "error") // :373-374
            turnError = message["errorMessage"]?.JSONValueString ?? "Pi reported an error.";
    }

    /// <summary>The live argv with the bundle appended when a launch is set; the same list otherwise.</summary>
    internal List<string> WithSkills(List<string> arguments) => SkillLaunch is { } skills ? ProviderSkillArguments.Pi(arguments, skills) : arguments;

    /// <summary>The live session argument vector, :93-101 (moved out of Start unchanged so a test can pin it).</summary>
    internal static List<string> LiveArguments(string extensionPath, RuntimeMode runtimeMode, string? model, string? effort, string? sessionID)
    {
        var arguments = new List<string> { "--mode", "rpc", "-e", extensionPath };
        arguments.Add(runtimeMode is RuntimeMode.Auto or RuntimeMode.FullAccess ? "--approve" : "--no-approve");
        // :95-97. The split is on the FIRST slash; a slashless id or the literal "default" adds neither flag.
        if (model is { Length: > 0 } selectedModel && selectedModel != "default" && selectedModel.IndexOf('/', StringComparison.Ordinal) is var modelSlash && modelSlash >= 0)
        {
            arguments.Add("--provider");
            arguments.Add(selectedModel[..modelSlash]);
            arguments.Add("--model");
            arguments.Add(selectedModel[(modelSlash + 1)..]);
        }
        // :98-100
        if (effort is { Length: > 0 } selectedEffort && PiCLI.ThinkingLevels.Contains(selectedEffort))
        {
            arguments.Add("--thinking");
            arguments.Add(selectedEffort);
        }
        // :101. No emptiness check here: init already normalized an empty id to null.
        if (sessionID is { } resumeID)
        {
            arguments.Add("--session");
            arguments.Add(resumeID);
        }

        return arguments;
    }

    internal void HandleToolStart(JSONValue message) // :378-420
    {
        Tap.ToolStarted(message); // PORT-ONLY: SK-11e / DL-12
        if (message["toolCallId"]?.JSONValueString is not { } id || message["toolName"]?.JSONValueString is not { } name) return;
        var args = message["args"] ?? message["input"] ?? new JSONValue.Null();
        toolNames[id] = name;
        toolInputs[id] = args;
        if (name == "write" && args["path"]?.JSONValueString is { } writePath) // :383-385. B3 (N7): the shared
                                                                                // EditSnapshots.Capture (its own
                                                                                // 1 MB text-file read), replacing
                                                                                // this file's own Snapshot helper.
            editSnapshots.Capture(id, writePath);

        var kind = name switch // :386-393
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
        if (name == "bash") // :397-399
        {
            title = ToolTitles.UnwrapShell(args["command"]?.JSONValueString ?? "bash");
            detail = null;
        }
        else if (name is "read" or "edit" or "write") // :400-402
        {
            title = ToolTitles.RelativePath(args["path"]?.JSONValueString ?? name, WorkingDirectory);
            detail = null;
        }
        else if (name is "grep" or "find") // :403-405
        {
            title = args["pattern"]?.JSONValueString ?? name;
            detail = null;
        }
        else if (name == "ls") // :406-408
        {
            title = args["path"]?.JSONValueString ?? ".";
            detail = null;
        }
        else if (name.StartsWith("mcp__", StringComparison.Ordinal)) // :409-414
        {
            // SwiftString.Split takes a single Character separator; "__" is two, so Swift's
            // split(separator: "__", omittingEmptySubsequences: true) ports as a plain multi-char split (O-13).
            var parts = name.Split("__", StringSplitOptions.RemoveEmptyEntries);
            var server = parts.Length > 1 ? parts[1] : "MCP";
            var tool = parts.Length > 2 ? string.Join("__", parts.Skip(2)) : name;
            title = $"{server} \u00B7 {tool}"; // U+00B7 MIDDLE DOT
            detail = args.IsNull || args.CompactString == "{}" ? null : args.PrettyString;
        }
        else // :415-417
        {
            title = name;
            detail = args.IsNull || args.CompactString == "{}" ? null : args.PrettyString;
        }
        OnEvent?.Invoke(new ProviderEvent.ToolStarted(id, new ToolCall { ToolCallKind = kind, Title = title, Detail = detail })); // :419
    }

    internal void HandleToolEnd(JSONValue message) // :422-437
    {
        Tap.ToolEnded(message); // PORT-ONLY: SK-11e / DL-12
        if (message["toolCallId"]?.JSONValueString is not { } id) return;
        var result = message["result"] ?? new JSONValue.Null();
        var output = ToolText.Blocks(result["content"]); // :425. B3: the shared ToolText.Blocks (ProviderSession.cs),
                                                           // replacing this file's own private TextIn helper.
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
    private ValueList<FileEdit>? EditDiff(string name, JSONValue args, string id) // :441-456
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
                var before = editSnapshots.Text(id); // :450. B3 (N7): EditSnapshots.Text then Discard, replacing
                editSnapshots.Discard(id);            // :451. this file's own single Dictionary.Remove.
                return new ValueList<FileEdit>([UnifiedDiff(writePath, before, content)]);
            default:
                return null;
        }
    }

    private static FileEdit UnifiedDiff(string path, string old, string @new) // :458-466
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

    private void FinishTurn() // :468-486
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

    private void HandleExtensionUI(JSONValue message) // :490-537
    {
        if ((message["id"]?.JSONValueString ?? message["requestId"]?.JSONValueString) is not { } id) return;
        if (message["method"]?.JSONValueString is not { } method) return;
        var parameters = message["params"] ?? message;
        switch (method)
        {
            case "select": // :495-496
                HandleSelect(id, parameters);
                break;
            case "confirm": // :497-511
                var title = parameters["title"]?.JSONValueString ?? "Pi";
                var prompt = string.Join("\n", new[] { title, parameters["message"]?.JSONValueString }.Where(part => !string.IsNullOrEmpty(part)));
                pendingQuestions[id] = "confirm";
                OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(id, new ValueList<QuestionRequest.Question>([
                    new QuestionRequest.Question(id, "Pi", prompt, new ValueList<QuestionRequest.Choice>([ // :504 header "Pi" (UI-PROVCLI-012)
                        new QuestionRequest.Choice("Yes"), new QuestionRequest.Choice("No"), // :506 "Yes"/"No" choices (UI-PROVCLI-010, UI-PROVCLI-011)
                    ]), AllowsMultiple: false, AllowsOther: false, IsSecret: false),
                ]))));
                break;
            case "input" or "editor": // :512-524
                pendingQuestions[id] = method;
                OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(id, new ValueList<QuestionRequest.Question>([
                    new QuestionRequest.Question(id, "Pi", parameters["title"]?.JSONValueString ?? "Pi", ValueList<QuestionRequest.Choice>.Empty, // :517-518 header/prompt default "Pi" (UI-PROVCLI-012)
                        AllowsMultiple: false, AllowsOther: true, IsSecret: false),
                ]))));
                break;
            case "notify": // :525-531
                var level = parameters["notifyType"]?.JSONValueString switch
                {
                    "warning" => Notice.Level.Warning,
                    "error" => Notice.Level.Error,
                    _ => Notice.Level.Info,
                };
                OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = level, Message = parameters["message"]?.JSONValueString ?? "Pi sent a notification." }));
                break;
            case "setStatus" or "setWidget" or "setTitle" or "set_editor_text": // :532-533
                break;
            default: // :534-535
                break;
        }
    }

    private void HandleSelect(string id, JSONValue parameters) // :539-591
    {
        var title = parameters["title"]?.JSONValueString ?? "";
        var options = (parameters["options"]?.JSONValueArray ?? []).Select(item => item.JSONValueString).Where(item => item is not null).Select(item => item!).ToList();
        if (!title.StartsWith("droppy-gate ", StringComparison.Ordinal) ||
            JSONValue.Parse(title["droppy-gate ".Length..]) is not { } payload)
        {
            pendingQuestions[id] = "select";
            OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(id, new ValueList<QuestionRequest.Question>([
                new QuestionRequest.Question(id, "Pi", title, new ValueList<QuestionRequest.Choice>(options.Select(option => new QuestionRequest.Choice(option))), // :547 header "Pi" (UI-PROVCLI-012)
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
        if (kind == ApprovalRequest.Kind.Command) // :568-570
        {
            callTitle = ToolTitles.UnwrapShell(input["command"]?.JSONValueString ?? toolName);
            callDetail = input["command"]?.JSONValueString;
        }
        else if (kind == ApprovalRequest.Kind.FileChange) // :571-573
        {
            callTitle = ToolTitles.RelativePath(input["path"]?.JSONValueString ?? toolName, WorkingDirectory);
            callDetail = input["path"]?.JSONValueString;
        }
        else // :574-576
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
        Tap.Held(payload); // PORT-ONLY: SK-11e / DL-12
        OnEvent?.Invoke(new ProviderEvent.Approval(approval)); // :590
    }

    /// <summary>Test seam for this part's own tests: the turn-active state <c>Send()</c> sets at :180-187,
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

    /// <summary>Sets the lifecycle flag for the PR-PROVCLI-300 predicate test, without a process launch.</summary>
    internal void TestSetStartedForTesting(bool value) => started = value;

    // Swift `private`; kept internal so this part's own tests can drive it without tearing down a live process
    // (per the file's established seam pattern, e.g. Handle, HandleToolEnd).
    internal void DidClose(int status) // :593-609
    {
        // :594. B3 (N1, PR-PROVCLI-299): every waiting request is released with JSON null FIRST, so a handshake
        // or model-switch get_state still waiting when pi exits returns at once (PR-PROVCLI-276) instead of
        // hanging for the rest of its 30 second timeout.
        pendingResponses.ResolveAll(new JSONValue.Null());
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
