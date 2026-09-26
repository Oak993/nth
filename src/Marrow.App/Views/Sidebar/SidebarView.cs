using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Sound;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift (B3 52bc792), :1-2695.
// Skeleton (c100 group 5): every type the file declares, the views' inputs, the record shapes, the metrics constants
// and the members the other files use. Every view body and every member with behaviour is left to the implementing
// unit (MARROW_STUB).
// Implemented so far (parts 1 and 2 of 8): SidebarView's body (:69-205) with its search field, bell, list and bottom
// rows, query (:61-63), the scroll freeze (:392-404), the placements and helper grouping (:385-422), listItems
// (:424-448), hasRunningHead, helperItems and orderedHeads (:450-485), addProject (:498-511), projectItems (:515-557),
// the activity layout (:559-686), search (:690-749), rowTransition and itemView's dispatch and thread arm (:312-347),
// reorderableRow and the reorder (:753-868: the list's drag gesture, dragChanged, dragEnded, peers, slotHeights,
// DragPeers), SidebarItem.id(for:) and the two extensions (:1053-1076), ThreadRowMetrics.headCardHeight
// (:1305-1311), SidebarThreadRow.mergeStage (:1980-1984), ActivityViewToggle (:2316-2343), RowHeights and RowFrames
// (:2442-2472), PlacementCache, ThreadPlacement and SidebarItemCache (:2480-2658), and the list's pointer style while
// a row is grabbed (:124, PA-0019, ED-205; was STOP #5, now resolved).
// Part 3 of 8 adds: RidesWithDraggedParent.Body (:1622-1632); SidebarThreadRow.Fill, BackgroundFill,
// TrailingClearance, ShowsCheck, SelectThread, NoteHover, ApplyDraggedLift, BuildSettleCheckOverlay and
// BuildTrailingOverlay (:1736-1849, :1874-1898, :1895-1898) as directly testable static pieces of the body, cited
// at each; ThreadRowFace's body and TitleOpacity (:2019-2078); SettleCheck (:2114-2141); ThreadBadge (:2143-2175);
// ActivityStatus (:2408-2428); MinuteClock and RelativeTimeLabel (:2662-2695). SidebarThreadRow's own constructor
// stays MARROW_STUB: its full body needs settle()/reopen(), archiveWithGenie(), makeActions(), RowGlideAnimator and
// ThreadActions.makeContextMenu (parts 4+); the pieces above are written so that constructor can call them once
// those exist, and each is proven directly by its own SidebarViewTests.cs test in the meantime.
// Part 4 of 8 adds: DeleteThreadPopover (:967-983) and ArchiveThreadPopover (:905-963, its key monitor as the
// window-scoped handler pair AttachmentPreviewPanel.cs:160-195 already establishes) in full; SidebarView's popover
// coordinator state (renaming, renameText, pendingDeletionID, pendingArchiveID, confirmedDeletion,
// isDeletePopoverShown: :13-30) and menuRequests (:42); its private helpers requestDeletion (the :890-896, :360-366,
// :190-196 shared branch), deletePopover, archivePopover, confirmDeletion, carryOutConfirmedDeletion (:207-306) and
// liveRenameTarget (:882-887, :357); the three `.onChange` handlers this adds to Appear() (:184-202); HelperConnector
// and HelperConnectorShape in full (:1636-1675); SidebarThreadRow.FinishRequestReceived, Settle, Reopen, Glide and
// ArchiveWithGenie (:1863-1870, :1904-1953, :1988-1999) as directly testable static pieces, each cited; ThreadActions
// Make and MakeContextMenu in full (:2193-2312); SidebarHelperRow.TitleWeight, TitleOpacity, TrailingPadding,
// BackgroundFill, HeadGlyph and BuildTrailingOverlay (:1361-1384) as directly testable static pieces of that row's
// body. renamePopover (:239-252) stays MARROW_STUB: STOP #6 (SidebarViewTests.cs), RenameThreadPopover's
// `.textFieldStyle(.roundedBorder)` and `.buttonStyle(.bordered)`/`.buttonStyle(.borderedProminent)` have no Windows
// chrome or governing decision yet.
// Still MARROW_STUB and named where they stand: RenameThreadPopover and renamePopover (STOP #6 above); the row
// views' remaining assembly (SidebarThreadRow's and SidebarHelperRow's own constructors, ThreadRow, HelperItemView),
// the head-card and stub arms of itemView, ProjectRow, ActivityHeader, SettledHeader and ThreadGhostBadge's own body
// (parts 5 to 8), and the empty-needle case of the search match (STOP #1 in SidebarViewTests.cs).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record
// with `Control Body(Control content)`; a Shape is a sealed record with `Geometry Path(Rect in)`; a caseless enum is a
// static class; an enum with associated values is an abstract record (R-ENUM-2), an unlabelled payload named Value; a
// parameter without a default that follows one with a default moves before it (ED-095), so callers name their
// arguments. UUID is Guid, Date DateTimeOffset, CGFloat double, a Swift Int long (R-NUM-4), CGRect/CGPoint Avalonia
// Rect/Point, Color SwiftColor, AnyTransition SwiftTransition, `[T]` in a value ValueList<T>, `[K: V]` ValueMap and
// `Set<T>` ValueSet (R-VAL-2). Swift `private` top-level types are `internal` (C# has no private top-level type).
// `@Environment(AppModel.self)` is read by the bodies, so it is no constructor parameter (the question group 3 raised).
// Renames: SidebarItem's `static func id(for:)` is IdFor (the record's own `Id` property takes the name), and its
// `kind` property is SidebarItemKind (R-NAME-3, type SidebarItem.Kind). The file's two library extensions (:1058-1062
// on ChatThread, :1064-1076 on AnyTransition) go in SidebarViewExtensions, as C# 14 extension blocks (ED-164): the
// file's own name belongs to the view.
// Not declared yet (private, read only by the bodies the implementing unit writes): SidebarView's other @State storage
// (:13-30 and :42: the rename, delete and archive popovers' state, menuRequests, listFrame) and its remaining private
// helpers (:207-306, :487-494, :870-897), the rows' @State storage, their private helpers (ProjectRow.actions
// :1205-1229, SidebarHelperRow.makeActions :1422-1424, SidebarThreadRow :1895-2000), HelperConnector.style (:1642, a
// StrokeStyle with a dash pattern, no port type yet) and MinuteClock.tick (:2671).
// The rows' custom `==` (SidebarHelperRow :1413-1420, SidebarThreadRow :1882-1891) stand for `.equatable()`; a
// Control keeps reference equality, so they are not declared as Equals (NEEDS DECISION, as group 3 reports).

namespace Marrow.App.Views.Sidebar;

/// <summary>:4-898 <c>struct SidebarView: View</c>: the thread list, in the project or the activity layout.</summary>
public sealed class SidebarView : Decorator
{
    // :67 private nonisolated static let listSpace = "sidebar-list"
    private const string ListSpace = "sidebar-list";

    // :767 private static let slide = Animation.spring(response: 0.28, dampingFraction: 0.82)
    private static Animation Slide { get; } = Animation.Spring(response: 0.28, dampingFraction: 0.82);

    // :9-10 the view's inputs.
    private readonly bool inPopover;
    private readonly Action? dismiss;

    /// <summary>The @State the body reads (R-VIEW-2).</summary>
    private sealed class ViewState : MainActorModel
    {
        private string search = ""; // :12
        private RowDrag<Guid> drag = new(); // :33
        private bool isListScrolling; // :56
        private ChatThread? renaming; // :13
        private string renameText = ""; // :14
        private Guid? pendingDeletionID; // :17
        private Guid? pendingArchiveID; // :19
        private ConfirmedDeletion? confirmedDeletion; // :22
        private bool isDeletePopoverShown; // :26

        // :12 @State private var search = ""
        public string Search { get => Read(search); set => Set(ref search, value); }

        // :31-33 @State private var drag = RowDrag<UUID>(): the live reorder; the grabbed row follows the pointer.
        public RowDrag<Guid> Drag { get => Read(drag); set => Set(ref drag, value); }

        // :56 @State private var isListScrolling = false
        public bool IsListScrolling { get => Read(isListScrolling); set => Set(ref isListScrolling, value); }

        // :13 @State private var renaming: ChatThread? (part 4). PR-SIDEBAR-093.
        public ChatThread? Renaming { get => Read(renaming); set => Set(ref renaming, value); }

        // :14 @State private var renameText = "" (part 4). PR-SIDEBAR-093.
        public string RenameText { get => Read(renameText); set => Set(ref renameText, value); }

        // :17 @State private var pendingDeletionID: UUID? (part 4). PR-SIDEBAR-086, PR-SIDEBAR-087.
        public Guid? PendingDeletionID { get => Read(pendingDeletionID); set => Set(ref pendingDeletionID, value); }

        // :19 @State private var pendingArchiveID: UUID? (part 4). PR-SIDEBAR-094, PR-SIDEBAR-095.
        public Guid? PendingArchiveID { get => Read(pendingArchiveID); set => Set(ref pendingArchiveID, value); }

        // :22 @State private var confirmedDeletion: ConfirmedDeletion? (part 4). PR-SIDEBAR-089.
        public ConfirmedDeletion? ConfirmedDeletion { get => Read(confirmedDeletion); set => Set(ref confirmedDeletion, value); }

        // :26 @State private var isDeletePopoverShown = false (part 4). PR-SIDEBAR-087, PR-SIDEBAR-089, PR-SIDEBAR-097.
        public bool IsDeletePopoverShown { get => Read(isDeletePopoverShown); set => Set(ref isDeletePopoverShown, value); }
    }

    private readonly ViewState state = new();

    // :42 @State private var menuRequests = SidebarMenuRequests() (part 4): held only for the rows a later part
    // assembles (ThreadRow, HelperItemView) to write into; read only by actions, so a plain field (R-VIEW-2).
    private readonly SidebarMenuRequests menuRequests = new();

    // :59 @State private var scrollSettle: Task<Void, Never>? (read only by actions: a field, R-VIEW-2).
    private TaskHandle? scrollSettle;

    // :52 @State private var itemCache = SidebarItemCache()
    private readonly SidebarItemCache itemCache = new();

    // :385 @State private var placementCache = PlacementCache()
    private readonly PlacementCache placementCache = new();

    // :34-38 @State private var rowHeights = RowHeights(): every row's height by its item id, out of observation.
    private readonly RowHeights rowHeights = new();

    // :43-46 @State private var rowFrames = RowFrames(): every open thread row's frame in the list, out of observation.
    private readonly RowFrames rowFrames = new();

    // :47-49 @State private var dragPeers: DragPeers? (read only by actions: a field, R-VIEW-2).
    private DragPeers? dragPeers;

    // :129 the DragGesture's own state: where the press began in the list's space, and whether the pointer has since
    // travelled the gesture's minimum distance (it has succeeded).
    private Point? pressStart;
    private bool gestureActive;

    // :101-124 the ScrollView and its LazyVStack.
    private readonly ScrollViewer scroll;
    private readonly StackPanel list;

    // The list's rows by the id their ForEach keys them with (R-VIEW-8), and the ids ScrollViewReader can reach.
    private readonly Dictionary<string, ListRow> listRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> scrollTargets = new(StringComparer.Ordinal);
    private ListMotion? lastMotion;
    private Action<TimeSpan>? scrollFrame;
    private IFrameSource? scrollFrames;
    private readonly List<IDisposable> changes = [];

    /// <param name="inPopover">:9 <c>var inPopover = false</c>.</param>
    /// <param name="dismiss">:10 <c>var dismiss: (() -> Void)? = nil</c>.</param>
    // :69-205 body
    public SidebarView(bool inPopover = false, Action? dismiss = null)
    {
        this.inPopover = inPopover;
        this.dismiss = dismiss;

        // :74 VStack(alignment: .leading, spacing: 0); the scroll view takes the height the others leave.
        var column = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var row = 0;
        void Add(Control child, GridLength height)
        {
            column.RowDefinitions.Add(new RowDefinition(height));
            Grid.SetRow(child, row++);
            column.Children.Add(child);
        }

        // :75-80 Clears the window buttons that float over the sidebar's top corner (OD-0008 C4: the app draws them
        // at the source's positions on Windows), in the window column only.
        if (!inPopover)
        {
            Add(new Border
            {
                Height = Chrome.TrafficLightDiameter, // :78
                Margin = new Thickness(0, Chrome.TrafficLightTop, 0, 0), // :79
            }, GridLength.Auto);
        }

        // :82-99 the search field and the bell, 6 pt apart.
        var field = new SidebarSearchField(
            new Binding<string>(() => state.Search, value => state.Search = value), // :84 $search
            prompt: "Search threads", // :85
            onSubmit: () => // :86-90
            {
                var results = SearchResults(HelpersByParent(ThreadPlacements));
                if (results.Count > 0 && results[0].Threads.Count > 0) Model.SelectedThreadID = results[0].Threads[0].Id;
            },
            onMove: offset => MoveSearchSelection(offset, HelpersByParent(ThreadPlacements))); // :91
        var toggle = new ActivityViewToggle(new Binding<bool>( // :93-96
            () => Model.Settings.SidebarActivityView,
            value => Model.Settings.SidebarActivityView = value));
        toggle.Margin = new Thickness(6, 0, 0, 0); // :82 HStack(spacing: 6)
        toggle.VerticalAlignment = VerticalAlignment.Center;
        field.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(field, 0);
        Grid.SetColumn(toggle, 1);
        Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(Chrome.ListInset, inPopover ? 12 : 14, Chrome.ListInset, 0), // :98-99
            Children = { field, toggle },
        }, GridLength.Auto);

        // :101-124 ScrollView(.vertical) { LazyVStack(alignment: .leading, spacing: 1) { ... } }
        list = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1, // :103
            Margin = new Thickness(Chrome.ListInset, 12, Chrome.ListInset, 8), // :120-122
        };
        scroll = new ScrollViewer
        {
            Template = ListScrollTemplate, // :139 .scrollIndicators(.never)
            Content = list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, // :102 .vertical
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Add(scroll, new GridLength(1, GridUnitType.Star));

        // :124 .pointerStyle(drag.id != nil ? .grabActive : nil). B3 replaced B2's NSCursor.closedHand.push()/pop()
        // pair (dragChanged :775, dragEnded :805) with this state-driven style; SwiftUI applies and clears the
        // pointer itself, so there is no cursor stack to balance (PR-SIDEBAR-049, PA-0019, ED-205).
        list.Track(() => state.Drag.Id is not null, dragging => list.PointerStyle(dragging ? PointerStyle.GrabActive : null));
        // :123, :125-137 One drag gesture for the whole list, ahead of every row's own click: it listens on the way down
        // (tunnelling), so it sees a press before the row's button does. The list's space here is the stack's own:
        // the rows report their frames and the gesture reads its locations in it, whatever the scroll position.
        list.AddHandler(InputElement.PointerPressedEvent, ListPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerMovedEvent, ListMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent, ListReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.PointerCaptureLost += (_, _) => GestureCancelled();

        // :164-178 Add project and Settings, 1 pt apart, below the list.
        var addProject = new SidebarRow("Add project", AddProject, // :165
            () => new SidebarIconBadge(() => new SidebarSymbol("plus"))); // :166
        var settings = new SidebarRow("Settings", () => WindowManager.Shared.ShowSettings(), SettingsIcon); // :168-175
        Add(new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1, // :164
            Margin = new Thickness(Chrome.ListInset, 0, Chrome.ListInset, 12), // :177-178
            Children = { addProject, settings },
        }, GridLength.Auto);

        // :180 .frame(maxHeight: .infinity, alignment: .top); :181 .background { if !inPopover { WindowDragArea() } }
        var root = new Panel();
        if (!inPopover) root.Children.Add(new WindowDragArea());
        root.Children.Add(column);
        Child = root;

        // :104-113 the list, in either layout or as search results, rebuilt when what it reads changes.
        list.Track(ListPassNow, ApplyList);
        // :115 .allowsHitTesting(!isListScrolling)
        list.Track(() => state.IsListScrolling, scrolling => list.IsHitTestVisible = !scrolling);

        // :141-148 .onScrollPhaseChange: a moving phase re-arms the freeze, .idle lifts it at once. ED-010: Windows
        // reports a phase only for gesture scrolling (touch, pen, precision touchpad through the recognizer); wheel
        // ticks report none and are covered by the offset below and the 150 ms settle.
        scroll.AddHandler(InputElement.ScrollGestureEvent, (_, _) => NoteListScroll());
        scroll.AddHandler(InputElement.ScrollGestureInertiaStartingEvent, (_, _) => NoteListScroll());
        scroll.AddHandler(InputElement.ScrollGestureEndedEvent, (_, _) =>
        {
            scrollSettle?.Cancel(); // :143
            state.IsListScrolling = false; // :144
        });
        // :149-151 .onScrollGeometryChange(for: CGFloat.self, of: { $0.contentOffset.y }) { old, new in ... }
        scroll.PropertyChanged += (_, e) =>
        {
            if (e.Property != ScrollViewer.OffsetProperty) return;
            if (e.GetOldValue<Vector>().Y != e.GetNewValue<Vector>().Y) NoteListScroll();
        };

        AttachedToVisualTree += (_, _) => Appear();
        DetachedFromVisualTree += (_, _) => Disappear();
    }

    // :5 @Environment(AppModel.self) private var model
    private AppModel Model => AppEnvironment.GetAppModel(this);

    // :61-63
    private string Query => state.Search.Trimmed();

    // The view's `.onChange` modifiers, alive while it is on screen (R-VIEW-5).
    private void Appear()
    {
        // :182-183 Read only in the popover: the list itself has no reason to re-render on selection.
        changes.Add(new OnChange<Guid?>(() => inPopover ? Model.SelectedThreadID : null, (_, _) =>
        {
            if (inPopover) dismiss?.Invoke();
        }));
        // :154-161 A thread that has just moved to another project is brought into view.
        changes.Add(new OnChange<Guid?>(() => Model.SidebarRevealThreadID, (_, reveal) =>
        {
            if (reveal is not { } id) return; // :155
            Model.SidebarRevealThreadID = null; // :156
            if (Model.Thread(id) is not { } thread) return; // :157
            Transaction.WithAnimation(Chrome.PanelSlide, () => ScrollTo(SidebarItem.IdFor(thread))); // :158-160
        }));
        // :184-188 A rename requested from the AppKit context menu (the ellipsis popover reads the live thread
        // itself, at the row: PR-SIDEBAR-093). The posted value is used as-is, unlike the live lookup.
        changes.Add(new OnChange<ChatThread?>(() => menuRequests.Rename, (_, thread) =>
        {
            if (thread is not { } posted) return; // :185
            menuRequests.Rename = null; // :186
            state.RenameText = posted.Title; // :187
            state.Renaming = posted; // :188
        }));
        // :189-196 A delete requested from the AppKit context menu. PR-SIDEBAR-086.
        changes.Add(new OnChange<ChatThread?>(() => menuRequests.Delete, (_, thread) =>
        {
            if (thread is not { } posted) return; // :190
            menuRequests.Delete = null; // :191
            RequestDeletion(posted.Id); // :192-196
        }));
        // :198-202 The shortcut's question lands on the thread's own row. PR-SIDEBAR-094.
        changes.Add(new OnChange<AppModel.ArchiveRequest?>(() => Model.AppModelArchiveRequest, (_, request) =>
        {
            if (request is not { } posted || Model.Thread(posted.ThreadID) is null) return; // :199
            Model.AppModelArchiveRequest = null; // :200
            state.PendingArchiveID = posted.ThreadID; // :201
        }));
    }

    private void Disappear()
    {
        foreach (var change in changes) change.Dispose();
        changes.Clear();
        StopScrollAnimation();
    }

    // :168-175 the Settings row's icon: the gear, with the update dot at its top-trailing corner while an update waits.
    private static Control SettingsIcon()
    {
        var badge = new SidebarIconBadge(() => new SidebarSymbol("gear", scale: 1.15)); // :169
        var overlay = new Panel { Children = { badge } };
        UpdateAvailableDot? dot = null;
        overlay.Track(() => UpdateChecker.Shared.UpdateAvailable, available => // :171
        {
            if (available && dot is null)
            {
                dot = new UpdateAvailableDot // :172
                {
                    HorizontalAlignment = HorizontalAlignment.Right, // :170 .overlay(alignment: .topTrailing)
                    VerticalAlignment = VerticalAlignment.Top,
                };
                overlay.Children.Add(dot);
            }
            else if (!available && dot is { } shown)
            {
                overlay.Children.Remove(shown);
                dot = null;
            }
        });
        return overlay;
    }

    // ENGINEERING DECISION: `.scrollIndicators(.never)` (:139) is a scroll view with no scroll bars at all, so its
    // template is the content presenter alone, with the recognizer that scrolls it by touch and pen; the wheel is the
    // presenter's own. A template of its own also keeps the list scrolling where no theme supplies one.
    private static readonly FuncControlTemplate<ScrollViewer> ListScrollTemplate = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty],
        };
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = false });
        return presenter.RegisterInNameScope(scope);
    });

    // :392-404 A scroll movement (re)arms the list's scroll freeze: rows take no hover or clicks for 150 ms after the
    // last movement, covering wheel ticks, which report no phase.
    private void NoteListScroll()
    {
        if (!state.IsListScrolling) state.IsListScrolling = true; // :397
        scrollSettle?.Cancel(); // :398
        scrollSettle = MainActor.Spawn(async () => // :399
        {
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(150)); // :400
            if (SwiftTask.IsCancelled || !state.IsListScrolling) return; // :401
            state.IsListScrolling = false; // :402
        });
    }

    // MARK: The list as the body builds it

    // :117-119 the three values whose change animates the list with Chrome.panelSlide.
    private sealed record ListMotion(long ThreadCount, ValueList<bool> Expansion, bool SettledCollapsed);

    // One pass of :72-73 and :103-113: the items of the current layout, or the search results.
    private sealed record ListPass(
        ValueList<SidebarItem>? Items,
        ValueList<SidebarSearchResult>? Results,
        ValueMap<Guid, ValueList<ChatThread>> Helpers,
        ListMotion Motion);

    private ListPass ListPassNow()
    {
        var placements = ThreadPlacements; // :72
        var helpers = HelpersByParent(placements); // :73
        var model = Model;
        var motion = new ListMotion(
            model.Threads.Count, // :117
            new ValueList<bool>(model.Projects.Select(project => project.IsExpanded)), // :118
            model.Settings.SettledCollapsed); // :119
        return Query.Length == 0 // :104
            ? new ListPass(ListItems(placements, helpers), null, helpers, motion) // :107-110
            : new ListPass(null, SearchResults(helpers), helpers, motion); // :112
    }

    private void ApplyList(ListPass pass)
    {
        // :117-119 R-VIEW-4: a change of any of the three values animates the pass with Chrome.panelSlide; otherwise the
        // change's own transaction applies. The entering and leaving rows ride it. STOP #3 (test file): the rows that
        // stay have no layout animation in the port, so they move without the slide.
        var animation = lastMotion is not null && lastMotion != pass.Motion ? Chrome.PanelSlide : Transaction.Current;
        lastMotion = pass.Motion;
        Transaction.WithAnimation(animation, () => ApplyEntries(pass.Items is { } items
            ? ItemEntries(items)
            : SearchEntries(pass.Results!, pass.Helpers)));
    }

    // A row of the list as its ForEach keeps it: the value it was built from and its control (R-VIEW-8).
    private sealed class ListRow(object value, Control control)
    {
        public object Value { get; set; } = value;
        public Control Control { get; set; } = control;
    }

    // One entry a pass wants: its ForEach id, the value that builds it, the id ScrollViewReader finds it by, the
    // control, and how a surviving row takes a new value in place (false: it is rebuilt where it stands).
    private sealed record ListEntry(string Key, object Value, string? Target, Func<Control> Make, Func<Control, object, bool>? Update);

    // :107-110 ForEach(listItems(...)) { item in itemView(item).transition(rowTransition(item)) }
    private List<ListEntry> ItemEntries(ValueList<SidebarItem> items) =>
        items.Select(item => new ListEntry(
            "item:" + item.Id,
            item,
            item.Id,
            () => ItemView(item).Transition(RowTransition(item)),
            (control, value) => UpdateItemView(control, (SidebarItem)value))).ToList();

    // :691-716 searchList(helpers:)
    private List<ListEntry> SearchEntries(ValueList<SidebarSearchResult> results, ValueMap<Guid, ValueList<ChatThread>> helpers)
    {
        var entries = new List<ListEntry>();
        if (results.Count == 0) // :693
        {
            entries.Add(new ListEntry("search:empty", "empty", null, NoResults, null)); // :694-703
            return entries;
        }
        for (var index = 0; index < results.Count; index++) // :705 ForEach(..., id: \.element.project.id)
        {
            var result = results[index];
            var group = "search:" + SwiftUUID.UuidString(result.Project.Id);
            if (index > 0) // :706-708
                entries.Add(new ListEntry(group + ":gap", Chrome.GroupGap, null, () => new Border { Height = Chrome.GroupGap }, null));
            entries.Add(new ListEntry(group + ":project", (result.Project, result.Count), null, // :709
                () => new ProjectRow(result.Project, result.Count, togglesExpansion: false), null));
            foreach (var thread in result.Threads) // :711 ForEach(result.threads, id: \.sidebarItemID)
            {
                var hasHelpers = !thread.IsSettled && helpers.TryGetValue(thread.Id, out var own) && own.Count > 0; // :712
                entries.Add(new ListEntry(group + ":" + thread.SidebarItemID, (thread, hasHelpers), thread.SidebarItemID,
                    () => ThreadRow(thread, projectName: null, projectIcon: null, hasHelpers: hasHelpers), null));
            }
        }
        return entries;
    }

    // :694-703 the search list's empty state.
    private static Control NoResults()
    {
        var title = new TextBlock { Text = "No results" }; // :695
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(title); // :696
        title.Foreground = Chrome.PrimaryText.Opacity(0.92).Brush(title); // :697
        var hint = new TextBlock { Text = "Try a thread title or a project name." }; // :698
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(hint); // :699
        hint.Foreground = Chrome.SecondaryText.Brush(hint); // :700
        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6, // :694 VStack(alignment: .leading, spacing: 6)
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 14, 8, 0), // :702-703
            Children = { title, hint },
        };
    }

    // The ForEach diff (R-VIEW-8) over the list: a vanished id's row leaves with its transition, a new id's row
    // enters with its own, a surviving id keeps its control and takes the new value, and the rows follow the order.
    // Every transition here is an opacity one, so no row is wrapped and each control is the list's own child.
    private void ApplyEntries(List<ListEntry> entries)
    {
        var wanted = new List<ListEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (seen.Add(entry.Key)) wanted.Add(entry); // a duplicate id keeps its first entry (UNVERIFIED O-18)
        foreach (var gone in listRows.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            SwiftTransition.Remove(list, listRows[gone].Control);
            listRows.Remove(gone);
        }
        scrollTargets.Clear();
        Control? previous = null;
        foreach (var entry in wanted)
        {
            var target = previous is null ? 0 : list.Children.IndexOf(previous) + 1;
            Control control;
            if (listRows.TryGetValue(entry.Key, out var row))
            {
                if (!Equals(row.Value, entry.Value))
                {
                    if (entry.Update is { } update && update(row.Control, entry.Value)) row.Value = entry.Value;
                    else
                    {
                        var fresh = entry.Make();
                        list.Children[list.Children.IndexOf(row.Control)] = fresh;
                        row.Control = fresh;
                        row.Value = entry.Value;
                    }
                }
                control = row.Control;
                var current = list.Children.IndexOf(control);
                if (current != target) list.Children.Move(current, current < target ? target - 1 : target);
            }
            else
            {
                control = entry.Make();
                listRows[entry.Key] = new ListRow(entry.Value, control);
                SwiftTransition.Insert(list, target, control);
            }
            if (entry.Target is { } id) scrollTargets[id] = control;
            previous = control;
        }
    }

    // :159 proxy.scrollTo(id, anchor: .center): the row's centre to the middle of the visible list, as far as the
    // content allows, along the current transaction's animation.
    private void ScrollTo(string id)
    {
        if (!scrollTargets.TryGetValue(id, out var row)) return;
        scroll.UpdateLayout();
        if (row.TranslatePoint(new Point(0, row.Bounds.Height / 2), scroll) is not { } centre) return;
        var limit = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var y = Math.Min(Math.Max(scroll.Offset.Y + centre.Y - scroll.Viewport.Height / 2, 0), limit);
        var to = new Vector(scroll.Offset.X, y);
        StopScrollAnimation();
        if (Transaction.Current is not { } animation)
        {
            scroll.Offset = to;
            return;
        }
        var from = scroll.Offset;
        var start = SwiftClock.Now;
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            var complete = seconds >= animation.Duration;
            scroll.Offset = from + (to - from) * (complete ? 1 : animation.Progress(seconds));
            if (complete) scrollFrame = null;
            else frames.RequestFrame(frame!);
        };
        scrollFrames = frames;
        scrollFrame = frame;
        frames.RequestFrame(frame);
    }

    private void StopScrollAnimation()
    {
        if (scrollFrame is { } frame) scrollFrames?.CancelFrame(frame);
        scrollFrame = null;
    }

    // :312-326 How a row enters and leaves the list. A settled thread's row, the Settled header and the gaps ride the
    // list's own motion; every other row keeps the sidebarRow handover delay.
    private SwiftTransition RowTransition(SidebarItem item) => item.SidebarItemKind switch
    {
        SidebarItem.Kind.Thread thread => thread.Value.IsSettled ? SwiftTransition.Opacity : SwiftTransition.SidebarRow, // :319-320
        SidebarItem.Kind.SettledHeader or SidebarItem.Kind.Gap => SwiftTransition.Opacity, // :321-322
        _ => SwiftTransition.SidebarRow, // :323-324
    };

    // :328-376 itemView(_:)
    private Control ItemView(SidebarItem item) => item.SidebarItemKind switch
    {
        SidebarItem.Kind.Gap gap => new Border { Height = gap.Value }, // :331-332 Color.clear.frame(height:)
        SidebarItem.Kind.Project project => new ProjectRow(project.Value, project.Count), // :333-334
        SidebarItem.Kind.Header header => new ActivityHeader(header.Value, header.IsFirst), // :335-336
        SidebarItem.Kind.SettledHeader settled => new SettledHeader(settled.Count, settled.IsFirst), // :337-338
        SidebarItem.Kind.Thread thread => ThreadItemView(item, thread), // :339-346
        SidebarItem.Kind.Helper helper => HelperItemView(item, helper), // :347-367
        SidebarItem.Kind.HeadCards cards => HeadCardsItemView(item, cards), // :368-371
        SidebarItem.Kind.HelperStub stub => HelperStubItemView(item, stub), // :372-375
        _ => throw new UnreachableException(),
    };

    // A surviving row takes its item's new value in place where its view can; the others are rebuilt where they stand.
    private static bool UpdateItemView(Control control, SidebarItem item)
    {
        if (item.SidebarItemKind is not SidebarItem.Kind.Gap gap || control is not Border border) return false;
        border.Height = gap.Value; // :332
        return true;
    }

    // :339-347 the thread arm: reorderableRow with its height and frame reports.
    private Control ThreadItemView(SidebarItem item, SidebarItem.Kind.Thread thread) =>
        ReportsFrame(ReorderableRow(thread.Value, thread.ProjectName, thread.ProjectIcon, thread.Peers, thread.HasHelpers), item.Id, thread.Value);

    // :341-347 .onGeometryChange(for: CGRect.self, of: { $0.frame(in: .named(Self.listSpace)) }) { frame in ... } and
    // .onDisappear. The frame is the row's laid-out slot in the list's space: a lifted row's offset (:760) is drawn,
    // not laid out, so it does not move the frame.
    private Control ReportsFrame(Control row, string itemID, ChatThread thread)
    {
        void Report()
        {
            if (row.GetVisualParent() is not Visual parent || parent.TranslatePoint(row.Bounds.Position, list) is not { } origin) return;
            var frame = new Rect(origin, row.Bounds.Size);
            rowHeights.Note(itemID, frame.Height); // :342
            // :343-344 A settled row keeps its place among the settled; there is nothing to reorder.
            if (!thread.IsSettled) rowFrames.Note(thread.Id, frame);
        }
        row.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty) Report();
        };
        // :346-347 A row that has left the list (scrolled out, settled, deleted) is not there to grab.
        row.DetachedFromVisualTree += (_, _) => rowFrames.Forget(thread.Id);
        return row;
    }

    // :347-367 the helper arm: the helper row inside the delete and archive popovers (part 4).
    private Control HelperItemView(SidebarItem item, SidebarItem.Kind.Helper helper) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:347");

    // :368-371 the head-card arm, which rides a dragged parent (parts 3 and 5).
    private Control HeadCardsItemView(SidebarItem item, SidebarItem.Kind.HeadCards cards) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:368");

    // :372-375 the folded-helpers arm, which rides a dragged parent (parts 3 and 5).
    private Control HelperStubItemView(SidebarItem item, SidebarItem.Kind.HelperStub stub) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:372");

    // :870-897 threadRow(_:projectName:projectIcon:hasHelpers:isDragged:), the popovers around SidebarThreadRow (parts 3
    // and 4).
    private Control ThreadRow(ChatThread thread, string? projectName, ProjectIcon? projectIcon, bool hasHelpers, bool isDragged = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:870");

    // MARK: Rows

    // :753-762 Drag a row up or down the list and it moves live: within its project in the project layout, within its
    // group in the activity layout. The list's one gesture drives it (see the constructor), ahead of the row's own
    // click, so a drag never selects the thread on release; a plain click still does. The row only lifts and follows
    // here.
    private Control ReorderableRow(ChatThread thread, string? projectName, ProjectIcon? projectIcon, ValueList<Guid>? peers, bool hasHelpers) =>
        Lifted(thread.Id, isDragged => ThreadRow(thread, projectName, projectIcon, hasHelpers, isDragged)); // :759

    // :758-761 The row as a drag sees it: built with whether it is the grabbed one (:758-759, its `let` input, so a
    // change builds it again), offset by the drag's visual offset while grabbed (:760) and lifted over its
    // neighbours (:761). The offset moves along the change's animation, and at once when there is none (:777-779).
    private Control Lifted(Guid id, Func<bool, Control> row)
    {
        var translate = new TranslateTransform();
        var host = new Decorator { RenderTransform = translate };
        bool? builtDragged = null;
        double target = 0;
        IFrameSource? frames = null;
        Action<TimeSpan>? pending = null;
        host.Track(() => (IsDragged: state.Drag.Id == id, Offset: state.Drag.VisualOffset), value =>
        {
            var isDragged = value.IsDragged; // :758
            if (builtDragged != isDragged)
            {
                builtDragged = isDragged;
                host.Child = row(isDragged); // :759
            }
            host.ZIndex = isDragged ? 1 : 0; // :761 .zIndex(isDragged ? 1 : 0)
            var to = isDragged ? value.Offset : 0; // :760 .offset(y: isDragged ? drag.visualOffset : 0)
            if (to == target) return;
            target = to;
            if (pending is { } running) frames?.CancelFrame(running);
            pending = null;
            if (Transaction.Current is not { } animation)
            {
                translate.Y = to;
                return;
            }
            var from = translate.Y;
            var start = SwiftClock.Now;
            var source = FrameClock.Source.Current;
            Action<TimeSpan>? tick = null;
            tick = _ =>
            {
                var seconds = (SwiftClock.Now - start).TotalSeconds;
                var complete = seconds >= animation.Duration;
                translate.Y = complete ? to : from + (to - from) * animation.Progress(seconds);
                if (complete) pending = null;
                else source.RequestFrame(tick!);
            };
            frames = source;
            pending = tick;
            source.RequestFrame(tick);
        });
        return host;
    }

    // MARK: Reorder

    // :129 DragGesture(minimumDistance: 4, coordinateSpace: .named(Self.listSpace)), enabled only while the query is
    // empty (:136 isEnabled: query.isEmpty): nothing to reorder while searching. A primary-button press starts it.
    private void ListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (gestureActive) return; // another button pressed during a drag starts nothing
        pressStart = null;
        gestureActive = false;
        if (Query.Length != 0 || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
        pressStart = e.GetPosition(list);
    }

    private void ListMoved(object? sender, PointerEventArgs e)
    {
        if (pressStart is not { } start) return;
        // A drag lives only while the button is held: a release the list never saw ends it here.
        if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            GestureCancelled();
            return;
        }
        var translation = e.GetPosition(list) - start;
        if (!gestureActive)
        {
            // The gesture succeeds once the pointer is 4 pt from where it was pressed, in the list's own points.
            if (Math.Sqrt(translation.X * translation.X + translation.Y * translation.Y) < 4) return;
            gestureActive = true;
            // :128 .highPriorityGesture: from here the gesture has the pointer, so the row's button under it never
            // sees its release and does not click.
            e.Pointer.Capture(list);
        }
        // :130-133 .onChanged: the row under the pointer at the grab is the one that moves.
        if ((state.Drag.Id ?? rowFrames.Thread(start)) is not { } id) return; // :131
        DragChanged(id, translation.Y); // :132
    }

    private void ListReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (pressStart is null) return;
        var succeeded = gestureActive;
        pressStart = null;
        gestureActive = false;
        if (!succeeded) return;
        e.Pointer.Capture(null);
        DragEnded(); // :134 .onEnded { _ in dragEnded() }
    }

    // A gesture the system takes away (its pointer capture lost) ends, as a cancelled SwiftUI gesture does.
    private void GestureCancelled()
    {
        if (pressStart is null) return;
        var succeeded = gestureActive;
        pressStart = null;
        gestureActive = false;
        if (succeeded) DragEnded();
    }

    // :769-801 The pointer has moved `translation` since the grab. The grabbed row follows it exactly and RowDrag swaps
    // it past every neighbour whose centre it has crossed.
    private void DragChanged(Guid id, double translation)
    {
        if (state.Drag.Id != id) // :772
        {
            state.Drag = new RowDrag<Guid> { Id = id }; // :773
            // :774-775 Taken once at the grab: the peers and their slots stand until a swap.
            dragPeers = new DragPeers(Peers(id), SlotHeights);
        }
        // :777-779 A transaction that disables animations: the row follows the pointer one to one.
        Transaction.WithAnimation(null, () => state.Drag = state.Drag with { Translation = translation });

        if (dragPeers is not { } snapshot) return; // :781
        var (order, group) = (snapshot.Order, snapshot.Group); // :782
        var (settled, moved) = state.Drag.Settle(order, snapshot.Heights, fallbackHeight: Chrome.RowHeight + 1, move: (neighbour, placeAfter) => // :783
        {
            // :784-793 The neighbour slides and the grabbed row's slot moves in the same animation as its
            // compensation, so it stays put under the pointer while the list flows around it.
            var result = false;
            Transaction.WithAnimation(Slide, () =>
            {
                result = group is { } peers
                    ? Model.MoveInActivity(id, neighbour, placeAfter, peers) // :789
                    : Model.MoveThread(id, neighbour, placeAfter); // :791
            });
            return result;
        });
        state.Drag = settled; // the mutating call's copy, written back to the state when it returns
        if (moved) // :795
        {
            // :796-798 The row's peers as the list stands now, not as it stood at the grab: every swap reorders them.
            // The slots are by id and stand.
            dragPeers = new DragPeers(Peers(id), SlotHeights);
            // :799 NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
            // DEVIATION ED-008: no Windows desktop haptic; the cue is omitted and nothing stands in for it.
        }
    }

    // :803-809
    private void DragEnded()
    {
        if (state.Drag.Id is null) return; // :804
        dragPeers = null; // :805
        // :806-808 The offset animates from wherever the pointer let go to the row's slot, so the row settles instead of
        // snapping.
        Transaction.WithAnimation(Animation.Spring(response: 0.32, dampingFraction: 0.8), () => state.Drag = new RowDrag<Guid>());
    }

    // :811-825 The threads a row can be reordered among, in their current order: its activity group (returned as
    // `group` too, for the move) or its project's top-level threads.
    private (ValueList<Guid> Order, ValueList<Guid>? Group) Peers(Guid of)
    {
        var model = Model;
        if (model.Settings.SidebarActivityView) // :814
        {
            var placements = ThreadPlacements; // :815
            foreach (var item in ListItems(placements, HelpersByParent(placements))) // :816
            {
                if (item.SidebarItemKind is SidebarItem.Kind.Thread { Peers: { } peers } thread && thread.Value.Id == of) // :817
                    return (peers, peers); // :818
            }
            return (ValueList<Guid>.Empty, null); // :821
        }
        if (model.Thread(of) is not { } found || model.Project(found.ProjectID) is not { } project) return (ValueList<Guid>.Empty, null); // :823
        return (new ValueList<Guid>(model.ThreadsIn(project).Where(thread => !thread.IsSettled).Select(thread => thread.Id)), null); // :824
    }

    // :827-854 Each peer's slot: its row, the helper rows or folded line under it, and the list's spacing after each.
    private ValueMap<Guid, double> SlotHeights(ValueList<Guid> order)
    {
        var model = Model;
        var grouped = HelpersByParent(ThreadPlacements); // :830
        var heights = new Dictionary<Guid, double>(); // :831
        foreach (var id in order) // :832
        {
            var uuid = SwiftUUID.UuidString(id);
            var height = (rowHeights.Values.TryGetValue(uuid, out var row) ? row : Chrome.RowHeight) + 1; // :833
            var helpers = grouped.TryGetValue(id, out var own) ? own : ValueList<ChatThread>.Empty; // :834
            if (helpers.Count > 0) // :835
            {
                if (model.Thread(id)?.FoldsHelpers == true) // :836
                {
                    height += (rowHeights.Values.TryGetValue("helpers-" + uuid, out var stub) ? stub : ThreadRowMetrics.HelperHeight) + 1; // :837
                }
                else
                {
                    foreach (var helper in helpers.Where(helper => !helper.IsHydraHead)) // :839
                        height += (rowHeights.Values.TryGetValue(SwiftUUID.UuidString(helper.Id), out var line) ? line : ThreadRowMetrics.HelperHeight) + 1; // :840
                    long headCount = model.Settings.HydraAutoHidesIdleHeads && !HasRunningHead(helpers.Where(helper => helper.IsHydraHead).ToList()) // :842
                        ? 0
                        : helpers.Count(helper => helper.IsHydraHead); // :843-844
                    if (headCount > 0) // :845
                    {
                        var rows = (headCount + 5) / 6; // :846
                        height += (rowHeights.Values.TryGetValue("heads-" + uuid, out var card) ? card : ThreadRowMetrics.HeadCardHeight(rows)) + 1; // :847
                    }
                }
            }
            heights[id] = height; // :851
        }
        return new ValueMap<Guid, double>(heights); // :853
    }

    // MARK: Where the threads sit

    // :385-389 Where every thread sits in the list, kept while a cheap fingerprint of the threads stands.
    private ValueList<ThreadPlacement> ThreadPlacements => placementCache.Placements(Model.Threads);

    // :406-422 Every helper by the thread it hangs under, worked out once for the whole list.
    private ValueMap<Guid, ValueList<ChatThread>> HelpersByParent(ValueList<ThreadPlacement> placements) =>
        itemCache.Helpers(placements, () =>
        {
            var grouped = new Dictionary<Guid, List<ChatThread>>();
            // Heads count while they are still in the lead's panel too (see `AppModel.helpers(of:)`).
            foreach (var thread in Model.Threads)
            {
                if (thread.IsArchived || (thread.IsInPanel && !thread.IsHydraHead)) continue; // :415
                if (thread.ParentThreadID is not { } parentID) continue; // :416
                if (!grouped.TryGetValue(parentID, out var siblings)) grouped[parentID] = siblings = [];
                siblings.Add(thread); // :417
            }
            // :419-420 Newest first, like the threads around them.
            return new ValueMap<Guid, ValueList<ChatThread>>(grouped.Select(pair =>
                KeyValuePair.Create(pair.Key, new ValueList<ChatThread>(pair.Value.OrderByDescending(thread => thread.CreatedAt)))));
        });

    // :424-448 The list in the current layout, built only when something that places a row has changed.
    private ValueList<SidebarItem> ListItems(ValueList<ThreadPlacement> placements, ValueMap<Guid, ValueList<ChatThread>> helpers)
    {
        var model = Model;
        var activity = model.Settings.SidebarActivityView; // :430
        var autoHidesIdleHeads = model.Settings.HydraAutoHidesIdleHeads; // :431
        var parentsShowingHeads = autoHidesIdleHeads // :432-434
            ? new ValueSet<Guid>(helpers.Where(pair => HasRunningHead(pair.Value)).Select(pair => pair.Key))
            : ValueSet<Guid>.Empty;
        var key = new SidebarItemCache.Key // :435-444
        {
            Placements = placements,
            Projects = model.Projects,
            Attention = activity ? new ValueSet<Guid>(model.Threads.Where(NeedsAttention).Select(thread => thread.Id)) : ValueSet<Guid>.Empty,
            ActivityView = activity,
            ActivityThreadStyle = model.Settings.ActivityThreadStyle,
            SettledCollapsed = model.Settings.SettledCollapsed,
            AutoHidesIdleHeads = autoHidesIdleHeads,
            ParentsShowingHeads = parentsShowingHeads,
        };
        return itemCache.Items(key, () => activity ? ActivityItems(helpers) : ProjectItems(helpers)); // :445-447
    }

    // :450-454 Whether any of these heads is still running, read through the model rather than the snapshot.
    private bool HasRunningHead(IReadOnlyList<ChatThread> heads) =>
        heads.Any(head => (Model.Thread(head.Id) ?? head).Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running);

    // :456-474 The helpers under a thread: one small row each, or a single line naming them while they are folded.
    private ValueList<SidebarItem> HelperItems(ChatThread under, IReadOnlyList<ChatThread> helpers)
    {
        // A settled thread is one small line; what it spawned comes back when it reopens.
        if (under.IsSettled || helpers.Count == 0) return ValueList<SidebarItem>.Empty; // :460
        var parent = SwiftUUID.UuidString(under.Id);
        if (under.FoldsHelpers) // :461-463
            return new ValueList<SidebarItem>([new SidebarItem { Id = "helpers-" + parent, SidebarItemKind = new SidebarItem.Kind.HelperStub(under, new ValueList<ChatThread>(helpers)) }]);
        var plain = helpers.Where(helper => !helper.IsHydraHead).ToList(); // :464
        var heads = OrderedHeads(helpers.Where(helper => helper.IsHydraHead).ToList()); // :465
        var showsHeads = heads.Count > 0 && !(Model.Settings.HydraAutoHidesIdleHeads && !HasRunningHead(heads)); // :466
        Guid? lastPlain = plain.Count > 0 ? plain[^1].Id : null;
        var items = plain.Select(helper => new SidebarItem // :467-469
        {
            Id = SwiftUUID.UuidString(helper.Id),
            SidebarItemKind = new SidebarItem.Kind.Helper(helper, helper.Id == lastPlain && !showsHeads),
        }).ToList();
        if (showsHeads) // :470-472
            items.Add(new SidebarItem { Id = "heads-" + parent, SidebarItemKind = new SidebarItem.Kind.HeadCards(under, heads) });
        return new ValueList<SidebarItem>(items);
    }

    // :476-485 Hydra heads in card order: running heads first, newest `createdAt` first among them, then finished heads,
    // newest first. A stable sort by the source's predicate (R-COLL-1).
    public static ValueList<ChatThread> OrderedHeads(IReadOnlyList<ChatThread> heads)
    {
        static bool Before(ChatThread lhs, ChatThread rhs)
        {
            var lhsRunning = lhs.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running; // :480
            var rhsRunning = rhs.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running; // :481
            if (lhsRunning != rhsRunning) return lhsRunning; // :482
            return lhs.CreatedAt > rhs.CreatedAt; // :483
        }
        return new ValueList<ChatThread>(heads.OrderBy(head => head,
            Comparer<ChatThread>.Create((lhs, rhs) => Before(lhs, rhs) ? -1 : Before(rhs, lhs) ? 1 : 0)));
    }

    // MARK: Adding projects

    // :498-511 New projects join from here: a folder picker, one project per folder, then a thread in the last one
    // added. Adding persists through the model.
    private void AddProject()
    {
        var panel = new OpenPanel(); // :501
        panel.CanChooseDirectories = true; // :502
        panel.CanChooseFiles = false; // :503
        panel.AllowsMultipleSelection = true; // :504
        panel.Prompt = "Add project"; // :505
        panel.Message = "Choose a folder for your agents to work in."; // :506
        if (panel.RunModal() != ModalResponse.OK) return; // :507
        Project? added = null; // :508
        foreach (var url in panel.Urls) added = Model.AddProject(url); // :509
        if (added is { } project) Model.NewThread(project); // :510
    }

    // MARK: Project layout

    // :515-557
    private ValueList<SidebarItem> ProjectItems(ValueMap<Guid, ValueList<ChatThread>> helpers)
    {
        var model = Model;
        var items = new List<SidebarItem>(); // :516
        var projects = model.Projects;
        for (var index = 0; index < projects.Count; index++) // :517
        {
            var project = projects[index];
            var projectID = SwiftUUID.UuidString(project.Id);
            if (index > 0) // :518-520
                items.Add(new SidebarItem { Id = "gap-" + projectID, SidebarItemKind = new SidebarItem.Kind.Gap(Chrome.GroupGap) });
            var projectThreads = model.ThreadsIn(project); // :521
            // :522-523 The count a folded project shows: its threads and everything under them.
            var count = projectThreads.Aggregate(0L, (sum, thread) => sum + 1 + (helpers.TryGetValue(thread.Id, out var under) ? under.Count : 0));
            items.Add(new SidebarItem { Id = "project-" + projectID, SidebarItemKind = new SidebarItem.Kind.Project(project, count) }); // :524
            if (!project.IsExpanded) continue; // :525
            // :526-528 The settled threads close the project's list, a small step below the ones still open, under a
            // header that folds them away. Pinned threads lead each project's open list; settled ones never do.
            var collapsed = model.Settings.SettledCollapsed; // :529
            var hasOpen = false; // :530
            var reachedSettled = false; // :531
            var openThreads = projectThreads.Where(thread => !thread.IsSettled).ToList(); // :532
            openThreads = openThreads.Where(thread => thread.IsPinned).Concat(openThreads.Where(thread => !thread.IsPinned)).ToList(); // :533
            var orderedThreads = openThreads.Concat(projectThreads.Where(thread => thread.IsSettled)).ToList(); // :534
            foreach (var thread in orderedThreads) // :535
            {
                if (thread.IsSettled && !reachedSettled) // :536
                {
                    reachedSettled = true; // :537
                    if (hasOpen) // :538-540
                        items.Add(new SidebarItem { Id = "settled-gap-" + projectID, SidebarItemKind = new SidebarItem.Kind.Gap(ThreadRowMetrics.SettledGap) });
                    long settledCount = orderedThreads.Count(value => value.IsSettled); // :541
                    if (settledCount > 0) // :542-544
                        items.Add(new SidebarItem { Id = "settled-header-" + projectID, SidebarItemKind = new SidebarItem.Kind.SettledHeader(settledCount, IsFirst: false) });
                    if (collapsed) break; // :545
                }
                hasOpen = hasOpen || !thread.IsSettled; // :547
                var own = helpers.TryGetValue(thread.Id, out var found) ? found : ValueList<ChatThread>.Empty; // :548
                items.Add(new SidebarItem // :549-552
                {
                    Id = SidebarItem.IdFor(thread),
                    SidebarItemKind = new SidebarItem.Kind.Thread(thread, ProjectName: null, ProjectIcon: null, Peers: null, HasHelpers: !thread.IsSettled && own.Count > 0),
                });
                items.AddRange(HelperItems(thread, own)); // :553
            }
        }
        return new ValueList<SidebarItem>(items); // :556
    }

    // MARK: Activity layout

    // :561-605
    private ValueList<SidebarItem> ActivityItems(ValueMap<Guid, ValueList<ChatThread>> helpers)
    {
        var model = Model;
        var shown = model.Threads.Where(thread => !thread.IsArchived && !thread.IsInPanel).ToList(); // :562
        // :563-566 A helper whose parent is here sits under it (see helperItems); one whose parent is gone or archived
        // stands on its own.
        var shownIDs = shown.Select(thread => thread.Id).ToHashSet(); // :565
        var active = shown.Where(thread => thread.ParentThreadID is not { } parentID || !shownIDs.Contains(parentID)).ToList(); // :566
        // :567-569 A settled thread stays among the settled whatever it is up to.
        var attention = Placed(active.Where(thread => NeedsAttention(thread) && !thread.IsSettled).ToList()); // :568
        var rest = active.Where(thread => !NeedsAttention(thread) || thread.IsSettled).ToList(); // :569
        var items = new List<SidebarItem>(); // :570
        if (attention.Count > 0) // :571-574
        {
            items.Add(new SidebarItem { Id = "attention", SidebarItemKind = new SidebarItem.Kind.Header("Needs attention", IsFirst: true) });
            items.AddRange(ActivityThreadItems(attention, helpers));
        }
        // :575-581 Open pinned threads sit above the day groups, in the activity sort's order.
        var open = rest.Where(thread => !thread.IsSettled).ToList(); // :576
        var pinned = Placed(open.Where(thread => thread.IsPinned).ToList()); // :577
        if (pinned.Count > 0) // :578
        {
            items.Add(new SidebarItem { Id = "day-Pinned", SidebarItemKind = new SidebarItem.Kind.Header("Pinned", IsFirst: items.Count == 0) }); // :579
            items.AddRange(ActivityThreadItems(pinned, helpers)); // :580
        }
        foreach (var group in ActivityGroups(open.Where(thread => !thread.IsPinned).ToList())) // :582
        {
            // :583-584 Without an attention section the day header is the first row, so it takes the tighter top padding.
            items.Add(new SidebarItem { Id = "day-" + group.Title, SidebarItemKind = new SidebarItem.Kind.Header(group.Title, IsFirst: items.Count == 0) });
            items.AddRange(ActivityThreadItems(group.Threads, helpers)); // :585
        }
        // :587-590 The settled threads sit under everything, whatever day they were last active, latest settled first,
        // under a header that folds them away. They keep their place: no dragging among them.
        var settled = Sorted(rest.Where(thread => thread.IsSettled), AppModel.SettledFirst);
        if (settled.Count > 0) // :591
        {
            items.Add(new SidebarItem { Id = "settled", SidebarItemKind = new SidebarItem.Kind.SettledHeader(settled.Count, IsFirst: items.Count == 0) }); // :592
            if (!model.Settings.SettledCollapsed) // :593
            {
                foreach (var thread in settled) // :594
                {
                    var project = model.Project(thread.ProjectID); // :595
                    var compact = model.Settings.ActivityThreadStyle == ActivityThreadStyle.Icon; // :596
                    items.Add(new SidebarItem // :597-600
                    {
                        Id = SidebarItem.IdFor(thread),
                        SidebarItemKind = new SidebarItem.Kind.Thread(thread, ProjectName: compact ? null : (project?.Name ?? ""), ProjectIcon: ActivityMark(project, compact), Peers: null, HasHelpers: false),
                    });
                }
            }
        }
        return new ValueList<SidebarItem>(items); // :604
    }

    // :607-620 Threads of one activity group, each with the helpers under it. A thread can be dragged to another place
    // within its own group; its helpers follow it.
    private List<SidebarItem> ActivityThreadItems(IReadOnlyList<ChatThread> threads, ValueMap<Guid, ValueList<ChatThread>> helpers)
    {
        var model = Model;
        var peers = new ValueList<Guid>(threads.Select(thread => thread.Id)); // :610
        var items = new List<SidebarItem>();
        foreach (var thread in threads) // :611 flatMap
        {
            var own = helpers.TryGetValue(thread.Id, out var found) ? found : ValueList<ChatThread>.Empty; // :612
            var project = model.Project(thread.ProjectID); // :613
            var compact = model.Settings.ActivityThreadStyle == ActivityThreadStyle.Icon; // :614
            items.Add(new SidebarItem // :615-618
            {
                Id = SwiftUUID.UuidString(thread.Id),
                SidebarItemKind = new SidebarItem.Kind.Thread(thread, ProjectName: compact ? null : (project?.Name ?? ""), ProjectIcon: ActivityMark(project, compact), Peers: peers, HasHelpers: !thread.IsSettled && own.Count > 0),
            });
            items.AddRange(HelperItems(thread, own)); // :618
        }
        return items;
    }

    // :622-631 A thread's project mark in the activity layout. In the icon style the slot in front of the title belongs
    // to the project: the mark picked for it, or the folder mark until one is. In the project-name style a picked mark
    // leads the title and the project line below it drops; with none picked, the row keeps the folder line. The
    // folder mark is drawn as Lucide `folder` where symbols are drawn (OD-0010).
    private ProjectIcon? ActivityMark(Project? project, bool compact)
    {
        if (!compact) return project?.Icon; // :629
        return project?.Icon ?? new ProjectIcon.Symbol("folder.fill"); // :630
    }

    // :633-636
    private bool NeedsAttention(ChatThread thread)
    {
        if (Model.ExistingRuntime(thread.Id) is not { } runtime) return false; // :634
        return runtime.Approvals.Count > 0 || runtime.Questions.Count > 0; // :635
    }

    // :643-656 Threads grouped under Today, Yesterday, a weekday within the week, then a date.
    private static List<ActivityGroup> ActivityGroups(IReadOnlyList<ChatThread> threads)
    {
        var calendar = CurrentCalendar; // :645
        var groups = new List<ActivityGroup>(); // :646
        foreach (var thread in threads.OrderByDescending(thread => thread.UpdatedAt)) // :647, a stable sort (R-COLL-1)
        {
            var title = DayTitle(thread.UpdatedAt, calendar); // :648
            if (groups.Count > 0 && SwiftString.Equal(groups[^1].Title, title)) // :649
                groups[^1] = groups[^1] with { Threads = groups[^1].Threads.Appending(thread) }; // :650
            else
                groups.Add(new ActivityGroup { Title = title, Threads = new ValueList<ChatThread>([thread]) }); // :652
        }
        return groups.Select(group => new ActivityGroup { Title = group.Title, Threads = Placed(group.Threads) }).ToList(); // :655
    }

    // :658-675 Newest first, except threads dragged into place, which keep that place for the rest of their day.
    private static ValueList<ChatThread> Placed(IReadOnlyList<ChatThread> threads)
    {
        // :660-665 A place counts only while it was taken on the day the thread was last active (Calendar.current's
        // isDate(_:inSameDayAs:), :663).
        double? Order(ChatThread thread)
        {
            if (thread.ActivityOrder is not { } order || thread.ActivityOrderDay is not { } day || LocalDay(day) != LocalDay(thread.UpdatedAt)) return null;
            return order;
        }
        return Sorted(threads, (lhs, rhs) => (Order(lhs), Order(rhs)) switch // :666-674
        {
            ({ } left, { } right) when left != right => left < right, // :668
            // :669-670 Threads that became active after a reorder have no place yet and stay on top.
            (null, not null) => true,
            (not null, null) => false, // :671
            _ => lhs.UpdatedAt > rhs.UpdatedAt, // :672
        });
    }

    // :677-686. `.now` is read once: the three reads Swift makes are one moment for the title.
    private static string DayTitle(DateTimeOffset date, System.Globalization.Calendar calendar)
    {
        var day = LocalDay(date);
        var today = LocalDay(SwiftDate.Now);
        if (day == today) return "Today"; // :678 isDateInToday
        if (day == today.AddDays(-1)) return "Yesterday"; // :679 isDateInYesterday
        // :680 dateComponents([.day], from: startOfDay(for: date), to: startOfDay(for: .now)).day: whole calendar days.
        var days = (today - day).Days;
        if (days < 7) return SwiftFormatters.Formatted(date, DateFormatStyle.DateTime.Weekday(DateFormatStyle.Symbol.Weekday.Wide)); // :681
        if (calendar.GetEra(day) == calendar.GetEra(today) && calendar.GetYear(day) == calendar.GetYear(today)) // :682 isDate(_:equalTo:toGranularity: .year)
            return SwiftFormatters.Formatted(date, DateFormatStyle.DateTime.Month(DateFormatStyle.Symbol.Month.Wide).Day()); // :683
        return SwiftFormatters.Formatted(date, DateFormatStyle.DateTime.Month(DateFormatStyle.Symbol.Month.Wide).Day().Year()); // :685
    }

    // Calendar.current (:645, :660): ED-094, the Windows user's calendar with the local zone.
    private static System.Globalization.Calendar CurrentCalendar => CultureInfo.CurrentCulture.Calendar;

    // The calendar day a moment falls on in Calendar.current's zone, as a wall-clock date: a day runs from midnight to
    // midnight there in every calendar Foundation offers, so the day itself needs only the zone.
    private static DateTime LocalDay(DateTimeOffset date) => TimeZoneInfo.ConvertTime(date, SwiftFormatters.CurrentTimeZone.Current).Date;

    // R-COLL-1: `sorted(by:)` as a stable sort by the source's predicate.
    private static ValueList<ChatThread> Sorted(IEnumerable<ChatThread> threads, Func<ChatThread, ChatThread, bool> areInIncreasingOrder) =>
        new(threads.OrderBy(thread => thread, Comparer<ChatThread>.Create((lhs, rhs) =>
            areInIncreasingOrder(lhs, rhs) ? -1 : areInIncreasingOrder(rhs, lhs) ? 1 : 0)));

    // MARK: Search

    // :718-731 Up and down in the search field walk the results without leaving the keyboard; the row highlights exactly
    // as a clicked one does.
    private void MoveSearchSelection(int by, ValueMap<Guid, ValueList<ChatThread>> helpers)
    {
        if (Query.Length == 0) return; // :721
        var results = SearchResults(helpers).SelectMany(result => result.Threads).ToList(); // :722
        if (results.Count == 0) return; // :723
        var model = Model;
        var index = model.SelectedThreadID is { } current ? results.FindIndex(thread => thread.Id == current) : -1; // :724-725
        if (index < 0)
        {
            model.SelectedThreadID = (by < 0 ? results[^1] : results[0]).Id; // :726
            return; // :727
        }
        var next = Math.Min(Math.Max(index + by, 0), results.Count - 1); // :729
        model.SelectedThreadID = results[next].Id; // :730
    }

    // :733-749 The projects and threads matching the query, kept for the query and library they were found in.
    private ValueList<SidebarSearchResult> SearchResults(ValueMap<Guid, ValueList<ChatThread>> helpers)
    {
        var model = Model;
        var query = Query;
        var key = new SidebarItemCache.SearchKey { Threads = model.Threads, Projects = model.Projects, Query = query }; // :738
        return itemCache.Search(key, titles => // :739
        {
            var needle = SwiftString.Lowercased(query); // :740
            var results = new List<SidebarSearchResult>();
            foreach (var project in model.Projects) // :741 compactMap, :746 nil for no match
            {
                var all = model.ThreadsIn(project) // :742
                    .SelectMany(thread => helpers.TryGetValue(thread.Id, out var own) ? new[] { thread }.Concat(own) : [thread])
                    .ToList();
                var threads = Contains(SwiftString.Lowercased(project.Name), needle) // :743
                    ? all // :744
                    : all.Where(thread => titles.TryGetValue(thread.Id, out var title) // :745
                        ? Contains(title, needle)
                        : Contains(SwiftString.Lowercased(thread.Title), needle)).ToList();
                if (threads.Count == 0) continue; // :746
                results.Add(new SidebarSearchResult { Project = project, Threads = new ValueList<ChatThread>(threads), Count = all.Count });
            }
            return new ValueList<SidebarSearchResult>(results);
        });
    }

    // :743, :745 `lowercased().contains(needle)`: a plain substring test of the lowercased texts, as the port reads
    // Swift's String.contains at its other lowercased sites (AppModel+HydraMerge.cs, MCPProxy.cs, ZaiAPI.cs). An empty
    // needle is STOP #1 (SidebarViewTests.cs): which overload the source binds, and so its answer, is unsettled.
    private static bool Contains(string text, string needle)
    {
        if (needle.Length == 0)
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:743 (STOP #1: String.contains with an empty needle)");
        return text.Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>:308-311 <c>private struct ConfirmedDeletion</c>.</summary>
    private sealed record ConfirmedDeletion
    {
        public required Guid ThreadID { get; init; } // :309
        public required bool RemoveWorktree { get; init; } // :310
    }

    // :889-895 threadRow onDelete, :359-365 the helper row's identical branch, :189-196 the context-menu branch
    // (wired above in Appear()). The other two call sites are wired once ThreadRow and HelperItemView (parts 4+) pass
    // this as their row's onDelete. PR-SIDEBAR-086.
    private void RequestDeletion(Guid threadID)
    {
        if (Model.Settings.ConfirmBeforeDeleting) state.PendingDeletionID = threadID; // :890, :360, :190
        else Model.Delete(threadID); // :896, :366, :196 (removeWorktree defaults false)
    }

    /// <summary>:207-216, :217-237 deletePopover(for:on:). The binding compares ids only (the doc comment at :203-216
    /// records why: an earlier ordering read a torn-down view's presentation binding and crashed). PR-SIDEBAR-086,
    /// PR-SIDEBAR-087.</summary>
    private Control DeletePopover(ChatThread thread, Control row)
    {
        var threadID = thread.Id;
        var isPresented = new Binding<bool>(
            () => state.PendingDeletionID == threadID, // :221
            value =>
            {
                if (!value && state.PendingDeletionID == threadID) state.PendingDeletionID = null; // :222
            });
        var popup = PlainControls.Popover(row, isPresented, () =>
        {
            state.IsDeletePopoverShown = true; // :229
            return BuildDeletePopoverContent(thread);
        }, PlacementMode.Right); // :220 arrowEdge: .trailing
        popup.Closed += (_, _) =>
        {
            state.IsDeletePopoverShown = false; // :231
            CarryOutConfirmedDeletion(); // :232
        };
        return new Panel { Children = { row, popup } };
    }

    /// <summary>:225-227 the delete popover's content, extracted so it is directly testable (Avalonia's headless
    /// Popup never opens, R-TEST-7's RecordingPresenter records the request but never fires the content factory
    /// above). PR-SIDEBAR-086, PR-SIDEBAR-088, PR-SIDEBAR-089.</summary>
    private DeleteThreadPopover BuildDeletePopoverContent(ChatThread thread)
    {
        var threadID = thread.Id;
        return new DeleteThreadPopover(Model.Thread(threadID) ?? thread, removeWorktree => ConfirmDeletion(threadID, removeWorktree)); // :225-227
    }

    /// <summary>:239-252 renamePopover(for:on:). STOP #6 (RenameThreadPopover, this unit): the content this wraps has
    /// no chrome yet, so this wrapper stays unwritten rather than calling a MARROW_STUB constructor from a live
    /// path. PR-SIDEBAR-091.</summary>
    private Control RenamePopover(ChatThread thread, Control row) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:239 (STOP #6)");

    /// <summary>:267-291 archivePopover(for:on:). PR-SIDEBAR-094, PR-SIDEBAR-095, PR-SIDEBAR-096, PR-SIDEBAR-097.</summary>
    private Control ArchivePopover(ChatThread thread, Control row)
    {
        var threadID = thread.Id;
        var isPresented = new Binding<bool>(
            () => state.PendingArchiveID == threadID, // :271
            value =>
            {
                if (!value && state.PendingArchiveID == threadID) state.PendingArchiveID = null; // :272
            });
        var popup = PlainControls.Popover(row, isPresented, () =>
        {
            state.IsDeletePopoverShown = true; // :285, shared with the delete popover so the two can never race
            return BuildArchivePopoverContent(thread);
        }, PlacementMode.Right); // :270 arrowEdge: .trailing
        popup.Closed += (_, _) =>
        {
            state.IsDeletePopoverShown = false; // :287
            CarryOutConfirmedDeletion(); // :288
        };
        return new Panel { Children = { row, popup } };
    }

    /// <summary>:275-284 the archive popover's content, extracted so it is directly testable (see
    /// BuildDeletePopoverContent above for why). PR-SIDEBAR-094, PR-SIDEBAR-095, PR-SIDEBAR-096, PR-SIDEBAR-097.</summary>
    private ArchiveThreadPopover BuildArchivePopoverContent(ChatThread thread)
    {
        var threadID = thread.Id;
        return new ArchiveThreadPopover(
            Model.Thread(threadID) ?? thread,
            onArchive: () =>
            {
                state.PendingArchiveID = null; // :278
                Transaction.WithAnimation(Chrome.PanelSlide, () => Model.Archive(threadID)); // :279
            },
            onDelete: () => ConfirmDeletion(threadID, removeWorktree: false), // :281
            onDismiss: () => state.PendingArchiveID = null); // :283
    }

    /// <summary>:256-263 confirmDeletion(of:removeWorktree:). PR-SIDEBAR-089.</summary>
    private void ConfirmDeletion(Guid id, bool removeWorktree)
    {
        state.ConfirmedDeletion = new ConfirmedDeletion { ThreadID = id, RemoveWorktree = removeWorktree }; // :258
        state.PendingDeletionID = null; // :259
        state.PendingArchiveID = null; // :260
        if (!state.IsDeletePopoverShown) CarryOutConfirmedDeletion(); // :261
    }

    /// <summary>:295-306 carryOutConfirmedDeletion(). PR-SIDEBAR-089.</summary>
    private void CarryOutConfirmedDeletion()
    {
        if (state.ConfirmedDeletion is not { } confirmed) return; // :300
        state.ConfirmedDeletion = null; // :301
        var model = Model; // :302
        MainActor.Spawn(() => // :303-305 Task { @MainActor in ... }
        {
            model.Delete(confirmed.ThreadID, confirmed.RemoveWorktree);
            return Task.CompletedTask;
        });
    }

    /// <summary>:882-887, :354-358 the ellipsis path's live lookup, contrasted with the context menu's posted
    /// snapshot (:182-186, wired in Appear() as-is). PR-SIDEBAR-093.</summary>
    private static ChatThread LiveRenameTarget(AppModel model, ChatThread thread) => model.Thread(thread.Id) ?? thread; // :886, :357

    /// <summary>:638-641 <c>private struct ActivityGroup</c>.</summary>
    private sealed record ActivityGroup
    {
        public required string Title { get; init; } // :639
        public required ValueList<ChatThread> Threads { get; init; } // :640 var threads
    }

    /// <summary>:856-868 <c>private struct DragPeers</c>: what a drag reuses between pointer moves: the grabbed row's
    /// peers in their order, the group they form in the activity layout, and each peer's slot height.</summary>
    private sealed record DragPeers
    {
        public ValueList<Guid> Order { get; } // :859
        public ValueList<Guid>? Group { get; } // :860
        public ValueMap<Guid, double> Heights { get; } // :861

        // :863-867 init(_ peers: (order: [UUID], group: [UUID]?), heights: ([UUID]) -> [UUID: CGFloat])
        public DragPeers((ValueList<Guid> Order, ValueList<Guid>? Group) peers, Func<ValueList<Guid>, ValueMap<Guid, double>> heights)
        {
            Order = peers.Order; // :864
            Group = peers.Group; // :865
            Heights = heights(peers.Order); // :866
        }
    }
}

/// <summary>:905-963 <c>private struct ArchiveThreadPopover: View</c>: the archive shortcut's question. Part 4.
/// PR-SIDEBAR-095, PR-SIDEBAR-096.</summary>
internal sealed class ArchiveThreadPopover : Decorator
{
    // :912 @State private var keyMonitor: Any?, ported as the window-scoped handler pair (ENGINEERING DECISION, the
    // same substitution AttachmentPreviewPanel.cs:160-195 already makes for a local NSEvent monitor).
    private Avalonia.Controls.Window? monitorWindow;
    private Button? archiveButton;
    private readonly Action onArchive;
    private readonly Action onDismiss;

    /// <param name="thread">:906 <c>let thread: ChatThread</c>.</param>
    /// <param name="onArchive">:907 <c>let onArchive: () -> Void</c>.</param>
    /// <param name="onDelete">:908 <c>let onDelete: () -> Void</c>.</param>
    /// <param name="onDismiss">:909 <c>let onDismiss: () -> Void</c>.</param>
    // :914-962 body
    public ArchiveThreadPopover(ChatThread thread, Action onArchive, Action onDelete, Action onDismiss)
    {
        this.onArchive = onArchive;
        this.onDismiss = onDismiss;

        // :917 Text("Archive \(Text(verbatim: "\u{201C}\(thread.title)\u{201D}").fontWeight(.semibold))?")
        var question = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, // :919-920 lineLimit(2), fixedSize(vertical: true)
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis, // :920 .truncationMode(.tail)
            HorizontalAlignment = HorizontalAlignment.Stretch, // :922 frame(maxWidth: .infinity, alignment: .leading)
        };
        SwiftFont.Callout.Apply(question); // :918
        var styled = new StyledText("Archive ")
            + new StyledText("“" + thread.Title + "”").FontWeight(SwiftFont.Weight.Semibold) // :917
            + new StyledText("?");
        styled.Render(question);

        // :926-929 Text("Delete").foregroundStyle(.red), .buttonStyle(.glass). UNVERIFIED O-63: controlSize(.small)'s
        // exact height and horizontal padding have no sourced Windows number yet (only the regular capsule height,
        // Chrome.CapsuleContentHeight, is settled); the button below sizes to its content instead of a guessed height.
        var deleteLabel = new TextBlock { Text = "Delete", Foreground = Chrome.Danger.Brush(this) }; // :927
        var delete = GlassButton(deleteLabel, onDelete);
        ToolTip.SetTip(delete, "Remove the thread and its history"); // :929 .help(...)

        var archiveLabel = new TextBlock { Text = "Archive" }; // :931 Button("Archive", action: onArchive)
        var archive = GlassProminentButton(archiveLabel, onArchive);
        archive.IsDefault = true; // :932 .keyboardShortcut(.defaultAction)
        archive.Focusable = true; // :933 .focusable()
        ToolTip.SetTip(archive, "Move it to Archived (Return)"); // :936 .help(...)
        archiveButton = archive;

        var buttons = new StackPanel // :925 HStack(spacing: 6)
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { delete, archive },
            HorizontalAlignment = HorizontalAlignment.Right, // :923 Spacer(minLength: 0)
        };
        var column = new StackPanel // :916 VStack(alignment: .leading, spacing: 9)
        {
            Orientation = Orientation.Vertical,
            Spacing = 9,
            Children = { question, buttons },
        };
        var padded = new Border { Padding = new Thickness(8, 6, 8, 4), Child = column }; // :938-940
        var menu = new PopoverMenu(() => padded);
        menu.Width = 272; // :943 .frame(width: 272)
        Child = menu;

        AttachedToVisualTree += (_, _) => Appear(); // :944 .onAppear
        DetachedFromVisualTree += (_, _) => Disappear(); // :958 .onDisappear
    }

    // :926-929, :931-936 the untinted and accent-tinted twins of FollowUpEditor.cs's glass buttons, sized to their
    // content: OD-0005 governs the material, not this button's height.
    private static Button GlassButton(Control label, Action action)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        var framed = new Border { Padding = new Thickness(10, 4), Child = label };
        framed.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
        return PlainControls.Button(framed, action);
    }

    private static Button GlassProminentButton(Control label, Action action)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        var framed = new Border { Padding = new Thickness(10, 4), Child = label };
        framed.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.Accent, 0.3).Interactive());
        framed.Background = Chrome.Accent.Opacity(0.3).Brush(framed);
        return PlainControls.Button(framed, action);
    }

    // :944-956 onAppear: focus the Archive button, then install the window-scoped key handler.
    private void Appear()
    {
        archiveButton?.Focus(); // :945 isFocused = true
        if (TopLevel.GetTopLevel(this) is not Avalonia.Controls.Window window) return;
        monitorWindow = window;
        window.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true); // :946-955
    }

    // :958-961 onDisappear: tear the handler down.
    private void Disappear()
    {
        if (monitorWindow is { } window) window.RemoveHandler(InputElement.KeyDownEvent, OnWindowKeyDown);
        monitorWindow = null;
    }

    // :946-955 the monitor's body: Return/keypad Enter archives, Escape dismisses, any modifier held or any other key
    // passes through untouched.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (((ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers) != 0) return; // :949
        if (e.PhysicalKey is PhysicalKey.Enter or PhysicalKey.NumPadEnter) onArchive(); // :951 keyCode 36, 76
        else if (e.PhysicalKey == PhysicalKey.Escape) onDismiss(); // :952 keyCode 53
        else return; // :953 default: return event
        e.Handled = true; // :954 return nil
    }
}

/// <summary>:967-983 <c>private struct DeleteThreadPopover: View</c>. Part 4. PR-SIDEBAR-088.</summary>
internal sealed class DeleteThreadPopover : Decorator
{
    /// <param name="thread">:968 <c>let thread: ChatThread</c>.</param>
    /// <param name="onDelete">:969 <c>let onDelete: (_ removeWorktree: Bool) -> Void</c>.</param>
    // :971-982 body
    public DeleteThreadPopover(ChatThread thread, Action<bool> onDelete)
    {
        var menu = new PopoverMenu(() => // :971-981
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            stack.Children.Add(new PopoverSectionHeader("Delete this thread?")); // :973
            // :974 "“\(thread.title)” and its history will be removed. Files in your project stay as they are."
            stack.Children.Add(new PopoverNote(
                "“" + thread.Title + "” and its history will be removed. Files in your project stay as they are."));
            stack.Children.Add(new PopoverDivider()); // :975
            stack.Children.Add(new PopoverItem("Delete thread", () => onDelete(false), symbol: "trash", isDestructive: true)); // :976
            if (thread.WorktreePath is not null) // :977
                stack.Children.Add(new PopoverItem("Delete thread and worktree", () => onDelete(true), symbol: "trash", isDestructive: true)); // :978
            return stack;
        });
        menu.Width = 280; // :980 .frame(width: 280)
        Child = menu;
    }
}

/// <summary>:987-1025 <c>private struct RenameThreadPopover: View</c>. PR-SIDEBAR-091: STOP #6 (see
/// SidebarViewTests.cs).</summary>
internal sealed class RenameThreadPopover : Decorator
{
    /// <param name="title">:988 <c>let title: String</c>.</param>
    /// <param name="onRename">:989 <c>let onRename: (String) -> Void</c>.</param>
    /// <param name="onCancel">:990 <c>let onCancel: () -> Void</c>.</param>
    // :995-1000 init; :1002-1024 body. STOP #6 (this unit): `.textFieldStyle(.roundedBorder)` (:1009) and
    // `.buttonStyle(.bordered)`/`.buttonStyle(.borderedProminent)` (:1017, :1019-1022) have no Windows chrome anywhere
    // in this port yet, and no ED/PA decision (this packet's part 4 list: ED-008, ED-009, ED-010, ED-041, ED-051,
    // ED-059, ED-062, ED-149, ED-187, OD-0001, OD-0005, OD-0008, OD-0009, OD-0010, PA-0019) or engineering_decisions.md
    // entry names either style. Every other row-level popover this part ports (Delete, Archive) reuses PopoverItem or
    // the already-decided `.glass`/`.glassProminent` recipe (OD-0005); this one alone needs a new, undecided pair.
    public RenameThreadPopover(string title, Action<string> onRename, Action onCancel) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:995 (STOP #6)");
}

/// <summary>:1030-1056 <c>private struct SidebarItem: Identifiable</c>: one entry of the sidebar list.</summary>
internal sealed record SidebarItem
{
    /// <summary>:1031-1048 <c>enum Kind</c> (R-ENUM-2).</summary>
    public abstract record Kind
    {
        private Kind() { }

        public sealed record Gap(double Value) : Kind; // :1032

        public sealed record Project(Marrow.Core.Models.Project Value, long Count) : Kind; // :1034

        public sealed record Header(string Value, bool IsFirst) : Kind; // :1035

        public sealed record SettledHeader(long Count, bool IsFirst) : Kind; // :1038

        // :1041
        public sealed record Thread(ChatThread Value, string? ProjectName, ProjectIcon? ProjectIcon, ValueList<Guid>? Peers, bool HasHelpers) : Kind;

        public sealed record Helper(ChatThread Value, bool IsLast) : Kind; // :1043

        public sealed record HeadCards(ChatThread Parent, ValueList<ChatThread> Heads) : Kind; // :1045

        public sealed record HelperStub(ChatThread Parent, ValueList<ChatThread> Helpers) : Kind; // :1047
    }

    public required string Id { get; init; } // :1050

    public required Kind SidebarItemKind { get; init; } // :1051 Swift: kind (R-NAME-3)

    // :1053-1055 static func id(for thread: ChatThread) -> String. Swift: id(for:); `Id` is the record's property.
    public static string IdFor(ChatThread thread) => thread.SidebarItemID; // :1054
}

/// <summary>The file's extensions of types it does not own (R-NAME-4, ED-164): <c>private extension ChatThread</c>
/// (:1058-1062) and <c>private extension AnyTransition</c> (:1064-1076).</summary>
internal static class SidebarViewExtensions
{
    extension(ChatThread thread)
    {
        // :1059-1061 var sidebarItemID: String
        public string SidebarItemID =>
            thread.IsSettled ? "settled-" + SwiftUUID.UuidString(thread.Id) : SwiftUUID.UuidString(thread.Id); // :1060
    }

    extension(SwiftTransition)
    {
        // :1070-1075 static var sidebarRow: AnyTransition
        public static SwiftTransition SidebarRow => SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Opacity.Animation(Animation.EaseIn(duration: 0.18).Delay(0.12)), // :1072
            removal: SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.06))); // :1073
    }
}

/// <summary>:1078-1230 <c>private struct ProjectRow: View</c>.</summary>
internal sealed class ProjectRow : Decorator
{
    /// <param name="snapshot">:1081 <c>let snapshot: Project</c>.</param>
    /// <param name="count">:1088 <c>let count: Int</c>.</param>
    /// <param name="togglesExpansion">:1089 <c>var togglesExpansion = true</c>.</param>
    // :1093-1163 body
    public ProjectRow(Project snapshot, long count, bool togglesExpansion = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1093");

    // :1168-1203 private static func contextMenuActions(project:isActive:hasOverride:model:) -> [RowAction]
    private static ValueList<RowAction> ContextMenuActions(Project project, bool isActive, bool hasOverride, AppModel model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1168");
}

/// <summary>:1233-1236 <c>private struct ProjectPairChoice: Identifiable</c>.</summary>
internal sealed record ProjectPairChoice
{
    public required Guid Id { get; init; } // :1234
    public required string Title { get; init; } // :1235
}

/// <summary>:1241-1270 <c>private struct ProjectPairMenuItems: View</c>.</summary>
internal sealed class ProjectPairMenuItems : Decorator
{
    /// <param name="projectID">:1242 <c>let projectID: UUID</c>.</param>
    /// <param name="selectedID">:1243 <c>let selectedID: UUID?</c>.</param>
    /// <param name="pairs">:1244 <c>let pairs: [ProjectPairChoice]</c>.</param>
    /// <param name="model">:1245 <c>let model: AppModel</c>.</param>
    // :1247-1269 body
    public ProjectPairMenuItems(Guid projectID, Guid? selectedID, IReadOnlyList<ProjectPairChoice> pairs, AppModel model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1247");
}

/// <summary>:1272-1312 <c>private enum ThreadRowMetrics</c>.</summary>
internal static class ThreadRowMetrics
{
    public const double DetailedVerticalPadding = 7; // :1273
    public const double DetailedHeight = 48; // :1275
    public const double HelperHeight = 24; // :1277
    public const double SettledHeight = 24; // :1279
    public const double SettledGap = 4; // :1281
    public const double SettledCheckInset = 6; // :1284
    public const double OpenCheckInset = 26; // :1285
    public const double FoldInset = 46; // :1288
    public const double SettledTitleInset = 22; // :1289
    public const double ConnectorWidth = 28; // :1291
    public const double ConnectorLineX = 18; // :1292
    public const double HeadGlyphSize = 14; // :1295
    public const double HeadGlyphPitch = 22; // :1297
    public const double HeadBadgeOverhang = 3; // :1301
    public const double HeadRowGap = 6; // :1303
    public const double HeadCardPadding = 6; // :1304

    // :1305-1311 A head card's height for `rows` rows of glyphs: the rows, the gaps between them and the padding. The
    // card reports its own height as it lays out; this stands in until the first measurement lands.
    public static double HeadCardHeight(long rows)
    {
        var count = Math.Max(1, rows); // :1309
        return 2 * HeadCardPadding + count * HeadGlyphSize + (count - 1) * HeadRowGap; // :1310
    }
}

/// <summary>:1317-1425 <c>private struct SidebarHelperRow: View, Equatable</c>.</summary>
internal sealed class SidebarHelperRow : Decorator
{
    /// <param name="snapshot">:1320 <c>let snapshot: ChatThread</c>.</param>
    /// <param name="isLast">:1326 <c>let isLast: Bool</c>.</param>
    /// <param name="menuRequests">:1330 <c>let menuRequests: SidebarMenuRequests</c>.</param>
    /// <param name="onFold">:1331 <c>let onFold: () -> Void</c>.</param>
    /// <param name="onRename">:1332 <c>let onRename: () -> Void</c>.</param>
    /// <param name="onDelete">:1333 <c>let onDelete: () -> Void</c>.</param>
    // :1338-1409 body. Still MARROW_STUB: the constructor needs RowGlideAnimator.Shared.IsHiding(_:) and
    // model.IsSelected(_:) wired through a live per-row @State (isHovering, isMenuPresented) that no part has added
    // yet; the pieces below are written so that constructor can call them once it is assembled, each proven directly.
    public SidebarHelperRow(ChatThread snapshot, bool isLast, SidebarMenuRequests menuRequests, Action onFold, Action onRename, Action onDelete) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1338");

    // :1361 .font(.system(.callout, weight: isSelected || thread.hasUnread ? .medium : .regular)). PR-SIDEBAR-107.
    internal static SwiftFont.Weight TitleWeight(bool isSelected, bool hasUnread) =>
        isSelected || hasUnread ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular;

    // :1362 .foregroundStyle(Chrome.primaryText.opacity(isSelected ? 0.96 : 0.8)). PR-SIDEBAR-107.
    internal static double TitleOpacity(bool isSelected) => isSelected ? 0.96 : 0.8;

    // :1365-1367 the trailing padding: 8 (Chrome.rowHorizontalPadding) plus 26 while the actions show, 18 otherwise.
    // PR-SIDEBAR-107.
    internal static double TrailingPadding(bool showsActions) => Chrome.RowHorizontalPadding + (showsActions ? 26 : 18);

    // :1370-1372 the row's own background: the label colour at 0.12 selected, 0.06 hovered, transparent otherwise, in
    // the row's rounded rect, animated on selection. Reuses SidebarThreadRow.Fill (:1875-1877, PR-SIDEBAR-063): the
    // identical formula. PR-SIDEBAR-107.
    internal static double BackgroundFill(bool isSelected, bool isHovering) => SidebarThreadRow.Fill(isSelected, isHovering);

    // :1354-1357 HydraGlyph(persona: head.persona, size: ThreadRowMetrics.headGlyphSize, status: head.status): no
    // isRunning argument, unlike HeadCardsRow's explicit isRunning: (info.status == .running) (:1477). Reproduces the
    // upstream defect (porting/upstream_defects.md entry 65): a running head's glyph shows no breath ring here, even
    // for the same head the parent's card shows with one. PR-SIDEBAR-108.
    internal static HydraGlyph? HeadGlyph(ChatThread thread) =>
        thread.Hydra is { } head ? new HydraGlyph(head.Persona, ThreadRowMetrics.HeadGlyphSize, status: head.HydraHeadInfoStatus) : null;

    // :1375-1384 the trailing overlay, 6 pt from the edge: the ellipsis while hovered or the menu is open, the
    // activity status otherwise. A helper row never shows a settle check, an archive button, a fold chevron or a
    // relative time. PR-SIDEBAR-109.
    internal static Control BuildTrailingOverlay(bool showsActions, ChatThread thread, Control actionsButton)
    {
        Control content = showsActions ? actionsButton : new ActivityStatus(thread); // :1376-1380
        content.Margin = new Thickness(0, 0, 6, 0); // :1382 .padding(.trailing, 6)
        content.HorizontalAlignment = HorizontalAlignment.Right;
        return content;
    }
}

/// <summary>:1427-1548 <c>private struct HeadCardsRow: View</c>: the parent's Hydra heads as glyph cards.</summary>
internal sealed class HeadCardsRow : Decorator
{
    /// <param name="parentSnapshot">:1429 <c>let parentSnapshot: ChatThread</c>.</param>
    /// <param name="headSnapshots">:1430 <c>let headSnapshots: [ChatThread]</c>.</param>
    /// <param name="menuRequests">:1431 <c>let menuRequests: SidebarMenuRequests</c>.</param>
    /// <param name="onFold">:1432 <c>let onFold: () -> Void</c>.</param>
    // :1440-1542 body
    public HeadCardsRow(ChatThread parentSnapshot, IReadOnlyList<ChatThread> headSnapshots, SidebarMenuRequests menuRequests, Action onFold) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1440");

    // :1545-1547 private static func chunk(_ heads: [ChatThread], by size: Int) -> [[ChatThread]]
    private static ValueList<ValueList<ChatThread>> Chunk(IReadOnlyList<ChatThread> heads, long by) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1545");
}

/// <summary>:1552-1618 <c>private struct HelperStubRow: View</c>: a parent's folded helpers as one line.</summary>
internal sealed class HelperStubRow : Decorator
{
    /// <param name="parentSnapshot">:1555 <c>let parentSnapshot: ChatThread</c>.</param>
    /// <param name="helperSnapshots">:1558 <c>let helperSnapshots: [ChatThread]</c>.</param>
    /// <param name="action">:1559 <c>let action: () -> Void</c>.</param>
    // :1568-1617 body
    public HelperStubRow(ChatThread parentSnapshot, IReadOnlyList<ChatThread> helperSnapshots, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1568");
}

/// <summary>:1622-1632 <c>private struct RidesWithDraggedParent: ViewModifier</c>.</summary>
/// <param name="ParentID">:1623 <c>let parentID: UUID?</c>.</param>
/// <param name="Drag">:1624 <c>let drag: RowDrag&lt;UUID&gt;</c>.</param>
internal sealed record RidesWithDraggedParent(Guid? ParentID, RowDrag<Guid> Drag)
{
    // :1626-1631 func body(content: Content) -> some View. PR-SIDEBAR-056: every helper row, the head card and the
    // folded stub under a dragged parent take the same offset and zIndex, so the thread and everything under it
    // move together.
    public Control Body(Control content)
    {
        var rides = ParentID is not null && Drag.Id == ParentID; // :1627
        content.RenderTransform = new TranslateTransform(0, rides ? Drag.VisualOffset : 0); // :1629 .offset(y:)
        content.ZIndex = rides ? 1 : 0; // :1630 .zIndex()
        return content;
    }
}

/// <summary>:1636-1660 <c>private struct HelperConnector: View</c>: the dotted connector beside a helper's row.
/// Part 4. PR-SIDEBAR-110.</summary>
internal sealed class HelperConnector : Decorator
{
    // :1642 static let style = StrokeStyle(lineWidth: 1, lineCap: .round, dash: [0.1, 3.6])
    private static readonly double[] Dash = [0.1, 3.6];

    /// <param name="endsHere">:1637 <c>let endsHere: Bool</c>.</param>
    /// <param name="action">:1638 <c>let action: () -> Void</c>.</param>
    // :1648-1659 body
    public HelperConnector(bool endsHere, Action action)
    {
        var state = new ViewState();
        var stroke = new Avalonia.Controls.Shapes.Path // :1649 HelperConnectorShape(endsHere: endsHere).stroke(...)
        {
            StrokeThickness = 1, // :1642 lineWidth: 1
            StrokeLineCap = PenLineCap.Round, // :1642 lineCap: .round
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>(Dash), // :1642 dash: [0.1, 3.6]
        };
        var fill = new AnimatedColor(stroke);
        stroke.Stroke = fill.Brush;
        stroke.SizeChanged += (_, e) => stroke.Data = new HelperConnectorShape(endsHere).Path(new Rect(e.NewSize)); // :1649-1651
        stroke.Track(() => state.IsHovering, hovering => fill.Set(Color(hovering), Transaction.Current)); // :1650 .animation(Chrome.hover, value:)
        var label = new Border { Child = stroke, IsHitTestVisible = true }; // :1651 .contentShape(.rect): the whole rect is the hit area
        var button = PlainControls.Button(label, action); // :1648 Button(action:)
        button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :1656 .onHover
        ToolTip.SetTip(button, "Hide helpers"); // :1657 .help("Hide helpers")
        AutomationProperties.SetName(button, "Hide helpers"); // :1658 .accessibilityLabel(Text("Hide helpers"))
        Child = button;
    }

    // :1640 @State private var isHovering = false
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    // :1644-1646 static func color(hovering: Bool) -> Color
    public static SwiftColor Color(bool hovering) =>
        Chrome.SecondaryText.Opacity(hovering ? 0.9 : 0.45); // :1645
}

/// <summary>:1662-1675 <c>private struct HelperConnectorShape: Shape</c>. Part 4. PR-SIDEBAR-110.</summary>
/// <param name="EndsHere">:1663 <c>let endsHere: Bool</c>.</param>
internal sealed record HelperConnectorShape(bool EndsHere)
{
    // :1665-1674 func path(in rect: CGRect) -> Path. Two disjoint strokes: the vertical stem (full height, or only to
    // the midpoint on the last row) and the horizontal tick into the row at the midpoint.
    public Geometry Path(Rect @in)
    {
        var x = ThreadRowMetrics.ConnectorLineX; // :1666
        var midY = @in.Center.Y; // :1667 rect.midY
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(x, @in.Top), isFilled: false); // :1668
            context.LineTo(new Point(x, EndsHere ? midY : @in.Bottom)); // :1669
            context.EndFigure(isClosed: false);
            context.BeginFigure(new Point(x, midY), isFilled: false); // :1670
            context.LineTo(new Point(@in.Right - 2, midY)); // :1671
            context.EndFigure(isClosed: false);
        }
        return geometry;
    }
}

/// <summary>:1682-2001 <c>private struct SidebarThreadRow: View, Equatable</c>: a thread in either layout.</summary>
internal sealed class SidebarThreadRow : Decorator
{
    /// <param name="snapshot">:1686 <c>let snapshot: ChatThread</c>.</param>
    /// <param name="projectName">:1692 <c>let projectName: String?</c>.</param>
    /// <param name="projectIcon">:1696 <c>let projectIcon: ProjectIcon?</c>.</param>
    /// <param name="menuRequests">:1700 <c>let menuRequests: SidebarMenuRequests</c>.</param>
    /// <param name="onRename">:1707 <c>let onRename: () -> Void</c> (moved before the defaulted inputs, ED-095).</param>
    /// <param name="onDelete">:1708 <c>let onDelete: () -> Void</c> (moved before the defaulted inputs, ED-095).</param>
    /// <param name="isDragged">:1702 <c>var isDragged = false</c>.</param>
    /// <param name="listFrame">:1704 <c>var listFrame: FrameHolder?</c>.</param>
    /// <param name="onToggleFold">:1706 <c>var onToggleFold: (() -> Void)?</c>.</param>
    // :1716-1872 body. The full body needs settle()/reopen(), archiveWithGenie(), makeActions(), RowGlideAnimator and
    // ThreadActions.makeContextMenu, none of which exist yet (parts 4+ of this unit; the test file's own header
    // already defers an attached, populated row to "parts 2 to 5"). Part 3 proves every record of this part that has
    // no missing collaborator, each as its own static piece below (Fill, BackgroundFill, TrailingClearance,
    // ShowsCheck, SelectThread, NoteHover, ApplyDraggedLift, BuildSettleCheckOverlay, BuildTrailingOverlay); the
    // constructor itself stays MARROW_STUB until a later part assembles them with the pieces still missing.
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
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:1716");

    // :1874-1877 private static func fill(isSelected: Bool, isHovering: Bool) -> Double. PR-SIDEBAR-063.
    internal static double Fill(bool isSelected, bool isHovering)
    {
        if (isSelected) return 0.12; // :1875
        return isHovering ? 0.06 : 0; // :1876
    }

    // :1750-1758 the row's own background: the pinned tint replaces the selection/hover fill while unselected, and a
    // selected pinned row gains a danger overlay on top of its selection fill. PR-SIDEBAR-064.
    private static (SwiftColor Background, SwiftColor? PinnedOverlay) BackgroundFill(bool isPinned, bool isSelected, bool isHovering)
    {
        var background = isPinned && !isSelected // :1751-1752
            ? Chrome.Danger.Opacity(0.10)
            : Chrome.Overlay(Fill(isSelected, isHovering));
        var overlay = isPinned && isSelected ? Chrome.Danger.Opacity(0.06) : null; // :1756-1758
        return (background, overlay);
    }

    // :1895-1898 private static func trailingClearance(isSettled:isDetailed:showsActions:showsFold:) -> CGFloat.
    // PR-SIDEBAR-076.
    private static double TrailingClearance(bool isSettled, bool isDetailed, bool showsActions, bool showsFold = false)
    {
        if (showsActions) return (isSettled ? 30 : 52) + (showsFold ? 20 : 0); // :1896
        return isDetailed || isSettled ? 18 : 52; // :1897
    }

    // :1729 let showsCheck = isSettled || (settles && (showsActions || isSettling)). PR-SIDEBAR-077.
    private static bool ShowsCheck(bool isSettled, bool settles, bool showsActions, bool isSettling) =>
        isSettled || (settles && (showsActions || isSettling));

    // :1763-1771 the check's overlay: alignment, and the leading or trailing inset, only while ShowsCheck. Null when
    // it does not show. PR-SIDEBAR-077.
    private static Control? BuildSettleCheckOverlay(bool isSettled, bool settles, bool showsActions, bool isSettling, bool isPopping, Action action)
    {
        if (!ShowsCheck(isSettled, settles, showsActions, isSettling)) return null; // :1764
        var check = new SettleCheck(isSettled, isPopping, action); // :1765
        check.HorizontalAlignment = isSettled ? HorizontalAlignment.Left : HorizontalAlignment.Right; // :1763 overlay(alignment:)
        check.Margin = isSettled
            ? new Thickness(ThreadRowMetrics.SettledCheckInset, 0, 0, 0) // :1768
            : new Thickness(0, 0, ThreadRowMetrics.OpenCheckInset, 0); // :1769
        return check;
    }

    // :1736-1738 Button { model.selectedThreadID = thread.id } label: { ... }. PR-SIDEBAR-061.
    private static void SelectThread(AppModel model, ChatThread thread) => model.SelectedThreadID = thread.Id; // :1737

    // :1816-1820 .onHover { hovering in withAnimation(Chrome.hover) { isHovering = hovering }; if hovering { ... } }.
    // The hover-state animation itself is the caller's ViewState, driven the same way SidebarRow drives IsHovering
    // (Chrome.cs); this is the hover closure's second effect, the one PR-SIDEBAR-062 names. PR-SIDEBAR-062.
    private static void NoteHover(AppModel model, ChatThread thread, bool hovering)
    {
        if (hovering) model.WarmDocuments([thread.Id]); // :1819
    }

    // :1838-1849 the lifted row's visual: an opaque fill and shadow appear behind it and it scales to 1.02, springing
    // in and out on response 0.25 damping 0.8 as isDragged changes. A directly testable seam; the row's own body
    // (part 4+, once settle/reopen/RowGlideAnimator/ThreadActions exist) wraps its content with this. PR-SIDEBAR-054.
    private static Control ApplyDraggedLift(Control content, Func<bool> isDragged)
    {
        var scale = new ScaleTransform(1, 1);
        content.RenderTransform = scale;
        var lift = new Border // :1841-1843
        {
            Background = Chrome.Overlay(0.12).Brush(content), // :1842
            BoxShadow = new BoxShadows(new BoxShadow { Color = Color.FromArgb((byte)(0.28 * 255), 0, 0, 0), Blur = 10, OffsetY = 4 }), // :1843
            Opacity = 0,
            IsHitTestVisible = false,
        }.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous); // :1735 shape, followed by both the row's own background and this lift
        var host = new Panel { Children = { lift, content } };
        var spring = Animation.Spring(response: 0.25, dampingFraction: 0.8); // :1847, a fixed animation (R-VIEW-4):
        // .animation(_, value:) ignores whatever ambient transaction changed isDragged.
        var current = 0d;
        var target = 0d;
        IFrameSource? frames = null;
        Action<TimeSpan>? pending = null;
        void Apply(double progress)
        {
            current = progress;
            var factor = 1 + 0.02 * progress; // :1846 .scaleEffect(isDragged ? 1.02 : 1)
            scale.ScaleX = factor;
            scale.ScaleY = factor;
            lift.Opacity = progress; // the fill/shadow fade with the same spring as the scale
        }
        host.Track(isDragged, dragged =>
        {
            var to = dragged ? 1d : 0d;
            if (to == target) return;
            target = to;
            if (pending is { } running) frames?.CancelFrame(running);
            pending = null;
            var from = current;
            var start = SwiftClock.Now;
            var source = FrameClock.Source.Current;
            Action<TimeSpan>? tick = null;
            tick = _ =>
            {
                var seconds = (SwiftClock.Now - start).TotalSeconds;
                var complete = seconds >= spring.Duration;
                Apply(complete ? to : from + (to - from) * spring.Progress(seconds));
                if (complete) pending = null;
                else source.RequestFrame(tick!);
            };
            frames = source;
            pending = tick;
            source.RequestFrame(tick);
        });
        return host;
    }

    // :1772-1801 the trailing overlay: hover buttons, activity status, status+time, or time alone, in that
    // precedence, with 6 pt of trailing padding on the whole group. archiveAction, actions and isMenuPresented are
    // the row's own collaborators (archiveWithGenie(), makeActions(), the ellipsis binding); the caller supplies them
    // once they exist (part 4+), so this dispatch and the three branches with no missing collaborator are provable
    // now. PR-SIDEBAR-072.
    private static Control BuildTrailingOverlay(
        ChatThread thread,
        bool showsActions,
        bool isDetailed,
        bool isSettled,
        bool hasProjectIcon,
        bool settles,
        Action archiveAction,
        IReadOnlyList<RowAction> actions,
        Binding<bool> isMenuPresented)
    {
        Control content;
        if (showsActions) // :1774
        {
            var cluster = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 }; // :1775
            if (!settles && !isSettled) // :1776
            {
                var archive = PlainControls.Button(new RowAccessoryIcon("archivebox"), archiveAction); // :1777-1782
                ToolTip.SetTip(archive, "Archive thread"); // :1783
                cluster.Children.Add(archive);
            }
            cluster.Children.Add(new RowActionsButton(actions, isMenuPresented)); // :1785
            content = cluster;
        }
        else if (isDetailed || isSettled) // :1787
        {
            content = new ActivityStatus(thread); // :1788
        }
        else if (hasProjectIcon) // :1789
        {
            content = new StackPanel // :1792-1795
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new ActivityStatus(thread), new RelativeTimeLabel(thread.UpdatedAt) },
            };
        }
        else
        {
            content = new RelativeTimeLabel(thread.UpdatedAt); // :1797
        }
        content.Margin = new Thickness(0, 0, 6, 0); // :1800 .padding(.trailing, 6)
        content.HorizontalAlignment = HorizontalAlignment.Right;
        return content;
    }

    // :1981-1984 static func mergeStage(of runtime: ThreadRuntime?) -> String?
    public static string? MergeStage(ThreadRuntime? of)
    {
        if (of is not { } runtime || !runtime.IsHydraMerging) return null; // :1982
        return (runtime.HydraMergeStage ?? "Merging") + "\U00002026"; // :1983 U+2026 HORIZONTAL ELLIPSIS
    }

    // MARK: Settling (part 4)

    /// <summary>:1863-1870 the row's own <c>.onChange(of: model.finishRequest)</c>: a settle or reopen asked for
    /// elsewhere is taken up only by the matching row, so it looks exactly like a click on the check.
    /// PR-SIDEBAR-083.</summary>
    internal static void FinishRequestReceived(AppModel model, ChatThread thread, bool isSettled, Action settle, Action reopen)
    {
        if (model.AppModelFinishRequest is not { } request || request.ThreadID != thread.Id || request.Reopens != isSettled) return; // :1864
        model.AppModelFinishRequest = null; // :1865
        if (request.Reopens) reopen(); else settle(); // :1866-1867
    }

    /// <summary>:1904-1910 settle(). The row's own exit: the check pops and the thread settles in the same pass, so
    /// the row leaves on the list's removal fade and no ghost flies. PR-SIDEBAR-079.</summary>
    internal static void Settle(AppModel model, ChatThread thread, bool isSettling, bool settleSound, Action<bool> setIsSettling)
    {
        if (isSettling || thread.IsSettled) return; // :1907
        if (settleSound) SettleChime.Play(); // :1908
        Transaction.WithAnimation(Animation.Spring(response: 0.3, dampingFraction: 0.45), () => setIsSettling(true)); // :1909
        Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settle(thread.Id, sounds: false)); // :1910
    }

    /// <summary>:1912-1916 reopen(). A settled row's check reopens it with a gliding ghost. PR-SIDEBAR-081.</summary>
    internal static void Reopen(AppModel model, ChatThread thread, Action glide)
    {
        if (!thread.IsSettled) return; // :1913
        glide(); // :1914 glide(.reopen), launched first so the arriving row can report its frame to it
        model.ReopenAnimated(thread.Id); // :1915
    }

    /// <summary>:1929-1953 glide(_:). Sends the row's ghost from this row's frame to where the thread's other row
    /// will be. Only a reopen flies (see <see cref="Reopen"/>); the direction parameter and the settled-fallback
    /// arithmetic are kept for the flight's shape, as the source itself notes. PR-SIDEBAR-081.</summary>
    internal static void Glide(
        RowGlideAnimator animator,
        RowGlideAnimator.Glide.Direction direction,
        Rect listFrame,
        Rect windowFrame,
        ChatThread thread,
        string? projectName,
        ProjectIcon? projectIcon,
        bool isSelected,
        bool isHovering,
        ThreadRuntime? runtime,
        ThemeVariant colorScheme)
    {
        var from = windowFrame; // :1930
        var bounds = listFrame; // :1931
        var settled = direction == RowGlideAnimator.Glide.Direction.Settle; // :1933
        // :1935-1940 the thread as its other row will show it.
        var arriving = thread with
        {
            IsSettled = settled,
            IsPinned = settled ? false : thread.IsPinned,
            HasUnread = settled ? false : thread.HasUnread,
        };
        var arrivalHeight = settled // :1941
            ? ThreadRowMetrics.SettledHeight
            : projectName is not null ? ThreadRowMetrics.DetailedHeight : Chrome.RowHeight;
        var fallback = new Rect(from.X, settled ? bounds.Bottom : bounds.Y - arrivalHeight, from.Width, arrivalHeight); // :1942-1947
        Control Badge() => new ThreadGhostBadge(thread, runtime); // :1949
        var departureClearance = TrailingClearance(thread.IsSettled, projectName is not null, showsActions: true); // :1952
        var arrivalClearance = TrailingClearance(settled, !settled && projectName is not null, showsActions: false); // :1953
        var departure = animator.Render(
            new ThreadRowFace(thread, projectName, isSelected, departureClearance, Badge, projectIcon), from.Size, colorScheme); // :1954-1957
        if (departure is null) return; // :1958 guard let departure = ..., let arrival = ... else { return }
        var arrival = animator.Render(
            new ThreadRowFace(arriving, projectName, isSelected, arrivalClearance, Badge, projectIcon),
            new Size(from.Width, arrivalHeight), colorScheme); // :1959-1961
        if (arrival is null) return;
        animator.Launch( // :1962-1970
            thread.Id,
            direction,
            from,
            fallback,
            bounds,
            departure,
            arrival,
            Fill(isSelected, isHovering),
            Fill(isSelected, false));
    }

    // MARK: Archiving (part 4)

    /// <summary>:1988-1999 archiveWithGenie(). The hover archive button flies a genie ghost of the row and archives
    /// the thread. PR-SIDEBAR-084.</summary>
    internal static void ArchiveWithGenie(
        GenieAnimator animator,
        AppModel model,
        ChatThread thread,
        string? projectName,
        ProjectIcon? projectIcon,
        bool isSelected,
        ThreadRuntime? runtime,
        Rect windowFrame,
        ThemeVariant colorScheme)
    {
        var fill = Chrome.Overlay(isSelected ? 0.12 : 0.06); // :1991
        var badge = new ThreadGhostBadge(thread, runtime); // :1993
        animator.Launch(windowFrame, colorScheme, () => // :1994-1997
        {
            var face = new ThreadRowFace(thread, projectName, isSelected, 52, () => badge, projectIcon);
            // :1992, :1996 .background { shape.fill(fill) }: the row's own rounded rect, filled, drawn behind the face.
            var backed = new Border { Child = face };
            backed.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
            backed.Background = fill.Brush(backed);
            return backed;
        });
        Transaction.WithAnimation(Chrome.PanelSlide, () => model.Archive(thread.Id)); // :1998
    }
}

/// <summary>:2006-2079 <c>private struct ThreadRowFace&lt;Badge: View&gt;: View</c>: what a thread row shows, drawn
/// by the live row and by its ghosts.</summary>
internal sealed class ThreadRowFace : Decorator
{
    /// <param name="thread">:2007 <c>let thread: ChatThread</c>.</param>
    /// <param name="projectName">:2009 <c>let projectName: String?</c>.</param>
    /// <param name="isSelected">:2011 <c>let isSelected: Bool</c> (moved before the defaulted inputs, ED-095).</param>
    /// <param name="trailingClearance">:2013 <c>let trailingClearance: CGFloat</c> (moved, ED-095).</param>
    /// <param name="badge">:2017 <c>@ViewBuilder let badge: Badge</c> (moved, ED-095).</param>
    /// <param name="projectIcon">:2010 <c>var projectIcon: ProjectIcon? = nil</c>.</param>
    /// <param name="mergeStage">:2015 <c>var mergeStage: String? = nil</c>.</param>
    // :2019-2072 body. PR-SIDEBAR-065, PR-SIDEBAR-066, PR-SIDEBAR-067, PR-SIDEBAR-070.
    public ThreadRowFace(
        ChatThread thread,
        string? projectName,
        bool isSelected,
        double trailingClearance,
        Func<Control> badge,
        ProjectIcon? projectIcon = null,
        string? mergeStage = null)
    {
        var isSettled = thread.IsSettled; // :2020
        var isDetailed = projectName is not null && !isSettled; // :2021

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") }; // :2022 HStack(spacing: 8)

        if (!isDetailed && !isSettled) // :2023 PR-SIDEBAR-066
        {
            var leading = projectIcon is { } compactIcon ? new ProjectIconMark(compactIcon, 12) : badge(); // :2025-2029
            leading.Width = Chrome.IconSize; // :2031 .frame(width: Chrome.iconSize)
            leading.HorizontalAlignment = HorizontalAlignment.Center;
            leading.VerticalAlignment = VerticalAlignment.Center;
            leading.Margin = new Thickness(0, 0, 8, 0); // HStack(spacing: 8)'s gap before the text column
            Grid.SetColumn(leading, 0);
            row.Children.Add(leading);
        }

        var texts = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3, VerticalAlignment = VerticalAlignment.Center }; // :2034
        if (isDetailed && projectIcon is { } detailedIcon) // :2037 PR-SIDEBAR-067
        {
            var mark = new ProjectIconMark(detailedIcon, 14) { VerticalAlignment = VerticalAlignment.Center }; // :2039
            var titled = new StackPanel // :2038 HStack(spacing: 6)
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { mark, MakeTitle(thread, isSettled, isSelected) },
            };
            foreach (var part in titled.Children) part.VerticalAlignment = VerticalAlignment.Center;
            texts.Children.Add(titled);
        }
        else
        {
            texts.Children.Add(MakeTitle(thread, isSettled, isSelected)); // :2047-2051
        }

        if (projectName is { } name && !isSettled && projectIcon is null) // :2053 PR-SIDEBAR-070
        {
            var glyph = SwiftImage.SystemName(thread.WorktreePath is null ? "folder" : "arrow.triangle.branch") // :2055
                .Font(SwiftFont.Footnote); // :2056
            glyph.ForegroundStyle(Chrome.SecondaryText); // :2061
            var label = new TextBlock { Text = mergeStage ?? name, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }; // :2057-2059
            SwiftFont.Callout.Apply(label); // :2058
            label.Foreground = Chrome.SecondaryText.Brush(label); // :2061
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { glyph, label } }; // :2054
            foreach (var part in line.Children) part.VerticalAlignment = VerticalAlignment.Center;
            texts.Children.Add(line);
        }

        texts.Margin = new Thickness(0, 0, 4, 0); // :2065 Spacer(minLength: 4)
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        Child = new Border
        {
            Child = row,
            Padding = new Thickness(
                Chrome.RowHorizontalPadding + (isSettled ? ThreadRowMetrics.SettledTitleInset : 0), // :2067
                isDetailed ? ThreadRowMetrics.DetailedVerticalPadding : 0, // :2069
                Chrome.RowHorizontalPadding + trailingClearance, // :2068
                isDetailed ? ThreadRowMetrics.DetailedVerticalPadding : 0),
            MinHeight = isSettled ? ThreadRowMetrics.SettledHeight : Chrome.RowHeight, // :2070
            HorizontalAlignment = HorizontalAlignment.Stretch, // :2071 .frame(maxWidth: .infinity, alignment: .leading)
        };
    }

    // :2040-2051 the title text, shared by the detailed-with-mark layout and the plain one.
    private static TextBlock MakeTitle(ChatThread thread, bool isSettled, bool isSelected)
    {
        var title = new TextBlock { Text = thread.Title, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }; // :2040/:2047, :2043-2044/:2050-2051
        SwiftFont.System(isSettled ? 12 : 13, !isSettled && (isSelected || thread.HasUnread) ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular).Apply(title); // :2041/:2048
        title.Foreground = Chrome.PrimaryText.Opacity(TitleOpacity(isSettled, isSelected)).Brush(title); // :2042/:2049
        return title;
    }

    // :2075-2078 private static func titleOpacity(isSettled: Bool, isSelected: Bool) -> Double. PR-SIDEBAR-065.
    private static double TitleOpacity(bool isSettled, bool isSelected)
    {
        if (isSettled) return isSelected ? 0.75 : 0.55; // :2076
        return isSelected ? 1 : 0.92; // :2077
    }
}

/// <summary>:2083-2109 <c>private struct ThreadGhostBadge: View</c>: the badge as a ghost carries it. Part 4, built
/// so Glide and ArchiveWithGenie (PR-SIDEBAR-081, PR-SIDEBAR-084) have a real badge to carry, not a stub.</summary>
internal sealed class ThreadGhostBadge : Decorator
{
    public ChatThread Thread { get; } // :2084
    public bool NeedsInput { get; } // :2085
    public bool IsRunning { get; } // :2086

    // :2088-2092 init(thread:runtime:); :2094-2108 body
    public ThreadGhostBadge(ChatThread thread, ThreadRuntime? runtime)
    {
        Thread = thread;
        NeedsInput = runtime is { } r1 && (r1.Approvals.Count > 0 || r1.Questions.Count > 0); // :2089
        IsRunning = runtime is { } r2 && (r2.IsRunning || r2.IsHydraMerging || r2.HasWorkingHelpers); // :2090
        Child = new SidebarIconBadge(() => // :2095
            NeedsInput
                ? Tinted(new SidebarSymbol("hand.raised.fill"), Chrome.Orange) // :2097-2099
                : IsRunning
                    ? new MiniSpinner(cellSize: 2.4, isStill: true) // :2100-2101
                    : thread.IsPinned
                        ? Tinted(new SidebarSymbol("pin.fill", scale: 0.9), Chrome.Danger.Opacity(0.85)) // :2102-2104
                        : new ProviderIcon { Provider = thread.Provider, Size = 14 }); // :2105-2106
    }

    private static Control Tinted(Control symbol, SwiftColor color)
    {
        symbol.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, color.Brush(symbol));
        return symbol;
    }
}

/// <summary>:2114-2141 <c>private struct SettleCheck: View</c>.</summary>
internal sealed class SettleCheck : Decorator
{
    /// <param name="isSettled">:2115 <c>let isSettled: Bool</c>.</param>
    /// <param name="isPopping">:2117 <c>let isPopping: Bool</c>.</param>
    /// <param name="action">:2118 <c>let action: () -> Void</c>.</param>
    // :2122-2140 body. PR-SIDEBAR-078.
    public SettleCheck(bool isSettled, bool isPopping, Action action)
    {
        var state = new ViewState();
        var glyphHost = new Panel { Width = 20, Height = 20, Background = Brushes.Transparent }; // :2130 frame, :2131 contentShape(.rect)
        Control? currentGlyph = null;
        var button = PlainControls.Button(glyphHost, action); // :2125, :2134 .buttonStyle(.plain)
        button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :2135-2137
        button.Track(() => state.IsHovering, hovering =>
        {
            var filled = isPopping || (isSettled && !hovering); // :2124
            var isDone = isSettled || isPopping; // :2123
            var color = isDone || hovering
                ? Chrome.Success.Opacity(isSettled && !hovering ? 0.8 : 1) // :2128
                : Chrome.SecondaryText;
            if (currentGlyph is { } previous) glyphHost.Children.Remove(previous);
            // :2129 MARROW_DEFERRED ED-182 (SwiftImage.cs's own deferral list names this site): the symbol-replace
            // content transition is a static swap here, not a crossfade.
            var glyph = SwiftImage.SystemName(filled ? "checkmark.circle.fill" : "checkmark.circle") // :2126
                .Font(Chrome.InlineIconFont); // :2127
            glyph.ForegroundStyle(color); // :2128
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.RenderTransform = new ScaleTransform(isPopping ? 1.35 : 1, isPopping ? 1.35 : 1); // :2132
            glyph.IsHitTestVisible = false;
            currentGlyph = glyph;
            glyphHost.Children.Add(glyph);
        });
        var tip = isSettled ? "Reopen thread" : "Settle thread"; // :2138-2139
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip); // :2139 .accessibilityLabel
        Child = button;
    }

    // :2120 @State private var isHovering = false
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }
}

/// <summary>:2143-2175 <c>private struct ThreadBadge: View</c>.</summary>
internal sealed class ThreadBadge : Decorator
{
    /// <param name="thread">:2145 <c>let thread: ChatThread</c>.</param>
    // :2144 @Environment(AppModel.self) private var model
    private AppModel Model => AppEnvironment.GetAppModel(this);

    // :2147-2174 body. PR-SIDEBAR-068, PR-SIDEBAR-069.
    public ThreadBadge(ChatThread thread)
    {
        // Every field the body reads off the runtime is packed into the tracked tuple, not read inside apply: only
        // reads performed inside the Track selector are recorded as dependencies (ViewLifetime.Track, Observation.Track),
        // matching how SwiftUI's own body observes every property this closure touches (:2148-2151), and the pattern
        // this file's own other Track calls already use (:640, :1965).
        this.Track(() =>
        {
            var runtime = Model.ExistingRuntime(thread.Id); // :2148
            var needsInput = runtime is { Approvals.Count: > 0 } || runtime is { Questions.Count: > 0 }; // :2149
            var mergeStage = SidebarThreadRow.MergeStage(runtime); // :2150
            var isRunning = runtime?.IsRunning == true || mergeStage is not null || runtime?.HasWorkingHelpers == true; // :2151
            return (NeedsInput: needsInput, MergeStage: mergeStage, IsRunning: isRunning);
        }, value =>
        {
            var (needsInput, mergeStage, isRunning) = value;

            SwiftColor? tint = null;
            Control glyph;
            if (needsInput) // :2153
            {
                glyph = new SidebarSymbol("hand.raised.fill"); // :2154
                tint = Chrome.Orange; // :2155
            }
            else if (isRunning) // :2156
            {
                var spinner = new MiniSpinner(cellSize: 2.4); // :2157
                ToolTip.SetTip(spinner, mergeStage ?? ""); // :2158
                glyph = spinner;
            }
            else if (thread.IsPinned) // :2159
            {
                glyph = new SidebarSymbol("pin.fill", scale: 0.9); // :2160
                tint = Chrome.Danger.Opacity(0.85); // :2161
            }
            else
            {
                glyph = new ProviderIcon { Provider = thread.Provider, Size = 14 }; // :2163
            }

            var badge = new SidebarIconBadge(() => glyph); // :2152, :2165
            if (tint is { } color) Recolor(badge, color); // reasserts over SidebarIconBadge's own default (below)

            var overlay = new Panel { Children = { badge } }; // :2166 .overlay(alignment: .topTrailing)
            if (thread.HasUnread && !needsInput && !isRunning) // :2167
            {
                var dot = new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 7, // :2170
                    Height = 7,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -2.5, -2.5, 0), // :2171 .offset(x: 2.5, y: -2.5)
                };
                dot.Fill = (thread.LastStatus == TurnStatus.Failed ? Chrome.Danger : Chrome.Accent).Brush(dot); // :2169
                overlay.Children.Add(dot);
            }
            Child = overlay;
        });
    }

    // SidebarIconBadge (UI/Chrome/Chrome.cs :670-679) sets its glyph's TextElement.ForegroundProperty to the label
    // colour at 0.85 unconditionally, after the glyph factory returns; SwiftUI's own foregroundStyle lets the
    // nearer (the glyph's own) modifier win over the wrapper's, so a coloured branch here (:2155, :2161) must
    // reassert its colour once more, after the badge, to match what the source actually draws.
    private static void Recolor(SidebarIconBadge badge, SwiftColor color)
    {
        if (badge.Child is Panel { Children.Count: > 0 } panel && panel.Children[0] is Control glyph)
            glyph.SetValue(TextElement.ForegroundProperty, color.Brush(glyph));
    }
}

/// <summary>:2184-2188 <c>@Observable private final class SidebarMenuRequests</c>: context-menu intents carried
/// outside any row's state.</summary>
internal sealed class SidebarMenuRequests : ObservableModel
{
    private ChatThread? rename; // :2186
    private ChatThread? delete; // :2187

    // :2186 var rename: ChatThread?
    public ChatThread? Rename
    {
        get => Read(rename);
        set => Set(ref rename, value);
    }

    // :2187 var delete: ChatThread?
    public ChatThread? Delete
    {
        get => Read(delete);
        set => Set(ref delete, value);
    }
}

/// <summary>:2191-2313 <c>@MainActor private enum ThreadActions</c>: what a thread row offers.</summary>
internal static class ThreadActions
{
    // :2193-2246 static func make(model:thread:onRename:onDelete:onSettle:onReopen:) -> [RowAction]. Order: Rename;
    // Pin/Unpin; Forward to new thread; Reveal worktree; the settle/archive group; Delete. PR-SIDEBAR-098 (order and
    // equivalence with MakeContextMenu below), PR-SIDEBAR-099 (Pin/Unpin), PR-SIDEBAR-100 (Forward), PR-SIDEBAR-101
    // (Reveal, OD-0008 B1 deviation), PR-SIDEBAR-102 (a helper offers no Pin, no Settle).
    public static ValueList<RowAction> Make(AppModel model, ChatThread thread, Action onRename, Action onDelete, Action? onSettle = null, Action? onReopen = null)
    {
        var items = new List<RowAction> { new() { Title = "Rename", Symbol = "pencil", Action = onRename } }; // :2200
        if (!thread.IsHelper && !thread.IsSettled) // :2204, :2196-2198
        {
            items.Add(new RowAction
            {
                Title = thread.IsPinned ? "Unpin" : "Pin", // :2205
                Symbol = thread.IsPinned ? "pin.slash" : "pin", // :2205
                Action = () => Transaction.WithAnimation(Chrome.PanelSlide, () => // :2206-2207
                    model.UpdateThread(thread.Id, t => t with { IsPinned = !t.IsPinned })),
            });
        }
        items.Add(new RowAction // :2210-2212
        {
            Title = "Forward to new thread",
            Symbol = "arrowshape.turn.up.right",
            Action = () => MainActor.Spawn(async () => { await model.ForwardThread(thread.Id); return; }),
        });
        if (thread.WorktreePath is { } path) // :2214
        {
            items.Add(new RowAction // :2215, OD-0008 B1: the label deviates from upstream's "Reveal worktree in Finder"
            {
                Title = "Reveal worktree in File Explorer",
                Symbol = "folder",
                Action = () => ShellWorkspace.RevealInFileExplorer(path),
            });
        }
        var settleFirst = !thread.IsHelper && model.Settings.ThreadFinishAction == ThreadFinishAction.Settle; // :2220
        var archive = new RowAction // :2221-2223
        {
            Title = "Archive",
            Symbol = "archivebox",
            StartsGroup = !settleFirst,
            Action = () => Transaction.WithAnimation(Chrome.PanelSlide, () => model.Archive(thread.Id)),
        };
        if (thread.IsHelper) // :2224
        {
            items.Add(archive); // :2225
        }
        else
        {
            var settle = new RowAction // :2227-2231
            {
                Title = thread.IsSettled ? "Reopen" : "Settle",
                Symbol = thread.IsSettled ? "arrow.uturn.backward" : "checkmark.circle",
                StartsGroup = settleFirst,
                Action = () => // :2233-2240 the row's own handlers fly the ghost; without them the thread just moves
                {
                    if (thread.IsSettled) { if (onReopen is { } reopen) reopen(); else model.ReopenAnimated(thread.Id); }
                    else if (onSettle is { } settleNow) settleNow();
                    else model.SettleAnimated(thread.Id);
                },
            };
            items.AddRange(settleFirst ? [settle, archive] : [archive, settle]); // :2242
        }
        items.Add(new RowAction { Title = "Delete…", Symbol = "trash", IsDestructive = true, Action = onDelete }); // :2244
        return new ValueList<RowAction>(items);
    }

    // :2253-2312 static func makeContextMenu(model:thread:settleFirst:requests:) -> [RowAction]. The identical list,
    // built from value snapshots with weak model captures (:2245-2250): the AppKit menu outlives the right-click, so
    // its callbacks must not retain the row. Rename and Delete post to requests; Settle/Reopen post a finish request;
    // everything else touches only the model. PR-SIDEBAR-098, PR-SIDEBAR-099, PR-SIDEBAR-100, PR-SIDEBAR-101,
    // PR-SIDEBAR-102, PR-SIDEBAR-103.
    public static ValueList<RowAction> MakeContextMenu(AppModel model, ChatThread thread, bool settleFirst, SidebarMenuRequests requests)
    {
        var threadID = thread.Id; // :2258
        var weakModel = new WeakReference<AppModel>(model); // [weak model]
        var items = new List<RowAction>
        {
            new() // :2259-2262
            {
                Title = "Rename",
                Symbol = "pencil",
                Action = () => { if (weakModel.TryGetTarget(out _)) requests.Rename = thread; }, // :2260-2261
            },
        };
        if (!thread.IsHelper && !thread.IsSettled) // :2265
        {
            var isPinned = thread.IsPinned; // :2266
            items.Add(new RowAction
            {
                Title = isPinned ? "Unpin" : "Pin", // :2267
                Symbol = isPinned ? "pin.slash" : "pin",
                Action = () => // :2267-2272
                {
                    if (!weakModel.TryGetTarget(out var live)) return; // :2269
                    Transaction.WithAnimation(Chrome.PanelSlide, () => live.UpdateThread(threadID, t => t with { IsPinned = !t.IsPinned })); // :2270-2271
                },
            });
        }
        items.Add(new RowAction // :2277-2280
        {
            Title = "Forward to new thread",
            Symbol = "arrowshape.turn.up.right",
            Action = () =>
            {
                if (!weakModel.TryGetTarget(out var live)) return; // :2278
                MainActor.Spawn(async () => { await live.ForwardThread(threadID); return; }); // :2279
            },
        });
        if (thread.WorktreePath is { } path) // :2282
        {
            items.Add(new RowAction // :2283-2286
            {
                Title = "Reveal worktree in File Explorer", // OD-0008 B1 deviation
                Symbol = "folder",
                Action = () => { if (weakModel.TryGetTarget(out _)) ShellWorkspace.RevealInFileExplorer(path); }, // :2284-2285
            });
        }
        var archive = new RowAction // :2288-2292
        {
            Title = "Archive",
            Symbol = "archivebox",
            StartsGroup = !settleFirst,
            Action = () =>
            {
                if (!weakModel.TryGetTarget(out var live)) return; // :2290
                Transaction.WithAnimation(Chrome.PanelSlide, () => live.Archive(threadID)); // :2291
            },
        };
        if (thread.IsHelper) // :2293
        {
            items.Add(archive); // :2294
        }
        else
        {
            var isSettled = thread.IsSettled; // :2296
            var settle = new RowAction // :2297-2301
            {
                Title = isSettled ? "Reopen" : "Settle",
                Symbol = isSettled ? "arrow.uturn.backward" : "checkmark.circle",
                StartsGroup = settleFirst,
                Action = () =>
                {
                    if (!weakModel.TryGetTarget(out var live)) return; // :2303
                    live.AppModelFinishRequest = new AppModel.FinishRequest(threadID, isSettled); // :2304
                },
            };
            items.AddRange(settleFirst ? [settle, archive] : [archive, settle]); // :2306
        }
        items.Add(new RowAction // :2308-2311
        {
            Title = "Delete…",
            Symbol = "trash",
            IsDestructive = true,
            Action = () => { if (weakModel.TryGetTarget(out _)) requests.Delete = thread; }, // :2309-2310
        });
        return new ValueList<RowAction>(items);
    }
}

/// <summary>:2316-2343 <c>private struct ActivityViewToggle: View</c>.</summary>
internal sealed class ActivityViewToggle : Decorator
{
    /// <param name="isOn">:2317 <c>@Binding var isOn: Bool</c>.</param>
    // :2320-2342 body
    public ActivityViewToggle(Binding<bool> isOn)
    {
        var state = new ViewState();
        // :2329-2332 .background { Circle().fill(...) } behind the 30 by 30 frame; :2333 .contentShape(Circle()): the
        // circle takes the hits, the glyph's square corners do not.
        var circle = new Avalonia.Controls.Shapes.Ellipse { Width = 30, Height = 30 };
        var fill = new AnimatedColor(circle);
        circle.Fill = fill.Brush;
        var glyph = SwiftImage.SystemName("bell").Font(Chrome.IconFont); // :2325-2326
        glyph.Width = 30; // :2328 .frame(width: 30, height: 30)
        glyph.Height = 30;
        glyph.IsHitTestVisible = false;
        var label = new Panel { Width = 30, Height = 30, Children = { circle, glyph } };
        var button = PlainControls.Button(label, () => // :2322-2324, :2335 .buttonStyle(.plain)
            Transaction.WithAnimation(Chrome.PanelSlide, () => isOn.Value = !isOn.Value));
        button.OnHover(hovering => // :2336-2338
            Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering));
        AutomationProperties.SetName(button, "Activity view"); // :2340
        button.Track(() => (isOn.Value, state.IsHovering, ShortcutStore.Shared.Current.Chord(AppShortcut.ToggleActivityView)), value =>
        {
            var (on, hovering, chord) = value;
            glyph.ForegroundStyle(on ? Chrome.Accent : Chrome.SecondaryText); // :2327
            fill.Set(on ? Chrome.Accent.Opacity(0.16) : (hovering ? Chrome.Overlay(0.08) : SwiftColor.Clear), Transaction.Current); // :2331
            // :2321 DEVIATION OD-0008 B7: the chord's description is Windows notation (Ctrl+Alt+U), Shortcuts.cs.
            var hint = chord is { } bound ? " (" + bound.Description + ")" : "";
            ToolTip.SetTip(button, (on ? "Turn off activity view" : "Turn on activity view") + hint); // :2339
            // :2341 .accessibilityValue, as the port carries it (ChatBadge.cs): the automation item status.
            AutomationProperties.SetItemStatus(button, on ? "On" : "Off");
        });
        Child = button;
    }

    // :2318 @State private var isHovering = false
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }
}

/// <summary>:2345-2358 <c>private struct ActivityHeader: View</c>.</summary>
internal sealed class ActivityHeader : Decorator
{
    /// <param name="title">:2346 <c>let title: String</c>.</param>
    /// <param name="isFirst">:2347 <c>let isFirst: Bool</c>.</param>
    // :2349-2357 body
    public ActivityHeader(string title, bool isFirst) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2349");
}

/// <summary>:2363-2406 <c>private struct SettledHeader: View</c>.</summary>
internal sealed class SettledHeader : Decorator
{
    /// <param name="count">:2365 <c>let count: Int</c>.</param>
    /// <param name="isFirst">:2366 <c>let isFirst: Bool</c>.</param>
    // :2368-2405 body
    public SettledHeader(long count, bool isFirst) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2368");
}

/// <summary>:2408-2428 <c>private struct ActivityStatus: View</c>.</summary>
internal sealed class ActivityStatus : Decorator
{
    /// <param name="thread">:2410 <c>let thread: ChatThread</c>.</param>
    // :2409 @Environment(AppModel.self) private var model
    private AppModel Model => AppEnvironment.GetAppModel(this);

    // :2412-2427 body. PR-SIDEBAR-073.
    public ActivityStatus(ChatThread thread)
    {
        // As in ThreadBadge above: everything the apply lambda reads off the runtime is read inside the Track
        // selector (packed into the tuple), so a later change to any of it re-fires this Track, not just the first
        // mount (:2413-2421 reads runtime once per body evaluation in SwiftUI's own tracking).
        this.Track(() =>
        {
            var runtime = Model.ExistingRuntime(thread.Id); // :2413
            var needsInput = runtime is { Approvals.Count: > 0 } || runtime is { Questions.Count: > 0 }; // :2414
            var isRunning = runtime?.IsRunning == true || runtime?.IsHydraMerging == true || runtime?.HasWorkingHelpers == true; // :2418
            var mergeStage = SidebarThreadRow.MergeStage(runtime); // :2420
            return (NeedsInput: needsInput, IsRunning: isRunning, MergeStage: mergeStage);
        }, value =>
        {
            var (needsInput, isRunning, mergeStage) = value;
            Control? content;
            if (needsInput) // :2414
            {
                var hand = SwiftImage.SystemName("hand.raised.fill").Font(SwiftFont.Subheadline); // :2415-2416
                hand.ForegroundStyle(Chrome.Warning); // :2417
                content = hand;
            }
            else if (isRunning) // :2418
            {
                var spinner = new MiniSpinner(cellSize: 2.4); // :2419
                ToolTip.SetTip(spinner, mergeStage ?? ""); // :2420
                content = spinner;
            }
            else if (thread.HasUnread) // :2421
            {
                var dot = new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 7, // :2424
                    Height = 7,
                    Margin = new Thickness(0, 0, 4, 0), // :2425 .padding(.trailing, 4)
                };
                dot.Fill = (thread.LastStatus == TurnStatus.Failed ? Chrome.Danger : Chrome.Accent).Brush(dot); // :2423
                content = dot;
            }
            else
            {
                content = null; // :2426 nothing drawn
            }
            Child = content;
        });
    }
}

/// <summary>:2433-2440 <c>private final class FrameHolder</c>: a row's latest frame, kept out of observation.</summary>
internal sealed class FrameHolder
{
    // :2434 var frame: CGRect = .zero
    public Rect Frame { get; set; }

    // :2436-2439 func note(_ frame: CGRect)
    public void Note(Rect frame) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/SidebarView.swift:2436");
}

/// <summary>:2442-2452 <c>private final class RowHeights</c>: every row's measured height.</summary>
internal sealed class RowHeights
{
    // :2446 var values: [String: CGFloat] = [:]
    public ValueMap<string, double> Values { get; set; } = ValueMap<string, double>.Empty;

    // :2448-2451 func note(_ id: String, _ height: CGFloat): unchanged reports are skipped.
    public void Note(string id, double height)
    {
        if (Values.TryGetValue(id, out var noted) && noted == height) return; // :2449
        Values = Values.Mutating(values => values[id] = height); // :2450
    }
}

/// <summary>:2454-2472 <c>private final class RowFrames</c>: every open thread row's frame in the list.</summary>
internal sealed class RowFrames
{
    // :2457 private var frames: [UUID: CGRect] = [:]
    private readonly Dictionary<Guid, Rect> frames = [];

    // :2459-2462 func note(_ id: UUID, _ frame: CGRect)
    public void Note(Guid id, Rect frame)
    {
        if (frames.TryGetValue(id, out var noted) && noted == frame) return; // :2460
        frames[id] = frame; // :2461
    }

    // :2464-2466 func forget(_ id: UUID)
    public void Forget(Guid id) => frames.Remove(id); // :2465

    // :2468-2471 The thread whose row is under `point`, if an open one is: the first frame holding it in the map's own
    // order. UPSTREAM DEFECT 58, reproduced: two overlapping frames get no tie-break.
    public Guid? Thread(Point at)
    {
        foreach (var (id, frame) in frames) // :2470 frames.first { $0.value.contains(point) }?.key
            if (Contains(frame, at)) return id;
        return null;
    }

    // CGRect.contains(_:): a point on the minimum edges is inside, one on the maximum edges is not.
    private static bool Contains(Rect rect, Point point) =>
        point.X >= rect.X && point.X < rect.Right && point.Y >= rect.Y && point.Y < rect.Bottom;
}

/// <summary>:2475-2479 <c>private struct SidebarSearchResult</c>: one project's search hits.</summary>
internal sealed record SidebarSearchResult
{
    public required Project Project { get; init; } // :2476
    public required ValueList<ChatThread> Threads { get; init; } // :2477
    public required long Count { get; init; } // :2478
}

/// <summary>:2485-2536 <c>private final class PlacementCache</c>: the placements, kept while a fingerprint of the
/// threads stands.</summary>
internal sealed class PlacementCache
{
    /// <summary>:2486-2524 <c>private struct Fingerprint: Equatable</c>.</summary>
    private sealed record Fingerprint
    {
        public Guid Id { get; } // :2487
        public Guid ProjectID { get; } // :2488
        public Guid? ParentThreadID { get; } // :2489
        public bool IsArchived { get; } // :2490
        public bool IsInPanel { get; } // :2491
        public bool IsSettled { get; } // :2492
        public DateTimeOffset? SettledAt { get; } // :2493
        public bool IsPinned { get; } // :2494
        public double? SortOrder { get; } // :2495
        public double? ActivityOrder { get; } // :2496
        public DateTimeOffset? ActivityOrderDay { get; } // :2497
        public DateTimeOffset CreatedAt { get; } // :2498
        public DateTimeOffset UpdatedAt { get; } // :2499
        public bool FoldsHelpers { get; } // :2500
        public bool IsHydraHead { get; } // :2501
        public long? HydraIndex { get; } // :2502
        public bool HasUnread { get; } // :2503

        // :2505-2523 init(_ thread: ChatThread)
        public Fingerprint(ChatThread thread)
        {
            Id = thread.Id; // :2506
            ProjectID = thread.ProjectID;
            ParentThreadID = thread.ParentThreadID;
            IsArchived = thread.IsArchived;
            IsInPanel = thread.IsInPanel;
            IsSettled = thread.IsSettled;
            SettledAt = thread.SettledAt;
            IsPinned = thread.IsPinned;
            SortOrder = thread.SortOrder;
            ActivityOrder = thread.ActivityOrder;
            ActivityOrderDay = thread.ActivityOrderDay;
            CreatedAt = thread.CreatedAt;
            UpdatedAt = thread.UpdatedAt;
            FoldsHelpers = thread.FoldsHelpers;
            IsHydraHead = thread.IsHydraHead;
            HydraIndex = thread.Hydra?.Index;
            HasUnread = thread.HasUnread; // :2522
        }
    }

    // :2529-2535 func placements(for threads: [ChatThread]) -> [ThreadPlacement]
    public ValueList<ThreadPlacement> Placements(IReadOnlyList<ChatThread> @for)
    {
        var next = new ValueList<Fingerprint>(@for.Select(thread => new Fingerprint(thread))); // :2530
        if (next.Equals(fingerprint)) return cached; // :2531
        fingerprint = next; // :2532
        cached = new ValueList<ThreadPlacement>(@for.Select(thread => new ThreadPlacement(thread))); // :2533
        return cached; // :2534
    }

    private ValueList<Fingerprint>? fingerprint; // :2526
    private ValueList<ThreadPlacement> cached = ValueList<ThreadPlacement>.Empty; // :2527
}

/// <summary>:2543-2582 <c>private struct ThreadPlacement: Equatable</c>: the fields that decide where a thread sits.</summary>
internal sealed record ThreadPlacement
{
    public Guid Id { get; } // :2544
    public Guid ProjectID { get; } // :2545
    public Guid? ParentThreadID { get; } // :2546
    public bool IsArchived { get; } // :2547
    public bool IsInPanel { get; } // :2548
    public bool IsSettled { get; } // :2549
    public DateTimeOffset? SettledAt { get; } // :2550
    public bool IsPinned { get; } // :2551
    public double? SortOrder { get; } // :2552
    public double? ActivityOrder { get; } // :2554
    public DateTimeOffset? ActivityOrderDay { get; } // :2555
    public DateTimeOffset CreatedAt { get; } // :2556
    public DateTimeOffset UpdatedAt { get; } // :2557
    public bool FoldsHelpers { get; } // :2558
    public bool IsHydraHead { get; } // :2559
    public long? HydraIndex { get; } // :2560
    public bool HasUnread { get; } // :2561

    // :2563-2581 init(_ thread: ChatThread)
    public ThreadPlacement(ChatThread thread)
    {
        Id = thread.Id; // :2564
        ProjectID = thread.ProjectID;
        ParentThreadID = thread.ParentThreadID;
        IsArchived = thread.IsArchived;
        IsInPanel = thread.IsInPanel;
        IsSettled = thread.IsSettled;
        SettledAt = thread.SettledAt;
        IsPinned = thread.IsPinned;
        SortOrder = thread.SortOrder;
        ActivityOrder = thread.ActivityOrder;
        ActivityOrderDay = thread.ActivityOrderDay;
        CreatedAt = thread.CreatedAt;
        UpdatedAt = thread.UpdatedAt;
        FoldsHelpers = thread.FoldsHelpers;
        IsHydraHead = thread.IsHydraHead;
        HydraIndex = thread.Hydra?.Index;
        HasUnread = thread.HasUnread; // :2580
    }
}

/// <summary>:2589-2659 <c>private final class SidebarItemCache</c>: the built list, kept while its inputs stand.</summary>
internal sealed class SidebarItemCache
{
    /// <summary>:2590-2601 <c>struct Key: Equatable</c>.</summary>
    public sealed record Key
    {
        public required ValueList<ThreadPlacement> Placements { get; init; } // :2591
        public required ValueList<Project> Projects { get; init; } // :2592
        public required ValueSet<Guid> Attention { get; init; } // :2594
        public required bool ActivityView { get; init; } // :2595
        public required ActivityThreadStyle ActivityThreadStyle { get; init; } // :2596
        public required bool SettledCollapsed { get; init; } // :2597
        public required bool AutoHidesIdleHeads { get; init; } // :2599
        public required ValueSet<Guid> ParentsShowingHeads { get; init; } // :2600
    }

    /// <summary>:2603-2607 <c>struct SearchKey: Equatable</c>.</summary>
    public sealed record SearchKey
    {
        public required ValueList<ChatThread> Threads { get; init; } // :2604
        public required ValueList<Project> Projects { get; init; } // :2605
        public required string Query { get; init; } // :2606
    }

    // :2620-2625 func items(for wanted: Key, build: () -> [SidebarItem]) -> [SidebarItem]
    public ValueList<SidebarItem> Items(Key @for, Func<ValueList<SidebarItem>> build)
    {
        if (@for.Equals(key)) return built; // :2621
        built = build(); // :2622
        key = @for; // :2623
        return built; // :2624
    }

    private Key? key; // :2609
    private ValueList<SidebarItem> built = ValueList<SidebarItem>.Empty; // :2610
    private ValueList<ThreadPlacement>? helpersFor; // :2611
    private ValueMap<Guid, ValueList<ChatThread>> helpers = ValueMap<Guid, ValueList<ChatThread>>.Empty; // :2612
    private SearchKey? searchKey; // :2613
    private ValueList<SidebarSearchResult> found = ValueList<SidebarSearchResult>.Empty; // :2614
    private ValueList<ChatThread>? titlesFor; // :2617
    private ValueMap<Guid, string> titles = ValueMap<Guid, string>.Empty; // :2618

    // :2628-2633 func helpers(for placements: [ThreadPlacement], group: () -> [UUID: [ChatThread]]) -> [UUID: [ChatThread]]
    public ValueMap<Guid, ValueList<ChatThread>> Helpers(ValueList<ThreadPlacement> @for, Func<ValueMap<Guid, ValueList<ChatThread>>> group)
    {
        if (@for.Equals(helpersFor)) return helpers; // :2629
        helpers = group(); // :2630
        helpersFor = @for; // :2631
        return helpers; // :2632
    }

    // :2635-2640 func search(for wanted: SearchKey, find: ([UUID: String]) -> [SidebarSearchResult]) -> [SidebarSearchResult]
    public ValueList<SidebarSearchResult> Search(SearchKey @for, Func<ValueMap<Guid, string>, ValueList<SidebarSearchResult>> find)
    {
        if (@for.Equals(searchKey)) return found; // :2636
        found = find(LowercasedTitles(@for.Threads)); // :2637
        searchKey = @for; // :2638
        return found; // :2639
    }

    // :2642-2658 private func lowercasedTitles(for threads: [ChatThread]) -> [UUID: String]
    private ValueMap<Guid, string> LowercasedTitles(ValueList<ChatThread> @for)
    {
        if (@for.Equals(titlesFor)) return titles; // :2643
        var next = new Dictionary<Guid, string>(@for.Count); // :2644-2645
        // :2646-2647 Only a title that changed is lowercased again; the rest carry over. The previous titles by id,
        // the first of a repeated id kept (uniquingKeysWith: { first, _ in first }).
        var previous = new Dictionary<Guid, string>();
        if (titlesFor is { } before)
            foreach (var thread in before) previous.TryAdd(thread.Id, thread.Title);
        foreach (var thread in @for) // :2648
        {
            if (previous.TryGetValue(thread.Id, out var title) && SwiftString.Equal(title, thread.Title) && titles.TryGetValue(thread.Id, out var kept))
                next[thread.Id] = kept; // :2649-2650
            else
                next[thread.Id] = SwiftString.Lowercased(thread.Title); // :2651-2652
        }
        titles = new ValueMap<Guid, string>(next); // :2655
        titlesFor = @for; // :2656
        return titles; // :2657
    }
}

/// <summary>:2664-2682 <c>@MainActor @Observable final class MinuteClock</c> (R-OBS-6): the minute hand behind the
/// sidebar's relative times.</summary>
public sealed class MinuteClock : MainActorModel
{
    // :2667 static let shared = MinuteClock(). R-LIFE-2's side-effecting-static-with-a-test-seam form
    // (Ambient<T>) is not used here: the skeleton (c100 group 5) already committed this member's public shape as
    // `MinuteClock Shared`, not `Ambient<MinuteClock>`, and nothing in the tree yet depends on it being swappable;
    // a test that needs its own clock constructs one directly (the constructor is reached by reflection, as the
    // rest of this file's private types already are).
    private static readonly Lazy<MinuteClock> shared = new(() => new MinuteClock(), LazyThreadSafetyMode.ExecutionAndPublication);
    public static MinuteClock Shared => shared.Value;

    private DateTimeOffset now = SwiftDate.Now; // :2669 private(set) var now = Date.now

    // :2669 private(set) var now = Date.now
    public DateTimeOffset Now { get => Read(now); private set => Set(ref now, value); }

    // :2671 @ObservationIgnored private var tick: Task<Void, Never>?
    private readonly TaskHandle? tick;

    // :2673-2681 private init(). PR-SIDEBAR-075.
    private MinuteClock()
    {
        // ENGINEERING DECISION: the source captures [weak self] only so the loop stops if the singleton is ever
        // released, "which in practice it is not" (the record's own text); Shared holds this instance for the
        // process's life, so a weak wrapper here would never observe a difference (R-LIFE-1's deinit/weak-loop
        // guidance does not apply: nothing ever drops the last reference).
        tick = MainActor.Spawn(async () => // :2674 Task { [weak self] in ... }
        {
            while (!SwiftTask.IsCancelled) // :2675
            {
                if (!await SwiftTask.TrySleep(SwiftTask.Seconds(60))) return; // :2676 try? await Task.sleep(for: .seconds(60))
                Now = SwiftDate.Now; // :2678 self.now = .now
            }
        });
    }
}

/// <summary>:2686-2695 <c>private struct RelativeTimeLabel: View</c>.</summary>
internal sealed class RelativeTimeLabel : Decorator
{
    /// <param name="date">:2687 <c>let date: Date</c>.</param>
    // :2689-2694 body. PR-SIDEBAR-075.
    public RelativeTimeLabel(DateTimeOffset date)
    {
        this.Track(() => MinuteClock.Shared.Now, now => // :2690, the minute's tick re-renders this label alone
        {
            var text = new TextBlock { Text = RelativeTime.Short(date, now) }; // :2690
            SwiftFont.Subheadline.MonospacedDigit().Apply(text); // :2691
            text.Foreground = Chrome.SecondaryText.Opacity(0.8).Brush(text); // :2692
            text.Margin = new Thickness(0, 0, 4, 0); // :2693 .padding(.trailing, 4)
            Child = text;
        });
    }
}
