using Avalonia.Controls;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.UI.Common;

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

    private TimelineItem item;

    public string Id { get; }                 // :20
    public Kind TimelineEntryKind { get; }    // :22 Swift: kind (R-NAME-3)
    public Guid? TurnID { get; }              // :23

    // :24
    public TimelineItem Item { get => Read(item); set => Set(ref item, value); }

    // :26-40
    public TimelineEntry(TimelineItem item) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:26");
}

// :54-100 (R-VAL-1)
public sealed record ReplyQuote
{
    public Guid Id { get; init; } = Guid.NewGuid(); // :55
    public required string Text { get; init; }      // :56

    // :58-63
    public string Excerpt =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:58");

    // :71-99. nonisolated static.
    public static (ValueList<ReplyQuote> Quotes, string Body) Peel(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:71");
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
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:116");

    // :122-135
    public string OutgoingText =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:122");
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

    // :154-156
    public bool IsEmpty =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:154");
}

// :159-163
public enum RuntimePhase
{
    Idle,     // :160
    Starting, // :161
    Running,  // :162
}

// :165-3661 and the rehearsal extension :3696-3758. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed class ThreadRuntime : MainActorModel
{
    // :354-357
    private sealed record HydraBatch
    {
        public required ValueSet<Guid> Pending { get; init; }                          // :355
        public ValueList<HydraReport> Reports { get; init; } = ValueList<HydraReport>.Empty; // :356
    }

    // :510-526
    private sealed record SessionSignature
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

        // :3154-3156
        public void NoteStreaming(ThreadRuntime runtime) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3154");

        // :3158-3160
        public void Forget(ThreadRuntime runtime) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3158");

        // :3163-3167
        public long StreamingCount =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3163");

        // :3170-3172
        public long HeadFlushDelayMilliseconds =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3170");

        // :3174-3176
        public TimeSpan HeadFlushWindow =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3174");
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
    private bool isLoadingHistory = false;
    private ValueList<FollowUpPrompt> followUps = ValueList<FollowUpPrompt>.Empty;
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
    private bool isHydraMerging = false;
    private string? hydraMergeStage;
    private HydraMergeStage? hydraMergeStep;
    private DateTimeOffset? hydraMergeStartedAt;
    private string? hydraMergeNoteID;
    private string? hydraActivity;
    private long hydraToolCalls = 0;
    private long hydraTokens = 0;
    private long hydraEditCount = 0;
    private Guid? diffSelection;
    private long diffOpenRequest = 0;
    private ValueList<FileEdit> diffFocusEdits = ValueList<FileEdit>.Empty;
    private long diffFocusRevision = 0;
    private FileChangeSummary? changeStats;

    // :169
    public Guid ThreadID { get; }

    // :171
    public ValueList<TimelineEntry> Entries { get => Read(entries); private set => Set(ref entries, value); }

    // :172-182
    public ValueList<TurnRecord> Turns
    {
        get => Read(turns);
        private set => Set(ref turns, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:175"));
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

    // :196-201
    public long DiffRevision
    {
        get => Read(diffRevision);
        private set => Set(ref diffRevision, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:197"));
    }

    // :205
    public bool IsLoadingHistory { get => Read(isLoadingHistory); private set => Set(ref isLoadingHistory, value); }

    // :206-211
    public ValueList<FollowUpPrompt> FollowUps
    {
        get => Read(followUps);
        private set => Set(ref followUps, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:208"));
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
        set => Set(ref draft, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:231"));
    }

    // :238
    public bool DraftIsEmpty { get => Read(draftIsEmpty); private set => Set(ref draftIsEmpty, value); }

    // :239-243
    public bool IsTerminalVisible
    {
        get => Read(isTerminalVisible);
        set => Set(ref isTerminalVisible, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:242"));
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
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:342");

    // :345-352
    public HydraHeadInfo HydraLiveInfo(HydraHeadInfo stored) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:345");

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
    public void ShowDiff(Control anchor, Guid? turn, PreviewPanelEdge edge = PreviewPanelEdge.MaxY, IReadOnlyList<FileEdit>? focusEdits = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:383");

    // :394-396
    public void ClearDiffFocus() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:394");

    // :408-412
    public void ToggleDiff() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:408");

    // :505. @ObservationIgnored private(set) (R-OBS-5). Paths, external text (R-VAL-2).
    public ValueSet<string> HydraCurrentTurnTouchedPaths { get; private set; } = new([], SwiftStringComparer.Instance);

    // :540-555
    public ThreadRuntime(Guid threadID, AppModel app) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:540");

    // :641-643
    public Task EnsureLoaded() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:641");

    // :646-655. Swift: installContinuation(_:).
    public Task<bool> InstallContinuation(ThreadContinuation value) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:646");

    // :657
    public bool IsRunning =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:657");

    // :659
    public ChatThread? Thread =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:659");

    // :665-667
    public bool HasWorkingHeads =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:665");

    // :672-680
    public bool HasWorkingHelpers =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:672");

    // :685-688
    public bool HasWorkingNativeHeads =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:685");

    // :698-722
    public void TakeBackRunningTurn() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:698");

    // :724-729
    public ValueList<string> SentPrompts =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:724");

    // :731-736
    public bool HasSentPrompts =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:731");

    // :738-740
    public ApprovalRequest? PendingPlanApproval =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:738");

    // MARK: - Sending

    // :744-756
    public void Send() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:744");

    // :758-765. Swift: sendHydraBrief(_:attachments:).
    public void SendHydraBrief(string text, IReadOnlyList<Attachment> attachments) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:758");

    // MARK: - Follow-up queue

    // :772-774
    public bool CanQueue =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:772");

    // :781-789
    public void QueueDraftAsFollowUp() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:781");

    // :809-830
    public void InterruptAndSend() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:809");

    // :835-848. Swift: bundleFollowUp(_:onto:).
    public void BundleFollowUp(Guid id, Guid target) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:835");

    // :852-858
    public void UnbundleFollowUp(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:852");

    // :907-930. Swift: bundleText(_:).
    public static string BundleText(IReadOnlyList<FollowUpPrompt> members) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:907");

    // :954-990
    public void SendFollowUpNow(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:954");

    // :992-1009. Swift: enqueueFollowUp(text:attachments:).
    public void EnqueueFollowUp(string text, IReadOnlyList<Attachment> attachments) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:992");

    // :1074-1079
    public void RemoveFollowUp(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1074");

    // :1084-1098. Swift: moveFollowUp(_:to:placeAfter:), @discardableResult.
    public bool MoveFollowUp(Guid id, Guid target, bool placeAfter) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1085");

    // :1100-1112. Swift: updateFollowUp(_:text:attachments:).
    public void UpdateFollowUp(Guid id, string text, IReadOnlyList<Attachment> attachments) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1100");

    // :1141-1148
    public void ImplementPlan(string entryID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1141");

    // :1150-1156
    public void DismissPlan(string entryID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1150");

    // :1416-1428
    public void WarmSession() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1416");

    // :1691-1719
    public void Interrupt() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1691");

    // :1721-1731. Swift: resolve(_:option:).
    public void Resolve(ApprovalRequest request, ApprovalRequest.Option option) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1721");

    // :1733-1736. Swift: answer(_:answers:); the map is ProviderSession.AnswerQuestion's.
    public void Answer(QuestionRequest request, ValueMap<string, ValueList<string>> answers) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1733");

    // :1738-1757
    public void Compact() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1738");

    // :1765-1781. Swift: revertPreview(for:) -> Task<RevertPreview, Error> (a throwing TaskHandle, R-TASK-1).
    public TaskHandle<RevertPreview> RevertPreview(Guid turnID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1765");

    // :1785-1789. Swift: cachedRevertPreview(for:) -> Result<RevertPreview, Error>? (see the header).
    public Task<RevertPreview>? CachedRevertPreview(Guid turnID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1785");

    // :1820-1831. Swift: resend(_:text:attachments:restoreFiles:) async throws.
    public Task Resend(Guid turnID, string text, IReadOnlyList<Attachment> attachments, bool restoreFiles) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1820");

    // :1836-1936. Swift: revert(to:restoreFiles:) async throws -> Bool, @discardableResult.
    public Task<bool> Revert(Guid turnID, bool restoreFiles) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1837");

    // :1939
    public FileChangeSummary? ChangeStats { get => Read(changeStats); private set => Set(ref changeStats, value); }

    // :1945-1953
    public Task RefreshChangeStats() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:1945");

    // :2000-2002. Swift: hasCachedDiff(selection:).
    public bool HasCachedDiff(Guid? selection) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2000");

    // :2004-2011
    public ValueList<TurnRecord> TurnsWithChanges() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2004");

    // :2013-2050. Swift: parsedDiff(selection:providerFiles:).
    public Task<ValueList<DiffFile>> ParsedDiff(Guid? selection, IReadOnlyList<DiffFile>? providerFiles = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2013");

    // :2070-2077. Swift: observe(_:).
    public void Observe(ProviderSession provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2070");

    // :2098-2115
    public void StopSession() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2098");

    // MARK: - Hydra

    // :2485-2490
    public bool IsReadyForHydraMerge =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2485");

    // :2494-2504
    public void MarkHydraMerged(IReadOnlyList<Guid> turnIDs) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2494");

    // :2507-2509
    public ValueList<TurnRecord> HydraUnmergedTurns =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2507");

    // :2512-2517
    public void RecordHydraMerge(HydraMergeRecord record) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2512");

    // :2520-2522
    public long HydraUnmergedFileCount =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2520");

    // :2525-2527. Swift: hydraHead(forTool:).
    public Guid? HydraHead(string toolID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2525");

    // :2530-2532. Swift: rehearseHydraHead(toolID:headID:).
    public void RehearseHydraHead(string toolID, Guid headID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2530");

    // :2651-2654. Swift: stopNativeHead(_:).
    public Task<bool> StopNativeHead(string nativeID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2651");

    // :2660-2695. Swift: hydraHeadFinished(_:info:status:summary:landing:copyPath:).
    public void HydraHeadFinished(Guid headID, HydraHeadInfo info, HydraHeadInfo.Status status, string? summary,
        HydraLanding? landing = null, string? copyPath = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:2660");

    // MARK: - Timeline mutations

    // :3014-3018. Swift: appendHydraNote(_:id:).
    public void AppendHydraNote(string text, string? id = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3014");

    // :3501-3521. Swift: fileEdits(from:repositoryRoot:), nonisolated static @concurrent.
    public static Task<ValueList<FileEdit>> FileEdits(string patch, SwiftURL repositoryRoot) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3502");

    // MARK: - Persistence

    // :3581-3602
    public void ScheduleSave() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3581");

    // :3604-3620
    public void SaveNow() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3604");

    // :3624-3627
    public ThreadDocument? UnsavedSnapshot() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3624");

    // :3633-3640
    public bool HoldsOnlyPersistedState =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3633");

    // :3642-3648
    public void CancelPersistence() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3642");

    // :3650-3660
    public ThreadDocument SnapshotForPersistence() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3650");

    // MARK: - Rehearsal (extension ThreadRuntime, :3696-3758)

    // :3700-3723. Swift: rehearseTurn(_:touchedPaths:providerDiff:); `touchedPaths: [String] = []` is null here
    // for the empty default.
    public void RehearseTurn(string? text, IReadOnlyList<string>? touchedPaths = null, string? providerDiff = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3700");

    // :3727-3737
    public void RehearseBrief(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3727");

    // :3739-3741
    public void Rehearse(ProviderEvent @event) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3739");

    // :3744-3746
    public void NoteDiffChanged() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3744");

    // :3751-3757. Swift: storeLegacyChanges(_:turnID:).
    public void StoreLegacyChanges(FileChangeSummary changes, Guid turnID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/ThreadRuntime.swift:3751");
}
