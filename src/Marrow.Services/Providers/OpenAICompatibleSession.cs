using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/OpenAICompatibleSession.swift (B2 1fbf81e), :1-1337.
//
// Unit Services.Providers.OpenAICompatibleSession part 1 of 5 (PR-PROVAPI-089 to 112) implements
// lifecycle (Start, Send, IsRunning, ApiKey), turn orchestration (PerformTurn's round loop, the
// Hydra pacing/wrap-up/final-budget notes, the 200-round ceiling), the tool-loop's batching of
// approval-free reads against every other call as a barrier (:362-404, :854-892), cancellation
// (Interrupt, FinishInterrupted, the per-turn reset of approveAllRemaining) and the base
// FriendlyError fallback (:804-806). It also carries the self-contained history and message
// helpers those flows call directly and that do not depend on network code: TrimHistory,
// CondenseHistory, SanitizeHistory, AssistantTurn, UserContentValue, (Refresh)SystemPrompt,
// RequiresApproval, ReadOnlyKind, Stop, Compact and NextID (:230-486, :1046-1057, :1098-1332).
//
// The request payload, SSE streaming and its transport-retry ladder (:506-800), provider-specific
// error mapping beyond the base FriendlyError, the six built-in tool bodies and MCP dispatch
// (:894-1035) and approvals (:1058-1097) are not cited here: their Swift lines are not in this
// part's packet and their records are not in PR-PROVAPI-089..112. They land in later parts of
// this unit. StreamOneRound and ExecuteTool are exposed as protected virtual seams so this part's
// orchestration is testable against controlled round/tool results ahead of those parts landing;
// a later part replaces the seam's default body with the real implementation and removes the
// NotSupportedException.
public class OpenAICompatibleSession : ProviderSession
{
    // :13-33. The constants that genuinely differ per provider.
    public sealed record Config
    {
        public required ProviderKind Kind { get; init; }                    // :15
        public required string Title { get; init; }                        // :17
        public required Func<string, string> AgentIdentity { get; init; }  // :20
        public required string DefaultModel { get; init; }                 // :21
        public required Uri ChatURL { get; init; }                         // :22
        public required int ContextWindow { get; init; }                   // :23
        public int? MaxOutputTokens { get; init; }                         // :25
        public bool ReplaysReasoning { get; init; }                        // :29
        public required Func<string, bool> IsVisionModel { get; init; }    // :31
    }

    // :99-113. Sendable struct -> immutable record (R-VAL-1); built incrementally by the SSE
    // drain in a later part, consumed here as a finished value.
    public sealed record StreamRound
    {
        public string Content { get; init; } = "";                         // :100
        public string Reasoning { get; init; } = "";                       // :101
        public ValueList<PendingToolCall> ToolCalls { get; init; } = ValueList<PendingToolCall>.Empty; // :102
        public ContextUsage? Usage { get; init; }                          // :103
        public ReportedTokenUsage? TokenUsage { get; init; }               // :106
        public string? RawError { get; init; }                             // :107
        public int StatusCode { get; init; } = 200;                        // :108
    }

    // :114-122
    public sealed record ReportedTokenUsage
    {
        public required long Total { get; init; }
        public long? Input { get; init; }
        public long? Output { get; init; }
        public long? CachedInput { get; init; }

        // :122
        public bool HasSpend => Total > 0 || (Input ?? 0) > 0 || (Output ?? 0) > 0;
    }

    // :125-128
    public sealed record PendingToolCall(string Id, string Name, string Arguments);

    public Config OpenAICompatibleSessionConfig { get; } // :34 Swift: config (R-NAME-3)
    public Action<ProviderEvent>? OnEvent { get; set; }  // :35
    public SessionConfiguration Configuration { get; }   // :37

    private readonly NativeFileTools fileTools;           // :38
    private ProjectGuidance guidance;                      // :41
    private string? sessionID;                             // :42
    private List<JSONValue> messages = [];                 // :43
    private RuntimeMode runtimeMode;                        // :44
    private InteractionMode interactionMode;                // :45
    private string currentModel;                            // :46
    private int counter;                                    // :47
    private bool interrupted;                               // :48
    private bool isStopping;                                // :49
    private bool approveAllRemaining;                       // :50

    private TaskHandle<bool>? sendTask;                     // :53. Task<Void, Error> stood in as TaskHandle<bool>.
    private TaskHandle<StreamRound>? roundTask;              // :54
    private TaskHandle? sseDrain;                           // :57
    private readonly Dictionary<string, CheckedContinuation<bool>> pendingApprovals = []; // :58

    // :230-238
    public OpenAICompatibleSession(Config config, SessionConfiguration configuration)
    {
        OpenAICompatibleSessionConfig = config;
        Configuration = configuration;
        fileTools = new NativeFileTools(configuration.WorkingDirectory.Path);
        guidance = new ProjectGuidance(configuration.WorkingDirectory.Path);
        runtimeMode = configuration.RuntimeMode;
        interactionMode = configuration.InteractionMode;
        currentModel = configuration.Model ?? config.DefaultModel;
    }

    // :240. PR-PROVAPI-093: no child process backs these sessions, so liveness is this
    // composition alone.
    public bool IsRunning => sessionID is not null && !isStopping;

    // Test-only accessor (InternalsVisibleTo Marrow.Services.Tests): :43 `messages`.
    internal IReadOnlyList<JSONValue> Messages => messages;

    // Test-only accessor: :48-50.
    internal bool IsInterrupted => interrupted;

    private string WorkingDirectory => Configuration.WorkingDirectory.Path; // :242
    private string ApiKey => (Configuration.ApiKey ?? "").Trimmed();       // :243

    // MARK: - Lifecycle

    // :247-265. PR-PROVAPI-089, PR-PROVAPI-090, PR-PROVAPI-091, PR-PROVAPI-092.
    public Task<string> Start()
    {
        if (ApiKey.Length == 0) throw new ProviderError(new ProviderError.Case.NotInstalled(OpenAICompatibleSessionConfig.Kind)); // :248
        var id = Configuration.ResumeID ?? SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid())); // :249
        sessionID = id;       // :250
        interrupted = false;  // :251
        isStopping = false;   // :252
        if (messages.Count == 0) // :253
        {
            messages = [JSONValue.FromDictionaryLiteral(("role", "system"), ("content", SystemPrompt()))]; // :254
            // A relaunch starts with nothing said: the thread's past exchanges go back in, so the
            // model still knows its brief and what it did about it. :255-256
            foreach (var message in Configuration.Transcript) // :257
            {
                messages.Add(JSONValue.FromDictionaryLiteral(
                    ("role", message.TranscriptMessageRole == TranscriptMessage.Role.User ? "user" : "assistant"),
                    ("content", message.Text))); // :258
            }
            TrimHistory(); // :260
        }
        // No .models event: the registry owns the live catalog, and a seed sent here overwrote
        // it every time a chat started. :262-263 PR-PROVAPI-092.
        return Task.FromResult(id); // :264
    }

    // :267-279. PR-PROVAPI-094.
    public async Task Send(TurnInput input)
    {
        if (sendTask is not null || sessionID is null || isStopping) throw new ProviderError(new ProviderError.Case.NotRunning()); // :268
        var task = MainActor.SpawnThrowing<bool>(async () =>
        {
            try { await PerformTurn(input); return true; } // :270-271
            finally { sendTask = null; } // :270 defer
        });
        sendTask = task; // :273
        await SwiftTask.WithCancellationHandler(() => task.Value, () => task.Cancel()); // :274-278
    }

    // :281-435. PR-PROVAPI-095 through PR-PROVAPI-111.
    private async Task PerformTurn(TurnInput input)
    {
        if (SwiftTask.IsCancelled) // :282
        {
            await FinishInterrupted(); // :283
            return;
        }
        if (sessionID is null || isStopping) throw new ProviderError(new ProviderError.Case.NotRunning());   // :286
        if (ApiKey.Length == 0) throw new ProviderError(new ProviderError.Case.NotInstalled(OpenAICompatibleSessionConfig.Kind)); // :287
        runtimeMode = input.RuntimeMode;             // :288
        interactionMode = input.InteractionMode;     // :289
        if (input.Model is { Length: > 0 } model) currentModel = model; // :290
        interrupted = false;         // :291
        approveAllRemaining = false; // :292
        // A turn that resumes after AGENTS.md or CLAUDE.md changed gets the new rules; the files
        // are only re-read when their size or mtime moved. :293-294
        RefreshSystemPrompt(); // :295

        var userContent = await UserContentValue(input.Text, input.Images); // :297
        messages.Add(JSONValue.FromDictionaryLiteral(("role", "user"), ("content", userContent))); // :298
        TrimHistory(); // :299
        OnEvent?.Invoke(new ProviderEvent.TurnStarted(null)); // :300 PR-PROVAPI-096

        var finalText = "";       // :302
        var rounds = 0;            // :303
        var toolsSinceChange = 0;  // :304
        var pacingAt = HydraBudget.PacingTools; // :305
        var toolsUsed = 0L;        // :306
        var wrapUpAsked = false;   // :307
        var turnStartedAt = SwiftDate.Now; // :308
        var hitCeiling = false;    // :309

        try
        {
            while (!interrupted) // :311
            {
                SwiftTask.CheckCancellation(); // :312
                if (rounds >= MaxRoundsPerTurn) { hitCeiling = true; break; } // :313 PR-PROVAPI-097
                rounds += 1; // :314

                // A head past its budget writes its report with no tools left to reach for; a
                // head stopped by its runtime for the same reason answers the same way. :315-316
                var budgetSpent = Configuration.IsHydraHead &&
                    (toolsUsed >= HydraBudget.MaxTools || (SwiftDate.Now - turnStartedAt).TotalSeconds >= HydraBudget.MaxSeconds); // :317
                if (input.IsFinalReport || budgetSpent) // :318
                {
                    if (budgetSpent) messages.Add(JSONValue.FromDictionaryLiteral(("role", "user"), ("content", HydraBudget.FinalNote))); // :319
                    var last = await StreamOneRound(currentModel, input.Effort, false); // :320
                    ReportUsage(last); // :321
                    if (last.RawError is { Length: > 0 } lastError) throw new ProviderError(new ProviderError.Case.Failed(FriendlyError(lastError, last.StatusCode))); // :322
                    if (last.Content.Length > 0) // :323
                    {
                        finalText = last.Content; // :324
                        messages.Add(AssistantTurn(last.Content, last.Reasoning, [])); // :325
                    }
                    break; // :327
                }
                if (Configuration.IsHydraHead && !wrapUpAsked &&
                    (toolsUsed >= HydraBudget.WrapUpTools || (SwiftDate.Now - turnStartedAt).TotalSeconds >= HydraBudget.WrapUpSeconds)) // :329
                {
                    wrapUpAsked = true; // :330
                    messages.Add(JSONValue.FromDictionaryLiteral(("role", "user"), ("content", HydraBudget.WrapUpNote))); // :331
                }
                // A head that only ever reads is paced: every so many tools without a change, a
                // note in its history asks it to act or report. :333-335
                if (Configuration.IsHydraHead && toolsSinceChange >= pacingAt) // :336
                {
                    messages.Add(JSONValue.FromDictionaryLiteral(("role", "user"), ("content", HydraBudget.PacingNote))); // :337
                    pacingAt = toolsSinceChange + HydraBudget.PacingTools; // :338
                }
                CondenseHistory(); // :340
                var round = await StreamOneRound(currentModel, input.Effort); // :341
                ReportUsage(round); // :342
                if (round.StatusCode == 401 ||
                    (round.RawError is { } rawError401 &&
                     SwiftString.LocalizedCaseInsensitiveContains(rawError401, "invalid") &&
                     SwiftString.LocalizedCaseInsensitiveContains(rawError401, "key"))) // :343-344 PR-PROVAPI-101
                {
                    throw new ProviderError(new ProviderError.Case.Failed(
                        $"{OpenAICompatibleSessionConfig.Title} rejected the API key. Check it in Settings \U00002192 Providers \U00002192 {OpenAICompatibleSessionConfig.Title}.")); // :345, U+2192 RIGHTWARDS ARROW
                }
                if (round.RawError is { Length: > 0 } error) throw new ProviderError(new ProviderError.Case.Failed(FriendlyError(error, round.StatusCode))); // :346-347 PR-PROVAPI-102
                if (round.Content.Length > 0) finalText = round.Content; // :349
                if (round.ToolCalls.Count == 0) // :350
                {
                    // Keep the reply in history, or the next turn forgets it. :351
                    if (round.Content.Length > 0) messages.Add(AssistantTurn(round.Content, round.Reasoning, [])); // :352-353 PR-PROVAPI-103
                    break; // :355
                }
                // Record the assistant turn with its tool calls so the next request keeps the
                // OpenAI tool-call chain intact. :357-358
                messages.Add(AssistantTurn(round.Content, round.Reasoning, round.ToolCalls)); // :359 PR-PROVAPI-104
                var shouldContinue = true; // :360
                var toolIndex = 0;          // :361
                while (toolIndex < round.ToolCalls.Count) // :362
                {
                    if (interrupted || SwiftTask.IsCancelled) { shouldContinue = false; break; } // :363 PR-PROVAPI-108
                    var tool = round.ToolCalls[toolIndex]; // :364
                    // Consecutive reads that need no approval run together, four at a time.
                    // Everything else - a write, a shell command, an MCP call, an unknown tool, or
                    // a read the user has to approve - is a barrier: it waits for the group before
                    // it, and the model's order is kept. :365-368
                    if (ReadOnlyKind(tool.Name) is { } kind && !RequiresApproval(kind)) // :369
                    {
                        var end = toolIndex; // :370
                        while (end < round.ToolCalls.Count &&
                               ReadOnlyKind(round.ToolCalls[end].Name) is { } nextKind &&
                               !RequiresApproval(nextKind)) // :371-374
                        {
                            end += 1;
                        }
                        var batch = round.ToolCalls.Skip(toolIndex).Take(end - toolIndex).ToList(); // :376
                        toolsUsed += batch.Count;        // :377
                        toolsSinceChange += batch.Count; // :378
                        var outputs = await ExecuteReadBatch(batch); // :379 PR-PROVAPI-105
                        if (SwiftTask.IsCancelled || interrupted) { shouldContinue = false; break; } // :380
                        for (var offset = 0; offset < outputs.Count; offset++) // :381
                        {
                            messages.Add(JSONValue.FromDictionaryLiteral(
                                ("role", "tool"), ("tool_call_id", batch[offset].Id), ("content", outputs[offset]))); // :382 PR-PROVAPI-107
                        }
                        TrimHistory();   // :384
                        toolIndex = end; // :385
                        continue;        // :386
                    }
                    toolsUsed += 1; // :388
                    if (tool.Name is "write_file" or "edit_file") // :389
                    {
                        toolsSinceChange = 0;                     // :390
                        pacingAt = HydraBudget.PacingTools;       // :391
                    }
                    else
                    {
                        toolsSinceChange += 1; // :393
                    }
                    // The tool runs inside the turn's task: a stop cancels it with the turn, an
                    // approval it is waiting on comes back declined, and a process it launched is
                    // told to stop. Nothing it produced is said or remembered. :395-397
                    var output = await ExecuteTool(tool); // :398 PR-PROVAPI-106
                    if (SwiftTask.IsCancelled || interrupted) { shouldContinue = false; break; } // :399
                    messages.Add(JSONValue.FromDictionaryLiteral(("role", "tool"), ("tool_call_id", tool.Id), ("content", output))); // :400 PR-PROVAPI-107
                    TrimHistory();  // :401
                    toolIndex += 1; // :402
                }
                if (!shouldContinue || interrupted) break; // :404
            }
        }
        catch (CancellationError) // :406
        {
            await FinishInterrupted(); // :407
            return;
        }
        catch (ProviderError error) // :409
        {
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, SwiftErrors.LocalizedDescription(error))); // :410
            return;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) // :412
        {
            if (interrupted || SwiftTask.IsCancelled) // :413
            {
                await FinishInterrupted(); // :414
            }
            else
            {
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Failed, FriendlyError(SwiftErrors.LocalizedDescription(error), 0))); // :416 PR-PROVAPI-110
            }
            return;
        }

        if (interrupted || SwiftTask.IsCancelled) // :421
        {
            await FinishInterrupted(); // :422
            return;
        }
        if (hitCeiling) // :425
        {
            // The tool results are in history, so "continue" resumes cleanly. :426
            OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
            {
                NoticeLevel = Notice.Level.Warning,
                Message = $"{OpenAICompatibleSessionConfig.Title} paused after {MaxRoundsPerTurn} rounds of tool calls in one turn. " +
                    "Say \U0000201Ccontinue\U0000201D to pick up where it left off.", // :427, U+201C/U+201D LEFT/RIGHT DOUBLE QUOTATION MARK
            })); // PR-PROVAPI-097
            OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null)); // :428
            return;
        }
        if (interactionMode == InteractionMode.Plan && finalText.Trimmed().Length > 0) // :431
        {
            OnEvent?.Invoke(new ProviderEvent.PlanCompleted(NextID("plan"), finalText)); // :432 PR-PROVAPI-111
        }
        OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)); // :434
    }

    // :437-446. PR-PROVAPI-112.
    public async Task Interrupt()
    {
        interrupted = true;               // :438
        sendTask?.Cancel();                // :439
        roundTask?.Cancel();               // :440
        roundTask = null;                  // :441
        sseDrain?.Cancel();                // :442
        sseDrain = null;                   // :443
        foreach (var continuation in pendingApprovals.Values) continuation.Resume(false); // :444
        pendingApprovals.Clear();          // :445
        await Task.CompletedTask;
    }

    // :448-453
    public void ResolveApproval(string requestID, string optionID)
    {
        if (!pendingApprovals.Remove(requestID, out var continuation)) return; // :449
        if (optionID == "always") approveAllRemaining = true; // :450
        continuation.Resume(optionID is "allow" or "always"); // :451
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :452
    }

    // :455
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) { }

    // :457-468
    public async Task Compact()
    {
        if (messages.Count <= 12) // :459
        {
            OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Nothing to compact yet." })); // :460
            return;
        }
        var system = messages.Count > 0 ? messages[0] : null; // :463
        var tail = messages.Skip(Math.Max(0, messages.Count - 10)).ToList(); // :464
        messages = (system is not null ? new List<JSONValue> { system } : []).Concat(tail).ToList(); // :465
        // Slicing can orphan tool messages from their tool_calls. :466
        SanitizeHistory(); // :467
        OnEvent?.Invoke(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." })); // :468
        await Task.CompletedTask;
    }

    // :471-482
    public void Stop()
    {
        isStopping = true;   // :472
        interrupted = true;  // :473
        sendTask?.Cancel();  // :474
        roundTask?.Cancel(); // :475
        roundTask = null;    // :476
        sseDrain?.Cancel();  // :477
        sseDrain = null;     // :478
        foreach (var continuation in pendingApprovals.Values) continuation.Resume(false); // :479
        pendingApprovals.Clear(); // :480
        sessionID = null;    // :481
    }

    // :488-502. TokenLedger (Services/Store/TokenLedger.cs) has since landed in the tree, so both
    // shapes are reported as the Swift does.
    protected virtual void ReportUsage(StreamRound round)
    {
        if (round.Usage is { } usage) // :489
        {
            OnEvent?.Invoke(new ProviderEvent.Usage(usage)); // :490
            Marrow.Services.Store.TokenLedger.Shared.Current.Record(usage.UsedTokens); // :491
        }
        if (round.TokenUsage is not { HasSpend: true } reported) return; // :493
        OnEvent?.Invoke(new ProviderEvent.TokenSpend(new TokenSpend(
            OpenAICompatibleSessionConfig.Kind, reported.Total, reported.Input, reported.Output, reported.CachedInput, currentModel))); // :494-501
    }

    // MARK: - Chat round

    // :506-561. Builds the request payload, drains the SSE stream and retries the three
    // transient transport failures. Not cited in this part's packet (no PR-PROVAPI-089..112
    // record names these lines) and TokenLedger/requestPayload/performStream/drainSSE are not in
    // the C# tree yet; exposed as a protected virtual seam so PerformTurn's own orchestration
    // (the round loop, the Hydra notes, the 401/error/no-tools/tool-call branches) is testable
    // now. A later part of this unit replaces this default with the real implementation.
    protected virtual Task<StreamRound> StreamOneRound(string model, string? effort, bool withTools = true) =>
        throw new NotSupportedException(
            $"{nameof(OpenAICompatibleSession)}.{nameof(StreamOneRound)}: the request payload and SSE " +
            "streaming (Swift :506-800) land in a later part of this unit; override this seam in a test " +
            "or wait for that part.");

    // :804-806. The base mapping every subclass overrides; DeepSeek/Meta/Z.ai's own mappings are
    // a later part's concern.
    public virtual string FriendlyError(string message, int statusCode) =>
        message.Length == 0 ? $"{OpenAICompatibleSessionConfig.Title} stopped before finishing." : message;

    // :808-812. PR-PROVAPI-109.
    private async Task FinishInterrupted()
    {
        foreach (var continuation in pendingApprovals.Values) continuation.Resume(false); // :809
        pendingApprovals.Clear(); // :810
        OnEvent?.Invoke(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null)); // :811
        await Task.CompletedTask;
    }

    // MARK: - Tools

    // :855. The most built-in reads one batch may have in flight.
    private const int MaxConcurrentReads = 4;

    // :857-865. The tool kind of the three built-in reads, null for everything else. Only calls
    // whose kind runs without approval take part in a concurrent batch.
    private static ToolCall.Kind? ReadOnlyKind(string name) => name switch
    {
        "read_file" => ToolCall.Kind.Read,     // :861
        "list_files" or "search_text" => ToolCall.Kind.Search, // :862
        _ => null, // :863
    };

    // :867-892. PR-PROVAPI-105. Runs a group of approval-free built-in reads, at most four at a
    // time, and returns their results in the order the model asked for them. Cancelling the turn
    // cancels every read still in flight; a cancelled read's output is never appended (the caller
    // checks that after this returns, as the Swift does).
    private async Task<List<string>> ExecuteReadBatch(IReadOnlyList<PendingToolCall> tools)
    {
        var outputs = new string[tools.Count]; // :872
        await TaskGroup<(int Index, string Output)>.Run(async group =>
        {
            var limit = Math.Min(MaxConcurrentReads, tools.Count); // :874
            var next = 0; // :875
            while (next < limit) // :876
            {
                var index = next; // :877
                group.AddTask(async () => (index, await ExecuteTool(tools[index]))); // :878
                next += 1; // :879
            }
            while (await group.Next() is (true, var result)) // :881
            {
                if (SwiftTask.IsCancelled || interrupted) { group.CancelAll(); break; } // :882
                outputs[result.Index] = result.Output; // :883
                if (next < tools.Count) // :884
                {
                    var index = next; // :885
                    group.AddTask(async () => (index, await ExecuteTool(tools[index]))); // :886
                    next += 1; // :887
                }
            }
        });
        return [.. outputs]; // :891
    }

    // :894-896. The cancellation guard is real (PR-PROVAPI-108); the six built-in tool bodies and
    // MCP dispatch (:897-1035) are not cited in this part's packet and land in a later part of
    // this unit. Exposed as a protected virtual seam so the tool-loop scheduling above is
    // testable now.
    protected virtual Task<string> ExecuteTool(PendingToolCall tool)
    {
        if (SwiftTask.IsCancelled || interrupted) return Task.FromResult("Stopped."); // :895
        throw new NotSupportedException(
            $"{nameof(OpenAICompatibleSession)}.{nameof(ExecuteTool)}: the built-in tool bodies " +
            "(read_file, list_files, search_text, write_file, edit_file, run_command) and MCP dispatch " +
            "(Swift :897-1035) land in a later part of this unit; override this seam in a test or wait " +
            "for that part.");
    }

    // :1035-1038. Used by the later part's approveIfNeeded/executeTool bodies; kept here because
    // it is a one-line, self-contained event plus return value with no missing dependency.
    private string Declined(string callID)
    {
        OnEvent?.Invoke(new ProviderEvent.ToolUpdated(callID, new ToolUpdate
        {
            Output = "The user declined this action.",
            Status = ToolCall.Status.Declined,
        }));
        return "The user declined this action. Ask how to proceed instead of retrying it.";
    }

    // :1046-1056. Whether this kind of tool would ask the user before running. The concurrent
    // read batch uses it to stay off the approval path entirely: only calls this returns false
    // for may run together.
    //
    // Plan mode is a promise the turn keeps, not a preference, so it is decided before "approve
    // for turn" rather than after it: one click of that button on a read used to switch the
    // promise off for every edit and command left in the turn.
    private bool RequiresApproval(ToolCall.Kind kind)
    {
        var planBlocks = interactionMode == InteractionMode.Plan && (kind == ToolCall.Kind.Edit || kind == ToolCall.Kind.Command); // :1048
        if (approveAllRemaining && !planBlocks) return false; // :1049
        if (planBlocks) return true; // :1050
        return runtimeMode switch
        {
            RuntimeMode.Supervised => true,                                  // :1052
            RuntimeMode.AutoAcceptEdits or RuntimeMode.Auto => kind == ToolCall.Kind.Command, // :1053
            RuntimeMode.FullAccess => false,                                 // :1054
            _ => throw new UnreachableException(),
        };
    }

    // :1098-1117. Self-contained; no missing dependency.
    private const long CondenseHistoryAt = 160_000;
    private const long CondenseHistoryTo = 100_000;
    private const int CondenseKeepsRecent = 10;
    private const string CondensedMarker = "\U00002026[Droppy Code condensed this output"; // U+2026 HORIZONTAL ELLIPSIS

    private void CondenseHistory()
    {
        var toolIndices = new List<int>(); // :1099
        var total = 0L;                     // :1100
        for (var index = 0; index < messages.Count; index++) // :1101
        {
            if (messages[index]["role"]?.JSONValueString != "tool") continue;
            toolIndices.Add(index);
            total += SwiftString.Utf8Count(messages[index]["content"]?.JSONValueString ?? "");
        }
        if (total <= CondenseHistoryAt) return; // :1105
        foreach (var index in toolIndices.Take(Math.Max(0, toolIndices.Count - CondenseKeepsRecent))) // :1106
        {
            if (total <= CondenseHistoryTo) break; // :1107
            if (messages[index] is not JSONValue.Object { Value: { } fields } ||
                fields.TryGetValue("content", out var contentValue) is not true ||
                contentValue is not JSONValue.String { Value: { } content } ||
                SwiftString.Utf8Count(content) <= 1_200 ||
                content.Contains(CondensedMarker, StringComparison.Ordinal)) // :1108-1109
            {
                continue;
            }
            var kept = SwiftString.Prefix(content, 400); // :1110
            var condensed = $"{kept}\n{CondensedMarker} ({SwiftString.Utf8Count(content)} bytes); call the tool again if you need it]"; // :1111
            var updated = new Dictionary<string, JSONValue>(fields) { ["content"] = condensed }; // :1112
            messages[index] = new JSONValue.Object(new ValueMap<string, JSONValue>(updated, SwiftStringComparer.Instance)); // :1113
            total -= SwiftString.Utf8Count(content) - SwiftString.Utf8Count(condensed); // :1114
        }
    }

    // MARK: - Messages

    // :1172-1176. Rebuilds the system prompt in place so the current project guidelines are the
    // ones the model sees. Only the system message is touched; the higher-priority policy inside
    // it is rebuilt identically.
    private void RefreshSystemPrompt()
    {
        if (messages.Count == 0 || messages[0]["role"]?.JSONValueString != "system") return; // :1173
        messages[0] = JSONValue.FromDictionaryLiteral(("role", "system"), ("content", SystemPrompt())); // :1174
    }

    // :1177-1216
    private string SystemPrompt()
    {
        var modeLine = (interactionMode, runtimeMode) switch // :1178
        {
            (InteractionMode.Plan, _) =>
                "You are in PLAN mode: research the project, then write a clear step-by-step plan. Do not edit files or run commands that change anything; reads, lists and searches are fine.", // :1180
            (InteractionMode.Build, RuntimeMode.Supervised) =>
                "You are in BUILD mode with supervised permissions: use tools freely but the user approves each one in Droppy Code.", // :1182
            (InteractionMode.Build, RuntimeMode.AutoAcceptEdits) or (InteractionMode.Build, RuntimeMode.Auto) =>
                "You are in BUILD mode: read and edit files freely; the user approves shell commands in Droppy Code.", // :1184
            (InteractionMode.Build, RuntimeMode.FullAccess) =>
                "You are in BUILD mode with full access: use tools freely without asking.", // :1186
            _ => throw new UnreachableException(),
        };
        // The optional Hydra policy and the anti-slop guidance go in once each. The off notice
        // goes out too, so a resumed thread cannot keep earlier guidance alive. :1190-1196
        var policy = string.Join("\n\n", new[]
        {
            Configuration.Hydra is { } hydra ? HydraPrompts.FallbackPolicy(hydra) : null,
            AntiSlopPolicy.Instructions(Configuration.AntiSlopEnabled),
        }.Where(value => value is not null));
        // The project's own root guidelines come last, so the policy above stays higher
        // priority. They are re-read here only when the files changed. :1197-1199
        var appendedPolicy = string.Join("\n\n", new[]
        {
            policy.Length == 0 ? null : policy,
            guidance.SystemPromptBlock(),
        }.Where(value => value is not null));
        return
            $"You are Droppy Code's {OpenAICompatibleSessionConfig.AgentIdentity(currentModel)}, working inside {WorkingDirectory} on macOS.\n" +
            $"{modeLine}\n" +
            "Rules:\n" +
            "- Prefer the provided tools over asking the user to run things. Read files before editing them.\n" +
            "- Keep file paths relative to the project root and never touch paths outside it.\n" +
            "- Report the result and material limitations when the task is complete.\n" +
            "- Never hard-wrap prose at a fixed column: write each paragraph of a reply as one unbroken\n" +
            "  line, however long, so a copied reply keeps its full line length wherever it is pasted.\n" +
            "  Lines inside a fenced code block keep their own breaks.\n" +
            "- If a tool result shows the user declined an action, do not retry it: ask how to proceed.\n" +
            $"- Today's date is {FoundationISO8601.Format(SwiftDate.Now)}.\n\n{appendedPolicy}"; // :1213-1215
    }

    // :1217-1235
    private async Task<JSONValue> UserContentValue(string text, IReadOnlyList<Attachment> images)
    {
        var usable = images.Where(image => image.IsImage).Take(4).ToList(); // :1218
        if (usable.Count == 0 || !OpenAICompatibleSessionConfig.IsVisionModel(currentModel)) // :1219
        {
            if (usable.Count == 0) return text; // :1220
            var names = string.Join(", ", usable.Select(image => image.Name)); // :1221
            return $"{text}\n\n[Attached images (model cannot see them): {names}]"; // :1222
        }
        var parts = new List<JSONValue> { JSONValue.FromDictionaryLiteral(("type", "text"), ("text", text)) }; // :1224
        foreach (var (image, base64) in await Attachment.Base64Images(usable)) // :1225
        {
            var url = $"data:{image.MimeType};base64,{base64}"; // :1226
            parts.Add(JSONValue.FromDictionaryLiteral(("type", "image_url"), ("image_url", JSONValue.FromDictionaryLiteral(("url", url))))); // :1227-1228
        }
        return JSONValue.FromArrayLiteral([.. parts]); // :1229
    }

    // :1236-1246. DeepSeek's thinking mode rejects the follow-up request with "The
    // `reasoning_content` in the thinking mode must be passed back to the API" unless every
    // tool-call message carries it (an empty string is accepted); where reasoning is not
    // replayed, the field is left off entirely.
    private JSONValue AssistantTurn(string content, string reasoning, IReadOnlyList<PendingToolCall> toolCalls)
    {
        var message = new Dictionary<string, JSONValue>
        {
            ["role"] = "assistant",
            ["content"] = content,
        }; // :1237
        if (OpenAICompatibleSessionConfig.ReplaysReasoning) message["reasoning_content"] = reasoning; // :1238
        if (toolCalls.Count > 0) // :1239
        {
            message["tool_calls"] = JSONValue.FromArrayLiteral([.. toolCalls.Select(tool => JSONValue.FromDictionaryLiteral(
                ("id", tool.Id),
                ("type", "function"),
                ("function", JSONValue.FromDictionaryLiteral(("name", tool.Name), ("arguments", tool.Arguments)))))]); // :1240-1245
        }
        return new JSONValue.Object(new ValueMap<string, JSONValue>(message, SwiftStringComparer.Instance));
    }

    // :1247-1258. System prompt plus roughly the last 40 exchanges; each exchange is user +
    // assistant + tool messages, so cap the raw message count. The running turn is never cut: a
    // blind suffix dropped its user message once the turn passed 50 messages, leaving the model
    // tool results with no task, so it stopped and asked what to do.
    private void TrimHistory()
    {
        if (messages.Count <= 60) return; // :1253
        var system = messages.Count > 0 ? messages[0] : null; // :1254
        var body = messages.Skip(1).ToList(); // :1255
        var turnStart = body.FindLastIndex(message => message["role"]?.JSONValueString == "user"); // :1256
        if (turnStart < 0) turnStart = body.Count;
        var current = body.Skip(turnStart).ToList(); // :1257
        var earlierAll = body.Take(turnStart).ToList();
        var earlier = earlierAll.Skip(Math.Max(0, earlierAll.Count - Math.Max(0, 50 - current.Count))).ToList(); // :1258
        messages = (system is not null ? new List<JSONValue> { system } : []).Concat(earlier).Concat(current).ToList(); // :1259
    }

    // :1268-1320. Repairs tool-call chains after trimming, compaction or interruption. These
    // APIs reject any history where a `tool` message has no preceding `assistant` message with a
    // matching `tool_calls` id (and vice versa). Once such an orphan exists, every later request
    // fails identically, so no retry can heal it - this drops orphaned tool messages and strips
    // tool calls that lost their answers, keeping the history sendable.
    private void SanitizeHistory()
    {
        var clean = new List<JSONValue>(messages.Count); // :1270
        var open = new List<(int Index, HashSet<string> Ids)>(); // :1273

        void CloseOpen()
        {
            // Calls that can never be answered now must go. Applied last-index-first so removals
            // never shift pending indices. :1275-1277
            foreach (var entry in open.OrderByDescending(item => item.Index))
            {
                if (clean[entry.Index] is not JSONValue.Object { Value: { } values } ||
                    values.TryGetValue("tool_calls", out var callsValue) is not true ||
                    callsValue is not JSONValue.Array { Value: { } calls })
                {
                    continue;
                }
                var kept = calls.Where(call =>
                    call["id"]?.JSONValueString is { Length: > 0 } id && !entry.Ids.Contains(id)).ToList(); // :1281-1284
                var fields = new Dictionary<string, JSONValue>(values);
                if (kept.Count == 0) // :1285
                {
                    fields.Remove("tool_calls"); // :1286
                    var content = (fields.TryGetValue("content", out var contentValue) ? contentValue.JSONValueString : null)?.Trimmed() ?? ""; // :1287
                    if (content.Length == 0) // :1288
                    {
                        clean.RemoveAt(entry.Index); // :1289
                    }
                    else
                    {
                        clean[entry.Index] = new JSONValue.Object(new ValueMap<string, JSONValue>(fields, SwiftStringComparer.Instance)); // :1291
                    }
                }
                else
                {
                    fields["tool_calls"] = JSONValue.FromArrayLiteral([.. kept]); // :1294
                    clean[entry.Index] = new JSONValue.Object(new ValueMap<string, JSONValue>(fields, SwiftStringComparer.Instance)); // :1295
                }
            }
            open.Clear(); // :1297
        }

        foreach (var message in messages) // :1299
        {
            var role = message["role"]?.JSONValueString ?? ""; // :1300
            if (role == "tool") // :1301
            {
                var id = message["tool_call_id"]?.JSONValueString ?? ""; // :1302
                var slot = open.FindIndex(entry => entry.Ids.Contains(id)); // :1303
                if (slot >= 0) // :1304
                {
                    clean.Add(message);            // :1305
                    open[slot].Ids.Remove(id);      // :1306
                    if (open[slot].Ids.Count == 0) open.RemoveAt(slot); // :1307
                }
                // else: orphaned by a trim/compact cut - drop it. :1308
                continue; // :1309
            }
            if (role == "assistant" &&
                message["tool_calls"]?.JSONValueArray is { Count: > 0 } assistantCalls) // :1310
            {
                CloseOpen();     // :1311
                clean.Add(message); // :1312
                var ids = new HashSet<string>(assistantCalls
                    .Select(call => call["id"]?.JSONValueString)
                    .Where(id => !string.IsNullOrEmpty(id))!); // :1313
                if (ids.Count > 0) open.Add((clean.Count - 1, ids)); // :1314
                continue; // :1315
            }
            CloseOpen();       // :1317
            clean.Add(message); // :1318
        }
        CloseOpen();    // :1320
        messages = clean; // :1321
    }

    // :1332-1335
    private string NextID(string prefix)
    {
        counter += 1; // :1333
        return $"{prefix}-{counter}-{SwiftString.Prefix(SwiftUUID.UuidString(Guid.NewGuid()), 8)}"; // :1334
    }

    // :72. Runaway guard only (see the block comment at :67-71 in the Swift source, quoted at
    // PR-PROVAPI-097's evidence): a turn used to stop after 12 rounds and report itself as
    // completed, so any real task ended mid-edit with no reply and needed a "continue" every
    // dozen tool calls. Real work runs until the model answers without tools or the user hits
    // stop; hitting this many rounds means the model is looping, and the turn says so.
    private const int MaxRoundsPerTurn = 200;
}
