using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift (B2 1fbf81e), :1-2696.
// Skeleton (c100 group 5): every type the file declares, the views' inputs, the record shapes, the metrics constants
// and the members the other files use. Every view body and every member with behaviour is left to the implementing
// unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record
// with `Control Body(Control content)`; a Shape is a sealed record with `Geometry Path(Rect in)`; a caseless enum is a
// static class; an enum with associated values is an abstract record (R-ENUM-2), an unlabelled payload named Value; a
// parameter without a default that follows one with a default moves before it (ED-095), so callers name their
// arguments. UUID is Guid, Date DateTimeOffset, CGFloat double, a Swift Int long (R-NUM-4), CGRect/CGPoint Avalonia
// Rect/Point, Color SwiftColor, AnyTransition SwiftTransition, `[T]` in a value ValueList<T>, `[K: V]` ValueMap and
// `Set<T>` ValueSet (R-VAL-2). Swift `private` top-level types are `internal` (C# has no private top-level type).
// `@Environment(AppModel.self)` is read by the bodies, so it is no constructor parameter (the question group 3 raised).
// Renames: SidebarItem's `static func id(for:)` is IdFor (the record's own `Id` property takes the name), and its
// `kind` property is SidebarItemKind (R-NAME-3, type SidebarItem.Kind). The file's two library extensions (:1059-1063
// on ChatThread, :1065-1077 on AnyTransition) go in SidebarViewExtensions, as C# 14 extension blocks (ED-164): the
// file's own name belongs to the view.
// Not declared yet (private, read only by the bodies the implementing unit writes): SidebarView's @State storage
// (:12-59, :384) and its private instance helpers (:61-63, :206-305, :316-898), the rows' @State storage, their
// private helpers (ProjectRow.actions :1206-1230, SidebarHelperRow.makeActions :1423-1425, SidebarThreadRow :1896-2001),
// HelperConnector.style (:1643, a StrokeStyle with a dash pattern, no port type yet) and the private storage of
// PlacementCache (:2527-2528), SidebarItemCache (:2610-2619), RowFrames (:2458) and MinuteClock.tick (:2672).
// The rows' custom `==` (SidebarHelperRow :1414-1421, SidebarThreadRow :1883-1892) stand for `.equatable()`; a
// Control keeps reference equality, so they are not declared as Equals (NEEDS DECISION, as group 3 reports).

namespace Marrow.App.Views.Sidebar;

/// <summary>:4-899 <c>struct SidebarView: View</c>: the thread list, in the project or the activity layout.</summary>
public sealed class SidebarView : Decorator
{
    // :67 private nonisolated static let listSpace = "sidebar-list"
    private const string ListSpace = "sidebar-list";

    // :766 private static let slide = Animation.spring(response: 0.28, dampingFraction: 0.82)
    private static Animation Slide { get; } = Animation.Spring(response: 0.28, dampingFraction: 0.82);

    /// <param name="inPopover">:9 <c>var inPopover = false</c>.</param>
    /// <param name="dismiss">:10 <c>var dismiss: (() -> Void)? = nil</c>.</param>
    // :69-204 body
    public SidebarView(bool inPopover = false, Action? dismiss = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:69");

    // :477-484 fileprivate static func orderedHeads(_ heads: [ChatThread]) -> [ChatThread]
    public static ValueList<ChatThread> OrderedHeads(IReadOnlyList<ChatThread> heads) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:477");

    /// <summary>:307-310 <c>private struct ConfirmedDeletion</c>.</summary>
    private sealed record ConfirmedDeletion
    {
        public required Guid ThreadID { get; init; } // :308
        public required bool RemoveWorktree { get; init; } // :309
    }

    /// <summary>:637-640 <c>private struct ActivityGroup</c>.</summary>
    private sealed record ActivityGroup
    {
        public required string Title { get; init; } // :638
        public required ValueList<ChatThread> Threads { get; init; } // :639 var threads
    }

    /// <summary>:859-869 <c>private struct DragPeers</c>.</summary>
    private sealed record DragPeers
    {
        public ValueList<Guid> Order { get; } = ValueList<Guid>.Empty; // :860
        public ValueList<Guid>? Group { get; } // :861
        public ValueMap<Guid, double> Heights { get; } = ValueMap<Guid, double>.Empty; // :862

        // :864-868 init(_ peers: (order: [UUID], group: [UUID]?), heights: ([UUID]) -> [UUID: CGFloat])
        public DragPeers((ValueList<Guid> Order, ValueList<Guid>? Group) peers, Func<ValueList<Guid>, ValueMap<Guid, double>> heights) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:864");
    }
}

/// <summary>:906-964 <c>private struct ArchiveThreadPopover: View</c>: the archive shortcut's question.</summary>
internal sealed class ArchiveThreadPopover : Decorator
{
    /// <param name="thread">:907 <c>let thread: ChatThread</c>.</param>
    /// <param name="onArchive">:908 <c>let onArchive: () -> Void</c>.</param>
    /// <param name="onDelete">:909 <c>let onDelete: () -> Void</c>.</param>
    /// <param name="onDismiss">:910 <c>let onDismiss: () -> Void</c>.</param>
    // :915-963 body
    public ArchiveThreadPopover(ChatThread thread, Action onArchive, Action onDelete, Action onDismiss) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:915");
}

/// <summary>:968-984 <c>private struct DeleteThreadPopover: View</c>.</summary>
internal sealed class DeleteThreadPopover : Decorator
{
    /// <param name="thread">:969 <c>let thread: ChatThread</c>.</param>
    /// <param name="onDelete">:970 <c>let onDelete: (_ removeWorktree: Bool) -> Void</c>.</param>
    // :972-983 body
    public DeleteThreadPopover(ChatThread thread, Action<bool> onDelete) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:972");
}

/// <summary>:988-1026 <c>private struct RenameThreadPopover: View</c>.</summary>
internal sealed class RenameThreadPopover : Decorator
{
    /// <param name="title">:989 <c>let title: String</c>.</param>
    /// <param name="onRename">:990 <c>let onRename: (String) -> Void</c>.</param>
    /// <param name="onCancel">:991 <c>let onCancel: () -> Void</c>.</param>
    // :996-1001 init; :1003-1025 body
    public RenameThreadPopover(string title, Action<string> onRename, Action onCancel) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:996");
}

/// <summary>:1031-1057 <c>private struct SidebarItem: Identifiable</c>: one entry of the sidebar list.</summary>
internal sealed record SidebarItem
{
    /// <summary>:1032-1049 <c>enum Kind</c> (R-ENUM-2).</summary>
    public abstract record Kind
    {
        private Kind() { }

        public sealed record Gap(double Value) : Kind; // :1033

        public sealed record Project(Marrow.Core.Models.Project Value, long Count) : Kind; // :1035

        public sealed record Header(string Value, bool IsFirst) : Kind; // :1036

        public sealed record SettledHeader(long Count, bool IsFirst) : Kind; // :1039

        // :1042
        public sealed record Thread(ChatThread Value, string? ProjectName, ProjectIcon? ProjectIcon, ValueList<Guid>? Peers, bool HasHelpers) : Kind;

        public sealed record Helper(ChatThread Value, bool IsLast) : Kind; // :1044

        public sealed record HeadCards(ChatThread Parent, ValueList<ChatThread> Heads) : Kind; // :1046

        public sealed record HelperStub(ChatThread Parent, ValueList<ChatThread> Helpers) : Kind; // :1048
    }

    public required string Id { get; init; } // :1051

    public required Kind SidebarItemKind { get; init; } // :1052 Swift: kind (R-NAME-3)

    // :1054-1056 static func id(for thread: ChatThread) -> String. Swift: id(for:); `Id` is the record's property.
    public static string IdFor(ChatThread thread) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1054");
}

/// <summary>The file's extensions of types it does not own (R-NAME-4, ED-164): <c>private extension ChatThread</c>
/// (:1059-1063) and <c>private extension AnyTransition</c> (:1065-1077).</summary>
internal static class SidebarViewExtensions
{
    extension(ChatThread thread)
    {
        // :1060-1062 var sidebarItemID: String
        public string SidebarItemID =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1060");
    }

    extension(SwiftTransition)
    {
        // :1071-1076 static var sidebarRow: AnyTransition
        public static SwiftTransition SidebarRow =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1071");
    }
}

/// <summary>:1079-1231 <c>private struct ProjectRow: View</c>.</summary>
internal sealed class ProjectRow : Decorator
{
    /// <param name="snapshot">:1082 <c>let snapshot: Project</c>.</param>
    /// <param name="count">:1089 <c>let count: Int</c>.</param>
    /// <param name="togglesExpansion">:1090 <c>var togglesExpansion = true</c>.</param>
    // :1094-1164 body
    public ProjectRow(Project snapshot, long count, bool togglesExpansion = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1094");

    // :1169-1204 private static func contextMenuActions(project:isActive:hasOverride:model:) -> [RowAction]
    private static ValueList<RowAction> ContextMenuActions(Project project, bool isActive, bool hasOverride, AppModel model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1169");
}

/// <summary>:1234-1237 <c>private struct ProjectPairChoice: Identifiable</c>.</summary>
internal sealed record ProjectPairChoice
{
    public required Guid Id { get; init; } // :1235
    public required string Title { get; init; } // :1236
}

/// <summary>:1242-1271 <c>private struct ProjectPairMenuItems: View</c>.</summary>
internal sealed class ProjectPairMenuItems : Decorator
{
    /// <param name="projectID">:1243 <c>let projectID: UUID</c>.</param>
    /// <param name="selectedID">:1244 <c>let selectedID: UUID?</c>.</param>
    /// <param name="pairs">:1245 <c>let pairs: [ProjectPairChoice]</c>.</param>
    /// <param name="model">:1246 <c>let model: AppModel</c>.</param>
    // :1248-1270 body
    public ProjectPairMenuItems(Guid projectID, Guid? selectedID, IReadOnlyList<ProjectPairChoice> pairs, AppModel model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1248");
}

/// <summary>:1273-1313 <c>private enum ThreadRowMetrics</c>.</summary>
internal static class ThreadRowMetrics
{
    public const double DetailedVerticalPadding = 7; // :1274
    public const double DetailedHeight = 48; // :1276
    public const double HelperHeight = 24; // :1278
    public const double SettledHeight = 24; // :1280
    public const double SettledGap = 4; // :1282
    public const double SettledCheckInset = 6; // :1285
    public const double OpenCheckInset = 26; // :1286
    public const double FoldInset = 46; // :1289
    public const double SettledTitleInset = 22; // :1290
    public const double ConnectorWidth = 28; // :1292
    public const double ConnectorLineX = 18; // :1293
    public const double HeadGlyphSize = 14; // :1296
    public const double HeadGlyphPitch = 22; // :1298
    public const double HeadBadgeOverhang = 3; // :1302
    public const double HeadRowGap = 6; // :1304
    public const double HeadCardPadding = 6; // :1305

    // :1309-1312 static func headCardHeight(rows: Int) -> CGFloat
    public static double HeadCardHeight(long rows) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1309");
}

/// <summary>:1318-1426 <c>private struct SidebarHelperRow: View, Equatable</c>.</summary>
internal sealed class SidebarHelperRow : Decorator
{
    /// <param name="snapshot">:1321 <c>let snapshot: ChatThread</c>.</param>
    /// <param name="isLast">:1327 <c>let isLast: Bool</c>.</param>
    /// <param name="menuRequests">:1331 <c>let menuRequests: SidebarMenuRequests</c>.</param>
    /// <param name="onFold">:1332 <c>let onFold: () -> Void</c>.</param>
    /// <param name="onRename">:1333 <c>let onRename: () -> Void</c>.</param>
    /// <param name="onDelete">:1334 <c>let onDelete: () -> Void</c>.</param>
    // :1339-1410 body
    public SidebarHelperRow(ChatThread snapshot, bool isLast, SidebarMenuRequests menuRequests, Action onFold, Action onRename, Action onDelete) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1339");
}

/// <summary>:1428-1549 <c>private struct HeadCardsRow: View</c>: the parent's Hydra heads as glyph cards.</summary>
internal sealed class HeadCardsRow : Decorator
{
    /// <param name="parentSnapshot">:1430 <c>let parentSnapshot: ChatThread</c>.</param>
    /// <param name="headSnapshots">:1431 <c>let headSnapshots: [ChatThread]</c>.</param>
    /// <param name="menuRequests">:1432 <c>let menuRequests: SidebarMenuRequests</c>.</param>
    /// <param name="onFold">:1433 <c>let onFold: () -> Void</c>.</param>
    // :1441-1543 body
    public HeadCardsRow(ChatThread parentSnapshot, IReadOnlyList<ChatThread> headSnapshots, SidebarMenuRequests menuRequests, Action onFold) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1441");

    // :1546-1548 private static func chunk(_ heads: [ChatThread], by size: Int) -> [[ChatThread]]
    private static ValueList<ValueList<ChatThread>> Chunk(IReadOnlyList<ChatThread> heads, long by) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1546");
}

/// <summary>:1553-1619 <c>private struct HelperStubRow: View</c>: a parent's folded helpers as one line.</summary>
internal sealed class HelperStubRow : Decorator
{
    /// <param name="parentSnapshot">:1556 <c>let parentSnapshot: ChatThread</c>.</param>
    /// <param name="helperSnapshots">:1559 <c>let helperSnapshots: [ChatThread]</c>.</param>
    /// <param name="action">:1560 <c>let action: () -> Void</c>.</param>
    // :1569-1618 body
    public HelperStubRow(ChatThread parentSnapshot, IReadOnlyList<ChatThread> helperSnapshots, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1569");
}

/// <summary>:1623-1633 <c>private struct RidesWithDraggedParent: ViewModifier</c>.</summary>
/// <param name="ParentID">:1624 <c>let parentID: UUID?</c>.</param>
/// <param name="Drag">:1625 <c>let drag: RowDrag&lt;UUID&gt;</c>.</param>
internal sealed record RidesWithDraggedParent(Guid? ParentID, RowDrag<Guid> Drag)
{
    // :1627-1632 func body(content: Content) -> some View
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1627");
}

/// <summary>:1637-1661 <c>private struct HelperConnector: View</c>: the dotted connector beside a helper's row.</summary>
internal sealed class HelperConnector : Decorator
{
    /// <param name="endsHere">:1638 <c>let endsHere: Bool</c>.</param>
    /// <param name="action">:1639 <c>let action: () -> Void</c>.</param>
    // :1649-1660 body
    public HelperConnector(bool endsHere, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1649");

    // :1645-1647 static func color(hovering: Bool) -> Color
    public static SwiftColor Color(bool hovering) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1645");
}

/// <summary>:1663-1676 <c>private struct HelperConnectorShape: Shape</c>.</summary>
/// <param name="EndsHere">:1664 <c>let endsHere: Bool</c>.</param>
internal sealed record HelperConnectorShape(bool EndsHere)
{
    // :1666-1675 func path(in rect: CGRect) -> Path
    public Geometry Path(Rect @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1666");
}

/// <summary>:1683-2002 <c>private struct SidebarThreadRow: View, Equatable</c>: a thread in either layout.</summary>
internal sealed class SidebarThreadRow : Decorator
{
    /// <param name="snapshot">:1687 <c>let snapshot: ChatThread</c>.</param>
    /// <param name="projectName">:1693 <c>let projectName: String?</c>.</param>
    /// <param name="projectIcon">:1697 <c>let projectIcon: ProjectIcon?</c>.</param>
    /// <param name="menuRequests">:1701 <c>let menuRequests: SidebarMenuRequests</c>.</param>
    /// <param name="onRename">:1708 <c>let onRename: () -> Void</c> (moved before the defaulted inputs, ED-095).</param>
    /// <param name="onDelete">:1709 <c>let onDelete: () -> Void</c> (moved before the defaulted inputs, ED-095).</param>
    /// <param name="isDragged">:1703 <c>var isDragged = false</c>.</param>
    /// <param name="listFrame">:1705 <c>var listFrame: FrameHolder?</c>.</param>
    /// <param name="onToggleFold">:1707 <c>var onToggleFold: (() -> Void)?</c>.</param>
    // :1717-1873 body
    public SidebarThreadRow(
        ChatThread snapshot,
        string? projectName,
        ProjectIcon? projectIcon,
        SidebarMenuRequests menuRequests,
        Action onRename,
        Action onDelete,
        bool isDragged = false,
        FrameHolder? listFrame = null,
        Action? onToggleFold = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1717");

    // :1875-1878 private static func fill(isSelected: Bool, isHovering: Bool) -> Double
    private static double Fill(bool isSelected, bool isHovering) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1875");

    // :1896-1899 private static func trailingClearance(isSettled:isDetailed:showsActions:showsFold:) -> CGFloat
    private static double TrailingClearance(bool isSettled, bool isDetailed, bool showsActions, bool showsFold = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1896");

    // :1982-1985 static func mergeStage(of runtime: ThreadRuntime?) -> String?
    public static string? MergeStage(ThreadRuntime? of) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1982");
}

/// <summary>:2007-2080 <c>private struct ThreadRowFace&lt;Badge: View&gt;: View</c>: what a thread row shows, drawn
/// by the live row and by its ghosts.</summary>
internal sealed class ThreadRowFace : Decorator
{
    /// <param name="thread">:2008 <c>let thread: ChatThread</c>.</param>
    /// <param name="projectName">:2010 <c>let projectName: String?</c>.</param>
    /// <param name="isSelected">:2012 <c>let isSelected: Bool</c> (moved before the defaulted inputs, ED-095).</param>
    /// <param name="trailingClearance">:2014 <c>let trailingClearance: CGFloat</c> (moved, ED-095).</param>
    /// <param name="badge">:2018 <c>@ViewBuilder let badge: Badge</c> (moved, ED-095).</param>
    /// <param name="projectIcon">:2011 <c>var projectIcon: ProjectIcon? = nil</c>.</param>
    /// <param name="mergeStage">:2016 <c>var mergeStage: String? = nil</c>.</param>
    // :2020-2073 body
    public ThreadRowFace(
        ChatThread thread,
        string? projectName,
        bool isSelected,
        double trailingClearance,
        Func<Control> badge,
        ProjectIcon? projectIcon = null,
        string? mergeStage = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2020");

    // :2076-2079 private static func titleOpacity(isSettled: Bool, isSelected: Bool) -> Double
    private static double TitleOpacity(bool isSettled, bool isSelected) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2076");
}

/// <summary>:2084-2110 <c>private struct ThreadGhostBadge: View</c>: the badge as a ghost carries it.</summary>
internal sealed class ThreadGhostBadge : Decorator
{
    public ChatThread Thread { get; } // :2085
    public bool NeedsInput { get; } // :2086
    public bool IsRunning { get; } // :2087

    // :2089-2093 init(thread:runtime:); :2095-2109 body
    public ThreadGhostBadge(ChatThread thread, ThreadRuntime? runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2089");
}

/// <summary>:2115-2142 <c>private struct SettleCheck: View</c>.</summary>
internal sealed class SettleCheck : Decorator
{
    /// <param name="isSettled">:2116 <c>let isSettled: Bool</c>.</param>
    /// <param name="isPopping">:2118 <c>let isPopping: Bool</c>.</param>
    /// <param name="action">:2119 <c>let action: () -> Void</c>.</param>
    // :2123-2141 body
    public SettleCheck(bool isSettled, bool isPopping, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2123");
}

/// <summary>:2144-2176 <c>private struct ThreadBadge: View</c>.</summary>
internal sealed class ThreadBadge : Decorator
{
    /// <param name="thread">:2146 <c>let thread: ChatThread</c>.</param>
    // :2148-2175 body
    public ThreadBadge(ChatThread thread) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2148");
}

/// <summary>:2185-2189 <c>@Observable private final class SidebarMenuRequests</c>: context-menu intents carried
/// outside any row's state.</summary>
internal sealed class SidebarMenuRequests : ObservableModel
{
    private ChatThread? rename; // :2187
    private ChatThread? delete; // :2188

    // :2187 var rename: ChatThread?
    public ChatThread? Rename
    {
        get => Read(rename);
        set => Set(ref rename, value);
    }

    // :2188 var delete: ChatThread?
    public ChatThread? Delete
    {
        get => Read(delete);
        set => Set(ref delete, value);
    }
}

/// <summary>:2192-2314 <c>@MainActor private enum ThreadActions</c>: what a thread row offers.</summary>
internal static class ThreadActions
{
    // :2194-2247 static func make(model:thread:onRename:onDelete:onSettle:onReopen:) -> [RowAction]
    public static ValueList<RowAction> Make(AppModel model, ChatThread thread, Action onRename, Action onDelete, Action? onSettle = null, Action? onReopen = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2194");

    // :2254-2313 static func makeContextMenu(model:thread:settleFirst:requests:) -> [RowAction]
    public static ValueList<RowAction> MakeContextMenu(AppModel model, ChatThread thread, bool settleFirst, SidebarMenuRequests requests) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2254");
}

/// <summary>:2317-2344 <c>private struct ActivityViewToggle: View</c>.</summary>
internal sealed class ActivityViewToggle : Decorator
{
    /// <param name="isOn">:2318 <c>@Binding var isOn: Bool</c>.</param>
    // :2321-2343 body
    public ActivityViewToggle(Binding<bool> isOn) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2321");
}

/// <summary>:2346-2359 <c>private struct ActivityHeader: View</c>.</summary>
internal sealed class ActivityHeader : Decorator
{
    /// <param name="title">:2347 <c>let title: String</c>.</param>
    /// <param name="isFirst">:2348 <c>let isFirst: Bool</c>.</param>
    // :2350-2358 body
    public ActivityHeader(string title, bool isFirst) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2350");
}

/// <summary>:2364-2407 <c>private struct SettledHeader: View</c>.</summary>
internal sealed class SettledHeader : Decorator
{
    /// <param name="count">:2366 <c>let count: Int</c>.</param>
    /// <param name="isFirst">:2367 <c>let isFirst: Bool</c>.</param>
    // :2369-2406 body
    public SettledHeader(long count, bool isFirst) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2369");
}

/// <summary>:2409-2429 <c>private struct ActivityStatus: View</c>.</summary>
internal sealed class ActivityStatus : Decorator
{
    /// <param name="thread">:2411 <c>let thread: ChatThread</c>.</param>
    // :2413-2428 body
    public ActivityStatus(ChatThread thread) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2413");
}

/// <summary>:2434-2441 <c>private final class FrameHolder</c>: a row's latest frame, kept out of observation.</summary>
internal sealed class FrameHolder
{
    // :2435 var frame: CGRect = .zero
    public Rect Frame { get; set; }

    // :2437-2440 func note(_ frame: CGRect)
    public void Note(Rect frame) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2437");
}

/// <summary>:2446-2453 <c>private final class RowHeights</c>: every row's measured height.</summary>
internal sealed class RowHeights
{
    // :2447 var values: [String: CGFloat] = [:]
    public ValueMap<string, double> Values { get; set; } = ValueMap<string, double>.Empty;

    // :2449-2452 func note(_ id: String, _ height: CGFloat)
    public void Note(string id, double height) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2449");
}

/// <summary>:2457-2473 <c>private final class RowFrames</c>: every open thread row's frame in the list.</summary>
internal sealed class RowFrames
{
    // :2460-2463 func note(_ id: UUID, _ frame: CGRect)
    public void Note(Guid id, Rect frame) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2460");

    // :2465-2467 func forget(_ id: UUID)
    public void Forget(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2465");

    // :2470-2472 func thread(at point: CGPoint) -> UUID?
    public Guid? Thread(Point at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2470");
}

/// <summary>:2476-2480 <c>private struct SidebarSearchResult</c>: one project's search hits.</summary>
internal sealed record SidebarSearchResult
{
    public required Project Project { get; init; } // :2477
    public required ValueList<ChatThread> Threads { get; init; } // :2478
    public required long Count { get; init; } // :2479
}

/// <summary>:2486-2537 <c>private final class PlacementCache</c>: the placements, kept while a fingerprint of the
/// threads stands.</summary>
internal sealed class PlacementCache
{
    /// <summary>:2487-2525 <c>private struct Fingerprint: Equatable</c>.</summary>
    private sealed record Fingerprint
    {
        public Guid Id { get; } // :2488
        public Guid ProjectID { get; } // :2489
        public Guid? ParentThreadID { get; } // :2490
        public bool IsArchived { get; } // :2491
        public bool IsInPanel { get; } // :2492
        public bool IsSettled { get; } // :2493
        public DateTimeOffset? SettledAt { get; } // :2494
        public bool IsPinned { get; } // :2495
        public double? SortOrder { get; } // :2496
        public double? ActivityOrder { get; } // :2497
        public DateTimeOffset? ActivityOrderDay { get; } // :2498
        public DateTimeOffset CreatedAt { get; } // :2499
        public DateTimeOffset UpdatedAt { get; } // :2500
        public bool FoldsHelpers { get; } // :2501
        public bool IsHydraHead { get; } // :2502
        public long? HydraIndex { get; } // :2503
        public bool HasUnread { get; } // :2504

        // :2506-2524 init(_ thread: ChatThread)
        public Fingerprint(ChatThread thread) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2506");
    }

    // :2530-2536 func placements(for threads: [ChatThread]) -> [ThreadPlacement]
    public ValueList<ThreadPlacement> Placements(IReadOnlyList<ChatThread> @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2530");
}

/// <summary>:2544-2583 <c>private struct ThreadPlacement: Equatable</c>: the fields that decide where a thread sits.</summary>
internal sealed record ThreadPlacement
{
    public Guid Id { get; } // :2545
    public Guid ProjectID { get; } // :2546
    public Guid? ParentThreadID { get; } // :2547
    public bool IsArchived { get; } // :2548
    public bool IsInPanel { get; } // :2549
    public bool IsSettled { get; } // :2550
    public DateTimeOffset? SettledAt { get; } // :2551
    public bool IsPinned { get; } // :2552
    public double? SortOrder { get; } // :2553
    public double? ActivityOrder { get; } // :2555
    public DateTimeOffset? ActivityOrderDay { get; } // :2556
    public DateTimeOffset CreatedAt { get; } // :2557
    public DateTimeOffset UpdatedAt { get; } // :2558
    public bool FoldsHelpers { get; } // :2559
    public bool IsHydraHead { get; } // :2560
    public long? HydraIndex { get; } // :2561
    public bool HasUnread { get; } // :2562

    // :2564-2582 init(_ thread: ChatThread)
    public ThreadPlacement(ChatThread thread) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2564");
}

/// <summary>:2590-2660 <c>private final class SidebarItemCache</c>: the built list, kept while its inputs stand.</summary>
internal sealed class SidebarItemCache
{
    /// <summary>:2591-2602 <c>struct Key: Equatable</c>.</summary>
    public sealed record Key
    {
        public required ValueList<ThreadPlacement> Placements { get; init; } // :2592
        public required ValueList<Project> Projects { get; init; } // :2593
        public required ValueSet<Guid> Attention { get; init; } // :2595
        public required bool ActivityView { get; init; } // :2596
        public required ActivityThreadStyle ActivityThreadStyle { get; init; } // :2597
        public required bool SettledCollapsed { get; init; } // :2598
        public required bool AutoHidesIdleHeads { get; init; } // :2600
        public required ValueSet<Guid> ParentsShowingHeads { get; init; } // :2601
    }

    /// <summary>:2604-2608 <c>struct SearchKey: Equatable</c>.</summary>
    public sealed record SearchKey
    {
        public required ValueList<ChatThread> Threads { get; init; } // :2605
        public required ValueList<Project> Projects { get; init; } // :2606
        public required string Query { get; init; } // :2607
    }

    // :2621-2626 func items(for wanted: Key, build: () -> [SidebarItem]) -> [SidebarItem]
    public ValueList<SidebarItem> Items(Key @for, Func<ValueList<SidebarItem>> build) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2621");

    // :2629-2634 func helpers(for placements: [ThreadPlacement], group: () -> [UUID: [ChatThread]]) -> [UUID: [ChatThread]]
    public ValueMap<Guid, ValueList<ChatThread>> Helpers(ValueList<ThreadPlacement> @for, Func<ValueMap<Guid, ValueList<ChatThread>>> group) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2629");

    // :2636-2641 func search(for wanted: SearchKey, find: ([UUID: String]) -> [SidebarSearchResult]) -> [SidebarSearchResult]
    public ValueList<SidebarSearchResult> Search(SearchKey @for, Func<ValueMap<Guid, string>, ValueList<SidebarSearchResult>> find) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2636");

    // :2643-2659 private func lowercasedTitles(for threads: [ChatThread]) -> [UUID: String]
    private ValueMap<Guid, string> LowercasedTitles(ValueList<ChatThread> @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2643");
}

/// <summary>:2665-2683 <c>@MainActor @Observable final class MinuteClock</c> (R-OBS-6): the minute hand behind the
/// sidebar's relative times.</summary>
public sealed class MinuteClock : MainActorModel
{
    // :2668 static let shared = MinuteClock()
    public static MinuteClock Shared =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2668");

    // :2670 private(set) var now = Date.now
    public DateTimeOffset Now =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2670");

    // :2674-2682 private init()
    private MinuteClock() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2674");
}

/// <summary>:2687-2696 <c>private struct RelativeTimeLabel: View</c>.</summary>
internal sealed class RelativeTimeLabel : Decorator
{
    /// <param name="date">:2688 <c>let date: Date</c>.</param>
    // :2690-2695 body
    public RelativeTimeLabel(DateTimeOffset date) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2690");
}
