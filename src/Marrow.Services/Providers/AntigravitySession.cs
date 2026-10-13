using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;

// Source: DroppyCode/Services/Providers/AntigravitySession.swift (B3 52bc792), :1-719.

namespace Marrow.Services.Providers;

/// <summary>
/// Drives Google's Antigravity CLI (`agy`) in headless streaming mode. The CLI speaks newline-delimited JSON on stdio:
/// one persistent process per thread with `--input-format stream-json --output-format stream-json`, one
/// `{"event":"user","message":{"content":"..."}}` object per turn on stdin, and `init` / `step_update` / `result`
/// events on stdout. (:3-13)
/// </summary>
// :14-719. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class AntigravitySession : ProviderSession, Port.Skills.Providers.ISkillSessionLifetime
{
    public Action<ProviderEvent>? OnEvent { get; set; } // :16

    private readonly SessionConfiguration configuration;          // :18
    private StdioProcess? process;                                 // :19
    private TaskHandle? readTask;                                  // :20
    private CheckedContinuation<string>? initContinuation;         // :21
    private string? conversationID;                                // :22
    // UPSTREAM DEFECT 2, reproduced: runtimeMode and interactionMode are written (init, send) but never read; start()
    // builds its mode flags from the configuration (PR-PROVCLI-323).
    private RuntimeMode runtimeMode;                               // :23
    private InteractionMode interactionMode;                       // :24
    private string? model;                                         // :25
    private string? effort;                                        // :26
    private bool turnActive;                                       // :27
    private bool interruptRequested;                               // :28
    private bool isStopping;                                       // :29
    // Resolves the cumulative session token total into per-turn spend. :30-31
    private TokenSpendTracker spendTracker = new();
    // Whether the cumulative total's baseline is known. A launch with `--conversation` resumes a conversation whose
    // token total carries its history, and the protocol does not say how much that is, so the first total is used as
    // the baseline and nothing is charged for it. :32-36
    private bool spendBaselineKnown;
    // Namespaces step ids per launch. `step_index` counts up within one process but a resumed conversation starts
    // over, and a repeated id would stream a new turn's text into an old entry of the thread. :37-40
    private readonly string launchID = SwiftString.Prefix(SwiftUUID.UuidString(Guid.NewGuid()), 8);
    // Whether any reply text streamed this turn; without it, the result's `response` is the reply. :41-43
    private bool streamedText;
    // Files an edit step is about to change, captured when the step went active. The stream never carries the edit
    // itself, so the row's diff is the file before against the file after. :44-47
    // B3 (unit N7): the local map and its reader (B2 :267-273) move onto the shared SessionSupport.EditSnapshots
    // (ENGINEERING DECISION, recommended so there is one implementation). :47
    private readonly EditSnapshots editSnapshots = new();
    // Tool steps whose start was announced, so a step that surfaces only as finished still gets its proper row rather
    // than a bare "Tool". :48-50
    private readonly HashSet<string> startedTools = new(SwiftStringComparer.Instance);

    // Reasoning efforts `agy --effort` accepts. :52-53
    public static readonly ValueList<string> Efforts = new(new[] { "low", "medium", "high" });

    public AntigravitySession(SessionConfiguration configuration) // :55-62
    {
        this.configuration = configuration;
        runtimeMode = configuration.RuntimeMode;
        interactionMode = configuration.InteractionMode;
        model = configuration.Model;
        effort = configuration.Effort;
        spendBaselineKnown = configuration.ResumeID is not { Length: > 0 }; // resumeID?.isEmpty != false
    }

    public bool IsRunning => process?.IsRunning ?? false; // :64

    // PORT-ONLY: SK-17. Stop requests termination; generation readers end only when the child has actually exited.
    public Task SkillReadersExited => process is { IsRunning: true } child ? child.WaitForExit() : Task.CompletedTask;

    private string WorkingDirectory => configuration.WorkingDirectory.Path; // :66

    // MARK: - Lifecycle

    public async Task<string> Start() // :70-120
    {
        if (configuration.Executable is not { } executable) throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider)); // :71
        // `--add-dir` is what makes the thread's folder the workspace. The CLI does not adopt its cwd (verified live,
        // git repo or not): without the flag the agent works in `~/.gemini/antigravity-cli/scratch` and spends its
        // first steps hunting the home directory for the project. :72-81
        List<string> arguments =
        [
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--print-timeout", "30m",
            "--add-dir", WorkingDirectory,
        ];
        // DEVIATION OD-0099: SK-17 live check proved discovery under the second root's .agents/skills.
        // The immutable Marrow-owned tree is passed per process; no provider configuration is written.
        if (configuration.SkillGeneration is { } skills)
            arguments.AddRange(["--add-dir", Port.Skills.Providers.SessionSkillOverlay.RequireFolder(skills, "agy")]);
        // Scoping to the connected servers happens through the generated settings file `ProviderRegistry` hands the
        // CLI in `GEMINI_CLI_SYSTEM_SETTINGS_PATH`: it lists those servers and no others, so the user's own stay
        // untouched. `agy` has no `--allowed-mcp-server-names` flag (that one is Claude's) and its flag parser exits 2
        // with usage text on an unknown flag, which killed every Antigravity session as soon as one MCP server was
        // connected. :82-87
        var (resolvedModel, resolvedEffort) = Normalize(model, effort); // :88
        if (resolvedModel is { Length: > 0 }) arguments.AddRange(["--model", resolvedModel]); // :89
        if (resolvedEffort is { Length: > 0 }) arguments.AddRange(["--effort", resolvedEffort]); // :90
        // UPSTREAM DEFECT 2, reproduced: the launch-time modes, not the stored ones. :91
        arguments.AddRange(ModeArguments(configuration.RuntimeMode, configuration.InteractionMode));
        if (configuration.ResumeID is { Length: > 0 } resumeID) arguments.AddRange(["--conversation", resumeID]); // :92-94

        // PORT-ONLY (Tactus, ED-NEW-pcctl-41; DESIGN.md 3.4): with built-in servers (nth-pc while Tactus is on, a head's
        // advisor) the CLI reads a per-chat copy of the system settings with them added; without, the launch is as before.
        var environment = configuration.Environment;
        if (MCP.MCPProviderConfig.GeminiSessionSettings(MCP.MCPProviderConfig.BuiltInServers(configuration), configuration.IgnisThreadID) is { } settings)
            environment = environment.Mutating(map =>
            {
                foreach (var key in map.Keys.Where(k => string.Equals(k, "GEMINI_CLI_SYSTEM_SETTINGS_PATH", StringComparison.OrdinalIgnoreCase)).ToList()) map.Remove(key);
                map["GEMINI_CLI_SYSTEM_SETTINGS_PATH"] = settings;
            });
        var process = new StdioProcess(executable.Path, arguments, configuration.WorkingDirectory.Path, environment); // :96-101
        this.process = process; // :102
        isStopping = false;     // :103
        await process.StartAsync(); // :104. ED-NEW-perf-ui-thread-7: CreateProcessW runs on the pool, not on the UI thread's turn.
        // A Stop or an Interrupt that came while the child was being created found nothing running to end: end it now that it exists. The reader below then
        // fails the start through DidClose, as it does for a Stop that comes while the session waits for the init line.
        if (isStopping) process.Terminate();
        var messages = process.Messages; // :105
        readTask = MainActor.Spawn(async () => // :106-112
        {
            await foreach (var message in messages) Handle(message);
            DidClose();
        });
        return await CheckedContinuation<string>.With(continuation => // :113-119
        {
            initContinuation = continuation;
            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(30));
                FailStartAfterTimeout();
            });
        });
    }

    /// <summary>`agy --mode` only knows `accept-edits` and `plan`: the default review mode is the absence of the flag.
    /// Full access additionally approves every tool, since headless runs soft-deny unapproved actions instead of
    /// prompting. (:122-125)</summary>
    private List<string> ModeArguments(RuntimeMode runtimeMode, InteractionMode interaction) // :126-142
    {
        List<string> arguments = [];
        if (interaction == InteractionMode.Plan)
        {
            arguments.AddRange(["--mode", "plan"]);
        }
        else
        {
            switch (runtimeMode)
            {
                case RuntimeMode.Supervised:
                    break;
                case RuntimeMode.AutoAcceptEdits:
                case RuntimeMode.Auto:
                case RuntimeMode.FullAccess:
                    arguments.AddRange(["--mode", "accept-edits"]);
                    break;
            }
        }
        // provider-argv-and-handshakes.md section 10 item 9: appended even in plan mode, as the Swift does. :138-140
        if (runtimeMode == RuntimeMode.FullAccess) arguments.Add("--dangerously-skip-permissions");
        return arguments;
    }

    private void FailStartAfterTimeout() // :144-149
    {
        if (initContinuation is not { } continuation) return;
        initContinuation = null;
        process?.Terminate();
        continuation.Resume(new ProviderError(new ProviderError.Case.Failed("Antigravity did not answer. Is `agy` signed in? Run `agy` once in Terminal.")));
    }

    public Task Send(TurnInput input) // :151-164
    {
        if (process is not { IsRunning: true } running) return Task.FromException(new ProviderError(new ProviderError.Case.NotRunning())); // :152
        runtimeMode = input.RuntimeMode;         // :153
        interactionMode = input.InteractionMode; // :154
        if (input.Model is { Length: > 0 } turnModel) model = turnModel;     // :155
        if (input.Effort is { Length: > 0 } turnEffort) effort = turnEffort; // :156
        // Stream-json only takes text blocks; anything else ends the session with an error, so images never go on
        // the wire (see supportsImages). :157-158
        turnActive = true;          // :159
        interruptRequested = false; // :160
        streamedText = false;       // :161
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null)); // :162
        running.Send(JSONValue.FromDictionaryLiteral( // :163
            ("event", "user"),
            ("message", JSONValue.FromDictionaryLiteral(("content", new JSONValue.String(input.Text))))));
        return Task.CompletedTask;
    }

    public Task Interrupt() // :166-178
    {
        if (!turnActive) return Task.CompletedTask;
        interruptRequested = true;
        // The process going is the stop, not a crash: without this `didClose` would follow the interrupted turn with
        // `.exited` and the chat would read "Antigravity stopped unexpectedly" every time the user stops one. `start`
        // clears the flag again for the next launch. :169-172
        isStopping = true;
        // The stream has no interrupt request: stopping the process ends the turn, and the conversation persists
        // server-side for the next turn's `--conversation` resume. :174-176
        process?.Terminate();
        return Task.CompletedTask;
    }

    public Task Compact() => // :180-182
        Task.FromException(new ProviderError(new ProviderError.Case.Failed("Antigravity manages its own context.")));

    public void ResolveApproval(string requestID, string optionID) { } // :184
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { } // :185

    public void Stop() // :187-190
    {
        isStopping = true;
        process?.Terminate();
    }

    // MARK: - Stream

    private void Handle(JSONValue message) // :194-211
    {
        var eventName = message["event"]?.JSONValueString;
        if (Is(eventName, "init")) // :196-203
        {
            if (message["conversation_id"]?.JSONValueString is not { Length: > 0 } id) return;
            conversationID = id;
            if (initContinuation is { } continuation)
            {
                initContinuation = null;
                continuation.Resume(id);
            }
            OnEvent?.Invoke(new ProviderEvent.SessionReady(id));
        }
        else if (Is(eventName, "step_update")) // :204-205
        {
            HandleStep(message["step_update"] ?? new JSONValue.Null());
        }
        else if (Is(eventName, "result")) // :206-207
        {
            HandleResult(message["result"] ?? new JSONValue.Null());
        }
    }

    private void HandleStep(JSONValue step) // :213-254
    {
        var index = step["step_index"]?.JSONValueInt ?? 0; // :214
        var stepType = step["step_type"]?.JSONValueString;
        if (Is(stepType, "agent_response")) // :216-220
        {
            if (ContextUsage(step["usage"]) is { } usage) OnEvent?.Invoke(new ProviderEvent.Usage(usage));
            if (step["text_delta"]?.JSONValueString is not { Length: > 0 } text) return;
            streamedText = true;
            OnEvent?.Invoke(new ProviderEvent.MessageDelta($"agy-{launchID}-message-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}", text));
        }
        else if (Is(stepType, "tool")) // :221-248
        {
            if ((step["tool_name"]?.JSONValueString ?? step["tool_info"]?["name"]?.JSONValueString) is not { } name) return; // :222
            var id = $"agy-{launchID}-tool-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"; // :223
            var info = step["tool_info"] ?? new JSONValue.Null();          // :224
            var parameters = info["parameters"] ?? new JSONValue.Null();   // :225
            var error = info["error"]?["message"]?.JSONValueString;        // :226
            var state = step["state"]?.JSONValueString;                    // :227
            if (!startedTools.Contains(id)) // :228-236
            {
                startedTools.Add(id);
                var call = MakeToolCall(name, parameters);
                if (call.ToolCallKind == ToolCall.Kind.Edit && Is(state, "ACTIVE") && Path(parameters) is { } path)
                    editSnapshots.Capture(id, path); // :232
                OnEvent?.Invoke(new ProviderEvent.ToolStarted(id, call));
                if (Is(state, "ACTIVE")) return;
            }
            // Errors ride on the `error` field (the documented shape) as well as an `ERROR` state, so either ends the
            // call as a failure. :237-242
            if (Is(state, "ERROR") || (Is(state, "DONE") && error is not null))
            {
                var failure = error ?? "The tool failed.";
                var status = IsDenied(failure) ? ToolCall.Status.Declined : ToolCall.Status.Failed;
                OnEvent?.Invoke(new ProviderEvent.ToolUpdated(id, new ToolUpdate { Output = failure, Status = status, Edits = FinishEdit(id, parameters) }));
            }
            else if (Is(state, "DONE")) // :243-248
            {
                var update = new ToolUpdate { Status = ToolCall.Status.Completed };
                if (info["output"]?.JSONValueString is { Length: > 0 } output) update = update with { Output = output };
                update = update with { Edits = FinishEdit(id, parameters) };
                OnEvent?.Invoke(new ProviderEvent.ToolUpdated(id, update));
            }
        }
        // default: `user_input` acknowledgements and `checkpoint` markers carry nothing Droppy Code renders. :249-252
    }

    /// <summary>The finished edit as a diff of the file before against after, for the row's inline diff and the turn's
    /// changed-files attribution. Nil for non-edit steps, so the update leaves the call's edits alone. (:256-258)</summary>
    // B3: delegates to the shared SessionSupport.EditSnapshots.Finish; the before/after re-read and the
    // path-only-when-unchanged result are identical (B2 finishEdit :256-265, snapshot :267-273 -> B3 :259-262 here
    // plus SessionSupport.cs:27-40; PR-PROVCLI-332/333, refactor with no observable change).
    private ValueList<FileEdit>? FinishEdit(string id, JSONValue parameters) // :259-262
    {
        if (Path(parameters) is not { } path) return null;
        return editSnapshots.Finish(id, path, WorkingDirectory);
    }

    /// <summary>The soft-deny wording headless runs use when a permission rule blocks a tool: the agent asked, the user
    /// (or a rule) said no. (:264-265)</summary>
    private static bool IsDenied(string message) // :266-270
    {
        var lowered = SwiftString.Lowercased(message);
        return lowered.Contains("permission check failed", StringComparison.Ordinal) || lowered.Contains("denied permission", StringComparison.Ordinal)
            || lowered.Contains("permission denied", StringComparison.Ordinal);
    }

    /// <summary>The context in use after a model call: the step's fresh input plus what was served from cache, plus the
    /// reply. Result totals are the session's cumulative spend and say nothing about the window. (:272-274)</summary>
    private static ContextUsage? ContextUsage(JSONValue? usage) // :275-280
    {
        if (usage is null || usage.IsNull) return null;
        var used = (usage["input_tokens"]?.JSONValueInt ?? 0) + (usage["cache_read_tokens"]?.JSONValueInt ?? 0) + (usage["output_tokens"]?.JSONValueInt ?? 0);
        if (used <= 0) return null;
        return new ContextUsage { UsedTokens = used, WindowTokens = null };
    }

    private void HandleResult(JSONValue result) // :282-352
    {
        turnActive = false;        // :283
        editSnapshots.RemoveAll(); // :284
        startedTools.Clear();      // :285
        if (result["usage"] is { IsNull: false } usage) // :286-311
        {
            // Totals are cumulative over the session (per the headless docs), so the spend tracker resolves them into
            // per-turn deltas, but only once its baseline is known. A conversation resumed with `--conversation`
            // reports a total that already holds the history it resumed with, and the protocol does not say how much
            // that is: the first total seeds the baseline and is left uncounted, rather than charged as new spend or
            // reported as zero. The context meter is fed per step instead (see handleStep). :287-293
            var used = usage["total_tokens"]?.JSONValueInt ?? ((usage["input_tokens"]?.JSONValueInt ?? 0) + (usage["output_tokens"]?.JSONValueInt ?? 0));
            if (used > 0)
            {
                if (spendBaselineKnown)
                {
                    var (next, spend) = spendTracker.Spend(used);
                    spendTracker = next;
                    if (spend > 0)
                    {
                        TokenLedger.Shared.Current.Record(spend); // :299
                        OnEvent?.Invoke(new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Antigravity, spend, model: model))); // :300-304
                    }
                }
                else
                {
                    spendTracker = spendTracker.SeedBaseline(used); // :307
                    spendBaselineKnown = true;                      // :308
                }
            }
        }
        // A turn whose reply never streamed (resumed conversations can answer in the result alone) still shows its
        // text. :312-317
        if (!streamedText && result["response"]?.JSONValueString is { } response && response.Trimmed().Length != 0)
            OnEvent?.Invoke(new ProviderEvent.MessageCompleted($"agy-{launchID}-result-{SwiftString.Prefix(SwiftUUID.UuidString(Guid.NewGuid()), 8)}", response));
        streamedText = false; // :318
        var denied = new List<string>(); // :319
        foreach (var action in result["denied_actions"]?.JSONValueArray ?? [])
        {
            if ((action["display_name"]?.JSONValueString ?? action["action"]?.JSONValueString) is { } name) denied.Add(name);
        }
        if (denied.Count != 0) // :320-326
        {
            var names = string.Join(", ", denied);
            OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
            {
                NoticeLevel = Notice.Level.Warning,
                Message = $"Antigravity held back {names} without prompting. Use Full access or add an allow rule in the CLI's settings.",
            }));
        }
        if (result["conversation_id"]?.JSONValueString is { Length: > 0 } id) // :327-330
        {
            conversationID = id;
            OnEvent?.Invoke(new ProviderEvent.SessionReady(id));
        }
        if (interruptRequested) // :331-332
        {
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
        }
        else // :333-350
        {
            var status = result["status"]?.JSONValueString;
            if (Is(status, "WAITING") && denied.Count != 0)
            {
                // Stopped for a permission nobody could grant headless, with actions held back: reported as a failure
                // so a head's lead hears it was starved of permissions rather than reading a clean completion. :336-338
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed,
                    $"Antigravity held back {string.Join(", ", denied)} waiting for a permission it could not ask for. Use Full access for this chat."));
            }
            else if (Is(status, "SUCCESS") || Is(status, "WAITING"))
            {
                // WAITING: the agent stopped to ask something it cannot ask headless; the turn is over and the reply
                // says what it needs. :341-342
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            }
            else if (Is(status, "CANCELED") || Is(status, "INTERRUPTED"))
            {
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
            }
            else
            {
                var error = result["error"]?.JSONValueString;
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, error is { Length: > 0 } ? error : "Antigravity stopped before finishing."));
            }
        }
        interruptRequested = false; // :351
    }

    private void DidClose() // :354-372
    {
        if (initContinuation is { } continuation) // :355-359
        {
            initContinuation = null;
            var tail = process?.ErrorTail ?? "";
            continuation.Resume(new ProviderError(new ProviderError.Case.Failed(AuthHint(tail) ?? (TextCleanup.LastLines(tail) ?? "Antigravity exited."))));
        }
        if (turnActive) // :360-369
        {
            turnActive = false;
            if (interruptRequested)
            {
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
            }
            else
            {
                var tail = process?.ErrorTail ?? "";
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, AuthHint(tail) ?? TextCleanup.LastLines(tail) ?? "Antigravity exited."));
            }
            interruptRequested = false;
        }
        if (isStopping) return; // :370
        OnEvent?.Invoke(new ProviderEvent.Exited(TextCleanup.LastLines(process?.ErrorTail ?? ""))); // :371
    }

    private static string? AuthHint(string text) // :374-379
    {
        var lowered = SwiftString.Lowercased(text);
        if (!(lowered.Contains("authentication required", StringComparison.Ordinal) || lowered.Contains("not signed in", StringComparison.Ordinal)
            || lowered.Contains("unauthenticated", StringComparison.Ordinal)
            || (lowered.Contains("login", StringComparison.Ordinal) && lowered.Contains("agy", StringComparison.Ordinal)))) return null;
        return "Antigravity is not signed in. Run `agy` once in Terminal to sign in.";
    }

    // MARK: - Model and Effort Normalization

    // :383-387
    public sealed record ParsedModelRow(string BaseSlug, string BaseName, string? Effort);

    public static ParsedModelRow ParseModelRow(string slug, string name) // :389-402
    {
        foreach (var effort in new[] { "high", "medium", "low" })
        {
            if (SwiftString.HasSuffix(slug, $"-{effort}"))
            {
                var baseSlug = SwiftString.DropLast(slug, SwiftString.Count(effort) + 1);
                var baseName = name;
                var suffix = $" ({Capitalized(effort)})";
                if (SwiftString.HasSuffix(baseName, suffix))
                    baseName = SwiftString.DropLast(baseName, SwiftString.Count(suffix));
                return new ParsedModelRow(baseSlug, baseName, effort);
            }
        }
        return new ParsedModelRow(slug, name, null);
    }

    /// <summary>Normalizes model and effort flags for `agy`. Strips any legacy effort suffixes from model slugs,
    /// resolves the effective effort level, and ensures --effort is only passed for models that support it.
    /// (:404-406)</summary>
    public static (string? Model, string? Effort) Normalize(string? model, string? effort) // :407-433
    {
        if (model is not { Length: > 0 }) return (null, null);
        var parsed = ParseModelRow(model, "");
        var baseSlug = parsed.BaseSlug;

        // Models that do not take --effort (such as Claude models hosted on Antigravity). :412-415
        if (baseSlug.Contains("claude", StringComparison.Ordinal)) return (baseSlug, null);

        var resolvedEffort = effort; // :417-423
        if (resolvedEffort is not { Length: > 0 }) resolvedEffort = parsed.Effort;
        if (resolvedEffort is not { Length: > 0 }) resolvedEffort = Is(baseSlug, "gpt-oss-120b") ? "medium" : "high";

        // Validate effort against model constraints. :425-430
        if (Is(baseSlug, "gemini-3.1-pro") && Is(resolvedEffort, "medium"))
            resolvedEffort = "high";
        else if (Is(baseSlug, "gpt-oss-120b"))
            resolvedEffort = "medium";

        return (baseSlug, resolvedEffort);
    }

    // MARK: - Catalog

    /// <summary>`agy models` prints `slug&lt;TAB&gt;Name` rows on stdout (progress goes to stderr). Rows with effort
    /// variants (-high, -medium, -low) are collapsed into a single base model entry whose supported efforts are toggled
    /// via the reasoning effort slider. (:437-440)</summary>
    public static async Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment) // :441-506
    {
        await using var scope = new AsyncLetScope();
        var modelsFetch = scope.Start(() => Shell.Run(executable.Path, ["models"], environment: environment, timeout: 30)); // :442
        var defaultFetch = scope.Start(() => Shell.Run(executable.Path, ["-p", "/model", "--output-format", "json"], environment: environment, timeout: 30)); // :443
        var models = await SwiftTry.OptionalAsync<ShellResult>(async () => await modelsFetch);   // :444
        var current = await SwiftTry.OptionalAsync<ShellResult>(async () => await defaultFetch); // :445
        string? currentID = null; // :446-449
        if (current?.Stdout is { } stdout && JSONValue.Parse(stdout) is { } json)
            currentID = json["command"]?["data"]?["id"]?.JSONValueString;
        if (models?.Stdout is not { } output || SwiftText.DecodeUtf8OrNil(output) is not { } text) // :450-452
            throw new ShellError("`agy models` produced no output.");

        var grouped = new Dictionary<string, (string Name, List<string> Efforts, bool IsDefault)>(SwiftStringComparer.Instance); // :454
        var order = new List<string>(); // :455

        foreach (var line in SwiftString.ComponentsSeparatedByNewlines(TextCleanup.StripANSI(text))) // :457-482
        {
            var trimmed = line.Trimmed();
            if (trimmed.Length == 0) continue;
            var parts = SwiftString.Split(trimmed, "\t", maxSplits: 1).Select(part => FoundationText.Trim(part)).ToList();
            if (parts.Count == 0 || !IsSlug(parts[0])) continue;
            var slug = parts[0];
            var name = parts.Count > 1 && parts[1].Length != 0 ? parts[1] : slug;
            var parsed = ParseModelRow(slug, name);

            // :465
            var isCurrent = (currentID is not null && SwiftString.Equal(slug, currentID)) || (currentID is not null && SwiftString.Equal(parsed.BaseSlug, currentID))
                || (currentID is not null && SwiftString.HasPrefix(currentID, parsed.BaseSlug));

            if (!grouped.TryGetValue(parsed.BaseSlug, out var group)) // :467-473
            {
                order.Add(parsed.BaseSlug);
                grouped[parsed.BaseSlug] = (parsed.BaseName, parsed.Effort is { } seeded ? [seeded] : [], isCurrent);
            }
            else // :474-481
            {
                if (parsed.Effort is { } eff && !group.Efforts.Any(existing => SwiftString.Equal(existing, eff))) group.Efforts.Add(eff);
                if (isCurrent) grouped[parsed.BaseSlug] = group with { IsDefault = true };
            }
        }

        var list = new List<ModelOption>(); // :484-496
        foreach (var baseSlug in order)
        {
            if (!grouped.TryGetValue(baseSlug, out var data)) continue;
            var sortedEfforts = new[] { "low", "medium", "high" }.Where(level => data.Efforts.Any(effort => SwiftString.Equal(effort, level))).ToList();
            var defaultEffort = sortedEfforts.Any(level => SwiftString.Equal(level, "high")) ? "high" : (sortedEfforts.Count > 0 ? sortedEfforts[^1] : null);
            list.Add(new ModelOption(
                id: baseSlug,
                name: data.Name,
                efforts: new ValueList<string>(sortedEfforts),
                defaultEffort: defaultEffort,
                isDefault: data.IsDefault));
        }

        if (list.Count == 0) throw new ShellError("`agy models` listed no models."); // :498
        if (!list.Any(option => option.IsDefault)) // :499-504
        {
            var first = list[0];
            list[0] = new ModelOption(
                id: first.Id, name: first.Name, detail: first.Detail,
                efforts: first.Efforts, defaultEffort: first.DefaultEffort, isDefault: true);
        }
        return new ValueList<ModelOption>(list);
    }

    private static bool IsSlug(string text) // :508-512
    {
        var characters = SwiftString.Characters(text).ToList();
        if (characters.Count == 0 || !(SwiftCharacter.IsLetter(characters[0]) || SwiftCharacter.IsNumber(characters[0]))) return false;
        return characters.All(character => SwiftCharacter.IsLetter(character) || SwiftCharacter.IsNumber(character)
                || SwiftString.Equal(character, "-") || SwiftString.Equal(character, "_") || SwiftString.Equal(character, "."))
            && !characters.Any(character => SwiftString.Equal(character, " "));
    }

    /// <summary>Sign-in state without spending tokens: `agy models` needs the cached Google session (or
    /// `GEMINI_API_KEY`) and fails loudly without one. (:514-515)</summary>
    public static async Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment) // :516-529
    {
        if (await SwiftTry.OptionalAsync<ShellResult>(async () => await Shell.Run(executable.Path, ["models"], environment: environment, timeout: 20)) is not { } result)
            return new ProviderStatus.Auth.Unknown();
        var text = TextCleanup.StripANSI(result.Output + result.ErrorOutput);
        var rows = SwiftString.ComponentsSeparatedByNewlines(text)
            .Where(line => IsSlug(SwiftString.Split(line, "\t") is { Count: > 0 } fields ? fields[0] : ""))
            .ToList();
        if (result.Succeeded && rows.Count != 0) return new ProviderStatus.Auth.SignedIn(null);
        var lowered = SwiftString.Lowercased(text);
        if (lowered.Contains("authentication required", StringComparison.Ordinal) || lowered.Contains("not signed in", StringComparison.Ordinal)
            || lowered.Contains("unauthenticated", StringComparison.Ordinal) || lowered.Contains("sign in", StringComparison.Ordinal)
            || lowered.Contains("login", StringComparison.Ordinal))
            return new ProviderStatus.Auth.SignedOut();
        return result.Succeeded ? new ProviderStatus.Auth.SignedIn(null) : new ProviderStatus.Auth.Unknown();
    }

    // MARK: - Plan limits

    /// <summary>Model quotas (`/usage`: weekly + 5-hour buckets per model group) and the AI credit balance
    /// (`/credits`). Both are zero-token headless calls, so reading limits never spends quota itself. (:533-535)</summary>
    public static async Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment) // :536-551
    {
        await using var scope = new AsyncLetScope();
        var usageFetch = scope.Start(() => Shell.Run(executable.Path, ["-p", "/usage", "--output-format", "json"], environment: environment, timeout: 30));     // :537
        var creditsFetch = scope.Start(() => Shell.Run(executable.Path, ["-p", "/credits", "--output-format", "json"], environment: environment, timeout: 30)); // :538
        var usage = await SwiftTry.OptionalAsync<ShellResult>(async () => await usageFetch);     // :539
        var credits = await SwiftTry.OptionalAsync<ShellResult>(async () => await creditsFetch); // :540
        var windows = new List<PlanLimits.Window>(); // :541
        if (usage is not null && SwiftText.DecodeUtf8OrNil(usage.Stdout) is { } usageText && JSONValue.Parse(usageText) is { } usageJSON) // :542-544
            windows.AddRange(ParseUsage(usageJSON["command"]?["data"], fallback: usageJSON["response"]?.JSONValueString));
        if (credits is not null && SwiftText.DecodeUtf8OrNil(credits.Stdout) is { } creditsText && JSONValue.Parse(creditsText) is { } creditsJSON // :545-548
            && ParseCredits(creditsJSON["command"]?["data"], fallback: creditsJSON["response"]?.JSONValueString) is { } window)
            windows.Add(window);
        if (windows.Count == 0) return null; // :549
        return new PlanLimits { PlanName = null, Windows = new ValueList<PlanLimits.Window>(windows) }; // :550
    }

    /// <summary>The structured envelope: groups of models sharing a weekly and a 5-hour bucket. `remaining_fraction`
    /// is what is left, so the usage panel's percent is the complement. (:553-555)</summary>
    private static ValueList<PlanLimits.Window> ParseUsage(JSONValue? data, string? fallback) // :556-572
    {
        var windows = new List<PlanLimits.Window>();
        foreach (var group in data?["groups"]?.JSONValueArray ?? [])
        {
            var groupName = group["name"]?.JSONValueString ?? "Models";
            foreach (var bucket in group["buckets"]?.JSONValueArray ?? [])
            {
                if (bucket["remaining_fraction"]?.JSONValueDouble is not { } remaining) continue;
                windows.Add(new PlanLimits.Window
                {
                    Id = bucket["id"]?.JSONValueString ?? $"{groupName}-{windows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    Title = $"{groupName} \U000000B7 {bucket["name"]?.JSONValueString ?? "Limit"}", // U+00B7 MIDDLE DOT
                    Percent = Max(0, Min(100, (1 - remaining) * 100)),
                    ResetsAt = bucket["reset_time"]?.JSONValueString is { } reset ? ISO8601.Date(reset) : null,
                });
            }
        }
        if (windows.Count == 0 && fallback is { } response) windows.AddRange(ParseUsageText(response)); // :570
        return new ValueList<PlanLimits.Window>(windows);
    }

    /// <summary>Older CLIs without the structured envelope: `Group&lt;TAB&gt;Bucket&lt;TAB&gt;95%&lt;TAB&gt;&lt;ISO
    /// date&gt;` rows, where the percent is what remains. (:574-575)</summary>
    private static List<PlanLimits.Window> ParseUsageText(string response) // :576-592
    {
        var windows = new List<PlanLimits.Window>();
        foreach (var line in SwiftString.ComponentsSeparatedByNewlines(response))
        {
            var parts = SwiftString.Split(line, "\t").Select(part => FoundationText.Trim(part)).ToList();
            if (parts.Count < 3) continue;
            var percentText = parts[2].Replace("%", "", StringComparison.Ordinal);
            if (SwiftNumber.ParseDouble(percentText) is not { } remaining) continue;
            var reset = parts.Count >= 4 ? ISO8601.Date(parts[3]) : null;
            windows.Add(new PlanLimits.Window
            {
                Id = $"usage-{windows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                Title = $"{parts[0]} \U000000B7 {parts[1]}", // U+00B7 MIDDLE DOT
                Percent = Max(0, Min(100, 100 - remaining)),
                ResetsAt = reset,
            });
        }
        return windows;
    }

    // provider-argv-and-handshakes.md section 10 item 10: the structured branch returns before the fallback is read.
    private static PlanLimits.Window? ParseCredits(JSONValue? data, string? fallback) // :594-616
    {
        if (data?["remaining_credits"]?.JSONValueInt is { } remaining)
        {
            return new PlanLimits.Window
            {
                Id = "ai-credits",
                Title = $"AI credits \U000000B7 {remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)} remaining", // U+00B7 MIDDLE DOT
                Percent = remaining > 0 ? 0 : 100,
                ResetsAt = null,
            };
        }
        if (fallback is not { } response) return null;
        foreach (var line in SwiftString.ComponentsSeparatedByNewlines(response))
        {
            var parts = SwiftString.Split(line, "\t").Select(part => FoundationText.Trim(part)).ToList();
            if (parts.Count < 2 || !SwiftString.LocalizedCaseInsensitiveContains(parts[0], "credit")
                || SwiftNumber.ParseInt(parts[1]) is not { } credits) continue;
            return new PlanLimits.Window
            {
                Id = "ai-credits",
                Title = $"AI credits \U000000B7 {credits.ToString(System.Globalization.CultureInfo.InvariantCulture)} remaining", // U+00B7 MIDDLE DOT
                Percent = credits > 0 ? 0 : 100,
                ResetsAt = null,
            };
        }
        return null;
    }

    // MARK: - Tool mapping

    /// <summary>Same row shapes as Claude's: the subject is the command, the path, the pattern or the URL, and the
    /// detail is only what qualifies it (a search folder, an MCP tool's input). The stream sends a display subset of the
    /// parameters (`AbsolutePath`, `TargetFile`, `SearchDirectory`, ...), never the edit content. (:620-624)</summary>
    private ToolCall MakeToolCall(string name, JSONValue parameters) // :625-665
    {
        var kind = Kind(name);
        var path = Path(parameters) is { } found ? ToolTitles.RelativePath(found, WorkingDirectory) : null;
        switch (kind)
        {
            case ToolCall.Kind.Command: // :629-631
            {
                var command = String(parameters, ["CommandLine", "command", "Command"]) is { } line ? ToolTitles.UnwrapShell(line) : null;
                return new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = command ?? Humanize(name) };
            }
            case ToolCall.Kind.Read: // :632-635
            {
                var call = new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = path ?? String(parameters, ["Url", "URL", "url"]) ?? Humanize(name) };
                if (path is not null && LineRange(parameters) is { } range) call = call with { Detail = range };
                return call;
            }
            case ToolCall.Kind.Edit: // :636-639
            {
                var call = new ToolCall { ToolCallKind = ToolCall.Kind.Edit, Title = path ?? Humanize(name) };
                if (path is not null) call = call with { Edits = new ValueList<FileEdit>([new FileEdit(path)]) };
                return call;
            }
            case ToolCall.Kind.Search: // :640-647
            {
                var pattern = String(parameters, ["Query", "query", "Pattern", "pattern"]);
                var folder = String(parameters, ["SearchPath", "SearchDirectory", "DirectoryPath", "Path", "path"]) is { } directory
                    ? ToolTitles.RelativePath(directory, WorkingDirectory) : null;
                if (pattern is not null) return new ToolCall { ToolCallKind = ToolCall.Kind.Search, Title = pattern, Detail = folder };
                return new ToolCall { ToolCallKind = ToolCall.Kind.Search, Title = folder ?? Humanize(name) };
            }
            case ToolCall.Kind.Web: // :648-650
            {
                var subject = String(parameters, ["Url", "URL", "url", "query", "Query"]);
                return new ToolCall { ToolCallKind = ToolCall.Kind.Web, Title = subject ?? Humanize(name) };
            }
            case ToolCall.Kind.Agent: // :651-653
            {
                var task = String(parameters, ["Task", "task", "Prompt", "prompt", "Description", "description"]);
                return new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = task is not null ? TextCleanup.SingleLine(task, limit: 96) : Humanize(name) };
            }
            case ToolCall.Kind.Mcp: // :654-658
            {
                var server = String(parameters, ["ServerName", "server_name", "Server", "server"]) ?? "MCP";
                var tool = String(parameters, ["ToolName", "tool_name", "Tool", "tool"]) ?? "tool";
                var input = parameters["Arguments"] ?? parameters["arguments"] ?? parameters["Input"] ?? new JSONValue.Null();
                return new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = $"{server} \U000000B7 {tool}", Detail = input.IsNull ? null : input.CompactString }; // U+00B7 MIDDLE DOT
            }
            case ToolCall.Kind.Other: // :659-663
            {
                var call = new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = Humanize(name) };
                var detail = parameters.IsNull ? null : parameters.CompactString;
                if (detail is { Length: > 0 } && !string.Equals(detail, "{}", StringComparison.Ordinal) && SwiftString.Count(detail) <= 300)
                    call = call with { Detail = detail };
                return call;
            }
            default:
                throw new System.Diagnostics.UnreachableException();
        }
    }

    private static ToolCall.Kind Kind(string name) => name switch // :667-691
    {
        "run_command" or "send_command_input" or "command_status" or "wait" or "wait_5_seconds" or "schedule"
            => ToolCall.Kind.Command,
        "view_file" or "view_file_outline" or "view_code_item" or "view_content_chunk" or "read_resource"
            or "read_browser_page" or "capture_browser_screenshot" or "capture_browser_console_logs"
            => ToolCall.Kind.Read,
        "write_to_file" or "replace_file_content" or "multi_replace_file_content" or "sed_file" or "notebook_edit" or "notebook_execution"
            => ToolCall.Kind.Edit,
        "list_dir" or "grep_search" or "find_by_name" or "list_resources" or "list_browser_pages"
            => ToolCall.Kind.Search,
        "search_web" or "read_url_content" or "open_browser_url" or "browser_click_element" or "browser_drag_pixel_to_pixel"
            or "browser_get_dom" or "browser_get_network_request" or "browser_input" or "browser_list_network_requests"
            or "browser_mouse_down" or "browser_mouse_up" or "browser_move_mouse" or "browser_press_key" or "browser_refresh_page"
            or "browser_resize_window" or "browser_scroll" or "browser_scroll_dom" or "browser_select_option" or "click_browser_pixel"
            or "execute_browser_javascript"
            => ToolCall.Kind.Web,
        "invoke_subagent" or "browser_subagent" or "define_subagent" or "manage_subagents"
            => ToolCall.Kind.Agent,
        "call_mcp_tool"
            => ToolCall.Kind.Mcp,
        _ => ToolCall.Kind.Other,
    };

    private static string? Path(JSONValue parameters) => // :693-698
        String(parameters, [
            "AbsolutePath", "TargetFile", "NotebookPath", "FilePath", "File", "file", "path", "Path",
            "DirectoryPath", "directory", "dir",
        ]);

    private static string? String(JSONValue parameters, IReadOnlyList<string> keys) // :700-705
    {
        foreach (var key in keys)
        {
            if (parameters[key]?.JSONValueString is { Length: > 0 } value) return value;
        }
        return null;
    }

    /// <summary>"L12-40" (with an en dash) when a read names a line window, so partial reads read as such. (:707)</summary>
    private static string? LineRange(JSONValue parameters) // :708-712
    {
        if (parameters["StartLine"]?.JSONValueInt is not { } start) return null;
        var startText = start.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (parameters["EndLine"]?.JSONValueInt is { } end && end > start)
            return $"L{startText}\U00002013{end.ToString(System.Globalization.CultureInfo.InvariantCulture)}"; // U+2013 EN DASH
        return $"L{startText}";
    }

    private static string Humanize(string name) // :714-718
    {
        var words = SwiftString.Split(name, "_");
        if (words.Count == 0) return name;
        return string.Join(" ", new[] { Capitalized(words[0]) }.Concat(words.Skip(1)));
    }

    // Foundation `String.capitalized` (:394 `effort.capitalized`, :717 `first.capitalized`): the first character of
    // each word upper-cased and every other character lower-cased, a word being delimited by whitespace (NSString
    // capitalizedString). Every call site here passes one word with no whitespace.
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

    // Swift `==` between an optional string and a literal (R-STR-2).
    private static bool Is(string? value, string literal) => value is not null && SwiftString.Equal(value, literal);

    // Swift `max(x, y)` and `min(x, y)` on Double (R-NUM-4).
    private static double Max(double x, double y) => y >= x ? y : x;
    private static double Min(double x, double y) => y < x ? y : x;
}
