using Avalonia.Controls;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Port.MultiAccount;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B2 1fbf81e), :1-3758. Skeleton (c100 group 1): every type
// the file declares (:5-163, the private helpers of ThreadRuntime :354-357, :510-538, :748-755, :791-802,
// :1791-1796 and StreamLoad :3147-3177), the record shapes, the enum cases and every observed and
// @ObservationIgnored stored property that another file reads are complete; every didSet (:172-182, :196-201,
// :206-211, :230-235, :239-243), every initializer with behaviour, every computed property and every method are
// left to the implementing unit, as are the private stored state (:274-310, :414-508, :793-797, :1799, :1941,
// :1960-1962), the private methods and the private `TimelineItem.Content.appendStreamed` extension (:3663-3694).
// Forms: NSView is Control and NSRectEdge is the PreviewPanelEdge stand-in of AttachmentPreviewPanel (ED-095,
// AttachmentPreviewPanel.cs:25-33); a stored Task is a TaskHandle (R-TASK-1); URL is SwiftURL; Date is a UTC
// DateTimeOffset; TimeInterval is double seconds and Duration a TimeSpan; a Swift Int is long (R-NUM-4).
// ENGINEERING DECISION (skeleton, open for the join): Swift's `Result<RevertPreview, Error>` (:1785, :1795) has no
// port type; it is carried as a finished Task<RevertPreview> (success is its value, failure its exception).

namespace Marrow.App.Runtime;

// :5-50. @MainActor @Observable final class -> MainActorModel (R-OBS-6). Equatable by identity (:46-50): the
// default reference equality of a C# class is that `===`, so nothing is overridden.
public sealed class TimelineEntry : MainActorModel
{
    // :9-18
    public enum Kind
    {
        User,
        Assistant,
        Reasoning,
        Tool,
        Plan,
        Todos,
        Notice,
        TurnEnd,
    }

    private TimelineItem item = null!;

    public string Id { get; }                 // :20
    public Kind TimelineEntryKind { get; }    // :22 Swift: kind (R-NAME-3)
    public Guid? TurnID { get; }              // :23

    // :24
    public TimelineItem Item { get => Read(item); set => Set(ref item, value); }

    // ThreadRuntime.swift:26-40
    public TimelineEntry(TimelineItem item)
    {
        this.item = item;
        Id = item.Id;
        TimelineEntryKind = item.TimelineItemContent switch
        {
            TimelineItem.Content.User => Kind.User,
            TimelineItem.Content.Assistant => Kind.Assistant,
            TimelineItem.Content.Reasoning => Kind.Reasoning,
            TimelineItem.Content.Tool => Kind.Tool,
            TimelineItem.Content.Plan => Kind.Plan,
            TimelineItem.Content.Todos => Kind.Todos,
            TimelineItem.Content.Notice => Kind.Notice,
            TimelineItem.Content.TurnEnd => Kind.TurnEnd,
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        TurnID = item.TurnID;
    }
}

// :54-100 (R-VAL-1)
public sealed record ReplyQuote
{
    public Guid Id { get; init; } = Guid.NewGuid(); // :55
    public required string Text { get; init; }      // :56

    // :58-63
    public string Excerpt
    {
        get
        {
            var firstLine = Text.Split('\n', StringSplitOptions.None)
                .Select(line => FoundationText.Trim(line, includeNewlines: true))
                .FirstOrDefault(line => line.Length != 0);
            if (firstLine is null) return "";

            var words = new List<string>();
            var currentWord = new List<string>();
            foreach (var character in SwiftString.Characters(firstLine))
            {
                if (SwiftCharacter.IsWhitespace(character))
                {
                    if (currentWord.Count == 0) continue;
                    words.Add(string.Concat(currentWord));
                    currentWord.Clear();
                }
                else
                {
                    currentWord.Add(character);
                }
            }
            if (currentWord.Count != 0) words.Add(string.Concat(currentWord));

            var excerpt = string.Join(" ", words);
            var characters = SwiftString.Characters(excerpt).ToArray();
            return characters.Length > 72 ? string.Concat(characters.Take(72)) + "…" : excerpt;
        }
    }

    // :66-99. nonisolated static. A sent message read back into the quotes it LED with and the words after them, the
    // reverse of ComposerDraft.outgoingText: each "> " block up to a blank line that no further ">" line follows. A
    // leading slash command stays with the words; a message with no leading quote comes back whole. Transcribed line
    // for line (PR-CHAT-078's windows_path: character for character): an earlier port lifted ">" runs from anywhere in
    // the message, split "\r\n" apart and trimmed every newline character before the words.
    public static (ValueList<ReplyQuote> Quotes, string Body) Peel(string text)
    {
        var lines = SwiftString.Split(text, "\n", omittingEmptySubsequences: false).ToList(); // :72
        string? command = null; // :73
        if (lines.Count > 0 && SwiftString.HasPrefix(lines[0], "/") && lines.Count > 2 && lines[1].Length == 0
            && SwiftString.HasPrefix(lines[2], ">")) // :74
        {
            command = lines[0]; // :75
            lines.RemoveRange(0, 2); // :76
        }
        var quotes = new List<ReplyQuote>(); // :78
        var index = 0; // :79
        while (index < lines.Count && SwiftString.HasPrefix(lines[index], ">")) // :80
        {
            var block = new List<string>(); // :81
            while (index < lines.Count && SwiftString.HasPrefix(lines[index], ">")) // :82
            {
                var line = SwiftString.DropFirst(lines[index]); // :83
                if (SwiftString.HasPrefix(line, " ")) line = SwiftString.DropFirst(line); // :84
                block.Add(line); // :85
                index += 1; // :86
            }
            quotes.Add(new ReplyQuote { Text = string.Join("\n", block) }); // :88
            // :89-92 one blank line separates a block from the next; a blank line before the words stays for the trim.
            if (index + 1 < lines.Count && lines[index].Length == 0 && SwiftString.HasPrefix(lines[index + 1], ">"))
                index += 1;
        }
        if (quotes.Count == 0) return (ValueList<ReplyQuote>.Empty, text); // :94
        var body = string.Join("\n", lines.Skip(index)); // :95
        while (SwiftString.HasPrefix(body, "\n")) body = SwiftString.DropFirst(body); // :96
        if (command is not null) body = body.Length == 0 ? command : command + " " + body; // :97
        return (new ValueList<ReplyQuote>(quotes), body); // :98
    }
}

// :104-108 (R-VAL-1)
public sealed record DraftCommand
{
    public required string Name { get; init; }    // :105
    public required string Detail { get; init; }  // :106
    public required bool IsBuiltIn { get; init; } // :107
}

// :110-136 (R-VAL-1)
public sealed record ComposerDraft
{
    public string Text { get; init; } = "";                                         // :111
    public ValueList<Attachment> Attachments { get; init; } = ValueList<Attachment>.Empty; // :112
    public ValueList<ReplyQuote> Quotes { get; init; } = ValueList<ReplyQuote>.Empty;      // :113
    public DraftCommand? Command { get; init; } = null;                             // :114

    // :116-118
    public bool IsEmpty =>
        Attachments.Count == 0 && Command is null
            && !SwiftString.Characters(Text).Any(character => !SwiftCharacter.IsWhitespace(character)); // ThreadRuntime.swift:116-118

    // :122-135
    public string OutgoingText =>
        BuildOutgoingText(); // ThreadRuntime.swift:122-135

    // ThreadRuntime.swift:122-135
    private string BuildOutgoingText()
    {
        var body = Text;
        if (Quotes.Count > 0)
        {
            var blocks = Quotes.Select(quote => string.Join("\n", quote.Text.Split('\n')
                .Select(line => line.Length == 0 ? ">" : "> " + line)));
            body = string.Join("\n\n", blocks) + "\n\n" + Text;
        }

        if (Command is not { } command) return body;
        var head = "/" + command.Name;
        if (FoundationText.Trim(body, includeNewlines: true).Length == 0) return head;
        return Quotes.Count == 0 ? head + " " + body : head + "\n\n" + body;
    }
}

// :141-157. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed class PromptEdit : MainActorModel
{
    private string text;
    private ValueList<Attachment> attachments;

    public Guid Id { get; } // :144

    public string Text { get => Read(text); set => Set(ref text, value); } // :145

    public ValueList<Attachment> Attachments { get => Read(attachments); set => Set(ref attachments, value); } // :146

    // :148-152. R-OBS-1: the initializer assigns the backing fields.
    public PromptEdit(Guid id, string text, ValueList<Attachment> attachments)
    {
        Id = id;
        this.text = text;
        this.attachments = attachments;
    }

    // :154-156. UI-CHAT-017: an edit is empty when it has no attachments and no non-whitespace character.
    public bool IsEmpty =>
        FoundationText.Trim(Text, includeNewlines: true).Length == 0 && Attachments.Count == 0;
}

// :159-163
public enum RuntimePhase
{
    Idle,     // :160
    Starting, // :161
    Running,  // :162
}

// :165-3661 and the rehearsal extension :3696-3758. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed partial class ThreadRuntime : MainActorModel
{
    // :354-357
    private sealed record HydraBatch
    {
        public required ValueSet<Guid> Pending { get; init; }                          // :355
        public ValueList<HydraReport> Reports { get; init; } = ValueList<HydraReport>.Empty; // :356
    }

    // PORT-ONLY: OD-0114 / ED-404, ED-410. The identity the port adds to a launch lives in a base so the Swift signature (below) keeps its nine ordered fields.
    private abstract record SessionSignatureAccount
    {
        public Guid? AccountId { get; init; } // process identity
        public string? LocalServerId { get; init; } // changing server restarts the session
        public long? LocalCatalogRevision { get; init; } // refreshed capabilities/models replace the bound snapshot
    }

    // :510-526
    private sealed record SessionSignature : SessionSignatureAccount
    {
        public required ProviderKind Provider { get; init; }          // :511
        public required string Directory { get; init; }               // :512
        public RuntimeMode? LaunchRuntimeMode { get; init; }          // :513
        public string? LaunchEffort { get; init; }                    // :514
        public bool? LaunchFast { get; init; }                        // :515
        public string? LaunchModel { get; init; }                     // :518
        public InteractionMode? LaunchInteraction { get; init; }      // :519
        public HydraLaunch? Hydra { get; init; }                      // :522
        public required bool AntiSlopEnabled { get; init; }           // :525
    }

    // :528-533
    public enum DeltaKind
    {
        Message,
        Reasoning,
        ToolOutput,
        Plan,
    }

    // :535-538
    private sealed record PendingDelta
    {
        public required DeltaKind Kind { get; init; } // :536
        public required string Text { get; init; }   // :537
    }

    // :799-802
    private sealed record PendingSend
    {
        public required string Text { get; init; }                        // :800
        public required ValueList<Attachment> Attachments { get; init; }  // :801
    }

    // :1791-1796. `Result<RevertPreview, Error>?` is a finished Task (see the header).
    private sealed record RevertPreviewEntry
    {
        public required long Revision { get; init; }                     // :1792
        public required ContinuousInstant StartedAt { get; init; }        // :1793
        public required TaskHandle<RevertPreview> Task { get; init; }     // :1794
        public Task<RevertPreview>? Result { get; init; }                 // :1795
    }

    // :2748-2755
    private enum DelegationOutcome
    {
        None,        // :2750
        HeadsOut,    // :2752
        TurnStarted, // :2754
    }

    // :3141-3177. @MainActor final class, not observable: a plain class.
    public sealed class StreamLoad
    {
        // :3148
        public static StreamLoad Shared { get; } = new();

        // :3150
        private const long HeadFlushBudget = 70;
        private static readonly TimeSpan Stale = TimeSpan.FromSeconds(2); // :3151
        private readonly Dictionary<ThreadRuntime, ContinuousInstant> lastSeen = new(); // :3152

        // :3154-3156
        public void NoteStreaming(ThreadRuntime runtime) => lastSeen[runtime] = SwiftClock.Now;

        // :3158-3160
        public void Forget(ThreadRuntime runtime) => lastSeen.Remove(runtime);

        // :3163-3167
        public long StreamingCount
        {
            get
            {
                var now = SwiftClock.Now;
                foreach (var stale in lastSeen.Where(pair => now - pair.Value >= Stale).Select(pair => pair.Key).ToArray())
                    lastSeen.Remove(stale);
                return lastSeen.Count;
            }
        }

        // :3170-3172
        public long HeadFlushDelayMilliseconds => Math.Max(45, StreamingCount * HeadFlushBudget);

        // :3174-3176
        public TimeSpan HeadFlushWindow => TimeSpan.FromMilliseconds(HeadFlushDelayMilliseconds);
    }

    private ValueList<TimelineEntry> entries = ValueList<TimelineEntry>.Empty;
    private ValueList<TurnRecord> turns = ValueList<TurnRecord>.Empty;
    private ContextUsage? usage;
    private ThreadContinuation? continuation;
    private RuntimePhase phase = RuntimePhase.Idle;
    private bool isReverting = false;
    private ValueList<ApprovalRequest> approvals = ValueList<ApprovalRequest>.Empty;
    private ValueList<QuestionRequest> questions = ValueList<QuestionRequest>.Empty;
    private DateTimeOffset? turnStartedAt;
    private long diffRevision = 0;
    private long saveRevision;
    private bool isLoadingHistory = false;
    private long lastSavedRevision;
    private ValueList<FollowUpPrompt> followUps = ValueList<FollowUpPrompt>.Empty;
    // PORT-ONLY: SK-12 / DL-13. UI unit SK-14 binds the card; the host supplies the checked installer.
    public Marrow.App.Port.Skills.SkillsOfferFlow? SkillsOffers { get; private set; }
    private readonly HashSet<Guid> skillsFollowUps = [];
    private readonly AsyncLocal<bool> skillsContinuationTurn = new();
    private readonly AsyncLocal<bool> ignisPingTurn = new(); // PORT-ONLY: Ignis (ED-NEW-ignis-runtime): a hidden keep-warm ping (Port/Ignis/Runtime/ThreadRuntimeIgnis.cs)
    internal void AttachSkillsOffers(Marrow.App.Port.Skills.SkillsOfferFlow flow)
    {
        if (SkillsOffers is not null) throw new InvalidOperationException("A chat already owns its skills offer flow.");
        SkillsOffers = flow;
    }
    internal long SkillsSessionEpoch => sessionEpoch;
    internal void ResolveSkillsApproval(ApprovalRequest request, bool approved, string? reason, long epoch)
    {
        if (epoch != sessionEpoch || !Approvals.Any(a => a.Id == request.Id)) return;
        var role = approved ? ApprovalRequest.Option.Role.Approve : ApprovalRequest.Option.Role.Decline;
        if (request.Options.FirstOrDefault(o => o.OptionRole == role) is not { } option) return;
        Approvals = new ValueList<ApprovalRequest>(Approvals.Where(a => a.Id != request.Id));
        session?.ResolveApproval(request.Id, option.Id, reason);
    }
    internal void QueueSkillsContinuation(string text)
    {
        var prompt = new FollowUpPrompt { Text = text };
        skillsFollowUps.Add(prompt.Id);
        FollowUps = followUps.Appending(prompt);
        saveRevision++;
        ScheduleSave();
        if (Phase == RuntimePhase.Idle) DrainFollowUps(TurnStatus.Completed);
    }

    private async Task StartSkillsTurn(string text, IReadOnlyList<Attachment> attachments)
    {
        var previous = skillsContinuationTurn.Value;
        try
        {
            skillsContinuationTurn.Value = true;
            await EnsureLoaded();
            await StartTurn(text, attachments);
        }
        finally { skillsContinuationTurn.Value = previous; }
    }

    private PendingSend? pendingSend; // :797
    private bool followUpFlushScheduled = false; // :1057
    private ValueList<HydraMergeRecord> hydraMerges = ValueList<HydraMergeRecord>.Empty;
    private PromptEdit? followUpEdit;
    private bool followUpsCollapsed = false;
    private PromptEdit? messageEdit;
    private ComposerDraft draft = new();
    private bool draftIsEmpty = true;
    private bool isTerminalVisible = false;
    private bool isTerminalResizing = false;
    private bool isDiffVisible = false;
    private PanelDockCorner subagentPanelDock = PanelDockCorner.BottomTrailing;
    private PanelDockCorner hydraPanelDock = PanelDockCorner.BottomTrailing;
    private Guid? hydraSelectedHeadID;
    private Guid? hydraPoppedHeadID;
    private PanelDockCorner hydraPoppedPanelDock = PanelDockCorner.BottomLeading;
    private ValueSet<Guid> hydraAutoPopHeld = ValueSet<Guid>.Empty;
    private ValueList<FloatingPanelID> panelStackOrder = ValueList<FloatingPanelID>.Empty;
    private ValueMap<Guid, PanelDockCorner> hydraAutoPanelDocks = ValueMap<Guid, PanelDockCorner>.Empty;
    private bool isHydraPanelHidden = false;
    private ValueMap<string, Guid> hydraToolHeads = ValueMap<string, Guid>.Empty;
    private long hydraWaitingCount = 0;
    // :274-287. These collections are ignored by observation and are consumed by the lead's Hydra runtime.
    private List<HydraReport> hydraPendingReports = [];
    private bool hydraFlushScheduled = false;
    private readonly Dictionary<Guid, HydraBatch> hydraBatches = [];
    private Guid? hydraFallbackNoted = null; // ThreadRuntime.swift:467
    private List<(HydraDelegation Delegation, Guid BatchID, DateTimeOffset QueuedAt)> hydraWaitingStorage = [];
    private List<(HydraDelegation Delegation, Guid BatchID, DateTimeOffset QueuedAt)> hydraWaiting
    {
        get => hydraWaitingStorage;
        set
        {
            hydraWaitingStorage = value;
            HydraWaitingCount = value.Count;
        }
    }
    private bool isHydraMerging = false;
    private bool isHydraMergeQueued = false; // :314. New in B3.
    private bool hasHydraMergeRetryPending = false; // :317. New in B3.
    private string? hydraMergeStage;
    private HydraMergeStage? hydraMergeStep;
    private DateTimeOffset? hydraMergeStartedAt;
    private string? hydraMergeNoteID;
    private string? hydraActivity;
    private long hydraToolCalls = 0;
    private long hydraTokens = 0;
    private long hydraEditCount = 0;
    private readonly Dictionary<string, Guid> hydraNativeHeads = new(StringComparer.Ordinal);
    private Guid? diffSelection;
    private long diffOpenRequest = 0;
    private ValueList<FileEdit> diffFocusEdits = ValueList<FileEdit>.Empty;
    private long diffFocusRevision = 0;
    private FileChangeSummary? changeStats;
    private readonly WeakReference<AppModel> appReference;
    private readonly Dictionary<string, TimelineEntry> entryIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, RevertPreviewEntry> revertPreviews = [];
    private RecentCache<Guid, Task<ValueList<DiffFile>>> turnDiffs = new(64);
    private RecentCache<string, Task<ValueList<DiffFile>>> revisionDiffs = new(4);
    private readonly Dictionary<string, Task<string>> repositoryRoots = new(StringComparer.Ordinal);
    private Task? loadTask;
    private bool isLoaded;
    private Guid? currentTurnID;
    private Guid? closingTurnID;
    private long? changeStatsRevision;
    // :472-475. New in B3. The CLI version a message was last sent again at after the CLI said it was too old
    // for the model, per provider: one resend per version. Not persisted (PR-RUNTIME-266).
    private readonly Dictionary<ProviderKind, string> cliResendVersions = [];
    private readonly HashSet<string> hydraTouched = new(SwiftStringComparer.Instance);
    private ProviderSession? session;
    // PORT-ONLY: SK-17. Per-candidate ownership also covers failed resumes and late startup completion.
    private readonly Dictionary<ProviderSession, Marrow.Services.Port.Skills.Generations.Generation> sessionSkills = [];
    private Task pendingSkillRelease = Task.CompletedTask;
    private TaskHandle? warmup;
    private string? unsentSessionID;
    private SessionSignature? sessionSignature;
    private bool antiSlopFallbackSent;
    private bool continuationSent;
    private long sessionEpoch;
    private bool isStartingSession;
    private string? todosEntryID;
    private string? currentProviderDiff;
    private string? currentProviderAnchor;
    private Guid? finishingTurnID;
    private TaskHandle? interruptWatchdog;
    private readonly Dictionary<string, string?> commandTrees = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> hydraCommandTrees = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskHandle> commandSettles = new(StringComparer.Ordinal);
    private long treeEpoch;
    private (string Tree, long Epoch)? settledTree;
    private string? latestTree;
    private Task<string?>? treeCaptures;
    private HydraDelegationBudget hydraBudget = new();
    private (Guid TurnID, string EntryID, Guid BatchID, long Sent)? hydraStream;
    private HydraDelegationStreamParser? delegationParser;
    private TaskHandle? headBudget;
    private bool headBudgetSpent;
    private bool headReportsNext;
    private readonly Dictionary<string, PendingDelta> pendingDeltas = new(SwiftStringComparer.Instance);
    private TaskHandle? flushTask; // :450
    private ContinuousInstant lastFlushAt = SwiftClock.Now - TimeSpan.FromSeconds(1); // :425
    private TaskHandle? saveTask; // :457
    private bool persistenceEnabled = true; // :458

    // ThreadRuntime.swift:414-448, 496-508, 793-797, 1799, 1941, 1960-1962.
    // App ownership is weak because AppModel owns the runtime cache.
    internal AppModel? App => appReference is not null && appReference.TryGetTarget(out var value) ? value : null;

    // :169
    public Guid ThreadID { get; }

    // :171
    public ValueList<TimelineEntry> Entries { get => Read(entries); private set => Set(ref entries, value); }

    // ThreadRuntime.swift:172-182
    public ValueList<TurnRecord> Turns
    {
        get => Read(turns);
        private set
        {
            var previous = turns;
            Set(ref turns, value, changed =>
            {
                if (currentTurnID is { } id && !turns.Any(turn => turn.Id == id)) currentTurnID = null;
                if (messageEdit is { } edit && !turns.Any(turn => turn.Id == edit.Id)) MessageEdit = null;
                if (turns.Count < previous.Count)
                {
                    var kept = turns.Select(turn => turn.Id).ToHashSet();
                    foreach (var removed in previous.Where(turn => !kept.Contains(turn.Id)))
                        turnDiffs.RemoveValue(removed.Id, out _);
                }
            });
        }
    }

    // :183
    public ContextUsage? Usage { get => Read(usage); private set => Set(ref usage, value); }

    // :187
    public ThreadContinuation? Continuation { get => Read(continuation); private set => Set(ref continuation, value); }

    // :188
    public RuntimePhase Phase { get => Read(phase); private set => Set(ref phase, value); }

    // :189
    public bool IsReverting { get => Read(isReverting); private set => Set(ref isReverting, value); }

    // :190
    public ValueList<ApprovalRequest> Approvals { get => Read(approvals); private set => Set(ref approvals, value); }

    // :191
    public ValueList<QuestionRequest> Questions { get => Read(questions); private set => Set(ref questions, value); }

    // :192
    public DateTimeOffset? TurnStartedAt { get => Read(turnStartedAt); private set => Set(ref turnStartedAt, value); }

    // ThreadRuntime.swift:196-201
    public long DiffRevision
    {
        get => Read(diffRevision);
        private set => Set(ref diffRevision, value, _ =>
        {
            revisionDiffs = new RecentCache<string, Task<ValueList<DiffFile>>>(4);
            changeStatsRevision = null;
        });
    }

    // :205
    public bool IsLoadingHistory { get => Read(isLoadingHistory); private set => Set(ref isLoadingHistory, value); }

    // :206-211
    public ValueList<FollowUpPrompt> FollowUps
    {
        get => Read(followUps);
        private set => Set(ref followUps, value, _ =>
        {
            if (followUpEdit is { } edit && !followUps.Any(prompt => prompt.Id == edit.Id))
                FollowUpEdit = null;
        }); // ThreadRuntime.swift:206-211
    }

    // :212
    public ValueList<HydraMergeRecord> HydraMerges { get => Read(hydraMerges); private set => Set(ref hydraMerges, value); }

    // :216
    public PromptEdit? FollowUpEdit { get => Read(followUpEdit); set => Set(ref followUpEdit, value); }

    // :220
    public bool FollowUpsCollapsed { get => Read(followUpsCollapsed); set => Set(ref followUpsCollapsed, value); }

    // :226. @ObservationIgnored (R-OBS-5).
    public long AutoStartedTurnToken { get; private set; } = 0;

    // :228
    public PromptEdit? MessageEdit { get => Read(messageEdit); set => Set(ref messageEdit, value); }

    // :230-235
    public ComposerDraft Draft
    {
        get => Read(draft);
        set => Set(ref draft, value, _ => DraftIsEmpty = draft.IsEmpty); // ThreadRuntime.swift:230-235
    }

    // :238
    public bool DraftIsEmpty { get => Read(draftIsEmpty); private set => Set(ref draftIsEmpty, value); }

    // :239-243
    public bool IsTerminalVisible
    {
        get => Read(isTerminalVisible);
        set => Set(ref isTerminalVisible, value, wasVisible =>
        {
            if (!wasVisible && isTerminalVisible) TerminalWantsFocus = true; // :241-243
        });
    }

    // :244. @ObservationIgnored (R-OBS-5).
    public bool TerminalWantsFocus { get; set; } = false;

    // :246
    public bool IsTerminalResizing { get => Read(isTerminalResizing); set => Set(ref isTerminalResizing, value); }

    // :247
    public bool IsDiffVisible { get => Read(isDiffVisible); set => Set(ref isDiffVisible, value); }

    // :249
    public PanelDockCorner SubagentPanelDock { get => Read(subagentPanelDock); set => Set(ref subagentPanelDock, value); }

    // :251
    public PanelDockCorner HydraPanelDock { get => Read(hydraPanelDock); set => Set(ref hydraPanelDock, value); }

    // :253
    public Guid? HydraSelectedHeadID { get => Read(hydraSelectedHeadID); set => Set(ref hydraSelectedHeadID, value); }

    // :256
    public Guid? HydraPoppedHeadID { get => Read(hydraPoppedHeadID); set => Set(ref hydraPoppedHeadID, value); }

    // :257
    public PanelDockCorner HydraPoppedPanelDock { get => Read(hydraPoppedPanelDock); set => Set(ref hydraPoppedPanelDock, value); }

    // :260
    public ValueSet<Guid> HydraAutoPopHeld { get => Read(hydraAutoPopHeld); set => Set(ref hydraAutoPopHeld, value); }

    // :264
    public ValueList<FloatingPanelID> PanelStackOrder { get => Read(panelStackOrder); set => Set(ref panelStackOrder, value); }

    // :267
    public ValueMap<Guid, PanelDockCorner> HydraAutoPanelDocks { get => Read(hydraAutoPanelDocks); set => Set(ref hydraAutoPanelDocks, value); }

    // :269
    public bool IsHydraPanelHidden { get => Read(isHydraPanelHidden); set => Set(ref isHydraPanelHidden, value); }

    // :272. Keys are tool row ids.
    public ValueMap<string, Guid> HydraToolHeads { get => Read(hydraToolHeads); private set => Set(ref hydraToolHeads, value); }

    // :283. @ObservationIgnored (R-OBS-5). The property shadows the type Marrow.Core.Models.HydraLanding in
    // expression context inside this class; type positions still name the type.
    public TaskHandle? HydraLanding { get; set; }

    // :292
    public long HydraWaitingCount { get => Read(hydraWaitingCount); private set => Set(ref hydraWaitingCount, value); }

    // :312
    public bool IsHydraMerging { get => Read(isHydraMerging); set => Set(ref isHydraMerging, value); }

    // :313-314. New in B3 (App.Runtime.ThreadRuntime lane, unit A33).
    public bool IsHydraMergeQueued { get => Read(isHydraMergeQueued); set => Set(ref isHydraMergeQueued, value); }

    // :316-317. New in B3 (App.Runtime.ThreadRuntime lane, unit A33).
    public bool HasHydraMergeRetryPending { get => Read(hasHydraMergeRetryPending); set => Set(ref hasHydraMergeRetryPending, value); }

    // :315. The property shadows the type HydraMergeStage in expression context inside this class; type positions
    // (HydraMergeStep below) still name the type.
    public string? HydraMergeStage { get => Read(hydraMergeStage); set => Set(ref hydraMergeStage, value); }

    // :318
    public HydraMergeStage? HydraMergeStep { get => Read(hydraMergeStep); set => Set(ref hydraMergeStep, value); }

    // :320
    public DateTimeOffset? HydraMergeStartedAt { get => Read(hydraMergeStartedAt); set => Set(ref hydraMergeStartedAt, value); }

    // :325
    public string? HydraMergeNoteID { get => Read(hydraMergeNoteID); set => Set(ref hydraMergeNoteID, value); }

    // :331
    public string? HydraActivity { get => Read(hydraActivity); set => Set(ref hydraActivity, value); }

    // :332
    public long HydraToolCalls { get => Read(hydraToolCalls); set => Set(ref hydraToolCalls, value); }

    // :333
    public long HydraTokens { get => Read(hydraTokens); set => Set(ref hydraTokens, value); }

    // :339. @ObservationIgnored (R-OBS-5).
    public DateTimeOffset? HydraLastEventAt { get; set; }

    // :340
    public long HydraEditCount { get => Read(hydraEditCount); set => Set(ref hydraEditCount, value); }

    // :342. TimeInterval, seconds.
    public double HydraIdleSeconds =>
        HydraLastEventAt is { } lastEventAt
            ? SwiftDate.ReferenceSeconds(SwiftDate.Now) - SwiftDate.ReferenceSeconds(lastEventAt)
            : 0d;

    // Deleted in B3 (PR-RUNTIME-253 b3_note): hydraLiveInfo had no caller even at B2 and upstream removes it
    // entirely at ThreadRuntime.swift (formerly :345-352).

    // :358
    public Guid? DiffSelection { get => Read(diffSelection); set => Set(ref diffSelection, value); }

    // :362. @ObservationIgnored (R-OBS-5).
    public WeakView? DiffAnchor { get; set; }

    // :366. @ObservationIgnored (R-OBS-5).
    public PreviewPanelEdge DiffAnchorEdge { get; set; } = PreviewPanelEdge.MaxY;

    // :369
    public long DiffOpenRequest { get => Read(diffOpenRequest); private set => Set(ref diffOpenRequest, value); }

    // :374
    public ValueList<FileEdit> DiffFocusEdits { get => Read(diffFocusEdits); private set => Set(ref diffFocusEdits, value); }

    // :377
    public long DiffFocusRevision { get => Read(diffFocusRevision); private set => Set(ref diffFocusRevision, value); }

    // :383-390. Swift: showDiff(on:edge:turn:focusEdits:). `turn` has no default and follows `edge`, so it moves
    // before it (ED-095).
    public void ShowDiff(Control anchor, Guid? turn, PreviewPanelEdge edge = PreviewPanelEdge.MaxY, IReadOnlyList<FileEdit>? focusEdits = null)
    {
        DiffAnchor = new WeakView(anchor); // :384
        DiffAnchorEdge = edge; // :385
        if (DiffSelection != turn) DiffSelection = turn; // :386
        SetDiffFocus(focusEdits ?? Array.Empty<FileEdit>()); // :387
        if (!IsDiffVisible) IsDiffVisible = true; // :388
        DiffOpenRequest++; // :389
    }

    // :394-396
    public void ClearDiffFocus() => SetDiffFocus(Array.Empty<FileEdit>());

    // :398-403
    private void SetDiffFocus(IReadOnlyList<FileEdit> edits)
    {
        var focus = new ValueList<FileEdit>(edits.Where(edit => edit.Path.Length != 0)); // :399
        if (DiffFocusEdits.SequenceEqual(focus)) return; // :400
        DiffFocusEdits = focus; // :401
        DiffFocusRevision++; // :402
    }

    // :408-412
    public void ToggleDiff()
    {
        ClearDiffFocus(); // :409
        if (!IsDiffVisible) DiffAnchor = null; // :410
        IsDiffVisible = !IsDiffVisible; // :411
    }

    // :505. @ObservationIgnored private(set) (R-OBS-5). Paths, external text (R-VAL-2).
    public ValueSet<string> HydraCurrentTurnTouchedPaths { get; private set; } = new([], SwiftStringComparer.Instance);

    // ThreadRuntime.swift:540-555
    public ThreadRuntime(Guid threadID, AppModel app)
    {
        ThreadID = threadID;
        appReference = new WeakReference<AppModel>(app);
        if (DocumentPrefetch.Shared.Take(threadID) is { } prefetched)
        {
            Install(prefetched);
            isLoaded = true;
        }
        else
        {
            IsLoadingHistory = true;
            loadTask = LoadDocument();
        }
    }

    // Source: DroppyCode/App/Runtime/ThreadRuntime.swift (B3 52bc792), :556-565.
    // :556-560. Sweep C verifies stored plus live token totals and the Droppy-only gate.
    private void Install(ThreadDocument document)
    {
        // :556-565. Stored rows precede anything that arrived while disk was read.
        Turns = new ValueList<TurnRecord>(document.Turns.Concat(Turns));
        if (Thread?.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
            HydraTokens = Turns.SelectMany(turn => turn.TokenSpends ?? ValueList<TokenSpend>.Empty).Sum(spend => spend.TotalTokens); // :557-560
        Usage = Usage ?? document.Usage; // :562
        Continuation = document.Continuation ?? Continuation; // :563
        FollowUps = new ValueList<FollowUpPrompt>(document.FollowUps.Where(prompt => !prompt.IsEmpty).Concat(FollowUps)); // :564
        HydraMerges = new ValueList<HydraMergeRecord>(document.HydraMerges.Concat(HydraMerges)); // :565

        // :566-580. Old histories can contain blank reasoning and streaming flags from a quit.
        var loaded = document.Items.Where(item => !item.IsEmptyReasoning)
            .Select(item => new TimelineEntry(item.CleanedOfEmDashes)).ToArray();
        foreach (var entry in loaded)
        {
            entryIndex[entry.Id] = entry;
            EndStreaming(entry, recordSave: false);
            if (entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool && tool.Value.ToolCallStatus == ToolCall.Status.Running)
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Tool(tool.Value.Finish(ToolCall.Status.Failed)) }; // :575-578
        }
        Entries = new ValueList<TimelineEntry>(loaded.Concat(Entries)); // :580

        // :581-587. A sent block was already dispatched before a quit interrupted the hold.
        foreach (var entry in loaded)
        {
            if (entry.Item.TimelineItemContent is not TimelineItem.Content.Assistant assistant
                || !HydraPrompts.HasSentDelegationBlock(assistant.Value.Text)) continue;
            var text = HydraPrompts.WithoutDelegationBlock(assistant.Value.Text);
            if (text.Length == 0) text = "Sent out heads.";
            entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { Text = text }) };
        }

        // :588-613. A process that quit mid-turn gets an end row so the history folds normally.
        for (var index = 0; index < Turns.Count; index++)
            if (Turns[index].Status == TurnStatus.Running)
                Turns = Turns.SetItem(index, Turns[index] with { Status = TurnStatus.Interrupted });

        var marked = Entries.Where(entry => entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd)
            .Select(entry => entry.TurnID).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        foreach (var storedTurn in document.Turns.Reverse())
        {
            var index = -1;
            for (var candidate = 0; candidate < Turns.Count; candidate++)
                if (Turns[candidate].Id == storedTurn.Id) { index = candidate; break; }
            if (index < 0 || marked.Contains(storedTurn.Id)) continue;
            var lastIndex = -1;
            for (var candidate = Entries.Count - 1; candidate >= 0; candidate--)
                if (Entries[candidate].TurnID == storedTurn.Id) { lastIndex = candidate; break; }
            if (lastIndex < 0) continue;

            var turn = Turns[index];
            var endedAt = turn.CompletedAt ?? Entries[lastIndex].Item.Date;
            if (turn.CompletedAt is null) Turns = Turns.SetItem(index, turn with { CompletedAt = endedAt });
            turn = Turns[index];
            var summary = new TurnSummary
            {
                TurnID = turn.Id,
                Status = turn.Status,
                Duration = Math.Max(0, (endedAt - turn.StartedAt).TotalSeconds),
                FilesChanged = turn.TouchedPaths?.Count ?? 0,
                Additions = 0,
                Deletions = 0,
                Changes = null,
            };
            var end = new TimelineEntry(new TimelineItem(storedTurn.Id, new TimelineItem.Content.TurnEnd(summary), date: endedAt));
            Entries = new ValueList<TimelineEntry>(Entries.Take(lastIndex + 1)
                .Append(end).Concat(Entries.Skip(lastIndex + 1)));
            entryIndex[end.Id] = end;
            marked.Add(storedTurn.Id);
        }

        // :617-630. A clean normalization is already on disk; also recover paths from events
        // that arrived while history was loading.
        saveRevision++;
        var installed = SnapshotForPersistence();
        if (installed.Items.SequenceEqual(document.Items)
            && installed.Turns.SequenceEqual(document.Turns)
            && installed.FollowUps.SequenceEqual(document.FollowUps)
            && installed.HydraMerges.SequenceEqual(document.HydraMerges))
            lastSavedRevision = saveRevision;

        if (currentTurnID is null)
        {
            EndDelegationStream();
            HydraCurrentTurnTouchedPaths = new ValueSet<string>(Array.Empty<string>(), SwiftStringComparer.Instance);
        }
        else
        {
            foreach (var entry in Entries.Where(entry => entry.TurnID == currentTurnID))
                if (entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool)
                    RecordCurrentTurnEdits(tool.Value.Edits);
        }
    }

    // :628-629, :3407. Tool edits become repository-relative paths for Hydra accounting.
    private void RecordCurrentTurnEdits(IEnumerable<FileEdit> edits)
    {
        if (currentTurnID is null || WorkingRoot is not { } root) return;
        var touched = HydraCurrentTurnTouchedPaths.ToHashSet(SwiftStringComparer.Instance);
        foreach (var edit in edits)
            if (TouchedPaths.Relative(edit.Path, root) is { } path)
                touched.Add(path);
        HydraCurrentTurnTouchedPaths = new ValueSet<string>(touched, SwiftStringComparer.Instance);
    }

    // :3435-3440. Native-head edits are made in the lead's checkout and count for its turn.
    private void NoteHydraEdits(IEnumerable<FileEdit> edits)
    {
        if (WorkingRoot is not { } root) return;
        var paths = edits.Select(edit => TouchedPaths.Relative(edit.Path, root))
            .Where(path => path is not null).Select(path => path!).ToArray();
        if (paths.Length == 0) return;
        hydraTouched.UnionWith(paths);
        HydraCurrentTurnTouchedPaths = HydraCurrentTurnTouchedPaths.Mutating(current => current.UnionWith(paths));
    }

    // ThreadRuntime.swift:3554-3558
    private void UpdateTurn(Guid id, Func<TurnRecord, TurnRecord> change)
    {
        var index = -1;
        for (var candidate = 0; candidate < turns.Count; candidate++)
            if (turns[candidate].Id == id)
            {
                index = candidate;
                break;
            }
        if (index < 0) return;
        Turns = turns.SetItem(index, change(turns[index]));
        saveRevision++;
    }

    // :633-643
    private void FinishLoad(ThreadDocument document)
    {
        if (!IsLoadingHistory) return;
        Install(document);
        isLoaded = true;
        IsLoadingHistory = false;
        loadTask = null;
    }

    public Task EnsureLoaded() => isLoaded ? Task.CompletedTask : loadTask ??= LoadDocument();

    private async Task LoadDocument()
    {
        var document = await TaskHandle.Detached(() => Task.FromResult(
            Storage.DecodeDocument(ThreadID) ?? new ThreadDocument(ThreadID))).Value;
        await MainActor.Run(() => FinishLoad(document));
    }

    // :646-655. Swift: installContinuation(_:).
    public async Task<bool> InstallContinuation(ThreadContinuation value)
    {
        await EnsureLoaded(); // :647
        if (Thread is not { } thread || Turns.Count != 0 || Entries.Count != 0 || thread.ProviderSessionID is not null)
            return false; // :648
        ReleaseSession(stop: true); // :650
        Continuation = value; // :651
        saveRevision++; // :652
        SaveNow(); // :653
        return true; // :654
    }

    // :657
    public bool IsRunning => Phase != RuntimePhase.Idle;

    // :659
    public ChatThread? Thread => App?.Thread(ThreadID);

    // :665-667
    public bool HasWorkingHeads =>
        (App?.RunningHydraHeads(ThreadID) ?? 0) > 0;

    // :672-680
    public bool HasWorkingHelpers
    {
        get
        {
            var app = App;
            return app is not null && app.Helpers(ThreadID)
                .Any(helper => app.ExistingRuntime(helper.Id)?.IsRunning == true);
        }
    }

    // :685-688
    public bool HasWorkingNativeHeads =>
        App?.HydraHeads(ThreadID).Any(head => head.Hydra is
            { HydraHeadInfoKind: HydraHeadInfo.Kind.Native, HydraHeadInfoStatus: HydraHeadInfo.Status.Running }) == true;

    // :698-722
    public void TakeBackRunningTurn()
    {
        if (Draft.IsEmpty && !HasWorkingHeads && currentTurnID is { } turnID
            && turns.FirstOrDefault(turn => turn.Id == turnID) is { } turn
            && turn.UserItemID is { } userItemID
            && entryIndex.TryGetValue(userItemID, out var entry)
            && entry.Item.TimelineItemContent is TimelineItem.Content.User user
            && !user.Value.IsFromHydra)
        {
            var message = user.Value;
            Interrupt(); // :705
            _ = MainActor.Spawn(async () =>
            {
                for (var attempt = 0; attempt < 200 && Phase != RuntimePhase.Idle; attempt++) // :707
                    await Task.Delay(TimeSpan.FromMilliseconds(50)); // :708

                if (Phase != RuntimePhase.Idle || !Draft.IsEmpty || !turns.Any(candidate => candidate.Id == turnID)) return; // :709-710
                try
                {
                    _ = await Revert(turnID, restoreFiles: false); // :714
                }
                catch (Exception error)
                {
                    AppendNotice(Notice.Level.Error, "The stopped message was kept: " + SwiftErrors.LocalizedDescription(error)); // :716
                    return;
                }

                Draft = Draft with { Text = message.Text, Attachments = message.Attachments }; // :721-722
            });
            return;
        }

        Interrupt(); // :701
    }

    // :724-729
    public ValueList<string> SentPrompts => new(Entries.Select(entry => entry.Item.TimelineItemContent)
        .OfType<TimelineItem.Content.User>()
        .Where(user => !user.Value.IsFromHydra && !user.Value.IsHydraBrief)
        .Select(user => user.Value.Text));

    // :731-736
    // Unlike recall history, Swift's presence check includes a head's brief (:731-736).
    public bool HasSentPrompts => Entries.Any(entry =>
        entry.Item.TimelineItemContent is TimelineItem.Content.User user && !user.Value.IsFromHydra);

    // :738-740
    public ApprovalRequest? PendingPlanApproval =>
        Approvals.FirstOrDefault(request => request.RequestKind == ApprovalRequest.Kind.Plan);

    // MARK: - Sending

    // :744-756
    public void Send()
    {
        if (IsReverting || Draft.IsEmpty || Phase != RuntimePhase.Idle) return;
        var text = FoundationText.Trim(Draft.OutgoingText, includeNewlines: true);
        var attachments = Draft.Attachments;
        if (HandleLocalCommand(text))
        {
            Draft = new ComposerDraft();
            return;
        }

        Draft = new ComposerDraft();
        if (DispatchSentHead(text, attachments)) return; // ThreadRuntime.swift:753
        _ = MainActor.Spawn(() => StartTurn(text, attachments));
    }

    // :758-765. Swift: sendHydraBrief(_:attachments:).
    /// <summary>
    /// PORT-ONLY (Dextra AS-5, ED-NEW-dextra-as5-6): the mission runner's message to one of its chats (the brief, a
    /// redirect, the reviewer's findings). False when the chat cannot take it now: the text is blank or a turn runs.
    /// </summary>
    public bool SendMissionMessage(string text)
    {
        if (FoundationText.Trim(text, includeNewlines: true).Length == 0 || phase != RuntimePhase.Idle || IsReverting) return false;
        _ = MainActor.Spawn(async () =>
        {
            await EnsureLoaded();
            await StartTurn(text, []);
        });
        return true;
    }

    public void SendHydraBrief(string text, IReadOnlyList<Attachment> attachments)
    {
        // ThreadRuntime.swift:758-765: reject empty/busy sends before touching the composer or head clock.
        if ((FoundationText.Trim(text, includeNewlines: true).Length == 0 && attachments.Count == 0)
            || phase != RuntimePhase.Idle) return;
        Draft = new ComposerDraft();
        HydraLastEventAt = SwiftDate.Now;
        _ = MainActor.Spawn(async () =>
        {
            await EnsureLoaded();
            await StartTurn(text, attachments, hydraBrief: true);
        });
    }

    // ThreadRuntime.swift:1158-1167
    private bool HandleLocalCommand(string text)
    {
        text = FoundationText.Trim(text, includeNewlines: true);
        if (text == "/plan" && Thread is { } thread)
        {
            App?.Providers.RecordCommand("plan", thread.Provider);
            App?.UpdateThread(ThreadID, current => current with
            {
                InteractionMode = current.InteractionMode == InteractionMode.Plan
                    ? InteractionMode.Build
                    : InteractionMode.Plan,
            });
            return true;
        }

        if (text == "/compact" && Thread is { Provider.SupportsCompact: true } compactThread)
        {
            App?.Providers.RecordCommand("compact", compactThread.Provider);
            Compact();
            return true;
        }

        return false;
    }

    // MARK: - Follow-up queue

    // :768-773 While a turn runs, and while heads are out with the lead waiting on them. Reads neither the draft nor a
    // revert.
    public bool CanQueue => IsRunning || HasWorkingHeads; // ThreadRuntime.swift:771-773

    // :775-788 With nothing to queue behind, the draft simply goes out: the queue chord is never a dead key.
    public void QueueDraftAsFollowUp() // ThreadRuntime.swift:780-788
    {
        if (IsReverting || Draft.IsEmpty) return; // :781
        if (!CanQueue) // :782-785
        {
            Send();
            return;
        }
        EnqueueFollowUp(Draft.OutgoingText, Draft.Attachments); // :786
        Draft = new ComposerDraft(); // :787
    }

    // :809-830
    public void InterruptAndSend() // ThreadRuntime.swift:809-830
    {
        if (IsReverting || Draft.IsEmpty || Phase == RuntimePhase.Idle) return;

        if (DispatchSentHead(Draft.OutgoingText, Draft.Attachments))
        {
            Draft = new ComposerDraft();
            return;
        }

        if (HasWorkingHeads)
        {
            EnqueueFollowUp(Draft.OutgoingText, Draft.Attachments);
            Draft = new ComposerDraft();
            return;
        }

        pendingSend = new PendingSend
        {
            Text = Draft.OutgoingText,
            Attachments = draft.Attachments,
        };
        Draft = new ComposerDraft();
        Interrupt();
    }

    // :835-848. Swift: bundleFollowUp(_:onto:).
    public void BundleFollowUp(Guid id, Guid target) =>
        BundleFollowUpCore(id, target); // ThreadRuntime.swift:835-848

    // ThreadRuntime.swift:835-848
    private void BundleFollowUpCore(Guid id, Guid target)
    {
        if (id == target) return;
        var sourceIndex = -1;
        var targetIndex = -1;
        for (var index = 0; index < followUps.Count; index++)
        {
            if (followUps[index].Id == id) sourceIndex = index;
            if (followUps[index].Id == target) targetIndex = index;
        }
        if (sourceIndex < 0 || targetIndex < 0) return;

        var bundle = followUps[targetIndex].BundleID ?? Guid.NewGuid();
        var items = followUps.ToList();
        if (items[targetIndex].BundleID is null)
            items[targetIndex] = items[targetIndex] with { BundleID = bundle };
        var dragged = items[sourceIndex] with { BundleID = bundle };
        items.RemoveAt(sourceIndex);
        var lastBundleIndex = items.FindLastIndex(prompt => prompt.BundleID == bundle);
        var insertAt = (lastBundleIndex >= 0 ? lastBundleIndex : items.Count - 1) + 1;
        items.Insert(insertAt, dragged);
        FollowUps = new ValueList<FollowUpPrompt>(items);
        // :844-845 saveRevision += 1; scheduleSave(). bundleFollowUp does not call tidyBundles(): a prompt whose partner
        // was dragged into another bundle keeps its bundle until an operation that tidies runs (the port used to tidy here).
        saveRevision++;
        ScheduleSave();
    }

    // :852-858
    public void UnbundleFollowUp(Guid id) =>
        UnbundleFollowUpCore(id); // ThreadRuntime.swift:852-858

    // ThreadRuntime.swift:852-858
    private void UnbundleFollowUpCore(Guid id)
    {
        var index = IndexOfFollowUp(id);
        if (index < 0 || followUps[index].BundleID is null) return;
        FollowUps = followUps.SetItem(index, followUps[index] with { BundleID = null });
        TidyBundles();
        saveRevision++;
        ScheduleSave();
    }

    // ThreadRuntime.swift:862-873
    private void UnbundleIfSeparated(Guid id)
    {
        var index = IndexOfFollowUp(id);
        if (index < 0 || followUps[index].BundleID is not { } bundle) return;
        var prevShares = index > 0 && followUps[index - 1].BundleID == bundle;
        var nextShares = index + 1 < followUps.Count && followUps[index + 1].BundleID == bundle;
        if (prevShares || nextShares) return;

        var items = followUps.ToList();
        items[index] = items[index] with { BundleID = null };
        var remaining = items.Where(prompt => prompt.BundleID == bundle).ToArray();
        if (remaining.Length == 1)
        {
            var loneIndex = items.FindIndex(prompt => prompt.Id == remaining[0].Id);
            items[loneIndex] = items[loneIndex] with { BundleID = null };
        }
        FollowUps = new ValueList<FollowUpPrompt>(items);
    }

    // ThreadRuntime.swift:876-886
    private void TidyBundles()
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var prompt in followUps)
            if (prompt.BundleID is { } bundle)
                counts[bundle] = counts.GetValueOrDefault(bundle) + 1;

        var items = followUps.ToList();
        for (var index = 0; index < items.Count; index++)
            if (items[index].BundleID is { } bundle && counts[bundle] == 1)
                items[index] = items[index] with { BundleID = null };
        FollowUps = new ValueList<FollowUpPrompt>(items);
    }

    // ThreadRuntime.swift:889-900
    private FollowUpPrompt MergedBundle(int index)
    {
        var first = followUps[index];
        if (first.BundleID is not { } bundle) return first;
        var end = index;
        while (end + 1 < followUps.Count && followUps[end + 1].BundleID == bundle) end++;
        var members = followUps.Skip(index).Take(end - index + 1).ToArray();
        return first with
        {
            Text = BundleText(members),
            Attachments = new ValueList<Attachment>(members.SelectMany(member => member.Attachments)),
            BundleID = null,
        };
    }

    // ThreadRuntime.swift:934-945
    private FollowUpPrompt TakeBundle(int index)
    {
        var merged = MergedBundle(index);
        if (followUps[index].BundleID is { } bundle)
        {
            var end = index;
            while (end + 1 < followUps.Count && followUps[end + 1].BundleID == bundle) end++;
            FollowUps = followUps.Mutating(items => items.RemoveRange(index, end - index + 1));
        }
        else
        {
            FollowUps = followUps.Mutating(items => items.RemoveAt(index));
        }
        return merged;
    }

    // ThreadRuntime.swift:853, 1087, 1101
    private int IndexOfFollowUp(Guid id)
    {
        for (var index = 0; index < followUps.Count; index++)
            if (followUps[index].Id == id) return index;
        return -1;
    }

    // :907-930. Swift: bundleText(_:).
    public static string BundleText(IReadOnlyList<FollowUpPrompt> members) =>
        BundleTextCore(members); // ThreadRuntime.swift:907-930

    // ThreadRuntime.swift:907-930
    private static string BundleTextCore(IReadOnlyList<FollowUpPrompt> members)
    {
        var texts = members.Select(member => FoundationText.Trim(member.Text, includeNewlines: true)).ToArray();
        if (members.Count <= 1 || !members.Any(member => member.Attachments.Count > 0))
            return string.Join("\n\n", texts.Where(text => text.Length > 0));

        var imageNumber = 0;
        var sections = new List<string>();
        for (var offset = 0; offset < members.Count; offset++)
        {
            var number = offset + 1;
            var lines = new List<string> { $"Message {number}:" };
            if (texts[offset].Length > 0) lines.Add(texts[offset]);
            var named = new List<string>();
            foreach (var attachment in members[offset].Attachments)
            {
                if (!attachment.IsImage)
                    named.Add("file " + attachment.Path);
                else
                {
                    imageNumber++;
                    named.Add($"image {imageNumber} ({attachment.Name})");
                }
            }
            if (named.Count > 0)
                lines.Add($"Attached to message {number}: {string.Join(", ", named)}.");
            sections.Add(string.Join("\n", lines));
        }
        var header = $"{members.Count} messages sent together. The pictures and files a message came with are named under it, the pictures numbered in the order they are attached.";
        return string.Join("\n\n", new[] { header }.Concat(sections));
    }

    // :954-990
    public void SendFollowUpNow(Guid id) // ThreadRuntime.swift:954-990
    {
        if (isReverting) return;
        if (phase != RuntimePhase.Idle && skillsFollowUps.Contains(id)) return;
        var index = IndexOfFollowUp(id);
        if (index < 0) return;

        var start = index;
        if (followUps[index].BundleID is { } bundle)
            while (start > 0 && followUps[start - 1].BundleID == bundle) start--;

        var prompt = TakeBundle(start);
        saveRevision++;
        ScheduleSave();
        if (prompt.IsEmpty) return;

        if (phase == RuntimePhase.Idle)
        {
            if (HandleLocalCommand(FoundationText.Trim(prompt.Text, includeNewlines: true))) return;
            MainActor.Spawn(async () =>
            {
                await EnsureLoaded();
                await StartTurn(prompt.Text, prompt.Attachments);
            });
        }
        else if (pendingSend is null)
        {
            // Native heads share the lead session, so sending now must not stop them.
            if (HasWorkingNativeHeads)
            {
                if (!DispatchQueuedHead(prompt))
                {
                    FollowUps = followUps.Mutating(items => items.Insert(0, prompt));
                    saveRevision++;
                    ScheduleSave();
                }
                return;
            }

            pendingSend = new PendingSend { Text = prompt.Text, Attachments = prompt.Attachments };
            Interrupt();
        }
        else
        {
            // A Return key already owns the next turn; this prompt follows it.
            FollowUps = followUps.Mutating(items => items.Insert(0, prompt));
            saveRevision++;
            ScheduleSave();
        }
    }

    // ThreadRuntime.swift:1013-1016.
    private bool DispatchQueuedHead(FollowUpPrompt prompt)
    {
        if (skillsFollowUps.Contains(prompt.Id)) return false;
        if (appReference is null || !appReference.TryGetTarget(out var app)
            || (!app.Settings.HydraQueueHeads && !app.Settings.HydraAlwaysHeads)) return false;
        return DispatchHead(prompt, HydraHeadInfo.Origin.Queued);
    }

    // ThreadRuntime.swift:1021-1026.
    private bool DispatchSentHead(string text, IReadOnlyList<Attachment> attachments)
    {
        if (appReference is null || !appReference.TryGetTarget(out var app) || !app.Settings.HydraAlwaysHeads)
            return false;
        var prompt = new FollowUpPrompt
        {
            Text = FoundationText.Trim(text, includeNewlines: true),
            Attachments = new ValueList<Attachment>(attachments),
        };
        if (prompt.IsEmpty || prompt.Text is "/plan" or "/compact") return false;
        return DispatchHead(prompt, HydraHeadInfo.Origin.Sent);
    }

    // ThreadRuntime.swift:1028-1040.
    private bool DispatchHead(FollowUpPrompt prompt, HydraHeadInfo.Origin origin)
    {
        if (appReference is null || !appReference.TryGetTarget(out var app)) return false; // :1029
        if (app.Thread(ThreadID) is not { } thread || thread.IsArchived // :1029
            || app.HydraLaunch(thread) is not { } launch // :1029
            || !launch.HasRoom(app.RunningDroppyHeads(ThreadID))) return false; // :1030
        var task = TextCleanup.SingleLine(prompt.Text, 60); // :1031 the head's title; the brief carries the full text
        // :1032 read now, so the head sees the chat as it stands at dispatch, not as the closure later runs.
        var (lastUserPrompt, lastReply, touchedPaths) = app.HydraChatContext(ThreadID);
        var context = new HydraPrompts.ChatContext { LastUserPrompt = lastUserPrompt, LastReply = lastReply, TouchedPaths = touchedPaths };
        var leadIsWorking = phase != RuntimePhase.Idle; // :1033 captured here, not re-read inside the closure
        var text = prompt.Text; // :1034
        // :1035-1037 no batch, no preferred index, no project, no profile: such a head is never part of a batch.
        return app.SpawnDroppyHead(ThreadID, task, origin, prompt.Attachments, null, null, null, null,
            (persona, workplace) => HydraPrompts.QueuedHeadPrompt(persona, text, context, workplace, leadIsWorking)) is not null;
    }

    // ThreadRuntime.swift:1042-1053.
    private void DispatchQueuedFollowUps()
    {
        if (phase == RuntimePhase.Idle) return;
        while (followUps.Count > 0)
        {
            var merged = MergedBundle(0);
            if (!DispatchQueuedHead(merged)) break;
            _ = TakeBundle(0);
            saveRevision += 1;
            ScheduleSave();
        }
    }

    // :991-1008. Swift: enqueueFollowUp(text:attachments:).
    public void EnqueueFollowUp(string text, IReadOnlyList<Attachment> attachments) =>
        EnqueueFollowUpCore(text, attachments); // ThreadRuntime.swift:991-1008

    // ThreadRuntime.swift:991-1008
    private void EnqueueFollowUpCore(string text, IReadOnlyList<Attachment> attachments)
    {
        var prompt = new FollowUpPrompt
        {
            Text = FoundationText.Trim(text, includeNewlines: true),
            Attachments = new ValueList<Attachment>(attachments),
        };
        if (prompt.IsEmpty) return;
        if (phase != RuntimePhase.Idle && DispatchQueuedHead(prompt)) return;
        FollowUps = followUps.Appending(prompt);
        saveRevision++;
        ScheduleSave();
        // :1003-1007 An idle lead takes the next queued message at once, even with heads still out: the queue only
        // ever waits behind a running turn, never behind an idle lead.
        FlushFollowUpsIfIdle();
    }

    // :1074-1079
    public void RemoveFollowUp(Guid id) =>
        RemoveFollowUpCore(id); // ThreadRuntime.swift:1074-1079

    // ThreadRuntime.swift:1074-1079
    private void RemoveFollowUpCore(Guid id)
    {
        FollowUps = new ValueList<FollowUpPrompt>(followUps.Where(prompt => prompt.Id != id));
        TidyBundles();
        saveRevision++;
        ScheduleSave();
    }

    // :1084-1098. Swift: moveFollowUp(_:to:placeAfter:), @discardableResult.
    public bool MoveFollowUp(Guid id, Guid target, bool placeAfter) =>
        MoveFollowUpCore(id, target, placeAfter); // ThreadRuntime.swift:1085-1098

    // ThreadRuntime.swift:1085-1098
    private bool MoveFollowUpCore(Guid id, Guid target, bool placeAfter)
    {
        var from = IndexOfFollowUp(id);
        var to = IndexOfFollowUp(target);
        if (id == target || from < 0 || to < 0) return false;
        var dest = to + (placeAfter ? 1 : 0);
        if (from < dest) dest--;
        if (from == dest) return false;

        var items = followUps.ToList();
        var prompt = items[from];
        items.RemoveAt(from);
        items.Insert(dest, prompt);
        FollowUps = new ValueList<FollowUpPrompt>(items);
        UnbundleIfSeparated(id);
        saveRevision++;
        ScheduleSave();
        return true;
    }

    // :1100-1112. Swift: updateFollowUp(_:text:attachments:).
    public void UpdateFollowUp(Guid id, string text, IReadOnlyList<Attachment> attachments) =>
        UpdateFollowUpCore(id, text, attachments); // ThreadRuntime.swift:1100-1112

    // ThreadRuntime.swift:1100-1112
    private void UpdateFollowUpCore(Guid id, string text, IReadOnlyList<Attachment> attachments)
    {
        var index = IndexOfFollowUp(id);
        if (index < 0) return;
        var trimmed = FoundationText.Trim(text, includeNewlines: true);
        if (trimmed.Length == 0 && attachments.Count == 0)
        {
            FollowUps = followUps.Mutating(items => items.RemoveAt(index));
            TidyBundles();
        }
        else
        {
            FollowUps = followUps.SetItem(index, followUps[index] with
            {
                Text = text,
                Attachments = new ValueList<Attachment>(attachments),
            });
        }
        saveRevision++;
        ScheduleSave();
    }

    // ThreadRuntime.swift:1114-1139.
    private bool DrainFollowUps(TurnStatus status)
    {
        if (Phase != RuntimePhase.Idle || FollowUps.Count == 0) return false;
        if (status != TurnStatus.Completed)
        {
            var index = followUps.ToList().FindIndex(p => skillsFollowUps.Contains(p.Id));
            if (index < 0) return false;
            var skill = followUps[index];
            FollowUps = new ValueList<FollowUpPrompt>(followUps.Where(p => p.Id != skill.Id));
            skillsFollowUps.Remove(skill.Id);
            saveRevision++;
            ScheduleSave();
            AutoStartedTurnToken++;
            _ = MainActor.Spawn(() => StartSkillsTurn(skill.Text, skill.Attachments));
            return true;
        }

        var skillContinuation = skillsFollowUps.Remove(followUps[0].Id);
        var next = TakeBundle(0);
        saveRevision++;
        while (next.IsEmpty && FollowUps.Count != 0)
        {
            next = TakeBundle(0);
            saveRevision++;
        }

        if (next.IsEmpty)
        {
            ScheduleSave();
            return false;
        }

        ScheduleSave();
        if (HandleLocalCommand(FoundationText.Trim(next.Text, includeNewlines: true)))
            return DrainFollowUps(status);

        AutoStartedTurnToken++;
        _ = MainActor.Spawn(async () =>
        {
            await EnsureLoaded();
            if (skillContinuation) await StartSkillsTurn(next.Text, next.Attachments);
            else await StartTurn(next.Text, next.Attachments);
        });
        return true;
    }

    // ThreadRuntime.swift:2464-2479.
    private bool DrainPendingSend()
    {
        if (Phase != RuntimePhase.Idle || pendingSend is not { } pending) return false;
        pendingSend = null;
        var text = FoundationText.Trim(pending.Text, includeNewlines: true);
        if (text.Length == 0 && pending.Attachments.Count == 0)
        {
            ScheduleSave();
            return false;
        }
        if (HandleLocalCommand(text)) return false;

        _ = MainActor.Spawn(async () =>
        {
            await EnsureLoaded();
            await StartTurn(pending.Text, pending.Attachments);
        });
        return true;
    }

    // :1141-1148
    public void ImplementPlan(string entryID)
    {
        if (Phase != RuntimePhase.Idle || !entryIndex.TryGetValue(entryID, out var entry)
            || entry.Item.TimelineItemContent is not TimelineItem.Content.Plan plan) return; // :1142
        entry.Item = entry.Item with
        {
            TimelineItemContent = new TimelineItem.Content.Plan(plan.Value with { ProposedPlanState = ProposedPlan.State.Accepted }),
        }; // :1143-1144
        saveRevision++;
        App?.UpdateThread(ThreadID, current => current with { InteractionMode = InteractionMode.Build }); // :1145-1146
        _ = MainActor.Spawn(async () =>
        {
            await EnsureLoaded(); // :1147
            await StartTurn("Implement the plan.", Array.Empty<Attachment>());
        });
    }

    // :1150-1156
    public void DismissPlan(string entryID)
    {
        if (!entryIndex.TryGetValue(entryID, out var entry)
            || entry.Item.TimelineItemContent is not TimelineItem.Content.Plan plan) return; // :1151
        entry.Item = entry.Item with
        {
            TimelineItemContent = new TimelineItem.Content.Plan(plan.Value with { ProposedPlanState = ProposedPlan.State.Dismissed }),
        }; // :1152-1153
        saveRevision++;
        ScheduleSave(); // :1154-1155
    }

    // ThreadRuntime.swift:1176-1382. Checkpoint capture and session startup overlap, but both must
    // finish before the provider receives any part of the prompt.
    private void EndDelegationStream()
    {
        hydraStream = null;
        delegationParser = null;
    }

    private async Task StartTurn(string text, IReadOnlyList<Attachment> attachments, IReadOnlyList<long>? hydraHeads = null, bool hydraBrief = false)
    {
        await EnsureLoaded();
        var ignisPing = ignisPingTurn.Value; // Ignis: a ping that cannot run now is dropped, never queued as a follow-up
        if (Phase != RuntimePhase.Idle || IsReverting)
        {
            if (!ignisPing) EnqueueFollowUp(text, attachments);
            return;
        }

        if (App is not { } app || app.Thread(ThreadID) is not { } initialThread
            || app.Project(initialThread.ProjectID) is not { } project) return;

        if (initialThread.IsArchived)
        {
            if (!ignisPing) EnqueueFollowUp(text, attachments);
            return;
        }

        var turnIndex = Turns.Select(turn => turn.Index).DefaultIfEmpty(-1).Max() + 1;
        var turn = new TurnRecord { Index = turnIndex };
        var userItem = new TimelineItem(turn.Id, new TimelineItem.Content.User(new UserMessage
        {
            Text = text,
            Attachments = new ValueList<Attachment>(attachments),
            HydraHeads = hydraHeads is null ? null : new ValueList<long>(hydraHeads),
            HydraBrief = hydraBrief ? true : null,
        }));
        if (skillsContinuationTurn.Value)
        {
            userItem = userItem with { TimelineItemContent = new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = text }) };
        }
        if (ignisPing) userItem = IgnisPingItem(userItem, turn.Id);
        turn = turn with { UserItemID = userItem.Id };
        Turns = Turns.Appending(turn);
        saveRevision++;
        limitedReplyTurnID = null;
        currentTurnID = turn.Id;
        currentProviderDiff = null;
        currentProviderAnchor = null;
        Append(userItem);
        Phase = RuntimePhase.Starting;
        TurnStartedAt = SwiftDate.Now;
        app.UpdateThread(ThreadID, thread => ignisPing ? thread with { LastStatus = TurnStatus.Running } : thread with { UpdatedAt = SwiftDate.Now, LastStatus = TurnStatus.Running });
        if (!ignisPing) app.ReopenIfSettled(ThreadID);

        EndDelegationStream();
        HydraCurrentTurnTouchedPaths = new ValueSet<string>([], SwiftStringComparer.Instance);
        hydraBudget = hydraBudget.BeginTurn(userAuthored: hydraHeads is null && !ignisPing);

        if (initialThread.Hydra is { HydraHeadInfoKind: HydraHeadInfo.Kind.Droppy, IsFinished: true })
        {
            app.UpdateThread(ThreadID, current => current.Hydra is { } info
                ? current with { Hydra = info with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running, FinishedAt = null } }
                : current);
        }

        if (initialThread.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
        {
            var now = SwiftDate.Now;
            var isLaterTurn = Turns.Count > 1;
            app.UpdateThread(ThreadID, current => current.Hydra is { } info
                ? current with
                {
                    Hydra = info with
                    {
                        StartedAt = isLaterTurn ? now : info.StartedAt,
                        QueuedAt = isLaterTurn ? null : info.QueuedAt,
                        ExecutionStartedAt = isLaterTurn ? now : info.ExecutionStartedAt,
                        ExecutionFinishedAt = null,
                        LandingStartedAt = null,
                        LandingFinishedAt = null,
                    },
                }
                : current); // :1217-1228
        }

        var isFinalReport = headReportsNext;
        headReportsNext = false;
        if (initialThread.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
        {
            var allowance = isFinalReport
                ? HydraBudget.ReportSeconds
                : HydraBudget.MaxSeconds + (initialThread.Provider.IsAPIKeyBased ? 90 : 0);
            headBudget?.Cancel();
            headBudget = MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(allowance));
                if (SwiftTask.IsCancelled || currentTurnID != turn.Id || Phase == RuntimePhase.Idle) return;
                headBudgetSpent = !isFinalReport;
                Interrupt();
            });
        }

        ScheduleSave();
        if (!ignisPing) GenerateTitle(text, attachments);
        await EnsureModel(initialThread);

        var directory = initialThread.WorktreePath ?? project.Path;
        var git = new Git(directory);
        var checkpointRef = Git.CheckpointRef(ThreadID, turnIndex, "start");
        settledTree = null;
        latestTree = null;
        treeEpoch++;
        var checkpoint = TaskHandle.Detached(async () =>
        {
            if (ignisPing || !await git.IsRepository()) return false;
            try
            {
                await git.CaptureCheckpoint(checkpointRef);
                return true;
            }
            catch
            {
                return false;
            }
        });

        try
        {
            if (warmup is { } pendingWarmup) await pendingWarmup.Completion;
            var start = MainActor.SpawnThrowing(async () => await EnsureSession(directory));
            if (await checkpoint.Value) UpdateTurn(turn.Id, current => current with { BaseCheckpoint = checkpointRef });
            _ = await start.Value;

            if (SlashCommand.NameIn(text) is not null) await app.Providers.LoadCommands(initialThread.Provider, directory);

            if (currentTurnID != turn.Id || session is not { } liveSession || app.Thread(ThreadID) is not { } thread) return;

            var commandName = SlashCommand.NameIn(text);
            SlashCommand? command = null;
            if (commandName is not null
                && app.Providers.Commands.TryGetValue(ProviderRegistry.CommandKey(thread.Provider, directory), out var commands))
            {
                command = commands.FirstOrDefault(candidate => candidate.Name == commandName);
            }

            var prompt = text;
            var files = attachments.Where(attachment => !attachment.IsImage).ToArray();
            if (files.Length != 0)
                prompt += "\n\nAttached files:\n" + string.Join("\n", files.Select(file => "- " + file.Path));

            // ThreadRuntime.swift:1282-1306. Carry the selected Hydra route to the actual provider input.
            if (!ignisPing && app.HydraLaunch(thread) is { } launch)
            {
                var merge = launch.AutoMerges
                    ? HydraPrompts.MergeStatus(HydraMerges, app.HydraUnmergedFileCount(ThreadID))
                    : null;
                if (!launch.RunsNatively)
                {
                    var team = HydraPrompts.TeamStatus(app.HydraTeam(ThreadID)
                        .Where(head => head.Hydra is not null).Select(head => head.Hydra!).ToArray());
                    var canDelegate = hydraBudget.CanDelegate;
                    if (KeepsHydraPolicyInSystemPrompt(thread.Provider))
                    {
                        prompt = (hydraHeads is null
                            ? HydraPrompts.FallbackTurnNote(team, merge)
                            : HydraPrompts.FallbackReportNote(team, canDelegate, merge)) + prompt;
                    }
                    else
                    {
                        prompt = (hydraHeads is null
                            ? HydraPrompts.FallbackPreamble(launch, team, merge)
                            : HydraPrompts.FallbackReportPreamble(launch, team, canDelegate, merge)) + prompt;
                    }
                }
                else if (merge is not null)
                {
                    prompt = HydraPrompts.FallbackTurnNote(null, merge) + prompt;
                }
                // User turns request delegation; automatic reports retain their review and round-limit policy.
                if (hydraHeads is null) prompt = HydraPrompts.DelegationRequest(launch) + prompt;
            }

            var injectedAntiSlopPolicy = false;
            var antiSlopEpoch = sessionEpoch;
            if (!KeepsHydraPolicyInSystemPrompt(thread.Provider) && commandName is null && !antiSlopFallbackSent)
            {
                var instructions = AntiSlopPolicy.Instructions(sessionSignature?.AntiSlopEnabled ?? app.Settings.AntiSlopEnabled);
                if (instructions.Length != 0)
                {
                    prompt = instructions + "\n\n" + prompt;
                    injectedAntiSlopPolicy = true;
                }
            }

            var injectedContinuation = false;
            var continuationEpoch = sessionEpoch;
            if (Continuation is { } forwarded && !continuationSent && commandName is null)
            {
                prompt = forwarded.Prompt + "\n\n" + prompt;
                injectedContinuation = true;
            }

            // PORT-ONLY (OD-0116, ED-436): the context an account without a standby was handed travels with its first real turn.
            var injectedHandoff = false;
            var handoffEpoch = sessionEpoch;
            if (commandName is null && PendingDeferredHandoff() is { } handoff)
            {
                prompt = handoff + "\n\n" + prompt;
                injectedHandoff = true;
            }

            var (nextHydraBudget, dropNotice) = hydraBudget.TakeDropNotice();
            hydraBudget = nextHydraBudget;
            if (dropNotice is not null) prompt = dropNotice + prompt;

            Phase = RuntimePhase.Running;
            await liveSession.Send(new TurnInput
            {
                Text = prompt,
                Images = thread.Provider.SupportsImages
                    ? new ValueList<Attachment>(attachments.Where(attachment => attachment.IsImage))
                    : ValueList<Attachment>.Empty,
                Model = thread.Model,
                Effort = thread.Effort,
                ServiceTier = ServiceTier(thread),
                FastMode = thread.FastMode,
                RuntimeMode = Marrow.App.Assistant.MissionLaunch.TurnMode(thread), // PORT-ONLY (Dextra AS-5): a mission chat stays in the ask mode
                InteractionMode = thread.InteractionMode,
                IsFinalReport = isFinalReport,
                Command = command,
            });

            if (injectedAntiSlopPolicy && sessionEpoch == antiSlopEpoch) antiSlopFallbackSent = true;
            if (injectedContinuation && sessionEpoch == continuationEpoch) continuationSent = true;
            if (injectedHandoff && sessionEpoch == handoffEpoch) HoldDeferredHandoff(null);
            if (unsentSessionID is { } pendingSessionID)
            {
                unsentSessionID = null;
                app.UpdateThread(ThreadID, current => current with { ProviderSessionID = pendingSessionID, ProviderAccountID = sessionSignature?.AccountId ?? current.ProviderAccountID });
            }
            if (command is not null) app.Providers.RecordCommand(command.Name, thread.Provider);
        }
        catch (Exception error)
        {
            if (currentTurnID != turn.Id) return;
            // Foundation localizes process-launch errors; Win32Exception.Message is the Windows equivalent.
            var description = error is System.ComponentModel.Win32Exception nativeError
                ? nativeError.Message
                : SwiftErrors.LocalizedDescription(error);
            if (FinishTurnUpdatingCLI(description, TurnStatus.Failed, turn.Id)) return; // B3: PR-RUNTIME-077/265.
            AppendNotice(Notice.Level.Error, description);
            await FinishTurn(TurnStatus.Failed, turn.Id);
        }
    }

    // ThreadRuntime.swift:1392-1397.
    private string? ServiceTier(ChatThread thread)
    {
        if (thread.Provider != ProviderKind.Codex
            || App?.Providers.Model(thread.Model, thread.Provider)?.FastTier is not { } tier) return null;
        return thread.FastMode ? tier : "default";
    }

    // ThreadRuntime.swift:1401-1410.
    private async Task EnsureModel(ChatThread thread)
    {
        if (thread.Model is not null || App is not { } app) return;
        await app.Providers.LoadCatalog(thread.Provider);
        if (app.Providers.DefaultModel(thread.Provider) is { } model)
        {
            app.UpdateThread(ThreadID, current => current with
            {
                Model = model.Id,
                Effort = current.Effort ?? model.DefaultEffort,
            });
        }
    }

    // ThreadRuntime.swift:1412-1428.
    public void WarmSession()
    {
        if (Phase != RuntimePhase.Idle || session is not null || warmup is not null || IsReverting
            || App is not { } app || app.Thread(ThreadID) is not { } thread || thread.IsArchived
            || thread.Provider.IsAPIKeyBased || app.Project(thread.ProjectID) is not { } project) return;

        var directory = thread.WorktreePath ?? project.Path;
        var weakSelf = new WeakReference<ThreadRuntime>(this);
        warmup = MainActor.Spawn(async () =>
        {
            if (!weakSelf.TryGetTarget(out var runtime)) return;
            await runtime.EnsureModel(thread);
            if (runtime.Phase != RuntimePhase.Idle || runtime.session is not null)
            {
                runtime.warmup = null;
                return;
            }

            // A failed start intentionally keeps the finished task as the per-runtime warm-up latch.
            try
            {
                _ = await runtime.EnsureSession(directory);
                runtime.warmup = null;
            }
            catch
            {
                // A send makes its own start attempt; subsequent warmSession calls stay disabled.
            }
        });
    }

    // ThreadRuntime.swift:1640-1686. Swift characters are counted as grapheme clusters.
    private ValueList<TranscriptMessage> ApiTranscript()
    {
        var fragments = new List<(bool Assistant, string Text)>();
        foreach (var entry in Entries)
        {
            if (entry.Item.TurnID == currentTurnID) continue;
            switch (entry.Item.TimelineItemContent)
            {
                case TimelineItem.Content.User user when user.Value.Text.Length != 0:
                    fragments.Add((false, user.Value.Text));
                    break;
                case TimelineItem.Content.Assistant assistant when assistant.Value.Text.Length != 0:
                    fragments.Add((true, assistant.Value.Text));
                    break;
            }
        }

        var transcript = new List<TranscriptMessage>(fragments.Count);
        var index = 0;
        while (index < fragments.Count)
        {
            if (!fragments[index].Assistant)
            {
                transcript.Add(new TranscriptMessage
                {
                    TranscriptMessageRole = TranscriptMessage.Role.User,
                    Text = fragments[index].Text,
                });
                index++;
                continue;
            }

            var runStart = index;
            while (index < fragments.Count && fragments[index].Assistant) index++;
            transcript.Add(new TranscriptMessage
            {
                TranscriptMessageRole = TranscriptMessage.Role.Assistant,
                Text = string.Join("\n\n", fragments.Skip(runStart).Take(index - runStart).Select(fragment => fragment.Text)),
            });
        }

        var kept = transcript.Skip(Math.Max(0, transcript.Count - 40)).ToList();
        var budget = 60_000;
        for (var keptIndex = kept.Count - 1; keptIndex >= 0; keptIndex--)
        {
            var characters = SwiftString.Characters(kept[keptIndex].Text).ToArray();
            if (characters.Length <= budget)
            {
                budget -= characters.Length;
            }
            else if (budget > 200)
            {
                kept[keptIndex] = kept[keptIndex] with { Text = "…" + string.Concat(characters.TakeLast(budget)) };
                budget = 0;
            }
            else
            {
                kept.RemoveRange(0, keptIndex + 1);
                break;
            }
        }

        return new ValueList<TranscriptMessage>(kept);
    }

    // ThreadRuntime.swift:1388-1390; PR-HYDRA-060. Native Hydra providers plus API-key providers.
    private static bool KeepsHydraPolicyInSystemPrompt(ProviderKind provider) =>
        provider is ProviderKind.Claude or ProviderKind.Codex or ProviderKind.Copilot
        || provider.IsAPIKeyBased || provider == ProviderKind.Local; // ED-410

    private static SessionSignature MakeSessionSignature(
        ChatThread thread, string directory, bool antiSlopEnabled, HydraLaunch? hydra)
    {
        var copilotLaunchMode = thread.Provider == ProviderKind.Copilot && thread.RuntimeMode == RuntimeMode.Auto
            ? RuntimeMode.Auto
            : (RuntimeMode?)null;

        return new SessionSignature
        {
            Provider = thread.Provider,
            AccountId = thread.ProviderAccountID,
            Directory = directory,
            LaunchRuntimeMode = thread.Provider is ProviderKind.Cursor or ProviderKind.Grok or ProviderKind.Devin or ProviderKind.Antigravity
                ? thread.RuntimeMode
                : copilotLaunchMode,
            LaunchEffort = thread.Provider is ProviderKind.Claude or ProviderKind.Antigravity ? thread.Effort : null,
            LaunchFast = thread.Provider == ProviderKind.Claude ? thread.FastMode : null,
            LaunchModel = thread.Provider == ProviderKind.Antigravity ? thread.Model : null,
            LaunchInteraction = thread.Provider == ProviderKind.Antigravity ? thread.InteractionMode : null,
            Hydra = hydra,
            AntiSlopEnabled = antiSlopEnabled,
        };
    }

    // :1430-1604. Build the session signature after resolving any routed worker catalogue.
    private Task<ProviderSession> EnsureSession(string directory, bool allowNewSession = true) =>
        EnsureSessionOwned(directory, allowNewSession, CancellationToken.None, null);

    // A reset-driven restart keeps its original resume identity and may commit only while its route owns it.
    private ProviderSession? guardedStartingSession;
    private async Task<ProviderSession> EnsureSessionOwned(string directory, bool allowNewSession,
        CancellationToken startCancellation, Func<bool>? canCommit)
    {
        void CheckStart()
        {
            startCancellation.ThrowIfCancellationRequested();
            if (canCommit?.Invoke() == false) throw new CancellationError();
        }
        CheckStart();
        if (App is not { } app || app.Thread(ThreadID) is not { } thread)
            throw new ProviderError(new ProviderError.Case.NotRunning());

        if (canCommit is not null && !thread.Provider.IsAPIKeyBased && thread.Provider != ProviderKind.Local
            && string.IsNullOrWhiteSpace(thread.ProviderSessionID))
            throw new ProviderError(new ProviderError.Case.NotRunning());

        // :1430-1450. The worker model appears in the lead brief, so load its catalogue first.
        if (app.HydraIsOn(thread) && app.HydraPair(thread) is { } pair)
        {
            var headsProvider = app.HydraHeadsProvider(pair);
            if (headsProvider != thread.Provider)
                await SwiftTry.IgnoreAsync(() => app.Providers.LoadCatalog(headsProvider));
        }

        var hydra = KeepsHydraPolicyInSystemPrompt(thread.Provider) ? app.HydraLaunch(thread) : null;
        // PORT-ONLY: OD-0114 / ED-404. Existing native IDs belong to the unmoved legacy root.
        var accounts = app.Settings.Accounts;
        var boundID = thread.ProviderAccountID;
        if (boundID is null && thread.ProviderSessionID is not null)
            boundID = accounts.ForService(thread.Provider).FirstOrDefault(a => a.UsesLegacyCredentials)?.Id
                ?? throw new InvalidOperationException("The original account for this conversation is unavailable.");
        var account = accounts.Choose(thread.Provider, boundID)
            ?? throw new InvalidOperationException("No enabled account is available for this conversation.");
        app.AccountRouting.Attach(this);
        var launchThread = account.UsesLegacyCredentials ? thread : thread with { ProviderAccountID = account.Id };
        var localTarget = thread.Provider == ProviderKind.Local ? app.Providers.Local.Resolve(thread.Model) : null;
        if (thread.Provider == ProviderKind.Local && localTarget is null)
        {
            await app.Providers.LoadCatalog(ProviderKind.Local);
            localTarget = app.Providers.Local.Resolve(thread.Model)
                ?? throw new ProviderError(new ProviderError.Case.Failed("That local server or model is unavailable. Refresh the model list and select it again."));
        }
        var signature = MakeSessionSignature(launchThread, directory, app.Settings.AntiSlopEnabled, hydra)
            with { LocalServerId = localTarget?.Server.Id, LocalCatalogRevision = localTarget is null ? null : app.Providers.Local.Revision };
        if (session is { IsRunning: true } && sessionSignature == signature)
        {
            var reused = session;
            if (sessionSkills.TryGetValue(reused, out var skills))
            {
                var epoch = sessionEpoch;
                var verified = await Task.Run(skills.Verify);
                if (sessionEpoch != epoch || !ReferenceEquals(session, reused)) throw new CancellationError();
                if (!verified) throw new ProviderError(new ProviderError.Case.Failed("The skill generation failed verification before the next turn."));
            }
            return reused;
        }

        // Resolve the login environment before tearing down the current session.
        await LoginEnvironment.Load();
        CheckStart();
        ReleaseSession(stop: true);
        StopNativeHeads();

        if (thread.Provider.IsAPIKeyBased || thread.Provider == ProviderKind.Local)
        {
            var apiKey = thread.Provider == ProviderKind.Local ? null : app.Providers.ApiKey(account.Id);
            if (thread.Provider.IsAPIKeyBased && string.IsNullOrEmpty(apiKey))
                throw new ProviderError(new ProviderError.Case.NotInstalled(thread.Provider));

            async Task<ProviderSession> MakeApiSession(string? resumeID)
            {
                var launchEpoch = sessionEpoch;
                await pendingSkillRelease;
                var environment = app.Providers.Environment(account.Id);
                var skills = await Marrow.App.Port.Skills.ProviderSkills.Prepare.Current(thread.Provider, ThreadID.ToString("N"), directory, environment);
                try
                {
                    if (SwiftTask.IsCancelled || sessionEpoch != launchEpoch) throw new CancellationError();
                    var configuration = new SessionConfiguration
                    {
                        Provider = thread.Provider,
                        Executable = null,
                        WorkingDirectory = SessionDirectoryUrl(directory),
                        AccountId = account.Id,
                        Environment = environment,
                        ResumeID = resumeID,
                        ResumeAt = resumeID is null ? null : thread.ProviderResumeAt,
                        Model = thread.Model,
                        Effort = thread.Effort,
                        FastMode = thread.FastMode,
                        RuntimeMode = thread.RuntimeMode,
                        InteractionMode = thread.InteractionMode,
                        ApiKey = apiKey,
                        Hydra = hydra,
                        Transcript = ApiTranscript(),
                        IsHydraHead = thread.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy,
                        AntiSlopEnabled = signature.AntiSlopEnabled,
                        SkillGeneration = skills,
                        SkillDataRoot = Storage.Root,
                        SkillHome = LoginEnvironment.HomeDirectory,
                        SkillToolOffer = SkillsOffers,
                        IgnisThreadID = ThreadID, // PORT-ONLY: Ignis (ED-NEW-ignis-runtime)
                    };

                    configuration = app.MissionLaunchOf(thread, configuration); // PORT-ONLY (Dextra AS-5): a mission chat is sealed and jobbed
                    var created = AccountSessionFactory.Create(app.Providers, configuration, account.Id).Session;
                    if (created is OpenAICompatibleSession api) api.SkillTools = Marrow.Services.Port.Skills.Providers.SkillSessionFactory.NativeTools(configuration);
                    if (skills is not null) sessionSkills.Add(created, skills);
                    Observe(created, account.UsesLegacyCredentials ? UsageAccountKey.Default(account.Service) : new(account.Service, account.Id.ToString("D")));
                    return created;
                }
                catch { skills?.Dispose(); throw; }
            }

            ProviderSession candidate = await MakeApiSession(thread.ProviderSessionID);
            session = candidate;
            sessionSignature = signature;
            if (canCommit is not null) guardedStartingSession = candidate;
            var started = false;
            try
            {
                string sessionID;
                try
                {
                    sessionID = await StartOwnedSessionAsync(candidate, startCancellation);
                }
                catch (Exception) when (thread.ProviderSessionID is not null
                    && thread.ProviderResumeAt is null
                    && allowNewSession
                    && ReferenceEquals(session, candidate)
                    && !startCancellation.IsCancellationRequested
                    && canCommit?.Invoke() != false
                    && !SwiftTask.IsCancelled)
                {
                    ReleaseSession(stop: true);
                    candidate = await MakeApiSession(resumeID: null);
                    session = candidate;
                    sessionSignature = signature;
                    sessionID = await StartOwnedSessionAsync(candidate, startCancellation);
                }

                CheckStart();
                if (!ReferenceEquals(session, candidate) || SwiftTask.IsCancelled)
                    throw new CancellationError();
                BindLiveApiKeys(candidate, account.Id);
                started = true;
                app.UpdateThread(ThreadID, current => current with { ProviderSessionID = sessionID, ProviderAccountID = launchThread.ProviderAccountID });
                return candidate;
            }
            finally
            {
                if (ReferenceEquals(guardedStartingSession, candidate)) guardedStartingSession = null;
                if (!started)
                {
                    candidate.OnEvent = null;
                    if (ReferenceEquals(session, candidate)) ReleaseSession(stop: true);
                    else candidate.Stop();
                }
            }
        }

        if (app.Providers.Executable(thread.Provider) is not { } executable)
            throw new ProviderError(new ProviderError.Case.NotInstalled(thread.Provider));

        async Task<ProviderSession> MakeSession(string? resumeID)
        {
            var launchEpoch = sessionEpoch;
            await pendingSkillRelease;
            var environment = app.Providers.Environment(account.Id);
            var skills = thread.Provider switch
            {
                ProviderKind.Antigravity => await Marrow.App.Port.Skills.AntigravitySkills.Prepare.Current(ThreadID.ToString("N"), directory),
                ProviderKind.Cursor => await Marrow.App.Port.Skills.CursorSkills.Prepare.Current(ThreadID.ToString("N"), directory),
                ProviderKind.Devin => null, // explicit invocation only until its discovery route is proven
                _ => await Marrow.App.Port.Skills.ProviderSkills.Prepare.Current(thread.Provider, ThreadID.ToString("N"), directory, environment),
            };
            try
            {
                if (SwiftTask.IsCancelled || sessionEpoch != launchEpoch) throw new CancellationError();
                var configuration = new SessionConfiguration
                {
                    Provider = thread.Provider,
                    Executable = executable,
                    WorkingDirectory = SessionDirectoryUrl(directory),
                    AccountId = account.Id,
                    Environment = environment,
                    ResumeID = resumeID,
                    ResumeAt = resumeID is null ? null : thread.ProviderResumeAt,
                    Model = thread.Model,
                    Effort = thread.Effort,
                    FastMode = thread.FastMode,
                    RuntimeMode = thread.RuntimeMode,
                    InteractionMode = thread.InteractionMode,
                    Hydra = hydra,
                    AntiSlopEnabled = signature.AntiSlopEnabled,
                    SkillGeneration = skills,
                    SkillDataRoot = Storage.Root,
                    SkillHome = environment.TryGetValue("HOME", out var skillHome) ? skillHome : LoginEnvironment.HomeDirectory,
                    SkillToolOffer = SkillsOffers,
                    SkillMcpExecutable = thread.Provider == ProviderKind.Cursor ? Path.Combine(AppContext.BaseDirectory, "Marrow.App.exe") : null,
                    IgnisThreadID = ThreadID, // PORT-ONLY: Ignis (ED-NEW-ignis-runtime)
                };

                configuration = app.MissionLaunchOf(thread, configuration); // PORT-ONLY (Dextra AS-5): a mission chat is sealed and jobbed
                var created = AccountSessionFactory.Create(app.Providers, configuration, account.Id).Session;
                if (skills is not null) sessionSkills.Add(created, skills);
                Observe(created, account.UsesLegacyCredentials ? UsageAccountKey.Default(account.Service) : new(account.Service, account.Id.ToString("D")));
                return created;
            }
            catch { skills?.Dispose(); throw; }
        }

        async Task<string> StartCliSession(ProviderSession candidate)
        {
            try { return await StartOwnedSessionAsync(candidate, startCancellation); }
            catch (Exception error) when (thread.Provider is ProviderKind.Antigravity or ProviderKind.Cursor
                && error is IOException or UnauthorizedAccessException)
            {
                throw new ProviderError(new ProviderError.Case.Failed(error.Message));
            }
        }

        var cliCandidate = await MakeSession(thread.ProviderSessionID);
        session = cliCandidate;
        sessionSignature = signature;
        if (canCommit is not null) guardedStartingSession = cliCandidate;
        var cliStarted = false;
        isStartingSession = true;
        try
        {
            string sessionID;
            try
            {
                sessionID = await StartCliSession(cliCandidate);
            }
            catch (Exception error) when (allowNewSession
                && ReferenceEquals(session, cliCandidate)
                && !startCancellation.IsCancellationRequested
                && canCommit?.Invoke() != false
                && !SwiftTask.IsCancelled
                && StartsOver(error, thread))
            {
                ReleaseSession(stop: true);
                if (LostConversation(error))
                {
                    app.UpdateThread(ThreadID, current => current with { ProviderResumeAt = null });
                    if (currentTurnID is not null)
                        AppendNotice(Notice.Level.Warning,
                            $"{thread.Provider.DisplayName} no longer has this conversation. It starts a new one here, without the messages above.");
                }
                cliCandidate = await MakeSession(resumeID: null);
                session = cliCandidate;
                sessionSignature = signature;
                sessionID = await StartCliSession(cliCandidate);
            }

            CheckStart();
            if (!ReferenceEquals(session, cliCandidate) || SwiftTask.IsCancelled)
                throw new CancellationError();
            if (canCommit is not null && thread.ProviderSessionID is { } resumed
                && !SameResumeIdentity(thread.Provider, resumed, sessionID))
                throw new ProviderError(new ProviderError.Case.NotRunning());
            cliStarted = true;
            if (ReferenceEquals(guardedStartingSession, cliCandidate)) guardedStartingSession = null;
            if (launchThread.ProviderAccountID is not null)
                app.UpdateThread(ThreadID, current => current with { ProviderAccountID = launchThread.ProviderAccountID });
            RecordSessionID(sessionID);
            return cliCandidate;
        }
        finally
        {
            if (ReferenceEquals(guardedStartingSession, cliCandidate)) guardedStartingSession = null;
            isStartingSession = false;
            if (!cliStarted)
            {
                cliCandidate.OnEvent = null;
                if (ReferenceEquals(session, cliCandidate)) ReleaseSession(stop: true);
                else
                {
                    try { cliCandidate.Stop(); }
                    finally { ReleaseSessionSkills(cliCandidate); }
                }
            }
        }
    }

    // ThreadRuntime.swift:1474,1537; Windows boundary as in ProviderRegistry.FileUrl.
    // Percent-encode a native path under a non-file scheme: file URL .Path is POSIX.
    internal static SwiftURL SessionDirectoryUrl(string path) // internal: ED-NEW-ignisadvisor-2 (the advisor session's scratch directory)
    {
        var native = path.Replace('/', '\\');
        var builder = new System.Text.StringBuilder();
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(native))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return SwiftURL.Create("marrow-native-path:" + builder) ?? throw SwiftTraps.Fatal("Invalid session directory path.");
    }

    // :1691-1719
    public void Interrupt()
    {
        if (Phase == RuntimePhase.Idle) return;

        var turnID = currentTurnID;
        interruptWatchdog?.Cancel();
        interruptWatchdog = null;

        if (Phase == RuntimePhase.Starting)
        {
            currentTurnID = null;
            _ = MainActor.Spawn(() => FinishTurn(TurnStatus.Interrupted, turnID));
            return;
        }

        if (turnID is not { } id) return;

        _ = MainActor.Spawn(async () =>
        {
            if (currentTurnID != id) return;
            if (session is { } provider)
            {
                await provider.Interrupt();
            }
            else
            {
                await FinishTurn(TurnStatus.Interrupted, id);
            }
        });

        interruptWatchdog = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(8));
            if (SwiftTask.IsCancelled || currentTurnID != id) return;

            interruptWatchdog = null;
            ReleaseSession(stop: true);
            await FinishTurn(TurnStatus.Interrupted);
        });
    }

    private async Task FinishTurn(TurnStatus status, Guid? requestedTurnID = null)
    {
        interruptWatchdog?.Cancel();
        interruptWatchdog = null;
        headBudget?.Cancel();
        headBudget = null;

        if ((requestedTurnID ?? currentTurnID) is not { } turnID
            || Turns.FirstOrDefault(turn => turn.Id == turnID) is not { } turn)
        {
            Phase = RuntimePhase.Idle;
            TurnStartedAt = null;
            DrainPendingSend();
            return;
        }

        if (finishingTurnID == turnID) return;
        finishingTurnID = turnID;
        try
        {
            foreach (var id in commandTrees.Keys.ToArray()) SettleCommand(id);
            foreach (var key in hydraCommandTrees.Keys.ToArray()) SettleHydraCommand(key);

            if (currentTurnID is { } activeTurnID && activeTurnID != turnID) return;

            closingTurnID = turnID;
            try
            {
                var settles = commandSettles.Values.ToArray();
                commandSettles.Clear();
                var settleTask = Task.WhenAll(settles.Select(task => task.Completion));
                if (status == TurnStatus.Completed)
                {
                    await settleTask;
                }
                else
                {
                    var timeout = MainActor.Spawn(async () =>
                        await SwiftTask.TrySleep(TimeSpan.FromSeconds(3)));
                    await Task.WhenAny(settleTask, timeout.Completion);
                    timeout.Cancel();
                }

                if (currentTurnID is { } lateTurnID && lateTurnID != turnID) return;

                currentTurnID = null;
                FlushDeltas();
                Approvals = ValueList<ApprovalRequest>.Empty;
                Questions = ValueList<QuestionRequest>.Empty;

                var reportedEdits = new List<FileEdit>();
                foreach (var entry in Entries.Where(entry => entry.Item.TurnID == turnID))
                {
                    EndStreaming(entry);
                    if (entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool) continue;

                    var call = tool.Value;
                    if (call.ToolCallStatus == ToolCall.Status.Running)
                    {
                        call = call.Finish(status == TurnStatus.Completed
                            ? ToolCall.Status.Completed
                            : ToolCall.Status.Failed);
                        entry.Item = entry.Item with
                        {
                            TimelineItemContent = new TimelineItem.Content.Tool(call),
                        };
                        saveRevision += 1;
                    }
                    reportedEdits.AddRange(call.Edits);
                }

                var providerDiff = currentProviderDiff;
                var providerAnchor = currentProviderAnchor;
                currentProviderDiff = null;
                currentProviderAnchor = null;
                UpdateTurn(turnID, current => current with
                {
                    Status = status,
                    CompletedAt = SwiftDate.Now,
                    ProviderDiff = providerDiff ?? current.ProviderDiff,
                    ProviderAnchor = providerAnchor ?? current.ProviderAnchor,
                });

                if (providerAnchor is not null && Thread?.ProviderResumeAt is not null)
                    App?.UpdateThread(ThreadID, current => current with { ProviderResumeAt = providerAnchor });

                turn = Turns.First(candidate => candidate.Id == turnID);
                string? endCheckpoint = null;
                if (App is { } app && app.Thread(ThreadID) is { } thread
                    && app.Project(thread.ProjectID) is { } project)
                {
                    var root = thread.WorktreePath ?? project.Path;
                    var git = new Git(root);
                    if (turn.BaseCheckpoint is not null)
                    {
                        var checkpointRef = Git.CheckpointRef(ThreadID, turn.Index, "end");
                        try
                        {
                            await git.CaptureCheckpoint(checkpointRef);
                            endCheckpoint = checkpointRef;
                        }
                        catch
                        {
                            // A missing end snapshot does not invalidate the rest of the turn summary.
                        }
                    }

                    var touched = new HashSet<string>(SwiftStringComparer.Instance);
                    foreach (var edit in reportedEdits)
                    {
                        if (TouchedPaths.Relative(edit.Path, root) is { } relative) touched.Add(relative);
                    }
                    touched.UnionWith(hydraTouched);
                    hydraTouched.Clear();
                    var touchedPaths = touched.OrderBy(path => path, SwiftStringComparer.Instance).ToArray();

                    UpdateTurn(turnID, current => current with
                    {
                        EndCheckpoint = endCheckpoint ?? current.EndCheckpoint,
                        TouchedPaths = new ValueList<string>(touchedPaths),
                    });
                    HydraCurrentTurnTouchedPaths = HydraCurrentTurnTouchedPaths.Mutating(paths =>
                        paths.UnionWith(touched));
                }

                DiffRevision += 1;
                var changes = FileChangeSummaryDiff.Create(await ParsedDiff(turnID));
                var summary = new TurnSummary
                {
                    TurnID = turnID,
                    Status = status,
                    Duration = (SwiftDate.Now - turn.StartedAt).TotalSeconds,
                    FilesChanged = changes.Files.Count,
                    Additions = changes.Additions,
                    Deletions = changes.Deletions,
                    Changes = changes,
                };
                Append(new TimelineItem(turnID, new TimelineItem.Content.TurnEnd(summary)));

                if (status == TurnStatus.Completed && limitedReplyTurnID == turnID) limitedReplyTurnID = null;
                Phase = RuntimePhase.Idle;
                TurnStartedAt = null;
                SaveNow();
                SettleForegroundHeads(status);
                var answerAuthor = status == TurnStatus.Completed && TryHandshakeAccountId(out var author) ? (Guid?)author : null;

                if (Thread?.IsArchived == true)
                {
                    headBudgetSpent = false;
                    App?.TurnFinished(ThreadID, status, continues: false);
                    return;
                }

                var continues = false;
                if (pendingSend is not null)
                {
                    continues = DrainPendingSend();
                }
                else if (status == TurnStatus.Completed
                    && SpawnDelegatedHeads(turnID) is var delegation
                    && delegation != DelegationOutcome.None)
                {
                    continues = true;
                    if (delegation == DelegationOutcome.HeadsOut) _ = DrainFollowUps(status);
                }
                else if (status == TurnStatus.Completed && FlushHydraReports())
                {
                    continues = true;
                }
                else
                {
                    if (status != TurnStatus.Completed) DropDelegationBlock(turnID, status);
                    continues = DrainFollowUps(status);
                }

                if (!continues && status != TurnStatus.Interrupted && FlushHydraReports())
                    continues = true;

                if (ShouldContinueForRunningDroppyHeads(continues, status))
                    continues = true;

                if (headBudgetSpent)
                {
                    headBudgetSpent = false;
                    if (status == TurnStatus.Interrupted
                        && Thread?.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
                    {
                        headReportsNext = true;
                        AutoStartedTurnToken += 1;
                        _ = MainActor.Spawn(async () =>
                        {
                            await EnsureLoaded();
                            await StartTurn(
                                "[nth] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left.",
                                Array.Empty<Attachment>());
                        });
                        continues = true;
                    }
                }

                App?.TurnFinished(ThreadID, status, continues);
                if (answerAuthor is { } completedAuthor) CheckCompletedAccountAnswer(turnID, completedAuthor);

                // ThreadRuntime.swift:2493-2503: once the entire job has settled, hand its
                // work to the existing merge coordinator, which rechecks readiness after waiting.
                if (AutoMergeAppIfReady(continues, status) is { } mergeApp)
                    _ = MainActor.Spawn(() => mergeApp.AutoMergeHydraWork(ThreadID));
            }
            finally
            {
                closingTurnID = null;
            }
        }
        finally
        {
            finishingTurnID = null;
            App?.TickAccountRouting(ThreadID);
        }
    }

    private bool ShouldContinueForRunningDroppyHeads(bool continues, TurnStatus status) =>
        !continues
        && status == TurnStatus.Completed
        && (App?.RunningDroppyHeads(ThreadID) ?? 0) > 0;

    private AppModel? AutoMergeAppIfReady(bool continues, TurnStatus status)
    {
        if (continues) return null;
        if (status != TurnStatus.Completed) return null;
        if (hydraPendingReports.Count != 0) return null;
        if (hydraBatches.Count != 0) return null;
        if (hydraWaiting.Count != 0) return null;

        var app = App;
        if (app is null) return null;
        var thread = app.Thread(ThreadID);
        if (thread is null) return null;
        if (!app.HydraIsOn(thread)) return null;
        if (!app.Settings.HydraAutoMerge) return null;
        if (app.RunningHydraHeads(ThreadID) != 0) return null;
        return app;
    }

    private void EndStreaming(TimelineEntry entry, bool recordSave = true)
    {
        switch (entry.Item.TimelineItemContent)
        {
            case TimelineItem.Content.Assistant assistant when assistant.Value.IsStreaming:
                entry.Item = entry.Item with
                {
                    TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { IsStreaming = false }),
                };
                if (recordSave) saveRevision += 1;
                break;
            case TimelineItem.Content.Reasoning reasoning when reasoning.Value.IsStreaming:
                entry.Item = entry.Item with
                {
                    TimelineItemContent = new TimelineItem.Content.Reasoning(reasoning.Value with { IsStreaming = false }),
                };
                if (recordSave) saveRevision += 1;
                break;
            case TimelineItem.Content.Plan plan when plan.Value.ProposedPlanState == ProposedPlan.State.Drafting:
                if (recordSave) CompletePlan(entry.Id, plan.Value.Markdown);
                else entry.Item = entry.Item with
                {
                    TimelineItemContent = new TimelineItem.Content.Plan(plan.Value with { ProposedPlanState = ProposedPlan.State.Proposed }),
                };
                break;
        }
    }

    private void SettleForegroundHeads(TurnStatus status)
    {
        if (App is not { } app) return;
        foreach (var (agentID, headID) in hydraNativeHeads.ToArray())
        {
            if (app.Thread(headID)?.Hydra is not { IsBackground: false, IsFinished: false }) continue;
            HydraAgentFinished(agentID,
                status == TurnStatus.Completed ? TurnStatus.Completed : TurnStatus.Interrupted,
                summary: null);
        }
    }

    // :1721-1731. Swift: resolve(_:option:).
    public void Resolve(ApprovalRequest request, ApprovalRequest.Option option)
    {
        // PORT-ONLY (Dextra AS-5, ED-NEW-dextra-as5-13): a mission chat's answer is logged first, and goes as a decline when
        // it cannot be logged or its mission is stopping. Any other chat's answer passes unchanged.
        if (App is { } dextraApp) option = dextraApp.DextraChatAnswer(ThreadID, request, option);
        // DEVIATION OD-0114 / ED-407: retain the request, chosen answer and scope after it leaves Approvals.
        BufferHandoff(HandoffJournal.EncodeResolution(request.Id, new { Request = request, Option = option }), preserve: true);
        Approvals = Approvals.Mutating(items => items.RemoveAll(item => item.Id == request.Id));
        if (request.RequestKind == ApprovalRequest.Kind.Plan
            && request.ToolItemID is { } itemID
            && entryIndex.TryGetValue(itemID, out var entry)
            && entry.Item.TimelineItemContent is TimelineItem.Content.Plan plan)
        {
            var state = option.OptionRole == ApprovalRequest.Option.Role.Approve
                ? ProposedPlan.State.Accepted
                : ProposedPlan.State.Dismissed;
            entry.Item = entry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Plan(plan.Value with { ProposedPlanState = state }),
            };
            saveRevision++;
            ScheduleSave();
        }
        session?.ResolveApproval(request.Id, option.Id);
        App?.DextraRequestResolved(ThreadID, request.Id); // PORT-ONLY (Dextra AS-5): answered in the chat, its card goes
    }

    // :1733-1736. Swift: answer(_:answers:); the map is ProviderSession.AnswerQuestion's.
    public void Answer(QuestionRequest request, ValueMap<string, ValueList<string>> answers)
    {
        BufferHandoff(HandoffJournal.EncodeResolution(request.Id, new { Request = request, Answers = answers }), preserve: true);
        Questions = Questions.Mutating(items => items.RemoveAll(item => item.Id == request.Id));
        session?.AnswerQuestion(request.Id, answers);
        App?.DextraRequestResolved(ThreadID, request.Id); // PORT-ONLY (Dextra AS-5)
    }

    // :1738-1757
    public void Compact() => Compact(ignisKeepTurns: null);

    // PORT-ONLY: Ignis (ED-NEW-ignis-runtime). ignisKeepTurns set: a silent tidy-up (no notices; IgnisTidy runs it).
    private void Compact(int? ignisKeepTurns)
    {
        if (Phase != RuntimePhase.Idle) return;
        if (session is not { IsRunning: true } activeSession)
        {
            if (ignisKeepTurns is null) AppendNotice(Notice.Level.Info, "Nothing to compact yet.");
            return;
        }

        if (ignisKeepTurns is null) AppendNotice(Notice.Level.Info, "Compacting context.");
        else IgnisTidyStarted();
        _ = MainActor.Spawn(async () =>
        {
            try
            {
                if (ignisKeepTurns is { } keep) await IgnisCompact(activeSession, keep);
                else await activeSession.Compact();
                await MainActor.Run(() =>
                {
                    antiSlopFallbackSent = false;
                    continuationSent = false;
                    if (ignisKeepTurns is not null) IgnisTidyFinished(succeeded: true);
                });
            }
            catch (Exception error)
            {
                await MainActor.Run(() =>
                {
                    if (ignisKeepTurns is not null) IgnisTidyFinished(succeeded: false);
                    else AppendNotice(Notice.Level.Warning, SwiftErrors.LocalizedDescription(error));
                });
            }
        });
    }

    // :1765-1781. Swift: revertPreview(for:) -> Task<RevertPreview, Error> (a throwing TaskHandle, R-TASK-1).
    public TaskHandle<RevertPreview> RevertPreview(Guid turnID)
    {
        if (revertPreviews.TryGetValue(turnID, out var cached)
            && cached.Revision == DiffRevision
            && SwiftClock.Now - cached.StartedAt < TimeSpan.FromSeconds(10))
            return cached.Task; // :1766-1768

        var revision = DiffRevision; // :1769
        TaskHandle<RevertPreview> task = null!;
        task = MainActor.SpawnThrowing(async () =>
        {
            try
            {
                var preview = await PreviewRevert(turnID);
                if (revertPreviews.TryGetValue(turnID, out var entry) && entry.Revision == revision)
                    revertPreviews[turnID] = entry with { Result = task.Value }; // :1772-1775
                return preview;
            }
            catch
            {
                if (revertPreviews.TryGetValue(turnID, out var entry) && entry.Revision == revision)
                    revertPreviews[turnID] = entry with { Result = task.Value }; // :1772-1775
                throw;
            }
        });

        foreach (var stale in revertPreviews.Where(pair => pair.Value.Revision != revision).Select(pair => pair.Key).ToArray())
            revertPreviews.Remove(stale); // :1778
        revertPreviews[turnID] = new RevertPreviewEntry // :1779
        {
            Revision = revision,
            StartedAt = SwiftClock.Now,
            Task = task,
        };
        return task;
    }

    // :1785-1789. Swift: cachedRevertPreview(for:) -> Result<RevertPreview, Error>? (see the header).
    public Task<RevertPreview>? CachedRevertPreview(Guid turnID) =>
        revertPreviews.TryGetValue(turnID, out var entry)
        && entry.Revision == DiffRevision
        && SwiftClock.Now - entry.StartedAt < TimeSpan.FromSeconds(10)
            ? entry.Result
            : null; // :1786-1788

    // :1804-1813
    private async Task<RevertPreview> PreviewRevert(Guid turnID)
    {
        var app = App;
        var thread = Thread;
        var project = thread is null ? null : app?.Project(thread.ProjectID);
        var index = turns.ToList().FindIndex(turn => turn.Id == turnID);
        if (app is null || thread is null || project is null || index < 0 || turns[index].BaseCheckpoint is not { } baseCheckpoint)
            throw new ProviderError(new ProviderError.Case.Failed("No file checkpoint is available for this message. You can still keep the file changes.")); // :1805-1807

        var removed = turns.Skip(index).ToArray(); // :1809
        var paths = removed.SelectMany(turn => turn.TouchedPaths ?? ValueList<string>.Empty)
            .Distinct(SwiftStringComparer.Instance)
            .OrderBy(path => path, SwiftStringComparer.Instance)
            .ToArray(); // :1810
        var end = removed.Select(turn => turn.EndCheckpoint).LastOrDefault(checkpoint => checkpoint is not null); // :1811
        return await new Git(thread.WorktreePath ?? project.Path).PreviewRestore(baseCheckpoint, end, paths); // :1812
    }

    // :2255-2270. Swift: finishTurnUpdatingCLI(_:status:turnID:). New in B3 (PR-RUNTIME-265). A model newer than
    // the installed CLI fails with the CLI's own "version X or newer is required". That is not left to the user:
    // the turn ends, the CLI runs its own updater, and the message goes out again on the new version. Returns
    // whether the error was taken that way; the caller then neither posts it nor ends the turn.
    private bool FinishTurnUpdatingCLI(string error, TurnStatus status, Guid? turnID)
    {
        if (turnID is not { } id || Thread?.Provider is not { } provider
            || !ProviderRegistry.UpdatesOwnCLI(provider) || !ProviderRegistry.NeedsNewerCLI(error)) return false;
        // Part of the turn, so the resend's rewind takes it away with the failed attempt. UI-RUNTIME-039.
        AppendNotice(Notice.Level.Info, $"This model needs a newer {provider.DisplayName}. Updating it, then sending your message again.");
        var weakSelf = new WeakReference<ThreadRuntime>(this);
        _ = MainActor.Spawn(async () =>
        {
            if (!weakSelf.TryGetTarget(out var runtime)) return;
            await runtime.FinishTurn(status, id);
            await runtime.ResendAfterCLIUpdate(id, provider, error);
        });
        return true;
    }

    // :2272-2293. Swift: resendAfterCLIUpdate(_:provider:error:) async. New in B3 (PR-RUNTIME-266).
    private async Task ResendAfterCLIUpdate(Guid turnID, ProviderKind provider, string error)
    {
        if (App is not { } app) return;
        var command = provider == ProviderKind.Claude ? "claude update" : "codex update";
        await app.Providers.UpdateCLIIfDue(provider, force: true);
        if (await app.Providers.InstalledCLIVersion(provider) is not { } version
            || (cliResendVersions.TryGetValue(provider, out var resentAt) && resentAt == version))
        {
            // UI-RUNTIME-040. ED-193: OD-0012 F1 renames "Droppy Code" to "Marrow"; OD-0008 B6 keeps "in Terminal".
            AppendNotice(Notice.Level.Error,
                $"{error}\n\nnth could not bring {provider.DisplayName} up to date. Run `{command}` in Terminal, then send the message again.");
            return;
        }
        cliResendVersions[provider] = version;
        // The session still runs the binary that refused: the next one starts the new one.
        ReleaseSession(stop: true);
        if (Phase != RuntimePhase.Idle
            || turns.FirstOrDefault(turn => turn.Id == turnID) is not { } turn
            || turn.UserItemID is not { } itemID
            || !entryIndex.TryGetValue(itemID, out var entry)
            || entry.Item.TimelineItemContent is not TimelineItem.Content.User user
            || user.Value.IsFromHydra)
        {
            // UI-RUNTIME-041.
            AppendNotice(Notice.Level.Error, $"{error}\n\n{provider.DisplayName} is up to date now ({version}). Send the message again.");
            return;
        }
        try
        {
            await Resend(turnID, user.Value.Text, user.Value.Attachments, restoreFiles: false);
        }
        catch (Exception ex) when (SwiftTry.IsThrownError(ex))
        {
            // UI-RUNTIME-042. Foundation localizes process-launch errors; Win32Exception.Message is the
            // Windows equivalent (same per-site decision as the StartTurn catch at :1689).
            var description = ex is System.ComponentModel.Win32Exception nativeError
                ? nativeError.Message
                : SwiftErrors.LocalizedDescription(ex);
            AppendNotice(Notice.Level.Error,
                $"{provider.DisplayName} is up to date now ({version}), but the message could not be sent again: {description} Send it again.");
        }
    }

    // :1820-1831. Swift: resend(_:text:attachments:restoreFiles:) async throws.
    public async Task Resend(Guid turnID, string text, IReadOnlyList<Attachment> attachments, bool restoreFiles)
    {
        var restored = await Revert(turnID, restoreFiles); // :1821
        var edited = new ComposerDraft { Text = text, Attachments = new ValueList<Attachment>(attachments) }; // :1822
        if (!restored) // :1823-1825
        {
            Draft = edited;
            return;
        }

        var kept = Draft; // :1827
        Draft = edited; // :1828
        Send(); // :1829
        Draft = kept; // :1830
    }

    // :1836-1936. Swift: revert(to:restoreFiles:) async throws -> Bool, @discardableResult.
    public async Task<bool> Revert(Guid turnID, bool restoreFiles)
    {
        if (Phase != RuntimePhase.Idle || IsReverting)
            throw new ProviderError(new ProviderError.Case.Failed("Wait for the running turn to finish first.")); // :1838
        var app = App;
        var thread = app?.Thread(ThreadID);
        var project = thread is null ? null : app?.Project(thread.ProjectID);
        var index = turns.ToList().FindIndex(turn => turn.Id == turnID);
        if (app is null || thread is null || project is null || index < 0)
            throw new ProviderError(new ProviderError.Case.Failed("This message is no longer in the thread.")); // :1839-1842

        if (app.RunningDroppyHeads(ThreadID) > 0)
            throw new ProviderError(new ProviderError.Case.Failed("Stop the running agents before reverting this conversation.")); // :1845
        var removed = turns.Skip(index).ToArray(); // :1846
        var directory = thread.WorktreePath ?? project.Path; // :1847

        IsReverting = true; // :1849
        try
        {
            if (restoreFiles)
            {
                if (removed[0].BaseCheckpoint is not { } checkpoint)
                    throw new ProviderError(new ProviderError.Case.Failed("This turn has no file checkpoint. Choose to keep the file changes instead.")); // :1852-1854
                _ = await new Git(directory).Output(["rev-parse", "--verify", checkpoint]); // :1855
            }

            switch (thread.Provider) // :1857
            {
                case ProviderKind.Codex:
                {
                    var targetID = removed.Select(turn => turn.ProviderTurnID).FirstOrDefault(value => value is not null); // :1859
                    if (targetID is not null)
                    {
                        if (thread.ProviderSessionID is null)
                            throw new ProviderError(new ProviderError.Case.Failed("The original Codex conversation is unavailable.")); // :1860-1862
                        var provider = await EnsureSession(directory, allowNewSession: false);
                        if (provider is not CodexSession codex)
                            throw new ProviderError(new ProviderError.Case.Failed("The original Codex conversation is unavailable."));
                        var sessionID = await codex.Rollback(targetID); // :1864
                        app.UpdateThread(ThreadID, current => current with { ProviderSessionID = sessionID }); // :1865
                    }
                    else if (thread.ProviderSessionID is not null || removed.Any(turn => turn.Status != TurnStatus.Failed))
                    {
                        throw new ProviderError(new ProviderError.Case.Failed("The selected message has no Codex turn ID, so its history cannot be safely reverted.")); // :1866-1867
                    }
                    StopSession(); // :1869
                    break;
                }
                case ProviderKind.Copilot:
                {
                    if (thread.ProviderSessionID is null)
                        throw new ProviderError(new ProviderError.Case.Failed("The original Copilot conversation is unavailable.")); // :1871-1873
                    var provider = await EnsureSession(directory, allowNewSession: false);
                    if (provider is not CopilotSession copilot)
                        throw new ProviderError(new ProviderError.Case.Failed("The original Copilot conversation is unavailable."));
                    await copilot.Rollback(removed.Length); // :1875
                    StopSession(); // :1876
                    break;
                }
                case ProviderKind.Claude:
                {
                    ReleaseSession(stop: true); // :1878
                    if (index == 0)
                    {
                        StopSession(); // :1880
                        app.UpdateThread(ThreadID, current => current with { ProviderSessionID = null, ProviderResumeAt = null }); // :1881-1884
                    }
                    else
                    {
                        if (thread.ProviderSessionID is null || turns[index - 1].ProviderAnchor is not { } anchor)
                            throw new ProviderError(new ProviderError.Case.Failed("Claude has no resume point before this message. The conversation was kept.")); // :1886-1887
                        StopSession(); // :1889
                        app.UpdateThread(ThreadID, current => current with { ProviderResumeAt = anchor }); // :1890
                    }
                    break;
                }
                default:
                    ReleaseSession(stop: true); // :1897
                    app.UpdateThread(ThreadID, current => current with { ProviderSessionID = null, ProviderResumeAt = null }); // :1898-1901
                    break;
            }

            string? fileError = null; // :1904
            if (restoreFiles && removed[0].BaseCheckpoint is { } baseCheckpoint)
            {
                try
                {
                    var preview = await PreviewRevert(turnID); // :1908
                    await new Git(directory).Restore(preview.Restorable.Select(file => file.Path).ToArray(), baseCheckpoint); // :1909
                }
                catch (Exception error)
                {
                    fileError = "Conversation reverted, but files could not be restored: " + SwiftErrors.LocalizedDescription(error); // :1911
                }
            }

            var removedIDs = removed.Select(turn => turn.Id).ToHashSet(); // :1915
            Entries = new ValueList<TimelineEntry>(Entries.Where(entry => entry.TurnID is not { } owner || !removedIDs.Contains(owner))); // :1916-1919
            entryIndex.Clear(); // :1920
            foreach (var entry in Entries) entryIndex.Add(entry.Id, entry);
            Turns = new ValueList<TurnRecord>(Turns.Take(index)); // :1921
            hydraTouched.Clear(); // :1924
            HydraCurrentTurnTouchedPaths = new ValueSet<string>(Array.Empty<string>(), SwiftStringComparer.Instance); // :1925
            app.UpdateThread(ThreadID, current => current with { LastStatus = Turns.Count == 0 ? null : Turns[^1].Status }); // :1928
            saveRevision++; // :1929
            Usage = null; // :1930
            if (fileError is not null) AppendNotice(Notice.Level.Error, fileError); // :1931
            DiffSelection = null; // :1932
            DiffRevision++; // :1933
            ScheduleSave(); // :1934
            return fileError is null; // :1935; GD-003 preserves cited Swift behavior despite conflicting packet prose/test.
        }
        finally
        {
            IsReverting = false;
        }
    }

    // :1939
    public FileChangeSummary? ChangeStats { get => Read(changeStats); private set => Set(ref changeStats, value); }

    // :1945-1953
    public async Task RefreshChangeStats()
    {
        var revision = DiffRevision; // :1946
        if (changeStatsRevision == revision) return; // :1947
        var files = await ParsedDiff(selection: null); // :1948
        if (revision != DiffRevision) return; // :1949
        var stats = files.Count == 0 ? null : FileChangeSummaryDiff.Create(files); // :1950
        if (stats != ChangeStats) ChangeStats = stats; // :1951
        changeStatsRevision = revision; // :1952
    }

    // :1964-1979. Only concurrent callers share this repository-root lookup.
    private Task<string> RepositoryRoot(Git git)
    {
        var directory = git.Directory;
        if (repositoryRoots.TryGetValue(directory, out var existing)) return existing;

        repositoryRoots.Clear();
        TaskHandle<string> lookup = null!;
        lookup = MainActor.Spawn(async () =>
        {
            var repository = directory;
            try
            {
                var prefix = await git.Output(["rev-parse", "--show-prefix"]);
                foreach (var _ in prefix.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries))
                    repository = Path.GetDirectoryName(repository) ?? repository;
            }
            catch
            {
                // Swift's try? leaves the checkout path as the root when this lookup fails.
            }

            if (repositoryRoots.TryGetValue(directory, out var current) && ReferenceEquals(current, lookup.Value))
                repositoryRoots.Remove(directory);
            return repository;
        });
        repositoryRoots[directory] = lookup.Value;
        return lookup.Value;
    }

    // :1982-1998
    private Guid? FixedTurn(Guid? selection) =>
        selection is { } id && turns.Any(turn => turn.Id == id && turn.Status != TurnStatus.Running) ? id : null;

    private Task<ValueList<DiffFile>>? CachedDiff(Guid? selection)
    {
        if (FixedTurn(selection) is { } turnID)
            return turnDiffs.TryGetValue(turnID, out var fixedTask) ? fixedTask : null;
        var key = selection?.ToString() ?? "all";
        return revisionDiffs.TryGetValue(key, out var revisionTask) ? revisionTask : null;
    }

    private void CacheDiff(Task<ValueList<DiffFile>> task, Guid? selection)
    {
        if (FixedTurn(selection) is { } turnID) turnDiffs.Insert(task, turnID);
        else revisionDiffs.Insert(task, selection?.ToString() ?? "all");
    }

    // :2000-2002. Swift: hasCachedDiff(selection:).
    public bool HasCachedDiff(Guid? selection) => CachedDiff(selection) is not null;

    // :2004-2011
    public ValueList<TurnRecord> TurnsWithChanges()
    {
        var edited = new HashSet<Guid>(); // :2005
        foreach (var entry in Entries)
        {
            if (entry.TurnID is { } id && entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool
                && tool.Value.Edits.Count != 0)
                edited.Add(id); // :2006-2009
        }
        return new ValueList<TurnRecord>(Turns.Where(turn => turn.EndCheckpoint is not null
            || turn.ProviderDiff is not null
            || turn.TouchedPaths is { Count: > 0 }
            || edited.Contains(turn.Id))); // :2010
    }

    // :2013-2050. Swift: parsedDiff(selection:providerFiles:).
    public Task<ValueList<DiffFile>> ParsedDiff(Guid? selection, IReadOnlyList<DiffFile>? providerFiles = null)
    {
        if (CachedDiff(selection) is { } cached) return cached; // :2014
        var app = App;
        var thread = app?.Thread(ThreadID);
        var project = thread is null ? null : app?.Project(thread.ProjectID);
        if (app is null || thread is null || project is null) return Task.FromResult(ValueList<DiffFile>.Empty); // :2015

        var root = thread.WorktreePath ?? project.Path; // :2016
        var git = new Git(root);
        var repository = RepositoryRoot(git); // :2018
        var selectedTurns = selection is { } selected
            ? Turns.Where(turn => turn.Id == selected).ToList()
            : Turns.ToList(); // :2019
        if (selectedTurns.Count == 0) return Task.FromResult(ValueList<DiffFile>.Empty); // :2020

        var selectedIDs = selectedTurns.Select(turn => turn.Id).ToHashSet(); // :2021
        var projectRoots = app.Projects.Select(candidate => candidate.Path).ToArray(); // :2026
        var edits = Entries.SelectMany(entry =>
        {
            if (entry.TurnID is not { } id || !selectedIDs.Contains(id)
                || entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool)
                return Array.Empty<FileEdit>();
            return tool.Value.Edits.Where(edit => IsProjectFile(edit.Path, root, projectRoots)).ToArray(); // :2027-2029
        }).ToArray();
        var touched = selectedTurns.SelectMany(turn => turn.TouchedPaths ?? ValueList<string>.Empty)
            .Select(path => TouchedPaths.Normalize(path, root))
            .ToHashSet(SwiftStringComparer.Instance); // :2035

        (string First, string Last)? span = null;
        var ended = selectedTurns.FindLastIndex(turn => turn.EndCheckpoint is not null); // :2037
        if (ended >= 0 && selectedTurns.Take(ended + 1).FirstOrDefault(turn => turn.BaseCheckpoint is not null)?.BaseCheckpoint is { } first
            && selectedTurns[ended].EndCheckpoint is { } last)
            span = (first, last); // :2037-2040

        var task = TaskHandle.Detached(async () =>
        {
            string? snapshot = null;
            if (span is { } trees)
            {
                try { snapshot = await git.Diff(trees.First, trees.Last); } // :2042-2044
                catch { }
            }
            var providerPatch = providerFiles is null
                ? string.Join("\n", selectedTurns.Where(turn => turn.ProviderDiff is not null).Select(turn => turn.ProviderDiff!))
                : ""; // :2045
            return ResolveDiff(await repository, snapshot, providerPatch, providerFiles, edits, touched, root); // :2046
        }).Value;
        CacheDiff(task, selection); // :2048
        return task; // :2049
    }

    // :2052-2056
    private static bool IsProjectFile(string path, string root, IReadOnlyList<string> projects) =>
        TouchedPaths.Relative(path, root) is not null || projects.Any(project => TouchedPaths.Relative(path, project) is not null);

    // :2058-2060
    private static ValueList<DiffFile> ResolveDiff(string repositoryRoot, string? snapshot, string providerPatch,
        IReadOnlyList<DiffFile>? providerFiles, IReadOnlyList<FileEdit> edits, IReadOnlyCollection<string> touched, string root) =>
        TurnDiff.Merge(snapshot is null ? null : DiffParser.Parse(snapshot), providerPatch, edits, touched, root, repositoryRoot, providerFiles);

    // ThreadRuntime.swift:1610-1617.
    private void RecordSessionID(string sessionID)
    {
        if (guardedStartingSession is not null && ReferenceEquals(session, guardedStartingSession)) return;
        if (App is not { } app) return;
        if (app.Thread(ThreadID)?.ProviderSessionID is null)
        {
            unsentSessionID = sessionID;
        }
        else
        {
            app.UpdateThread(ThreadID, thread => thread with { ProviderSessionID = sessionID, ProviderAccountID = sessionSignature?.AccountId ?? thread.ProviderAccountID });
        }
    }

    // ThreadRuntime.swift:1619-1633.
    private static bool StartsOver(Exception error, ChatThread thread) =>
        thread.ProviderSessionID is not null
        && (LostConversation(error) || thread.ProviderResumeAt is null);

    private static bool LostConversation(Exception error)
    {
        var text = SwiftErrors.LocalizedDescription(error);
        return text.Contains("no conversation found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no longer has this conversation", StringComparison.OrdinalIgnoreCase);
    }

    // ThreadRuntime.swift:2070-2077.
    public UsageAccountKey? UsageAccount { get; private set; }
    private Action<ProviderEvent>? observedSessionEvent;

    public void Observe(ProviderSession provider) => Observe(provider, CurrentUsageAccount());

    // PORT-ONLY: ED-403. Observe freezes the actual source identity, including explicit account GUIDs.
    private UsageAccountKey? CurrentUsageAccount() => Thread is not { } thread ? null
        : thread.ProviderAccountID is { } id
            && App?.Settings.Accounts.Accounts.FirstOrDefault(account => account.Id == id)?.UsesLegacyCredentials != true
            ? new UsageAccountKey(thread.Provider, id.ToString("D")) : UsageAccountKey.Default(thread.Provider);

    // OD-0114: freeze the source at emission. Live API selection advances UsageAccount before its request; epoch guards reject retired sessions.
    public void Observe(ProviderSession provider, UsageAccountKey? account)
    {
        UsageAccount = account;
        sessionEpoch++;
        var epoch = sessionEpoch;
        var weakSelf = new WeakReference<ThreadRuntime>(this);
        Action<Marrow.Core.Port.Skills.Detect.ToolEvent> skills = value =>
        {
            if (weakSelf.TryGetTarget(out var runtime) && runtime.sessionEpoch == epoch)
                runtime.SkillsOffers?.Observe(value);
        };
        switch (provider)
        {
            case ClaudeSession claude: claude.OnSkillEvent = skills; break;
            case CodexSession codex: codex.OnToolEvent = skills; break;
            case CopilotSession copilot: copilot.OnSkillsEvent = skills; break;
            case ACPSession acp: acp.OnSkillsEvent = skills; break;
            case PiSession pi: pi.ToolEvents = skills; break;
            case CommandCodeSession command: command.ToolEvents = skills; break;
            case OpenAICompatibleSession api:
                api.OnToolEvent = skills;
                api.ContinueAfterAccountStep = ct => weakSelf.TryGetTarget(out var owner) && owner.sessionEpoch == epoch
                    ? owner.ContinueAfterAccountStepAsync(api, epoch, ct) : Task.FromResult(false);
                break;
        }
        provider.OnEvent = observedSessionEvent = @event =>
        {
            if (weakSelf.TryGetTarget(out var runtime) && runtime.sessionEpoch == epoch)
                runtime.Handle(@event is ProviderEvent.UsageLimit limit
                    ? limit with { Account = provider is OpenAICompatibleSession { KeyRotation: AppApiKeyRotation } ? runtime.UsageAccount : account }
                    : @event);
        };
    }

    // ThreadRuntime.swift:2082-2096.
    private void ReleaseSession(bool stop)
    {
        sessionEpoch++;
        sessionSignature = null;
        unsentSessionID = null;
        antiSlopFallbackSent = false;
        continuationSent = false;
        if (session is not { } old) return;
        session = null;
        try { if (stop) old.Stop(); }
        finally { ReleaseSessionSkills(old); }
        _ = MainActor.Spawn(() =>
        {
            old.OnEvent = null;
            return Task.CompletedTask;
        });
    }

    private void ReleaseSessionSkills(ProviderSession provider)
    {
        if (!sessionSkills.Remove(provider, out var skills)) return;
        var exited = provider is Marrow.Services.Port.Skills.Providers.ISkillSessionLifetime lifetime
            ? lifetime.SkillReadersExited : Task.CompletedTask;
        if (exited.IsCompleted) skills.Dispose();
        else pendingSkillRelease = Task.WhenAll(pendingSkillRelease, ReleaseAfterExit());

        async Task ReleaseAfterExit()
        {
            try { await exited; }
            finally { skills.Dispose(); }
        }
    }

    // ThreadRuntime.swift:2098-2115.
    public void StopSession()
    {
        App?.DetachAccountRouting(ThreadID);
        ReleaseSession(stop: true);
        StopNativeHeads();
        if (Phase == RuntimePhase.Idle) return;
        SaveNow();
        var turnID = currentTurnID;
        currentTurnID = null;
        _ = MainActor.Spawn(() => FinishTurn(TurnStatus.Interrupted, turnID));
    }

    // MARK: - Hydra

    // :2485-2490
    public bool IsReadyForHydraMerge =>
        Phase == RuntimePhase.Idle && !IsLoadingHistory && !IsReverting && pendingSend is null
            && Turns.Count != 0 && Turns[^1].Status == TurnStatus.Completed && !HasWorkingHeads
            && hydraPendingReports.Count == 0 && hydraBatches.Count == 0 && hydraWaiting.Count == 0
            && !hydraFlushScheduled; // :2485-2490

    // :2494-2504
    public void MarkHydraMerged(IReadOnlyList<Guid> turnIDs)
    {
        var ids = turnIDs.ToHashSet(); // ThreadRuntime.swift:2495
        var changed = false;
        var updated = turns.ToArray();
        for (var index = 0; index < updated.Length; index++)
        {
            if (!ids.Contains(updated[index].Id) || updated[index].HydraMerged) continue;
            updated[index] = updated[index] with { HydraMerged = true };
            changed = true;
        }
        if (!changed) return; // ThreadRuntime.swift:2501
        Turns = new ValueList<TurnRecord>(updated);
        saveRevision = checked(saveRevision + 1); // ThreadRuntime.swift:2502
        SaveNow(); // ThreadRuntime.swift:2503
    }

    // :2507-2509
    public ValueList<TurnRecord> HydraUnmergedTurns =>
        new(turns.Where(turn => !turn.HydraMerged)); // ThreadRuntime.swift:2507-2509

    // :2512-2517
    public void RecordHydraMerge(HydraMergeRecord record)
    {
        HydraMerges = new ValueList<HydraMergeRecord>(hydraMerges.Append(record).TakeLast(20)); // ThreadRuntime.swift:2513-2514
        saveRevision = checked(saveRevision + 1); // ThreadRuntime.swift:2515
        SaveNow(); // ThreadRuntime.swift:2516
    }

    // :2520-2522
    public long HydraUnmergedFileCount =>
        HydraUnmergedTurns.Where(turn => turn.Status != TurnStatus.Running)
            .Where(turn => turn.TouchedPaths is not null)
            .SelectMany(turn => turn.TouchedPaths!)
            .ToHashSet(SwiftStringComparer.Instance)
            .Count; // ThreadRuntime.swift:2520-2522

    // :2525-2527. Swift: hydraHead(forTool:).
    public Guid? HydraHead(string toolID) =>
        HydraToolHeads.TryGetValue(toolID, out var headID) ? headID : null;

    // :2530-2532. Swift: rehearseHydraHead(toolID:headID:).
    public void RehearseHydraHead(string toolID, Guid headID) =>
        HydraToolHeads = HydraToolHeads.Mutating(map => map[toolID] = headID);

    // :2651-2654. Swift: stopNativeHead(_:).
    public async Task<bool> StopNativeHead(string nativeID)
    {
        if (session is not { } current) return false;
        return await current.StopAgent(nativeID);
    }

    // :2660-2695. Swift: hydraHeadFinished(_:info:status:summary:landing:copyPath:).
    public void HydraHeadFinished(Guid headID, HydraHeadInfo info, HydraHeadInfo.Status status, string? summary,
        HydraLanding? landing = null, string? copyPath = null)
    {
        App?.AssistantTaps.TapHeadFinished(ThreadID, headID, info, status, summary); // PORT-ONLY (AS-2, ED-NEW-dextra-as2-8): the Assistant's live feed
        if (info.HydraHeadInfoKind == HydraHeadInfo.Kind.Native)
        {
            if (info.ToolUseID is not { } toolID
                || !entryIndex.TryGetValue(toolID, out var entry)
                || entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool
                || tool.Value.ToolCallStatus != ToolCall.Status.Running) return;

            var outcome = status == HydraHeadInfo.Status.Completed
                ? ToolCall.Status.Completed
                : ToolCall.Status.Failed;
            ApplyToolUpdate(toolID, new ToolUpdate { Output = summary, Status = outcome });
            FlushFollowUpsIfIdle();
            return;
        }

        if (info.HydraHeadInfoKind != HydraHeadInfo.Kind.Droppy) return;
        var report = new HydraReport
        {
            HeadIndex = info.Index,
            Task = info.Task,
            Origin = info.HydraHeadInfoOrigin,
            Status = status,
            Text = summary ?? "",
            Landing = landing,
            CopyPath = copyPath,
            Elapsed = SwiftDate.TimeIntervalSince(SwiftDate.Now, info.StartedAt),
            ToolCalls = info.ToolCalls,
        };
        hydraPendingReports.RemoveAll(candidate => candidate.HeadIndex == info.Index);
        if (info.BatchID is { } batchID && hydraBatches.TryGetValue(batchID, out var batch))
        {
            var reports = batch.Reports.Where(candidate => candidate.HeadIndex != info.Index).Append(report);
            hydraBatches[batchID] = batch with
            {
                Pending = batch.Pending.Mutating(set => set.Remove(headID)),
                Reports = new ValueList<HydraReport>(reports),
            };
        }
        else
        {
            hydraPendingReports.Add(report);
        }

        SpawnWaitingHeads();
        DispatchQueuedFollowUps();
        SettleBatches();
        if (phase == RuntimePhase.Idle && Thread?.LastStatus != TurnStatus.Interrupted)
            FlushHydraReports();
        FlushFollowUpsIfIdle();
    }

    // :1057-1072. The flag keeps multiple idle arrivals from taking the same queued prompt.
    private void FlushFollowUpsIfIdle()
    {
        if (phase != RuntimePhase.Idle || followUpFlushScheduled || followUps.Count == 0
            || Thread?.LastStatus == TurnStatus.Interrupted) return;
        followUpFlushScheduled = true;
        _ = MainActor.Spawn(StartQueuedFollowUpTurn);
    }

    // :1066-1072.
    private Task StartQueuedFollowUpTurn()
    {
        followUpFlushScheduled = false;
        if (phase != RuntimePhase.Idle) return Task.CompletedTask;
        DrainFollowUps(TurnStatus.Completed);
        return Task.CompletedTask;
    }

    // :2696-2702. Snapshot before removals so no batch is changed under dictionary enumeration.
    private void SettleBatches()
    {
        foreach (var (batchID, batch) in hydraBatches.ToArray())
        {
            if (batch.Pending.Count != 0 || hydraWaiting.Any(waiting => waiting.BatchID == batchID)) continue;
            hydraBatches.Remove(batchID);
            hydraPendingReports.AddRange(batch.Reports.OrderBy(report => report.HeadIndex));
        }
    }

    // :2717-2723. Batches are filtered before reports reach this queue; every waiting report is flushable.
    private IReadOnlyList<HydraReport> FlushableHydraReports() =>
        hydraPendingReports.OrderBy(report => report.HeadIndex).ToArray();

    // :2707-2715.
    private bool FlushHydraReports()
    {
        if (phase != RuntimePhase.Idle || hydraFlushScheduled || FlushableHydraReports().Count == 0) return false;
        hydraFlushScheduled = true;
        AutoStartedTurnToken = unchecked(AutoStartedTurnToken + 1);
        _ = MainActor.Spawn(StartHydraReportTurn);
        return true;
    }

    // :2727-2741.
    private Task StartHydraReportTurn()
    {
        hydraFlushScheduled = false;
        if (phase != RuntimePhase.Idle || isReverting || App is null) return Task.CompletedTask;
        var reports = FlushableHydraReports();
        if (reports.Count == 0) return Task.CompletedTask;

        var sent = reports.Select(report => report.HeadIndex).ToHashSet();
        hydraPendingReports.RemoveAll(report => sent.Contains(report.HeadIndex));
        var stillWorking = App.WorkingHydraHeadNames(ThreadID, sent);
        var text = HydraPrompts.ReportMessage(reports, stillWorking, App.Settings.HydraReviewHeads);
        return StartHydraReportTurnCore(text, reports.Select(report => report.HeadIndex).ToArray());
    }

    private async Task StartHydraReportTurnCore(string text, IReadOnlyList<long> headIndexes)
    {
        await EnsureLoaded();
        await StartTurn(text, Array.Empty<Attachment>(), headIndexes);
    }
    // :2759-2784.
    private void DropDelegationBlock(Guid turnID, TurnStatus status)
    {
        if (hydraStream is { } stream && stream.TurnID == turnID && stream.Sent > 0
            && entryIndex.TryGetValue(stream.EntryID, out var streamedEntry)
            && streamedEntry.Item.TimelineItemContent is TimelineItem.Content.Assistant streamedAssistant)
        {
            EndDelegationStream();
            var text = HydraPrompts.WithoutDelegationBlock(streamedAssistant.Value.Text);
            if (text.Length == 0) text = "Asked for heads.";
            streamedEntry.Item = streamedEntry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Assistant(streamedAssistant.Value with { Text = text }),
            };
            saveRevision += 1;
            ScheduleSave();
            SettleBatches();
            AppendHydraNote($"Hydra sent {(stream.Sent == 1 ? "one head" : $"{stream.Sent} heads")} before the turn {(status == TurnStatus.Interrupted ? "was stopped" : "failed")}; the rest of the block never went out. Ask again to send them.");
            return;
        }

        if (hydraStream is { } emptyStream && emptyStream.TurnID == turnID && emptyStream.Sent == 0)
            EndDelegationStream();

        TimelineEntry? entry = null;
        for (var index = Entries.Count - 1; index >= 0; index--)
        {
            var candidate = Entries[index];
            if (candidate.TurnID == turnID && candidate.TimelineEntryKind == TimelineEntry.Kind.Assistant
                && HoldsDelegationBlock(candidate))
            {
                entry = candidate;
                break;
            }
        }
        if (entry is null || entry.Item.TimelineItemContent is not TimelineItem.Content.Assistant assistant) return;

        var stripped = HydraPrompts.WithoutDelegationBlock(assistant.Value.Text);
        if (stripped.Length == 0) stripped = "Asked for heads.";
        entry.Item = entry.Item with
        {
            TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { Text = stripped }),
        };
        saveRevision += 1;
        ScheduleSave();
        AppendHydraNote($"Hydra sent no heads: the turn {(status == TurnStatus.Interrupted ? "was stopped" : "failed")} before they could go out. Ask again to send them.");
        hydraBudget = hydraBudget.NoteDropped(HydraPrompts.InterruptedBlockMessage(status == TurnStatus.Interrupted));
    }

    // :2786-2789.
    private static bool HoldsDelegationBlock(TimelineEntry entry) =>
        entry.Item.TimelineItemContent is TimelineItem.Content.Assistant assistant
        && HydraPrompts.HasDelegationBlock(assistant.Value.Text);

    private const int MaxDelegatedTasks = 32; // ThreadRuntime.swift:2743-2745

    // ThreadRuntime.swift:2757-2890.
    private DelegationOutcome SpawnDelegatedHeads(Guid turnID)
    {
        if (App is not { } app || app.Thread(ThreadID) is not { } thread
            || app.HydraLaunch(thread) is not { } launch) return DelegationOutcome.None;

        var assistantRows = Entries.Where(entry => entry.TurnID == turnID
            && entry.Item.TimelineItemContent is TimelineItem.Content.Assistant).ToArray();
        var entry = assistantRows.LastOrDefault(HoldsDelegationBlock) ?? assistantRows.LastOrDefault();
        if (entry?.Item.TimelineItemContent is not TimelineItem.Content.Assistant assistant)
            return DelegationOutcome.None;

        scanStreamedDelegations(hydraStream?.EntryID ?? entry.Id, turnID, assistant.Value.Text);

        if (hydraStream is { } stream && stream.TurnID == turnID
            && entryIndex.TryGetValue(stream.EntryID, out var streamedEntry)
            && streamedEntry.Item.TimelineItemContent is TimelineItem.Content.Assistant streamedAssistant)
        {
            // ThreadRuntime.swift:2851: `hydraStream = nil` only. The delegation parser stays until the next startTurn
            // (PR-RUNTIME-261); endDelegationStream() would have cleared it too.
            hydraStream = null;
            var sentText = HydraPrompts.MarkingDelegationBlockSent(streamedAssistant.Value.Text);
            streamedEntry.Item = streamedEntry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Assistant(streamedAssistant.Value with { Text = sentText }),
            };
            saveRevision += 1;

            // :2855 HydraPrompts.streamedDelegations(in:)?.delegations ?? []: the streaming reader, which counts every closed
            // task of a block still open (cut off, or one closing fence short), unlike delegations(in:), which needs the whole block.
            var all = HydraPrompts.StreamedDelegationsIn(streamedAssistant.Value.Text)?.Delegations ?? ValueList<HydraDelegation>.Empty;
            var total = all.Count == 0 ? stream.Sent : all.Count;
            var sent = stream.Sent;
            if (total > sent && hydraBudget.CanDelegate)
            {
                var skip = (int)Math.Min(sent, all.Count);
                var take = (int)Math.Max(0, MaxDelegatedTasks - sent);
                var rest = all.Skip(skip).Take(take).ToArray();
                if (rest.Length != 0)
                {
                    hydraWaiting = hydraWaiting.Concat(rest.Select(delegation =>
                        (delegation, stream.BatchID, SwiftDate.Now))).ToList();
                    sent += rest.Length;
                    hydraStream = (turnID, stream.EntryID, stream.BatchID, sent);
                    SpawnWaitingHeads(launch);
                }
            }

            if (total > sent)
                AppendHydraNote($"Not every task went out\n{sent} of the {total} tasks in that block went to heads. The last {total - sent} did not: ask for them again once these report back.");
            HoldSentDelegationBlock(stream.EntryID);
            SettleBatches();
            ScheduleSave();
            return hydraBatches.Count == 0 && hydraWaiting.Count == 0
                ? DelegationOutcome.None
                : DelegationOutcome.HeadsOut;
        }

        if (!HydraPrompts.HasDelegationBlock(assistant.Value.Text)) return DelegationOutcome.None;
        var delegations = HydraPrompts.Delegations(assistant.Value.Text);
        var stripped = HydraPrompts.WithoutDelegationBlock(assistant.Value.Text);
        if (delegations is null)
        {
            if (stripped.Length == 0) stripped = "Asking for heads.";
            entry.Item = entry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { Text = stripped }),
            };
            saveRevision += 1;
            ScheduleSave();
            return RefuseBlock(HydraPrompts.UnreadableBlockMessage(null),
                "Its delegation block could not be read, and it had been told so already.");
        }

        if (delegations.Count == 0)
        {
            if (stripped.Length == 0) stripped = "Done, with no heads.";
            entry.Item = entry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { Text = stripped }),
            };
            saveRevision += 1;
            ScheduleSave();
            AppendHydraNote("No heads went out: the lead did this itself.");
            return DelegationOutcome.None;
        }

        if (!hydraBudget.CanDelegate)
        {
            if (stripped.Length == 0) stripped = "Asking for more heads.";
            entry.Item = entry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { Text = stripped }),
            };
            saveRevision += 1;
            ScheduleSave();
            // :2908 heldBackMessage(count: delegations.count): the number of heads asked for, not the round cap.
            return RefuseBlock(HydraPrompts.HeldBackMessage(delegations.Count),
                $"This request has had all {HydraPrompts.MaxDelegationRounds} of its rounds of heads, and the lead had been told so already.");
        }

        hydraBudget = hydraBudget.ChargeRound();
        var markedText = HydraPrompts.MarkingDelegationBlockSent(assistant.Value.Text);
        entry.Item = entry.Item with
        {
            TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { Text = markedText }),
        };
        saveRevision += 1;
        var batchID = Guid.NewGuid();
        hydraBatches[batchID] = new HydraBatch { Pending = ValueSet<Guid>.Empty };
        var sentDelegations = delegations.Take(MaxDelegatedTasks).ToArray();
        hydraWaiting = hydraWaiting.Concat(sentDelegations.Select(delegation =>
            (delegation, batchID, SwiftDate.Now))).ToList();
        if (delegations.Count > MaxDelegatedTasks)
        {
            var dropped = delegations.Count - MaxDelegatedTasks;
            AppendHydraNote($"Not every task went out\n{MaxDelegatedTasks} of the {delegations.Count} tasks in that block went to heads. The last {dropped} did not: ask for them again once these report back.");
        }

        SpawnWaitingHeads(launch);
        HoldSentDelegationBlock(entry.Id);
        SettleBatches();
        ScheduleSave();
        return hydraBatches.Count == 0 && hydraWaiting.Count == 0
            ? DelegationOutcome.None
            : DelegationOutcome.HeadsOut;
    }
    // ThreadRuntime.swift:2893-2915.
    private void HoldSentDelegationBlock(string entryID)
    {
        var weakSelf = new WeakReference<ThreadRuntime>(this);
        _ = MainActor.Spawn(async () =>
        {
            await Task.Delay(HydraPrompts.DelegationSentHold);
            if (!weakSelf.TryGetTarget(out var self)
                || !self.entryIndex.TryGetValue(entryID, out var entry)
                || entry.Item.TimelineItemContent is not TimelineItem.Content.Assistant assistant
                || !HydraPrompts.HasSentDelegationBlock(assistant.Value.Text)) return;

            var leaving = assistant.Value with
            {
                Text = HydraPrompts.MarkingDelegationBlockLeaving(assistant.Value.Text),
            };
            Transaction.WithAnimation(Animation.EaseOut(duration: 0.4), () =>
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(leaving) });

            await Task.Delay(HydraPrompts.DelegationLeaveFade);
            if (entry.Item.TimelineItemContent is not TimelineItem.Content.Assistant faded
                || !HydraPrompts.HasSentDelegationBlock(faded.Value.Text)) return;

            var text = HydraPrompts.WithoutDelegationBlock(faded.Value.Text);
            if (text.Length == 0) text = "Sent out heads.";
            var completed = faded.Value with { Text = text };
            Transaction.WithAnimation(Chrome.PanelSlide, () =>
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(completed) });
            if (!weakSelf.TryGetTarget(out self)) return;
            self.saveRevision += 1;
            self.ScheduleSave();
        });
    }

    // ThreadRuntime.swift:2917-2931.
    private DelegationOutcome RefuseBlock(string message, string dropped)
    {
        var (nextBudget, counted) = hydraBudget.Refuse(message);
        hydraBudget = nextBudget;
        if (!counted)
        {
            AppendHydraNote($"No heads went out\n{dropped} The block was dropped from the reply.");
            return DelegationOutcome.None;
        }

        _ = MainActor.Spawn(async () =>
        {
            await EnsureLoaded();
            await StartTurn(message, Array.Empty<Attachment>(), Array.Empty<long>());
        });
        return DelegationOutcome.TurnStarted;
    }

    // ThreadRuntime.swift:2943-2973.
    private void scanStreamedDelegations(string entryID, Guid turnID, string text, bool isDelta = false)
    {
        if (App is not { } app || app.Thread(ThreadID) is not { } thread
            || app.HydraLaunch(thread) is not { } launch) return;
        if (!entryIndex.TryGetValue(entryID, out var entry) || entry.TurnID != turnID) return;
        if (hydraStream is { } active && (active.EntryID != entryID || active.TurnID != turnID)) return;

        var parser = isDelta && delegationParser is { } current
            && current.EntryID == entryID && current.TurnID == turnID
                ? current
                : new HydraDelegationStreamParser(turnID, entryID);
        parser.Ingest(text);
        delegationParser = parser;

        var sent = hydraStream?.Sent ?? 0;
        var skip = (int)Math.Min(sent, parser.Delegations.Count);
        var take = (int)Math.Max(0, MaxDelegatedTasks - sent);
        var fresh = parser.Delegations.Skip(skip).Take(take).ToArray();
        if (fresh.Length == 0) return;
        if (hydraStream is null && !hydraBudget.CanDelegate) return;

        Guid batchID;
        if (hydraStream is { } stream)
        {
            batchID = stream.BatchID;
        }
        else
        {
            batchID = Guid.NewGuid();
            hydraBatches[batchID] = new HydraBatch { Pending = ValueSet<Guid>.Empty };
            hydraBudget = hydraBudget.ChargeRound();
            sent = 0;
        }

        hydraWaiting = hydraWaiting.Concat(fresh.Select(delegation =>
            (delegation, batchID, SwiftDate.Now))).ToList();
        hydraStream = (turnID, entryID, batchID, sent + fresh.Length);
        SpawnWaitingHeads(launch);
    }

    // ThreadRuntime.swift:2983-3007.
    private void SpawnWaitingHeads(HydraLaunch? launch = null)
    {
        if (hydraWaiting.Count == 0 || App is not { } app || app.Thread(ThreadID) is not { } thread) return;
        var resolvedLaunch = launch ?? app.HydraLaunch(thread);

        if (app.HydraPair(thread) is { } pair
            && app.HydraHeadsFallbackNote(pair) is { } fallback
            && hydraFallbackNoted != pair.Id)
        {
            hydraFallbackNoted = pair.Id;
            AppendHydraNote("Hydra: " + fallback);
        }

        while (hydraWaiting.Count != 0
            && (resolvedLaunch?.HasRoom(app.RunningDroppyHeads(ThreadID)) ?? true))
        {
            var next = hydraWaiting[0];
            hydraWaiting = hydraWaiting.Skip(1).ToList();
            var delegation = next.Delegation;
            var head = app.SpawnDroppyHead(
                ThreadID,
                delegation.Task,
                HydraHeadInfo.Origin.Delegated,
                Array.Empty<Attachment>(),
                next.BatchID,
                delegation.Name is { } name ? HydraRoster.Index(name) : null,
                delegation.Project,
                delegation.Profile,
                (persona, workplace) => HydraPrompts.DelegatedHeadPrompt(persona, delegation, workplace));
            if (head is null) continue;

            app.UpdateThread(head.Id, current => current.Hydra is { } info
                ? current with { Hydra = info with { QueuedAt = next.QueuedAt } }
                : current);
            var batch = hydraBatches.TryGetValue(next.BatchID, out var existing)
                ? existing
                : new HydraBatch { Pending = ValueSet<Guid>.Empty };
            hydraBatches[next.BatchID] = batch with { Pending = batch.Pending.Mutating(ids => ids.Add(head.Id)) };
        }
    }
    // MARK: - Timeline mutations

    // :3014-3018. Swift: appendHydraNote(_:id:).
    public void AppendHydraNote(string text, string? id = null)
    {
        Append(new TimelineItem(null, new TimelineItem.Content.User(new UserMessage
        {
            Text = text,
            HydraHeads = ValueList<long>.Empty,
        }), id));
        if (appReference is not null && appReference.TryGetTarget(out var app))
            app.UpdateThread(ThreadID, thread => thread with { UpdatedAt = SwiftDate.Now });
        ScheduleSave();
    }

    // :3501-3521. One edit per file section; parse concurrently, preserving the section as the diff.
    public static Task<ValueList<FileEdit>> FileEdits(string patch, SwiftURL repositoryRoot) => Task.Run(() =>
    {
        var sections = new List<string>();
        var current = new List<string>();
        foreach (var line in patch.Split('\n', StringSplitOptions.None))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                if (current.Count > 0) sections.Add(string.Join("\n", current));
                current = [line];
            }
            else if (current.Count > 0)
            {
                current.Add(line);
            }
        }
        if (current.Count > 0) sections.Add(string.Join("\n", current));

        var edits = new List<FileEdit>(sections.Count);
        foreach (var section in sections)
        {
            var files = DiffParser.Parse(section);
            if (files.Count == 0) continue;
            var file = files[0];
            var diff = System.Text.Encoding.UTF8.GetByteCount(section) > EditDiffLimit ? null : section;
            edits.Add(new FileEdit(repositoryRoot.Appending(file.Path).Path, diff,
                file.Additions, file.Deletions));
        }

        return new ValueList<FileEdit>(edits);
    });

    // :3341-3362. A command watch starts only from a tree that was complete before the command.
    private void WatchCommand(string id, ToolCall call)
    {
        if (commandTrees.ContainsKey(id) || !ShellCommandKind.MayWriteFiles(call.Title)
            || RepositoryGit is null) return;
        var before = TreeBeforeCommand();
        if (before is null) return;
        commandTrees[id] = before;
        settledTree = null;
        treeEpoch++;
    }

    private string? TreeBeforeCommand()
    {
        if (settledTree is { } settled && settled.Epoch == treeEpoch) return settled.Tree;
        if (latestTree is not null) return latestTree;
        return currentTurnID is { } turnID
            ? Turns.FirstOrDefault(turn => turn.Id == turnID)?.BaseCheckpoint
            : null;
    }

    private Git? RepositoryGit
    {
        get
        {
            if (Thread is not { } thread || App?.Project(thread.ProjectID) is not { } project) return null;
            return new Git(thread.WorktreePath ?? project.Path);
        }
    }

    // :3471-3475.
    private string? WorkingRoot => Thread is { } thread && App?.Project(thread.ProjectID) is { } project
        ? thread.WorktreePath ?? project.Path
        : null;

    // :3446-3452. Watch only a writer and only from a tree known to predate it.
    private void WatchHydraCommand(string key, ToolCall call)
    {
        if (hydraCommandTrees.ContainsKey(key) || !ShellCommandKind.MayWriteFiles(call.Title)
            || RepositoryGit is null) return;
        var before = TreeBeforeCommand();
        if (before is null) return;
        hydraCommandTrees[key] = before;
        settledTree = null;
        treeEpoch++;
    }

    // :3454-3468. A native head's shell writes belong to the lead turn, not its separate row.
    private void SettleHydraCommand(string key)
    {
        if (!hydraCommandTrees.Remove(key, out var before) || RepositoryGit is not { } git) return;
        var epoch = treeEpoch;
        commandSettles[key] = MainActor.Spawn(async () =>
        {
            if (before is null) return;
            var after = await CaptureTree(git);
            if (after is null) return;
            latestTree = after;
            if (treeEpoch == epoch && commandTrees.Count == 0 && hydraCommandTrees.Count == 0)
                settledTree = (after, epoch);
            if (before == after) return;

            IReadOnlyList<string> paths;
            try { paths = await git.ChangedPaths(before, after); }
            catch { return; }
            var writes = paths.Where(path => path.Length != 0 && !TouchedPaths.IsBuildOutput(path)).ToArray();
            hydraTouched.UnionWith(writes);
            HydraCurrentTurnTouchedPaths = HydraCurrentTurnTouchedPaths.Mutating(current => current.UnionWith(writes));
        });
    }

    // :3383-3413. The final tree is serialized and the row gains only edits no other row claimed.
    private void SettleCommand(string id)
    {
        if (!commandTrees.Remove(id, out var before) || RepositoryGit is not { } git) return;
        var epoch = treeEpoch;
        commandSettles[id] = MainActor.Spawn(async () =>
        {
            if (before is null) return;
            var after = await CaptureTree(git);
            if (after is null) return;
            latestTree = after;
            if (treeEpoch == epoch && commandTrees.Count == 0 && hydraCommandTrees.Count == 0)
                settledTree = (after, epoch);
            if (before == after) return;

            IReadOnlyList<string> changed;
            try { changed = await git.ChangedPaths(before, after); }
            catch { return; }
            var repositoryRoot = await RepositoryRoot(git);
            var attributed = AttributedPathsInTurnOf(id, repositoryRoot);
            var real = changed.Where(path => path.Length != 0
                    && !TouchedPaths.IsBuildOutput(path)
                    && !attributed.Contains(TouchedPaths.Normalize(path, repositoryRoot)))
                .ToArray();
            if (real.Length == 0) return;

            string patch;
            try { patch = await git.Diff(before, after, paths: real); }
            catch { return; }
            if (patch.Length == 0) return;

            ValueList<FileEdit> edits;
            try { edits = await FileEdits(patch, SessionDirectoryUrl(repositoryRoot)); }
            catch { return; }
            if (edits.Count == 0 || !entryIndex.TryGetValue(id, out var entry)
                || entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool) return;

            var call = tool.Value;
            if (call.Edits.Any(edit => edit.Diff is not null)) return;
            call = call with { Edits = edits };
            if (entry.TurnID == currentTurnID) RecordCurrentTurnEdits(edits);
            entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Tool(call) };
            saveRevision++;
            NoteHydraHeadEvent(edits: edits.Count);
            ScheduleSave();
        });
    }

    private HashSet<string> AttributedPathsInTurnOf(string id, string repositoryRoot)
    {
        var result = new HashSet<string>(SwiftStringComparer.Instance);
        if (!entryIndex.TryGetValue(id, out var source) || source.TurnID is not { } turnID) return result;
        foreach (var entry in Entries)
        {
            if (entry.Id == id || entry.TurnID != turnID
                || entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool) continue;
            foreach (var edit in tool.Value.Edits)
                result.Add(TouchedPaths.Normalize(edit.Path, repositoryRoot));
        }
        return result;
    }

    private Task<string?> CaptureTree(Git git)
    {
        var previous = treeCaptures;
        var capture = MainActor.Spawn(async () =>
        {
            if (previous is not null) await previous;
            // :3463-3464 `try? await git.captureTree()`: a capture that fails yields nil. The error is caught inside the detached
            // body: a non-throwing detached task has nowhere to put one, and it would end the process.
            return await TaskHandle.Detached<string?>(async () =>
            {
                try { return await git.CaptureTree(); }
                catch { return null; }
            }).Value;
        });
        treeCaptures = capture.Value;
        return capture.Value;
    }

    // MARK: - Persistence

    // ThreadRuntime.swift:3563-3577.
    private void GenerateTitle(string text, IReadOnlyList<Attachment> attachments)
    {
        if (App is not { } app || app.Thread(ThreadID) is not { } thread || thread.HasCustomTitle) return;
        if (Turns.Count != 1 && thread.Title != ChatThread.Untitled) return;

        var names = attachments.Select(attachment => attachment.Name).ToArray();
        var input = names.Length == 0 ? text : text + "\nAttached: " + string.Join(", ", names);
        app.UpdateThread(ThreadID, current => current with { Title = TextCleanup.SingleLine(text, limit: 48) });
        if (app.TextEngine(thread.Provider) is not { } engine) return;

        _ = MainActor.Spawn(async () =>
        {
            if (await TextGeneration.ThreadTitle(input, engine) is not { } title) return;
            app.UpdateThread(ThreadID, current => current.HasCustomTitle ? current : current with { Title = title });
        });
    }

    // :3581-3602
    public void ScheduleSave()
    {
        if (!persistenceEnabled) return;
        if (Phase == RuntimePhase.Idle)
        {
            saveTask?.Cancel();
            var weakSelf = new WeakReference<ThreadRuntime>(this);
            saveTask = MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(800));
                if (SwiftTask.IsCancelled || !weakSelf.TryGetTarget(out var runtime)) return;
                await MainActor.Run(() => runtime.SaveNow());
            });
            return;
        }

        // :3634-3639. B3: no longer a fixed 5 seconds. No chat rewrites more than about 250 KB a second however
        // big it grows: five seconds up to a megabyte, twenty for five megabytes and more, linear in between
        // (PR-RUNTIME-158 b3_note).
        if (saveTask is not null) return;
        var bytes = DiskWriter.Shared.LastWrittenSize(Storage.ThreadURL(ThreadID)) ?? 0;
        var seconds = Math.Min(20.0, Math.Max(5.0, bytes / 250_000.0));
        var runningSelf = new WeakReference<ThreadRuntime>(this);
        saveTask = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(seconds));
            if (SwiftTask.IsCancelled || !runningSelf.TryGetTarget(out var runtime)) return;
            await MainActor.Run(() => runtime.SaveNow());
        });
    }

    // :3604-3620
    public void SaveNow()
    {
        // :3605-3610. Clear the finished timer before any branch that may skip the write.
        saveTask?.Cancel();
        saveTask = null;
        if (!persistenceEnabled || IsLoadingHistory) return; // :3612
        FlushHandoffJournal();
        if (saveRevision == lastSavedRevision) return; // :3617
        lastSavedRevision = saveRevision; // :3618
        DiskWriter.Shared.EncodeAndWrite(SnapshotForPersistence(), Storage.ThreadURL(ThreadID)); // :3619
    }

    // :3624-3627
    public ThreadDocument? UnsavedSnapshot() =>
        !IsLoadingHistory && saveRevision != lastSavedRevision ? SnapshotForPersistence() : null;

    // :3633-3640
    public bool HoldsOnlyPersistedState =>
        Phase == RuntimePhase.Idle && session is null && !IsReverting && pendingSend is null && !IsLoadingHistory
            && Draft.IsEmpty && FollowUpEdit is null && MessageEdit is null
            && Approvals.Count == 0 && Questions.Count == 0
            && hydraPendingReports.Count == 0 && hydraBatches.Count == 0 && hydraWaiting.Count == 0 && hydraNativeHeads.Count == 0
            && !hydraFlushScheduled && HydraLanding is null && !isHydraMerging && headBudget is null
            && commandTrees.Count == 0 && commandSettles.Count == 0;

    // :3642-3648
    public void CancelPersistence()
    {
        App?.DetachAccountRouting(ThreadID);
        FlushHandoffJournal();
        persistenceEnabled = false;
        saveTask?.Cancel();
        saveTask = null;
        flushTask?.Cancel();
        flushTask = null;
    }

    // :3650-3660
    public ThreadDocument SnapshotForPersistence()
    {
        FlushDeltas(scheduleSave: false);
        return new ThreadDocument(ThreadID)
        {
            Items = new ValueList<TimelineItem>(Entries.Select(entry => entry.Item)),
            Turns = Turns,
            Usage = Usage,
            Continuation = Continuation,
            FollowUps = FollowUps,
            HydraMerges = HydraMerges,
        };
    }

    // DEVIATION OD-0114 / ED-407: no UI, no ApiTranscript clipping, no AI call.
    private string HandoffFolder => System.IO.Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(ThreadID));

    public HandoffSummary SaveHandoff(string currentDiff)
    {
        if (!IsAccountSwitchSafe && !IsHandshakeCheckpointSafe)
            throw new InvalidOperationException("A handoff requires a quiet boundary with no pending tools, approvals or head results.");
        FlushHandoffJournal(explicitCheckpoint: true);
        var journal = ReadHandoffJournal();
        var context = new HandoffContext(HandoffJournal.Restore(SnapshotForPersistence(), journal), Thread?.Title ?? "", currentDiff)
        {
            EventJournal = journal,
            IsQuiescent = IsAccountSwitchSafe,
        };
        return HandoffStore.Save(context, HandoffFolder);
    }

    // :2119-2251. The provider event union is handled with explicit cases and no fallback arm.
    // Swift's pinned implementation contains two .tokenSpend arms; the first is selected for every such event.
    private void Handle(ProviderEvent @event)
    {
        App?.AssistantTaps.TapProvider(ThreadID, @event); // PORT-ONLY (AS-2, ED-NEW-dextra-as2-8): the Assistant's live feed
        if (@event is ProviderEvent.MessageCompleted) handoffMessageBoundary = true;
        else if (@event is ProviderEvent.MessageDelta or ProviderEvent.TurnStarted or ProviderEvent.ToolStarted
            or ProviderEvent.Approval or ProviderEvent.Question) handoffMessageBoundary = false;
        CaptureHandoff(@event);
        if (@event is ProviderEvent.TurnCompleted or ProviderEvent.Exited) FlushHandoffJournal();
        switch (@event)
        {
            case ProviderEvent.SessionReady value:
                RecordSessionID(value.SessionID);
                break;
            case ProviderEvent.TurnStarted value:
                Phase = RuntimePhase.Running;
                if (currentTurnID is { } turnID && value.ProviderTurnID is { } providerTurnID)
                    UpdateTurn(turnID, turn => turn with { ProviderTurnID = providerTurnID });
                break;
            case ProviderEvent.MessageDelta value:
                QueueDelta(value.Id, DeltaKind.Message, value.Text);
                break;
            case ProviderEvent.MessageCompleted value:
                FlushDeltas();
                CompleteMessage(value.Id, value.Text);
                break;
            case ProviderEvent.ReasoningDelta value:
                QueueDelta(value.Id, DeltaKind.Reasoning, value.Text);
                break;
            case ProviderEvent.ReasoningCompleted value:
                FlushDeltas();
                CompleteReasoning(value.Id, value.Text);
                break;
            case ProviderEvent.ToolStarted value:
                FlushDeltas();
                UpsertTool(value.Id, value.Call);
                if (value.Call.ToolCallKind == ToolCall.Kind.Command) WatchCommand(value.Id, value.Call);
                break;
            case ProviderEvent.ToolOutput value:
                QueueDelta(value.Id, DeltaKind.ToolOutput, value.Text);
                break;
            case ProviderEvent.ToolUpdated value:
                FlushDeltas();
                ApplyToolUpdate(value.Id, value.Update);
                if (entryIndex.TryGetValue(value.Id, out var updatedEntry)
                    && updatedEntry.Item.TimelineItemContent is TimelineItem.Content.Tool)
                {
                    var updatedTool = ((TimelineItem.Content.Tool)updatedEntry.Item.TimelineItemContent).Value;
                    if (updatedTool.ToolCallKind == ToolCall.Kind.Command
                        && updatedTool.ToolCallStatus != ToolCall.Status.Running)
                        SettleCommand(value.Id);
                }
                if (value.Update.Status is { } status
                    && status != ToolCall.Status.Running
                    && HydraToolHeads.TryGetValue(value.Id, out var completedHeadID))
                {
                    HydraToolFinished(completedHeadID, status, value.Update.Output);
                }
                break;
            case ProviderEvent.PlanDelta value:
                QueueDelta(value.Id, DeltaKind.Plan, value.Text);
                break;
            case ProviderEvent.PlanCompleted value:
                FlushDeltas();
                CompletePlan(value.Id, value.Markdown);
                break;
            case ProviderEvent.Todos value:
                UpsertTodos(value.Value);
                break;
            case ProviderEvent.Approval value:
                SkillsOffers?.BindApproval(value.Value);
                Approvals = Approvals.Mutating(items =>
                {
                    items.RemoveAll(item => item.Id == value.Value.Id);
                    items.Add(value.Value);
                });
                App?.ThreadNeedsAttention(ThreadID);
                App?.DextraApproval(ThreadID, value.Value); // PORT-ONLY (Dextra AS-5): a mission chat's approval goes to the queue
                break;
            case ProviderEvent.Question value:
                Questions = Questions.Mutating(items =>
                {
                    items.RemoveAll(item => item.Id == value.Value.Id);
                    items.Add(value.Value);
                });
                App?.ThreadNeedsAttention(ThreadID);
                App?.DextraQuestion(ThreadID, value.Value); // PORT-ONLY (Dextra AS-5)
                break;
            case ProviderEvent.RequestResolved value:
                Approvals = Approvals.Mutating(items => items.RemoveAll(item => item.Id == value.Id));
                Questions = Questions.Mutating(items => items.RemoveAll(item => item.Id == value.Id));
                App?.DextraRequestResolved(ThreadID, value.Id); // PORT-ONLY (Dextra AS-5): its card says the agent cancelled it
                break;
            case ProviderEvent.TokenSpend value:
                if (currentTurnID is { } tokenTurnID)
                {
                    UpdateTurn(tokenTurnID, turn => turn with
                    {
                        TokenSpends = (turn.TokenSpends ?? ValueList<TokenSpend>.Empty).Appending(value.Value),
                    });
                }
                break;
            case ProviderEvent.Usage value:
                Usage = value.Value;
                saveRevision += 1;
                if (Thread is { } usageThread)
                {
                    App?.Providers.InvalidateCredits(usageThread.Provider);
                    App?.Providers.InvalidatePlanLimits(usageThread.Provider);
                }
                break;
            case ProviderEvent.Diff value:
                if (currentTurnID is not null) currentProviderDiff = value.Value;
                break;
            case ProviderEvent.Notice value:
                if (IgnisSwallowsNotice(value.Value)) break; // Ignis: a silent tidy-up's "Context compacted."
                AppendNotice(value.Value.NoticeLevel, value.Value.Message);
                break;
            case ProviderEvent.CacheUsage value:
                IgnisObserve(value.Value); // Ignis: forwarded to the controller
                break;
            case ProviderEvent.CacheBilling value:
                IgnisAttach()?.CreditsHint(ThreadID, value.OnCredits); // Ignis: paid-credits hint (ED-NEW-ignis-runtime)
                break;
            case ProviderEvent.UsageLimit value:
                IgnisUsageLimit(value.ResetsAt);
                limitedReplyTurnID = currentTurnID;
                if (App?.AccountRouting.NoteLimit(this, value.Account, value.ResetsAt) == true) break;
                if (value.Account is { } limitedAccount) App?.AutoContinue.NoteLimit(ThreadID, value.ResetsAt, limitedAccount);
                else App?.AutoContinue.NoteLimit(ThreadID, value.ResetsAt);
                break;
            case ProviderEvent.ModeChanged value:
                App?.UpdateThread(ThreadID, thread => thread with { InteractionMode = value.Value });
                break;
            case ProviderEvent.Models value:
                if (Thread is { } modelsThread) App?.Providers.UpdateCatalog(value.Value, modelsThread.Provider);
                break;
            case ProviderEvent.Commands value:
                if (App is { } app && Thread is { } commandsThread && app.Project(commandsThread.ProjectID) is { } project)
                    app.Providers.UpdateCommands(value.Value, commandsThread.Provider,
                        commandsThread.WorktreePath ?? project.Path);
                break;
            case ProviderEvent.Title value:
                if (Turns.Count <= 1 && App is { } titleApp && Thread is { } titleThread
                    && !titleThread.HasCustomTitle && titleApp.TextEngine(titleThread.Provider) is null)
                    titleApp.UpdateThread(ThreadID, thread => thread with
                    {
                        Title = Marrow.Core.Support.TextCleanup.WithoutEmDashes(value.Value),
                    });
                break;
            case ProviderEvent.AssistantMessageID value:
                if (currentTurnID is not null) currentProviderAnchor = value.Value;
                break;
            case ProviderEvent.TurnCompleted value:
            {
                FlushDeltas();
                var completedTurnID = currentTurnID; // captured before either branch (PR-RUNTIME-102 b3_note).
                if (value.Error is { } error && FinishTurnUpdatingCLI(error, value.Status, completedTurnID)) break; // B3.
                if (value.Error is { } notice) AppendNotice(Notice.Level.Error, notice);
                _ = MainActor.Spawn(async () => await FinishTurn(value.Status, completedTurnID));
                break;
            }
            case ProviderEvent.Exited value:
                FlushDeltas();
                if (isStartingSession) break;
                ReleaseSession(stop: false);
                Approvals = ValueList<ApprovalRequest>.Empty;
                Questions = ValueList<QuestionRequest>.Empty;
                StopNativeHeads();
                if (currentTurnID is not null)
                {
                    var name = Thread?.Provider.DisplayName ?? "The agent";
                    var failedTurnID = currentTurnID;
                    if (value.Error is { } exitError && FinishTurnUpdatingCLI(exitError, TurnStatus.Failed, failedTurnID)) break; // B3.
                    AppendNotice(Notice.Level.Error, value.Error ?? $"{name} stopped unexpectedly.");
                    _ = MainActor.Spawn(async () => await FinishTurn(TurnStatus.Failed, failedTurnID));
                }
                break;
            case ProviderEvent.AgentStarted value:
                HydraAgentStarted(value.Value);
                break;
            case ProviderEvent.AgentEvent value:
                HydraAgentEvent(value.AgentID, value.Value);
                break;
            case ProviderEvent.AgentProgress value:
                if (!hydraNativeHeads.TryGetValue(value.AgentID, out var nativeHeadID)
                    || App?.Runtime(nativeHeadID) is not { } headRuntime) break;
                if (value.Summary is { Length: > 0 } summary) headRuntime.HydraActivity = summary;
                else if (value.LastTool is { } lastTool) headRuntime.HydraActivity = lastTool;
                if (value.Tokens is { } tokens) headRuntime.HydraTokens = tokens;
                if (value.ToolCalls is { } toolCalls) headRuntime.HydraToolCalls = toolCalls;
                headRuntime.HydraLastEventAt = SwiftDate.Now;
                break;
            case ProviderEvent.AgentFinished value:
                HydraAgentFinished(value.AgentID, value.Status, value.Summary);
                break;
        }
    }

    // ThreadRuntime.swift:2578-2603. Provider-owned heads share the app's Hydra records and timelines.
    private void HydraAgentStarted(AgentSpawn spawn)
    {
        if (App is not { } app || Thread is not { } thread || !app.HydraIsOn(thread)) return;
        if (hydraNativeHeads.TryGetValue(spawn.Id, out var headID))
        {
            var named = spawn.Description.Length != 0 && spawn.Description != "Subagent";
            app.UpdateThread(headID, head =>
            {
                if (head.Hydra is not { } info) return head;
                if (named && info.Task != spawn.Description)
                {
                    info = info with { Task = spawn.Description };
                    head = head with { Title = $"{info.Persona.Name} \u00B7 {TextCleanup.SingleLine(spawn.Description, 60)}" };
                }
                if (info.ToolUseID is null) info = info with { ToolUseID = spawn.ToolUseID };
                return head with { Hydra = info };
            });
            if (spawn.ToolUseID is { } toolUseID)
                HydraToolHeads = HydraToolHeads.Mutating(map => map[toolUseID] = headID);
            if (spawn.Prompt is { } prompt && app.ExistingRuntime(headID) is { } headRuntime && !headRuntime.HasSentPrompts)
                headRuntime.RehearseBrief(prompt);
            return;
        }
        if (app.SpawnNativeHead(ThreadID, spawn) is not { } createdHead) return;
        hydraNativeHeads[spawn.Id] = createdHead.Id;
        // :2599-2601 has no observable work: pendingNativeSpend is filled only by the unreachable
        // duplicate tokenSpend arm (PR-RUNTIME-252). Do not introduce buffering or reroute token events.
        if (spawn.ToolUseID is { } createdToolUseID)
            HydraToolHeads = HydraToolHeads.Mutating(map => map[createdToolUseID] = createdHead.Id);
    }

    // ThreadRuntime.swift:2606-2642.
    private void HydraAgentEvent(string agentID, ProviderEvent @event)
    {
        if (App is not { } app) return;
        if (!hydraNativeHeads.ContainsKey(agentID)
            && entryIndex.TryGetValue(agentID, out var entry)
            && entry.Item.TimelineItemContent is TimelineItem.Content.Tool { Value: { ToolCallKind: ToolCall.Kind.Agent } call })
        {
            HydraAgentStarted(new AgentSpawn
            {
                Id = agentID, TaskID = null, ToolUseID = agentID, Description = call.Title,
                Prompt = null, Model = null, IsBackground = false,
            });
        }
        if (!hydraNativeHeads.TryGetValue(agentID, out var headID)
            || app.ExistingRuntime(headID) is not { } headRuntime) return;
        switch (@event)
        {
            case ProviderEvent.TurnStarted:
                if (!headRuntime.IsRunning)
                {
                    headRuntime.RehearseTurn(null);
                    app.UpdateThread(headID, head => head.Hydra is { } info
                        ? head with { Hydra = info with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running, FinishedAt = null } }
                        : head);
                }
                break;
            case ProviderEvent.TurnCompleted completed:
                HydraAgentFinished(agentID, completed.Status, summary: null);
                break;
            case ProviderEvent.ToolStarted started:
                headRuntime.HydraToolCalls++;
                headRuntime.HydraActivity = ToolPresentation.Label(started.Call);
                headRuntime.HydraLastEventAt = SwiftDate.Now;
                NoteHydraEdits(started.Call.Edits);
                if (started.Call.ToolCallKind == ToolCall.Kind.Command) WatchHydraCommand($"{agentID}-{started.Id}", started.Call);
                headRuntime.Rehearse(@event);
                break;
            case ProviderEvent.ToolUpdated updated:
                if (updated.Update.Edits is { } edits) NoteHydraEdits(edits);
                if (updated.Update.Status is { } status && status != ToolCall.Status.Running)
                    SettleHydraCommand($"{agentID}-{updated.Id}");
                headRuntime.Rehearse(@event);
                break;
            case ProviderEvent.Usage:
            case ProviderEvent.SessionReady:
            case ProviderEvent.Models:
            case ProviderEvent.Commands:
            case ProviderEvent.Title:
            case ProviderEvent.AssistantMessageID:
                break;
            default:
                headRuntime.Rehearse(@event);
                break;
        }
    }

    // ThreadRuntime.swift:2646-2652. Retain the id mapping until session teardown.
    private void HydraAgentFinished(string agentID, TurnStatus status, string? summary)
    {
        if (App is not { } app || !hydraNativeHeads.TryGetValue(agentID, out var headID)) return;
        app.FinishHydraHead(headID, status, summary);
        if (app.ExistingRuntime(headID) is { IsRunning: true } headRuntime)
            headRuntime.Rehearse(new ProviderEvent.TurnCompleted(status, null));
    }

    // ThreadRuntime.swift:2657-2666. A background head outlives its spawning tool call.
    private void HydraToolFinished(Guid headID, ToolCall.Status status, string? output)
    {
        if (App?.Thread(headID)?.Hydra is not { HydraHeadInfoKind: HydraHeadInfo.Kind.Native, IsBackground: false, IsFinished: false } info
            || info.NativeID is not { } nativeID) return;
        var outcome = status switch
        {
            ToolCall.Status.Completed => TurnStatus.Completed,
            ToolCall.Status.Declined => TurnStatus.Interrupted,
            _ => TurnStatus.Failed,
        };
        HydraAgentFinished(nativeID, outcome, output);
    }

    // The provider event routes call this teardown on release and exit. :2680-2690. Review fix
    // (PR-RUNTIME-103): this was previously a no-op pending AppModel.FinishHydraHead/ExistingRuntime, which
    // are now present; ported line for line.
    private void StopNativeHeads()
    {
        if (App is not { } app) return;
        foreach (var headID in hydraNativeHeads.Values.ToArray())
        {
            if (app.Thread(headID) is not { } head || head.Hydra is not { IsFinished: false }) continue;
            app.FinishHydraHead(headID, TurnStatus.Interrupted, summary: null);
            if (app.ExistingRuntime(headID) is { IsRunning: true } headRuntime)
                headRuntime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
        }
        hydraNativeHeads.Clear();
    }

    // :2253-2266. Token reports with the same provider/model/agent identity are coalesced.
    private void RecordTokenSpend(TokenSpend spend)
    {
        if (spend.TotalTokens <= 0) return;

        var index = -1;
        for (var candidate = Turns.Count - 1; candidate >= 0; candidate--)
        {
            if (currentTurnID is { } turnID && Turns[candidate].Id == turnID)
            {
                index = candidate;
                break;
            }
        }
        if (index < 0 && Thread?.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Native && Turns.Count != 0)
            index = Turns.Count - 1;
        if (index < 0) return;

        var turn = Turns[index];
        var amounts = turn.TokenSpends ?? ValueList<TokenSpend>.Empty;
        var spendIndex = -1;
        for (var candidate = 0; candidate < amounts.Count; candidate++)
        {
            var existing = amounts[candidate];
            if (existing.Provider == spend.Provider && existing.Model == spend.Model && existing.AgentID == spend.AgentID)
            {
                spendIndex = candidate;
                break;
            }
        }
        amounts = spendIndex >= 0
            ? amounts.SetItem(spendIndex, amounts[spendIndex].Merge(spend))
            : amounts.Appending(spend);
        Turns = Turns.SetItem(index, turn with { TokenSpends = amounts });

        if (Thread?.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
            HydraTokens += spend.TotalTokens;
        saveRevision += 1;
        ScheduleSave();
    }

    // :3020-3048. Timeline inserts/removals keep the id index in step with visible rows.
    private Guid? TurnIDForNewRows => currentTurnID ?? closingTurnID;

    private void Append(TimelineItem item)
    {
        foreach (var existingEntry in Entries.TakeLast(6)) EndStreaming(existingEntry);
        var entry = new TimelineEntry(item);
        Entries = Entries.Appending(entry);
        entryIndex[item.Id] = entry;
        saveRevision += 1;
    }

    private void Remove(string id)
    {
        entryIndex.Remove(id);
        Entries = new ValueList<TimelineEntry>(Entries.Where(entry => entry.Id != id));
        saveRevision += 1;
    }

    // :3050-3097. Empty opening message/reasoning deltas do not create blank rows; plan deltas do.
    private void QueueDelta(string id, DeltaKind kind, string text)
    {
        if (!entryIndex.ContainsKey(id))
        {
            switch (kind)
            {
                case DeltaKind.Message:
                    if (text.Length == 0) return;
                    Append(new TimelineItem(TurnIDForNewRows,
                        new TimelineItem.Content.Assistant(new AssistantMessage { Text = "", IsStreaming = true }), id));
                    break;
                case DeltaKind.Reasoning:
                    if (text.Length == 0) return;
                    Append(new TimelineItem(TurnIDForNewRows,
                        new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "", IsStreaming = true }), id));
                    break;
                case DeltaKind.Plan:
                    Append(new TimelineItem(TurnIDForNewRows,
                        new TimelineItem.Content.Plan(new ProposedPlan
                        {
                            Markdown = "",
                            ProposedPlanState = ProposedPlan.State.Drafting,
                        }), id));
                    break;
                case DeltaKind.ToolOutput:
                    return;
            }
        }

        if (text.Length == 0) return;
        var delta = kind == DeltaKind.ToolOutput ? text : Marrow.Core.Support.TextCleanup.WithoutEmDashes(text);
        if (pendingDeltas.TryGetValue(id, out var pending))
            pendingDeltas[id] = pending with { Text = pending.Text + delta };
        else
            pendingDeltas[id] = new PendingDelta { Kind = kind, Text = delta };

        if (flushTask is not null) return;
        var isHead = appReference is not null
            && appReference.TryGetTarget(out var app)
            && app.Thread(ThreadID)?.Hydra is not null;
        if (isHead) StreamLoad.Shared.NoteStreaming(this);
        var flushWindow = isHead ? StreamLoad.Shared.HeadFlushWindow : FlushWindow;
        if (SwiftClock.Now - lastFlushAt >= flushWindow)
        {
            FlushDeltas();
            return;
        }

        var weakSelf = new WeakReference<ThreadRuntime>(this);
        flushTask = MainActor.Spawn(async () =>
        {
            var delay = FlushDelay(PendingFlushLength(), ScrollActivity.Shared.Current.IsScrolling);
            if (isHead) delay = Math.Max(delay, StreamLoad.Shared.HeadFlushDelayMilliseconds);
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(delay));
            if (!weakSelf.TryGetTarget(out var runtime)) return;
            await MainActor.Run(() => runtime.FlushDeltas());
        });
    }

    // ThreadRuntime.swift:3101-3138.
    private long PendingFlushLength()
    {
        long longest = 0;
        foreach (var (id, delta) in pendingDeltas)
        {
            if (delta.Kind == DeltaKind.ToolOutput) continue;
            if (!entryIndex.TryGetValue(id, out var entry)) continue;
            var existingLength = entry.Item.TimelineItemContent switch
            {
                TimelineItem.Content.Assistant assistant => SwiftString.Characters(assistant.Value.Text).LongCount(),
                TimelineItem.Content.Reasoning reasoning => SwiftString.Characters(reasoning.Value.Text).LongCount(),
                TimelineItem.Content.Plan plan => SwiftString.Characters(plan.Value.Markdown).LongCount(),
                _ => 0,
            };
            var deltaLength = SwiftString.Characters(delta.Text).LongCount();
            longest = Math.Max(longest, existingLength + deltaLength);
        }
        return longest;
    }

    private static long FlushDelay(long? length, bool scrolling)
    {
        var paced = (length ?? 0) switch
        {
            < 4_096 => 45,
            < 32_768 => 90,
            _ => 150,
        };
        return scrolling ? Math.Max(paced, 160) : paced;
    }

    private static readonly TimeSpan FlushWindow = TimeSpan.FromMilliseconds(45);

    // :3179-3204. Completion events flush queued text before finalizing their rows.
    private void FlushDeltas(bool scheduleSave = true)
    {
        flushTask?.Cancel();
        flushTask = null;
        if (pendingDeltas.Count == 0) return;
        lastFlushAt = SwiftClock.Now;
        var deltas = pendingDeltas.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        pendingDeltas.Clear();
        var heard = false;
        var streamedRows = new List<(string ID, string Delta)>();
        foreach (var (id, delta) in deltas)
        {
            if (!entryIndex.TryGetValue(id, out var entry) || !AppendStreamed(entry, delta.Text, delta.Kind)) continue;
            saveRevision += 1;
            if (delta.Kind != DeltaKind.Plan) heard = true;
            if (delta.Kind == DeltaKind.Message && entry.TimelineEntryKind == TimelineEntry.Kind.Assistant)
                streamedRows.Add((id, delta.Text));
        }
        if (heard) NoteHydraHeadEvent();
        if (currentTurnID is { } turnID)
            foreach (var row in streamedRows)
                scanStreamedDelegations(row.ID, turnID, row.Delta, isDelta: true);
        if (scheduleSave) ScheduleSave();
    }

    // ThreadRuntime.swift:3663-3692. Mutate only the four matching content/kind pairs.
    private static bool AppendStreamed(TimelineEntry entry, string text, DeltaKind kind)
    {
        switch (kind, entry.Item.TimelineItemContent)
        {
            case (DeltaKind.Message, TimelineItem.Content.Assistant assistant):
                entry.Item = entry.Item with
                {
                    TimelineItemContent = new TimelineItem.Content.Assistant(assistant.Value with { Text = assistant.Value.Text + text }),
                };
                return true;
            case (DeltaKind.Reasoning, TimelineItem.Content.Reasoning reasoning):
                entry.Item = entry.Item with
                {
                    TimelineItemContent = new TimelineItem.Content.Reasoning(reasoning.Value with { Text = reasoning.Value.Text + text }),
                };
                return true;
            case (DeltaKind.ToolOutput, TimelineItem.Content.Tool tool):
                entry.Item = entry.Item with
                {
                    TimelineItemContent = new TimelineItem.Content.Tool(tool.Value.AppendOutput(text)),
                };
                return true;
            case (DeltaKind.Plan, TimelineItem.Content.Plan plan):
                entry.Item = entry.Item with
                {
                    TimelineItemContent = new TimelineItem.Content.Plan(plan.Value with { Markdown = plan.Value.Markdown + text }),
                };
                return true;
            default:
                return false;
        }
    }

    // :3206-3242. Empty completion payloads keep streamed text; whitespace-only rows are removed.
    private void CompleteMessage(string id, string text)
    {
        var cleaned = Marrow.Core.Support.TextCleanup.WithoutEmDashes(text);
        try
        {
            if (!entryIndex.TryGetValue(id, out var entry))
            {
                if (cleaned.Length != 0)
                    Append(new TimelineItem(TurnIDForNewRows,
                        new TimelineItem.Content.Assistant(new AssistantMessage { Text = cleaned }), id));
                return;
            }
            if (entry.Item.TimelineItemContent is not TimelineItem.Content.Assistant assistant) return;
            var message = assistant.Value;
            if (cleaned.Length != 0) message = message with { Text = cleaned };
            message = message with { IsStreaming = false };
            if (!SwiftString.Characters(message.Text).Any(character => !SwiftCharacter.IsWhitespace(character)))
            {
                Remove(id);
            }
            else
            {
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(message) };
                saveRevision += 1;
                if (currentTurnID is { } turnID) scanStreamedDelegations(id, turnID, message.Text);
            }
        }
        finally
        {
            ScheduleSave();
        }
    }

    private void CompleteReasoning(string id, string text)
    {
        var cleaned = Marrow.Core.Support.TextCleanup.WithoutEmDashes(text);
        try
        {
            if (!entryIndex.TryGetValue(id, out var entry)
                || entry.Item.TimelineItemContent is not TimelineItem.Content.Reasoning reasoning)
            {
                if (cleaned.Length != 0)
                    Append(new TimelineItem(TurnIDForNewRows,
                        new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = cleaned }), id));
                return;
            }

            var block = reasoning.Value;
            if (cleaned.Length != 0) block = block with { Text = cleaned };
            block = block with { IsStreaming = false };
            if (!SwiftString.Characters(block.Text).Any(character => !SwiftCharacter.IsWhitespace(character)))
            {
                Remove(id);
            }
            else
            {
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Reasoning(block) };
                saveRevision += 1;
            }
        }
        finally
        {
            ScheduleSave();
        }
    }

    // :3244-3255. Empty completion text keeps the streamed markdown, while every found draft is proposed.
    private void CompletePlan(string id, string markdown)
    {
        var cleaned = Marrow.Core.Support.TextCleanup.WithoutEmDashes(markdown);
        if (entryIndex.TryGetValue(id, out var entry))
        {
            if (entry.Item.TimelineItemContent is TimelineItem.Content.Plan plan)
            {
                var completed = plan.Value;
                if (cleaned.Length != 0) completed = completed with { Markdown = cleaned };
                completed = completed with { ProposedPlanState = ProposedPlan.State.Proposed };
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Plan(completed) };
                saveRevision += 1;
            }
        }
        else if (cleaned.Length != 0)
        {
            Append(new TimelineItem(TurnIDForNewRows,
                new TimelineItem.Content.Plan(new ProposedPlan
                {
                    Markdown = cleaned,
                    ProposedPlanState = ProposedPlan.State.Proposed,
                }), id));
        }

        ScheduleSave();
    }

    // ThreadRuntime.swift:3257-3277.
    private void UpsertTool(string id, ToolCall call)
    {
        NoteToolMayWrite(call.ToolCallKind);
        RecordCurrentTurnEdits(call.Edits); // ThreadRuntime.swift:3259; implemented in its owning later slice.
        call = call with { Edits = Capped(call.Edits) };

        if (!entryIndex.TryGetValue(id, out var entry)
            || entry.Item.TimelineItemContent is not TimelineItem.Content.Tool existingContent)
        {
            Append(new TimelineItem(TurnIDForNewRows, new TimelineItem.Content.Tool(call), id));
            NoteHydraHeadEvent(edits: call.Edits.Count, tool: true);
            return;
        }

        var existing = existingContent.Value;
        if (call.Title.Length != 0) existing = existing with { Title = call.Title };
        existing = existing with
        {
            Detail = call.Detail ?? existing.Detail,
            ToolCallKind = call.ToolCallKind,
        };
        var landed = call.Edits.Count != 0 && existing.Edits.Count == 0 ? call.Edits.Count : 0;
        if (call.Edits.Count != 0) existing = existing with { Edits = call.Edits };
        if (existing.ToolCallStatus == ToolCall.Status.Running && call.ToolCallStatus != ToolCall.Status.Running)
            existing = existing.Finish(call.ToolCallStatus);
        entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Tool(existing) };
        saveRevision++;
        NoteHydraHeadEvent(edits: landed);
    }

    // ThreadRuntime.swift:3279-3289.
    private void NoteHydraHeadEvent(long edits = 0, bool tool = false)
    {
        if (appReference is null || !appReference.TryGetTarget(out var app)
            || app.Thread(ThreadID)?.Hydra is not { } info) return;
        HydraLastEventAt = SwiftDate.Now;
        HydraEditCount += edits;
        if (tool && info.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy) HydraToolCalls++;
    }

    // ThreadRuntime.swift:3291-3298.
    private void NoteToolMayWrite(ToolCall.Kind? kind)
    {
        if (kind is null or ToolCall.Kind.Read or ToolCall.Kind.Search or ToolCall.Kind.Web or ToolCall.Kind.Command)
            return;
        settledTree = null;
        treeEpoch++;
    }

    // ThreadRuntime.swift:3300-3324.
    private void ApplyToolUpdate(string id, ToolUpdate update)
    {
        if (update.Edits is { } reportedEdits)
            RecordCurrentTurnEdits(reportedEdits); // ThreadRuntime.swift:3301; implemented in its owning later slice.
        NoteToolMayWrite(update.Kind);

        if (!entryIndex.ContainsKey(id))
        {
            Append(new TimelineItem(TurnIDForNewRows, new TimelineItem.Content.Tool(new ToolCall
            {
                ToolCallKind = update.Kind ?? ToolCall.Kind.Other,
                Title = update.Title ?? "Tool",
            }), id));
        }
        if (!entryIndex.TryGetValue(id, out var entry)
            || entry.Item.TimelineItemContent is not TimelineItem.Content.Tool toolContent) return;

        var call = toolContent.Value;
        if (update.Title is { Length: > 0 } title) call = call with { Title = title };
        if (update.Detail is { } detail) call = call with { Detail = detail };
        if (update.Kind is { } kind) call = call with { ToolCallKind = kind };
        long landed = 0;
        if (update.Edits is { Count: > 0 } edits)
        {
            if (call.Edits.Count == 0) landed = edits.Count;
            call = call with { Edits = Capped(edits) };
        }
        if (update.Output is { } output) call = call.SetOutput(output);
        if (update.ExitCode is { } exitCode) call = call with { ExitCode = exitCode };
        if (update.Status is { } status)
            call = status == ToolCall.Status.Running ? call with { ToolCallStatus = status } : call.Finish(status);

        entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Tool(call) };
        saveRevision++;
        NoteHydraHeadEvent(edits: landed);
        ScheduleSave();
    }

    // ThreadRuntime.swift:3484-3498.
    private const int EditDiffLimit = 200_000;
    private static ValueList<FileEdit> Capped(IReadOnlyList<FileEdit> edits) => new(edits.Select(edit =>
        edit.Diff is { } diff && System.Text.Encoding.UTF8.GetByteCount(diff) > EditDiffLimit
            ? edit with { Diff = null }
            : edit));

    // ThreadRuntime.swift:3549-3552.
    private void AppendNotice(Notice.Level level, string message)
    {
        Append(new TimelineItem(TurnIDForNewRows,
            new TimelineItem.Content.Notice(new Notice { NoticeLevel = level, Message = message })));
        ScheduleSave();
    }

    // :3523-3547. The cache avoids a scan on repeated updates; the fallback scan stops at the turn boundary.
    private void UpsertTodos(IReadOnlyList<TodoStep> steps)
    {
        if (todosEntryID is { } cachedID
            && entryIndex.TryGetValue(cachedID, out var cachedEntry)
            && cachedEntry.Item.TurnID == currentTurnID)
        {
            cachedEntry.Item = cachedEntry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Todos(new ValueList<TodoStep>(steps)),
            };
            saveRevision += 1;
            return;
        }

        TimelineEntry? existing = null;
        foreach (var entry in Entries.Reverse())
        {
            if (entry.Item.TurnID is { } entryTurnID && entryTurnID != currentTurnID) break;
            if (entry.Item.TimelineItemContent is TimelineItem.Content.Todos)
            {
                existing = entry;
                break;
            }
        }

        if (existing is not null)
        {
            todosEntryID = existing.Id;
            existing.Item = existing.Item with
            {
                TimelineItemContent = new TimelineItem.Content.Todos(new ValueList<TodoStep>(steps)),
            };
            saveRevision += 1;
        }
        else if (steps.Count != 0)
        {
            var item = new TimelineItem(TurnIDForNewRows,
                new TimelineItem.Content.Todos(new ValueList<TodoStep>(steps)));
            Append(item);
            todosEntryID = item.Id;
        }

        ScheduleSave();
    }

    // MARK: - Rehearsal (extension ThreadRuntime, :3696-3758)

    // :3700-3723. Swift: rehearseTurn(_:touchedPaths:providerDiff:); `touchedPaths: [String] = []` is null here
    // for the empty default.
    public void RehearseTurn(string? text, IReadOnlyList<string>? touchedPaths = null, string? providerDiff = null)
    {
        var paths = touchedPaths ?? Array.Empty<string>();
        var turn = new TurnRecord
        {
            Index = checked((turns.Count == 0 ? -1 : turns.Max(existing => existing.Index)) + 1),
            ProviderDiff = providerDiff,
            TouchedPaths = paths.Count == 0 ? null : new ValueList<string>(paths),
        };
        TimelineItem? userItem = null;
        if (text is not null)
        {
            userItem = new TimelineItem(turn.Id, new TimelineItem.Content.User(new UserMessage { Text = text }));
            turn = turn with { UserItemID = userItem.Id };
        }
        Turns = new ValueList<TurnRecord>(turns.Append(turn));
        saveRevision += 1;
        if (userItem is not null) Append(userItem);
        currentTurnID = turn.Id;
        HydraCurrentTurnTouchedPaths = new ValueSet<string>(paths, SwiftStringComparer.Instance); // :3716
        Phase = RuntimePhase.Running;
        TurnStartedAt = SwiftDate.Now;
        App?.UpdateThread(ThreadID, current => current with
        {
            UpdatedAt = SwiftDate.Now,
            LastStatus = TurnStatus.Running,
        });
    }

    // :3727-3737
    public void RehearseBrief(string text)
    {
        if (turns.Count == 0 || turns[turns.Count - 1].UserItemID is not null) return;
        var turn = turns[turns.Count - 1];
        var item = new TimelineItem(turn.Id, new TimelineItem.Content.User(new UserMessage { Text = text }));
        UpdateTurn(turn.Id, current => current with { UserItemID = item.Id });
        var entry = new TimelineEntry(item);
        Entries = Entries.Mutating(items => items.Insert(0, entry));
        entryIndex[item.Id] = entry;
        saveRevision += 1;
        ScheduleSave();
    }

    // :3739-3741
    public void Rehearse(ProviderEvent @event) => Handle(@event);

    // :3744-3746
    public void NoteDiffChanged() => DiffRevision++;

    // :3751-3757. Swift: storeLegacyChanges(_:turnID:).
    public void StoreLegacyChanges(FileChangeSummary changes, Guid turnID)
    {
        for (var index = Entries.Count - 1; index >= 0; index--)
        {
            var entry = Entries[index];
            if (entry.TurnID != turnID || entry.TimelineEntryKind != TimelineEntry.Kind.TurnEnd
                || entry.Item.TimelineItemContent is not TimelineItem.Content.TurnEnd turnEnd
                || turnEnd.Value.Changes is not null) continue;
            entry.Item = entry.Item with
            {
                TimelineItemContent = new TimelineItem.Content.TurnEnd(turnEnd.Value with { Changes = changes }),
            };
            ScheduleSave();
            return;
        }
    }
}
