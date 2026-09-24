using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Chat/ThreadTimeline.swift (B2 1fbf81e), :1-2314.
// SKELETON (cycle c100, group 3). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; an NSViewRepresentable/NSView is a Control; NSWindow is Avalonia's Window. An enum
// with associated values is an abstract record with nested sealed records (R-ENUM-2); where a case's labelled value
// is named like a member of the enum itself (`id`, `turnID`), the case redeclares that property with `new`, so the
// Swift label stays and both read the same value. `@Observable` classes are MainActorModel (R-OBS-6), their
// @ObservationIgnored storage plain auto-properties (R-OBS-5); `static let shared` is an Ambient (R-LIFE-2, the
// ScrollActivity.Shared precedent). A Task handle is TaskHandle; CFTimeInterval/TimeInterval are double; `[T]` in a
// value is ValueList<T>, `[K: V]` ValueMap, `Set<T>` ValueSet (R-VAL-2). A Swift argument label that is a C# keyword
// is written with `@` (MCPProxy.Url(string @for) precedent). The `extension EnvironmentValues` (:2238-2243) cannot
// be a static class named after the file (R-NAME-4, ED-095): that name is the view's, so its attached property
// lives on ThreadTimeline; RevealTimelineEndKey (:2234-2236) holds its default.
// NEEDS DECISION (group report): `@Environment(AppModel.self)` / `@Environment(WindowLiveResize.self)` are read by the
// bodies, not passed in, so they are no constructor parameters. SwiftUI's ScrollPhase, ScrollGeometry, ScrollPosition
// and GeometryProxy have no port types, so TimelineScrollTracking.notePhase(_:) (:1084-1111), ScrollMetrics.init
// (:1356-1374) and ThreadTimeline.visibleHeight (:882-884) are not declared yet. `[ObjectIdentifier]` (:1676, :1996)
// has no port type: TimelineBlockCache.Key keeps the entries themselves, to be compared by identity.
// The views' `static ==` (:26-35, :1870-1872, :2016-2018) stand for SwiftUI's `.equatable()`; not declared yet.
// Not declared yet, private in Swift and read by no other file: every view's @State/@Environment storage and private
// instance methods (ThreadTimeline :59-96, :157-878; WorkingBlockView.thinking :2291-2302) and the private storage of
// TimelineScrollTracking (:921-965), FrontMonitor (:1291-1293), TimelineBlockCache (:1657-1709) and TurnGroupCache
// (:1996-1997), which the implementing unit adds with the bodies. The Swift-private view types are public, as the App
// views ported so far do (ShortcutsSettingsPage.cs).

namespace Marrow.App.Views.Chat;

/// <summary>:4-885 <c>struct ThreadTimeline: View, Equatable</c>.</summary>
public sealed class ThreadTimeline : Decorator
{
    // :58 private static let resizeStep: CGFloat = 12
    private const double ResizeStep = 12;

    // :314 private static let softAppearLimit = 8
    private const int SoftAppearLimit = 8;

    /// <param name="runtime">:9 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="scrollChrome">:10 <c>let scrollChrome: ChromeScrollModel</c>.</param>
    /// <param name="scrollState">:11 <c>let scrollState: TimelineScrollState</c>.</param>
    /// <param name="projectName">:12 <c>let projectName: String?</c>.</param>
    /// <param name="workingDirectory">:15 <c>let workingDirectory: String?</c>.</param>
    /// <param name="supportsRewind">:17 <c>let supportsRewind: Bool</c>.</param>
    /// <param name="columnHeight">:20 <c>let columnHeight: CGFloat</c>.</param>
    /// <param name="showsMinimap">:22 <c>var showsMinimap = true</c>.</param>
    // :98-145 body
    public ThreadTimeline(
        ThreadRuntime runtime,
        ChromeScrollModel scrollChrome,
        TimelineScrollState scrollState,
        string? projectName,
        string? workingDirectory,
        bool supportsRewind,
        double columnHeight,
        bool showsMinimap = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:98");

    // :148-155 private static func mentionTargets(for:) -> [String: HydraMentionTarget]
    private static ValueMap<string, HydraMentionTarget> MentionTargets(IReadOnlyList<ChatThread> @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:148");

    // :307-311 private static func transition(for:) -> AnyTransition
    private static SwiftTransition Transition(DisplayBlock @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:307");

    // :317-321 static let rowTransition: AnyTransition
    public static SwiftTransition RowTransition =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:317");

    // :824-826 private static func jumpTolerance(_:) -> CGFloat
    private static double JumpTolerance(double viewportHeight) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:824");

    // :2238-2243 extension EnvironmentValues { var revealTimelineEnd: TimelineRevealBox? }, an inherited environment
    // value (R-VIEW-6, ED-095).
    public static readonly AttachedProperty<TimelineRevealBox?> RevealTimelineEndProperty =
        AvaloniaProperty.RegisterAttached<Control, TimelineRevealBox?>(
            "RevealTimelineEnd", typeof(ThreadTimeline), RevealTimelineEndKey.DefaultValue, inherits: true);

    // :2240 get
    public static TimelineRevealBox? GetRevealTimelineEnd(Control control) => control.GetValue(RevealTimelineEndProperty);

    // :2241 set
    public static void SetRevealTimelineEnd(Control control, TimelineRevealBox? value) =>
        control.SetValue(RevealTimelineEndProperty, value);
}

/// <summary>:887-1237 <c>@MainActor @Observable final class TimelineScrollTracking: ScrollActivityReporter</c>.</summary>
public sealed class TimelineScrollTracking : MainActorModel, ScrollActivityReporter
{
    private bool isReaderScrolling;   // :905 = false
    private string? activeBlockID;    // :919
    private int checkRequest;         // :947 = 0
    private int refreshRequest;       // :950 = 0
    private WeakReference<Control>? probe; // :953 weak

    // :898 @ObservationIgnored var isPinnedToBottom = true
    public bool IsPinnedToBottom { get; set; } = true;

    // :905 var isReaderScrolling = false
    public bool IsReaderScrolling { get => Read(isReaderScrolling); set => Set(ref isReaderScrolling, value); }

    // :908 @ObservationIgnored var settleWatch: Task<Void, Never>?
    public TaskHandle? SettleWatch { get; set; }

    // :910 @ObservationIgnored var isUserScrolling = false
    public bool IsUserScrolling { get; set; }

    // :912 @ObservationIgnored var lastMovementAt: CFTimeInterval = 0
    public double LastMovementAt { get; set; }

    // :915 @ObservationIgnored var isCoasting = false
    public bool IsCoasting { get; set; }

    // :919 private(set) var activeBlockID: String?
    public string? ActiveBlockID { get => Read(activeBlockID); private set => Set(ref activeBlockID, value); }

    // :928 @ObservationIgnored private(set) var offset: CGFloat = 0
    public double Offset { get; private set; }

    // :929 @ObservationIgnored private(set) var distanceFromBottom: CGFloat = 0
    public double DistanceFromBottom { get; private set; }

    // :930 @ObservationIgnored private(set) var travel: CGFloat = 0
    public double Travel { get; private set; }

    // :940 @ObservationIgnored var repairSkippedWhileScrolling = false
    public bool RepairSkippedWhileScrolling { get; set; }

    // :941 @ObservationIgnored var refreshSkippedWhileScrolling = false
    public bool RefreshSkippedWhileScrolling { get; set; }

    // :945 @ObservationIgnored var blankRepairs = 0
    public int BlankRepairs { get; set; }

    // :947 private(set) var checkRequest = 0
    public int CheckRequest { get => Read(checkRequest); private set => Set(ref checkRequest, value); }

    // :950 private(set) var refreshRequest = 0
    public int RefreshRequest { get => Read(refreshRequest); private set => Set(ref refreshRequest, value); }

    // :953 @ObservationIgnored weak var probe: NSView?
    public Control? Probe
    {
        get => probe is not null && probe.TryGetTarget(out var view) ? view : null;
        set => probe = value is null ? null : new WeakReference<Control>(value);
    }

    // :967 var isNudging: Bool
    public bool IsNudging =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:967");

    // :970-972 var showsNothing: Bool
    public bool ShowsNothing =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:970");

    // :976-979 var endUnseen: Bool
    public bool EndUnseen =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:976");

    // :982-991 @discardableResult func setOutline(_:) -> Bool
    public bool SetOutline(IReadOnlyList<(string Id, bool HasUserMessage)> blocks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:983");

    // :993-1007 func setVisible(_:_:)
    public void SetVisible(string id, bool isVisible) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:993");

    // :1009-1012 func clearVisible()
    public void ClearVisible() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1009");

    // :1021 var isArriving: Bool
    public bool IsArriving =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1021");

    // :1023-1025 func holdEnd(for:)
    public void HoldEnd(double @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1023");

    // :1028-1030 func noteReaderMovement()
    public void NoteReaderMovement() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1028");

    // :1032-1039 func noteGeometry(offset:distanceFromBottom:travel:)
    public void NoteGeometry(double offset, double distanceFromBottom, double travel) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1032");

    // :1049-1055 func reportActivity(timeline:)
    public void ReportActivity(Guid timeline) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1049");

    // :1058 var isScrollingNow: Bool (ScrollActivityReporter, RenderBudget.swift:74)
    public bool IsScrollingNow =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1058");

    // :1062-1073 func endActivity()
    public void EndActivity() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1062");

    // :1116-1123 func armBlankWatch()
    public void ArmBlankWatch() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1116");

    // :1128-1133 func noteReturnToFront()
    public void NoteReturnToFront() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1128");

    // :1139-1144 func noteDrift(_:tolerance:) -> Bool
    public bool NoteDrift(double amount, double tolerance) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1139");

    // :1146-1148 func resetDrift()
    public void ResetDrift() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1146");

    // :1153-1174 func armViewportRefresh()
    public void ArmViewportRefresh() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1153");

    // :1184-1199 func beginNudge() -> Bool
    public bool BeginNudge() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1184");

    // :1202-1204 func noteNudgeSeen()
    public void NoteNudgeSeen() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1202");

    // :1208-1226 func restoreNudge(then:)
    public void RestoreNudge(Action then) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1208");

    // :1230-1236 private func resolveActive()
    private void ResolveActive() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1230");
}

/// <summary>:1241-1271 <c>private struct ScrollViewProbe: NSViewRepresentable</c>.</summary>
public sealed class ScrollViewProbe : Control
{
    /// <param name="onResolve">:1242 <c>let onResolve: (NSView) -> Void</c>.</param>
    // :1244-1250 makeNSView / updateNSView
    public ScrollViewProbe(Action<Control> onResolve) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1244");

    /// <summary>:1252-1270 <c>private final class ProbeView: NSView</c>.</summary>
    private sealed class ProbeView : Control
    {
        // :1253 var onResolve: (NSView) -> Void
        public Action<Control> OnResolve { get; set; }

        // :1255-1258 init(onResolve:)
        public ProbeView(Action<Control> onResolve) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1255");
    }
}

/// <summary>:1281-1341 <c>@MainActor @Observable final class FrontMonitor</c>.</summary>
public sealed class FrontMonitor : MainActorModel
{
    private int revision;          // :1285 = 0
    private bool isVisible = true; // :1289

    // :1284 static let shared = FrontMonitor()
    public static Ambient<FrontMonitor> Shared { get; } = new(() => new FrontMonitor());

    // :1295-1321 private init()
    private FrontMonitor() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1295");

    // :1285 private(set) var revision = 0
    public int Revision { get => Read(revision); private set => Set(ref revision, value); }

    // :1289 private(set) var isVisible = true
    public bool IsVisible { get => Read(isVisible); private set => Set(ref isVisible, value); }

    // :1325-1329 func concerns(_:) -> Bool
    public bool Concerns(Avalonia.Controls.Window? window) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1325");

    // :1331-1335 private func bump(for:)
    private void Bump(Avalonia.Controls.Window? @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1331");

    // :1337-1340 private func refreshVisibility()
    private void RefreshVisibility() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1337");
}

/// <summary>:1346-1375 <c>private struct ScrollMetrics: Equatable</c>. The init(geometry:) (:1356-1374) waits on a
/// port type for ScrollGeometry.</summary>
public sealed record ScrollMetrics
{
    public double ContentHeight { get; init; }      // :1347
    public double ContainerHeight { get; init; }    // :1348
    public double DistanceFromBottom { get; init; } // :1349
    public double Travel { get; init; }             // :1350
    public double CenterY { get; init; }            // :1352
    public double Offset { get; init; }             // :1354
}

/// <summary>:1377-1450 <c>enum TimelineGroup: Identifiable, Equatable</c> (R-ENUM-2).</summary>
public abstract record TimelineGroup
{
    private TimelineGroup() { }

    // :1378 case single(TimelineEntry)
    public sealed record Single(TimelineEntry Value) : TimelineGroup;

    // :1381 case work(id: String, entries: [TimelineEntry], startsCollapsed: Bool)
    public sealed record Work(string Id, ValueList<TimelineEntry> Entries, bool StartsCollapsed) : TimelineGroup
    {
        public new string Id { get; init; } = Id;
    }

    // :1385 case heads(id: String, entries: [TimelineEntry])
    public sealed record Heads(string Id, ValueList<TimelineEntry> Entries) : TimelineGroup
    {
        public new string Id { get; init; } = Id;
    }

    // :1387-1393 var id: String
    public string Id =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1387");

    // :1396-1400 @MainActor static func isHead(_:) -> Bool
    public static bool IsHead(TimelineEntry entry) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1397");

    // :1402-1449 @MainActor static func build(_:showReasoning:) -> [TimelineGroup]
    public static ValueList<TimelineGroup> Build(IReadOnlyList<TimelineEntry> entries, bool showReasoning) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1403");
}

/// <summary>:1454 <c>struct HeadPills: Equatable</c>.</summary>
public sealed record HeadPills
{
    public ValueList<int> Earlier { get; init; } = ValueList<int>.Empty; // :1454 var earlier: [Int] = []
    public ValueList<int> Later { get; init; } = ValueList<int>.Empty;   // :1454 var later: [Int] = []
}

/// <summary>:1467-1644 <c>enum DisplayBlock: Identifiable, Equatable</c> (R-ENUM-2).</summary>
public abstract record DisplayBlock
{
    private DisplayBlock() { }

    // :1471 case turn(id: String, turnID: UUID, entries: [TimelineEntry], summary: TurnSummary?, showsWorking: Bool)
    public sealed record Turn(string Id, Guid TurnID, ValueList<TimelineEntry> Entries, TurnSummary? Summary, bool ShowsWorking) : DisplayBlock
    {
        public new string Id { get; init; } = Id;
        public new Guid TurnID { get; init; } = TurnID;
    }

    // :1474 case group(TimelineGroup, summary: TurnSummary?, hasReply: Bool)
    public sealed record Group(TimelineGroup Value, TurnSummary? Summary, bool HasReply) : DisplayBlock;

    // :1477 case working(turnID: UUID?, liveWork: [TimelineEntry])
    public sealed record Working(Guid? TurnID, ValueList<TimelineEntry> LiveWork) : DisplayBlock
    {
        public new Guid? TurnID { get; init; } = TurnID;
    }

    // :1484 case merging(id: String)
    public sealed record Merging(string Id) : DisplayBlock
    {
        public new string Id { get; init; } = Id;
    }

    // :1488 case headsWorking(heads: [Int], trailing: Bool)
    public sealed record HeadsWorking(ValueList<int> Heads, bool Trailing) : DisplayBlock;

    /// <summary>:1537-1544 <c>struct Built</c>.</summary>
    public sealed record Built(ValueList<DisplayBlock> Blocks, int StableCount, int LastRunStart); // :1538-1543

    // :1490-1498 var id: String
    public string Id =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1490");

    // :1501-1507 var turnID: UUID?
    public Guid? TurnID =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1501");

    // :1511-1517 var hasUserMessage: Bool
    public bool HasUserMessage =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1511");

    // :1523-1532 @MainActor var hydraMergePhase: HydraMergePhase?
    public HydraMergePhase? HydraMergePhase =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1524");

    // :1546-1549 @MainActor static func build(_:meta:showReasoning:isRunning:hydraMergeID:pills:) -> [DisplayBlock];
    // a null pills is the Swift default HeadPills().
    public static ValueList<DisplayBlock> Build(IReadOnlyList<TimelineEntry> entries, TimelineMeta meta, bool showReasoning, bool isRunning, string? hydraMergeID, HeadPills? pills = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1547");

    // :1553-1619 @MainActor static func build(_:from:keeping:meta:showReasoning:isRunning:hydraMergeID:pills:) -> Built
    public static Built Build(IReadOnlyList<TimelineEntry> entries, int from, IReadOnlyList<DisplayBlock> keeping, TimelineMeta meta, bool showReasoning, bool isRunning, string? hydraMergeID, HeadPills pills) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1554");

    // :1621-1624 private var carriesWorkingLine: Bool
    private bool CarriesWorkingLine =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1621");

    // :1630-1634 @MainActor private static func endsWithMergeOutcome(_:) -> Bool
    private static bool EndsWithMergeOutcome(IReadOnlyList<TimelineEntry> entries) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1631");

    // :1639-1643 static func isMergeOutcome(_:) -> Bool
    public static bool IsMergeOutcome(UserMessage message) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1639");
}

/// <summary>:1652-1787 <c>@MainActor final class TimelineBlockCache</c>.</summary>
public sealed class TimelineBlockCache
{
    // :1659-1673 @MainActor static func cache(for:) -> TimelineBlockCache
    public static TimelineBlockCache Cache(Guid @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1659");

    // :1711-1737 func blocks(for:showReasoning:isRunning:hydraMergeID:pills:) -> [DisplayBlock]; a null pills is the
    // Swift default HeadPills().
    public ValueList<DisplayBlock> Blocks(IReadOnlyList<TimelineEntry> @for, bool showReasoning, bool isRunning, string? hydraMergeID, HeadPills? pills = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1711");

    // :1745-1770 private func buildTail(_:appendedFrom:showReasoning:isRunning:hydraMergeID:pills:) -> DisplayBlock.Built?
    private DisplayBlock.Built? BuildTail(IReadOnlyList<TimelineEntry> entries, int appendedFrom, bool showReasoning, bool isRunning, string? hydraMergeID, HeadPills pills) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1745");

    // :1775-1786 func mentionPersonas(for:roster:) -> [HydraPersona]
    public ValueList<HydraPersona> MentionPersonas(IReadOnlyList<ChatThread> @for, bool roster) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1775");

    /// <summary>:1675-1697 <c>private struct Key: Equatable</c>. <c>entries: [ObjectIdentifier]</c> (:1676) keeps the
    /// entries, compared by identity by the implementing unit (no port type for ObjectIdentifier yet).</summary>
    private sealed record Key
    {
        public required IReadOnlyList<TimelineEntry> Entries { get; init; } // :1676
        public required int TurnEnds { get; init; }                         // :1680
        public required bool IsRunning { get; init; }                       // :1681
        public string? HydraMergeID { get; init; }                          // :1683
        public required HeadPills Pills { get; init; }                      // :1685
        public required bool ShowReasoning { get; init; }                   // :1686

        // :1689-1696 func extends(_:) -> Bool
        public bool Extends(Key old) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1689");
    }
}

/// <summary>:1795-1799 <c>enum TimelineWindow</c>.</summary>
public static class TimelineWindow
{
    public const int FirstPaint = 3; // :1796
    public const int Initial = 24;   // :1797
    public const int Page = 24;      // :1798
}

/// <summary>:1805-1852 <c>struct TimelineMeta</c>.</summary>
public sealed record TimelineMeta
{
    public ValueMap<Guid, string> LastAssistantIDByTurn { get; init; } = ValueMap<Guid, string>.Empty; // :1806
    public ValueMap<Guid, TurnSummary> SummaryByTurn { get; init; } = ValueMap<Guid, TurnSummary>.Empty; // :1807
    public ValueSet<Guid> TurnsWithReply { get; init; } = ValueSet<Guid>.Empty;                          // :1808

    // :1810-1815 @MainActor static func build(_:) -> TimelineMeta
    public static TimelineMeta Build(IReadOnlyList<TimelineEntry> entries) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1811");

    // :1819-1834 @MainActor mutating func add(_:); a mutating method returns the new value (R-VAL-1).
    public TimelineMeta Add(TimelineEntry entry) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1820");

    // :1838-1843 @MainActor func summary(for:) -> TurnSummary?
    public TurnSummary? Summary(TimelineGroup @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1839");

    // :1847-1851 @MainActor func hasReply(for:) -> Bool
    public bool HasReply(TimelineGroup @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1848");
}

/// <summary>:1857-1863 <c>struct RowContext: Equatable</c>.</summary>
/// <param name="WorkingDirectory">:1858 <c>var workingDirectory: String?</c>.</param>
/// <param name="CanRewind">:1860 <c>var canRewind: Bool</c>.</param>
/// <param name="Chronological">:1862 <c>var chronological: Bool</c>.</param>
public sealed record RowContext(string? WorkingDirectory, bool CanRewind, bool Chronological);

/// <summary>:1865-1925 <c>private struct DisplayBlockView: View, Equatable</c>.</summary>
public sealed class DisplayBlockView : Decorator
{
    /// <param name="block">:1866 <c>let block: DisplayBlock</c>.</param>
    /// <param name="runtime">:1867 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="context">:1868 <c>let context: RowContext</c>.</param>
    // :1884-1924 body
    public DisplayBlockView(DisplayBlock block, ThreadRuntime runtime, RowContext context) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1884");

    // :1874-1882 private static func splitTurnEntries(_:) -> (userEntries:, content:)
    private static (ValueList<TimelineEntry> UserEntries, ValueList<TimelineEntry> Content) SplitTurnEntries(IReadOnlyList<TimelineEntry> entries) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1874");
}

/// <summary>:1930-1990 <c>private struct TurnRunningBlock: View</c>.</summary>
public sealed class TurnRunningBlock : Decorator
{
    /// <param name="runtime">:1931 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="entries">:1932 <c>let entries: [TimelineEntry]</c>.</param>
    /// <param name="showsWorking">:1933 <c>let showsWorking: Bool</c>.</param>
    /// <param name="context">:1934 <c>let context: RowContext</c>.</param>
    // :1939-1989 body
    public TurnRunningBlock(ThreadRuntime runtime, ValueList<TimelineEntry> entries, bool showsWorking, RowContext context) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1939");
}

/// <summary>:1994-2007 <c>@MainActor private final class TurnGroupCache</c>.</summary>
public sealed class TurnGroupCache
{
    // :1999-2006 func groups(for:) -> [TimelineGroup]
    public ValueList<TimelineGroup> Groups(IReadOnlyList<TimelineEntry> @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:1999");
}

/// <summary>:2011-2025 <c>private struct TurnRow: View, Equatable</c>.</summary>
public sealed class TurnRow : Decorator
{
    /// <param name="group">:2012 <c>let group: TimelineGroup</c>.</param>
    /// <param name="runtime">:2013 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="context">:2014 <c>let context: RowContext</c>.</param>
    // :2020-2024 body
    public TurnRow(TimelineGroup group, ThreadRuntime runtime, RowContext context) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:2020");
}

/// <summary>:2027-2058 <c>struct TimelineGroupView: View</c>.</summary>
public sealed class TimelineGroupView : Decorator
{
    /// <param name="group">:2028 <c>let group: TimelineGroup</c>.</param>
    /// <param name="runtime">:2029 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="summary">:2031 <c>let summary: TurnSummary?</c>.</param>
    /// <param name="hasReply">:2033 <c>let hasReply: Bool</c>.</param>
    /// <param name="context">:2034 <c>let context: RowContext</c>.</param>
    // :2036-2057 body
    public TimelineGroupView(TimelineGroup group, ThreadRuntime runtime, TurnSummary? summary, bool hasReply, RowContext context) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:2036");
}

/// <summary>:2063-2186 <c>private struct WorkingIndicator: View</c>.</summary>
public sealed class WorkingIndicator : Decorator
{
    // :2083 private static let change = Animation.smooth(duration: 0.3)
    private static Animation Change => Animation.Smooth(duration: 0.3);

    // :2085 private static let cardWidth: CGFloat = 380
    private const double CardWidth = 380;

    /// <param name="runtime">:2064 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="startedAt">:2065 <c>let startedAt: Date</c>.</param>
    /// <param name="seed">:2066 <c>let seed: UInt64</c>.</param>
    /// <param name="thinkingSteps">:2068 <c>let thinkingSteps: [ThinkingStep]</c>.</param>
    /// <param name="liveWork">:2070 <c>let liveWork: [TimelineEntry]</c>.</param>
    /// <param name="turnEntries">:2071 <c>let turnEntries: [TimelineEntry]</c>.</param>
    /// <param name="showsCard">:2073 <c>let showsCard: Bool</c>.</param>
    /// <param name="finishedAt">:2074 <c>var finishedAt: Date?</c>.</param>
    /// <param name="chronological">:2075 <c>var chronological = false</c>.</param>
    /// <param name="workingDirectory">:2076 <c>var workingDirectory: String?</c>.</param>
    // :2087-2185 body
    public WorkingIndicator(
        ThreadRuntime runtime,
        DateTimeOffset startedAt,
        ulong seed,
        ValueList<ThinkingStep> thinkingSteps,
        ValueList<TimelineEntry> liveWork,
        ValueList<TimelineEntry> turnEntries,
        bool showsCard,
        DateTimeOffset? finishedAt = null,
        bool chronological = false,
        string? workingDirectory = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:2087");
}

/// <summary>:2189-2192 <c>struct ThinkingStep: Equatable</c>.</summary>
/// <param name="Text">:2190 <c>var text: String</c>.</param>
/// <param name="IsStreaming">:2191 <c>var isStreaming: Bool</c>.</param>
public sealed record ThinkingStep(string Text, bool IsStreaming);

/// <summary>:2195-2215 <c>@MainActor @Observable final class TimelineScrollState</c>.</summary>
public sealed class TimelineScrollState : MainActorModel
{
    private bool showsJumpButton; // :2198 = false
    private int jumpRequest;      // :2199 = 0
    private int areaGrowthRequest; // :2204 = 0

    // :2198 var showsJumpButton = false
    public bool ShowsJumpButton { get => Read(showsJumpButton); set => Set(ref showsJumpButton, value); }

    // :2199 private(set) var jumpRequest = 0
    public int JumpRequest { get => Read(jumpRequest); private set => Set(ref jumpRequest, value); }

    // :2204 private(set) var areaGrowthRequest = 0
    public int AreaGrowthRequest { get => Read(areaGrowthRequest); private set => Set(ref areaGrowthRequest, value); }

    // :2206-2208 func jumpToLatest()
    public void JumpToLatest() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:2206");

    // :2212-2214 func noteComposerAreaGrowth()
    public void NoteComposerAreaGrowth() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:2212");
}

/// <summary>:2222-2232 <c>@MainActor final class TimelineRevealBox: Equatable</c>. Swift's <c>==</c> is identity
/// (<c>===</c>, :2229-2231), which is a C# class's default equality.</summary>
public sealed class TimelineRevealBox
{
    // :2224 var action: () -> Void = {}
    public Action Action { get; set; } = () => { };

    // :2227 var follow: () -> Void = {}
    public Action Follow { get; set; } = () => { };
}

/// <summary>:2234-2236 <c>private struct RevealTimelineEndKey: EnvironmentKey</c>: the default of
/// ThreadTimeline.RevealTimelineEndProperty.</summary>
file static class RevealTimelineEndKey
{
    // :2235 static let defaultValue: TimelineRevealBox? = nil
    public static TimelineRevealBox? DefaultValue => null;
}

/// <summary>:2249-2303 <c>private struct WorkingBlockView: View</c>.</summary>
public sealed class WorkingBlockView : Decorator
{
    /// <param name="runtime">:2251 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="liveWork">:2253 <c>let liveWork: [TimelineEntry]</c>.</param>
    /// <param name="workingDirectory">:2254 <c>let workingDirectory: String?</c>.</param>
    /// <param name="chronological">:2255 <c>var chronological = false</c>.</param>
    /// <param name="earlierEntries">:2258 <c>var earlierEntries: [TimelineEntry]?</c>.</param>
    // :2260-2274 body
    public WorkingBlockView(ThreadRuntime runtime, ValueList<TimelineEntry> liveWork, string? workingDirectory, bool chronological = false, ValueList<TimelineEntry>? earlierEntries = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:2260");

    // :2278-2287 private static func turnEntries(of:) -> [TimelineEntry]
    private static ValueList<TimelineEntry> TurnEntries(ThreadRuntime of) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:2278");
}

/// <summary>:2308-2314 <c>private struct ScrollFreeze: ViewModifier</c>.</summary>
/// <param name="Tracking">:2309 <c>let tracking: TimelineScrollTracking</c>.</param>
public sealed record ScrollFreeze(TimelineScrollTracking Tracking)
{
    // :2311-2313 func body(content:)
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ThreadTimeline.swift:2311");
}
