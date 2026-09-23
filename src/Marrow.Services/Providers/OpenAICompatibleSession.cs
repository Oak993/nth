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
    private List<MCPHub.Tool> mcpTools = [];                // :59
    private bool didLoadMCPTools;                            // :60

    // :61-65
    private IReadOnlyList<JSONValue> McpToolDefinitions => [.. mcpTools.Select(tool => JSONValue.FromDictionaryLiteral(
        ("type", "function"),
        ("function", JSONValue.FromDictionaryLiteral(
            ("name", tool.CallName), ("description", tool.Description), ("parameters", tool.InputSchema)))))];

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

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests): :59-60. Sets the already-loaded
    // MCP tool list directly so PR-PROVAPI-120's naming can be tested without spawning a real MCP
    // server process.
    internal void SeedMcpToolsForTests(IReadOnlyList<MCPHub.Tool> tools)
    {
        mcpTools = [.. tools];
        didLoadMCPTools = true;
    }

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests): drives PerformStream directly
    // against a fake chat endpoint, so SSE draining and tool-call accumulation (:600-735) can be
    // tested without going through PerformTurn's tool-execution loop, whose bodies (:894-1035)
    // are a later part of this unit.
    internal Task<StreamRound> PerformStreamForTests(string model, string? effort = null, bool withTools = true) =>
        PerformStream(RequestPayload(model, effort, withTools));

    // Test-only seams for this part's self-contained history and message helpers (InternalsVisibleTo
    // Marrow.Services.Tests): CondenseHistory, TrimHistory and SanitizeHistory read and rewrite
    // `messages` (:43) directly, so tests seed it here rather than driving a whole turn.
    internal void SetMessagesForTests(IEnumerable<JSONValue> value) => messages = [.. value];
    internal void CondenseHistoryForTests() => CondenseHistory();
    internal void TrimHistoryForTests() => TrimHistory();
    internal void SanitizeHistoryForTests() => SanitizeHistory();
    internal static string SummaryForTests(string text, int limit) => Summary(text, limit);
    internal Task<bool> ApproveIfNeededForTests(ToolCall.Kind kind, string title, string? detail = null, string? toolItemID = null) =>
        ApproveIfNeeded(kind, title, detail, toolItemID);
    internal void SetInterruptedForTests(bool value) => interrupted = value;
    internal void RefreshSystemPromptForTests() => RefreshSystemPrompt();
    internal string SystemPromptForTests() => SystemPrompt();
    internal bool RequiresApprovalForTests(ToolCall.Kind kind) => RequiresApproval(kind);
    internal void SetApproveAllRemainingForTests(bool value) => approveAllRemaining = value;

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests, PR-PROVAPI-113, PR-PROVAPI-114):
    // registers a pending approval continuation exactly as ApproveIfNeeded does at :1082, without
    // going through MainActor.Run's event post, so ResolveApproval and Stop's decline-all sweep are
    // testable in isolation.
    internal Task<bool> RegisterPendingApprovalForTests(string id) =>
        CheckedContinuation<bool>.With(continuation => pendingApprovals[id] = continuation);

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests, PR-PROVAPI-117): drives ReportUsage
    // directly with a scripted StreamRound.
    internal void ReportUsageForTests(StreamRound round) => ReportUsage(round);

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests, PR-PROVAPI-121, PR-PROVAPI-122,
    // PR-PROVAPI-123): drives StreamOneRound's transport-retry ladder directly.
    internal Task<StreamRound> StreamOneRoundForTests(string model, string? effort = null, bool withTools = true) =>
        StreamOneRound(model, effort, withTools);

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests, PR-PROVAPI-123): exercises the
    // terminal-transport-message composition (:562-567) directly.
    internal string TerminalTransportMessageForTests(Exception error) => TerminalTransportMessage(error);

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

    // :140-148. A chunk from the detached SSE drain to the actor-coalesced stream.
    private abstract record StreamChunk
    {
        public sealed record TextChunk(string Value) : StreamChunk;                                  // :141
        public sealed record ReasoningChunk(string Value) : StreamChunk;                              // :142
        public sealed record ToolCallChunk(int Index, string Id, string Name, string Arguments) : StreamChunk; // :143
        public sealed record UsageChunk(ContextUsage Value) : StreamChunk;                            // :144
        public sealed record TokenUsageChunk(ReportedTokenUsage Value) : StreamChunk;                 // :145
        public sealed record StreamErrorChunk(string Value) : StreamChunk;                            // :146
        public sealed record TransportErrorChunk(Exception Value) : StreamChunk;                      // :147
    }

    // :152-227. Holds consecutive text off the main actor and yields it as one larger delta, at
    // most 30 ms after the first piece, so the main actor hops less (R-TASK-6: Actor + Spawn +
    // TrySleep for this file's flush pattern).
    private sealed class StreamCoalescer : Actor
    {
        private readonly AsyncStream<StreamChunk>.Continuation continuation; // :153
        private string pendingText = "";                                     // :154
        private string pendingReasoning = "";                                // :155
        private bool finished;                                                // :156

        public StreamCoalescer(AsyncStream<StreamChunk>.Continuation continuation) => this.continuation = continuation; // :158-160

        public Task AppendText(string value) => Isolated(() => // :162-167
        {
            if (pendingReasoning.Length > 0) Flush();
            var idle = pendingText.Length == 0;
            pendingText += value;
            if (idle) ScheduleFlush();
        });

        public Task AppendReasoning(string value) => Isolated(() => // :169-174
        {
            if (pendingText.Length > 0) Flush();
            var idle = pendingReasoning.Length == 0;
            pendingReasoning += value;
            if (idle) ScheduleFlush();
        });

        public Task EmitToolCall(int index, string id, string name, string arguments) => Isolated(() => // :176-179
        {
            Flush();
            continuation.Yield(new StreamChunk.ToolCallChunk(index, id, name, arguments));
        });

        public Task EmitUsage(ContextUsage value) => Isolated(() => // :181-184
        {
            Flush();
            continuation.Yield(new StreamChunk.UsageChunk(value));
        });

        public Task EmitTokenUsage(ReportedTokenUsage value) => Isolated(() => // :186-189
        {
            Flush();
            continuation.Yield(new StreamChunk.TokenUsageChunk(value));
        });

        public Task EmitStreamError(string message) => Isolated(() => // :191-194
        {
            Flush();
            continuation.Yield(new StreamChunk.StreamErrorChunk(message));
        });

        public Task Finish() => Isolated(() => // :196-201
        {
            if (finished) return;
            finished = true;
            Flush();
            continuation.Finish();
        });

        public Task Fail(Exception error) => Isolated(() => // :203-209
        {
            if (finished) return;
            finished = true;
            Flush();
            continuation.Yield(new StreamChunk.TransportErrorChunk(error));
            continuation.Finish();
        });

        private void Flush() // :211-220
        {
            if (pendingText.Length > 0)
            {
                continuation.Yield(new StreamChunk.TextChunk(pendingText));
                pendingText = "";
            }
            if (pendingReasoning.Length > 0)
            {
                continuation.Yield(new StreamChunk.ReasoningChunk(pendingReasoning));
                pendingReasoning = "";
            }
        }

        private void ScheduleFlush() => Spawn(async () => // :222-227
        {
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(30));
            await Isolated(Flush);
        });
    }

    // :131-136. Carries the untyped transport error up together with whether the user had
    // already seen part of an answer, so the retry decision is made on the error's code, not its
    // wording. Internal, not private, only so PR-PROVAPI-137's ForTests seam can hand its result
    // back typed (InternalsVisibleTo Marrow.Services.Tests); nothing outside this file catches it.
    internal sealed class StreamAttemptFailure(Exception underlying, bool emittedOutput) : Exception
    {
        public Exception Underlying { get; } = underlying;
        public bool EmittedOutput { get; } = emittedOutput;
    }

    // :86-87
    private const int MaxTransportRetries = 2;
    private static readonly TimeSpan[] TransportRetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    // :91-97. The transport failures worth another attempt, by code rather than by message.
    private static bool IsTransientTransportFailure(Exception error) =>
        error is URLError urlError && urlError.URLErrorCode is
            URLError.Code.NetworkConnectionLost or URLError.Code.TimedOut or URLError.Code.CannotConnectToHost;

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests, PR-PROVAPI-121, PR-PROVAPI-122,
    // PR-PROVAPI-123): a single stream attempt, so a test can script a failure/success sequence
    // for StreamOneRound's retry ladder without a live dropped connection. Defaults to the real
    // PerformStream (:600-735).
    protected virtual Task<StreamRound> PerformStreamAttempt(Dictionary<string, JSONValue> payload) => PerformStream(payload);

    // :506-557. Repairs the history, loads MCP tools once per session, builds the payload once and
    // retries a dropped connection at the transport level only, waiting the delay ladder between
    // attempts. Also the seam PR-PROVAPI-089..112's fixture overrides to keep that part's round-loop
    // orchestration tests independent of the network.
    protected virtual async Task<StreamRound> StreamOneRound(string model, string? effort, bool withTools = true)
    {
        SanitizeHistory(); // :509
        if (withTools && !didLoadMCPTools) // :510
        {
            mcpTools = [.. await MCPHub.Shared.Tools()];
            didLoadMCPTools = true;
        }
        var payload = RequestPayload(model, effort, withTools); // :511
        var attempt = 0; // :516
        while (true) // :517
        {
            SwiftTask.CheckCancellation(); // :518
            if (interrupted || isStopping) throw new CancellationError(); // :521
            sseDrain?.Cancel(); // :522
            sseDrain = null; // :523
            var task = MainActor.SpawnThrowing<StreamRound>(() => PerformStreamAttempt(payload)); // :524
            roundTask = task; // :525
            try
            {
                var round = await task.Value; // :527
                roundTask = null; // :528
                SwiftTask.CheckCancellation(); // :529
                if (interrupted || isStopping) throw new CancellationError(); // :530
                return round; // :531
            }
            catch (StreamAttemptFailure failure) // :532
            {
                roundTask = null; // :533
                sseDrain?.Cancel(); // :534
                sseDrain = null; // :535
                SwiftTask.CheckCancellation(); // :536
                if (interrupted || isStopping) throw new CancellationError(); // :537
                if (!IsTransientTransportFailure(failure.Underlying)) throw failure.Underlying; // :540
                if (attempt >= MaxTransportRetries) // :541
                {
                    throw new ProviderError(new ProviderError.Case.Failed(TerminalTransportMessage(failure.Underlying))); // :542
                }
                if (failure.EmittedOutput) // :547
                {
                    OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
                    {
                        NoticeLevel = Notice.Level.Warning,
                        Message = $"{OpenAICompatibleSessionConfig.Title} lost the connection while answering. That reply was cut off, and this response is restarting.", // :548
                    }));
                }
                var delay = TransportRetryDelays[Math.Min(attempt, TransportRetryDelays.Length - 1)]; // :550
                attempt += 1; // :551
                await SwiftTask.Sleep(delay); // :552
            }
            catch (Exception) // :553-556
            {
                roundTask = null; // :554
                throw; // :555
            }
        }
    }

    // :560-567. What is left when the connection keeps dropping: how many times the same request
    // was tried, and the transport's own reason. STOP-PROVAPI-123 (section 6, kind: unverified):
    // error.localizedDescription for a URLError has no defined Swift text in this port
    // (SwiftErrors.LocalizedDescription throws NotSupportedException for it by design - R-ERR-2,
    // O-14 - confirmed by Marrow.Core.Tests HttpTests "SwiftErrors.LocalizedDescription throws for
    // a URLError"); no ED or OD records a per-code replacement text. Reproduced literally: this
    // call surfaces the same unresolved gap loudly instead of inventing wording.
    private string TerminalTransportMessage(Exception error) // :562
    {
        var reason = SwiftErrors.LocalizedDescription(error); // :563
        var attempts = MaxTransportRetries + 1; // :564
        var baseMessage = $"{OpenAICompatibleSessionConfig.Title} could not reach the network after {attempts} attempts. Check the connection, then try again."; // :565
        return reason.Length == 0 ? baseMessage : $"{baseMessage} ({reason})"; // :566-567
    }

    // A JSON Schema property with a type and an optional description (helper for ToolDefinitions;
    // not itself cited, only the values it produces are).
    private static JSONValue SchemaField(string type, string? description = null) =>
        description is null
            ? JSONValue.FromDictionaryLiteral(("type", type))
            : JSONValue.FromDictionaryLiteral(("type", type), ("description", description));

    // An object schema with named properties and optionally a required list (helper for
    // ToolDefinitions; not itself cited, only the values it produces are).
    private static JSONValue ObjectSchema(IEnumerable<(string Name, JSONValue Field)> properties, params string[] required)
    {
        var propertiesObject = new JSONValue.Object(new ValueMap<string, JSONValue>(
            properties.Select(p => new KeyValuePair<string, JSONValue>(p.Name, p.Field)), SwiftStringComparer.Instance));
        return required.Length == 0
            ? JSONValue.FromDictionaryLiteral(("type", "object"), ("properties", propertiesObject))
            : JSONValue.FromDictionaryLiteral(
                ("type", "object"),
                ("properties", propertiesObject),
                ("required", JSONValue.FromArrayLiteral([.. required.Select(name => (JSONValue)new JSONValue.String(name))])));
    }

    // One OpenAI function-tool definition (helper for ToolDefinitions; not itself cited, only the
    // values it produces are).
    private static JSONValue ToolDefinition(string name, string description, JSONValue parameters) =>
        JSONValue.FromDictionaryLiteral(("type", "function"), ("function", JSONValue.FromDictionaryLiteral(
            ("name", name), ("description", description), ("parameters", parameters))));

    // :817-849. The six built-in tool definitions, transcribed byte for byte from the Swift
    // literal. Referenced by RequestPayload (PR-PROVAPI-119); the tool bodies these describe
    // (:894-1035) land in a later part of this unit.
    private static readonly IReadOnlyList<JSONValue> ToolDefinitions =
    [
        ToolDefinition(
            "read_file",
            "Read a file inside the project. Path is relative to the project root. The whole file by default; for a big file (hundreds of lines) pass start_line and line_count to read just the part you need, and the result says how many lines the file has.",
            ObjectSchema(
                [
                    ("path", SchemaField("string", "Relative file path")),
                    ("start_line", SchemaField("integer", "First line to read, counted from 1")),
                    ("line_count", SchemaField("integer", "How many lines to read from start_line")),
                ],
                "path")),
        ToolDefinition(
            "list_files",
            "List files in a directory inside the project.",
            ObjectSchema(
                [
                    ("path", SchemaField("string", "Relative directory, empty for the root")),
                    ("recursive", SchemaField("boolean")),
                ])),
        ToolDefinition(
            "search_text",
            "Search file contents for a fixed string or regex.",
            ObjectSchema(
                [
                    ("pattern", SchemaField("string")),
                    ("path", SchemaField("string", "Relative directory or file")),
                    ("regex", SchemaField("boolean")),
                ],
                "pattern")),
        ToolDefinition(
            "write_file",
            "Create or overwrite a file inside the project with the full new content.",
            ObjectSchema(
                [
                    ("path", SchemaField("string")),
                    ("content", SchemaField("string")),
                ],
                "path", "content")),
        ToolDefinition(
            "edit_file",
            "Replace one exact block of text in a file with new text. old_string must appear exactly once.",
            ObjectSchema(
                [
                    ("path", SchemaField("string")),
                    ("old_string", SchemaField("string")),
                    ("new_string", SchemaField("string")),
                ],
                "path", "old_string", "new_string")),
        ToolDefinition(
            "run_command",
            "Run a shell command in the project root and capture its output.",
            ObjectSchema(
                [
                    ("command", SchemaField("string")),
                ],
                "command")),
    ];

    // :569-581. PR-PROVAPI-119.
    private Dictionary<string, JSONValue> RequestPayload(string model, string? effort, bool withTools = true)
    {
        var payload = new Dictionary<string, JSONValue>
        {
            ["model"] = model,                                  // :571
            ["messages"] = JSONValue.FromArrayLiteral([.. messages]), // :572
            ["stream"] = true,                                  // :573
        };
        if (withTools) // :575
        {
            payload["tools"] = JSONValue.FromArrayLiteral([.. ToolDefinitions, .. McpToolDefinitions]); // :576
            payload["tool_choice"] = "auto"; // :577
        }
        ApplyProviderPayload(payload, model, effort, withTools); // :579
        return payload; // :580
    }

    // :588-590. DeepSeek turns thinking on and maps the effort; Meta takes a top-level
    // reasoning_effort; Z.ai sets clear_thinking, a max_tokens cap and tool_stream (later parts).
    public virtual void ApplyProviderPayload(Dictionary<string, JSONValue> payload, string model, string? effort, bool withTools)
    {
        if (ReasoningEffort(effort, model) is { } mapped) payload["reasoning_effort"] = mapped; // :589
    }

    // :592-593. The subclass's effort mapping, applied when the payload is built.
    public virtual string? ReasoningEffort(string? effort, string model) => null;

    // :596-598
    private static Task<byte[]> Encode(Dictionary<string, JSONValue> payload) =>
        Task.Run(() => new JSONValue.Object(new ValueMap<string, JSONValue>(payload, SwiftStringComparer.Instance)).Data());

    // :600-735. Issues the POST, reads a non-200 body directly, and otherwise drains the SSE
    // stream through the coalescer, accumulating text, reasoning, tool calls, usage and any
    // in-stream error into one StreamRound.
    private async Task<StreamRound> PerformStream(Dictionary<string, JSONValue> payload)
    {
        var request = new URLRequest(OpenAICompatibleSessionConfig.ChatURL, timeoutInterval: 300) { HttpMethod = "POST" } // :601-602
            .SetValue("application/json", "Content-Type")   // :603
            .SetValue($"Bearer {ApiKey}", "Authorization")  // :604
            .SetValue("application/json", "Accept");        // :605
        request = request with { HttpBody = await Encode(payload) }; // :607
        if (SwiftTask.IsCancelled || interrupted) throw new CancellationError(); // :608

        var messageID = NextID("message"); // :610
        var thoughtID = NextID("thought"); // :611
        var content = "";                  // :612
        var reasoning = "";                // :613
        var sawContent = false;            // :614
        var sawReasoning = false;          // :615
        var accumulators = new Dictionary<int, (string Id, string Name, string Arguments)>(); // :616-617
        ContextUsage? usage = null;             // :618
        ReportedTokenUsage? tokenUsage = null;  // :619
        string? streamError = null;             // :620
        const int statusCode = 200;             // :621

        URLSession.AsyncBytes bytes; // :623
        URLResponse response;         // :624
        try
        {
            (bytes, response) = await URLSession.Shared.Bytes(request); // :626
        }
        catch (CancellationError) // :627
        {
            throw new CancellationError(); // :628
        }
        catch (Exception error) // :629-631
        {
            throw new StreamAttemptFailure(error, false);
        }
        if (response is HTTPURLResponse { StatusCode: not 200 } http) // :633
        {
            var body = ""; // :635
            await foreach (var line in bytes.Lines) // :636
            {
                body += line + "\n"; // :637
                if (body.Length > 8_000) break; // :638
            }
            return new StreamRound { StatusCode = http.StatusCode, RawError = ErrorMessage(body, http.StatusCode) }; // :640-643
        }

        sseDrain?.Cancel(); // :648
        var contextWindow = OpenAICompatibleSessionConfig.ContextWindow; // :649
        var (stream, continuation) = AsyncStream<StreamChunk>.MakeStream(); // :650
        var coalescer = new StreamCoalescer(continuation); // :651
        var drain = TaskHandle.Detached(async () => // :652-661
        {
            try { await DrainSSE(bytes, coalescer, contextWindow); }
            catch (CancellationError) { await coalescer.Finish(); }
        });
        sseDrain = TaskHandle.Detached(async () => // :663-666
            await SwiftTask.WithCancellationHandler(() => drain.Completion, () => drain.Cancel()));
        continuation.OnTermination = _ => drain.Cancel(); // :669

        try
        {
            await foreach (var chunk in stream) // :672
            {
                if (SwiftTask.IsCancelled || interrupted) throw new CancellationError(); // :673
                switch (chunk)
                {
                    case StreamChunk.TextChunk text: // :675
                        content += text.Value;
                        if (!sawContent) sawContent = true;
                        OnEvent?.Invoke(new ProviderEvent.MessageDelta(messageID, text.Value)); // :678
                        break;
                    case StreamChunk.ReasoningChunk think: // :679
                        reasoning += think.Value;
                        if (!sawReasoning) sawReasoning = true;
                        OnEvent?.Invoke(new ProviderEvent.ReasoningDelta(thoughtID, think.Value)); // :682
                        break;
                    case StreamChunk.ToolCallChunk toolCall: // :683
                        (string Id, string Name, string Arguments) acc = accumulators.TryGetValue(toolCall.Index, out var existing) ? existing : ("", "", "");
                        if (toolCall.Id.Length > 0) acc.Id = toolCall.Id;         // :685
                        if (toolCall.Name.Length > 0) acc.Name = toolCall.Name;   // :686
                        acc.Arguments += toolCall.Arguments;                      // :687
                        accumulators[toolCall.Index] = acc;                       // :688
                        break;
                    case StreamChunk.UsageChunk usageChunk: // :690
                        usage = usageChunk.Value;
                        break;
                    case StreamChunk.TokenUsageChunk tokenUsageChunk: // :692
                        tokenUsage = tokenUsageChunk.Value;
                        break;
                    case StreamChunk.StreamErrorChunk streamErrorChunk: // :694
                        streamError = streamErrorChunk.Value;
                        break;
                    case StreamChunk.TransportErrorChunk transportErrorChunk: // :695
                        throw TransportErrorFailure(transportErrorChunk.Value, streamError, content, sawContent, sawReasoning, statusCode);
                    default:
                        throw new UnreachableException();
                }
            }
        }
        finally
        {
            sseDrain = null; // :671 defer
        }

        var round = new StreamRound
        {
            Content = content,       // :712
            Reasoning = reasoning,   // :713
            Usage = usage,           // :714
            TokenUsage = tokenUsage, // :715
            RawError = streamError,  // :716
            StatusCode = statusCode, // :717
        };
        if (sawContent || content.Length > 0) OnEvent?.Invoke(new ProviderEvent.MessageCompleted(messageID, content)); // :718-719
        if (sawReasoning || reasoning.Length > 0) OnEvent?.Invoke(new ProviderEvent.ReasoningCompleted(thoughtID, reasoning)); // :721-722
        var toolCalls = new List<PendingToolCall>(); // :711 (declared with round in Swift)
        foreach (var index in accumulators.Keys.OrderBy(key => key)) // :724-725
        {
            var acc = accumulators[index]; // :726
            if (acc.Name.Length == 0) continue; // :727
            toolCalls.Add(new PendingToolCall(acc.Id.Length == 0 ? NextID("tool") : acc.Id, acc.Name, acc.Arguments)); // :728-732
        }
        return round with { ToolCalls = new ValueList<PendingToolCall>(toolCalls) }; // :734
    }

    // :694-707. PR-PROVAPI-137. The transport-error branch of PerformStream's stream loop,
    // extracted so it is testable without a live network failure: an in-stream error takes
    // priority; otherwise, once HydraPrompts.StreamedDelegationsIn finds at least one delegation
    // in the content streamed so far, the round fails outright with the transport error's own
    // description plus the fixed suffix, since replaying the response could dispatch the same
    // work twice; anything else is a StreamAttemptFailure for StreamOneRound's retry ladder.
    private Exception TransportErrorFailure(Exception transportError, string? streamError, string content, bool sawContent, bool sawReasoning, int statusCode)
    {
        if (streamError is { Length: > 0 } nonEmptyStreamError) // :696-697
        {
            return new ProviderError(new ProviderError.Case.Failed(FriendlyError(nonEmptyStreamError, statusCode)));
        }
        if (HydraPrompts.StreamedDelegationsIn(content) is { } delegations && delegations.Delegations.Count > 0) // :701-702
        {
            return new ProviderError(new ProviderError.Case.Failed(
                $"{SwiftErrors.LocalizedDescription(transportError)} This response was not retried because it had already streamed a head delegation."));
        }
        return new StreamAttemptFailure(transportError, sawContent || sawReasoning); // :707
    }

    // Test-only seam (InternalsVisibleTo Marrow.Services.Tests): exercises PR-PROVAPI-137's
    // decision without a live network failure or FriendlyError's provider-specific mapping.
    internal Exception TransportErrorFailureForTests(Exception transportError, string? streamError, string content, bool sawContent = false, bool sawReasoning = false, int statusCode = 200) =>
        TransportErrorFailure(transportError, streamError, content, sawContent, sawReasoning, statusCode);

    // :740-790. Runs off the main actor: trims, parses and picks apart each SSE line, then feeds
    // chunks to the coalescer. A transport failure is delivered as a chunk; CancellationError is
    // rethrown for the stop path.
    private static async Task DrainSSE(URLSession.AsyncBytes bytes, StreamCoalescer coalescer, int contextWindow)
    {
        try
        {
            await foreach (var line in bytes.Lines) // :742
            {
                if (SwiftTask.IsCancelled) throw new CancellationError(); // :743
                var trimmed = line.Trimmed(); // :744
                if (!SwiftString.HasPrefix(trimmed, "data:")) continue; // :745
                var data = FoundationText.Trim(SwiftString.DropFirst(trimmed, 5)); // :746
                if (data == "[DONE]") break; // :747
                if (JSONValue.Parse(data) is not { } json) continue; // :748
                if (json["error"] is { IsNull: false } err) // :749
                {
                    await coalescer.EmitStreamError(err["message"]?.JSONValueString ?? err.DisplayText); // :750
                    continue; // :751
                }
                if (json["usage"] is { IsNull: false } u) // :753
                {
                    var input = u["prompt_tokens"]?.JSONValueInt ?? u["input_tokens"]?.JSONValueInt;       // :754
                    var output = u["completion_tokens"]?.JSONValueInt ?? u["output_tokens"]?.JSONValueInt; // :755
                    var cached = u["prompt_tokens_details"]?["cached_tokens"]?.JSONValueInt                // :756-758
                        ?? u["input_tokens_details"]?["cached_tokens"]?.JSONValueInt
                        ?? u["prompt_cache_hit_tokens"]?.JSONValueInt;
                    long total = u["total_tokens"]?.JSONValueInt ?? ((input ?? 0) + (output ?? 0)); // :761
                    if (total > 0) await coalescer.EmitUsage(new ContextUsage { UsedTokens = total, WindowTokens = contextWindow }); // :762-763
                    await coalescer.EmitTokenUsage(new ReportedTokenUsage { Total = total, Input = input, Output = output, CachedInput = cached }); // :765
                }
                if (json["choices"]?.JSONValueArray is not { Count: > 0 } choices) continue; // :767
                var delta = choices[0]["delta"] ?? new JSONValue.Null(); // :768
                if (delta["content"]?.JSONValueString is { Length: > 0 } text) await coalescer.AppendText(text); // :769-770
                if ((delta["reasoning_content"]?.JSONValueString ?? delta["reasoning"]?.JSONValueString) is { Length: > 0 } think) // :772
                {
                    await coalescer.AppendReasoning(think); // :773
                }
                foreach (var call in delta["tool_calls"]?.JSONValueArray ?? []) // :775
                {
                    var index = call["index"]?.JSONValueInt ?? 0;               // :776
                    var id = call["id"]?.JSONValueString ?? "";                 // :777
                    var name = call["function"]?["name"]?.JSONValueString ?? ""; // :778
                    var args = call["function"]?["arguments"]?.JSONValueString ?? ""; // :779
                    await coalescer.EmitToolCall((int)index, id, name, args); // :780
                }
            }
            await coalescer.Finish(); // :783
        }
        catch (CancellationError) // :784
        {
            await coalescer.Finish(); // :785
            throw new CancellationError(); // :786
        }
        catch (Exception error) // :787
        {
            await coalescer.Fail(error); // :788
        }
    }

    // :792-800. PR-PROVAPI-125, PR-PROVAPI-126.
    private string ErrorMessage(string body, int statusCode)
    {
        if (JSONValue.Parse(body) is { } json) // :793
        {
            var message = json["error"]?["message"]?.JSONValueString ?? json["message"]?.JSONValueString; // :794
            if (message is { Length: > 0 }) return message; // :795
        }
        var trimmed = body.Trimmed(); // :797
        if (trimmed.Length > 0) return SwiftString.Prefix(trimmed, 500); // :798
        return $"{OpenAICompatibleSessionConfig.Title} returned status {statusCode}."; // :799
    }

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

    // :1058-1089. PR-PROVAPI-161. An interrupted turn declines whatever is still asking, before
    // anything else. Cancelling the wait (the caller's task cancelled while the dialog is open)
    // resumes the continuation with false and removes it from pendingApprovals, mirroring the
    // Swift's withTaskCancellationHandler onCancel hop back to the main actor.
    private async Task<bool> ApproveIfNeeded(ToolCall.Kind kind, string title, string? detail, string? toolItemID)
    {
        if (interrupted) return false; // :1060
        if (!RequiresApproval(kind)) return true; // :1061
        var id = NextID("approval"); // :1062
        var approvalKind = kind switch // :1063-1067
        {
            ToolCall.Kind.Command => ApprovalRequest.Kind.Command, // :1064
            ToolCall.Kind.Edit => ApprovalRequest.Kind.FileChange, // :1065
            _ => ApprovalRequest.Kind.Tool,                        // :1066
        };
        var options = new ValueList<ApprovalRequest.Option>( // :1068-1072
        [
            new ApprovalRequest.Option("allow", "Approve", ApprovalRequest.Option.Role.Approve),          // :1069
            new ApprovalRequest.Option("always", "Approve for turn", ApprovalRequest.Option.Role.ApproveAlways), // :1070
            new ApprovalRequest.Option("decline", "Decline", ApprovalRequest.Option.Role.Decline),         // :1071
        ]);
        await MainActor.Run(() => // :1073-1075
        {
            OnEvent?.Invoke(new ProviderEvent.Approval(new ApprovalRequest(id, approvalKind, title, options, Detail: detail, ToolItemID: toolItemID)));
        });
        return await SwiftTask.WithCancellationHandler( // :1076-1083
            () => CheckedContinuation<bool>.With(continuation =>
            {
                if (SwiftTask.IsCancelled || interrupted) // :1078-1081
                {
                    continuation.Resume(false);
                    return;
                }
                pendingApprovals[id] = continuation; // :1082
            }),
            () => MainActor.Spawn(async () => // :1084-1087
            {
                if (pendingApprovals.Remove(id, out var continuation)) continuation.Resume(false);
                await Task.CompletedTask;
            }));
    }

    // :1098-1117. Self-contained; no missing dependency.
    private const long CondenseHistoryAt = 160_000;
    private const long CondenseHistoryTo = 100_000;
    private const int CondenseKeepsRecent = 10;
    // DEVIATION OD-0012 F1 (PR-PROVAPI-138): upstream :80 reads
    // `"…[Droppy Code condensed this output"`; the product name in the marker becomes Marrow.
    private const string CondensedMarker = "\U00002026[Marrow condensed this output"; // U+2026 HORIZONTAL ELLIPSIS

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
                "You are in PLAN mode: research the project, then write a clear step-by-step plan. Do not edit files or run commands that change anything; reads, lists and searches are fine.", // :1180 PR-PROVAPI-142
            // DEVIATION OD-0012 F1 (PR-PROVAPI-143): upstream :1182 reads
            // `"...the user approves each one in Droppy Code."`; the product name becomes Marrow.
            (InteractionMode.Build, RuntimeMode.Supervised) =>
                "You are in BUILD mode with supervised permissions: use tools freely but the user approves each one in Marrow.", // :1182
            // DEVIATION OD-0012 F1 (PR-PROVAPI-144): upstream :1184 reads
            // `"...the user approves shell commands in Droppy Code."`; the product name becomes Marrow.
            (InteractionMode.Build, RuntimeMode.AutoAcceptEdits) or (InteractionMode.Build, RuntimeMode.Auto) =>
                "You are in BUILD mode: read and edit files freely; the user approves shell commands in Marrow.", // :1184
            (InteractionMode.Build, RuntimeMode.FullAccess) =>
                "You are in BUILD mode with full access: use tools freely without asking.", // :1186 PR-PROVAPI-145
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
        // DEVIATION OD-0012 F1 (PR-PROVAPI-141): upstream :1202 reads
        // `"You are Droppy Code's \(config.agentIdentity(currentModel)), working inside \(workingDirectory) on macOS."`;
        // the product name becomes Marrow and the platform word becomes Windows.
        return
            $"You are Marrow's {OpenAICompatibleSessionConfig.AgentIdentity(currentModel)}, working inside {WorkingDirectory} on Windows.\n" +
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

    // :1326-1330. PR-PROVAPI-153. Truncates timeline-shown tool output; the string returned to
    // the model (executeTool's own return values, a later part of this unit) is never truncated
    // by this helper. Bytes bound characters, so a short text (measured in bytes) skips the
    // grapheme count.
    private static string Summary(string text, int limit)
    {
        if (SwiftString.Utf8Count(text) <= limit || SwiftString.Count(text) <= limit) return text; // :1328
        return $"{SwiftString.Prefix(text, limit)}\n\U00002026(truncated)"; // :1329 U+2026 HORIZONTAL ELLIPSIS
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
