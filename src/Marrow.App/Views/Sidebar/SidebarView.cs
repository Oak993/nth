using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
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
using Marrow.App.Port.Updates;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port.Nth.Beads;
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
// Make and MakeContextMenu in full (:2193-2312); ThreadGhostBadge in full (:2083-2109; it was still MARROW_STUB, but
// Glide and ArchiveWithGenie need a real badge to carry, not a stub); SidebarHelperRow.TitleWeight, TitleOpacity,
// TrailingPadding, BackgroundFill, HeadGlyph and BuildTrailingOverlay (:1361-1384) as directly testable static pieces
// of that row's body. renamePopover (:239-252) and RenameThreadPopover were built later (2026-10-01); what STOP #6
// (SidebarViewTests.cs) still holds open is only the Windows chrome of `.textFieldStyle(.roundedBorder)`,
// `.buttonStyle(.bordered)`/`.borderedProminent` and presentedChrome's capsule border shape (Popovers.swift:10).
// Part 5 of 8 adds: HelperStubRow in full (:1552-1618), reusing HelperConnector.Dash (now internal) and
// HelperConnectorShape as :1583 reuses HelperConnector.style directly; HeadCardsRow.Usable, PerRow, RowGap, Chunk,
// Capped, Visible, CardRows and ChevronTooltip (:1450-1461, :1442-1443, :1526, :1545-1547) as directly testable
// static pieces (the constructor itself, its glyph buttons and its context menus stay MARROW_STUB: STOP #7 below);
// SidebarThreadRow.BuildFoldOverlay (:1804-1814) and .RowEquals (:1882-1891), and SidebarHelperRow.RowEquals
// (:1413-1420), each a directly testable static piece the row's own constructor (still MARROW_STUB) will call;
// ActivityHeader (:2345-2358) and SettledHeader (:2363-2406) in full.
// STOP App.Views.Sidebar.SidebarView #7 (narrowed part 6, narrowed again part 7; narrowed again Job SB (ROUND3.md
// items 2 and 3): full note in SidebarViewTests.cs's own header). HeadCardsRow's and ProjectRow's own constructors,
// named here at part 5, are built in full (parts 6 and 7) and are no longer part of this STOP. ProjectRow's and
// SidebarThreadRow's own live context menus (this file's ProjectRow and SidebarThreadRow constructors, each own
// ContextMenu.Opening wiring) are also no longer part of this STOP as of Job SB: the per-open-refresh foundation
// ENGINEERING DECISION is recorded at ProjectRow's own constructor (full citation there).
// CLOSED for SidebarHelperRow and HeadCardsRow (UI-SIDEBAR-033, UI-SIDEBAR-037): both now attach their own
// `.contextMenu` the same way (ContextMenu.Opening over a live Model read; see each constructor). The paragraph below
// is kept as the record of what the STOP asked.
// Kind: forbidden need
// Requirements (remaining, at that time): the right-click interaction named in UI-SIDEBAR-033 ("right-click opens the helper's
// context menu") and UI-SIDEBAR-037 ("right-click opens the head's context menu") - SidebarHelperRow's and
// HeadCardsRow's own `.contextMenu` sites only.
// What is unclear or missing: SidebarHelperRow's and HeadCardsRow's own constructors are each built in full (parts
// 6 and 7), but each still leaves its own `.contextMenu` unattached. ProjectRow's and SidebarThreadRow's own sites
// (Job SB) prove the foundation itself (Avalonia's ContextMenu.Opening) works; what remains for these two is
// determining each site's own live-read shape (SidebarHelperRow's and a head's own action list; Job SB's own
// ROUND3.md scoped only items 2 and 3, ProjectRow and the thread row, not these two), not a missing foundation.
// Needed to proceed: a follow-up pass wiring SidebarHelperRow's and HeadCardsRow's own ContextMenu the same way.
// Done so far: ProjectRow (PR-SIDEBAR-130, PR-SIDEBAR-133, UI-SIDEBAR-014/-015/-016) and SidebarThreadRow
// (PR-SIDEBAR-098 through PR-SIDEBAR-103's live-menu half) are proven; SidebarHelperRow and HeadCardsRow are not.
// Window sweep assembles SidebarThreadRow, ThreadRow, the three helper dispatch arms and FrameHolder.Note.
// Rename presentation and the SidebarHelperRow/HeadCardsRow right-click menus have since been written (STOP #6 now
// holds only the controls' chrome; STOP #7 is closed). Job SB (ROUND3.md) closed the empty-needle search match (STOP #1), ProjectRow's and
// SidebarThreadRow's own live context menus (STOP #7 narrowed, STOP #8 in SidebarViewTests.cs closed). Row state,
// frame reports and glides are now live.
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
// Window sweep also supplies the list's window frame, fold/rename intents and the row's private state and handlers.
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
        public RowDrag<Guid> Drag
        {
            get => Read(drag);
            set
            {
                var before = drag.Id;
                Set(ref drag, value);
                SyncDragCells(before);
                if (value.Id != before) SyncDragCells(value.Id);
            }
        }

        // ED-NEW-lean-15: per-id cells beside the drag, as AppModel.SelectionCell is beside the selection. A row reads only its own
        // lift (:758-761) and a helper, head card or folded stub only its parent's ride (:1622-1632), so a pointer move re-evaluates
        // the grabbed row and what rides it instead of every row in the list. Each cell holds exactly what its reader drew from the
        // drag (a row not grabbed: not dragged, no offset; a rider of a parent not grabbed: no offset, zIndex 0) and is written in the
        // same assignment as the drag, under the same transaction, so every change still moves along the change's own animation.
        private readonly Dictionary<Guid, ObservedValue<(bool IsDragged, double Offset)>> lifts = new();
        private readonly Dictionary<Guid, ObservedValue<(double Offset, int ZIndex)>> rides = new();

        public (bool IsDragged, double Offset) Lift(Guid id)
        {
            if (!lifts.TryGetValue(id, out var cell)) lifts[id] = cell = new ObservedValue<(bool IsDragged, double Offset)>(LiftOf(id));
            return cell.Value;
        }

        public (double Offset, int ZIndex) Ride(Guid parentID)
        {
            if (!rides.TryGetValue(parentID, out var cell)) rides[parentID] = cell = new ObservedValue<(double Offset, int ZIndex)>(RideOf(parentID));
            return cell.Value;
        }

        private (bool IsDragged, double Offset) LiftOf(Guid id) => drag.Id == id ? (true, drag.VisualOffset) : (false, 0);

        private (double Offset, int ZIndex) RideOf(Guid parentID)
        {
            var ride = new RidesWithDraggedParent(parentID, drag);
            return (ride.Offset, ride.ZIndex);
        }

        // Only the grab's old and new ids can change a cell: every other row reads "not dragged" before and after.
        private void SyncDragCells(Guid? id)
        {
            if (id is not { } changed) return;
            // ObservedValue tells its readers of every write (SetAlways); a cell is written only when its value changes, as the drag
            // itself tells its readers only of a change.
            if (lifts.TryGetValue(changed, out var lift) && LiftOf(changed) is var nextLift && !lift.Value.Equals(nextLift)) lift.Value = nextLift;
            if (rides.TryGetValue(changed, out var ride) && RideOf(changed) is var nextRide && !ride.Value.Equals(nextRide)) ride.Value = nextRide;
        }

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

    // :50, :140 The scroll viewport in the window's coordinate space; deliberately not observable.
    private readonly FrameHolder listFrame = new();

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
    private readonly Dictionary<ListKey, ListRow> listRows = new();
    private readonly ForEach<ListEntry, ListKey> layoutMotion;
    private readonly Dictionary<string, Control> scrollTargets = new(StringComparer.Ordinal);
    private ListMotion? lastMotion;
    private ListPass? lastPass; // ED-NEW-lean-15: the pass the list last applied; an equal pass changes nothing and is not applied again
    private int listPasses; // ED-NEW-lean-15: the list passes applied (ApplyList runs), read by SidebarLeanBudgetTests
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
        // :117-119 ED-218: the shared keyed layout glide also carries this caller's update-or-rebuild pass.
        layoutMotion = new ForEach<ListEntry, ListKey>(list, entry => entry.Key, entry => entry.Make(), (_, _) => { });
        scroll = new ScrollViewer
        {
            Template = ListScrollTemplate, // :139 .scrollIndicators(.never)
            Content = list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, // :102 .vertical
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Add(scroll, new GridLength(1, GridUnitType.Star));
        // PORT-ONLY (ED-NEW-session-hotkeys): the selection's glide, behind the list in the scroll's own cell.
        Glide = new SelectionGlide(list, enabled: !inPopover);
        Grid.SetRow(Glide, Grid.GetRow(scroll));
        column.Children.Insert(column.Children.IndexOf(scroll), Glide);
        scroll.LayoutUpdated += (_, _) =>
        {
            if (TopLevel.GetTopLevel(scroll) is { } window && scroll.TranslatePoint(default, window) is { } origin)
                listFrame.Note(new Rect(origin, scroll.Bounds.Size)); // :140, :1903-1905
        };

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

        // UE-5 (OD-0100 place D; no Swift twin): the update pill, in the column between the list and the Add project / Settings rows, which is "above
        // Settings". It takes no height while nothing is out of date and eases its height in and out (the list moves with it).
        var updatePill = new UpdatePill(nested: inPopover) { Margin = new Thickness(Chrome.ListInset, 0, Chrome.ListInset, 0) };
        Add(updatePill, GridLength.Auto);
        Add(new SkillsProgressPills(nested: inPopover) { Margin = new Thickness(14, 0, 14, 0) }, GridLength.Auto);

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
        // ED-NEW-lean-15: a re-evaluation that yields the pass already applied (a change the list does not show: a space in the
        // empty search field, a keystroke that keeps the same results) is not applied again. Tracked<T> applies every
        // re-evaluation (ViewSupport.cs), and an equal pass would only walk every row again to change nothing.
        list.Track(ListPassNow, pass =>
        {
            if (lastPass is not null && lastPass.Equals(pass)) return;
            lastPass = pass;
            ApplyList(pass);
        });
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

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): the selection's glide from row to row.</summary>
    internal SelectionGlide Glide { get; }

    /// <summary>
    /// PORT-ONLY (ED-NEW-session-hotkeys): this sidebar's session rows, top to bottom as the list draws them (threads and the
    /// helper rows under them, across project and activity groups; search results while searching), for Ctrl+1..9 and its
    /// hold-Ctrl numbers.
    /// </summary>
    private void PublishSessionOrder(ListPass pass)
    {
        var order = new List<Guid>();
        if (pass.Items is { } items)
        {
            foreach (var item in items)
            {
                if (item.SidebarItemKind is SidebarItem.Kind.Thread thread) order.Add(thread.Value.Id);
                else if (item.SidebarItemKind is SidebarItem.Kind.Helper helper) order.Add(helper.Value.Id);
            }
        }
        else if (pass.Results is { } results)
        {
            foreach (var result in results) order.AddRange(result.Threads.Select(thread => thread.Id));
        }
        try { Model.SessionNumbers.Publish(this, inPopover, IsOnScreen, order); }
        catch (InvalidOperationException) { } // no model in this tree
    }

    // On screen: in a window, visible, and inside no collapsed clip (the sidebar column at width 0).
    private bool IsOnScreen()
    {
        if (!IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null) return false;
        for (Visual? visual = this; visual is not null && visual is not TopLevel; visual = visual.GetVisualParent())
            if (visual.Bounds.Width < 1 || visual.Bounds.Height < 1) return false;
        return true;
    }

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
        try { Model.SessionNumbers.Withdraw(this); }
        catch (InvalidOperationException) { }
    }

    // :168-175 the Settings row's icon: the gear, with the update dot at its top-trailing corner while an update waits.
    private static Control SettingsIcon()
    {
        var badge = new SidebarIconBadge(() => new SidebarSymbol("gear", scale: 1.15)); // :169
        var overlay = new Panel { Children = { badge } };
        UpdateAvailableDot? dot = null;
        AnimatedScalar? shown = null;
        var wanted = false;
        var primed = false;
        // DEVIATION OD-0076 (leftovers pass 2026-10-04; the dot is a conditional view the Mac cuts in and out, :171-173
        // with no animation): the dot fades in and scales up from 0.8 over Chrome.hover and leaves the same way, the
        // clear button's motion (ED-280 (4)). UI-UPDATE-010's "disappears immediately" holds of the condition: the fade
        // starts in the frame the version catches up. The first read, at mount, shows it as it stands. The scale turns
        // about the circle's own centre, which :172's offset (2.5, -2.5) moves off the 7 pt box's centre (3.5, 3.5).
        overlay.Track(() => UpdateChecker.Shared.UpdateAvailable, available => // :171
        {
            wanted = available;
            var animation = primed ? Transaction.Current ?? Chrome.Hover : null;
            primed = true;
            if (available)
            {
                if (dot is null)
                {
                    var created = new UpdateAvailableDot // :172
                    {
                        HorizontalAlignment = HorizontalAlignment.Right, // :170 .overlay(alignment: .topTrailing)
                        VerticalAlignment = VerticalAlignment.Top,
                        RenderTransformOrigin = new RelativePoint(3.5 + 2.5, 3.5 - 2.5, RelativeUnit.Absolute),
                    };
                    dot = created;
                    overlay.Children.Add(created);
                    shown = new AnimatedScalar(created, animation is null ? 1 : 0, value =>
                    {
                        created.Opacity = value;
                        var scale = 0.8 + 0.2 * value;
                        created.RenderTransform = new ScaleTransform(scale, scale);
                        if (value != 0 || wanted) return;
                        overlay.Children.Remove(created); // faded out with nothing waiting
                        if (ReferenceEquals(dot, created)) { dot = null; shown = null; }
                    });
                }
                shown!.Set(1, animation);
            }
            else if (dot is not null)
            {
                shown!.Set(0, animation);
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
        // change's own transaction applies. ED-218 carries both the fades and surviving rows' positional motion.
        listPasses++;
        var animation = lastMotion is not null && lastMotion != pass.Motion ? Chrome.PanelSlide : Transaction.Current;
        lastMotion = pass.Motion;
        Transaction.WithAnimation(animation, () => ApplyEntries(pass.Items is { } items
            ? ItemEntries(items)
            : SearchEntries(pass.Results!, pass.Helpers)));
        PublishSessionOrder(pass);
    }

    // A row of the list as its ForEach keeps it: the value it was built from and its control (R-VIEW-8).
    private sealed class ListRow(object value, Control control)
    {
        public object Value { get; set; } = value;
        public Control Control { get; set; } = control;
    }

    // One entry a pass wants: its ForEach id, the value that builds it, the id ScrollViewReader finds it by, the
    // control, and how a surviving row takes a new value in place (false: it is rebuilt where it stands).
    private sealed record ListEntry(ListKey Key, object Value, string? Target, Func<Control> Make, Func<Control, object, bool>? Update);

    // ED-NEW-lean-15: one key space for the list and the search results, a value key with no string built per row per pass. The
    // list's rows and the search's rows stay apart (Space), as the two branches of :104-113 are apart in SwiftUI, so entering or
    // leaving a search still fades the one set out and the other in; each key maps one to one onto the string id it replaces
    // ("item:<id>", "search:empty", "search:<project>:gap", "search:<project>:project", "search:<project>:<sidebar item id>").
    private enum KeySpace : byte { Item, SearchEmpty, SearchGap, SearchProject, SearchThread }

    private readonly record struct ListKey(KeySpace Space, Guid Group, string? Id);

    // :107-110 ForEach(listItems(...)) { item in itemView(item).transition(rowTransition(item)) }
    private List<ListEntry> ItemEntries(ValueList<SidebarItem> items) =>
        items.Select(item => new ListEntry(
            new ListKey(KeySpace.Item, default, item.Id),
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
            entries.Add(new ListEntry(new ListKey(KeySpace.SearchEmpty, default, null), "empty", null, NoResults, null)); // :694-703
            return entries;
        }
        for (var index = 0; index < results.Count; index++) // :705 ForEach(..., id: \.element.project.id)
        {
            var result = results[index];
            var group = result.Project.Id;
            if (index > 0) // :706-708
                entries.Add(new ListEntry(new ListKey(KeySpace.SearchGap, group, null), Chrome.GroupGap, null, () => new Border { Height = Chrome.GroupGap }, null));
            entries.Add(new ListEntry(new ListKey(KeySpace.SearchProject, group, null), (result.Project, result.Count), null, // :709
                () => new ProjectRow(result.Project, result.Count, togglesExpansion: false),
                (control, value) => // :709 the same row under the same ForEach id takes its new snapshot and count in place
                {
                    if (control is not ProjectRow projectRow || value is not ValueTuple<Project, long> pair) return false;
                    projectRow.Update(pair.Item1, pair.Item2);
                    return true;
                }));
            foreach (var thread in result.Threads) // :711 ForEach(result.threads, id: \.sidebarItemID)
            {
                var hasHelpers = !thread.IsSettled && helpers.TryGetValue(thread.Id, out var own) && own.Count > 0; // :712
                var itemID = thread.SidebarItemID;
                entries.Add(new ListEntry(new ListKey(KeySpace.SearchThread, group, itemID), (thread, hasHelpers), itemID,
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
    private void ApplyEntries(List<ListEntry> entries) => layoutMotion.ApplyLayout(
        () => listRows.ToDictionary(pair => pair.Key, pair => pair.Value.Control),
        () => ReconcileEntries(entries));

    private void ReconcileEntries(List<ListEntry> entries)
    {
        var wanted = new List<ListEntry>(entries.Count);
        var seen = new HashSet<ListKey>();
        foreach (var entry in entries)
            if (seen.Add(entry.Key)) wanted.Add(entry); // a duplicate id keeps its first entry (UNVERIFIED O-18)
        foreach (var gone in listRows.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            SwiftTransition.Remove(list, listRows[gone].Control);
            listRows.Remove(gone);
        }
        scrollTargets.Clear();
        var children = list.Children;
        // ED-NEW-lean-15: the children are indexed once. The slot after the previous entry's control is carried from one entry to
        // the next (it is where that control was just placed) instead of searched for, and a control is searched for only when it
        // is not in the slot expected of it, so a pass that moves nothing reads each child once.
        int IndexOf(Control control, int expected) =>
            expected >= 0 && expected < children.Count && ReferenceEquals(children[expected], control) ? expected : children.IndexOf(control);
        var target = 0; // the slot after the previous entry's control
        foreach (var entry in wanted)
        {
            Control control;
            var placed = target;
            if (listRows.TryGetValue(entry.Key, out var row))
            {
                if (!Equals(row.Value, entry.Value))
                {
                    if (entry.Update is { } update && update(row.Control, entry.Value)) row.Value = entry.Value;
                    else
                    {
                        var fresh = entry.Make();
                        children[IndexOf(row.Control, target)] = fresh;
                        row.Control = fresh;
                        row.Value = entry.Value;
                    }
                }
                control = row.Control;
                var current = IndexOf(control, target);
                if (current != target)
                {
                    placed = current < target ? target - 1 : target;
                    children.Move(current, placed);
                }
            }
            else
            {
                control = entry.Make();
                listRows[entry.Key] = new ListRow(entry.Value, control);
                SwiftTransition.Insert(list, target, control);
            }
            if (entry.Target is { } id) scrollTargets[id] = control;
            target = IndexOf(control, placed) + 1;
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

    // A surviving row takes its item's new value in place where its view can; the others are rebuilt where they stand
    // (ENGINEERING DECISION ED-258 (3): SwiftUI keeps a row's @State under its ForEach id; the thread
    // and stub rows and the activity headers still rebuild, UNRESOLVED there).
    private static bool UpdateItemView(Control control, SidebarItem item)
    {
        if (item.SidebarItemKind is SidebarItem.Kind.SettledHeader header && control is SettledHeader settled)
        {
            settled.Update(header.Count, header.IsFirst); // :337-338, :2364-2366: one view whose count and isFirst change under it
            return true;
        }
        if (item.SidebarItemKind is SidebarItem.Kind.Project project && control is ProjectRow projectRow)
        {
            projectRow.Update(project.Value, project.Count); // :333-334, :1081-1089: one view whose snapshot and count change under it
            return true;
        }
        if (item.SidebarItemKind is SidebarItem.Kind.HeadCards cards && control is Decorator { Child: HeadCardsRow card })
        {
            card.Update(cards.Heads); // :368-371, :1430: one view whose headSnapshots change under it (the card rides its parent inside the Decorator)
            return true;
        }
        if (item.SidebarItemKind is SidebarItem.Kind.Helper helper && HelperRowIn(control) is { } helperRow)
        {
            helperRow.Update(helper.Value, helper.IsLast); // :347-367, :1320, :1326: one view whose snapshot and isLast change under it
            return true;
        }
        if (item.SidebarItemKind is not SidebarItem.Kind.Gap gap || control is not Border border) return false;
        border.Height = gap.Value; // :332
        return true;
    }

    // The helper row inside its item's view (HelperItemView): the riding host, the archive popover's panel, then the
    // delete popover's panel, whose first child is the row.
    private static SidebarHelperRow? HelperRowIn(Control control) =>
        control is Decorator { Child: Panel archive } && archive.Children.Count > 0 && archive.Children[0] is Panel delete
        && delete.Children.Count > 0 && delete.Children[0] is SidebarHelperRow row ? row : null;

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
        ReportsHeightAndRides(ArchivePopover(helper.Value, DeletePopover(helper.Value,
            new SidebarHelperRow(helper.Value, helper.IsLast, menuRequests,
                () => Fold(helper.Value.ParentThreadID), () => RequestRename(helper.Value), () => RequestDeletion(helper.Value.Id)))),
            item.Id, helper.Value.ParentThreadID);

    // :368-371 the head-card arm, which rides a dragged parent (parts 3 and 5).
    private Control HeadCardsItemView(SidebarItem item, SidebarItem.Kind.HeadCards cards) =>
        ReportsHeightAndRides(new HeadCardsRow(cards.Parent, cards.Heads, menuRequests, () => Fold(cards.Parent.Id)), item.Id, cards.Parent.Id);

    // :372-375 the folded-helpers arm, which rides a dragged parent (parts 3 and 5).
    private Control HelperStubItemView(SidebarItem item, SidebarItem.Kind.HelperStub stub) =>
        ReportsHeightAndRides(new HelperStubRow(stub.Parent, stub.Helpers, () => Fold(stub.Parent.Id)), item.Id, stub.Parent.Id);

    // :347-375 Height reports stay in the non-observable table; every dependent rides the same live parent drag.
    // The riders' .offset (:1629) reads the same `drag` the row's own .offset (:760) does, so a change made inside
    // withAnimation (dragEnded's spring, :805-809; a swap's slide, :767) moves them along that animation together with
    // the row, and at once when there is none, exactly as Lifted moves the row (PR-SIDEBAR-055, PR-SIDEBAR-056).
    private Control ReportsHeightAndRides(Control content, string itemID, Guid? parentID)
    {
        var translate = new TranslateTransform();
        var host = new Decorator { Child = content, RenderTransform = translate };
        host.SizeChanged += (_, e) => rowHeights.Note(itemID, e.NewSize.Height);
        double target = 0;
        IFrameSource? frames = null;
        Action<TimeSpan>? pending = null;
        // ED-NEW-lean-15: the parent's own ride cell, not the whole drag (a rider of no parent never rides: no offset, zIndex 0).
        host.Track(() => parentID is { } parent ? state.Ride(parent) : (Offset: 0d, ZIndex: 0), rides =>
        {
            host.ZIndex = rides.ZIndex; // :1630
            var to = rides.Offset; // :1629
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

    // :487-490 A helper connector or the parent's chevron toggles the fold on the panel spring.
    private void Fold(Guid? parentID)
    {
        if (parentID is { } id) Transaction.WithAnimation(Chrome.PanelSlide, () => Model.ToggleHelpersFold(id));
    }

    // :354-358, :882-887 Rename takes the live title, unlike the posted context-menu snapshot.
    private void RequestRename(ChatThread snapshot)
    {
        var live = LiveRenameTarget(Model, snapshot);
        state.RenameText = live.Title;
        state.Renaming = live;
    }

    // :870-897 threadRow(_:projectName:projectIcon:hasHelpers:isDragged:), the popovers around SidebarThreadRow (parts 3
    // and 4).
    // CLOSED STOP opencode-App.Views.Sidebar.SidebarView #5: SidebarThreadRow's own constructor and this ThreadRow
    // wiring are assembled (Window sweep) and driven by the mounted row tests (PR-SIDEBAR-134, UI-SIDEBAR-017,
    // UI-SIDEBAR-060); PR-SIDEBAR-135's capture comparison stays open under STOP #9 (SidebarViewTests.cs).
    private Control ThreadRow(ChatThread thread, string? projectName, ProjectIcon? projectIcon, bool hasHelpers, bool isDragged = false) =>
        // :874 archivePopover(for:on: renamePopover(for:on: deletePopover(for:on: SidebarThreadRow(...))))
        ArchivePopover(thread, RenamePopover(thread, DeletePopover(thread, new SidebarThreadRow(thread, projectName, projectIcon, menuRequests,
            () => RequestRename(thread), () => RequestDeletion(thread.Id), isDragged,
            inPopover ? null : listFrame, hasHelpers ? () => Fold(thread.Id) : null))));

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
        // ED-NEW-lean-15: this row's own lift cell (whether it is the grabbed one and, if so, the drag's visual offset), not the drag.
        host.Track(() => state.Lift(id), value =>
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
    // Swift's String.contains at its other lowercased sites (AppModel+HydraMerge.cs, MCPProxy.cs, ZaiAPI.cs).
    // ENGINEERING DECISION (closes STOP #1, PR-SIDEBAR-005's empty-query half): an empty needle answers true, for
    // every text including an empty one. `project.name.lowercased()`/`.title.lowercased()` are plain Swift String
    // values (not NSString), so the applicable `.contains(_:)` is the standard library's (SE-0357 "Regex-powered
    // String Processing Algorithms", available since SwiftStdlib 5.7 - unchanged through the pinned Swift 6.0
    // toolchain, so not version-dependent here), defined as `firstRange(of: other) != nil`
    // (swift-experimental-string-processing's StringProtocol+Algorithms.swift). SE-0357 states this "follows the
    // consensus behaviour" of finding an empty needle at every index - its own worked example is
    // `"Hello".firstRange(of: "")` == `0..<0` (found, not nil) - and Foundation's older Objective-C-bridged
    // `NSString.contains(_:)`, which alone answers false for an empty argument, requires an explicit NSString
    // receiver that this call site never produces. Residual caveat: no macOS run of the pinned toolchain confirmed
    // this by direct observation (no such oracle is available in this environment); the citations above are the
    // evidence this decision rests on.
    private static bool Contains(string text, string needle)
    {
        if (needle.Length == 0) return true;
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

    /// <summary>:239-252 renamePopover(for:on:): the rename field in a popover on the row itself. PR-SIDEBAR-091.</summary>
    // STOP #6 closed 2026-10-01 (see RenameThreadPopover). PR-SIDEBAR-091, UI-SIDEBAR-028, UI-SIDEBAR-029, UI-SIDEBAR-030.
    private Control RenamePopover(ChatThread thread, Control row)
    {
        var threadID = thread.Id; // :240
        var isPresented = new Binding<bool>(
            () => state.Renaming?.Id == threadID, // :243
            value => { if (!value) state.Renaming = null; }); // :244 clicking away keeps the thread's title
        var popup = PlainControls.Popover(row, isPresented, () => BuildRenamePopoverContent(threadID), PlacementMode.Bottom); // :246 arrowEdge: .bottom
        return new Panel { Children = { row, popup } };
    }

    /// <summary>:248 the rename popover's content, extracted so it is directly testable (as BuildDeletePopoverContent).</summary>
    private Control BuildRenamePopoverContent(Guid threadID) =>
        new RenameThreadPopover(state.RenameText, title => { Model.Rename(threadID, title); state.Renaming = null; }, () => state.Renaming = null) // :248
            .PresentedChrome(); // :249 .presentedChrome()

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
            return BuildArchivePopoverContent(thread, row);
        }, PlacementMode.Right); // :270 arrowEdge: .trailing
        popup.Closed += (_, _) =>
        {
            state.IsDeletePopoverShown = false; // :287
            CarryOutConfirmedDeletion(); // :288
        };
        return new Panel { Children = { row, popup } };
    }

    /// <summary>:275-284 the archive popover's content, extracted so it is directly testable (see
    /// BuildDeletePopoverContent above for why). <paramref name="anchor"/> is the row (ArchivePopover's own
    /// <c>row</c>), passed through so ArchiveThreadPopover's window-scoped key handler resolves the ThreadWindow
    /// behind the popup rather than the popup's own PopupRoot (REVIEW FIX, part 4: see ArchiveThreadPopover's
    /// constructor comment). PR-SIDEBAR-094, PR-SIDEBAR-095, PR-SIDEBAR-096, PR-SIDEBAR-097.</summary>
    private ArchiveThreadPopover BuildArchivePopoverContent(ChatThread thread, Control anchor)
    {
        var threadID = thread.Id;
        return new ArchiveThreadPopover(
            Model.Thread(threadID) ?? thread,
            anchor,
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
    // same substitution AttachmentPreviewPanel.cs:160-195 already makes for a local NSEvent monitor). REVIEW FIX
    // (part 4): the window must be resolved from the anchor row, not from this popover's own content -- once this
    // is actually hosted through a real Popup (PlainControls.Popover), its content is a separate top-level
    // (PopupRoot), exactly as AttachmentPreviewPanel.cs:162-172's own comment states for the identical shape, so
    // TopLevel.GetTopLevel(this) would resolve the popup's own window rather than the ThreadWindow the doc comment
    // above names ("even when the keys land on the chat window behind it").
    private Avalonia.Controls.Window? monitorWindow;
    private Button? archiveButton;
    private readonly Control anchor;
    private readonly Action onArchive;
    private readonly Action onDismiss;

    /// <param name="thread">:906 <c>let thread: ChatThread</c>.</param>
    /// <param name="anchor">The row this popover is anchored to (ArchivePopover's <c>row</c>): resolving the
    /// window-scoped handler's window from here, not from this popover's own content, matches
    /// AttachmentPreviewPanel.cs:168-172's StartMonitors(resolvedAnchor).</param>
    /// <param name="onArchive">:907 <c>let onArchive: () -> Void</c>.</param>
    /// <param name="onDelete">:908 <c>let onDelete: () -> Void</c>.</param>
    /// <param name="onDismiss">:909 <c>let onDismiss: () -> Void</c>.</param>
    // :914-962 body
    public ArchiveThreadPopover(ChatThread thread, Control anchor, Action onArchive, Action onDelete, Action onDismiss)
    {
        this.anchor = anchor;
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
        var deleteLabel = new TextBlock { Text = "Delete", Foreground = SwiftColor.Red.Brush(this) }; // :927 .foregroundStyle(.red): system red, not the theme's Chrome.danger
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
        // :938-940 padding; :943 .frame(width: 272). The Swift PopoverMenu's content takes the 272 pt proposed to it (its
        // frame(minWidth: 210, maxWidth: 360), Popovers.swift:108) inside its own 6 pt padding (:107), so the button row's
        // Spacer pushes Delete and Archive to the trailing edge. PopoverMenu (Marrow.UI) sizes its content to fit, so the
        // content is given that width here: 272 less 6 on each side (UI-SIDEBAR-031, UI-SIDEBAR-032 "right-aligned").
        var padded = new Border { Padding = new Thickness(8, 6, 8, 4), Child = column, Width = 272 - 2 * 6 };
        var menu = new PopoverMenu(() => padded);
        menu.Width = 272; // :943 .frame(width: 272)
        Child = menu;

        AttachedToVisualTree += (_, _) => Appear(); // :944 .onAppear
        DetachedFromVisualTree += (_, _) => Disappear(); // :958 .onDisappear
    }

    // :926-929, :931-936 the untinted and accent-tinted twins of FollowUpEditor.cs's glass buttons, sized to their
    // content: OD-0005 governs the material, not this button's height.
    // :923 `.controlSize(.small)`: the small glass text button's measured metrics (SwiftGlass.GlassButtonSmall*, ED-280 (2):
    // 20 high, an 11 pt label, 10.5 pt either side); the same for the prominent style (ENGINEERING DECISION, OD-0098).
    private static Border SmallGlassFrame(Control label)
    {
        if (label is TextBlock text) SwiftFont.System(SwiftGlass.GlassButtonSmallFontSize).Apply(text);
        var framed = new Border { Height = SwiftGlass.GlassButtonSmallHeight, Padding = new Thickness(SwiftGlass.GlassButtonSmallPadding, 0), Child = label };
        framed.Capsule(RoundedCornerStyle.Continuous);
        return framed;
    }

    private static Button GlassButton(Control label, Action action)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        var framed = SmallGlassFrame(label);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
        return PlainControls.Button(framed, action);
    }

    private static Button GlassProminentButton(Control label, Action action)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        var framed = SmallGlassFrame(label);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.Accent, 0.3).Interactive());
        framed.Background = Chrome.Accent.Opacity(0.3).Brush(framed);
        return PlainControls.Button(framed, action);
    }

    // :944-956 onAppear: focus the Archive button, then install the window-scoped key handler. REVIEW FIX (part 4):
    // resolved from the anchor row (see the constructor's comment), not from `this`.
    private void Appear()
    {
        archiveButton?.Focus(); // :945 isFocused = true
        // The content is attached before its window has laid it out, and a focus request made then is dropped; the same
        // request one turn later (once it is laid out and focusable) takes, so Archive holds the focus from the popover's
        // appearance, as :945 has it (UI-SIDEBAR-031, UI-SIDEBAR-032).
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (archiveButton is { IsEffectivelyVisible: true } button && !button.IsFocused) button.Focus(); }, Avalonia.Threading.DispatcherPriority.Loaded);
        if (TopLevel.GetTopLevel(anchor) is not Avalonia.Controls.Window window) return;
        monitorWindow = window;
        window.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel); // :946-955; a key PopoverHost already forwarded (Return pressing the default Archive, Escape dismissing) is handled and acts once (PopoverHost.Listen)
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

/// <summary>The Remove project question: port-only by owner rulings OD-0008 A9 and OD-0012 E2 (PR-SIDEBAR-130,
/// PR-SIDEBAR-131), in the shape of the thread delete question above (:966-981): a 280 pt popover menu with the
/// section header, the note naming the project in U+201C/U+201D, a divider and one destructive trash item.</summary>
internal sealed class RemoveProjectPopover : Decorator
{
    public RemoveProjectPopover(Project project, Action onRemove)
    {
        var menu = new PopoverMenu(() =>
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            stack.Children.Add(new PopoverSectionHeader("Remove this project?")); // OD-0012 E2
            stack.Children.Add(new PopoverNote(
                "“" + project.Name + "” and its threads will be removed. Files in your project stay as they are.")); // OD-0012 E2
            stack.Children.Add(new PopoverDivider()); // as :975
            stack.Children.Add(new PopoverItem("Remove project", onRemove, symbol: "trash", isDestructive: true)); // OD-0012 E2, as :976
            return stack;
        });
        menu.Width = 280; // as :980 .frame(width: 280)
        Child = menu;
    }
}

/// <summary>:987-1025 <c>private struct RenameThreadPopover: View</c>: what the rename popover asks, the title field and
/// the choice. PR-SIDEBAR-091.</summary>
internal sealed class RenameThreadPopover : Decorator
{
    /// <param name="title">:988 <c>let title: String</c>.</param>
    /// <param name="onRename">:989 <c>let onRename: (String) -> Void</c>.</param>
    /// <param name="onCancel">:990 <c>let onCancel: () -> Void</c>.</param>
    // :995-1000 init; :1002-1024 body. Formerly STOP #6 (no Windows chrome for .roundedBorder / .bordered /
    // .borderedProminent); closed 2026-10-01: the field is the RoundedBorder editor ChatView's and QuestionTab's
    // .roundedBorder fields use, the buttons SystemButtons.Bordered / BorderedProminent, both drawn as the macOS controls
    // from Apple's macOS 27 UI Kit (OD-0042, OD-0045).
    public RenameThreadPopover(string title, Action<string> onRename, Action onCancel)
    {
        // :992-993 @State private var text: String (initialValue: title); @FocusState private var isFocused
        var heading = new TextBlock { Text = "Rename thread" }; // :1004
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(heading); // :1005 .font(.system(.body, weight: .semibold))
        heading.Foreground = Chrome.PrimaryText.Brush(heading);
        Field = new TextBox { Text = title, PlaceholderText = "Title", AcceptsReturn = false, HorizontalAlignment = HorizontalAlignment.Stretch }; // :1006 TextField("Title", text: $text)
        Marrow.App.Port.ChatDialogControls.TextEditor(Field, style: "RoundedBorder"); // :1007 .textFieldStyle(.roundedBorder)
        // :1009 .onSubmit { onRename(text) }: Return submits whatever is typed (the button's empty-title guard is its own).
        Field.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            e.Handled = true;
            onRename(Field.Text ?? "");
        };
        var cancel = SystemButtons.Bordered("Cancel", onCancel); // :1012-1013 Button("Cancel", action: onCancel).buttonStyle(.bordered)
        var rename = SystemButtons.BorderedProminent("Rename", () => onRename(Field.Text ?? "")); // :1014-1015
        rename.IsDefault = true; // :1016 .keyboardShortcut(.defaultAction)
        void Guard() => rename.IsEnabled = FoundationText.Trim(Field.Text ?? "").Length != 0; // :1017 .disabled(text.trimmingCharacters(in: .whitespaces).isEmpty)
        Field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Guard(); };
        Guard();
        // :1010-1019 HStack { Spacer(); Cancel; Rename }; ENGINEERING DECISION (UNVERIFIED metric): the HStack's default
        // spacing as 8 points, the gap the port uses for SwiftUI's default stack spacing elsewhere in this row family.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, rename } };
        // :1003 VStack(alignment: .leading, spacing: 10); :1021-1022 .padding(14).frame(width: 300)
        Child = new Border { Width = 300, Padding = new Thickness(14), Child = new StackPanel { Spacing = 10, Children = { heading, Field, buttons } } };
        // :1023 .onAppear { isFocused = true }. UNVERIFIED (AppKit behaviour, not in the source): an NSTextField that
        // becomes first responder has its field editor select the whole text, so the title is selected here too.
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { Field.Focus(); Field.SelectAll(); });
    }

    /// <summary>:1006 the title field (exposed for the popover's tests).</summary>
    internal TextBox Field { get; }
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

/// <summary>:1078-1230 <c>private struct ProjectRow: View</c>. Part 6, then Job SB (ROUND3.md item 2). The row's own
/// body (PR-SIDEBAR-126 the click-to-expand and its togglesExpansion guard, PR-SIDEBAR-127 the icon and the
/// accessory's three branches, PR-SIDEBAR-128 opacity by activation), the context menu's own ITEMS (PR-SIDEBAR-129)
/// and, as of Job SB, the live AppKit-style context menu itself (PR-SIDEBAR-130, PR-SIDEBAR-133, UI-SIDEBAR-014,
/// UI-SIDEBAR-015, UI-SIDEBAR-016) opened by a real right-click are now provable directly.
// CLOSED STOP opencode-App.Views.Sidebar.SidebarView #4, for ProjectRow only (Job SB, ROUND3.md item 2): the
// per-open-refresh foundation ENGINEERING DECISION and the attachment itself are at this constructor's own
// ContextMenu.Opening wiring, below (full citation there). SidebarHelperRow's and HeadCardsRow's own .contextMenu
// sites, and the right-click interaction of UI-SIDEBAR-033/UI-SIDEBAR-037, are unchanged by this and stay under
// STOP #7 (SidebarViewTests.cs).
/// (porting/engineering_decisions.md and porting/platform_adaptations.md had no ".contextMenu" or "ContextMenu"
/// entry before this pass; RowActionMenuButtons' own header comment already anticipated "a caller" installing its
/// Items on a live ContextMenu.Items - this constructor is that caller, for ProjectRow). See STOP #7
/// (SidebarViewTests.cs, narrowed further by this closure) for the still-open helper/head-card sites.</summary>
internal sealed class ProjectRow : Decorator
{
    // :1091 @State private var isMenuPresented = false
    private sealed class ViewState : MainActorModel
    {
        private bool isMenuPresented;
        private bool isRemovalPresented;

        public bool IsMenuPresented { get => Read(isMenuPresented); set => Set(ref isMenuPresented, value); }

        // Port-only state, owner rulings OD-0008 A9 and OD-0012 E2 (PR-SIDEBAR-130, PR-SIDEBAR-131): upstream removes a
        // project at once from both menus (:1183-1186, :1212-1214); the port asks first while confirmBeforeDeleting is
        // on, in the thread delete question's own shape (deletePopover, :217-237, and DeleteThreadPopover, :966-981).
        public bool IsRemovalPresented { get => Read(isRemovalPresented); set => Set(ref isRemovalPresented, value); }

        // The confirmed removal waits for the question's popup to be gone, as a confirmed thread deletion does
        // (confirmDeletion/carryOutConfirmedDeletion, :256-306): removing the project removes this row, popup included.
        public bool RemovalConfirmed;
        public bool IsRemovalPopoverShown;
    }

    private readonly ViewState state = new();

    private Action<Project, long>? update;

    /// <summary>One row whose inputs change under it: the list hands a surviving project row its new snapshot and count
    /// (:1081, :1088), and the row keeps its own state.</summary>
    internal void Update(Project snapshot, long count) => update?.Invoke(snapshot, count);

    /// <param name="snapshot">:1081 <c>let snapshot: Project</c>.</param>
    /// <param name="count">:1088 <c>let count: Int</c>.</param>
    /// <param name="togglesExpansion">:1089 <c>var togglesExpansion = true</c>.</param>
    // :1093-1163 body. `snapshot`, `count` and `togglesExpansion` are stored, non-reactive inputs in Swift too (no
    // @State on any of them): the caller (ProjectItems, part 2) already rebuilds every row fresh whenever the thread
    // count, a project's own expansion or the settled fold changes (ListMotion, :117-119), so this constructor reads
    // them once. `project` (:1085, including its own :1108 `.icon`) and `isActive` (:1096) are computed properties
    // Swift's body re-reads on every render; SidebarRow (UI/Chrome/Chrome.cs) has no external hook to re-invoke its
    // own `icon`/`accessory` closures other than its own internal hover state, so this Tracks the computed values
    // directly onto the row it already built, the way ActivityViewToggle and SettledHeader mutate an already-built
    // control's properties rather than rebuilding it (surgical update, not a fresh SidebarRow, so its own internal
    // hover state survives): one Track for `current`/`isActive` (also re-populating the icon glyph, since Swift
    // recomputes it from the same `project` read) and a second, below, for `isMenuPresented` (:1091), a @State Swift
    // re-reads on every write regardless of hover, which SidebarRow's own hover Track alone cannot reach (REVIEW
    // FIX, part 6: both Tracks were reviewer findings against the initial part 6 implementation).
    public ProjectRow(Project snapshot, long count, bool togglesExpansion = true)
    {
        var current = snapshot; // :1085, refreshed by the Track below
        var shownIcon = snapshot.Icon; // the icon the badge draws now (:1108)
        AnimatedScalar? dim = null; // :1136's opacity, eased (OD-0076)
        var dimPrimed = false;
        var shownCount = count; // :1088, refreshed by Update
        var isMenuPresented = new Binding<bool>(() => state.IsMenuPresented, value => state.IsMenuPresented = value);
        SidebarIconBadge? iconBadge = null;
        Control BuildIconGlyph() => // :1108-1112, re-read fresh wherever this is invoked, matching Swift's computed `project.icon`
            current.Icon is { } icon ? new ProjectIconMark(icon, 12) : new SidebarSymbol("folder.fill");
        // :1115-1133 the accessory is one conditional: the ellipsis-and-plus HStack, else the count Text, else nothing.
        // SwiftUI keeps the view of the branch that holds under its identity, so the HStack keeps its ellipsis button (and
        // an open menu its anchor) however often the row renders, and the Text takes a new count in place; a branch that is
        // entered again is a new view. SidebarRow (Chrome.cs) keeps a control it is handed again where it is and
        // crossfades a new one in for an old one.
        StackPanel? cluster = null;
        TextBlock? countText = null;
        Border? countHost = null;
        Border? none = null;
        Control BuildAccessory(bool hovering) // :1115-1133, asked by SidebarRow on a hover change and by RefreshAccessory below
        {
            if (hovering || state.IsMenuPresented) // :1116
            {
                countHost = null;
                none = null;
                if (cluster is not null) return cluster;
                cluster = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 }; // :1117
                // :1118 RowActionsButton(actions: actions(isActive: isActive), ...): the actions the body builds now, with
                // :1096's isActive and :1085's project read when the menu opens.
                cluster.Children.Add(new RowActionsButton(() => Actions(Model, current, Model.Settings.IsProjectActive(current.Id), RequestRemoval), isMenuPresented));
                var plus = PlainControls.Button(new RowAccessoryIcon("plus"), () => Model.NewThread(current)); // :1119-1123, :1124 .buttonStyle(.plain)
                ToolTip.SetTip(plus, "New thread in " + current.Name); // :1125
                cluster.Children.Add(plus);
                return cluster;
            }
            cluster = null;
            if (!current.IsExpanded && shownCount > 0) // :1127
            {
                none = null;
                if (countHost is null)
                {
                    countText = new TextBlock();
                    SwiftFont.Subheadline.MonospacedDigit().Apply(countText); // :1129
                    countText.Foreground = Chrome.SecondaryText.Brush(countText); // :1130
                    // SidebarRow (UI/Chrome/Chrome.cs) sets `built.Margin` itself, unconditionally, once accessory()
                    // returns; a Margin set here would be overwritten. :1131's own .padding(.trailing, 4) nests
                    // inside SidebarRow's own trailing space the same way SwiftUI composes nested paddings, so it is
                    // carried as this wrapper's own Padding, which SidebarRow's Margin assignment does not touch.
                    countHost = new Border { Padding = new Thickness(0, 0, 4, 0), Child = countText };
                }
                countText!.Text = shownCount.ToString(CultureInfo.InvariantCulture); // :1128
                return countHost;
            }
            countHost = null;
            return none ??= new Border(); // :1115-1133, no branch matched: EmptyView, nothing drawn
        }
        var row = new SidebarRow(
            snapshot.Name, // :1098; no source path renames a project, so the title itself needs no reactivity
            action: () => // :1100-1105
            {
                if (!togglesExpansion) return; // :1101
                Transaction.WithAnimation(Chrome.PanelSlide, () => Model.UpdateProject(snapshot.Id, p => p with { IsExpanded = !p.IsExpanded })); // :1102-1104
            },
            icon: () => iconBadge = new SidebarIconBadge(BuildIconGlyph), // :1106-1114
            accessory: BuildAccessory,
            accessoryWidth: 40); // :1099
        row.Track(() =>
        {
            // AppSettings.IsProjectActive(id) (App.AppSettings, a dependency of this unit) reads its backing field
            // directly rather than through the tracked ProjectActivationOverrides/ProjectsEnabled properties, so
            // calling it alone here would record no dependency and this Track would never re-fire when either
            // changes; reading both properties (even unused) registers them, matching what Swift's Observable
            // macro instruments transitively and this port's own Read()/Set() otherwise would not.
            _ = Model.Settings.ProjectActivationOverrides;
            _ = Model.Settings.ProjectsEnabled;
            return (Model.Project(snapshot.Id) ?? snapshot, Model.Settings.IsProjectActive(snapshot.Id)); // :1085, :1096
        }, value =>
        {
            current = value.Item1;
            // :1135, on this ProjectRow itself (the whole body, as the source's own .opacity() wraps it), not the inner
            // SidebarRow. DEVIATION OD-0076 (ENGINEERING DECISION, leftovers pass 2026-10-04): the Mac snaps the dim; the
            // port eases it on Chrome.hover, the quick state curve OD-0076 names for sites the Mac snaps. The first read
            // shows the row as it stands.
            var dimTarget = value.Item2 ? 1d : 0.55d;
            dim ??= new AnimatedScalar(this, dimTarget, level => Opacity = level);
            dim.Set(dimTarget, dimPrimed ? Transaction.Current ?? Chrome.Hover : null);
            dimPrimed = true;
            // REVIEW FIX (part 6): SwiftUI re-reads `project.icon` (:1085, :1108) on every render, but the icon
            // slot (SidebarIconBadge, UI/Chrome/Chrome.cs) is built once, synchronously, inside SidebarRow's own
            // constructor (Chrome.cs:1079 `var iconSlot = icon();`), before this Track is ever installed; nothing
            // else re-invokes that closure. This Track already fires whenever the live project record changes
            // (the same record `current.Icon` reads), so it also re-populates the already-built badge in place,
            // the way this method's own header comment describes the whole constructor doing (surgical update,
            // not a fresh SidebarRow).
            // DEVIATION OD-0076: a changed icon crossfades the marks (the source's `if let` swaps them by a cut, :1107-1113);
            // a change of the record that leaves the icon as it was swaps nothing.
            if (iconBadge is not null && !Equals(shownIcon, current.Icon))
            {
                shownIcon = current.Icon;
                CrossfadeGlyph(iconBadge, BuildIconGlyph());
            }
            row.RefreshAccessory(); // :1127 `!project.isExpanded`: the live project's expansion picks the accessory's branch
        });
        // :1091 isMenuPresented is this row's own @State, which SidebarRow's hover Track never reads; SwiftUI's body
        // re-reads the accessory on any write to it, so a change of it asks SidebarRow for the accessory again, with the
        // row's own hover state (Chrome.swift:731-737, .overlay before .onHover: the pointer resting on the ellipsis or
        // the plus is still on the row). A menu closed with the pointer elsewhere drops the ellipsis and plus; one closed
        // with the pointer on them keeps them.
        row.Track(() => state.IsMenuPresented, _ => row.RefreshAccessory());
        // :1081-1089 `snapshot`, `count` and `togglesExpansion` are plain inputs of a struct the list re-evaluates with each
        // pass; SwiftUI keeps the row it already has under the same ForEach id ("project-<uuid>", :524) and hands it the
        // new inputs, so its @State (isMenuPresented, the hover, and the port's own removal question) survives them
        // (UpdateItemView). The accessory is the one part that reads them, :1127-1131: the count Text takes the new count
        // in place, and an open menu keeps the button it hangs on.
        update = (newSnapshot, newCount) =>
        {
            shownCount = newCount; // :1088
            current = Model.Project(newSnapshot.Id) ?? newSnapshot; // :1085
            row.RefreshAccessory();
        };
        // :1138-1161 .contextMenu: SwiftUI re-evaluates this closure's captures (project, isActive, hasOverride,
        // pairsSnapshot) fresh every time the menu opens, not once at construction. ENGINEERING DECISION (closes
        // STOP #7/#4 for ProjectRow only; SidebarHelperRow's and HeadCardsRow's own .contextMenu sites are unchanged
        // and stay under STOP #7): Avalonia's ContextMenu.Opening event (Avalonia.Controls.ContextMenu, fires each
        // time IsOpen goes false->true, System.ComponentModel.CancelEventHandler; Avalonia.Controls.xml) is the
        // Windows/Avalonia per-open hook STOP #7 named as the missing foundation; RowActionMenuButtons' own header ED
        // already names Avalonia's ContextMenu as the NSMenu counterpart a caller installs Items on
        // (UI/Chrome/Popovers.cs :532-535), leaving only this attachment. Setting Control.ContextMenu makes a real
        // right-click open it automatically (Avalonia.Input.InputElement.ContextRequestedEvent), the Windows
        // counterpart of AppKit's right-click-opens-NSMenu. Rebuilding Items inside Opening, reading Model directly
        // rather than this row's own `current`/Track-cached fields, reproduces the source's fresh per-open read
        // (:1139-1142, :1151) exactly, including a right-click during a stale `current` (before its own Track above
        // next fires).
        var contextMenu = new ContextMenu();
        contextMenu.Opening += (_, _) =>
        {
            var projectSnapshot = Model.Project(snapshot.Id) ?? current; // :1139-1140, :1085
            var activeSnapshot = Model.Settings.IsProjectActive(projectSnapshot.Id); // :1141
            var overrideSnapshot = Model.Settings.HasProjectActivationOverride(projectSnapshot.Id); // :1142
            var pairsSnapshot = Model.HydraPickerPairs // :1151-1153
                .Select(pair => new ProjectPairChoice { Id = pair.Id, Title = HydraPairSummary.Title(pair, Model.Providers) })
                .ToList();
            contextMenu.Items.Clear();
            foreach (var item in BuildContextMenuItems(projectSnapshot, activeSnapshot, overrideSnapshot, Model, pairsSnapshot, RequestRemoval))
                contextMenu.Items.Add(item);
        };
        row.ContextMenu = contextMenu; // :1138 .contextMenu { ... }; UI-SIDEBAR-014/-015/-016, PR-SIDEBAR-130/-133
        Child = new Panel { Children = { row, RemovalPopover(row, snapshot) } };
    }

    // OD-0008 A9 (PR-SIDEBAR-131): Remove project honours confirmBeforeDeleting the way the three thread-deletion paths
    // do (:190-197, :359-365, :890-896 `if model.settings.confirmBeforeDeleting { pendingDeletionID = thread.id } else
    // { model.delete(thread.id) }`); with it off the removal runs at once, as upstream's :1183-1186 and :1212-1214.
    private void RequestRemoval(Project project)
    {
        if (Model.Settings.ConfirmBeforeDeleting) state.IsRemovalPresented = true;
        else Model.RemoveProject(project);
    }

    // OD-0012 E2: the question on the row itself, in the thread delete question's style (:217-237 deletePopover,
    // arrowEdge .trailing). Dismissing it (click away, Escape) closes the popup, whose Closed handler clears the
    // presentation through the binding, and removes nothing.
    private Popup RemovalPopover(Control row, Project snapshot)
    {
        removalSnapshot = snapshot;
        var isPresented = new Binding<bool>(() => state.IsRemovalPresented, value => state.IsRemovalPresented = value);
        var popup = PlainControls.Popover(row, isPresented, () => new RemoveProjectPopover(
            Model.Project(snapshot.Id) ?? snapshot, ConfirmRemoval), PlacementMode.Right); // :220 arrowEdge: .trailing
        popup.Opened += (_, _) => state.IsRemovalPopoverShown = true; // :229 .onAppear
        popup.Closed += (_, _) =>
        {
            state.IsRemovalPopoverShown = false; // :231 .onDisappear
            CarryOutConfirmedRemoval(); // :232
        };
        return popup;
    }

    private Project? removalSnapshot;

    // :256-263 confirmDeletion's shape: record the deed, close the question, and run it now unless the popup is
    // still on screen.
    private void ConfirmRemoval()
    {
        state.RemovalConfirmed = true;
        state.IsRemovalPresented = false;
        if (!state.IsRemovalPopoverShown) CarryOutConfirmedRemoval();
    }

    // :295-306 carryOutConfirmedDeletion's shape: once, on the main actor's next turn.
    private void CarryOutConfirmedRemoval()
    {
        if (!state.RemovalConfirmed || removalSnapshot is not { } snapshot) return;
        state.RemovalConfirmed = false;
        var model = Model;
        MainActor.Spawn(() =>
        {
            model.RemoveProject(model.Project(snapshot.Id) ?? snapshot); // AppModel.swift:558-567, upstream's removal
            return Task.CompletedTask;
        });
    }

    // REVIEW FIX (part 6): SidebarIconBadge (UI/Chrome/Chrome.cs :674-677) sets its glyph's own foreground and
    // centers it once, inside its own constructor, the moment the glyph factory first returns; swapping the glyph
    // afterwards (this method) must reapply that same treatment, since a freshly built ProjectIconMark or
    // SidebarSymbol does not get it a second time from SidebarIconBadge's own constructor.
    // DEVIATION OD-0076 (leftovers pass 2026-10-04): the swap is a crossfade over Chrome.hover (or the transaction the
    // change runs in): the new mark rises over the old one, which fades where it stands in the same 20 pt box and leaves
    // once clear; each fades on its own scalar, so a swap that arrives mid-fade leaves nothing cut.
    private static void CrossfadeGlyph(SidebarIconBadge badge, Control glyph)
    {
        glyph.SetValue(TextElement.ForegroundProperty, Chrome.PrimaryText.Opacity(0.85).Brush(glyph)); // Chrome.cs :676
        glyph.HorizontalAlignment = HorizontalAlignment.Center; // Chrome.cs :674, :677 (centered inside the Panel)
        glyph.VerticalAlignment = VerticalAlignment.Center;
        if (badge.Child is not Panel panel) return;
        var animation = Transaction.Current ?? Chrome.Hover;
        var leaving = panel.Children.ToArray();
        glyph.Opacity = 0;
        panel.Children.Add(glyph);
        new AnimatedScalar(glyph, 0, level => glyph.Opacity = level).Set(1, animation);
        foreach (var old in leaving)
            new AnimatedScalar(old, old.Opacity, level =>
            {
                old.Opacity = level;
                if (level == 0) panel.Children.Remove(old);
            }).Set(0, animation);
    }

    // :5 @Environment(AppModel.self) private var model (this file's own convention: SidebarView.cs:337)
    private AppModel Model => AppEnvironment.GetAppModel(this); // :1079

    // :1168-1203 private static func contextMenuActions(project:isActive:hasOverride:model:) -> [RowAction]. Part 6,
    // wired to the live ContextMenu at Job SB (this class's own constructor, ContextMenu.Opening). The weak-model
    // capture mirrors ThreadActions.MakeContextMenu's own reasoning (the AppKit menu outlives the right-click).
    // PR-SIDEBAR-127, PR-SIDEBAR-129, PR-SIDEBAR-130; OD-0008 B1 ("Reveal in File Explorer" replaces "Reveal in Finder").
    // OD-0008 A9: `requestRemoval` is the row's RequestRemoval, which asks first while confirmBeforeDeleting is on.
    private static ValueList<RowAction> ContextMenuActions(Project project, bool isActive, bool hasOverride, AppModel model, Action<Project> requestRemoval)
    {
        var projectID = project.Id; // :1169
        var weakModel = new WeakReference<AppModel>(model); // [weak model]
        var items = new List<RowAction>
        {
            new() { Title = "New thread", Symbol = "square.and.pencil", Action = () => { if (weakModel.TryGetTarget(out var live)) live.NewThread(project); } }, // :1171-1174
            new() { Title = "New thread in worktree", Symbol = "square.stack.3d.up", Action = () => { if (weakModel.TryGetTarget(out var live)) live.NewThread(project, WorkspaceMode.Worktree); } }, // :1175-1178
            new() { Title = "Reveal in File Explorer", Symbol = "folder", StartsGroup = true, Action = () => { if (weakModel.TryGetTarget(out _)) ShellWorkspace.RevealInFileExplorer(project.Path); } }, // :1179-1182, OD-0008 B1
            new() { Title = "Remove project", Symbol = "trash", IsDestructive = true, StartsGroup = true, Action = () => { if (weakModel.TryGetTarget(out _)) requestRemoval(project); } }, // :1183-1186, asking first by OD-0008 A9
        };
        items.Add(new RowAction // :1188-1195
        {
            Title = isActive ? "Deactivate project" : "Activate project",
            Symbol = "power",
            StartsGroup = true,
            Action = () => { if (weakModel.TryGetTarget(out var live)) live.Settings.SetProjectActive(!isActive, projectID); },
        });
        if (hasOverride) // :1196
        {
            items.Add(new RowAction { Title = "Follow global setting", Symbol = "arrow.uturn.backward", Action = () => { if (weakModel.TryGetTarget(out var live)) live.Settings.ClearProjectActivationOverride(projectID); } }); // :1197-1200
        }
        return new ValueList<RowAction>(items);
    }

    // :1205-1229 private func actions(isActive:) -> [RowAction]. Part 6. The ellipsis popover's own list: live
    // closures over the model and project, unlike ContextMenuActions' weak snapshots above (the popover button owns
    // its own lifetime; there is no AppKit menu to outlive it, matching ThreadActions.Make's own reasoning).
    // PR-SIDEBAR-127, PR-SIDEBAR-129; OD-0008 B1.
    private static ValueList<RowAction> Actions(AppModel model, Project project, bool isActive, Action<Project> requestRemoval)
    {
        var items = new List<RowAction>
        {
            new() { Title = "New thread", Symbol = "square.and.pencil", Action = () => model.NewThread(project) }, // :1207
            new() { Title = "New thread in worktree", Symbol = "square.stack.3d.up", Action = () => model.NewThread(project, WorkspaceMode.Worktree) }, // :1208-1210
            new() { Title = "Reveal in File Explorer", Symbol = "folder", StartsGroup = true, Action = () => ShellWorkspace.RevealInFileExplorer(project.Path) }, // :1211, OD-0008 B1
            new() { Title = "Remove project", Symbol = "trash", IsDestructive = true, StartsGroup = true, Action = () => requestRemoval(project) }, // :1212-1214, asking first by OD-0008 A9
        };
        items.Add(new RowAction // :1216-1220
        {
            Title = isActive ? "Deactivate project" : "Activate project",
            Symbol = "power",
            StartsGroup = true,
            Action = () => model.Settings.SetProjectActive(!isActive, project.Id),
        });
        if (model.Settings.HasProjectActivationOverride(project.Id)) // :1223
        {
            items.Add(new RowAction { Title = "Follow global setting", Symbol = "arrow.uturn.backward", Action = () => model.Settings.ClearProjectActivationOverride(project.Id) }); // :1224-1226
        }
        return new ValueList<RowAction>(items);
    }

    // :1138-1161 the context menu's own content, assembled from an already-resolved snapshot the way this file's
    // static pieces always take their collaborators explicitly (BuildTrailingOverlay, BuildFoldOverlay); this
    // class's own constructor installs the result on a live ContextMenu.Items, freshly, on every open (Job SB).
    // PR-SIDEBAR-129, UI-SIDEBAR-014, UI-SIDEBAR-015, UI-SIDEBAR-016.
    private static IReadOnlyList<Control> BuildContextMenuItems(
        Project project, bool isActive, bool hasOverride, AppModel model, IReadOnlyList<ProjectPairChoice> pairs, Action<Project> requestRemoval)
    {
        var items = new List<Control>(new RowActionMenuButtons(ContextMenuActions(project, isActive, hasOverride, model, requestRemoval)).Items); // :1143-1148
        if (pairs.Count > 0) // :1154 pairsSnapshot.isEmpty
        {
            items.Add(new Separator()); // :1155
            var submenu = new MenuItem // :1156-1160
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { SwiftImage.SystemName("point.3.connected.trianglepath.dotted").Font(Chrome.IconFont), new TextBlock { Text = "Start new chats on" } }, // :1159
                },
            };
            foreach (var item in new ProjectPairMenuItems(project.Id, project.HydraPairID, pairs, model).Items) submenu.Items.Add(item); // :1157
            items.Add(submenu);
        }
        return items;
    }
}

/// <summary>:1233-1236 <c>private struct ProjectPairChoice: Identifiable</c>.</summary>
internal sealed record ProjectPairChoice
{
    public required Guid Id { get; init; } // :1234
    public required string Title { get; init; } // :1235
}

/// <summary>:1241-1270 <c>private struct ProjectPairMenuItems: View</c>. Part 6. PR-SIDEBAR-133: the source's own
/// capture changed from <c>[weak model]</c> to a direct one at B2 (not user-visible; this file's own note at
/// UI-SIDEBAR-016).</summary>
internal sealed class ProjectPairMenuItems : Decorator
{
    /// <param name="projectID">:1242 <c>let projectID: UUID</c>.</param>
    /// <param name="selectedID">:1243 <c>let selectedID: UUID?</c>.</param>
    /// <param name="pairs">:1244 <c>let pairs: [ProjectPairChoice]</c>.</param>
    /// <param name="model">:1245 <c>let model: AppModel</c>.</param>
    /// <summary>The rows this produces, in order, for a caller's submenu <c>Items</c> (as RowActionMenuButtons
    /// exposes its own for a ContextMenu.Items).</summary>
    public IReadOnlyList<Control> Items { get; }

    // :1247-1269 body
    public ProjectPairMenuItems(Guid projectID, Guid? selectedID, IReadOnlyList<ProjectPairChoice> pairs, AppModel model)
    {
        var items = new List<Control> { BuildItem("The chat's last pair", selectedID is null, () => model.SetHydraPair(null, projectID)) }; // :1248-1256
        items.Add(new Separator()); // :1257
        foreach (var pair in pairs) // :1258
        {
            var pairID = pair.Id; // :1259
            items.Add(BuildItem(pair.Title, selectedID == pair.Id, () => model.SetHydraPair(pairID, projectID))); // :1259-1267
        }
        Items = items;
    }

    // :1250-1255 (the clearing item) and :1262-1266 (a pair item): a checkmark Label for the current choice, plain
    // Text otherwise, with no reserved check column for the unchecked items (the label itself is swapped, not a
    // menu-state flag).
    private static MenuItem BuildItem(string title, bool isSelected, Action action)
    {
        var menuItem = new MenuItem
        {
            Header = isSelected
                ? new StackPanel // :1252 Label(_, systemImage: "checkmark")
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { SwiftImage.SystemName("checkmark").Font(Chrome.IconFont), new TextBlock { Text = title } },
                }
                : new TextBlock { Text = title }, // :1254 Text(title)
        };
        // The system menu this reaches (NativeContextMenus) draws text, separators and check marks, not a Label's image, so the
        // checkmark of the swapped label is also the row's check mark (ENGINEERING DECISION, leftovers pass 2026-10-04): one row
        // is the current choice, as a radio row. Without it the current choice showed no mark at all on Windows.
        menuItem.ToggleType = MenuItemToggleType.Radio;
        menuItem.IsChecked = isSelected;
        menuItem.Click += (_, _) => action();
        return menuItem;
    }
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
internal sealed class SidebarHelperRow : Decorator, IGlideRow
{
    /// <param name="snapshot">:1320 <c>let snapshot: ChatThread</c>.</param>
    /// <param name="isLast">:1326 <c>let isLast: Bool</c>.</param>
    /// <param name="menuRequests">:1330 <c>let menuRequests: SidebarMenuRequests</c>.</param>
    /// <param name="onFold">:1331 <c>let onFold: () -> Void</c>.</param>
    /// <param name="onRename">:1332 <c>let onRename: () -> Void</c>.</param>
    /// <param name="onDelete">:1333 <c>let onDelete: () -> Void</c>.</param>
    // :1338-1409 body. Part 7: RowGlideAnimator.Shared.IsHiding(_:) (used directly by HelperStubRow, part 5) and the
    // per-row hover/menu @State pattern (ProjectRow, part 6) are both now in the tree, so this constructor is
    // assembled from the pieces below. The right-click .contextMenu (:1387-1397) is attached on the button with its
    // overlay (overlayHost), rebuilt at every open from a live Model read, as ProjectRow's and SidebarThreadRow's are;
    // the ellipsis popover (built fresh on every hover/open) is the other menu surface.
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering; // :1339 @State private var isHovering = false
        private bool isMenuPresented; // :1340 @State private var isMenuPresented = false
        private ChatThread snapshot = null!;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
        public bool IsMenuPresented { get => Read(isMenuPresented); set => Set(ref isMenuPresented, value); }

        // :1320 `let snapshot`, an input of the struct the list hands a new value with each pass (Update), not @State.
        public ChatThread Snapshot { get => Read(snapshot); set => Set(ref snapshot, value); }
    }

    private Action<ChatThread, bool>? update;

    /// <summary>:1320, :1326 the list hands a surviving helper row its new <c>snapshot</c> and <c>isLast</c>; SwiftUI keeps
    /// the row it already has under the same ForEach id (the helper's sidebar item id, :470-472), so its @State (the hover
    /// and an open ellipsis menu) survives them.</summary>
    internal void Update(ChatThread snapshot, bool isLast) => update?.Invoke(snapshot, isLast);

    // PORT-ONLY (ED-NEW-session-hotkeys): the selection glide's hold and the row's number badge (see SidebarThreadRow).
    private GlideHold glideHold;
    private Action? repaintFill;
    private SelectionGlide? glide;
    private readonly Guid threadID;
    private readonly Control fillShape;
    internal SessionKeycap Keycap { get; }

    Guid IGlideRow.ThreadID => threadID;

    Control IGlideRow.FillShape => fillShape;

    void IGlideRow.SetGlideHold(GlideHold hold)
    {
        if (glideHold == hold) return;
        glideHold = hold;
        repaintFill?.Invoke();
    }

    public SidebarHelperRow(ChatThread snapshot, bool isLast, SidebarMenuRequests menuRequests, Action onFold, Action onRename, Action onDelete)
    {
        threadID = snapshot.Id;
        var state = new ViewState { Snapshot = snapshot };
        var current = snapshot; // :1324-1325, refreshed by the Track below
        var isMenuPresented = new Binding<bool>(() => state.IsMenuPresented, value => state.IsMenuPresented = value);

        var connector = new HelperConnector(isLast, onFold) // :1348-1349
        {
            Width = ThreadRowMetrics.ConnectorWidth,
            Height = ThreadRowMetrics.HelperHeight,
        };
        update = (newSnapshot, newIsLast) =>
        {
            state.Snapshot = newSnapshot; // :1324-1325 the fallback for a thread that has just been deleted
            connector.EndsHere = newIsLast; // :1349 HelperConnector(endsHere: isLast, ...)
        };

        // :1359-1364 Text(...).lineLimit(1).truncationMode(.tail) then Spacer(minLength: 4), inside HStack(spacing: 6).
        // A horizontal StackPanel measures its own children at infinite width along the stack axis regardless of
        // what bounded width the StackPanel itself received (the same defect HelperStubRow's own leading HStack
        // hit, :2437-2440), so TextTrimming never actually engaged here; a Grid's star column, ThreadRowFace's own
        // idiom for a bounded trailing measure width (:2969, :3014), replaces it. The Spacer(minLength: 4) is
        // reproduced as a trailing margin on the title itself, the same substitution ThreadRowFace already makes
        // for its own title column (:3013 texts.Margin). REVIEW FIX.
        var title = new TextBlock
        {
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 4, 0), // :1364 Spacer(minLength: 4)
        };
        Control? glyph = null;
        var leading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center }; // :1350 HStack(spacing: 6) { [glyph]; title; Spacer(minLength: 4) }
        Grid.SetColumn(title, 1);
        leading.Children.Add(title);

        var shape = new Border // :1370 shape.fill(...); .contentShape(shape)
        {
            Height = ThreadRowMetrics.HelperHeight, // :1367
            HorizontalAlignment = HorizontalAlignment.Stretch, // :1368 frame(maxWidth: .infinity, alignment: .leading)
            Child = leading,
        }.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        var fill = new AnimatedColor(shape);
        shape.Background = fill.Brush;

        var button = PlainControls.Button(shape, () => Model.SelectedThreadID = current.Id); // :1348-1350
        var overlayHost = new Panel { Children = { button } };
        Control? overlayControl = null;

        // :1375-1404 `.overlay(alignment: .trailing) { ... }` comes BEFORE `.onHover`, so the hover region is the button
        // together with its overlaid status or ellipsis: the pointer resting on the ellipsis is still on the row and keeps
        // it (the hover is on the overlay host, as SidebarThreadRow's overlays host carries its own, :1816).
        overlayHost.OnHover(hovering => // :1401-1404
        {
            Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering);
            if (hovering) Model.WarmDocuments([current.Id]); // :1404
        });

        // :1387-1397 .contextMenu on the button with its trailing overlay: snapshot-only, re-evaluated fresh at every open
        // (UI-SIDEBAR-033 "right-click opens the helper's context menu"). The same per-open-refresh foundation as
        // SidebarThreadRow's and ProjectRow's own menus (their ENGINEERING DECISION, ContextMenu.Opening over a live
        // Model read): the host is overlayHost, so a right-click on the ellipsis or the status opens it as well, as
        // SwiftUI's modifier covers the overlay.
        var contextMenu = new ContextMenu();
        contextMenu.Opening += (_, _) =>
        {
            var threadSnapshot = Model.Thread(snapshot.Id) ?? snapshot; // :1388, :1324-1325
            var settleFirstSnapshot = !threadSnapshot.IsHelper && Model.Settings.ThreadFinishAction == ThreadFinishAction.Settle; // :1391
            contextMenu.Items.Clear();
            foreach (var item in new RowActionMenuButtons(ThreadActions.MakeContextMenu(Model, threadSnapshot, settleFirstSnapshot, menuRequests)).Items) // :1392-1396
                contextMenu.Items.Add(item);
        };
        overlayHost.ContextMenu = contextMenu;

        bool? selectedBefore = null, actionsBefore = null;
        (bool HasUnread, TurnStatus? LastStatus) statusBefore = default;
        overlayHost.Track(() =>
        {
            var thread = Model.Thread(snapshot.Id) ?? state.Snapshot; // :1324-1325, the snapshot the list last handed the row
            var isSelected = Model.IsSelected(thread.Id); // :1340
            var showsActions = state.IsHovering || state.IsMenuPresented; // :1341
            var isHidden = RowGlideAnimator.Shared.Current.IsHiding(thread.ParentThreadID ?? thread.Id); // :1343, :1409
            return (thread, isSelected, showsActions, isHidden);
        }, value =>
        {
            var (thread, isSelected, showsActions, isHidden) = value;
            current = thread;
            title.Text = thread.Title; // :1359, verbatim
            SwiftFont.System(SwiftFont.TextStyle.Callout, weight: TitleWeight(isSelected, thread.HasUnread)).Apply(title); // :1361
            title.Foreground = Chrome.PrimaryText.Opacity(TitleOpacity(isSelected)).Brush(title); // :1362
            if (HeadGlyph(thread) is { } head) // :1354-1357
            {
                if (glyph is not null) leading.Children.Remove(glyph);
                glyph = head;
                glyph.Margin = new Thickness(0, 0, 6, 0); // :1350 HStack(spacing: 6), the gap before the title
                Grid.SetColumn(glyph, 0);
                leading.Children.Add(glyph);
            }
            else if (glyph is not null)
            {
                leading.Children.Remove(glyph);
                glyph = null;
            }
            shape.Padding = new Thickness(6, 0, TrailingPadding(showsActions), 0); // :1365-1367
            // :1370-1372 `.animation(Chrome.hover, value: isSelected)`: a selection change eases on Chrome.hover whatever
            // transaction carried it; a hover change rides its own withAnimation(Chrome.hover) (:1386).
            var hovering = state.IsHovering;
            void PaintFill(Animation? animation) => fill.Set(Chrome.Overlay(glideHold == GlideHold.Arriving && isSelected
                ? 0 // PORT-ONLY (ED-NEW-session-hotkeys): the glide draws this fill while it lands here
                : BackgroundFill(isSelected, hovering)), animation);
            repaintFill = () => PaintFill(null);
            PaintFill(glideHold != GlideHold.None ? null
                : selectedBefore is { } before && before != isSelected ? Chrome.Hover : Transaction.Current);
            selectedBefore = isSelected;
            // :1375-1384 the Group's two branches swap when showsActions flips inside withAnimation(Chrome.hover) (:1386),
            // so they crossfade on SwiftUI's default .opacity transition. While a branch holds SwiftUI keeps its view under
            // its identity and hands it the live record (REVIEW round 2, UI-SIDEBAR-033): the ellipsis keeps its button, so
            // an open menu keeps its anchor and is not presented again, and its actions are the ones the body builds now
            // (:1375-1376 makeActions(), read as the menu opens); the status is drawn again only when what it shows of the
            // record (:2421-2423 hasUnread, lastStatus) changed. A status redrawn with no transaction replaces in place.
            var statusInput = (thread.HasUnread, thread.LastStatus);
            var branchHolds = actionsBefore == showsActions && overlayControl is not null;
            var keep = branchHolds && (showsActions || statusInput == statusBefore);
            var crossfade = actionsBefore is { } wasShowing && wasShowing != showsActions && Transaction.Current is not null;
            actionsBefore = showsActions;
            statusBefore = statusInput;
            if (!keep)
            {
                if (overlayControl is { } previous)
                {
                    if (crossfade) SwiftTransition.Remove(overlayHost, previous);
                    else overlayHost.Children.Remove(SwiftTransition.Container(previous));
                }
                var actionsButton = showsActions
                    ? new RowActionsButton(() => ThreadActions.Make(Model, current, onRename, onDelete), isMenuPresented) // :1375-1376
                    : null;
                overlayControl = BuildTrailingOverlay(showsActions, thread, actionsButton!); // :1375-1384
                if (crossfade) SwiftTransition.Insert(overlayHost, overlayHost.Children.Count, overlayControl);
                else overlayHost.Children.Add(overlayControl);
            }
            Opacity = isHidden ? 0 : 1; // :1409, on this SidebarHelperRow itself (the whole HStack)
        });

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") }; // :1338 HStack(spacing: 0)
        Grid.SetColumn(connector, 0);
        Grid.SetColumn(overlayHost, 1);
        row.Children.Add(connector);
        row.Children.Add(overlayHost);
        // PORT-ONLY (ED-NEW-session-hotkeys): the number badge centres on the connector's line (:1292, 18 pt in).
        fillShape = shape;
        Keycap = new SessionKeycap(snapshot.Id, () => [connector], button)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(ThreadRowMetrics.ConnectorLineX - Chrome.IconSize / 2, 0, 0, 0),
        };
        Grid.SetColumn(Keycap, 0);
        Grid.SetColumnSpan(Keycap, 2);
        row.Children.Add(Keycap);
        Child = row;
        AttachedToVisualTree += (_, _) =>
        {
            glide = this.FindAncestorOfType<SidebarView>()?.Glide;
            glide?.Register(this);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            glide?.Unregister(this);
            glide = null;
        };
    }

    // :5 @Environment(AppModel.self) private var model (this file's own convention: SidebarView.cs:337)
    private AppModel Model => AppEnvironment.GetAppModel(this);

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
        content.VerticalAlignment = VerticalAlignment.Center; // :1375 .overlay(alignment: .trailing) centres it on the row's height
        return content;
    }

    // :1413-1420 static func == (lhs:rhs:) -> Bool. Part 5. PR-SIDEBAR-155.
    internal static bool RowEquals(ChatThread leftSnapshot, bool leftIsLast, ChatThread rightSnapshot, bool rightIsLast) =>
        leftSnapshot.Id == rightSnapshot.Id // :1414
        && SwiftString.Equal(leftSnapshot.Title, rightSnapshot.Title) // :1415
        && leftSnapshot.UpdatedAt == rightSnapshot.UpdatedAt // :1416
        && leftSnapshot.IsPinned == rightSnapshot.IsPinned // :1417
        && leftSnapshot.IsSettled == rightSnapshot.IsSettled // :1418
        && leftIsLast == rightIsLast; // :1419
}

/// <summary>:1427-1548 <c>private struct HeadCardsRow: View</c>: the parent's Hydra heads as glyph cards.</summary>
internal sealed class HeadCardsRow : Decorator
{
    /// <param name="parentSnapshot">:1429 <c>let parentSnapshot: ChatThread</c>.</param>
    /// <param name="headSnapshots">:1430 <c>let headSnapshots: [ChatThread]</c>.</param>
    /// <param name="menuRequests">:1431 <c>let menuRequests: SidebarMenuRequests</c>.</param>
    /// <param name="onFold">:1432 <c>let onFold: () -> Void</c>.</param>
    // :1434-1437 @State private var width/hoveredID/showsAllHeads.
    private sealed class ViewState : MainActorModel
    {
        private double width;
        private Guid? hoveredID;
        private bool showsAllHeads;
        private IReadOnlyList<ChatThread> heads = [];

        public double Width { get => Read(width); set => Set(ref width, value); }
        public Guid? HoveredID { get => Read(hoveredID); set => Set(ref hoveredID, value); }
        public bool ShowsAllHeads { get => Read(showsAllHeads); set => Set(ref showsAllHeads, value); }

        // :1430 `let headSnapshots`, an input of the struct the list hands a new value with each pass (Update), not @State.
        public IReadOnlyList<ChatThread> Heads { get => Read(heads); set => Set(ref heads, value); }
    }

    private Action<IReadOnlyList<ChatThread>>? update;

    /// <summary>:1430 the list hands a surviving card its new <c>headSnapshots</c>; SwiftUI keeps the card it already has
    /// under the same ForEach id ("heads-&lt;uuid&gt;", :470-472), so its @State (the width, the hovered glyph and
    /// whether every head shows) survives them.</summary>
    internal void Update(IReadOnlyList<ChatThread> headSnapshots) => update?.Invoke(headSnapshots);

    // PORT-ONLY: animates a ScaleTransform's uniform scale along a Chrome.hover-style transaction, the same
    // frame-tick technique PlainControls.AnimatedColor already uses for a colour (UI/Port/PlainControls.cs:186), here
    // adapted to a scalar for the per-glyph hover scale (:1484-1486).
    private sealed class AnimatedScale(ScaleTransform transform)
    {
        private IFrameSource? source;
        private Action<TimeSpan>? pending;

        public void Set(double to, Animation? animation)
        {
            Stop();
            var from = transform.ScaleX;
            if (animation is null || from == to)
            {
                transform.ScaleX = transform.ScaleY = to;
                return;
            }
            var start = SwiftClock.Now;
            var span = TimeSpan.FromSeconds(animation.Duration);
            var frames = FrameClock.Source.Current;
            Action<TimeSpan>? tick = null;
            tick = _ =>
            {
                var now = SwiftClock.Now;
                // ED-117 (2): a span S from instant s is complete at a frame when (now - since) + 1 ms >= S (in
                // ticks), matching FrameClock.IsComplete (UI/Port/FrameClock.cs, internal to Marrow.UI, so restated
                // here rather than called across the assembly boundary).
                var complete = (now - start).Ticks + TimeSpan.TicksPerMillisecond >= span.Ticks;
                var value = complete ? to : from + (to - from) * animation.Progress((now - start).TotalSeconds);
                transform.ScaleX = value;
                transform.ScaleY = value;
                if (complete) pending = null;
                else frames.RequestFrame(tick!);
            };
            source = frames;
            pending = tick;
            frames.RequestFrame(tick);
        }

        private void Stop()
        {
            if (pending is { } frame) source?.CancelFrame(frame);
            pending = null;
        }
    }

    // PORT-ONLY, same frame-tick idiom as AnimatedScale above and PlainControls.AnimatedColor (UI/Port/PlainControls.cs:186),
    // here adapted to a plain double sink for the card's and connector's own height (:1535
    // .animation(.snappy(duration: 0.18), value: cardRows) and :1541 .animation(Chrome.panelSlide, value:
    // heads.map(\.id)), neither of which was wired to anything but a bare property assignment before this fix).
    // REVIEW FIX.
    internal sealed class AnimatedDouble(Action<double> apply)
    {
        private double current;
        private bool primed;
        private IFrameSource? source;
        private Action<TimeSpan>? pending;

        public void Set(double to, Animation? animation)
        {
            Stop();
            if (!primed || animation is null || current == to)
            {
                primed = true;
                current = to;
                apply(current);
                return;
            }
            var from = current;
            var start = SwiftClock.Now;
            var span = TimeSpan.FromSeconds(animation.Duration);
            var frames = FrameClock.Source.Current;
            Action<TimeSpan>? tick = null;
            tick = _ =>
            {
                var now = SwiftClock.Now;
                // ED-117 (2): a span S from instant s is complete at a frame when (now - since) + 1 ms >= S (in
                // ticks), matching FrameClock.IsComplete (UI/Port/FrameClock.cs, internal to Marrow.UI, so restated
                // here rather than called across the assembly boundary).
                var complete = (now - start).Ticks + TimeSpan.TicksPerMillisecond >= span.Ticks;
                var value = complete ? to : from + (to - from) * animation.Progress((now - start).TotalSeconds);
                current = value;
                apply(value);
                if (complete) pending = null;
                else frames.RequestFrame(tick!);
            };
            source = frames;
            pending = tick;
            frames.RequestFrame(tick);
        }

        internal void Stop()
        {
            if (pending is { } frame) source?.CancelFrame(frame);
            pending = null;
        }
    }

    // :1440-1542 body. Part 7: OrderedHeads, model.SelectedThreadID/IsSelected/WarmDocuments and
    // ThreadActions.MakeContextMenu (this file's own dependencies) are all in the tree, so every piece, the per-glyph
    // right-click .contextMenu (:1490-1500) included, is assembled below: each glyph's menu is rebuilt at every open
    // (ContextMenu.Opening, as the other rows' menus are) for the head the glyph was last built for.
    public HeadCardsRow(ChatThread parentSnapshot, IReadOnlyList<ChatThread> headSnapshots, SidebarMenuRequests menuRequests, Action onFold)
    {
        var state = new ViewState { Heads = headSnapshots };
        update = heads => state.Heads = heads;
        var connector = new HelperConnector(true, onFold) { Width = ThreadRowMetrics.ConnectorWidth }; // :1433-1437, endsHere: true, .frame(width: connectorWidth, ...)
        var connectorHeight = new AnimatedDouble(v => connector.Height = v); // :1439, :1541
        long? previousCardRows = null; // :1535 .animation(.snappy(duration: 0.18), value: cardRows)
        ValueList<Guid>? previousHeadIDs = null; // :1541 .animation(Chrome.panelSlide, value: heads.map(\.id))

        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = ThreadRowMetrics.HeadRowGap }; // :1465
        var card = new Border // :1516-1521
        {
            Padding = new Thickness(ThreadRowMetrics.HeadCardPadding),
            HorizontalAlignment = HorizontalAlignment.Stretch, // :1517 frame(maxWidth: .infinity, alignment: .leading)
            VerticalAlignment = VerticalAlignment.Top, // :1518 frame(alignment: .top)
            Child = rows,
        }.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous); // :1519-1521 background + clipShape
        card.Background = Chrome.Overlay(0.04).Brush(card); // :1520
        card.ClipToBounds = true; // :1521 .clipShape: while the card grows its lower rows show only as its edge reaches them
        // (2-chat-use.mov 5.33-5.53 s, CATALOGUE sidebar.activity-grid-expand)
        // PORT-ONLY (ENGINEERING DECISION ED-258 (2), UI-SIDEBAR-038 review, 2-chat-use.mov 5.33-5.40 s CATALOGUE sidebar.activity-grid-expand):
        // a view that leaves under SwiftUI's default .opacity transition no longer takes part in the layout the others
        // settle into, it fades where it stood while the card clips it. A control Avalonia keeps in its panel until it has
        // faded would hold its slot, and a whole leaving row would hold the chevron down and let it jump up at the end.
        // So a leaving glyph (or chevron) is lifted out of its row onto this canvas, at the place it was drawn, and fades
        // there; the layer sits over the card, the same size and clipped to the same rounded shape, and takes no input.
        var ghosts = new Canvas { IsHitTestVisible = false };
        var ghostLayer = new Border // the card's own area (:1516 padding, :1517-1518 frame, :1521 clipShape)
        {
            Padding = new Thickness(ThreadRowMetrics.HeadCardPadding),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            ClipToBounds = true,
            Child = ghosts,
        }.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        var cardHeightAnimated = new AnimatedDouble(v => { card.Height = v; ghostLayer.Height = v; }); // :1518, :1535

        var outer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") }; // :1440 HStack(spacing: 0)
        Grid.SetColumn(connector, 0);
        Grid.SetColumn(card, 1);
        Grid.SetColumn(ghostLayer, 1);
        outer.Children.Add(connector);
        outer.Children.Add(card);
        outer.Children.Add(ghostLayer);
        outer.SizeChanged += (_, e) => state.Width = e.NewSize.Width; // :1544 onGeometryChange(width)

        // The glyphs the last build drew, by their row's id and their own (:1466-1467), and its chevron (:1514) with the
        // symbol it drew (:1516).
        var drawnGlyphs = new Dictionary<(Guid Row, Guid Head), Control>();
        Control? drawnChevron = null;
        string? drawnChevronSymbol = null;

        // UI-SIDEBAR-036 (OD-0076): the glide of the glyphs and chevron that survive a change. SwiftUI keeps a view under its
        // identity (:1466-1467 the row's id and the glyph's own, :1514 the chevron) and moves it from its old place to its new one on
        // the animation in force (:1535 snappy for a change of cardRows, :1541 panelSlide for a change of the head ids). The
        // port builds fresh controls on every pass, so the move is made as a shift: each survivor is put back at the place it was
        // drawn (its old slot plus what was left of an earlier glide) and the shift eases to nothing. The slots are the card's own
        // arithmetic (x = column * (glyph + rowGap), y = row * (glyph + rowSpacing) under the card's padding), so no layout pass is waited for.
        var drawnSlots = new Dictionary<(Guid Row, Guid Head), Point>();
        Point? drawnChevronSlot = null;
        var glideShifts = new List<(TranslateTransform Shift, Vector Start)>();
        var glideStarts = new Dictionary<object, Vector>(); // by glyph key, or the chevron's marker
        var chevronKey = new object();
        var glideProgress = 1d; // 0 at the start of a glide, 1 when it is over
        var glide = new AnimatedDouble(progress =>
        {
            glideProgress = progress;
            foreach (var (shift, start) in glideShifts)
            {
                shift.X = start.X * (1 - progress);
                shift.Y = start.Y * (1 - progress);
            }
        });

        void Rebuild(ValueList<ChatThread> heads, double width, bool showsAllHeads, bool isHidden)
        {
            var capped = Capped(heads.Count); // :1442
            var visible = Visible(heads, showsAllHeads); // :1443
            var perRow = PerRow(width); // :1451
            var rowGap = RowGap(width, perRow); // :1456-1458
            var chunked = Chunk(visible, perRow); // :1545-1547
            var cardRows = CardRows(chunked.Count, capped); // :1461
            var cardHeight = ThreadRowMetrics.HeadCardHeight(cardRows); // :1305-1311

            // :1535 .animation(.snappy(duration: 0.18), value: cardRows) is attached to the card's own VStack, the
            // innermost modifier covering the card's height specifically; a bare row-count change (no ambient
            // transaction, e.g. a width-driven reflow) still animates the card with it. :1541
            // .animation(Chrome.panelSlide, value: heads.map(\.id)) wraps the outer HStack (connector and card
            // together), so a head-set change animates the connector too even when no explicit transaction is
            // active; otherwise the connector rides whatever ambient transaction is already current (an explicit
            // withAnimation elsewhere, e.g. the chevron's own snappy(duration: 0.18), or none). REVIEW FIX: both
            // heights were a bare property assignment before this, with no animation at all.
            var headIDs = new ValueList<Guid>(heads.Select(head => head.Id).ToList()); // :1541 heads.map(\.id)
            var headIDsChanged = previousHeadIDs is not null && !previousHeadIDs.Equals(headIDs);
            var cardRowsChanged = previousCardRows is not null && previousCardRows.Value != cardRows;
            connectorHeight.Set(cardHeight, headIDsChanged ? Chrome.PanelSlide : Transaction.Current); // :1439, :1541
            cardHeightAnimated.Set(cardHeight, cardRowsChanged ? Animation.Snappy(duration: 0.18) : Transaction.Current); // :1518, :1535
            var firstBuild = previousHeadIDs is null;
            previousHeadIDs = headIDs;
            previousCardRows = cardRows;

            // :1466-1467 ForEach(rows, id: \.first?.id) { ForEach(row, id: \.id) { glyph } } and :1514 `if capped { chevron }`:
            // a view that joins or leaves a parent that is already on screen runs SwiftUI's default .opacity transition under
            // the animation in force for the change (REVIEW, UI-SIDEBAR-038, 2-chat-use.mov 5.33-5.40 s: the glyphs past
            // twelve fade in on the chevron's snappy toggle and out again). A glyph's identity is its row's id and its own,
            // so one that moves to another row on a reflow leaves one and joins the other. That animation is the innermost
            // `.animation` whose value changed over the glyphs, :1535 snappy for a change of cardRows, then :1541 panelSlide
            // for a change of the head ids, otherwise the transaction the change came in (the chevron's own withAnimation,
            // :1515-1516); none at all when the change carries none, and none for the first build.
            var membership = firstBuild ? null
                : cardRowsChanged ? Animation.Snappy(duration: 0.18)
                : headIDsChanged ? Chrome.PanelSlide
                : Transaction.Current;
            var drawn = new HashSet<(Guid Row, Guid Head)>();
            foreach (var line in chunked)
                foreach (var head in line)
                    if (head.Hydra is not null) drawn.Add((line[0].Id, head.Id)); // :1471
            // :1516 `Image(systemName: showsAllHeads ? "chevron.up" : "chevron.down")`: on the chevron's own toggle the
            // down-chevron does not vanish. 2-chat-use.mov 5.340-5.378 s (REVIEW round 2): 17-27 ms after the press it is
            // still drawn, faint, exactly where it stood, and the up-chevron comes in under the grown card's last row; so the
            // symbol that goes leaves as a view that leaves does (on the ghost layer, where it stood) and the one that comes
            // joins as a view that joins does, under the same animation (OD-0076).
            var chevronSymbol = capped ? (showsAllHeads ? "chevron.up" : "chevron.down") : null;
            var chevronChanged = drawnChevron is not null && chevronSymbol != drawnChevronSymbol;
            if (membership is not null)
            {
                foreach (var glyph in drawnGlyphs.Where(pair => !drawn.Contains(pair.Key)).Select(pair => pair.Value).ToList()) Leave(glyph, membership);
                if (chevronChanged && drawnChevron is { } leavingChevron) Leave(leavingChevron, membership); // :1514 `capped` turned false, or :1516 its symbol changed
            }
            var previous = drawnGlyphs;
            var previousSlots = drawnSlots;
            var previousChevronSlot = drawnChevronSlot;
            var previousStarts = glideStarts;
            // How much of an earlier glide is still to run. Deliberately NOT clamped at 0: a spring overshoots, its progress passes 1, and the
            // glyph is then drawn past its slot by start * (1 - progress), a negative share; clamping it would put an interrupted glyph back at its
            // slot in one frame (a jump). HeadCardGlideTests.A_glide_interrupted_while_the_spring_overshoots_continues_from_where_the_glyph_is_drawn.
            var leftOfGlide = 1 - glideProgress;
            drawnGlyphs = [];
            drawnSlots = [];
            drawnChevronSlot = null;
            var chevronJoins = membership is not null && capped && (drawnChevron is null || chevronChanged);
            drawnChevron = null;
            drawnChevronSymbol = chevronSymbol;

            rows.Children.Clear();
            var lineIndex = 0;
            foreach (var line in chunked)
            {
                var columnIndex = 0;
                var lineHost = new StackPanel // :1467 HStack(spacing: rowGap)
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = rowGap,
                    Margin = new Thickness(0, 0, ThreadRowMetrics.HeadBadgeOverhang, 0), // :1512-1513
                };
                foreach (var head in line)
                {
                    if (head.Hydra is not { } info) continue; // :1471

                    var glyphHost = new Panel // :1476, the glyph and its selection disc share this frame
                    {
                        Width = ThreadRowMetrics.HeadGlyphSize,
                        Height = ThreadRowMetrics.HeadGlyphSize,
                        ClipToBounds = false, // the disc rides 3 pt past this frame, unclipped
                    };
                    if (Model.IsSelected(head.Id)) // :1479
                    {
                        var disc = new Avalonia.Controls.Shapes.Ellipse // :1480-1482 Circle().fill(...).padding(-3)
                        {
                            Width = ThreadRowMetrics.HeadGlyphSize + 6,
                            Height = ThreadRowMetrics.HeadGlyphSize + 6,
                            Fill = Chrome.Overlay(0.14).Brush(glyphHost),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        glyphHost.Children.Add(disc);
                    }
                    var mark = new HydraGlyph(info.Persona, ThreadRowMetrics.HeadGlyphSize, // :1477
                        isRunning: info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, status: info.HydraHeadInfoStatus);
                    glyphHost.Children.Add(mark);
                    var scale = new ScaleTransform(1, 1); // :1484 .scaleEffect(...), applied to the glyph+disc composite
                    glyphHost.RenderTransform = scale;
                    glyphHost.RenderTransformOrigin = RelativePoint.Center;
                    var animatedScale = new AnimatedScale(scale);

                    var headID = head.Id;
                    var button = PlainControls.Button(glyphHost, () => Model.SelectedThreadID = headID); // :1473-1475
                    button.OnHover(hovering => // :1486-1488
                    {
                        Transaction.WithAnimation(Chrome.Hover, () =>
                            state.HoveredID = hovering ? headID : (state.HoveredID == headID ? null : state.HoveredID)); // :1487
                        if (hovering) Model.WarmDocuments([headID]); // :1488
                    });
                    button.Track(() => state.HoveredID == headID, hovering => animatedScale.Set(hovering ? 1.18 : 1, Transaction.Current)); // :1484-1486
                    ToolTip.SetTip(button, $"{info.Persona.Name} · {info.Task}"); // :1490 "\(persona.name) · \(task)"
                    // :1493-1503 .contextMenu: the head's own thread, settleFirst false, re-evaluated fresh at every open
                    // (UI-SIDEBAR-037 "right-click opens the head's context menu with settleFirst false"); the same
                    // per-open-refresh foundation as the other rows' menus (ContextMenu.Opening).
                    var headSnapshot = head;
                    var headMenu = new ContextMenu();
                    headMenu.Opening += (_, _) =>
                    {
                        headMenu.Items.Clear();
                        foreach (var item in new RowActionMenuButtons(ThreadActions.MakeContextMenu(Model, headSnapshot, settleFirst: false, menuRequests)).Items) // :1498-1502
                            headMenu.Items.Add(item);
                    };
                    button.ContextMenu = headMenu;
                    AutomationProperties.SetName(button, $"{info.Persona.Name}, {info.Task}"); // :1499
                    var glyphKey = (line[0].Id, headID);
                    drawnGlyphs[glyphKey] = button;
                    drawnSlots[glyphKey] = new Point(
                        ThreadRowMetrics.HeadCardPadding + columnIndex * (ThreadRowMetrics.HeadGlyphSize + rowGap),
                        ThreadRowMetrics.HeadCardPadding + lineIndex * (ThreadRowMetrics.HeadGlyphSize + ThreadRowMetrics.HeadRowGap));
                    columnIndex++;
                    if (membership is not null && !previous.ContainsKey(glyphKey)) Join(lineHost, button, membership);
                    else lineHost.Children.Add(button);
                }
                rows.Children.Add(lineHost);
                lineIndex++;
            }
            if (capped) // :1514
            {
                var chevronGlyph = SwiftImage.SystemName(chevronSymbol!) // :1516
                    .Font(SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Semibold)); // :1517
                chevronGlyph.ForegroundStyle(Chrome.SecondaryText); // :1518
                chevronGlyph.Width = ThreadRowMetrics.HeadGlyphSize; // :1519
                chevronGlyph.Height = ThreadRowMetrics.HeadGlyphSize;
                chevronGlyph.HorizontalAlignment = HorizontalAlignment.Center;
                chevronGlyph.VerticalAlignment = VerticalAlignment.Center;
                var chevronHost = new Border // :1519 .contentShape(Rectangle())
                {
                    Width = ThreadRowMetrics.HeadGlyphSize,
                    Height = ThreadRowMetrics.HeadGlyphSize,
                    Background = Brushes.Transparent,
                    Child = chevronGlyph,
                };
                var chevronButton = PlainControls.Button(chevronHost, () => // :1515-1516
                    Transaction.WithAnimation(Animation.Snappy(duration: 0.18), () => state.ShowsAllHeads = !state.ShowsAllHeads));
                ToolTip.SetTip(chevronButton, ChevronTooltip(showsAllHeads, heads.Count)); // :1526
                chevronButton.HorizontalAlignment = HorizontalAlignment.Left; // :1465 VStack(alignment: .leading): the 14 pt button sits at the leading edge
                drawnChevron = chevronButton;
                drawnChevronSlot = new Point(
                    ThreadRowMetrics.HeadCardPadding,
                    ThreadRowMetrics.HeadCardPadding + lineIndex * (ThreadRowMetrics.HeadGlyphSize + ThreadRowMetrics.HeadRowGap));
                if (chevronJoins && membership is not null) Join(rows, chevronButton, membership); // :1514 the chevron joins as `capped` turns true, :1516 or as its symbol changes
                else rows.Children.Add(chevronButton);
            }
            // The glide of the survivors (see glideShifts): a glyph or chevron of the last pass that is drawn again moves from
            // where it was drawn to its new slot, on the animation in force for the change. A change that carries none cuts.
            var shifts = new List<(TranslateTransform Shift, Vector Start)>();
            var starts = new Dictionary<object, Vector>();
            void Survives(object key, Control now, Point? was, Point? to)
            {
                if (membership is null || was is not { } from || to is not { } target) return;
                var drawnAt = from + (previousStarts.TryGetValue(key, out var earlier) ? earlier * leftOfGlide : default);
                var start = new Vector(drawnAt.X - target.X, drawnAt.Y - target.Y);
                if (start.Length < 0.01) return;
                var shift = new TranslateTransform(start.X, start.Y);
                now.RenderTransform = shift;
                shifts.Add((shift, start));
                starts[key] = start;
            }
            foreach (var (key, slot) in drawnSlots)
                if (previousSlots.TryGetValue(key, out var was)) Survives(key, drawnGlyphs[key], was, slot);
            if (drawnChevron is not null && !chevronChanged) Survives(chevronKey, drawnChevron, previousChevronSlot, drawnChevronSlot);
            glideShifts = shifts;
            glideStarts = starts;
            if (shifts.Count == 0) glide.Set(1, null);
            else
            {
                glide.Set(0, null);
                glide.Set(1, membership);
            }
            Opacity = isHidden ? 0 : 1; // :1543
        }

        // The animation's own transition: SwiftUI's default .opacity, with no .transition modifier on any of these views.
        // The animation is the one in force for the change (Rebuild's `membership`), whatever transaction is current here.
        static void Join(Panel host, Control child, Animation animation) =>
            Transaction.WithAnimation(animation, () => SwiftTransition.Insert(host, host.Children.Count, child));

        // A leaving glyph or chevron is lifted out of its row onto the ghost layer where it was drawn and fades there.
        void Leave(Control child, Animation animation)
        {
            var container = SwiftTransition.Container(child);
            if (container.GetVisualParent() is not Panel host || child.TranslatePoint(default, ghosts) is not { } at) return; // not laid out: it just goes
            host.Children.Remove(container);
            Canvas.SetLeft(container, at.X);
            Canvas.SetTop(container, at.Y);
            SwiftTransition.Place(ghosts, ghosts.Children.Count, child);
            Transaction.WithAnimation(animation, () => SwiftTransition.Remove(ghosts, child));
        }

        outer.Track(() => (
            SidebarView.OrderedHeads(state.Heads.Select(head => Model.Thread(head.Id) ?? head).ToList()), // :1441
            state.Width,
            state.ShowsAllHeads,
            RowGlideAnimator.Shared.Current.IsHiding(parentSnapshot.Id), // :1543
            Model.SelectedThreadID), // :1479 model.isSelected(head.id), read per glyph inside Rebuild; this signal
            // registers the dependency so a selection change (elsewhere in the sidebar) re-renders this card's rings
            value => Rebuild(value.Item1, value.Item2, value.Item3, value.Item4));

        Child = outer;
    }

    // :5 @Environment(AppModel.self) private var model (this file's own convention: SidebarView.cs:337)
    private AppModel Model => AppEnvironment.GetAppModel(this);

    // :1545-1547 private static func chunk(_ heads: [ChatThread], by size: Int) -> [[ChatThread]]. Part 5. PR-SIDEBAR-119.
    private static ValueList<ValueList<ChatThread>> Chunk(IReadOnlyList<ChatThread> heads, long by)
    {
        var size = checked((int)by);
        var rows = new List<ValueList<ChatThread>>();
        for (var start = 0; start < heads.Count; start += size) // :1546 stride(from: 0, to: heads.count, by: size)
            rows.Add(new ValueList<ChatThread>(heads.Skip(start).Take(size).ToList())); // :1546 Array(heads[$0 ..< min($0+size, heads.count)])
        return new ValueList<ValueList<ChatThread>>(rows);
    }

    // :1450 let cardInner = ...; let usable = cardInner - headBadgeOverhang. Part 5. PR-SIDEBAR-117, PR-SIDEBAR-118.
    private static double Usable(double width) =>
        width - ThreadRowMetrics.ConnectorWidth - 2 * ThreadRowMetrics.HeadCardPadding - ThreadRowMetrics.HeadBadgeOverhang;

    // :1451 let perRow = width == 0 ? 7 : max(1, Int((usable + headGlyphPitch - headGlyphSize) / headGlyphPitch)).
    // Part 5. PR-SIDEBAR-117: seven glyphs before the row is measured, otherwise the floor of the fit, clamped to at
    // least one. `(long)` on a non-negative double truncates toward zero exactly as Swift's `Int(...)` does.
    internal static long PerRow(double width)
    {
        if (width == 0) return 7; // :1451
        return Math.Max(1, (long)((Usable(width) + ThreadRowMetrics.HeadGlyphPitch - ThreadRowMetrics.HeadGlyphSize) / ThreadRowMetrics.HeadGlyphPitch));
    }

    // :1456-1458 let tightGap = ...; let rowGap = perRow > 1 && width > 0 ? max(tightGap, ...) : tightGap. Part 5.
    // PR-SIDEBAR-118.
    internal static double RowGap(double width, long perRow)
    {
        var tightGap = ThreadRowMetrics.HeadGlyphPitch - ThreadRowMetrics.HeadGlyphSize; // :1456, 22 - 14 = 8
        if (!(perRow > 1 && width > 0)) return tightGap; // :1458
        return Math.Max(tightGap, (Usable(width) - perRow * ThreadRowMetrics.HeadGlyphSize) / (perRow - 1)); // :1457
    }

    // :1442-1443 let capped = heads.count > 12; let visible = showsAllHeads ? heads : Array(heads.prefix(12)). Part 5.
    // PR-SIDEBAR-119.
    internal static bool Capped(long headCount) => headCount > 12; // :1442

    internal static ValueList<ChatThread> Visible(IReadOnlyList<ChatThread> heads, bool showsAllHeads) => // :1443
        showsAllHeads ? new ValueList<ChatThread>(heads) : new ValueList<ChatThread>(heads.Take(12).ToList());

    // :1461 let cardRows = rows.count + (capped ? 1 : 0). Part 5. PR-SIDEBAR-119.
    internal static long CardRows(long rowCount, bool capped) => rowCount + (capped ? 1 : 0);

    // :1526 .help(showsAllHeads ? "Show fewer heads" : "Show all \(heads.count) heads"). Part 5. PR-SIDEBAR-119.
    internal static string ChevronTooltip(bool showsAllHeads, long headCount) =>
        showsAllHeads ? "Show fewer heads" : $"Show all {headCount} heads";
}

/// <summary>:1552-1618 <c>private struct HelperStubRow: View</c>: a parent's folded helpers as one line.</summary>
internal sealed class HelperStubRow : Decorator
{
    /// <param name="parentSnapshot">:1555 <c>let parentSnapshot: ChatThread</c>.</param>
    /// <param name="helperSnapshots">:1558 <c>let helperSnapshots: [ChatThread]</c>.</param>
    /// <param name="action">:1559 <c>let action: () -> Void</c>.</param>
    // :1568-1617 body. Part 5. PR-SIDEBAR-113, PR-SIDEBAR-114, PR-SIDEBAR-115.
    private AppModel Model => AppEnvironment.GetAppModel(this); // :1553 @Environment(AppModel.self) private var model

    public HelperStubRow(ChatThread parentSnapshot, IReadOnlyList<ChatThread> helperSnapshots, Action action)
    {
        var state = new ViewState();

        var connector = new Avalonia.Controls.Shapes.Path // :1583-1584 HelperConnectorShape(endsHere: true).stroke(...)
        {
            StrokeThickness = 1, // :1642 (HelperConnector.style) lineWidth: 1
            StrokeLineCap = PenLineCap.Round, // :1642 lineCap: .round
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>(HelperConnector.Dash), // :1642 dash: [0.1, 3.6]
            Width = ThreadRowMetrics.ConnectorWidth, // :1584 .frame(width: connectorWidth, height: helperHeight)
            Height = ThreadRowMetrics.HelperHeight,
        };
        // :1583 endsHere: true, in the 28 x 24 frame of :1584 (a Path with no Data measures to nothing, so drawing it on
        // its own SizeChanged never happened: the stub showed no guide at all beside 2-chat-use.mov 162 s's dots).
        connector.Data = new HelperConnectorShape(true).Path(new Rect(0, 0, ThreadRowMetrics.ConnectorWidth, ThreadRowMetrics.HelperHeight));

        var chevron = SwiftImage.SystemName("chevron.right").Font(SwiftFont.System(8, weight: SwiftFont.Weight.Bold)); // :1586-1587
        var label = new TextBlock { TextWrapping = TextWrapping.NoWrap }; // :1588-1589
        SwiftFont.Subheadline.Apply(label); // :1590

        var spinner = NthWorkingMarks.Working(parentSnapshot.Id.ToString(), hydra: helperSnapshots.All(helper => helper.IsHydraHead)); spinner.Margin = new Thickness(0, 0, 4, 0); spinner.HorizontalAlignment = HorizontalAlignment.Right; // :1592-1594, the quiet working beads (Calculi WU6); they begin when the row shows them
        var leading = new StackPanel // :1585 HStack(spacing: 5) { chevron; label; Spacer(minLength: 4); [spinner] }
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, // :1585 an HStack centres each child on the row's height
            Children = { chevron, label },
        };
        var contentRow = new DockPanel { LastChildFill = true, Children = { leading } }; // spinner docks Right when working

        var shape = new Border // :1580, :1605-1606 shape.fill(...); .contentShape(shape)
        {
            Padding = new Thickness(6, 0, Chrome.RowHorizontalPadding, 0), // :1600-1601 padding(.leading, 6) / (.trailing, rowHorizontalPadding)
            Height = ThreadRowMetrics.HelperHeight, // :1602
            HorizontalAlignment = HorizontalAlignment.Stretch, // :1603 frame(maxWidth: .infinity, alignment: .leading)
            Background = Brushes.Transparent,
            Child = contentRow,
        }.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous); // :1580 RoundedRectangle(cornerRadius:, style: .continuous)
        var fill = new AnimatedColor(shape);
        shape.Background = fill.Brush;
        var stroke = new AnimatedColor(connector);
        connector.Stroke = stroke.Brush;
        var chevronColor = new AnimatedColor(chevron);
        chevron.Foreground = chevronColor.Brush;
        var labelColor = new AnimatedColor(label);
        label.Foreground = labelColor.Brush;

        // :1581 HStack(spacing: 0). A horizontal StackPanel arranges every child at its own DesiredSize along the
        // stack axis (HorizontalAlignment.Stretch is a no-op there), so shape's :1603 frame(maxWidth: .infinity)
        // would not actually reach the row's remaining width; Grid Auto,* is this file's own idiom for a fixed
        // leading column plus a flexible trailing one (ThreadRowFace, :2022, ThreadRowFace's `row`; REVIEW FIX).
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(connector, 0);
        Grid.SetColumn(shape, 1);
        row.Children.Add(connector);
        row.Children.Add(shape);
        shape.VerticalAlignment = VerticalAlignment.Stretch;

        var button = PlainControls.Button(row, action); // :1582 Button(action: action)
        button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :1611-1613 .onHover

        button.Track(() =>
        {
            var helpers = helperSnapshots.Select(helper => Model.Thread(helper.Id) ?? helper).ToList(); // :1569 model.thread($0.id) ?? $0
            var count = helpers.Count; // :1570
            var working = helpers.Any(helper => // :1571-1574
            {
                var runtime = Model.ExistingRuntime(helper.Id);
                return runtime?.IsRunning == true || runtime?.IsHydraMerging == true;
            });
            // :1577-1578 allSatisfy on an empty array is true (LINQ .All() agrees), so a helper-less set (which
            // cannot reach this row in practice) would read "head"; reproduced as found (PR-SIDEBAR-113).
            var noun = helpers.All(helper => helper.IsHydraHead) ? "head" : "helper";
            var isHidden = RowGlideAnimator.Shared.Current.IsHiding(parentSnapshot.Id); // :1580, :1617
            return (Count: count, Working: working, Noun: noun, IsHidden: isHidden, state.IsHovering);
        }, value =>
        {
            var (count, working, noun, isHidden, hovering) = value;
            label.Text = count == 1 ? $"1 {noun}" : $"{count} {noun}s"; // :1591
            // :1611-1613 withAnimation(Chrome.hover) { isHovering = hovering }: everything isHovering drives eases with it,
            // the stroke (:1583), the label and chevron's foreground (:1596) and the fill (:1607).
            stroke.Set(HelperConnector.Color(hovering), Transaction.Current); // :1583 HelperConnector.color(hovering:)
            var foreground = Chrome.SecondaryText.Opacity(hovering ? 1 : 0.85); // :1596
            chevronColor.Set(foreground, Transaction.Current);
            labelColor.Set(foreground, Transaction.Current);
            fill.Set(hovering ? Chrome.Overlay(0.06) : SwiftColor.Clear, Transaction.Current); // :1607
            if (working) // :1592-1594
            {
                spinner.SetState(noun == "head" ? NthBeadsState.Hydra : NthBeadsState.Tool);
                if (!contentRow.Children.Contains(spinner))
                {
                    DockPanel.SetDock(spinner, Dock.Right);
                    contentRow.Children.Add(spinner);
                }
                spinner.Begin();
            }
            else
            {
                spinner.End();
                contentRow.Children.Remove(spinner);
            }
            var tip = count == 1 ? $"Show the {noun}" : $"Show {count} {noun}s"; // :1615
            ToolTip.SetTip(button, tip);
            var a11y = count == 1 ? $"1 folded {noun}" : $"{count} folded {noun}s"; // :1616
            AutomationProperties.SetName(button, a11y);
            button.Opacity = isHidden ? 0 : 1; // :1617
        });
        Child = button;
    }

    // :1561 @State private var isHovering = false
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }
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
        content.RenderTransform = new TranslateTransform(0, Offset); // :1629 .offset(y:)
        content.ZIndex = ZIndex; // :1630 .zIndex()
        return content;
    }

    private bool Rides => ParentID is not null && Drag.Id == ParentID; // :1627

    public double Offset => Rides ? Drag.VisualOffset : 0; // :1629

    public int ZIndex => Rides ? 1 : 0; // :1630
}

/// <summary>:1636-1660 <c>private struct HelperConnector: View</c>: the dotted connector beside a helper's row.
/// Part 4. PR-SIDEBAR-110.</summary>
internal sealed class HelperConnector : Decorator
{
    // :1642 static let style = StrokeStyle(lineWidth: 1, lineCap: .round, dash: [0.1, 3.6]). Internal: HelperStubRow
    // (part 5) draws the identical dashed stroke on its own ending connector and reuses this constant, as :1583 in
    // the Swift reuses `HelperConnector.style` directly.
    internal static readonly double[] Dash = [0.1, 3.6];

    /// <param name="endsHere">:1637 <c>let endsHere: Bool</c>.</param>
    /// <param name="action">:1638 <c>let action: () -> Void</c>.</param>
    // :1648-1659 body
    public HelperConnector(bool endsHere, Action action)
    {
        var state = new ViewState();
        this.endsHere = endsHere;
        var stroke = new Avalonia.Controls.Shapes.Path // :1649 HelperConnectorShape(endsHere: endsHere).stroke(...)
        {
            StrokeThickness = 1, // :1642 lineWidth: 1
            StrokeLineCap = PenLineCap.Round, // :1642 lineCap: .round
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double>(Dash), // :1642 dash: [0.1, 3.6]
        };
        this.stroke = stroke;
        var fill = new AnimatedColor(stroke);
        stroke.Stroke = fill.Brush;
        stroke.SizeChanged += (_, e) => stroke.Data = new HelperConnectorShape(this.endsHere).Path(new Rect(e.NewSize)); // :1649-1651
        stroke.Track(() => state.IsHovering, hovering => fill.Set(Color(hovering), Transaction.Current)); // :1650 .animation(Chrome.hover, value:)
        // :1651 .contentShape(.rect): the whole rect is the hit area, not just the sparse dashed stroke. REVIEW FIX
        // (part 4): IsHitTestVisible alone does not expand hit-testing past whatever paints; Background =
        // Transparent is what actually makes the empty space hit-testable, the established pattern this same file
        // already uses for its own check glyph (glyphHost, below) and Chrome.cs (e.g. :577, :812, :875, :1195, :1448).
        var label = new Border { Child = stroke, IsHitTestVisible = true, Background = Brushes.Transparent };
        var button = PlainControls.Button(label, action); // :1648 Button(action:)
        // :1649-1651 the shape fills the frame its caller gives (28 x 24 beside a helper, 28 x the card's height beside
        // a head card). A Path with no Data measures to nothing and never changes size, so its own SizeChanged never
        // drew it: the shape is drawn in the connector's own rect instead.
        SizeChanged += (_, e) =>
        {
            stroke.Width = e.NewSize.Width;
            stroke.Height = e.NewSize.Height;
            stroke.Data = new HelperConnectorShape(this.endsHere).Path(new Rect(e.NewSize));
        };
        button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :1656 .onHover
        ToolTip.SetTip(button, "Hide helpers"); // :1657 .help("Hide helpers")
        AutomationProperties.SetName(button, "Hide helpers"); // :1658 .accessibilityLabel(Text("Hide helpers"))
        Child = button;
    }

    private bool endsHere;
    private readonly Avalonia.Controls.Shapes.Path stroke;

    /// <summary>:1637 <c>let endsHere: Bool</c>, handed in anew by a row the list keeps (SidebarHelperRow.Update): the
    /// shape is drawn again for it, the stem to the row's middle on the last helper and through the row otherwise.</summary>
    internal bool EndsHere
    {
        get => endsHere;
        set
        {
            if (endsHere == value) return;
            endsHere = value;
            if (Bounds.Width > 0 && Bounds.Height > 0) stroke.Data = new HelperConnectorShape(value).Path(new Rect(Bounds.Size));
        }
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
internal sealed class SidebarThreadRow : Decorator, IGlideRow
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
    // :1709-1714 Local row state and its unobserved geometry. The model's per-thread cell supplies live values.
    private sealed class ViewState : MainActorModel
    {
        private bool hovering, menuPresented, settling;
        public bool IsHovering { get => Read(hovering); set => Set(ref hovering, value); }
        public bool IsMenuPresented { get => Read(menuPresented); set => Set(ref menuPresented, value); }
        public bool IsSettling { get => Read(settling); set => Set(ref settling, value); }
    }

    private readonly ViewState state = new();
    private readonly FrameHolder windowFrame = new();
    private readonly Border shape = new();
    private readonly Border pinnedOverlay = new() { IsHitTestVisible = false };
    private readonly Panel overlays = new();
    private readonly Button selectButton;
    private AppModel Model => AppEnvironment.GetAppModel(this); // :1683

    // PORT-ONLY (ED-NEW-session-hotkeys): the selection glide's hold on this row, how the row repaints its fill for it, and the
    // row's hold-Ctrl number badge.
    private GlideHold glideHold;
    private Action? repaintFill;
    private SelectionGlide? glide;
    private readonly Guid threadID;
    internal SessionKeycap Keycap { get; }

    Guid IGlideRow.ThreadID => threadID;

    Control IGlideRow.FillShape => shape;

    void IGlideRow.SetGlideHold(GlideHold hold)
    {
        if (glideHold == hold) return;
        glideHold = hold;
        repaintFill?.Invoke();
    }

    // :1716-1872 body; :1899-1901 makeActions. The live context menu (Job SB, ROUND3.md item 3) is at this
    // constructor's own ContextMenu.Opening wiring, below.
    public SidebarThreadRow(
        ChatThread snapshot,
        string? projectName,
        ProjectIcon? projectIcon,
        SidebarMenuRequests menuRequests,
        Action onRename,
        Action onDelete,
        bool isDragged = false,
        FrameHolder? listFrame = null,
        Action? onToggleFold = null)
    {
        ChatThread Thread() => Model.Thread(snapshot.Id) ?? snapshot; // :1689
        void SettleRow() => Settle(Model, Thread(), state.IsSettling, Model.Settings.SettleSound, value => state.IsSettling = value); // :1904-1910
        void ReopenRow() => Reopen(Model, Thread(), () =>
        {
            if (listFrame is null) return; // :1934
            GlideAtScale(RowGlideAnimator.Shared.Current, RowGlideAnimator.Glide.Direction.Reopen, listFrame.Frame,
                windowFrame.Frame, Thread(), projectName, projectIcon, Model.IsSelected(snapshot.Id), state.IsHovering,
                Model.ExistingRuntime(snapshot.Id), ActualThemeVariant, TopLevel.GetTopLevel(this)?.RenderScaling ?? 2); // :1933-1978
        });
        void ArchiveRow() => ArchiveWithGenie(GenieAnimator.Shared.Current, Model, Thread(), projectName, projectIcon,
            Model.IsSelected(snapshot.Id), Model.ExistingRuntime(snapshot.Id), windowFrame.Frame, ActualThemeVariant); // :1988-1999
        IReadOnlyList<RowAction> MakeActions() => ThreadActions.Make(Model, Thread(), onRename, onDelete, SettleRow, ReopenRow); // :1899-1901

        shape.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        pinnedOverlay.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
        var fill = new AnimatedColor(shape);
        shape.Background = fill.Brush;
        selectButton = new ThreadButton(() => Model.IsSelected(snapshot.Id), () => Thread().IsSettled)
        {
            Content = new Panel { Children = { shape, pinnedOverlay } },
            Template = new FuncControlTemplate<Button>((button, scope) => new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = button[!ContentControl.ContentProperty],
            }.RegisterInNameScope(scope)),
        };
        selectButton.Click += (_, _) => SelectThread(Model, Thread()); // :1736-1738
        overlays.Children.Add(selectButton);
        threadID = snapshot.Id;
        Control? check = null, trailing = null, fold = null;
        ThreadRowFace? face = null; // the ONE face the row draws (:1739-1743), built once and updated in place
        var numberedDetailed = false;
        // PORT-ONLY (ED-NEW-session-hotkeys): the number badge sits over the row's leading mark (its icon or badge; a settled
        // row's check), or over the trailing status of a row whose title starts at its edge (the detailed rows).
        Keycap = new SessionKeycap(snapshot.Id,
            () => numberedDetailed ? [trailing] : face?.LeadingMark is { } mark ? [mark] : [check],
            selectButton);
        overlays.Children.Add(Keycap);
        AttachedToVisualTree += (_, _) =>
        {
            glide = this.FindAncestorOfType<SidebarView>()?.Glide;
            glide?.Register(this);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            glide?.Unregister(this);
            glide = null;
        };
        // PORT-ONLY (ED-NEW-ignis-guards): the Ignis keep-warm mark (its ember line at the leading edge, its hover zone just
        // before the trailing content), over the face and under the trailing buttons; null (unbound) keeps the row as it was.
        Ember = Marrow.App.Port.Ignis.Views.IgnisSidebar.MarkFor(snapshot.Id, () => SelectThread(Model, Thread()));
        if (Ember is not null) overlays.Children.Add(Ember);
        Child = ApplyDraggedLift(overlays, () => isDragged); // :1838-1849
        var menuPresented = new Binding<bool>(() => state.IsMenuPresented, value => state.IsMenuPresented = value);
        ChatThread shown = snapshot; // the thread the face is told of, for the badge it builds
        bool? selectedBefore = null, actionsBefore = null;
        // ED-NEW-lean-15: what the trailing and fold overlays were last built from; an apply that changes neither keeps them.
        ChatThread? trailingThread = null;
        bool trailingActions = false, trailingSettles = false, foldShows = false, foldFolded = false, overlaysBuilt = false;
        var tail = new Control?[3];
        this.Track(() =>
        {
            var thread = Thread();
            var selected = Model.IsSelected(thread.Id); // :1721
            var hover = state.IsHovering;
            var actions = (hover || state.IsMenuPresented) && !isDragged; // :1724
            return (thread, selected, hover, actions, settling: state.IsSettling,
                settles: Model.Settings.ThreadFinishAction == ThreadFinishAction.Settle,
                hidden: RowGlideAnimator.Shared.Current.IsHiding(thread.Id),
                mergeStage: MergeStage(Model.ExistingRuntime(thread.Id))); // :1735
        }, value =>
        {
            var (thread, selected, hover, actions, settling, settles, hidden, mergeStage) = value;
            var settled = thread.IsSettled;
            var detailed = projectName is not null && !settled;
            var showsFold = actions && onToggleFold is not null;
            // :1739-1749. ONE face under the row's identity (lead ruling 2026-10-04, ED-291 (1)): SwiftUI keeps
            // the view and re-reads its inputs, so the thread, the selection, the merge stage and the clearance are handed to
            // the face in place and every animated property (the padding on the hover's transaction, :2068) eases on from
            // where it stands; the face is never rebuilt for them.
            var clearance = TrailingClearance(settled, detailed, actions || settling, showsFold);
            shown = thread;
            if (face is null)
            {
                face = new ThreadRowFace(thread, projectName, selected, clearance, () => new ThreadBadge(shown), projectIcon, mergeStage);
                shape.Child = face;
                if (Ember is { } ember)
                {
                    var built = face;
                    built.ClearanceApplied = value => PlaceEmber(ember, built, value);
                    ember.TitleFadeChanged += () => FadeTitle(ember, built);
                    built.SizeChanged += (_, _) => FadeTitle(ember, built);
                    PlaceEmber(ember, built, clearance);
                }
            }
            else
            {
                face.Update(thread, selected, mergeStage, clearance, Transaction.Current);
            }
            // PORT-ONLY (ED-NEW-session-hotkeys): while the selection glides, the glide draws the selection fill and the row
            // repaints at once for it; at rest the row draws its own exactly as the source does.
            void PaintFill(Animation? animation)
            {
                var colors = GlideFill(thread.IsPinned, selected, hover, glideHold);
                fill.Set(colors.Background, animation);
                pinnedOverlay.Background = colors.PinnedOverlay?.Brush(pinnedOverlay); // :1756-1758
            }
            repaintFill = () => PaintFill(null);
            PaintFill(glideHold != GlideHold.None ? null
                : selectedBefore is not null && selectedBefore != selected ? Chrome.Hover : Transaction.Current); // :1750-1754
            selectedBefore = selected;
            // :1816-1817 the hover flips `showsActions` inside withAnimation(Chrome.hover): the branches that come and go
            // with it (the check, the trailing buttons or status or time, the fold chevron) leave and join on SwiftUI's
            // default .opacity transition, so they crossfade (2-chat-use.mov 70.74-71.30 s). Any other rebuild is an
            // update of the same branch and replaces in place.
            var hoverFlip = actionsBefore is { } before && before != actions && Transaction.Current is not null;
            actionsBefore = actions;
            var tailCount = 0;
            void Swap(ref Control? slot, Control? next, bool branchChanged)
            {
                var crossfade = hoverFlip && branchChanged;
                if (slot is not null)
                {
                    if (crossfade) SwiftTransition.Remove(overlays, slot);
                    else overlays.Children.Remove(SwiftTransition.Container(slot));
                }
                slot = next;
                if (next is null) return;
                if (crossfade) SwiftTransition.Insert(overlays, overlays.Children.Count, next);
                else overlays.Children.Add(next);
                tail[tailCount++] = SwiftTransition.Container(next);
            }
            void Keep(Control? slot)
            {
                if (slot is not null) tail[tailCount++] = SwiftTransition.Container(slot);
            }
            // :1763-1771. A check the row already shows for the same state is kept and told whether it pops (SwiftUI keeps
            // the view under its identity and re-reads `isPopping`), so the pop (:1909) is the spring it rides.
            if (ShowsCheck(settled, settles, actions, settling) && check is SettleCheck kept && kept.IsSettled == settled)
            {
                kept.SetPopping(settling);
            }
            else
            {
                var nextCheck = BuildSettleCheckOverlay(settled, settles, actions, settling, settling, settled ? ReopenRow : SettleRow); // :1763-1771
                Swap(ref check, nextCheck, (check is null) != (nextCheck is null)); // a settled row keeps its check: same branch
            }
            // ED-NEW-lean-15: the trailing overlay reads the thread, whether the hover's buttons show and the finish action (its
            // other inputs are this row's constants), so an apply that changes none of them (a selection, a merge stage, the
            // settle pop, the ghost hiding the row) keeps the overlay it has instead of building an equal one in its place.
            if (!overlaysBuilt || actions != trailingActions || settles != trailingSettles || !Equals(thread, trailingThread))
            {
                Swap(ref trailing, BuildTrailingOverlay(thread, actions, detailed, settled, projectIcon is not null, settles, ArchiveRow,
                    actions ? MakeActions() : [], menuPresented), branchChanged: true); // :1772-1801 the hover always changes the branch
                (trailingThread, trailingActions, trailingSettles) = (thread, actions, settles);
            }
            else Keep(trailing);
            if (!overlaysBuilt || showsFold != foldShows || thread.FoldsHelpers != foldFolded)
            {
                var nextFold = BuildFoldOverlay(showsFold, onToggleFold, thread.FoldsHelpers); // :1804-1814
                Swap(ref fold, nextFold, (fold is null) != (nextFold is null));
                (foldShows, foldFolded) = (showsFold, thread.FoldsHelpers);
            }
            else Keep(fold);
            overlaysBuilt = true;
            // A rebuilt overlay joins the end of the stack, in this order (check, trailing, fold); a kept one takes the same place,
            // so what draws over what is as a rebuild would leave it.
            var stack = overlays.Children;
            var ordered = tailCount <= stack.Count;
            for (var i = 0; ordered && i < tailCount; i++) ordered = ReferenceEquals(stack[stack.Count - tailCount + i], tail[i]);
            for (var i = 0; !ordered && i < tailCount; i++)
            {
                var at = stack.IndexOf(tail[i]!);
                if (at >= 0 && at != stack.Count - 1) stack.Move(at, stack.Count - 1);
            }
            Array.Clear(tail);
            SetHidden(hidden); // :1834-1838
            AutomationProperties.SetName(selectButton, thread.Title); // :1871-1872
            PlaceKeycap(settled, detailed);
            numberedDetailed = detailed;
            Keycap.Refresh(); // what it covers may have been rebuilt
        });
        overlays.OnHover(hovering => // :1816 hover includes the overlaid check and accessory buttons
        {
            Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering); // :1816-1818
            if (hovering) NoteHover(Model, Thread(), hovering); // :1819 `if hovering { ... }`: the model is read only then, so the exit a row sends as it leaves the tree (the pointer was over it) never asks a detached row for it
            ReportFrame();
        });
        void ReportFrame()
        {
            var animator = RowGlideAnimator.Shared.Current;
            if (!isDragged && !state.IsHovering && animator.Glides.Count == 0) return; // :1827
            if (TopLevel.GetTopLevel(this) is not { } window || this.TranslatePoint(default, window) is not { } origin) return;
            var frame = new Rect(origin, Bounds.Size); // :1903-1905 window space, not the list space
            windowFrame.Note(frame); // :1828,2433-2440 suppress only the holder write, never the following Land
            animator.Land(Thread().Id, snapshot.IsSettled, frame); // :1830 snapshot describes this row's destination
        }
        // ED-NEW-lean-15: one LayoutUpdated subscriber per window for all its thread rows (FrameReports), not one per row.
        FrameReports.Entry? frameReport = null;
        AttachedToVisualTree += (_, _) => frameReport ??= TopLevel.GetTopLevel(this) is { } top ? FrameReports.Join(top, ReportFrame) : null;
        DetachedFromVisualTree += (_, _) => { frameReport?.Leave(); frameReport = null; };
        OnChange<AppModel.FinishRequest?>? finish = null;
        AttachedToVisualTree += (_, _) => finish ??= new OnChange<AppModel.FinishRequest?>(() => Model.AppModelFinishRequest,
            (_, _) => FinishRequestReceived(Model, Thread(), Thread().IsSettled, SettleRow, ReopenRow)); // :1863-1870
        DetachedFromVisualTree += (_, _) => { finish?.Dispose(); finish = null; StopReveal(); };
        // :1848-1862 .contextMenu: snapshot-only, re-evaluated fresh every open. ENGINEERING DECISION (closes STOP #7
        // for SidebarThreadRow, ROUND3.md item 3, the same per-open-refresh foundation ProjectRow's own constructor
        // uses, this file's :1886-1899, full citation there): Avalonia's ContextMenu.Opening rebuilds Items from a
        // live Thread()/Model read on every open; Control.ContextMenu opens it on a real right-click via Avalonia's
        // own ContextRequestedEvent, the Windows counterpart of AppKit's right-click-opens-NSMenu.
        var contextMenu = new ContextMenu();
        contextMenu.Opening += (_, _) =>
        {
            var threadSnapshot = Thread(); // :1852, :1689
            var settleFirstSnapshot = !threadSnapshot.IsHelper && Model.Settings.ThreadFinishAction == ThreadFinishAction.Settle; // :1855
            contextMenu.Items.Clear();
            foreach (var item in new RowActionMenuButtons(WithIgnis(ThreadActions.MakeContextMenu(Model, threadSnapshot, settleFirstSnapshot, menuRequests))).Items)
                contextMenu.Items.Add(item);
        };
        ContextMenu = contextMenu; // :1851 .contextMenu { ... }; PR-SIDEBAR-098 through -103's live-menu half
    }

    /// <summary>PORT-ONLY (ED-NEW-ignis-guards): the row's Ignis keep-warm mark; null when Ignis is not bound.</summary>
    internal Marrow.App.Port.Ignis.Views.IgnisSidebarMark? Ember { get; }

    // ED-NEW-lean-15: the thread rows' frame reports, one LayoutUpdated subscriber per window. Avalonia raises a control's
    // LayoutUpdated from its layout manager's pass event (a Layoutable subscribes itself there for its first handler), so a list of
    // N rows meant N subscribers called after every layout pass. Here the window carries one, and it calls the rows' reports in the
    // order the rows attached, the order their own subscriptions had; the rows taking part in a pass are the ones attached when it
    // started, as a multicast event's are.
    private sealed class FrameReports
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TopLevel, FrameReports> windows = new();
        private readonly TopLevel top;
        private readonly LinkedList<Action> reports = new();
        private Action[] pass = [];

        private FrameReports(TopLevel top) => this.top = top;

        internal sealed class Entry(FrameReports owner, LinkedListNode<Action> node)
        {
            public void Leave() => owner.Leave(node);
        }

        public static Entry Join(TopLevel top, Action report)
        {
            var hub = windows.GetValue(top, window => new FrameReports(window));
            if (hub.reports.Count == 0) top.LayoutUpdated += hub.Passed;
            return new Entry(hub, hub.reports.AddLast(report));
        }

        private void Leave(LinkedListNode<Action> node)
        {
            if (node.List != reports) return;
            reports.Remove(node);
            if (reports.Count == 0) top.LayoutUpdated -= Passed;
        }

        private void Passed(object? sender, EventArgs e)
        {
            var count = reports.Count;
            if (pass.Length < count) pass = new Action[Math.Max(count, pass.Length * 2)];
            reports.CopyTo(pass, 0);
            for (var i = 0; i < count; i++) pass[i]();
            Array.Clear(pass, 0, count);
        }
    }

    /// <summary>PORT-ONLY (ED-NEW-ignis-guards): the context menu's "Turn off" (lit) or "Turn on" (cooled), last: the mark's pill by keyboard.</summary>
    internal IReadOnlyList<RowAction> WithIgnis(IReadOnlyList<RowAction> actions)
    {
        if (Ember is not { } ember || ember.ActionTitle is not { } title) return actions;
        return [.. actions, new RowAction { Title = title, StartsGroup = actions.Count > 0, Action = ember.Act }];
    }

    // The mark's zone ends where the title's room ends: the row's trailing clearance as it glides (the hover's buttons widen it).
    private static void PlaceEmber(Marrow.App.Port.Ignis.Views.IgnisSidebarMark ember, ThreadRowFace face, double clearance)
    {
        ember.Place(Chrome.RowHorizontalPadding + clearance);
        FadeTitle(ember, face);
    }

    // The title's end is masked under the pill and the flame as they come in (the mock's mask), and comes back as they go.
    private static void FadeTitle(Marrow.App.Port.Ignis.Views.IgnisSidebarMark ember, ThreadRowFace face)
    {
        var (from, to, amount) = ember.TitleFade;
        var width = face.Bounds.Width;
        if (amount <= 0 || width <= 0) { face.OpacityMask = null; return; }
        var start = Math.Clamp((width - from) / width, 0, 1);
        var end = Math.Clamp((width - to) / width, start, 1);
        var faded = Color.FromArgb((byte)Math.Round(255 * (1 - Math.Clamp(amount, 0, 1))), 0, 0, 0);
        face.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, start), new GradientStop(faded, end), new GradientStop(faded, 1) },
        };
    }

    // :1834-1838 Instant hide; reveal eases over the same 0.12s handover as the covering ghost.
    private bool? wasHidden;
    private IFrameSource? revealSource;
    private Action<TimeSpan>? revealFrame;
    private void SetHidden(bool hidden)
    {
        if (wasHidden == hidden) return;
        var initial = wasHidden is null;
        wasHidden = hidden;
        StopReveal();
        if (initial || hidden) { Opacity = hidden ? 0 : 1; return; }
        var from = Opacity;
        var start = SwiftClock.Now;
        var animation = Animation.EaseOut(0.12);
        var source = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            Opacity = seconds >= animation.Duration ? 1 : from + (1 - from) * animation.Progress(seconds);
            if (seconds >= animation.Duration) revealFrame = null;
            else source.RequestFrame(tick!);
        };
        revealSource = source;
        revealFrame = tick;
        source.RequestFrame(tick);
    }
    private void StopReveal()
    {
        if (revealFrame is { } pending) revealSource?.CancelFrame(pending);
        revealFrame = null;
    }

    // :1871-1872 Same selectable button foundation as SidebarRow; expose the live selection and settled value.
    private sealed class ThreadButton(Func<bool> selected, Func<bool> settled) : Button
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this, selected, settled);
        private sealed class Peer(ThreadButton owner, Func<bool> selected, Func<bool> settled) : ButtonAutomationPeer(owner), ISelectionItemProvider, IValueProvider
        {
            bool ISelectionItemProvider.IsSelected => selected();
            ISelectionProvider? ISelectionItemProvider.SelectionContainer => null;
            void ISelectionItemProvider.AddToSelection() { }
            void ISelectionItemProvider.RemoveFromSelection() { }
            void ISelectionItemProvider.Select() => owner.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool IValueProvider.IsReadOnly => true;
            string IValueProvider.Value => settled() ? "Settled" : "";
            void IValueProvider.SetValue(string? value) { }
        }
    }

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

    // PORT-ONLY (ED-NEW-session-hotkeys): the row's fill while the selection glides. Landing, the row draws its selected look but
    // for the 0.12 fill, which the glide draws in the same place (so the hand-over at the end changes no pixel); leaving, it
    // draws unselected at once.
    private static (SwiftColor Background, SwiftColor? PinnedOverlay) GlideFill(bool isPinned, bool isSelected, bool isHovering, GlideHold hold)
    {
        if (hold == GlideHold.Arriving && isSelected)
            return (Chrome.Overlay(0), isPinned ? Chrome.Danger.Opacity(0.06) : null);
        return BackgroundFill(isPinned, isSelected, isHovering);
    }

    // PORT-ONLY (ED-NEW-session-hotkeys): the number badge centres on the leading slot (:2024-2031, 8 pt in, Chrome.iconSize
    // wide), on a settled row's check (:1768, 6 pt in, 20 wide), or on the trailing status of a detailed row.
    private void PlaceKeycap(bool settled, bool detailed)
    {
        var margin = detailed
            ? new Thickness(0, 0, Chrome.RowHorizontalPadding, 0)
            : new Thickness(settled ? ThreadRowMetrics.SettledCheckInset : Chrome.RowHorizontalPadding, 0, 0, 0);
        var alignment = detailed ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        if (Keycap.Margin != margin) Keycap.Margin = margin;
        if (Keycap.HorizontalAlignment != alignment) Keycap.HorizontalAlignment = alignment;
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
        var lift = new Border // :1841-1843
        {
            Background = Chrome.Overlay(0.12).Brush(content), // :1842
            Opacity = 0,
            IsHitTestVisible = false,
        }.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous); // :1735 shape, followed by both the row's own background and this lift
        // :1838-1846 `.background { lift }.scaleEffect(isDragged ? 1.02 : 1)`: the scale is applied after the
        // background, so it grows the row together with its lifted fill and shadow (UI-SIDEBAR-060).
        // :1843 `.shadow(color: .black.opacity(0.28), radius: 10, y: 4)` on the filled shape: its own coverage, cast at the fill's alpha
        // (0.102: 0.029 drawn) and under it, through it (SwiftShadow.Caster, ED-358; sigma 10, ED-327). It fades with the fill.
        var shadow = new SwiftShadow.Caster(lift, Color.FromArgb((byte)(0.28 * 255), 0, 0, 0), radius: 10, y: 4);
        var host = new Panel { Children = { shadow, lift, content }, RenderTransform = scale };
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
        content.VerticalAlignment = VerticalAlignment.Center; // :1773 .overlay(alignment: .trailing) centres it on the row's height
        return content;
    }

    // :1981-1984 static func mergeStage(of runtime: ThreadRuntime?) -> String?
    public static string? MergeStage(ThreadRuntime? of)
    {
        if (of is not { } runtime || !runtime.IsHydraMerging) return null; // :1982
        return (runtime.HydraMergeStage ?? "Merging") + "\U00002026"; // :1983 U+2026 HORIZONTAL ELLIPSIS
    }

    // :1804-1814 the fold overlay, 46 pt from the trailing edge: a chevron button that toggles the fold, drawn only
    // while showsFold and onToggleFold is set. `fold(_:)` (SidebarView.swift:487-490, the caller's own concern,
    // outside this row) is what wraps the toggle in Chrome.panelSlide; onToggleFold arrives here already prepared,
    // as archiveAction and the other collaborators of BuildTrailingOverlay above do. Part 5. PR-SIDEBAR-116.
    private static Control? BuildFoldOverlay(bool showsFold, Action? onToggleFold, bool folded)
    {
        if (!showsFold || onToggleFold is not { } toggle) return null; // :1805
        var icon = new RowAccessoryIcon(folded ? "chevron.right" : "chevron.down"); // :1808, OD-0010 Lucide chevron-right/chevron-down
        var button = PlainControls.Button(icon, toggle); // :1807, :1810 .buttonStyle(.plain)
        button.HorizontalAlignment = HorizontalAlignment.Right; // :1804 .overlay(alignment: .trailing)
        button.VerticalAlignment = VerticalAlignment.Center; // the same overlay centres it on the row's height
        button.Margin = new Thickness(0, 0, ThreadRowMetrics.FoldInset, 0); // :1811, 46
        var tip = folded ? "Show helpers" : "Hide helpers"; // :1812
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip); // :1813
        return button;
    }

    // :1882-1891 static func == (lhs:rhs:) -> Bool: value inputs only (closures and the frame holder are excluded),
    // so the parent's re-evaluation skips a row whose thread did not change; live state still reaches the row
    // through the model's per-thread cells. Titles compare by Swift String equality (R-STR rules), not ordinally.
    // Part 5. PR-SIDEBAR-155.
    internal static bool RowEquals(
        ChatThread leftSnapshot, string? leftProjectName, bool leftIsDragged, bool leftHasOnToggleFold,
        ChatThread rightSnapshot, string? rightProjectName, bool rightIsDragged, bool rightHasOnToggleFold) =>
        leftSnapshot.Id == rightSnapshot.Id // :1883
        && SwiftString.Equal(leftSnapshot.Title, rightSnapshot.Title) // :1884
        && leftSnapshot.UpdatedAt == rightSnapshot.UpdatedAt // :1885
        && leftSnapshot.IsPinned == rightSnapshot.IsPinned // :1886
        && leftSnapshot.IsSettled == rightSnapshot.IsSettled // :1887
        && NullableStringEqual(leftProjectName, rightProjectName) // :1888
        && leftIsDragged == rightIsDragged // :1889
        && leftHasOnToggleFold == rightHasOnToggleFold; // :1890, (lhs.onToggleFold != nil) == (rhs.onToggleFold != nil)

    // :1888's `String?` comparison: Swift's Optional `==` is nil-safe Equatable, reproduced explicitly since
    // SwiftString.Equal itself takes non-nullable strings. Part 5. PR-SIDEBAR-155.
    private static bool NullableStringEqual(string? left, string? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return SwiftString.Equal(left, right);
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
        GlideAtScale(animator, direction, listFrame, windowFrame, thread, projectName, projectIcon,
            isSelected, isHovering, runtime, colorScheme, 2); // detached caller: PR-SIDEBAR-135 minimum/fallback
    }

    // :1933-1978 PR-SIDEBAR-135 windows_path: render at this row's window DPI, with minimum scale 2.
    private static void GlideAtScale(
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
        ThemeVariant colorScheme,
        double windowScale)
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
            new ThreadRowFace(thread, projectName, isSelected, departureClearance, Badge, projectIcon), from.Size, colorScheme, Math.Max(2, windowScale)); // :1954-1957
        if (departure is null) return; // :1958 guard let departure = ..., let arrival = ... else { return }
        var arrival = animator.Render(
            new ThreadRowFace(arriving, projectName, isSelected, arrivalClearance, Badge, projectIcon),
            new Size(from.Width, arrivalHeight), colorScheme, Math.Max(2, windowScale)); // :1959-1961
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
/// <remarks>
/// ONE face per row, updated in place (lead ruling 2026-10-04, from SidebarView.swift:1739-1743, which keeps the face under
/// the row's identity and re-reads its inputs). <see cref="Update"/> hands it a new thread, selection, merge stage and
/// trailing clearance; every property the face animates (the trailing padding :2068, the title's weight :2041 and opacity
/// :2042, the project line's glyph :2055) moves on from the value it stands at, so a change of any input that lands while
/// one of them is on its way leaves it on its way. A change of the thread that alters the layout itself (settled or open)
/// rebuilds the face's content inside this same object and carries the animated values across.
/// </remarks>
internal sealed class ThreadRowFace : Decorator
{
    private readonly string? projectName;
    private readonly ProjectIcon? projectIcon;
    private readonly Func<Control> badge;
    private readonly Border frame;
    private readonly AnimatedScalar clearance;
    private readonly AnimatedScalar weight;
    private readonly AnimatedScalar titleOpacity;
    private ChatThread thread;
    private bool isSelected;
    private string? mergeStage;
    private double leftPadding;
    private double vertical;
    private double clearanceTarget;
    private double weightTarget;
    private double titleOpacityTarget;
    private Panel? titleHost; // the title's slot: one copy of the text at rest, a Regular and a Medium copy stacked while the weight changes
    private TextBlock? regularTitle;
    private TextBlock? mediumTitle;
    private TextBlock? label;
    private Panel? glyphHost;
    private string? glyphSymbol;
    private Grid? row;
    private Control? leading;

    /// <summary>PORT-ONLY (ED-NEW-session-hotkeys): the leading slot's mark (:2024-2031), which a number badge covers; null when none.</summary>
    internal Control? LeadingMark => leading;
    private object? leadingKey;

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
        this.thread = thread;
        this.projectName = projectName;
        this.isSelected = isSelected;
        this.badge = badge;
        this.projectIcon = projectIcon;
        this.mergeStage = mergeStage;
        clearanceTarget = trailingClearance;
        weightTarget = WeightClass(thread, isSelected);
        titleOpacityTarget = TitleOpacity(thread.IsSettled, isSelected);
        frame = new Border { HorizontalAlignment = HorizontalAlignment.Stretch }; // :2071 .frame(maxWidth: .infinity, alignment: .leading)
        SetMetrics();
        clearance = new AnimatedScalar(this, trailingClearance, ApplyPadding); // :2068
        weight = new AnimatedScalar(this, weightTarget, ApplyWeight); // :2041/:2048
        titleOpacity = new AnimatedScalar(this, titleOpacityTarget, ApplyTitleOpacity); // :2042/:2049
        BuildContent();
        Child = frame;
        DetachedFromVisualTree += (_, _) => SettleGlyph(); // a crossfade the tree left does not stay half drawn
    }

    /// <summary>
    /// The row's inputs on a later render (:1739-1743): the same face, new values. The padding (:2068), the title's weight
    /// and opacity (:2041-2042) and the project line's glyph (:2055) take the new value along <paramref name="animation"/>,
    /// the transaction the change was made in (:1816-1818 withAnimation(Chrome.hover) for the hover), from where each
    /// stands now; with no animation a property whose TARGET changed is taken at once (SwiftUI replaces the running
    /// animation), and one whose target did not change is left exactly as it is, so an unrelated change (a selection, a
    /// title, a merge stage) never cuts an animation that is on its way. OD-0076 also asks for it ("a label whose width
    /// changes eases its container's width").
    /// </summary>
    public void Update(ChatThread nextThread, bool selected, string? stage, double trailingClearance, Animation? animation)
    {
        var settledChanged = nextThread.IsSettled != thread.IsSettled;
        thread = nextThread;
        isSelected = selected;
        mergeStage = stage;
        if (settledChanged)
        {
            SetMetrics();
            BuildContent(); // the layout changes (:2023, :2037, :2053); the animated values below keep where they stand
        }
        else
        {
            foreach (var copy in TitleCopies()) if (copy.Text != thread.Title) copy.Text = thread.Title;
            if (label is not null) label.Text = mergeStage ?? projectName; // :2057
            if (leading is not null && leadingKey is not null && !Equals(leadingKey, BadgeKey(thread)))
            {
                var next = BuildLeading(); // the badge reads the thread it was built with (:2152-2166)
                row!.Children[row.Children.IndexOf(leading)] = next;
                leading = next;
                leadingKey = BadgeKey(thread);
            }
            if (glyphHost is not null) SetGlyph(GlyphName(thread), animation);
        }
        SetTrailingClearance(trailingClearance, animation);
        var nextWeight = WeightClass(thread, isSelected);
        if (nextWeight != weightTarget) { weightTarget = nextWeight; weight.Set(nextWeight, animation); }
        var nextOpacity = TitleOpacity(thread.IsSettled, isSelected);
        if (nextOpacity != titleOpacityTarget) { titleOpacityTarget = nextOpacity; titleOpacity.Set(nextOpacity, animation); }
    }

    /// <summary>
    /// :2013 `trailingClearance` as the row hands the face on a later render (:1739-1743): the same face, a new number.
    /// SwiftUI animates the padding in the transaction the hover is written in (:1816-1818, `withAnimation(Chrome.hover)`),
    /// so the title's room, and where it truncates, glides; with no transaction it is taken at once. A number that is
    /// already the target is not set again: setting it without an animation would cut the glide that is on its way.
    /// </summary>
    public void SetTrailingClearance(double value, Animation? animation)
    {
        if (value == clearanceTarget) return;
        clearanceTarget = value;
        clearance.Set(value, animation);
    }

    private void SetMetrics()
    {
        var isSettled = thread.IsSettled; // :2020
        var isDetailed = projectName is not null && !isSettled; // :2021
        leftPadding = Chrome.RowHorizontalPadding + (isSettled ? ThreadRowMetrics.SettledTitleInset : 0); // :2067
        vertical = isDetailed ? ThreadRowMetrics.DetailedVerticalPadding : 0; // :2069
        frame.MinHeight = isSettled ? ThreadRowMetrics.SettledHeight : Chrome.RowHeight; // :2070
    }

    private void ApplyPadding(double value)
    {
        frame.Padding = new Thickness(leftPadding, vertical, Chrome.RowHorizontalPadding + value, vertical); // :2066-2069
        ClearanceApplied?.Invoke(value);
    }

    /// <summary>PORT-ONLY (ED-NEW-ignis-guards): told each drawn trailing clearance, so the row's Ignis mark glides with the title's room.</summary>
    internal Action<double>? ClearanceApplied { get; set; }

    /// <summary>
    /// :2041/:2048 the title's weight, 400 (.regular) to 500 (.medium), as the value of the animation. The title NEVER asks for
    /// a weight between the two: the embedded Inter is static faces and Avalonia resolves 413 and 425..498 to SemiBold or
    /// Bold, past the source's .medium (measured: SidebarLeftoversReviewTests). A weight change is drawn as a crossfade of two
    /// copies of the same text stacked exactly, one Regular and one Medium; their opacities are (1 - share) and share, share
    /// being how far the value is from 400 to 500, so they run along the curve the weight rides. At rest only the copy whose
    /// weight it is exists. A change that arrives mid-crossfade starts from the share the scalar stands at.
    /// </summary>
    private void ApplyWeight(double value)
    {
        if (titleHost is null) return;
        var share = Math.Clamp((value - (int)FontWeight.Normal) / ((int)FontWeight.Medium - (int)FontWeight.Normal), 0, 1);
        ShowTitleCopy(ref regularTitle, SwiftFont.Weight.Regular, 1 - share);
        ShowTitleCopy(ref mediumTitle, SwiftFont.Weight.Medium, share);
    }

    private void ShowTitleCopy(ref TextBlock? copy, SwiftFont.Weight weightOfCopy, double opacity)
    {
        if (titleHost is null) return;
        if (opacity <= 0)
        {
            if (copy is not null) titleHost.Children.Remove(copy);
            copy = null;
            return;
        }
        if (copy is null)
        {
            copy = new TextBlock { Text = thread.Title, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }; // :2040/:2047, :2043-2044/:2050-2051
            SwiftFont.System(thread.IsSettled ? 12 : 13, weightOfCopy).Apply(copy); // :2041/:2048
            copy.Foreground = Chrome.PrimaryText.Opacity(titleOpacity.Value).Brush(copy); // :2042/:2049
            titleHost.Children.Insert(weightOfCopy == SwiftFont.Weight.Regular ? 0 : titleHost.Children.Count, copy); // Medium over Regular
        }
        copy.Opacity = opacity;
    }

    private IEnumerable<TextBlock> TitleCopies() => new[] { regularTitle, mediumTitle }.OfType<TextBlock>();

    private void ApplyTitleOpacity(double value)
    {
        foreach (var copy in TitleCopies()) copy.Foreground = Chrome.PrimaryText.Opacity(value).Brush(copy); // :2042/:2049
    }

    // What ThreadBadge reads off the thread (:2159-2169); the leading badge is built again only when one of them changes.
    private static object BadgeKey(ChatThread thread) => (thread.Provider, thread.IsPinned, thread.HasUnread, thread.LastStatus);

    // :2041/:2048 weight: .medium when selected or unread and not settled, else .regular (as the OpenType class).
    private static double WeightClass(ChatThread thread, bool isSelected) =>
        !thread.IsSettled && (isSelected || thread.HasUnread) ? (int)FontWeight.Medium : (int)FontWeight.Normal;

    private static string GlyphName(ChatThread thread) => thread.WorktreePath is null ? "folder" : "arrow.triangle.branch"; // :2055

    // :2022-2072 the content under the frame; the animated values (padding, weight, opacity) are not touched here.
    private void BuildContent()
    {
        var isSettled = thread.IsSettled; // :2020
        var isDetailed = projectName is not null && !isSettled; // :2021
        titleHost = null;
        regularTitle = null;
        mediumTitle = null;
        label = null;
        glyphHost = null;
        glyphSymbol = null;
        leading = null;
        leadingKey = null;

        row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") }; // :2022 HStack(spacing: 8)

        if (!isDetailed && !isSettled) // :2023 PR-SIDEBAR-066
        {
            leading = BuildLeading();
            if (projectIcon is null) leadingKey = BadgeKey(thread);
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
                Children = { mark, MakeTitle() },
            };
            foreach (var part in titled.Children) part.VerticalAlignment = VerticalAlignment.Center;
            texts.Children.Add(titled);
        }
        else
        {
            texts.Children.Add(MakeTitle()); // :2047-2051
        }

        if (projectName is not null && !isSettled && projectIcon is null) // :2053 PR-SIDEBAR-070
        {
            glyphHost = new Panel();
            glyphSymbol = GlyphName(thread);
            glyphHost.Children.Add(MakeGlyph(glyphSymbol));
            label = new TextBlock { Tag = "projectName", Text = mergeStage ?? projectName, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }; // :2057-2059
            SwiftFont.Callout.Apply(label); // :2058
            label.Foreground = Chrome.SecondaryText.Brush(label); // :2061
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { glyphHost, label } }; // :2054
            foreach (var part in line.Children) part.VerticalAlignment = VerticalAlignment.Center;
            texts.Children.Add(line);
        }

        texts.Margin = new Thickness(0, 0, 4, 0); // :2065 Spacer(minLength: 4)
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        frame.Child = row;
        ApplyPadding(clearance.Value);
    }

    // :2024-2031 the leading slot: the project's mark, else the badge.
    private Control BuildLeading()
    {
        var control = projectIcon is { } compactIcon ? new ProjectIconMark(compactIcon, 12) : badge(); // :2025-2029
        control.Width = Chrome.IconSize; // :2031 .frame(width: Chrome.iconSize)
        control.HorizontalAlignment = HorizontalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(0, 0, 8, 0); // HStack(spacing: 8)'s gap before the text column
        Grid.SetColumn(control, 0);
        return control;
    }

    // :2040-2051 the title text, shared by the detailed-with-mark layout and the plain one.
    private Panel MakeTitle()
    {
        titleHost = new Panel();
        ApplyWeight(weight.Value); // the copies the weight stands at: one at rest, two while it changes
        return titleHost;
    }

    // :2055-2056, :2061 the project line's mark, a footnote-sized symbol in the secondary colour.
    private static Control MakeGlyph(string symbol)
    {
        var glyph = SwiftImage.SystemName(symbol).Font(SwiftFont.Footnote); // :2055 / :2056
        glyph.ForegroundStyle(Chrome.SecondaryText); // :2061
        glyph.VerticalAlignment = VerticalAlignment.Center;
        return glyph;
    }

    /// <summary>
    /// :2055 the symbol changes with the thread's worktree. Inside an animation the new mark rises over the old one, which
    /// fades where it stands and leaves once clear; each rides its own scalar, so a change that arrives while a mark is
    /// half shown fades it out from there and never from full. With no animation the new mark replaces the old at once.
    /// </summary>
    private void SetGlyph(string symbol, Animation? animation)
    {
        if (glyphHost is not { } host || glyphSymbol == symbol) return;
        glyphSymbol = symbol;
        var leaving = host.Children.ToArray();
        var incoming = MakeGlyph(symbol);
        host.Children.Add(incoming);
        if (animation is null)
        {
            foreach (var old in leaving) host.Children.Remove(old);
            return;
        }
        incoming.Opacity = 0;
        new AnimatedScalar(incoming, 0, level => incoming.Opacity = level).Set(1, animation);
        foreach (var old in leaving)
        {
            new AnimatedScalar(old, old.Opacity, level =>
            {
                old.Opacity = level;
                if (level == 0) host.Children.Remove(old);
            }).Set(0, animation);
        }
    }

    // A crossfade the face left the tree in the middle of ends at its target (the scalars stop on detach).
    private void SettleGlyph()
    {
        if (glyphHost is not { } host || host.Children.Count < 2) return;
        var current = host.Children[^1];
        host.Children.Clear();
        current.Opacity = 1;
        host.Children.Add(current);
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
                    ? NthWorkingMarks.Still(runtime is { IsHydraMerging: true }) // :2100-2101 isStill: the beads' still pose (a ghost is carried as a picture)
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
    private readonly ViewState state;
    private readonly ScaleTransform popScale; // :2132 .scaleEffect(isPopping ? 1.35 : 1), one transform every glyph shares
    private readonly AnimatedScalar pop;

    /// <summary>:2115 <c>let isSettled: Bool</c>.</summary>
    public bool IsSettled { get; }

    /// <param name="isSettled">:2115 <c>let isSettled: Bool</c>.</param>
    /// <param name="isPopping">:2117 <c>let isPopping: Bool</c>.</param>
    /// <param name="action">:2118 <c>let action: () -> Void</c>.</param>
    // :2122-2140 body. PR-SIDEBAR-078.
    public SettleCheck(bool isSettled, bool isPopping, Action action)
    {
        IsSettled = isSettled;
        state = new ViewState { IsPopping = isPopping };
        popScale = new ScaleTransform(isPopping ? 1.35 : 1, isPopping ? 1.35 : 1);
        var glyphHost = new Panel { Width = 20, Height = 20, Background = Brushes.Transparent }; // :2130 frame, :2131 contentShape(.rect)
        pop = new AnimatedScalar(glyphHost, isPopping ? 1.35 : 1, scale => { popScale.ScaleX = scale; popScale.ScaleY = scale; });
        Control? currentGlyph = null;
        string? currentSymbol = null;
        AnimatedColor? currentColor = null; // the shown glyph's foreground (:2128), eased when only the colour changes
        var button = PlainControls.Button(glyphHost, action); // :2125, :2134 .buttonStyle(.plain)
        button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :2135-2137
        button.Track(() => (state.IsHovering, state.IsPopping), value =>
        {
            var (hovering, popping) = value;
            var filled = popping || (isSettled && !hovering); // :2124
            var isDone = isSettled || popping; // :2123
            var color = isDone || hovering
                ? Chrome.Success.Opacity(isSettled && !hovering ? 0.8 : 1) // :2128
                : Chrome.SecondaryText;
            var symbol = filled ? "checkmark.circle.fill" : "checkmark.circle"; // :2126
            // UI-SIDEBAR-020 (OD-0076): :2128's foregroundStyle changes inside the hover's withAnimation(Chrome.hover) (:2135-2137), so
            // an unchanged symbol keeps its glyph and the colour eases from the old one to the new on the transaction the change
            // is written in (Reduce Motion takes it at once). A changed symbol is a new glyph: the replace below crossfades the two.
            if (currentGlyph is not null && currentSymbol == symbol && currentColor is not null)
            {
                var still = EnvironmentValues.GetAccessibilityReduceMotion(button) || Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
                currentColor.Set(color, still ? null : Transaction.Current);
                return;
            }
            var previous = currentGlyph;
            if (previous is not null) glyphHost.Children.Remove(previous);
            var glyph = SwiftImage.SystemName(symbol)
                .Font(Chrome.InlineIconFont); // :2127
            var glyphColor = new AnimatedColor(glyph);
            glyphColor.Set(color, null); // a new glyph is drawn in its colour at once
            glyph.Foreground = glyphColor.Brush; // :2128
            currentColor = glyphColor;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.RenderTransform = popScale; // :2132
            glyph.IsHitTestVisible = false;
            if (previous is not null && currentSymbol is not null && currentSymbol != symbol)
            {
                // :2129 .contentTransition(.symbolEffect(.replace)): the outline and the filled check replace each other in
                // two steps (SymbolReplace). The glyphs ride in wrappers so their own pop scale (:2132) is kept.
                var outgoing = new Decorator { Child = previous, IsHitTestVisible = false };
                var incoming = new Decorator { Child = glyph, IsHitTestVisible = false };
                glyphHost.Children.Add(incoming);
                glyphHost.Children.Add(outgoing);
                SymbolReplace.Play(outgoing, incoming, () => glyphHost.Children.Remove(outgoing));
                currentGlyph = incoming;
            }
            else
            {
                currentGlyph = glyph;
                glyphHost.Children.Add(glyph);
            }
            currentSymbol = symbol;
        });
        var tip = isSettled ? "Reopen thread" : "Settle thread"; // :2138-2139
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip); // :2139 .accessibilityLabel
        Child = button;
    }

    /// <summary>
    /// `isPopping` as the row hands it on a later render (:1765-1770): the row keeps the check it has and tells it, as
    /// SwiftUI keeps the view and re-reads its input. The scale rides the transaction the change is made in, the
    /// settle's `.spring(response: 0.3, dampingFraction: 0.45)` (:1909); with none it is taken at once.
    /// </summary>
    public void SetPopping(bool popping)
    {
        if (state.IsPopping == popping) return;
        pop.Set(popping ? 1.35 : 1, Transaction.Current); // :2132, before the glyph's own change redraws it
        state.IsPopping = popping;
    }

    // :2120 @State private var isHovering = false
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;
        private bool isPopping; // :2117, the row's input, written by SetPopping

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

        public bool IsPopping { get => Read(isPopping); set => Set(ref isPopping, value); }
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
                var spinner = NthWorkingMarks.Working(thread.Id.ToString(), hydra: mergeStage is not null); // :2157, the quiet working beads (Calculi WU6)
                spinner.IsHitTestVisible = true; // it carries the merge stage as its tooltip
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
                    RenderTransform = new TranslateTransform(2.5, -2.5), // :2171 .offset(x: 2.5, y: -2.5): drawn moved, takes no layout (a negative Margin would round to whole pixels)
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
        var glyphColor = new AnimatedColor(glyph);
        glyph.Foreground = glyphColor.Brush;
        var label = new Panel { Width = 30, Height = 30, Children = { circle, glyph } };
        var button = PlainControls.Button(label, () => // :2322-2324, :2335 .buttonStyle(.plain)
            Transaction.WithAnimation(Chrome.PanelSlide, () => isOn.Value = !isOn.Value));
        button.OnHover(hovering => // :2336-2338
            Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering));
        AutomationProperties.SetName(button, "Activity view"); // :2340
        button.Track(() => (isOn.Value, state.IsHovering, ShortcutStore.Shared.Current.Chord(AppShortcut.ToggleActivityView)), value =>
        {
            var (on, hovering, chord) = value;
            glyphColor.Set(on ? Chrome.Accent : Chrome.SecondaryText, Transaction.Current); // :2327, eased with the toggle's panelSlide (:2323)
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
    // :2349-2357 body. Part 5. PR-SIDEBAR-125.
    public ActivityHeader(string title, bool isFirst)
    {
        var text = new TextBlock { Text = title, TextWrapping = TextWrapping.NoWrap }; // :2350
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold).Apply(text); // :2351
        text.Foreground = Chrome.SecondaryText.Brush(text); // :2352
        text.Margin = new Thickness(Chrome.RowHorizontalPadding, isFirst ? 2 : 14, Chrome.RowHorizontalPadding, 4); // :2353-2355
        text.HorizontalAlignment = HorizontalAlignment.Stretch; // :2356 .frame(maxWidth: .infinity, alignment: .leading)
        Child = text;
    }
}

// :2375 HStack(alignment: .firstTextBaseline, spacing:) for a line of mixed sizes: every child is placed so that the
// first baseline of each (a text's first line, a symbol's ED-182 baseline offset) lies on one line. A child that has
// neither is aligned by its bottom edge, as a SwiftUI view without a baseline guide is. UI-SIDEBAR-040.
internal sealed class FirstBaselineRow : Panel
{
    public FirstBaselineRow() => UseLayoutRounding = false; // keep the font's fractional first baseline

    public double Spacing { get; init; }

    private double baseline;

    private static double Baseline(Control child)
    {
        if (child is TextBlock text && text.TextLayout.TextLines.Count != 0) return text.TextLayout.TextLines[0].Baseline;
        var offset = TextBlock.GetBaselineOffset(child);
        return offset > 0 ? offset : child.DesiredSize.Height;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Spacing * Math.Max(0, Children.Count - 1);
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            width += child.DesiredSize.Width;
        }
        baseline = Children.Count == 0 ? 0 : Children.Max(Baseline);
        var height = Children.Count == 0 ? 0 : Children.Max(child => baseline - Baseline(child) + child.DesiredSize.Height);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var child in Children)
        {
            child.Arrange(new Rect(x, baseline - Baseline(child), child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }
        return finalSize;
    }
}

/// <summary>:2363-2406 <c>private struct SettledHeader: View</c>.</summary>
internal sealed class SettledHeader : Decorator
{
    /// <param name="count">:2365 <c>let count: Int</c>.</param>
    /// <param name="isFirst">:2366 <c>let isFirst: Bool</c>.</param>
    // :2368-2405 body. Part 5. PR-SIDEBAR-123.
    private AppModel Model => AppEnvironment.GetAppModel(this); // :2364 @Environment(AppModel.self) private var model

    // The value the header was last given, and the pieces a later value changes in place: the SettledHeader of the
    // Swift is one view whose `count` and `isFirst` change under it, so the chevron's turn, the digits and the top
    // padding carry on from where they stood instead of starting again (:2364-2366, :2388, :2398).
    private long count;
    private bool isFirst;
    private TextBlock countText = null!;
    private Button button = null!;
    private HeadCardsRow.AnimatedDouble topPadding = null!;

    public SettledHeader(long count, bool isFirst)
    {
        this.count = count;
        this.isFirst = isFirst;
        var chevron = SwiftImage.SystemName("chevron.right").Font(SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Semibold)); // :2376-2377, OD-0010 Lucide chevron-right
        chevron.RenderTransformOrigin = RelativePoint.Center;
        chevron.ForegroundStyle(Chrome.SecondaryText); // :2392 cascades to every child of the HStack
        var rotation = new HeadCardsRow.AnimatedDouble(angle => chevron.RenderTransform = new RotateTransform(angle)); // :2378-2379
        DetachedFromVisualTree += (_, _) => rotation.Stop(); // cancel the frame callback when this header leaves

        var title = new TextBlock { Text = "Settled" }; // :2380
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold).Apply(title); // :2381
        title.Foreground = Chrome.SecondaryText.Brush(title); // :2392

        countText = new TextBlock { Text = count.ToString(CultureInfo.InvariantCulture) }; // :2384, a bare decimal, no grouping
        SwiftFont.Callout.MonospacedDigit().Apply(countText); // :2385-2386
        countText.Opacity = 0.8; // :2387
        countText.Foreground = Chrome.SecondaryText.Brush(countText); // :2392

        // :2375 HStack(alignment: .firstTextBaseline, spacing: 5): the chevron (10 pt), the title and the count (12 pt)
        // share one first-line baseline. The chevron's baseline is the ED-182 (Q1) one SwiftImage publishes through
        // TextBlock.BaselineOffset, which FirstBaselineRow reads beside the text lines' own. This closes STOP #13
        // (UI-SIDEBAR-040): the shared baseline layout it asked for is that offset, with no ED/PA of its own needed.
        var header = new FirstBaselineRow { Spacing = 5, Children = { chevron, title, countText } };

        var content = new Border // :2393-2397
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, // :2396 .frame(maxWidth: .infinity, alignment: .leading)
            Background = Brushes.Transparent, // :2397 .contentShape(.rect)
            Child = header,
        };

        // :2398 .animation(Chrome.panelSlide, value: isFirst): the top padding that follows isFirst is animated on its own.
        topPadding = new HeadCardsRow.AnimatedDouble(top => content.Padding = new Thickness(Chrome.RowHorizontalPadding, top, Chrome.RowHorizontalPadding, 4)); // :2393-2395
        topPadding.Set(isFirst ? 2 : 14, null); // :2394
        DetachedFromVisualTree += (_, _) => topPadding.Stop();

        button = PlainControls.Button(content, () => // :2370-2373
            Transaction.WithAnimation(Chrome.PanelSlide, () => Model.Settings.SettledCollapsed = !Model.Settings.SettledCollapsed));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Track(() => Model.Settings.SettledCollapsed, collapsed => // :2369
        {
            rotation.Set(collapsed ? 0 : 90, Chrome.PanelSlide); // :2378-2379 .animation(Chrome.panelSlide, value:)
            ToolTip.SetTip(button, collapsed ? "Show settled threads" : "Hide settled threads"); // :2401
            AutomationProperties.SetName(button, $"Settled, {this.count} threads"); // :2402, always the plural "threads"
            AutomationProperties.SetItemStatus(button, collapsed ? "Collapsed" : "Expanded"); // :2403, as ActivityViewToggle maps .accessibilityValue
        });
        Child = button;
    }

    // The header's `count` and `isFirst` as a later pass of the list gives them: the digits change at once, the top
    // padding along Chrome.panelSlide (:2388, :2398), and nothing else about the header is rebuilt.
    internal void Update(long count, bool isFirst)
    {
        if (count != this.count)
        {
            this.count = count;
            countText.Text = count.ToString(CultureInfo.InvariantCulture); // :2384
            AutomationProperties.SetName(button, $"Settled, {count} threads"); // :2402
        }
        if (isFirst != this.isFirst)
        {
            this.isFirst = isFirst;
            topPadding.Set(isFirst ? 2 : 14, Chrome.PanelSlide); // :2398
        }
    }
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
                var spinner = NthWorkingMarks.Working(thread.Id.ToString(), hydra: mergeStage is not null); // :2419, the quiet working beads (Calculi WU6)
                spinner.IsHitTestVisible = true; // it carries the merge stage as its tooltip
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
    public void Note(Rect frame)
    {
        if (Frame != frame) Frame = frame; // :2437-2439 writes never invalidate observation
    }
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

        // ED-NEW-lean-15: whether `new Fingerprint(thread)` would equal this one, field by field under the record's own equality
        // (EqualityComparer<T>.Default per field), without making it.
        public bool Matches(ChatThread thread) =>
            Id == thread.Id
            && ProjectID == thread.ProjectID
            && EqualityComparer<Guid?>.Default.Equals(ParentThreadID, thread.ParentThreadID)
            && IsArchived == thread.IsArchived
            && IsInPanel == thread.IsInPanel
            && IsSettled == thread.IsSettled
            && EqualityComparer<DateTimeOffset?>.Default.Equals(SettledAt, thread.SettledAt)
            && IsPinned == thread.IsPinned
            && EqualityComparer<double?>.Default.Equals(SortOrder, thread.SortOrder)
            && EqualityComparer<double?>.Default.Equals(ActivityOrder, thread.ActivityOrder)
            && EqualityComparer<DateTimeOffset?>.Default.Equals(ActivityOrderDay, thread.ActivityOrderDay)
            && EqualityComparer<DateTimeOffset>.Default.Equals(CreatedAt, thread.CreatedAt)
            && EqualityComparer<DateTimeOffset>.Default.Equals(UpdatedAt, thread.UpdatedAt)
            && FoldsHelpers == thread.FoldsHelpers
            && IsHydraHead == thread.IsHydraHead
            && EqualityComparer<long?>.Default.Equals(HydraIndex, thread.Hydra?.Index)
            && HasUnread == thread.HasUnread;
    }

    // :2529-2535 func placements(for threads: [ChatThread]) -> [ThreadPlacement]
    public ValueList<ThreadPlacement> Placements(IReadOnlyList<ChatThread> @for)
    {
        // ED-NEW-lean-15: the threads are checked against the kept fingerprints where they stand; a new set of fingerprints is made
        // only when one of them differs (the answer is the :2530-2531 comparison's, without a fingerprint per thread per read).
        if (fingerprint is { } kept && kept.Count == @for.Count && Unchanged(kept, @for)) return cached;
        var next = new ValueList<Fingerprint>(@for.Select(thread => new Fingerprint(thread))); // :2530
        if (next.Equals(fingerprint)) return cached; // :2531
        fingerprint = next; // :2532
        cached = new ValueList<ThreadPlacement>(@for.Select(thread => new ThreadPlacement(thread))); // :2533
        return cached; // :2534
    }

    private static bool Unchanged(ValueList<Fingerprint> kept, IReadOnlyList<ChatThread> threads)
    {
        for (var index = 0; index < threads.Count; index++)
            if (!kept[index].Matches(threads[index])) return false;
        return true;
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
        // ED-NEW-lean-15: one text block for the label's life; the minute's tick sets its text only when the words change (a
        // thread an hour old reads "1h" for sixty ticks), instead of building a new block every minute.
        TextBlock? text = null;
        this.Track(() => MinuteClock.Shared.Now, now => // :2690, the minute's tick re-renders this label alone
        {
            var words = RelativeTime.Short(date, now); // :2690
            if (text is null)
            {
                text = new TextBlock { Text = words };
                SwiftFont.Subheadline.MonospacedDigit().Apply(text); // :2691
                text.Foreground = Chrome.SecondaryText.Opacity(0.8).Brush(text); // :2692
                text.Margin = new Thickness(0, 0, 4, 0); // :2693 .padding(.trailing, 4)
                Child = text;
            }
            else if (!string.Equals(text.Text, words, StringComparison.Ordinal))
            {
                text.Text = words;
            }
        });
    }
}
