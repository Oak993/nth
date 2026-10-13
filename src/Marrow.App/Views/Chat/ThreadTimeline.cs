using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Markdown;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Tour;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

// Source: DroppyCode/App/Views/Chat/ThreadTimeline.swift (B3 52bc792), :1-2327.
// B3 owned behavior is ported. The one platform gap left (a window uncovered with no event of its own) is documented in ThreadTimelineTests STOP #3.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; an NSViewRepresentable/NSView is a Control; NSWindow is Avalonia's Window. An enum
// with associated values is an abstract record with nested sealed records (R-ENUM-2); where a case's labelled value
// is named like a member of the enum itself (`id`, `turnID`), the case redeclares that property with `new`, so the
// Swift label stays and both read the same value. `@Observable` classes are MainActorModel (R-OBS-6), their
// @ObservationIgnored storage plain auto-properties (R-OBS-5); `static let shared` is an Ambient (R-LIFE-2, the
// ScrollActivity.Shared precedent). A Task handle is TaskHandle; CFTimeInterval/TimeInterval are double; `[T]` in a
// value is ValueList<T>, `[K: V]` ValueMap, `Set<T>` ValueSet (R-VAL-2). A Swift argument label that is a C# keyword
// is written with `@` (MCPProxy.Url(string @for) precedent). The `extension EnvironmentValues` (:2250-2256) cannot
// be a static class named after the file (R-NAME-4, ED-095): that name is the view's, so its attached property
// lives on ThreadTimeline; RevealTimelineEndKey (:2246-2248) holds its default.
// AppModel and WindowLiveResize are inherited environments. Identity keys retain entry references rather than
// substituting value equality for ObjectIdentifier. The bounded eager stack needs no virtualized-range nudge.

namespace Marrow.App.Views.Chat;

/// <summary>:4-895 <c>struct ThreadTimeline: View, Equatable</c>.</summary>
public sealed class ThreadTimeline : Decorator
{
    private string? windowStartID; // :81
    private bool historyLoadPending; // :76
    private double paneWidthAtSlideStart; // :56
    private readonly TimelineScrollTracking tracking = new(); // :44
    private readonly TimelineRevealBox revealBox = new(); // :90
    private bool anchorsBottomOnGrowth = true; // :74
    private double viewportHeight; // :52
    private double paneWidth; // :52 `@State private var paneWidth: CGFloat = 0`: the scroll view's width, for the stepped layout width.
    private ScrollViewer? currentScroll;
    // :384-440 the ScrollView is one view for the life of the timeline: its geometry handler sees every change as old ->
    // new. Each draw swaps only its content; these are the current draw's pieces the long-lived handlers read.
    private Grid? scrollOverlay;
    private readonly BranchHost branches; // :134-144 the body's branches: the loading mark, the empty thread's prompt, the timeline (scrollOverlay)
    private Control? currentRail;
    private (IReadOnlyList<DisplayBlock> Blocks, bool Shows, double Height)? railInputs; // what the rail on show was made from (ED-NEW-lean-14)
    private Control? drawnColumn;
    private StackPanel? drawnBody;
    private StackPanel? drawnRows; // :388 the stack of the rows, one for the life of the content: each pass diffs it (ED-255)
    private readonly HashSet<Control> leavingViews = []; // badges fading out where they stood: still in the stack, in nobody's order
    private int earlierHidden; // the count the drawn earlier pill says
    private string? promptProject; // the project name the shown new-thread prompt says
    private Control? drawnSentinel;
    private Control? drawnEarlier;
    private IReadOnlyList<DisplayBlock> drawnBlocks = [];
    private readonly Dictionary<string, Control> laidOutRows = [];
    private readonly Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)> rowViews = [];
    private readonly Dictionary<string, (QuestionRequest Request, Control Row)> questionViews = []; // :393-397, stable ForEach identity
    private readonly Dictionary<string, (Marrow.Services.Port.PcControl.PcPendingAsk Ask, Control Row)> tactusViews = []; // PORT-ONLY: Tactus (ED-NEW-pcctl-27): its asks' badges
    private TimelineScrollState? state;
    private ThreadRuntime? ownerRuntime;
    private Action? redraw;
    private Action<string?, string?, bool, double, bool>? updateInputs;
    private TaskHandle? initialHistoryTask; // :594-607
    private bool initialHistoryStarted;
    // :363,436-441. Own the scroll presenter, as SidebarView does, rather than depending on a themed template.
    private static readonly FuncControlTemplate<ScrollViewer> TimelineScrollTemplate = new((viewer, scope) =>
    {
        // Transparent, so the scroll view takes the wheel over its whole frame, as NSScrollView does: while ScrollFreeze
        // (:2320-2325) turns hit-testing off on the rows, a presenter with no background left nothing under the pointer
        // that belongs to the scroll view, and every notch after the first went nowhere.
        var presenter = new ScrollContentPresenter { Name = "PART_ContentPresenter", Background = Brushes.Transparent, [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] };
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = false });
        return presenter.RegisterInNameScope(scope);
    });
    /// <summary>How many times the body has run (:98 `var body`): the render count PR-CHAT-160's `.equatable()` boundary is proven by (a title, an effort or a mode runs it no more).</summary>
    internal int DrawPasses { get; private set; }
    /// <summary>The rows of the timeline now (tests: the history hand-over raises them one by one).</summary>
    internal IReadOnlyList<Control> DrawnRows => drawnRows?.Children ?? (IReadOnlyList<Control>)[];
    private long? seenAutoStartedTurnToken; // :83-85
    private bool? seenRunning;
    private string? seenLastEntryID;
    private (ThreadRuntime Runtime, ChromeScrollModel Chrome, TimelineScrollState State, string? Project, string? Directory, bool Rewind, double Height, bool Minimap) inputs; // :26-35
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
        bool showsMinimap = true)
    {
        state = scrollState; ownerRuntime = runtime;
        inputs = (runtime, scrollChrome, scrollState, projectName, workingDirectory, supportsRewind, columnHeight, showsMinimap);
        SetRevealTimelineEnd(this, revealBox); revealBox.Action = RevealEnd; revealBox.Follow = FollowEnd;
        // :209-217 a head's name in the prose opens the head's chat; every other link keeps the system's handling.
        MarkdownView.SetOpenUrlAction(this, url =>
        {
            if (HydraMentionTarget.ThreadIDIn(url) is not { } headID) return false;
            AppEnvironment.GetAppModel(this).SelectedThreadID = headID; return true;
        });
        // :134-144 `if runtime.isLoadingHistory { Color.clear } else if blocks.isEmpty && !runtime.isRunning { NewThreadPrompt } else { timeline }`: the branch on show is one child of
        // this host. A swap between the prompt and the timeline crossfades (OD-0076: the Mac swaps them in one frame; ED-255 (3)). The loading mark hands over in about
        // 0.2 s, where the Mac opens a thread in one frame (CATALOGUE.md C 164.452-164.640): it fades out while what the thread shows fades and rises in (OD-0105 (1)).
        branches = new BranchHost(() => EnvironmentValues.GetAccessibilityReduceMotion(this), () => drawnRows?.Children); Child = branches;
        void Draw()
        {
            DrawPasses++;
            if (runtime.IsLoadingHistory)
            {
                // :134-135; OD-0136 amendment 3 (ED-NEW-nth-motion): the thread history loader is the nth "Flick" (B1), 48 px in the middle of the message area,
                // replacing Curtain Fold (OD-0113). It owns its 400 ms blank period, its entrance and its leave (NthFlick).
                if (branches.On is not NthFlick) branches.Show(new NthFlick { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, fadesIn: false);
                return;
            }
            var model = AppEnvironment.GetAppModel(this);
            var modelInputs = ReadModelInputs(runtime); seenModelInputs = modelInputs; // :99-131
            var pills = modelInputs.Pills;
            var blocks = TimelineBlockCache.Cache(runtime.ThreadID).Blocks(Marrow.App.Port.Ignis.Views.IgnisViews.Visible(runtime.Entries) /* PORT-ONLY Ignis U4 (ED-NEW-ignis-views): hidden keep-warm pings left out unless shown */, model.Settings.ShowReasoning, runtime.IsRunning, MergeID(runtime.IsHydraMerging, runtime.HydraMergeNoteID), pills);
            var lastID = runtime.Questions.LastOrDefault() is { } question ? "question-" + question.Id : blocks.LastOrDefault()?.Id;
            if (seenLastEntryID != lastID) { seenLastEntryID = lastID; NoteArrival(lastID, blocks); }
            MarkdownView.SetHydraMentionPersonas(this, TimelineBlockCache.Cache(runtime.ThreadID).MentionPersonas(modelInputs.Heads, modelInputs.Roster));
            MarkdownView.SetHydraMentionTargets(this, MentionTargets(modelInputs.Heads)); // :123-131,209-210; the open-URL action for them is set in the constructor (:209-217).
            if (blocks.Count == 0 && !runtime.IsRunning)
            {
                // :136-141 the prompt is one view while the thread stays empty and idle, and `.onAppear` runs when it appears, not at every pass of the body: a pass that
                // finds it shown leaves it, with its fade over, instead of replaying it, and a new project name is handed to it (NewThreadPrompt(threadID:projectName:) has no
                // `.id`: it says other words, it does not appear again, and `.onAppear` does not run).
                // The prompt comes in with its own appear (NewThreadPrompt.swift:50-53), so the host fades only what leaves; a timeline that was on show fades out where it stood.
                if (branches.On is NewThreadPrompt shown) { if (promptProject != projectName) { shown.Update(projectName); promptProject = projectName; } }
                else { branches.Show(new NewThreadPrompt(runtime.ThreadID, projectName), fadesIn: false); promptProject = projectName; scrollChrome.Update(0); scrollState.ShowsJumpButton = false; }
            }
            else
            {
                // :388-391 the stack of rows is one view for the life of the timeline's content, and each pass diffs it: a row whose id survives is the same
                // view in the same place and is never taken out of the tree and put back (a popover anchored in it, its selection, its hover and its focus
                // stay, and a long thread's pass costs the rows that changed, not all of them). ED-255.
                var rows = drawnRows ??= MakeRows();
                var rewindable = modelInputs.Rewindable.ToHashSet();
                var shownBefore = laidOutRows.Keys.ToArray(); laidOutRows.Clear(); rowsDrawn++;
                // This pass's views in order: the blocks' rows, then the questions' badges. A view arrives (with its transition) when it joins a stack that is already
                // on screen under an id that was not there. A timeline that comes in from nothing (its first pass, or back after the prompt or the loading mark took its
                // place and it had gone) comes in whole: :134-144 is an if/else, and the branch that comes back is a new view whose children come with it, so its rows and
                // its badges are placed with the stack, its rail with it, and what it had before (an answered badge, a rail it no longer has) is gone at once; the host's
                // one fade carries it (OD-0076, ED-255 (3)). A timeline turned back while it was still leaving is partly on screen: rows new to it arrive.
                var comesWhole = scrollOverlay is not { } layer || !branches.Holds(layer);
                var wanted = new List<(Control View, bool Arrives)>();
                var stale = new List<Control>(); var leaving = new List<Control>();
                foreach (var block in blocks.Skip(WindowStartIndex(blocks)))
                {
                    var context = new RowContext(workingDirectory, block.TurnID is { } id && rewindable.Contains(id), false);
                    var hadIdentity = rowViews.TryGetValue(block.Id, out var existing);
                    // :332-334 `.id(block.id)`: a block whose id survives keeps its view. A running turn's block changes with every step that lands in it, and its
                    // working line, its expansion and its bar must go on through that, so the row is told its new block instead of being made again.
                    var same = hadIdentity && existing.Block == block && existing.Context == context;
                    Control row;
                    if (same) row = existing.Row;
                    else if (hadIdentity && existing.Row is DisplayBlockView alive && alive.TryUpdate(block, context)) row = alive;
                    else
                    {
                        var fresh = new DisplayBlockView(block, runtime, context);
                        // The turn ends: the running block's row is replaced by the finished block's (the Mac cuts here, 2-chat-use.mov 125.743 -> 125.762 s). OD-0076, PR-CHAT-104: the
                        // finished block takes the running block's height and eases to its own, its rows fading in; the running rows are gone at once. Reduce Motion: the end state.
                        if (hadIdentity && existing.Block is DisplayBlock.Turn { Summary: null } && block is DisplayBlock.Turn { Summary: not null })
                            fresh.BeginTurnEnd(SwiftTransition.Container(existing.Row).Bounds.Height, EnvironmentValues.GetAccessibilityReduceMotion(this), NthWorkingBeads.Existing(runtime) is { } beads && beads.IsIn(existing.Row, out var inset) ? (beads, inset) : null); // Calculi WU5: the beads' exit plays in the finished header
                        row = fresh.Transition(Transition(block));
                    }
                    var made = !same && !(hadIdentity && ReferenceEquals(row, existing.Row));
                    if (made && hadIdentity) stale.Add(existing.Row);
                    rowViews[block.Id] = (block, context, row);
                    laidOutRows[block.Id] = row;
                    wanted.Add((row, made && !hadIdentity && !comesWhole)); // a view made again for an id that stood takes its place: no arrival
                }
                // Rows whose blocks left the window or the thread: :339 removes them as .identity, whatever a row's own transition says (the Mac cuts). OD-0105 (1), OD-0076:
                // the rows that leave in one pass fold shut together in 0.3 s (RowFold), on screen; under Reduce Motion, or with the timeline not on show, they go at once.
                var folds = new List<Control>(); var foldsShut = !comesWhole && TimelineOnShow && !EnvironmentValues.GetAccessibilityReduceMotion(this);
                foreach (var gone in rowViews.Keys.Except(laidOutRows.Keys).ToArray()) { (foldsShut ? folds : stale).Add(rowViews[gone].Row); rowViews.Remove(gone); }
                // :338 `.onDisappear { tracking.setVisible(block.id, false) }`: only for a row that leaves; the rows that stay report themselves as they cross the viewport's edge.
                foreach (var gone in shownBefore) if (!laidOutRows.ContainsKey(gone)) tracking.SetVisible(gone, false);
                foreach (var request in runtime.Questions)
                {
                    var id = "question-" + request.Id; var hadIdentity = questionViews.TryGetValue(id, out var existing);
                    var same = hadIdentity && existing.Request == request;
                    var badge = same ? existing.Row : new QuestionBadgeRow(request, runtime) { Name = id }.Transition(SoftAppearTransition);
                    if (!same && hadIdentity) stale.Add(existing.Row);
                    questionViews[id] = (request, badge);
                    wanted.Add((badge, !same && !hadIdentity && !comesWhole));
                } // :393-397
                foreach (var gone in questionViews.Keys.Except(runtime.Questions.Select(request => "question-" + request.Id)).ToArray())
                {
                    // :396 .transition(.softAppear): an answered question's badge leaves with a quick fade where it stood (one answered while the timeline had gone is not in the one that comes back).
                    (comesWhole ? stale : leaving).Add(questionViews[gone].Row); questionViews.Remove(gone);
                }
                // PORT-ONLY: Tactus (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.7): each Tactus approval, confirm and show-window ask of
                // this chat is a badge after the question badges, as they are; an answered one goes like an answered question.
                foreach (var ask in runtime.TactusAsks)
                {
                    var id = "tactus-" + ask.Id; var hadIdentity = tactusViews.TryGetValue(id, out var existing);
                    var same = hadIdentity && existing.Ask == ask;
                    var badge = same ? existing.Row : new TactusAskBadgeRow(ask) { Name = id }.Transition(SoftAppearTransition);
                    if (!same && hadIdentity) stale.Add(existing.Row);
                    tactusViews[id] = (ask, badge);
                    wanted.Add((badge, !same && !hadIdentity && !comesWhole));
                }
                foreach (var gone in tactusViews.Keys.Except(runtime.TactusAsks.Select(ask => "tactus-" + ask.Id)).ToArray())
                {
                    (comesWhole ? stale : leaving).Add(tactusViews[gone].Row); tactusViews.Remove(gone);
                }
                // A row whose place a new row takes in this pass (the running turn's line handed to the turn that began) is replaced, not folded: it goes at once, as
                // before, and the new one arrives with its own transition; only rows that leave with nothing in their place fold shut.
                var oldOrder = rows.Children.Where(view => !leavingViews.Contains(view)).ToList();
                var arrivingAt = wanted.Select((entry, index) => (entry, index)).Where(pair => !rows.Children.Contains(SwiftTransition.Container(pair.entry.View))).Select(pair => pair.index).ToHashSet();
                foreach (var row in folds)
                {
                    var container = SwiftTransition.Container(row);
                    if (arrivingAt.Contains(oldOrder.IndexOf(container))) rows.Children.Remove(container);
                    else FoldShut(rows, container);
                }
                foreach (var row in stale) rows.Children.Remove(SwiftTransition.Container(row));
                foreach (var row in leaving) SwiftTransition.Remove(rows, row);
                leavingViews.RemoveWhere(view => !rows.Children.Contains(view)); foreach (var row in leaving) if (rows.Children.Contains(SwiftTransition.Container(row))) leavingViews.Add(SwiftTransition.Container(row));

                for (var index = 0; index < wanted.Count; index++)
                {
                    var (view, arrives) = wanted[index]; var container = SwiftTransition.Container(view); var slot = SlotOf(rows, index); var at = rows.Children.IndexOf(container);
                    if (at == slot) continue;
                    if (at >= 0) rows.Children.Move(at, slot);
                    else if (arrives) SwiftTransition.Insert(rows, slot, view);
                    else SwiftTransition.Place(rows, slot, view);
                }
                if (tracking.SetOutline(blocks.Skip(WindowStartIndex(blocks)).Select(block => (block.Id, block.HasUserMessage)).ToArray()))
                {
                    tracking.ArmBlankWatch(); if (tracking.IsPinnedToBottom && !tracking.IsArriving) tracking.ArmViewportRefresh();
                } // :346-354
                // The column and its stack stay, like the ScrollView below: replacing them made the content extent collapse
                // and regrow inside one draw, which moved the offset and read as the reader scrolling.
                // :361-409 the column's stack is the pill (while history is hidden), the rows and the end sentinel: the rows and the sentinel stay as they are, and
                // the pill comes and goes in front of them, made again only when what it says changed (its count).
                var body = drawnBody ??= new StackPanel(); var hidden = WindowStartIndex(blocks);
                var sentinel = drawnSentinel as Border ?? new Border { Height = 1, Background = Brushes.Transparent }; // :402-409
                if (body.Children.Count == 0)
                {
                    body.Children.Add(rows);
                    // PORT-ONLY (SK-14, ED-384 item 16): Offer A is an inline card in the reply, so a chat with a skills offer flow draws its card, and the caption line it leaves, after the rows and before the end sentinel; both take no room while there is nothing to say.
                    Marrow.App.Port.Skills.SkillsOfferRows.Append(body, runtime, TimelineMetrics.RowSpacing);
                    Marrow.App.Port.MultiAccount.Cards.MultiAccountCardRows.Append(body, runtime, TimelineMetrics.RowSpacing);
                    Marrow.App.Port.MultiAccount.Opinions.OpinionAnswerRows.Append(body, runtime, TimelineMetrics.RowSpacing, () => rowViews.Values.Select(item => (item.Block, item.Row)).ToArray());
                    body.Children.Add(sentinel);
                }
                Button? earlier = hidden == 0 ? null : drawnEarlier is Button shown && earlierHidden == hidden ? shown : EarlierPill(hidden, () => { LoadEarlier(drawnBlocks); Draw(); });
                if (!ReferenceEquals(body.Children[0], rows) && !ReferenceEquals(body.Children[0], earlier)) body.Children.RemoveAt(0);
                if (earlier is not null && !ReferenceEquals(body.Children[0], earlier)) body.Children.Insert(0, earlier);
                earlierHidden = hidden;
                var width = CurrentLayoutWidth();
                Control column;
                if (drawnColumn is Grid kept) { column = kept; kept.Width = width ?? double.NaN; }
                else column = CreateColumn(body, SteppedHeight(columnHeight, 0, 0), width);
                HoldText(column); // ED-NEW-textsize-3
                // :384-440 one ScrollView for the life of the timeline. A new scroll view per draw started its geometry
                // history from zero, which read as "the reader was at the end": the first change re-pinned a reader who
                // had scrolled up and snapped them down on every new row (Swift :686-709 leaves them where they are).
                var created = currentScroll is null;
                if (currentScroll is not { } scroll)
                {
                    tracking.HoldEnd(1.5);
                    scroll = new ScrollViewer { Template = TimelineScrollTemplate, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
                    currentScroll = scroll; initialAnchorApplied = false;
                    SoftScroll.SetIsEnabled(scroll, true); // OD-0071 (ED-246, ED-333): eased wheel steps, momentum, a soft settle at the ends
                    scroll.SizeChanged += (_, _) => { viewportHeight = SteppedHeight(scroll.Bounds.Height, 0, 0); if (drawnColumn is { } current) current.MinHeight = viewportHeight; paneWidth = scroll.Bounds.Width; RefreshLayoutWidth(); }; // :446-450
                    var oldMetrics = new ScrollMetrics();
                    scroll.ScrollChanged += (_, change) =>
                    {
                        var next = ScrollMetrics.From(scroll.Extent.Height, scroll.Viewport.Height, scroll.Offset.Y, 0, 0, scroll.Offset.Y + scroll.Viewport.Height / 2);
                        // :439-441. Avalonia has no content-size anchor; retain the bottom-relative position before
                        // classifying movement, and apply the offset next turn, never from the geometry callback.
                        if (oldMetrics.ContentHeight > 0 && anchorsBottomOnGrowth && (change.ExtentDelta.Y != 0 || change.ViewportDelta.Y != 0))
                        {
                            // From the offset before this change: when the content shrinks under it, Avalonia has already
                            // clamped the offset (OffsetDelta), and anchoring from the clamped value took the shrink off twice.
                            var offset = Math.Clamp(next.Offset - change.OffsetDelta.Y + change.ExtentDelta.Y - change.ViewportDelta.Y, 0, Math.Max(0, next.ContentHeight - next.ContainerHeight));
                            if (Math.Abs(offset - next.Offset) > 1)
                            {
                                next = ScrollMetrics.From(next.ContentHeight, next.ContainerHeight, offset, 0, 0, offset + next.ContainerHeight / 2);
                                MainActor.Post(() => { if (ReferenceEquals(currentScroll, scroll)) scroll.Offset = new Vector(scroll.Offset.X, offset); });
                            }
                        }
                        // A timeline that is fading out under the prompt (OD-0076) has handed the chrome its travel of 0 (:139-140 `.onAppear`): what it still lays out is not the reader's.
                        NoteMetrics(oldMetrics, next, AppEnvironment.GetWindowLiveResize(this).IsActive); oldMetrics = next; if (TimelineOnShow) scrollChrome.Update(next.Travel);
                        ReportVisibility(scroll);
                    };
                    // :335-338,380-382,404-409 onScrollVisibilityChange reports whenever a view crosses its threshold, whatever moved it: a scroll, a row
                    // arriving, the pane resizing. A layout pass is the port's moment for all of them (a short thread growing past its window never scrolls).
                    scroll.LayoutUpdated += (_, _) => ReportVisibility(scroll);
                    // :463-485 .onScrollPhaseChange. ED-246: the shared soft scrolling reports the phases a touchpad stroke,
                    // its coast and a programmatic animation go through; a wheel reports none, as on the Mac (:466-467).
                    scroll.AddHandler(SoftScroll.PhaseChangedEvent, (_, change) => NotePhase(change.Phase));
                    scrollOverlay = new Grid { Children = { scroll } };
                }
                var oldOffset = scroll.Offset;
                if (!created && scroll.Bounds.Height > 0) column.MinHeight = SteppedHeight(scroll.Bounds.Height, 0, 0);
                viewportHeight = column.MinHeight;
                drawnColumn = column; drawnSentinel = sentinel; drawnEarlier = earlier; drawnBlocks = blocks;
                if (!ReferenceEquals(scroll.Content, column)) scroll.Content = column;
                var overlay = scrollOverlay!;
                // ED-NEW-lean-14: a pass with the blocks, the flag and the height the rail was last asked for keeps it without making a new one to compare (InputsEqual
                // holds for those: the same list, the same tracking, the same height), so a pass that changed nothing the rail reads costs it nothing.
                var nextRail = railInputs is { } asked && ReferenceEquals(asked.Blocks, blocks) && asked.Shows == showsMinimap && asked.Height == columnHeight ? currentRail : CreateRail(blocks, showsMinimap, columnHeight);
                railInputs = (blocks, showsMinimap, columnHeight);
                // :190 .equatable(): a rail whose inputs did not change is not drawn again, so its hover and its scrub go on across a redraw.
                if (currentRail is TimelineMinimapColumn oldRail && nextRail is TimelineMinimapColumn newRail && TimelineMinimapColumn.InputsEqual(oldRail, newRail)) nextRail = oldRail;
                if (!ReferenceEquals(nextRail, currentRail))
                {
                    var previous = currentRail; currentRail = nextRail;
                    // :195-200 `.transition(.opacity)` under `.animation(Chrome.panelSlide, value: showsMinimap)`: the rail fades in and out with the slide of the
                    // panel that takes its edge, held while the window is dragged (a fresh slide every frame lagged the chat behind the window). A rail whose
                    // ticks changed is the same view with new content, not a new rail. A timeline that comes in whole brings its rail with it and has none of the one before.
                    var fades = (previous is null) != (nextRail is null) && !AppEnvironment.GetWindowLiveResize(this).IsReshaping && !comesWhole;
                    Transaction.WithAnimation(fades ? Chrome.PanelSlide : null, () =>
                    {
                        if (previous is not null) SwiftTransition.Remove(overlay, previous);
                        if (nextRail is not null) SwiftTransition.Insert(overlay, overlay.Children.Count, nextRail);
                    });
                }
                branches.Show(overlay, fadesIn: true); // the timeline comes in over a prompt that fades out, with all it shows (a thread that opens on rows has nothing to fade from: the first branch is placed whole)
                MainActor.Post(() =>
                {
                    if (!ReferenceEquals(currentScroll, scroll)) return;
                    if (tracking.IsPinnedToBottom) SnapEnd(); else scroll.Offset = oldOffset;
                    // :438 .defaultScrollAnchor(.bottom, for: .initialOffset): a thread opens at its end, and no row reports itself before that (the first layout
                    // at offset 0 shows the top of the window, which the earlier pill would take for the reader arriving at it).
                    if (!initialAnchorApplied) { initialAnchorApplied = true; scroll.InvalidateArrange(); } // a layout pass at the anchored offset reports the rows' visibility
                });
                if (!initialHistoryStarted) StartInitialHistoryLoad(blocks); // :594-607; task starts only with a timeline, never with the empty prompt.
            }
        }
        this.Track(() => (runtime.IsLoadingHistory, runtime.Entries, runtime.IsRunning, runtime.IsHydraMerging, runtime.HydraMergeNoteID, AppEnvironment.GetAppModel(this).Settings.ShowReasoning), _ => Draw());
        this.Track(() => runtime.Questions, _ => Draw());
        this.Track(() => runtime.TactusAsks, _ => Draw()); // PORT-ONLY: Tactus (ED-NEW-pcctl-27)
        // :99-131,167-172 the body reads the model as well as the runtime: the heads' finish times (the pills), their personas and chats (the
        // mentions), the roster flag, and the runtime's turn start and turn records (the turns a row may offer to revert). It runs again when
        // one of those moved; a head's token count, or another thread changing, is not one of them.
        this.Track(() => ReadModelInputs(runtime), inputs => { if (seenModelInputs is { } previous && !previous.SameAs(inputs)) Draw(); });
        redraw = Draw;
        // ChatView.swift:97-123: the view keeps its identity (.id(runtime.threadID)) while the supplied layout values change,
        // so a changed column height or minimap flag updates this timeline in place instead of replacing it.
        updateInputs = (nextProject, nextDirectory, nextRewind, nextHeight, nextMinimap) =>
        {
            projectName = nextProject; workingDirectory = nextDirectory; supportsRewind = nextRewind; columnHeight = nextHeight; showsMinimap = nextMinimap;
            inputs = (runtime, scrollChrome, scrollState, nextProject, nextDirectory, nextRewind, nextHeight, nextMinimap);
            Draw();
        };
        DetachedFromVisualTree += (_, _) => { initialHistoryTask?.Cancel(); initialHistoryTask = null; initialHistoryStarted = false; tracking.EndActivity(); FollowZoom(null); };
        AttachedToVisualTree += (_, _) => FollowZoom(ZoomMotion.For(this)); // ED-NEW-textsize-3
        this.Track(() => scrollState.JumpRequest, _ => { tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; EndWithAnimation(scrollState.JumpRequest == 0 ? null : Animation.Smooth(0.35)); });
        this.Track(() => runtime.IsRunning, running => { if (seenRunning is { } previous && previous != running) NoteTurnChange(running, runtime.AutoStartedTurnToken); seenRunning = running; });
        this.Track(() => scrollState.AreaGrowthRequest, _ => { if (scrollState.AreaGrowthRequest != 0) ComposerGrew(AppEnvironment.GetWindowLiveResize(this).IsActive); });
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window window) { FrontMonitor.Shared.Current.Observe(window); tracking.Probe = this; }
        };
        this.Track(() => FrontMonitor.Shared.Current.Revision, _ => { if (FrontMonitor.Shared.Current.Concerns(TopLevel.GetTopLevel(this) as Avalonia.Controls.Window)) tracking.NoteReturnToFront(); });
        // :609-615 `.onChange(of: tracking.checkRequest)`: the app hears that a scroll ended, and the timeline checks its own layout. A request is asked for by the return to the
        // front (:616-623), by the blank watch and by the phase watch (TimelineScrollTracking); the first read is not a change.
        int? seenCheck = null;
        this.Track(() => tracking.CheckRequest, request => { if (seenCheck is { } previous && previous != request) { tracking.ReportActivity(runtime.ThreadID); RepairLayout(); } seenCheck = request; });
        // :59-67 layoutWidth is read by the body, which runs again when the reshape starts or ends, when the pane's width moves (onGeometryChange,
        // :450) and while the sidebar slides: the stepped width follows all of them, and the exact width lands when the drag ends (:454-457).
        this.Track(() => AppEnvironment.GetWindowLiveResize(this).IsReshaping, active => { RefreshLayoutWidth(); if (!active) SettleAfterResize(); else if (currentScroll is { } resizing) SoftScroll.StopAnimation(resizing); });
        // :458-462 `.onChange(of: sidebar.isSliding) { if $1 { paneWidthAtSlideStart = paneWidth } }`. DEVIATION (ED-NEW-sidebarmodes-8): a
        // slide that begins while one runs (a second press turning the column round) leaves isSliding true, so the Mac keeps the first
        // slide's start and adds the new delta to it, a width the pane never reaches (the text too wide, then a re-wrap as the slide
        // ends). A slide that begins mid-slide starts here from the width the running one was heading for.
        int? seenEpoch = null;
        var wasSliding = false;
        var runningDelta = 0.0;
        this.Track(() =>
        {
            var sidebar = AppEnvironment.GetAppModel(this).Sidebar;
            return (sidebar.IsSliding, sidebar.SlideEpoch, sidebar.SlideDelta);
        }, slide =>
        {
            if (slide.IsSliding && (!wasSliding || seenEpoch != slide.SlideEpoch))
                paneWidthAtSlideStart = wasSliding && paneWidthAtSlideStart > 0 ? paneWidthAtSlideStart + runningDelta : paneWidth;
            seenEpoch = slide.SlideEpoch;
            wasSliding = slide.IsSliding;
            runningDelta = slide.SlideDelta;
            RefreshLayoutWidth();
        });
    }

    // :59-67. No lower clamp in the ordinary width branch; only the sidebar-slide target is clamped to 12.
    private static double? SteppedWidth(bool reshaping, double paneWidth, bool sliding, double slideStart, double slideDelta) => !reshaping || paneWidth <= 0 ? null : sliding && slideStart > 0 ? Math.Min(860, Math.Max(ResizeStep, slideStart + slideDelta)) : Math.Min(860, Math.Floor(paneWidth / ResizeStep) * ResizeStep);
    // :59-67 private var layoutWidth: CGFloat? with this view's own inputs.
    private double? CurrentLayoutWidth()
    {
        var model = AppEnvironment.GetAppModel(this);
        return SteppedWidth(AppEnvironment.GetWindowLiveResize(this).IsReshaping, paneWidth, model.Sidebar.IsSliding, paneWidthAtSlideStart, model.Sidebar.SlideDelta);
    }

    // PORT-ONLY (ED-NEW-textsize-3): the window root's zoom motion, followed while the timeline is in a window. While it moves,
    // the column's text keeps the width it has at the new scale: it wraps once, on the frame of the press, and is drawn at each
    // frame's scale, instead of rewrapping every frame (9-12 ms a frame for a narrow column, measured) or at the end (a jump).
    // The column around it takes each frame's width, so the text starts where it does at rest at every scale on the way.
    private ZoomMotion? zoom;

    // The text's width at the zoom's target scale, taken at each press; null where it is 820 points at every scale on the way
    // (nothing rewraps). Kept at rest until the pane's own text width differs from it by a point (layout rounding aside).
    private double? zoomHeld;

    private void FollowZoom(ZoomMotion? next)
    {
        if (ReferenceEquals(zoom, next)) return;
        if (zoom is { } old) { old.Retargeted -= ZoomChanged; old.Rested -= ZoomChanged; }
        zoom = next;
        if (next is not null) { next.Retargeted += ZoomChanged; next.Rested += ZoomChanged; }
    }

    private void ZoomChanged(double _)
    {
        if (zoom is { IsMoving: true } moving && paneWidth > 0 && !AppEnvironment.GetWindowLiveResize(this).IsReshaping)
        {
            var target = paneWidth * moving.Value / moving.Target; // the pane at the target scale (its pixels do not change)
            zoomHeld = Math.Min(paneWidth, target) < 860 ? TextWidth(target) : null;
        }
        RefreshLayoutWidth();
    }

    // :416 the text's width in a pane: 820 points at most, 20 points in from each side.
    private static double TextWidth(double pane) => Math.Min(820, pane - 40);

    private void HoldText(Control column)
    {
        if (column is not Grid { Children.Count: > 0 } grid || grid.Children[0] is not Border { Child: Grid stack }) return;
        if (zoomHeld is { } held && (AppEnvironment.GetWindowLiveResize(this).IsReshaping || zoom is not { IsMoving: true } && Math.Abs(TextWidth(paneWidth) - held) >= 1)) zoomHeld = null;
        stack.Width = zoomHeld ?? double.NaN;
        stack.HorizontalAlignment = zoomHeld is null ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    // :418 .frame(width: layoutWidth, alignment: .leading), taken again whenever one of layoutWidth's inputs changed.
    private void RefreshLayoutWidth()
    {
        if (drawnColumn is not { } column) return;
        column.Width = CurrentLayoutWidth() ?? double.NaN;
        HoldText(column);
    }

    // :446-449,891-893
    private static double SteppedHeight(double height, double topInset, double bottomInset) => Math.Floor(Math.Max(0, height - topInset - bottomInset) / ResizeStep) * ResizeStep;
    // :388,400 VStack(alignment: .leading, spacing: TimelineMetrics.rowSpacing) with the scroll freeze on it.
    private StackPanel MakeRows()
    {
        var rows = new StackPanel { Spacing = TimelineMetrics.RowSpacing }; new ScrollFreeze(tracking).Body(rows); return rows;
    }

    // Where the live-th view that is not on its way out belongs in the stack (the badges fading out stand in it still, and are no part of its order).
    // OD-0105 (1): "rows that leave together fold shut together, in about 0.3 s". ENGINEERING DECISION: the height closes from the row's own to nothing and the row
    // fades with it, on the ease-out of the timeline's other swaps at that length; the rows of one pass start in the same frame, so they move as one. A folding row
    // stands in the stack in nobody's order (leavingViews), takes no input and is taken out once shut.
    internal static Animation RowFold => Animation.EaseOut(duration: 0.3);
    private void FoldShut(StackPanel rows, Control container)
    {
        if (!rows.Children.Contains(container)) return;
        var height = container.Bounds.Height;
        if (height <= 0) { rows.Children.Remove(container); return; }
        container.IsHitTestVisible = false; container.ClipToBounds = true; container.Height = height; container.VerticalAlignment = VerticalAlignment.Top;
        leavingViews.Add(container);
        var fold = new AnimatedDouble(open =>
        {
            container.Height = height * open; container.Opacity = open;
            if (open <= 0) { rows.Children.Remove(container); leavingViews.Remove(container); }
        });
        fold.Set(1, null); fold.Set(0, RowFold);
    }

    private int SlotOf(StackPanel rows, int live)
    {
        var seen = 0;
        for (var at = 0; at < rows.Children.Count; at++) { if (leavingViews.Contains(rows.Children[at])) continue; if (seen++ == live) return at; }
        return rows.Children.Count;
    }

    // :416-422,325. The star row is Spacer(minLength: 0), not a fixed gap.
    private Control CreateColumn(Control rows, double viewportHeight, double? layoutWidth)
    {
        var stack = new Grid { RowDefinitions = new RowDefinitions("*,Auto") }; Grid.SetRow(rows, 1); stack.Children.Add(new Border()); stack.Children.Add(rows);
        var content = new Border { MaxWidth = 820, Margin = new Thickness(20, Chrome.ContentTopInset, 20, 18), Child = stack, HorizontalAlignment = HorizontalAlignment.Stretch };
        NthWorkingBeads.SetReflowAnchor(content, true); // Calculi WU5: the working glyph's layout moves are measured inside the scrolled content (a scroll is not a move)
        var column = new Grid { MinHeight = viewportHeight, Children = { content } }; if (layoutWidth is { } width) column.Width = width; return column;
    }
    // :272-278
    private int WindowStartIndex(IReadOnlyList<DisplayBlock> blocks)
    {
        // ED-NEW-lean-14: looked for from the end, where the window starts (block ids are unique: they key the rows), not across the whole timeline.
        if (windowStartID is not null) for (var index = blocks.Count - 1; index >= 0; index--) if (blocks[index].Id == windowStartID) return index;
        return Math.Max(0, blocks.Count - TimelineWindow.FirstPaint);
    }
    // :257-268
    private void LoadEarlier(IReadOnlyList<DisplayBlock> blocks, int? count = null)
    {
        if (historyLoadPending) return; var start = WindowStartIndex(blocks); var target = count is { } wanted ? Math.Max(0, blocks.Count - wanted) : Math.Max(0, start - TimelineWindow.Page);
        if (target >= start) return; historyLoadPending = true; anchorsBottomOnGrowth = true; windowStartID = blocks[target].Id;
    }
    // :366-382
    private Button EarlierPill(int hidden, Action load)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Children = { SwiftImage.SystemName("chevron.up").Font(ChatFont.Chat(SwiftFont.TextStyle.Callout)), new TextBlock { Text = $"Show {hidden} earlier messages" }.Font(ChatFont.Chat(SwiftFont.TextStyle.Callout)) } };
        label.SetValue(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, SwiftColor.Secondary.Brush(this));
        var pill = PlainControls.Button(new Border { Padding = new Thickness(14, 7), CornerRadius = new CornerRadius(999), Background = SwiftColor.Quaternary.Opacity(0.5).Brush(this), Child = label }, load);
        pill.HorizontalAlignment = HorizontalAlignment.Center; pill.Margin = new Thickness(0, TimelineMetrics.RowSpacing, 0, 0); return pill;
    }

    // :148-155 private static func mentionTargets(for:) -> [String: HydraMentionTarget]
    private static ValueMap<string, HydraMentionTarget> MentionTargets(IReadOnlyList<ChatThread> @for) =>
        new(@for.OrderBy(head => head.CreatedAt).Where(head => head.Hydra is not null).Aggregate(new Dictionary<string, HydraMentionTarget>(StringComparer.Ordinal), (map, head) => { map[head.Hydra!.Persona.Name] = new HydraMentionTarget { ThreadID = head.Id, Task = head.Hydra.Task, Status = head.Hydra.HydraHeadInfoStatus }; return map; })); // :148-155

    // What the body reads from the model and the runtime besides the entries (:99-131, :167-172), as the values the rows are built from.
    private sealed record ModelInputs(HeadPills Pills, ChatThread[] Heads, bool Roster, ValueMap<string, HydraMentionTarget> Targets, ValueList<Guid> Rewindable, DateTimeOffset? Since)
    {
        // The heads enter the rows by roster index (the personas), the pills and the targets only; the chat records themselves are not compared.
        public bool SameAs(ModelInputs other) =>
            Pills == other.Pills && Roster == other.Roster && Since == other.Since && Targets.Equals(other.Targets) && Rewindable.Equals(other.Rewindable)
            && Heads.Select(head => head.Hydra?.Index).SequenceEqual(other.Heads.Select(head => head.Hydra?.Index));
    }
    private ModelInputs? seenModelInputs;

    private ModelInputs ReadModelInputs(ThreadRuntime runtime)
    {
        var model = AppEnvironment.GetAppModel(this);
        // :105-110 every unarchived head, in the panel or not; the finished ones by finish order, split at the running turn's start.
        var team = model.Helpers(runtime.ThreadID).Where(thread => thread.IsHydraHead).ToArray();
        var finished = team.Length == 0 ? new ValueList<HydraHeadInfo>([]) : HydraHeadsWorkingRow.FinishedHeads(team);
        var since = runtime.IsRunning ? runtime.TurnStartedAt : null;
        // :123-131 every head the chat ever sent out, the roster flag, and the targets oldest first.
        var heads = model.Children(runtime.ThreadID).Where(thread => thread.IsHydraHead).ToArray();
        var roster = model.Thread(runtime.ThreadID) is { } thread && model.HydraIsOn(thread);
        // :167-172 the turns a row may offer to revert, read once here.
        var rewindable = new ValueList<Guid>(RewindableIDs(inputs.Rewind, runtime.IsRunning, runtime.Turns.Select(turn => turn.Id)));
        return new ModelInputs(SplitHeadPills(finished, since), heads, roster, MentionTargets(heads), rewindable, since);
    }

    // :104-110. Nil finish dates sort as distantPast; membership is by roster index, not record identity.
    private static HeadPills SplitHeadPills(IReadOnlyList<HydraHeadInfo> finished, DateTimeOffset? since)
    {
        var sorted = finished.OrderBy(head => head.FinishedAt ?? SwiftDate.DistantPast).ToArray();
        var earlier = since is { } cut ? sorted.Where(head => (head.FinishedAt ?? SwiftDate.DistantPast) < cut).ToArray() : [];
        var indices = earlier.Select(head => head.Index).ToHashSet();
        return new HeadPills { Earlier = new(earlier.Select(head => checked((int)head.Index))), Later = new(sorted.Where(head => !indices.Contains(head.Index)).Select(head => checked((int)head.Index))) };
    }

    // :307-311 private static func transition(for:) -> AnyTransition
    private static SwiftTransition Transition(DisplayBlock @for) =>
        @for is DisplayBlock.Turn { Summary: not null } or DisplayBlock.Group { Value: TimelineGroup.Work { Entries.Count: > SoftAppearLimit } } ? SwiftTransition.Identity : RowTransition; // :307-314

    // Chrome.swift:949-955 AnyTransition.softAppear as the rows use it (:396 the question badges, :2167 and :2171 the working line's opened body): arrives with the
    // soft appear, leaves with a quick 0.12 s ease-out fade. The insertion is decomposed into the modifier's layer effects (Chrome.swift:938-945) for the reason
    // RowTransition gives.
    internal static SwiftTransition SoftAppearTransition { get; } = SwiftTransition.Asymmetric(
        SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y: 4)).Animation(Animation.SoftAppear),
        SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.12)));

    // :317-321 static let rowTransition: AnyTransition
    public static SwiftTransition RowTransition =>
        // :317-321; Chrome.swift:940-944. Decompose the modifier's exact layer effects so the shared transition
        // interpolates them; its Bool-only modifier path otherwise applies identity immediately.
        SwiftTransition.Asymmetric(SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y: 4)).Animation(Animation.SoftAppear), SwiftTransition.Identity);

    // :824-826 private static func jumpTolerance(_:) -> CGFloat
    private static double JumpTolerance(double viewportHeight) =>
        Math.Max(48, viewportHeight / 4); // :833-835

    // :180-200. Width gating belongs to ChatView, which supplies showsMinimap.
    private static bool ShouldShowMinimap(bool enabled, IReadOnlyList<DisplayBlock> blocks)
    {
        if (!enabled) return false; var users = 0; // ED-NEW-lean-14: `count > 1` is known at the second block with a user message
        for (var index = 0; index < blocks.Count; index++) if (blocks[index].HasUserMessage && ++users > 1) return true;
        return false;
    }
    // :179-208
    private Control? CreateRail(IReadOnlyList<DisplayBlock> blocks, bool enabled, double columnHeight)
    {
        if (!ShouldShowMinimap(enabled, blocks)) return null;
        var rail = new TimelineMinimapColumn(blocks, tracking, Math.Max(0, columnHeight - WindowChrome.TitlebarHeight), (id, animated) => Jump(id, blocks, animated)) { Width = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, WindowChrome.TitlebarHeight, 0, 0) };
        // :201-207 the rail belongs to the column's leading edge, not to the conversation that re-centres beside a docked panel: the padding the slide adds
        // (reserveSlideLeading, snapped at once) and the glide that carries the rows (reserveSlideOffset, every frame of it) are both cancelled, live.
        var cancel = new TranslateTransform(); rail.RenderTransform = cancel;
        void Follow() => cancel.X = -ChatView.GetReserveSlideLeading(rail) - ChatView.GetReserveSlideOffset(rail);
        rail.PropertyChanged += (_, change) => { if (change.Property == ChatView.ReserveSlideLeadingProperty || change.Property == ChatView.ReserveSlideOffsetProperty) Follow(); };
        rail.AttachedToVisualTree += (_, _) => Follow();
        Follow();
        return rail;
    }
    // :114
    private static string? MergeID(bool merging, string? noteID) => merging ? noteID ?? "hydra-merging" : null;
    // :167-172
    private static HashSet<Guid> RewindableIDs(bool supportsRewind, bool running, IEnumerable<Guid> turns) => supportsRewind && !running ? turns.ToHashSet() : [];
    // :594-607. The source task captures this pass's immutable blocks, not a rebuilt list after sleeping.
    private void StartInitialHistoryLoad(IReadOnlyList<DisplayBlock> blocks)
    {
        initialHistoryTask?.Cancel(); initialHistoryStarted = true;
        initialHistoryTask = MainActor.Spawn(async () =>
        {
            if (!await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(250))) return;
            tracking.HoldEnd(1.0); LoadEarlier(blocks, TimelineWindow.Initial); redraw?.Invoke(); tracking.ArmBlankWatch(); await WarmMarkdown();
        });
    }
    // :335-338,379-382,404-409. Intersections are area fractions, not pixel distances.
    // `above` is the view's own space above its bounds that its visibility frame includes (padding applied before onScrollVisibilityChange, :379-380).
    private static double VisibleFraction(Control row, ScrollViewer scroll, double above = 0)
    {
        var point = row.TranslatePoint(default, scroll); if (point is null || row.Bounds.Width <= 0 || row.Bounds.Height <= 0) return 0;
        var rect = new Rect(point.Value.X, point.Value.Y - above, row.Bounds.Width, row.Bounds.Height + above); var intersection = rect.Intersect(new Rect(scroll.Viewport)); return intersection.Width * intersection.Height / (rect.Width * rect.Height);
    }
    // :335-338 each row's own visibility (threshold 0.001), :404-409 the end sentinel's (0.01), :380-382 the earlier pill's (0.1).
    private void ReportVisibility(ScrollViewer scroll)
    {
        if (!ReferenceEquals(currentScroll, scroll) || !initialAnchorApplied || !TimelineOnShow) { reported = null; return; } // rows that are fading out under the prompt report nothing (the jump button, a page of history)
        // ED-NEW-perf-motion-8: every layout pass of the window raises LayoutUpdated (a keystroke in the composer, a sidebar frame); the rows' places
        // in the viewport change only with the offset, the extent, the viewport or the rows drawn, so a pass that changed none of them reports nothing new.
        var geometry = (scroll.Offset, scroll.Extent, scroll.Viewport, rowsDrawn);
        if (reported == geometry) return;
        reported = geometry;
        VisibilityReports++;
        foreach (var pair in laidOutRows) tracking.SetVisible(pair.Key, VisibleFraction(pair.Value, scroll) >= 0.001);
        // :404-409 the end sentinel reports when it crosses its threshold, not on every layout: a row landing under a reader who is following it has the
        // button hidden (:710) and not shown again for the beat before the scroll that brings the end back.
        if (drawnSentinel is { } end && VisibleFraction(end, scroll) >= 0.01 is var seen && endSeen != seen) { endSeen = seen; SetEndVisible(seen); }
        // The pill loads a page when it comes into view, not on every report while it stays there: the action of onScrollVisibilityChange is
        // called when the visibility crosses the threshold (a refused load, :258, is not retried either).
        var visible = drawnEarlier is { } pill && VisibleFraction(pill, scroll, pill.Margin.Top) >= 0.1; // the 20 points above the pill are inside the frame the modifier measures (:379-380)
        var appeared = visible && !earlierPillVisible; earlierPillVisible = visible;
        if (appeared && !historyLoadPending) { LoadEarlier(drawnBlocks); MainActor.Post(() => redraw?.Invoke()); }
    }
    private bool earlierPillVisible;
    private bool? endSeen;
    private (Vector Offset, Size Extent, Size Viewport, int Rows)? reported;
    private int rowsDrawn;

    /// <summary>Visibility reports that walked the rows (for tests).</summary>
    internal int VisibilityReports { get; private set; }
    private bool initialAnchorApplied;
    // The timeline is the branch on show (not fading out under the prompt, not under the loading mark).
    private bool TimelineOnShow => scrollOverlay is { } overlay && ReferenceEquals(branches.On, overlay);

    /// <summary>
    /// ENGINEERING DECISION (OD-0076, ED-255 (3)): the branches of the body (:134-144) as the layers of one host. The branch on show is <see cref="On"/>; a swap between the
    /// prompt and the timeline (the Mac swaps them in one frame, the first message sent and a turn that ends with nothing said alike) fades the branch that leaves out where it stood while the one that
    /// comes in fades in (the prompt's own appear, NewThreadPrompt.swift:50-53, is its fade-in), over 0.3 s on the ease-out of that appear, on the frame clock. A swap turned back part way goes on from
    /// the opacity each layer has. The system's reduce-motion setting stills it. OD-0105 (1), the owner's pick for the loading mark's hand-over (the Mac opens a thread in one frame):
    /// the mark fades out while the branch that comes in fades and rises in; OD-0136 amendment 3 (ED-NEW-nth-motion) makes the mark the nth Flick and the hand-over its spec's:
    /// the loader leaves on its own in 0.3 s while the messages rise 10 px and fade in, 0.5 s, 150 ms later, 45 ms apart (HandOver). The prompt comes in with its own appear
    /// and the loader leaves under it. A swap to the loading mark stays a cut (the mark draws nothing for its first 400 ms).
    /// A layer that is leaving takes no input and is taken out of the tree when it is gone.
    /// </summary>
    private sealed class BranchHost : Panel
    {
        private static Animation Fade => Animation.EaseOut(duration: 0.3);
        // OD-0105 (1): "in about 0.2 s"; ENGINEERING DECISION: the ease-out of the other branch swaps, at that length.
        // OD-0136 amendment 3 (spec "Hand-over"): 0.5 s on cubic-bezier(.2,.8,.2,1), 10 px, 150 ms after the loader starts to leave, 45 ms apart.
        internal static Animation HandOver => Animation.TimingCurve(.2, .8, .2, 1, duration: 0.5);
        internal const double HandOverRise = 10, HandOverDelay = .15, HandOverStagger = .045;
        internal const int HandOverStaggered = 8;
        private readonly Func<bool> stilled;
        private readonly Dictionary<Control, AnimatedDouble> fades = [];
        private readonly HashSet<Control> leaving = [];

        // OD-0136 amendment 3: the history hand-over raises the messages one by one; the host is told where the rows are.
        private readonly Func<IReadOnlyList<Control>?> rows;

        public BranchHost(Func<bool> stilled, Func<IReadOnlyList<Control>?> rows) { this.stilled = stilled; this.rows = rows; }

        /// <summary>The branch on show.</summary>
        public Control? On { get; private set; }

        /// <summary>Whether <paramref name="layer"/> is one of the layers here (on show, or leaving and still partly showing): a layer that has gone is not.</summary>
        public bool Holds(Control layer) => Children.Contains(layer);

        /// <summary>Puts <paramref name="next"/> on show. <paramref name="fadesIn"/>: this host brings it in (the timeline); a branch that comes in with its own appear (the prompt) is left to it.</summary>
        public void Show(Control next, bool fadesIn)
        {
            if (ReferenceEquals(On, next)) return;
            var previous = On; On = next;
            if (previous is NthFlick flick) { HandOverFrom(flick, next, fadesIn); return; }
            if (previous is null || next is NthFlick || stilled()) { Cut(next); return; }
            var back = Children.Contains(next); // a layer that was leaving, turned back
            Leave(previous);
            if (!back) { next.Opacity = fadesIn ? 0 : 1; Children.Add(next); }
            leaving.Remove(next); next.IsHitTestVisible = true;
            if (fadesIn || back) Animate(next, 1);
        }

        // OD-0136 amendment 3 (ED-NEW-nth-motion), the history loader's hand-over: the loader leaves on its own (opacity to 0 in 0.22 s, scale to 0.9 in 0.3 s,
        // NthFlick.Finish) and is taken out when it has gone; the messages rise 10 to 0 px and fade in, 0.5 s (.2,.8,.2,1), starting 150 ms later, 45 ms apart.
        // ENGINEERING DECISION: the thread opens at its end, so the stagger runs over the last HandOverStaggered rows (top to bottom); the rows above them, out of
        // sight, move with the first. Reduce motion: the loader fades, the messages fade in without rising (the spec's "opacity only").
        private void HandOverFrom(NthFlick mark, Control next, bool fadesIn)
        {
            foreach (var layer in Children.ToArray()) if (!ReferenceEquals(layer, next) && !ReferenceEquals(layer, mark)) Drop(layer);
            mark.Finish(); mark.IsHitTestVisible = false; leaving.Add(mark);
            var gone = new AnimatedDouble(progress => { if (progress >= 1 && leaving.Contains(mark)) Drop(mark); });
            gone.Set(0, null); gone.Set(1, Animation.Linear(NthFlick.LeaveSeconds));
            Stop(next); leaving.Remove(next); next.IsHitTestVisible = true; next.Opacity = 1;
            if (!Children.Contains(next)) Children.Add(next);
            if (!fadesIn) return;
            var calm = stilled();
            var all = rows() is { Count: > 0 } list && next is Grid ? list.ToArray() : [next];
            var first = Math.Max(0, all.Length - HandOverStaggered);
            for (var i = 0; i < all.Length; i++) Rise(all[i], Math.Max(0, i - first), calm);
        }

        private static void Rise(Control row, int order, bool calm)
        {
            if (row.RenderTransform is not null && row.RenderTransform is not TranslateTransform) { row.Opacity = 1; return; } // a row moving on its own keeps its motion
            var rise = calm ? null : new TranslateTransform(0, HandOverRise);
            if (rise is not null) row.RenderTransform = rise;
            row.Opacity = 0;
            var arrival = new AnimatedDouble(progress =>
            {
                row.Opacity = progress;
                if (rise is not null) { rise.Y = HandOverRise * (1 - progress); if (progress >= 1 && ReferenceEquals(row.RenderTransform, rise)) row.RenderTransform = null; }
            });
            arrival.Set(0, null); arrival.Set(1, HandOver.Delay(HandOverDelay + HandOverStagger * order));
        }

        private void Cut(Control next)
        {
            foreach (var layer in Children.ToArray()) if (!ReferenceEquals(layer, next)) Drop(layer);
            Stop(next); next.Opacity = 1; next.IsHitTestVisible = true;
            if (!Children.Contains(next)) Children.Add(next);
        }

        private void Leave(Control layer, Animation? curve = null)
        {
            layer.IsHitTestVisible = false; leaving.Add(layer);
            Animate(layer, 0, curve);
        }

        // The layer's opacity goes to <paramref name="target"/> from where it is now. A leaving layer is taken out when it has gone.
        private void Animate(Control layer, double target, Animation? curve = null)
        {
            if (!fades.TryGetValue(layer, out var fade))
                fades[layer] = fade = new AnimatedDouble(value => { layer.Opacity = value; if (value <= 0 && leaving.Contains(layer)) Drop(layer); });
            var isLeaving = leaving.Remove(layer); fade.Set(layer.Opacity, null); if (isLeaving) leaving.Add(layer); // primed with the opacity it has: the motion goes on from there
            fade.Set(target, curve ?? Fade);
        }

        private void Stop(Control layer)
        {
            if (fades.TryGetValue(layer, out var fade)) { leaving.Remove(layer); fade.Set(layer.Opacity, null); }
        }

        private void Drop(Control layer)
        {
            Stop(layer); fades.Remove(layer); leaving.Remove(layer); Children.Remove(layer);
        }
    }

    // :402-409
    private void SetEndVisible(bool visible) { if (state is not null && state.ShowsJumpButton == visible) Transaction.WithAnimation(Animation.Spring(response: 0.38, dampingFraction: 0.78), () => state.ShowsJumpButton = !visible); }
    // :224-252
    private void Jump(string id, IReadOnlyList<DisplayBlock> blocks, bool animated)
    {
        var index = blocks.Select((block, index) => (block, index)).Where(pair => pair.block.Id == id).Select(pair => (int?)pair.index).FirstOrDefault(); var expand = false;
        if (index is { } found) { if (found < WindowStartIndex(blocks)) { windowStartID = blocks[found].Id; historyLoadPending = expand = true; } tracking.IsPinnedToBottom = id == blocks.LastOrDefault()?.Id; anchorsBottomOnGrowth = tracking.IsPinnedToBottom || expand; }
        void Scroll() { if (currentScroll is not null && laidOutRows.TryGetValue(id, out var row) && row.TranslatePoint(default, currentScroll) is { } point) ScrollTo(new Vector(currentScroll.Offset.X, currentScroll.Offset.Y + point.Y), animated ? Animation.Smooth(0.35) : null); }
        if (expand) { redraw?.Invoke(); MainActor.Post(() => Transaction.WithAnimation(animated ? Animation.Smooth(0.35) : null, Scroll)); } else Transaction.WithAnimation(animated ? Animation.Smooth(0.35) : null, Scroll);
    }
    // :813-829
    private void RevealEnd() { if (state is null || !state.ShowsJumpButton || tracking.IsPinnedToBottom || tracking.DistanceFromBottom < viewportHeight) FollowEnd(); }
    private void FollowEnd() { tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; EndWithAnimation(Transaction.Current); NoteScrollMovement(); }
    private void SnapEnd() => EndWithAnimation(null);
    private void EndWithAnimation(Animation? animation) { if (currentScroll is { } scroll) ScrollTo(new Vector(scroll.Offset.X, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)), animation); }
    // :242-252. Avalonia offsets are not SwiftUI state; the source curve runs on the shared soft scrolling (ED-246), which
    // the reader's own wheel or touchpad takes over from, and which an unanimated move sets at once. The curve is followed
    // exactly to its duration, as before ED-246 here (ED-333: ED-246 (4)'s run-out spring is not ruled on for it).
    private void ScrollTo(Vector target, Animation? animation)
    {
        if (currentScroll is { } scroll) SoftScroll.ScrollTo(scroll, target, animation, runsOut: false);
    }
    // :463-485
    private void NotePhase(ScrollPhase phase)
    {
        tracking.NotePhase(phase switch { ScrollPhase.Interacting => "interacting", ScrollPhase.Decelerating => "decelerating", ScrollPhase.Animating => "animating", _ => "idle" });
        if (phase == ScrollPhase.Interacting) NoteScrollMovement(); else LiftScrollFreeze();
        if (phase != ScrollPhase.Idle) return;
        if (tracking.RepairSkippedWhileScrolling) { tracking.RepairSkippedWhileScrolling = false; tracking.ArmBlankWatch(); }
        if (tracking.RefreshSkippedWhileScrolling) { tracking.RefreshSkippedWhileScrolling = false; tracking.ArmViewportRefresh(); }
    }
    // :747-752
    private void LiftScrollFreeze()
    {
        tracking.SettleWatch?.Cancel(); tracking.SettleWatch = null;
        if (tracking.IsReaderScrolling) tracking.IsReaderScrolling = false;
        if (ownerRuntime is not null) tracking.ReportActivity(ownerRuntime.ThreadID);
    }
    // :727-745
    private void NoteScrollMovement()
    {
        tracking.LastMovementAt = SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond; if (tracking.IsReaderScrolling) return;
        tracking.IsReaderScrolling = true; if (ownerRuntime is not null) tracking.ReportActivity(ownerRuntime.ThreadID); tracking.SettleWatch?.Cancel();
        tracking.SettleWatch = MainActor.Spawn(async () => { while (await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(80))) if (SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond - tracking.LastMovementAt >= 0.15) { tracking.IsReaderScrolling = false; if (ownerRuntime is not null) tracking.ReportActivity(ownerRuntime.ThreadID); return; } });
    }
    // :490-593. Every correction from a geometry callback is deferred to prevent display-pass recursion.
    private void NoteMetrics(ScrollMetrics old, ScrollMetrics next, bool liveResizeActive)
    {
        tracking.NoteGeometry(next.Offset, next.DistanceFromBottom, next.Travel);
        if (next.CenterY != old.CenterY && next.ContentHeight == old.ContentHeight && !tracking.IsCoasting) NoteScrollMovement();
        var anchorMove = anchorsBottomOnGrowth ? next.ContentHeight - old.ContentHeight : 0;
        // :511 `> 1`: one Mac wheel step moves the view by many points at once. The port's soft scrolling (ED-246) spreads a step over the
        // frames of its glide, whose last ones move under a point: while the reader's own wheel or touchpad glides the view (not a
        // programmatic animation), any movement is the reader's, or a pinned reader one notch up was taken for drift and snapped back
        // to the end (ED-333).
        var readerGlide = currentScroll is { } gliding && SoftScroll.IsScrolling(gliding) && SoftScroll.For(gliding).Phase != ScrollPhase.Animating;
        var readerMoved = Math.Abs(next.Offset - old.Offset - anchorMove) > (readerGlide ? 0.001 : 1);
        if (readerMoved) tracking.NoteReaderMovement(); var scrolled = tracking.IsUserScrolling || (!tracking.IsArriving && readerMoved && next.ContainerHeight == old.ContainerHeight && !tracking.IsCoasting); var atRest = !tracking.IsUserScrolling && !tracking.IsCoasting;
        if (atRest && !scrolled) { var shrink = Math.Max(0, old.ContentHeight - next.ContentHeight); var drift = Math.Abs(next.Offset - old.Offset - anchorMove); if (tracking.NoteDrift(shrink + drift, JumpTolerance(viewportHeight))) tracking.ArmViewportRefresh(); if (shrink > 1) tracking.ArmBlankWatch(); } else if (scrolled) tracking.ResetDrift();
        if (next.DistanceFromBottom < -1 && atRest && (next.ContentHeight != old.ContentHeight || next.ContainerHeight != old.ContainerHeight)) MainActor.Post(SnapEnd);
        else if (historyLoadPending && next.ContentHeight > old.ContentHeight) { historyLoadPending = false; anchorsBottomOnGrowth = tracking.IsPinnedToBottom || next.DistanceFromBottom < 48; if (tracking.IsPinnedToBottom && next.DistanceFromBottom > 1) MainActor.Post(SnapEnd); }
        else if (scrolled) tracking.IsPinnedToBottom = anchorsBottomOnGrowth = next.DistanceFromBottom < 48;
        else if ((tracking.IsPinnedToBottom || old.DistanceFromBottom < 48) && atRest && Math.Abs(next.DistanceFromBottom) > 1 && !liveResizeActive) { tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; MainActor.Post(SnapEnd); }
    }
    // :26-35. ChatView's external mounting boundary must use these eight inputs; this view reads no live title/effort/mode.
    internal void UpdateInputs(string? projectName, string? workingDirectory, bool supportsRewind, double columnHeight, bool showsMinimap) =>
        updateInputs?.Invoke(projectName, workingDirectory, supportsRewind, columnHeight, showsMinimap);

    private bool SameInputs(ThreadTimeline other) => inputs == other.inputs;
    // :289-297
    private static string[] FinishedMarkdownTexts(IReadOnlyList<TimelineEntry> entries) => entries.Where(entry => entry.Item.TimelineItemContent is TimelineItem.Content.Assistant { Value.IsStreaming: false }).Select(entry => ((TimelineItem.Content.Assistant)entry.Item.TimelineItemContent).Value.Text).ToArray();
    private async Task WarmMarkdown()
    {
        if (ownerRuntime is null) return; var texts = FinishedMarkdownTexts(ownerRuntime.Entries); if (texts.Length == 0) return;
        try { await MarkdownView.Warm(texts); } catch (Exception) { } // :296, try?; warming errors, including cancellation, are deliberately ignored.
    }
    // :624-661
    private void NoteTurnChange(bool running, long autoStartedToken)
    {
        if (!running) { if (tracking.IsPinnedToBottom) MainActor.Post(SnapEnd); tracking.ArmViewportRefresh(); tracking.ArmBlankWatch(); MainActor.Spawn(WarmMarkdown); return; }
        tracking.NotePhase("idle"); var wasAtEnd = tracking.IsPinnedToBottom; var unattended = seenAutoStartedTurnToken is { } seen && seen != autoStartedToken; seenAutoStartedTurnToken = autoStartedToken;
        if (wasAtEnd || !unattended) { tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; EndWithAnimation(wasAtEnd ? null : Animation.EaseOut(0.25)); } tracking.ArmViewportRefresh();
    }
    // :689-712
    private void NoteArrival(string? id, IReadOnlyList<DisplayBlock> blocks)
    {
        if (historyLoadPending || tracking.IsUserScrolling || !(tracking.IsPinnedToBottom || tracking.DistanceFromBottom < 48 || id?.StartsWith("question-", StringComparison.Ordinal) == true)) return;
        if (blocks.Count - WindowStartIndex(blocks) > TimelineWindow.EagerLimit) windowStartID = blocks[blocks.Count - TimelineWindow.Initial].Id;
        tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; if (state is not null) state.ShowsJumpButton = false; MainActor.Post(SnapEnd);
    }
    // :678-688
    private void ComposerGrew(bool liveResizeActive) { if (liveResizeActive || !(tracking.IsPinnedToBottom || tracking.DistanceFromBottom < 48)) return; tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; SnapEnd(); }
    // :713-720. Runtime is immutable for this port's mounted view; a different runtime receives a fresh view without a transition.
    private void ResetThread() { windowStartID = null; anchorsBottomOnGrowth = true; historyLoadPending = false; tracking.ClearVisible(); reported = null; }
    // :770-803. The check of a layout that may have changed shape unseen. The reader's place is ported whole: a scroll in progress puts it off, a viewport past the end of the
    // conversation, or a reader pinned to the end and away from it, is sent to the end (unanimated), and a viewport on the conversation that shows no row of it is repaired in
    // up to three steps a beat apart. The lazy stack's own repairs (:785 telling it where the viewport is, :787-788 remaking it, :881-887) have nothing to repair in the eager stack
    // (PR-CHAT-056/057/167/168 windows_path), so those two steps only count; the third, pinning the reader to the end, is what a blank pane ends in. ED-255.
    private void RepairLayout()
    {
        if (viewportHeight <= 0) return;
        if (tracking.IsUserScrolling || tracking.IsCoasting || tracking.IsReaderScrolling) { tracking.RepairSkippedWhileScrolling = true; return; }
        var distance = tracking.DistanceFromBottom;
        if (distance < -1 || (tracking.IsPinnedToBottom && Math.Abs(distance) > 1))
        {
            SnapEnd();
            // Rows on screen: the snap was all there was to do.
            if (!tracking.ShowsNothing || tracking.BlankRepairs >= 3) return;
            tracking.ArmViewportRefresh();
        }
        else if (tracking.ShowsNothing)
        {
            switch (tracking.BlankRepairs)
            {
                case 0: case 1: tracking.ArmViewportRefresh(); break;
                case 2: tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; SnapEnd(); tracking.ArmViewportRefresh(); break;
                default: return;
            }
        }
        else return;
        // Blank, and something was just done about it: checked again a beat from now.
        tracking.BlankRepairs++; tracking.ArmBlankWatch();
    }
    // :840-851
    private void SettleAfterResize() { if (tracking.IsUserScrolling || tracking.IsCoasting || tracking.IsReaderScrolling) { tracking.ArmViewportRefresh(); return; } if (tracking.IsPinnedToBottom) SnapEnd(); else tracking.ArmViewportRefresh(); tracking.ArmBlankWatch(); }

    // :2250-2256 extension EnvironmentValues { var revealTimelineEnd: TimelineRevealBox? }, an inherited environment
    // value (R-VIEW-6, ED-095).
    public static readonly AttachedProperty<TimelineRevealBox?> RevealTimelineEndProperty =
        AvaloniaProperty.RegisterAttached<Control, TimelineRevealBox?>(
            "RevealTimelineEnd", typeof(ThreadTimeline), RevealTimelineEndKey.DefaultValue, inherits: true);

    // :2253 get
    public static TimelineRevealBox? GetRevealTimelineEnd(Control control) => control.GetValue(RevealTimelineEndProperty);

    // :2254 set
    public static void SetRevealTimelineEnd(Control control, TimelineRevealBox? value) =>
        control.SetValue(RevealTimelineEndProperty, value);
}

/// <summary>:903-1245 <c>@MainActor @Observable final class TimelineScrollTracking: ScrollActivityReporter</c>.</summary>
public sealed class TimelineScrollTracking : MainActorModel, ScrollActivityReporter
{
    private string[] outlineIDs = []; // :923
    private HashSet<string> outlineSet = []; // :924
    private HashSet<string> userBlockIDs = []; // :925
    private HashSet<string> visibleIDs = []; // :927
    private double arrivingUntil, lastReaderMovementAt, phaseStartedAt, drift; // :935-936,1028,969
    private Guid? reportedTimeline; // :1057
    private TaskHandle? phaseWatch, blankWatch; // :937,942
    private static double Now => SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond; // CACurrentMediaTime
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
        false; // :976. Avalonia's bounded eager stack has no remembered virtual viewport to nudge (PR-CHAT-056/057 windows_path).

    // :970-972 var showsNothing: Bool
    public bool ShowsNothing =>
        outlineIDs.Length != 0 && !visibleIDs.Overlaps(outlineSet); // :979-981

    // :976-979 var endUnseen: Bool
    public bool EndUnseen =>
        outlineIDs.Length != 0 && !visibleIDs.Contains(outlineIDs[^1]); // :987-990

    // :982-991 @discardableResult func setOutline(_:) -> Bool
    public bool SetOutline(IReadOnlyList<(string Id, bool HasUserMessage)> blocks)
    {
        var ids = blocks.Select(block => block.Id).ToArray(); if (ids.SequenceEqual(outlineIDs)) return false;
        outlineIDs = ids; outlineSet = ids.ToHashSet(StringComparer.Ordinal); userBlockIDs = blocks.Where(block => block.HasUserMessage).Select(block => block.Id).ToHashSet(StringComparer.Ordinal); ResolveActive(); return true; // :995-1001
    }

    // :993-1007 func setVisible(_:_:)
    public void SetVisible(string id, bool isVisible)
    {
        if (isVisible) { if (!visibleIDs.Add(id)) return; blankWatch?.Cancel(); blankWatch = null; BlankRepairs = 0; }
        else { if (!visibleIDs.Remove(id)) return; if (ShowsNothing) ArmBlankWatch(); }
        ResolveActive(); // :1004-1019
    }

    // :1009-1012 func clearVisible()
    public void ClearVisible() { visibleIDs.Clear(); if (ActiveBlockID is not null) ActiveBlockID = null; } // :1021-1024

    // :1021 var isArriving: Bool
    public bool IsArriving =>
        Now < arrivingUntil; // :1034

    // :1023-1025 func holdEnd(for:)
    public void HoldEnd(double @for) =>
        arrivingUntil = Math.Max(arrivingUntil, Now + @for); // :1036-1038

    // :1028-1030 func noteReaderMovement()
    public void NoteReaderMovement() =>
        lastReaderMovementAt = Now; // :1041-1043

    // :1032-1039 func noteGeometry(offset:distanceFromBottom:travel:)
    public void NoteGeometry(double offset, double distanceFromBottom, double travel)
    {
        Offset = offset; DistanceFromBottom = distanceFromBottom; Travel = travel; // :1045-1052
    }

    // :1049-1055 func reportActivity(timeline:)
    public void ReportActivity(Guid timeline)
    {
        if (reportedTimeline is { } old && old != timeline) ScrollActivity.Shared.Current.SetScrolling(false, old);
        reportedTimeline = timeline; ScrollActivity.Shared.Current.SetScrolling(IsScrollingNow, timeline, this); // :1062-1068
    }

    // :1058 var isScrollingNow: Bool (ScrollActivityReporter, RenderBudget.swift:74)
    public bool IsScrollingNow =>
        IsReaderScrolling || IsCoasting; // :1071

    // :1062-1073 func endActivity()
    public void EndActivity()
    {
        SettleWatch?.Cancel(); SettleWatch = null; phaseWatch?.Cancel(); phaseWatch = null; blankWatch?.Cancel(); blankWatch = null;
        IsReaderScrolling = false; IsCoasting = false; IsUserScrolling = false;
        if (reportedTimeline is { } id) ScrollActivity.Shared.Current.SetScrolling(false, id); // :1075-1086
    }

    // :1093-1120. Avalonia wheel events have no phases; pointer gestures map to these Swift phase names.
    public void NotePhase(string phase)
    {
        IsUserScrolling = phase is "interacting" or "decelerating"; IsCoasting = phase is "decelerating" or "animating";
        if (IsUserScrolling) drift = 0; phaseWatch?.Cancel(); phaseWatch = null; if (!IsUserScrolling && !IsCoasting) return;
        lastReaderMovementAt = phaseStartedAt = Now; var still = phase == "interacting" ? 2.0 : 0.4;
        phaseWatch = MainActor.Spawn(async () =>
        {
            while (await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(200)))
            {
                if (Now - lastReaderMovementAt < still && Now - phaseStartedAt < 3.0) continue;
                IsUserScrolling = false; IsCoasting = false; if (reportedTimeline is { } id && !IsReaderScrolling) ScrollActivity.Shared.Current.SetScrolling(false, id); CheckRequest++; return;
            }
        });
    }

    // :1116-1123 func armBlankWatch()
    public void ArmBlankWatch()
    {
        blankWatch?.Cancel(); blankWatch = MainActor.Spawn(async () => { if (await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(300)) && ShowsNothing) CheckRequest++; }); // :1125-1132
    }

    // :1128-1133 func noteReturnToFront()
    public void NoteReturnToFront() { BlankRepairs = 0; CheckRequest++; ArmBlankWatch(); ArmViewportRefresh(); } // :1135-1140

    // :1139-1144 func noteDrift(_:tolerance:) -> Bool
    public bool NoteDrift(double amount, double tolerance) { drift += amount; if (drift <= tolerance) return false; drift = 0; return true; } // :1146-1151

    // :1146-1148 func resetDrift()
    public void ResetDrift() =>
        drift = 0; // :1154-1156

    // :1153-1174 func armViewportRefresh()
    public void ArmViewportRefresh() { drift = 0; } // :1161-1181. Eager rows measure against the live viewport; no virtualization refresh is needed.

    // :1184-1199 func beginNudge() -> Bool
    public bool BeginNudge() =>
        false; // :1193-1208. No virtualized range to rebuild in the eager Avalonia stack.

    // :1202-1204 func noteNudgeSeen()
    public void NoteNudgeSeen() { } // :1211-1213; no nudge in the eager stack.

    // :1208-1226 func restoreNudge(then:)
    public void RestoreNudge(Action then) { } // :1217-1235; no nudge can be started in the eager stack.

    // :1230-1236 private func resolveActive()
    private void ResolveActive()
    {
        var onScreen = outlineIDs.Select((id, index) => (id, index)).Where(pair => visibleIDs.Contains(pair.id)).ToArray(); if (onScreen.Length == 0) return;
        var middle = onScreen[onScreen.Length / 2].index; var resolved = outlineIDs.Take(middle + 1).LastOrDefault(userBlockIDs.Contains); if (resolved != ActiveBlockID) ActiveBlockID = resolved; // :1239-1245
    }
}

/// <summary>:1250-1287 <c>private struct ScrollViewProbe: NSViewRepresentable</c>.</summary>
public sealed class ScrollViewProbe : Control
{
    /// <param name="onResolve">:1242 <c>let onResolve: (NSView) -> Void</c>.</param>
    // :1244-1250 makeNSView / updateNSView
    public ScrollViewProbe(Action<Control> onResolve) { IsHitTestVisible = false; Width = Height = 0; AttachedToVisualTree += (_, _) => onResolve(this); } // :1249-1283

    /// <summary>:1261-1286 <c>private final class ProbeView: NSView</c>.</summary>
    private sealed class ProbeView : Control
    {
        // :1253 var onResolve: (NSView) -> Void
        public Action<Control> OnResolve { get; set; }

        // :1255-1258 init(onResolve:)
        public ProbeView(Action<Control> onResolve) { OnResolve = onResolve; IsHitTestVisible = false; AttachedToVisualTree += (_, _) => OnResolve(this); } // :1263-1283
    }
}

/// <summary>:1292-1353 <c>@MainActor @Observable final class FrontMonitor</c>.</summary>
public sealed class FrontMonitor : MainActorModel
{
    private WeakReference<Avalonia.Controls.Window>? uncovered; // :1303
    private bool isAppWide; // :1304
    private readonly List<WeakReference<Avalonia.Controls.Window>> windows = [];
    private int revision;          // :1285 = 0
    private bool isVisible = true; // :1289

    // :1284 static let shared = FrontMonitor()
    public static Ambient<FrontMonitor> Shared { get; } = new(() => new FrontMonitor());

    // :1295-1321 private init()
    private FrontMonitor() { } // :1307-1334. Window observation is installed when this timeline attaches to its host.

    // :1310-1331; Windows activation/visibility/minimization are identity scoped; native full-occlusion is STOP #3.
    internal void Observe(Avalonia.Controls.Window window)
    {
        if (windows.Any(reference => reference.TryGetTarget(out var known) && ReferenceEquals(known, window))) return; windows.Add(new(window));
        window.Activated += (_, _) => { var wasVisible = IsVisible; RefreshVisibility(); Bump(wasVisible ? window : null); };
        window.Deactivated += (_, _) => RefreshVisibility(); window.Closed += (_, _) => RefreshVisibility();
        window.PropertyChanged += (_, change) => { if (change.Property == Visual.IsVisibleProperty || change.Property == Avalonia.Controls.Window.WindowStateProperty) { RefreshVisibility(); if (window.IsVisible && window.WindowState != WindowState.Minimized) Bump(window); } };
        // Avalonia 12.1.2 raises Activated before IsActive becomes true, so the Activated handler above can read the old value:
        // visibility is refreshed again when IsActive itself changes (found by the native frame run, 2026-10-02).
        window.PropertyChanged += (_, change) => { if (change.Property == WindowBase.IsActiveProperty) RefreshVisibility(); };
        RefreshVisibility();
    }

    // :1285 private(set) var revision = 0
    public int Revision { get => Read(revision); private set => Set(ref revision, value); }

    // :1289 private(set) var isVisible = true
    public bool IsVisible { get => Read(isVisible); private set => Set(ref isVisible, value); }

    // :1325-1329 func concerns(_:) -> Bool
    public bool Concerns(Avalonia.Controls.Window? window) =>
        isAppWide || (window is not null && uncovered is not null && uncovered.TryGetTarget(out var shown) && ReferenceEquals(window, shown)); // :1338-1342

    // :1331-1335 private func bump(for:)
    private void Bump(Avalonia.Controls.Window? @for) { uncovered = @for is null ? null : new(@for); isAppWide = @for is null; Revision++; } // :1344-1348

    // :1337-1340 private func refreshVisibility()
    private void RefreshVisibility() { IsVisible = windows.Any(reference => reference.TryGetTarget(out var window) && window.IsActive && window.IsVisible && window.WindowState != WindowState.Minimized); } // :1350-1353 (native occlusion check remains STOP #3).
}

/// <summary>:1355-1384 <c>private struct ScrollMetrics: Equatable</c>. From adapts the native scroll geometry.</summary>
public sealed record ScrollMetrics
{
    public double ContentHeight { get; init; }      // :1347
    public double ContainerHeight { get; init; }    // :1348
    public double DistanceFromBottom { get; init; } // :1349
    public double Travel { get; init; }             // :1350
    public double CenterY { get; init; }            // :1352
    public double Offset { get; init; }             // :1354
    // :1365-1384. Avalonia has no separate content-safe-area insets here; caller supplies zero for those two values.
    internal static ScrollMetrics From(double contentHeight, double containerHeight, double offset, double top, double bottom, double centerY) => new()
    {
        ContentHeight = contentHeight, ContainerHeight = containerHeight, Offset = offset, CenterY = centerY,
        Travel = Math.Min(Math.Max(0, offset + top), Math.Max(0, contentHeight + top + bottom - containerHeight)),
        DistanceFromBottom = contentHeight + top + bottom <= containerHeight + 1 ? 0 : contentHeight + bottom - offset - containerHeight,
    };
}

/// <summary>:1386-1459 <c>enum TimelineGroup: Identifiable, Equatable</c> (R-ENUM-2).</summary>
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
        this switch { Single single => single.Value.Id, Work work => work.Id, Heads heads => heads.Id, _ => throw new InvalidOperationException() }; // :1395-1401

    // :1396-1400 @MainActor static func isHead(_:) -> Bool
    public static bool IsHead(TimelineEntry entry) =>
        entry.Item.TimelineItemContent is TimelineItem.Content.Tool { Value.ToolCallKind: ToolCall.Kind.Agent }; // :1405-1409

    // :1402-1449 @MainActor static func build(_:showReasoning:) -> [TimelineGroup]
    public static ValueList<TimelineGroup> Build(IReadOnlyList<TimelineEntry> entries, bool showReasoning)
    {
        var replies = entries.Select((entry, index) => (entry, index)).Where(pair => pair.entry.TimelineEntryKind == TimelineEntry.Kind.Assistant).Select(pair => pair.index).ToArray(); // :1415-1420. Kind only, never streaming text.
        var groups = new List<TimelineGroup>(); var work = new List<TimelineEntry>(); var heads = new List<TimelineEntry>(); var start = 0;
        void Flush()
        {
            if (work.Count == 0) return;
            var end = start + work.Count - 1; groups.Add(new Work("work-" + work[0].Id, new(work), replies.Any(index => index > end))); work.Clear(); // :1425-1431
        }
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.TimelineEntryKind == TimelineEntry.Kind.Tool)
            {
                if (IsHead(entry)) { heads.Add(entry); continue; }
                if (work.Count > 0 && work[^1].TurnID != entry.TurnID) Flush();
                if (work.Count == 0) start = index; work.Add(entry);
            }
            else if (entry.TimelineEntryKind != TimelineEntry.Kind.Reasoning) { Flush(); groups.Add(new Single(entry)); }
        }
        Flush(); if (heads.Count > 0) groups.Add(new Heads("heads-" + heads[0].Id, new(heads))); return new(groups); // :1433-1456
    }
}

/// <summary>:1463-1468 <c>struct HeadPills: Equatable</c>.</summary>
public sealed record HeadPills
{
    public ValueList<int> Earlier { get; init; } = ValueList<int>.Empty; // :1454 var earlier: [Int] = []
    public ValueList<int> Later { get; init; } = ValueList<int>.Empty;   // :1454 var later: [Int] = []
}

/// <summary>:1476-1653 <c>enum DisplayBlock: Identifiable, Equatable</c> (R-ENUM-2).</summary>
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

    /// <summary>:1546-1553 <c>struct Built</c>.</summary>
    public sealed record Built(ValueList<DisplayBlock> Blocks, int StableCount, int LastRunStart); // :1538-1543

    // :1490-1498 var id: String
    public string Id =>
        this switch { Turn turn => turn.Id, Group group => group.Value.Id, Working working => "working-" + (working.TurnID is { } id ? SwiftUUID.UuidString(id) : ""), Merging merging => merging.Id, HeadsWorking heads => heads.Trailing ? "hydra-heads-working-tail" : "hydra-heads-working", _ => throw new InvalidOperationException() }; // :1499-1507

    // :1501-1507 var turnID: UUID?
    public Guid? TurnID =>
        this switch { Turn turn => turn.TurnID, Group { Value: TimelineGroup.Single single } => single.Value.TurnID, _ => null }; // :1510-1517

    // :1511-1517 var hasUserMessage: Bool
    public bool HasUserMessage =>
        this switch { Turn turn => turn.Entries.Any(entry => entry.TimelineEntryKind == TimelineEntry.Kind.User), Group { Value: TimelineGroup.Single single } => single.Value.TimelineEntryKind == TimelineEntry.Kind.User, _ => false }; // :1520-1526

    // :1523-1532 @MainActor var hydraMergePhase: HydraMergePhase?
    public HydraMergePhase? HydraMergePhase =>
        this switch { Merging => new HydraMergePhase.Merging(), Group { Value: TimelineGroup.Single { Value.Item.TimelineItemContent: TimelineItem.Content.User user } } when IsMergeOutcome(user.Value) => new HydraMergePhase.Outcome(user.Value), _ => null }; // :1533-1541

    // :1546-1549 @MainActor static func build(_:meta:showReasoning:isRunning:hydraMergeID:pills:) -> [DisplayBlock];
    // a null pills is the Swift default HeadPills().
    public static ValueList<DisplayBlock> Build(IReadOnlyList<TimelineEntry> entries, TimelineMeta meta, bool showReasoning, bool isRunning, string? hydraMergeID, HeadPills? pills = null) =>
        Build(entries, 0, [], meta, showReasoning, isRunning, hydraMergeID, pills ?? new()).Blocks; // :1555-1558

    // :1553-1619 @MainActor static func build(_:from:keeping:meta:showReasoning:isRunning:hydraMergeID:pills:) -> Built
    public static Built Build(IReadOnlyList<TimelineEntry> entries, int from, IReadOnlyList<DisplayBlock> keeping, TimelineMeta meta, bool showReasoning, bool isRunning, string? hydraMergeID, HeadPills pills)
    {
        var runs = new List<(Guid? Turn, int Start, int End)>(); // :1566-1574
        for (var index = from; index < entries.Count; index++)
        {
            var turn = entries[index].TurnID;
            if (runs.Count > 0 && runs[^1].Turn == turn) runs[^1] = (turn, runs[^1].Start, index + 1); else runs.Add((turn, index, index + 1));
        }
        var blocks = keeping.ToList(); var stable = blocks.Count;
        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index]; var last = index == runs.Count - 1; if (last) stable = blocks.Count;
            var rows = entries.Skip(run.Start).Take(run.End - run.Start).ToArray();
            if (run.Turn is { } turn)
            {
                meta.SummaryByTurn.TryGetValue(turn, out var summary);
                blocks.Add(new Turn("turn-" + SwiftUUID.UuidString(turn) + "-" + entries[run.Start].Id, turn, new(rows.Where(entry => entry.TimelineEntryKind is not (TimelineEntry.Kind.TurnEnd or TimelineEntry.Kind.Reasoning))), summary, last && summary is null && isRunning)); // :1584-1593
            }
            else foreach (var group in TimelineGroup.Build(rows, showReasoning)) blocks.Add(new Group(group, meta.Summary(group), meta.HasReply(group))); // :1594-1598
        }
        if (isRunning && !(blocks.LastOrDefault()?.CarriesWorkingLine ?? false)) blocks.Add(new Working(entries.LastOrDefault()?.TurnID, new([]))); // :1600-1602
        var running = blocks.FindLastIndex(block => block is Turn { Summary: null });
        if (isRunning && pills.Earlier.Count > 0 && running >= 0 && blocks[running] is Turn live && !live.Entries.Any(TimelineGroup.IsHead)) blocks.Insert(running, new HeadsWorking(pills.Earlier, false)); // :1608-1614
        if (pills.Later.Count > 0) blocks.Add(new HeadsWorking(pills.Later, isRunning)); // :1615-1617
        if (hydraMergeID is not null && !EndsWithMergeOutcome(entries)) blocks.Add(new Merging(hydraMergeID)); // :1624-1626
        return new(new(blocks), stable, runs.Count > 0 ? runs[^1].Start : from);
    }

    // :1621-1624 private var carriesWorkingLine: Bool
    private bool CarriesWorkingLine =>
        this is Turn { ShowsWorking: true }; // :1630-1633

    // :1630-1634 @MainActor private static func endsWithMergeOutcome(_:) -> Bool
    private static bool EndsWithMergeOutcome(IReadOnlyList<TimelineEntry> entries) =>
        entries.LastOrDefault()?.Item.TimelineItemContent is TimelineItem.Content.User user && IsMergeOutcome(user.Value); // :1639-1643

    // :1639-1643 static func isMergeOutcome(_:) -> Bool
    public static bool IsMergeOutcome(UserMessage message)
    {
        if (!message.IsFromHydra || (message.HydraHeads?.Count ?? 0) != 0) return false;
        var title = message.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return title.StartsWith("Hydra ", StringComparison.Ordinal); // :1648-1652
    }
}

/// <summary>:1662-1796 <c>@MainActor final class TimelineBlockCache</c>.</summary>
public sealed class TimelineBlockCache
{
    private static readonly Dictionary<Guid, TimelineBlockCache> shared = new(); // :1666
    private static readonly List<Guid> order = new(); // :1667
    private Key? key;
    private DisplayBlock.Built built = new(new([]), 0, 0);
    private TimelineMeta meta = new(); // :1709-1711
    // :1659-1673 @MainActor static func cache(for:) -> TimelineBlockCache
    public static TimelineBlockCache Cache(Guid @for)
    {
        if (shared.TryGetValue(@for, out var existing)) { order.RemoveAll(id => id == @for); order.Add(@for); return existing; }
        var created = new TimelineBlockCache(); shared[@for] = created; order.Add(@for);
        while (order.Count > 24) { shared.Remove(order[0]); order.RemoveAt(0); } return created; // :1668-1682
    }

    // :1711-1737 func blocks(for:showReasoning:isRunning:hydraMergeID:pills:) -> [DisplayBlock]; a null pills is the
    // Swift default HeadPills().
    public ValueList<DisplayBlock> Blocks(IReadOnlyList<TimelineEntry> @for, bool showReasoning, bool isRunning, string? hydraMergeID, HeadPills? pills = null)
    {
        // ED-NEW-lean-14: no pass over the whole timeline for a pass that finds it unchanged or grown at its end. A ValueList cannot change, so it is the key's snapshot
        // as it is (any other list is copied, as before); the turn-end count is carried from the old key plus the appended tail (an entry's kind never changes, so
        // equal references count the same); the prefix check compares references in a loop and is skipped for the very list the key holds.
        pills ??= new(); var entries = @for as ValueList<TimelineEntry> ?? (IReadOnlyList<TimelineEntry>)@for.ToArray();
        var wanted = new Key { Entries = entries, TurnEnds = 0, IsRunning = isRunning, HydraMergeID = hydraMergeID, Pills = pills, ShowReasoning = showReasoning }; // :1720-1737
        if (key is not null && wanted.Entries.Count == key.Entries.Count && wanted.SameFlags(key) && wanted.SamePrefix(key)) return built.Blocks; // :1738 (equal references: the same turn ends)
        if (key is not null && wanted.Extends(key) && BuildTail(@for, key.Entries.Count, showReasoning, isRunning, hydraMergeID, pills) is { } tail) { built = tail; wanted = wanted with { TurnEnds = key.TurnEnds + TurnEndsIn(entries, key.Entries.Count) }; }
        else { meta = TimelineMeta.Build(@for); built = DisplayBlock.Build(@for, 0, [], meta, showReasoning, isRunning, hydraMergeID, pills); wanted = wanted with { TurnEnds = TurnEndsIn(entries, 0) }; }
        key = wanted; return built.Blocks; // :1739-1746
    }

    private static int TurnEndsIn(IReadOnlyList<TimelineEntry> entries, int from)
    {
        var count = 0; for (var index = from; index < entries.Count; index++) if (entries[index].TimelineEntryKind == TimelineEntry.Kind.TurnEnd) count++; return count;
    }

    // :1745-1770 private func buildTail(_:appendedFrom:showReasoning:isRunning:hydraMergeID:pills:) -> DisplayBlock.Built?
    private DisplayBlock.Built? BuildTail(IReadOnlyList<TimelineEntry> entries, int appendedFrom, bool showReasoning, bool isRunning, string? hydraMergeID, HeadPills pills)
    {
        var grown = meta; var kept = built.Blocks.Take(built.StableCount).ToArray();
        foreach (var entry in entries.Skip(appendedFrom))
        {
            grown = grown.Add(entry);
            if (entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd && entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd end && kept.Any(block => block.TurnID == end.Value.TurnID)) return null; // :1755-1762
        }
        var tail = DisplayBlock.Build(entries, built.LastRunStart, kept, grown, showReasoning, isRunning, hydraMergeID, pills); meta = grown; return tail; // :1764-1779
    }

    // :1775-1786 func mentionPersonas(for:roster:) -> [HydraPersona]
    private long?[] personaHeads = []; // :1712
    private bool personaRoster;
    private ValueList<HydraPersona> personas = new([]);
    public ValueList<HydraPersona> MentionPersonas(IReadOnlyList<ChatThread> @for, bool roster)
    {
        var indices = @for.Select(head => head.Hydra?.Index).ToArray(); if (indices.SequenceEqual(personaHeads) && roster == personaRoster) return personas;
        var seen = new HashSet<string>(StringComparer.Ordinal); var list = @for.Where(head => head.Hydra is not null).Select(head => head.Hydra!.Persona).Where(persona => seen.Add(persona.Name)).ToList();
        if (roster) list.AddRange(HydraRoster.Personas.Where(persona => seen.Add(persona.Name))); personaHeads = indices; personaRoster = roster; personas = new(list); return personas; // :1784-1795
    }

    /// <summary>:1684-1706 <c>private struct Key: Equatable</c>. Entry references preserve ObjectIdentifier equality.</summary>
    private sealed record Key
    {
        public required IReadOnlyList<TimelineEntry> Entries { get; init; } // :1676
        public required int TurnEnds { get; init; }                         // :1680
        public required bool IsRunning { get; init; }                       // :1681
        public string? HydraMergeID { get; init; }                          // :1683
        public required HeadPills Pills { get; init; }                      // :1685
        public required bool ShowReasoning { get; init; }                   // :1686

        // :1689-1696 func extends(_:) -> Bool
        public bool Extends(Key old) => Entries.Count > old.Entries.Count && SameFlags(old) && SamePrefix(old); // :1698-1705
        public bool SameFlags(Key old) => IsRunning == old.IsRunning && HydraMergeID == old.HydraMergeID && Pills == old.Pills && ShowReasoning == old.ShowReasoning;
        public bool SamePrefix(Key old)
        {
            if (ReferenceEquals(Entries, old.Entries)) return true;
            if (Entries.Count < old.Entries.Count) return false;
            for (var index = old.Entries.Count - 1; index >= 0; index--) if (!ReferenceEquals(old.Entries[index], Entries[index])) return false; // from the end: a changed list differs there first
            return true;
        }
    }
}

/// <summary>:1804-1811 <c>enum TimelineWindow</c>.</summary>
public static class TimelineWindow
{
    public const int FirstPaint = 3; // :1796
    public const int Initial = 24;   // :1797
    public const int Page = 24;      // :1798
    public const int EagerLimit = 96; // :1808-1810 B3 eager stack ceiling
}

/// <summary>:1817-1863 <c>struct TimelineMeta</c>.</summary>
public sealed record TimelineMeta
{
    public ValueMap<Guid, string> LastAssistantIDByTurn { get; init; } = ValueMap<Guid, string>.Empty; // :1806
    public ValueMap<Guid, TurnSummary> SummaryByTurn { get; init; } = ValueMap<Guid, TurnSummary>.Empty; // :1807
    public ValueSet<Guid> TurnsWithReply { get; init; } = ValueSet<Guid>.Empty;                          // :1808

    // :1810-1815 @MainActor static func build(_:) -> TimelineMeta
    public static TimelineMeta Build(IReadOnlyList<TimelineEntry> entries)
    {
        var meta = new TimelineMeta(); foreach (var entry in entries) meta = meta.Add(entry); return meta; // :1822-1827
    }

    // :1819-1834 @MainActor mutating func add(_:); a mutating method returns the new value (R-VAL-1).
    public TimelineMeta Add(TimelineEntry entry)
    {
        if (entry.TimelineEntryKind == TimelineEntry.Kind.Assistant && entry.TurnID is { } turn) return this with { LastAssistantIDByTurn = LastAssistantIDByTurn.Mutating(map => map[turn] = entry.Id), TurnsWithReply = new(TurnsWithReply.Append(turn)) };
        if (entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd && entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd end) return this with { SummaryByTurn = SummaryByTurn.Mutating(map => map[end.Value.TurnID] = end.Value) };
        return this; // :1831-1846
    }

    // :1838-1843 @MainActor func summary(for:) -> TurnSummary?
    public TurnSummary? Summary(TimelineGroup @for) =>
        @for is TimelineGroup.Single { Value.TimelineEntryKind: TimelineEntry.Kind.Assistant } single && single.Value.TurnID is { } turn && LastAssistantIDByTurn.TryGetValue(turn, out var last) && last == single.Value.Id && SummaryByTurn.TryGetValue(turn, out var summary) ? summary : null; // :1848-1854

    // :1847-1851 @MainActor func hasReply(for:) -> Bool
    public bool HasReply(TimelineGroup @for) =>
        @for is TimelineGroup.Single { Value.TimelineEntryKind: TimelineEntry.Kind.TurnEnd } single && single.Value.TurnID is { } turn && TurnsWithReply.Contains(turn); // :1858-1863
}

/// <summary>:1869-1875 <c>struct RowContext: Equatable</c>.</summary>
/// <param name="WorkingDirectory">:1858 <c>var workingDirectory: String?</c>.</param>
/// <param name="CanRewind">:1860 <c>var canRewind: Bool</c>.</param>
/// <param name="Chronological">:1862 <c>var chronological: Bool</c>.</param>
public sealed record RowContext(string? WorkingDirectory, bool CanRewind, bool Chronological);

/// <summary>:1877-1936 <c>private struct DisplayBlockView: View, Equatable</c>.</summary>
public sealed class DisplayBlockView : Decorator
{
    /// <param name="block">:1866 <c>let block: DisplayBlock</c>.</param>
    /// <param name="runtime">:1867 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="context">:1868 <c>let context: RowContext</c>.</param>
    // :1884-1924 body
    public DisplayBlockView(DisplayBlock block, ThreadRuntime runtime, RowContext context)
    {
        shown = block; shownContext = context;
        // :1902-1935.
        if (block.HydraMergePhase is { } phase) { Child = new HydraMergeRow(runtime, phase); return; }
        switch (block)
        {
            case DisplayBlock.Group group: Child = new TimelineGroupView(group.Value, runtime, group.Summary, group.HasReply, context); break;
            case DisplayBlock.Turn turn:
                if (turn.Summary is { } summary) { var split = SplitTurnEntries(turn.Entries); Child = new TurnFinishedBlock(runtime, turn.TurnID, summary, split.UserEntries, split.Content, context.WorkingDirectory, context.CanRewind, context.Chronological).Transition(SwiftTransition.Identity); }
                else Child = new TurnRunningBlock(runtime, turn.Entries, turn.ShowsWorking, context).Transition(SwiftTransition.Identity); break;
            case DisplayBlock.Working working: Child = new WorkingBlockView(runtime, working.LiveWork, context.WorkingDirectory); break;
            case DisplayBlock.HeadsWorking heads: Child = new HydraHeadsWorkingRow(heads.Heads, runtime); break;
            case DisplayBlock.Merging: break;
        }
    }

    private DisplayBlock shown;
    private RowContext shownContext;

    /// <summary>PORT-ONLY (OD-0076, PR-CHAT-104): this view replaces a running turn's at the turn's end; see <see cref="TurnFinishedBlock.BeginTurnEnd"/>.</summary>
    internal void BeginTurnEnd(double fromHeight, bool reduceMotion, (NthWorkingBeads Beads, double Inset)? beads = null)
    {
        if (Child is TurnFinishedBlock finished) finished.BeginTurnEnd(fromHeight, reduceMotion, beads);
    }

    /// <summary>:1896-1936, :332-334 the same view with a new block or a new context under the same id (`.id(block.id)` on an `.equatable()` row: the body runs again and every
    /// @State inside it survives). The view is the same while the body's branch is: the merge pill (in either of its phases), a group, a turn that is running (or finished), the
    /// working line, the heads' pills. A block whose branch changed, or whose directory a row was made with changed, is a different view (false). ED-255 (3).</summary>
    internal bool TryUpdate(DisplayBlock next, RowContext nextContext)
    {
        if (next.Id != shown.Id || nextContext.Chronological != shownContext.Chronological) return false;
        var updated = false;
        if (next.HydraMergePhase is { } phase) { if (Child is HydraMergeRow merge) { merge.Update(phase); updated = true; } } // :1902-1905 the pill morphs between its phases, it is not swapped
        else if (Child is not HydraMergeRow)
            switch (next)
            {
                case DisplayBlock.Group group when shown is DisplayBlock.Group && Child is TimelineGroupView view:
                    updated = view.TryUpdate(group.Value, group.Summary, group.HasReply, nextContext); break;
                case DisplayBlock.Turn { Summary: null } turn when shown is DisplayBlock.Turn { Summary: null } && Child is TurnRunningBlock running:
                    running.Update(turn.Entries, turn.ShowsWorking, nextContext); updated = true; break;
                case DisplayBlock.Turn { Summary: { } summary } turn when shown is DisplayBlock.Turn { Summary: not null } && Child is TurnFinishedBlock finished:
                    var split = SplitTurnEntries(turn.Entries); finished.Update(summary, split.UserEntries, split.Content, nextContext.WorkingDirectory, nextContext.CanRewind); updated = true; break;
                case DisplayBlock.Working working when shown is DisplayBlock.Working && Child is WorkingBlockView line && nextContext.WorkingDirectory == shownContext.WorkingDirectory:
                    line.Update(working.LiveWork, null); updated = true; break;
                case DisplayBlock.HeadsWorking heads when shown is DisplayBlock.HeadsWorking && Child is HydraHeadsWorkingRow pills:
                    pills.Update(heads.Heads); updated = true; break;
            }
        if (updated) { shown = next; shownContext = nextContext; }
        return updated;
    }

    // :1874-1882 private static func splitTurnEntries(_:) -> (userEntries:, content:)
    private static (ValueList<TimelineEntry> UserEntries, ValueList<TimelineEntry> Content) SplitTurnEntries(IReadOnlyList<TimelineEntry> entries) =>
        (new(entries.Where(entry => entry.TimelineEntryKind == TimelineEntry.Kind.User)), new(entries.Where(entry => entry.TimelineEntryKind != TimelineEntry.Kind.User))); // :1887-1895
}

/// <summary>:1942-2002 <c>private struct TurnRunningBlock: View</c>.</summary>
public sealed class TurnRunningBlock : Decorator
{
    private readonly ThreadRuntime runtime;
    private readonly StackPanel stack = new() { Spacing = TimelineMetrics.RowSpacing }; // :1979
    private readonly TurnGroupCache groupCache = new(); // :1949 @State private var groupCache
    private readonly Dictionary<string, (TimelineGroup Group, TurnRow Row)> rowViews = [];
    private (TimelineGroup Group, TurnRow Row)? headsView;
    private WorkingBlockView? working;
    private bool placed;
    private RowContext? shownContext;

    /// <param name="runtime">:1931 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="entries">:1932 <c>let entries: [TimelineEntry]</c>.</param>
    /// <param name="showsWorking">:1933 <c>let showsWorking: Bool</c>.</param>
    /// <param name="context">:1934 <c>let context: RowContext</c>.</param>
    // :1939-1989 body
    public TurnRunningBlock(ThreadRuntime runtime, ValueList<TimelineEntry> entries, bool showsWorking, RowContext context)
    {
        this.runtime = runtime; Child = stack;
        Update(entries, showsWorking, context);
    }

    // The body run again with new entries: a row whose group is unchanged is the same view, a new one arrives with the row transition, a gone one leaves
    // (:1980-1984, :1985-1989 the heads after the rows, :1990-1999 the working line last). The working line is one view for as long as the turn runs.
    internal void Update(ValueList<TimelineEntry> entries, bool showsWorking, RowContext context)
    {
        // The first body of a block places its views with it: the children of a view that comes in come with it and run no transition of their own (:1979-2001, :303-321 the
        // block itself arrives like a row), and a thread that opens on a running turn opens in one frame. The bodies after it bring a row, the heads or the working line in
        // with their transitions (:1983, :1988, :1998). ED-255 (3).
        var arrives = placed; placed = true;
        var contextChanged = shownContext is { } before && before != context; shownContext = context;
        var partition = Partition(groupCache.Groups(entries)); var wanted = new List<Control>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in partition.Rows)
        {
            seen.Add(group.Id);
            if (!rowViews.TryGetValue(group.Id, out var kept)) kept = default;
            // :1980-1984 ForEach(groups): a group whose id stood and whose value (or context) changed is the same row with its new value, not a row that arrives.
            if (kept.Row is not null && (kept.Group != group || contextChanged)) { kept.Row.Update(group, context); rowViews[group.Id] = kept = (group, kept.Row); }
            else if (kept.Row is null)
            {
                rowViews[group.Id] = kept = (group, new TurnRow(group, runtime, context).Transition(ThreadTimeline.RowTransition));
                if (arrives) SwiftTransition.Insert(stack, stack.Children.Count, kept.Row); else SwiftTransition.Place(stack, stack.Children.Count, kept.Row);
            }
            wanted.Add(kept.Row);
        }
        foreach (var gone in rowViews.Keys.Where(id => !seen.Contains(id)).ToArray()) { stack.Children.Remove(rowViews[gone].Row); rowViews.Remove(gone); }
        if (partition.Heads is { } heads)
        {
            // :1985-1989 `if let heads`: the same view while there are heads (the branch is the identity, not the group's id), so a head joining or leaving the list changes its
            // value and brings nothing in again.
            if (headsView is { } shown)
            {
                if (shown.Group != heads || contextChanged) { shown.Row.Update(heads, context); headsView = (heads, shown.Row); }
            }
            else
            {
                headsView = (heads, new TurnRow(heads, runtime, context).Transition(ThreadTimeline.RowTransition));
                if (arrives) SwiftTransition.Insert(stack, stack.Children.Count, headsView.Value.Row); else SwiftTransition.Place(stack, stack.Children.Count, headsView.Value.Row);
            }
            wanted.Add(headsView.Value.Row);
        }
        else if (headsView is { } old) { stack.Children.Remove(old.Row); headsView = null; }
        if (showsWorking || partition.Work.Count != 0)
        {
            if (working is null)
            {
                working = new WorkingBlockView(runtime, partition.Work, context.WorkingDirectory, context.Chronological, showsWorking ? null : entries); working.Transition(ThreadTimeline.RowTransition);
                if (arrives) SwiftTransition.Insert(stack, stack.Children.Count, working); else SwiftTransition.Place(stack, stack.Children.Count, working);
            }
            else working.Update(partition.Work, showsWorking ? null : entries);
            wanted.Add(working);
        }
        else if (working is not null) { stack.Children.Remove(working); working = null; }
        // The order of the stack is the rows, the heads, the working line.
        for (var i = 0; i < wanted.Count; i++) { var at = stack.Children.IndexOf(wanted[i]); if (at >= 0 && at != i) stack.Children.Move(at, i); }
    }
    // :1948-1976
    private static (ValueList<TimelineGroup> Rows, TimelineGroup? Heads, ValueList<TimelineEntry> Work) Partition(IReadOnlyList<TimelineGroup> groups)
    {
        var head = groups.LastOrDefault() is TimelineGroup.Heads last ? last : null; var work = new List<TimelineEntry>(); var rows = new List<TimelineGroup>();
        foreach (var group in groups.Take(groups.Count - (head is null ? 0 : 1))) switch (group) { case TimelineGroup.Work tools: work.AddRange(tools.Entries); break; case TimelineGroup.Single { Value.TimelineEntryKind: TimelineEntry.Kind.Assistant } assistant: work.Add(assistant.Value); break; default: rows.Add(group); break; }
        return (new(rows), head, new(work));
    }
}

/// <summary>:2007-2019 <c>@MainActor private final class TurnGroupCache</c>.</summary>
public sealed class TurnGroupCache
{
    private TimelineEntry[] identities = []; // :2007
    private ValueList<TimelineGroup> groups = new([]); // :2008
    // :1999-2006 func groups(for:) -> [TimelineGroup]
    public ValueList<TimelineGroup> Groups(IReadOnlyList<TimelineEntry> @for)
    {
        if (identities.Length != @for.Count || identities.Where((entry, index) => !ReferenceEquals(entry, @for[index])).Any()) { groups = TimelineGroup.Build(@for, false); identities = @for.ToArray(); } return groups; // :2010-2018
    }
}

/// <summary>:2023-2037 <c>private struct TurnRow: View, Equatable</c>.</summary>
public sealed class TurnRow : Decorator
{
    /// <param name="group">:2012 <c>let group: TimelineGroup</c>.</param>
    /// <param name="runtime">:2013 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="context">:2014 <c>let context: RowContext</c>.</param>
    // :2020-2024 body
    public TurnRow(TimelineGroup group, ThreadRuntime runtime, RowContext context)
    {
        this.runtime = runtime; Child = new TimelineGroupView(group, runtime, null, false, context); // :2033-2036
    }

    private readonly ThreadRuntime runtime;

    /// <summary>The same row with a new group (the body of :2033-2036 run again for the new value): the group view in it takes the value in place, and is made again only where
    /// the body's branch changed. ED-255 (3).</summary>
    internal void Update(TimelineGroup group, RowContext context)
    {
        if (Child is TimelineGroupView view && view.TryUpdate(group, null, false, context)) return;
        Child = new TimelineGroupView(group, runtime, null, false, context);
    }
}

/// <summary>:2039-2071 <c>struct TimelineGroupView: View</c>.</summary>
public sealed class TimelineGroupView : Decorator
{
    /// <param name="group">:2028 <c>let group: TimelineGroup</c>.</param>
    /// <param name="runtime">:2029 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="summary">:2031 <c>let summary: TurnSummary?</c>.</param>
    /// <param name="hasReply">:2033 <c>let hasReply: Bool</c>.</param>
    /// <param name="context">:2034 <c>let context: RowContext</c>.</param>
    // :2036-2057 body
    public TimelineGroupView(TimelineGroup group, ThreadRuntime runtime, TurnSummary? summary, bool hasReply, RowContext context)
    {
        shownGroup = group; shownSummary = summary; shownHasReply = hasReply; shownContext = context;
        // :2053-2070.
        Child = group switch
        {
            TimelineGroup.Work work => new WorkGroup(work.Entries, runtime, context.WorkingDirectory, work.StartsCollapsed),
            TimelineGroup.Heads heads => new HydraHeadsList(heads.Entries, runtime, context.WorkingDirectory),
            TimelineGroup.Single single => single.Value.TimelineEntryKind switch
            {
                TimelineEntry.Kind.User => new UserMessageRow(single.Value, runtime, context.CanRewind),
                TimelineEntry.Kind.Assistant => new AssistantMessageRow(single.Value, summary, runtime),
                TimelineEntry.Kind.Reasoning => null,
                TimelineEntry.Kind.Tool => new WorkGroup(new([single.Value]), runtime, context.WorkingDirectory),
                TimelineEntry.Kind.Plan => new PlanCard(single.Value, runtime), TimelineEntry.Kind.Todos => new TodoListRow(single.Value), TimelineEntry.Kind.Notice => new NoticeRow(single.Value),
                TimelineEntry.Kind.TurnEnd when single.Value.Item.TimelineItemContent is TimelineItem.Content.TurnEnd end => new TurnEndRow(end.Value, hasReply), _ => null,
            },
            _ => null,
        };
        this.runtime = runtime;
    }

    private readonly ThreadRuntime runtime;
    private TimelineGroup shownGroup;
    private TurnSummary? shownSummary;
    private bool shownHasReply;
    private RowContext shownContext;

    /// <summary>:2053-2070 the body run again with a new value for the same group (its id stands): the branch's view is the same view, handed what changed (a work group its entries,
    /// the heads' list its rows, a message row whether it can revert, a reply its summary); a group of another branch is a different view (false). ED-255 (3).</summary>
    internal bool TryUpdate(TimelineGroup group, TurnSummary? summary, bool hasReply, RowContext context)
    {
        if (context.WorkingDirectory != shownContext.WorkingDirectory) return false; // the rows below were made with the directory
        switch (group)
        {
            case TimelineGroup.Work work when shownGroup is TimelineGroup.Work && Child is WorkGroup workGroup: workGroup.Update(work.Entries, work.StartsCollapsed); break;
            case TimelineGroup.Heads heads when shownGroup is TimelineGroup.Heads && Child is HydraHeadsList list: list.Update(heads.Entries); break;
            case TimelineGroup.Single single when shownGroup is TimelineGroup.Single before && ReferenceEquals(before.Value, single.Value):
                switch (Child)
                {
                    case UserMessageRow user: user.SetCanRevert(context.CanRewind); break;
                    case AssistantMessageRow reply: reply.SetSummary(summary); break;
                    case WorkGroup tool when single.Value.TimelineEntryKind == TimelineEntry.Kind.Tool: tool.Update(new([single.Value]), false); break;
                    case TurnEndRow when single.Value.Item.TimelineItemContent is TimelineItem.Content.TurnEnd end:
                        if (!Equals(shownSummary, summary) || shownHasReply != hasReply || context != shownContext) Child = new TurnEndRow(end.Value, hasReply); break; // no state in it: made again with what it says
                    default: break; // a plan, a checklist, a notice, a reply with nothing to say: the entry is all they hold, and it stands
                }
                break;
            default: return false;
        }
        shownGroup = group; shownSummary = summary; shownHasReply = hasReply; shownContext = context;
        return true;
    }
}

/// <summary>:2075-2197 <c>private struct WorkingIndicator: View</c>.</summary>
public sealed class WorkingIndicator : Decorator
{
    private Action<ValueList<ThinkingStep>, ValueList<TimelineEntry>, ValueList<TimelineEntry>, bool, DateTimeOffset, DateTimeOffset?>? applyInputs;
    private DateTimeOffset startedAtValue;
    /// <summary>The time the line counts from (:2065): the turn's start, or an earlier segment's first entry.</summary>
    internal DateTimeOffset StartedAt => startedAtValue;
    // :2065-2092. Updating view values retains the indicator's local expansion state, whatever the line's times become: a running line and an earlier segment's are the same view with
    // other values (a live clock and a bar in motion, or a clock stopped), and a running line a Hydra note splits off goes on as it was, opened. ED-255 (3).
    internal void UpdateValues(ValueList<ThinkingStep> thinkingSteps, ValueList<TimelineEntry> liveWork, ValueList<TimelineEntry> turnEntries, bool showsCard, DateTimeOffset startedAt, DateTimeOffset? finishedAt) =>
        applyInputs?.Invoke(thinkingSteps, liveWork, turnEntries, showsCard, startedAt, finishedAt);
    internal void UpdateValues(ValueList<ThinkingStep> thinkingSteps, ValueList<TimelineEntry> liveWork, ValueList<TimelineEntry> turnEntries, bool showsCard) =>
        UpdateValues(thinkingSteps, liveWork, turnEntries, showsCard, startedAtValue, finishedAtValue);
    private DateTimeOffset? finishedAtValue;
    // :2189 `.task(id: FrontMonitor.shared.isVisible && isRunning)`: whether the line runs is part of the task's id, so it is observed (a line that stops running ends its
    // clock's task, one that runs again starts it).
    private sealed class LineState(bool running) : MainActorModel
    {
        private bool running = running;
        public bool Running { get => Read(running); set => Set(ref running, value); }
    }
    private readonly LineState lineState;
    /// <summary>How many times the line's one-second clock has woken (:2192-2195), for the tests of its task's id.</summary>
    internal int Ticks { get; private set; }
    // :2083 private static let change = Animation.smooth(duration: 0.3)
    private static Animation Change => Animation.Smooth(duration: 0.3);

    // :2085 private static let cardWidth: CGFloat = 380
    private const double CardWidth = 380;

    // :2167, :2171 .transition(.softAppear); Chrome.swift:949-955: arrives with SoftAppearModifier under Animation.softAppear,
    // leaves with .opacity.animation(.easeOut(duration: 0.12)). The insertion is decomposed into the modifier's layer
    // effects (Chrome.swift:940-944) for the reason ThreadTimeline.RowTransition gives.
    private static SwiftTransition SoftAppearTransition => ThreadTimeline.SoftAppearTransition;

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
        string? workingDirectory = null)
    {
        startedAtValue = startedAt; finishedAtValue = finishedAt;
        var isExpanded = chronological; var showsAllSteps = chronological; var now = SwiftDate.Now;
        var canExpand = thinkingSteps.Count != 0 || liveWork.Count != 0; var running = finishedAt is null; lineState = new LineState(running); // seeded, not set: the view may be made before it reaches the main actor's tree
        var shell = new Border { CornerRadius = new CornerRadius(TimelineMetrics.PillRadius), Background = SwiftColor.Quaternary.Opacity(0.32).Brush(this), HorizontalAlignment = HorizontalAlignment.Left }.RoundedRectangle(TimelineMetrics.PillRadius, RoundedCornerStyle.Continuous); Child = shell; // ThreadTimeline.swift:2179 `.continuous`: the card passes through ED-275 (2)'s band (36 to 55 pt) as it opens (OD-0102) // :2178-2181 the fill is the card's own width: the stack it backs hugs what it holds, it is not the row's width
        var column = new StackPanel(); shell.Child = column; // :2106 VStack(spacing: rowSpacing): the opened body brings that space with it, so it grows and folds with the body
        // The line's own parts live across renders, as SwiftUI keeps the views of a body that runs again: the label and its time go on ticking, the
        // bar keeps its state, and what changes (:2182-2183 the label and whether there is anything to open) changes in place under `change`.
        // PORT-ONLY (OD-0136 amendment 2, step 2 B): the clock is rolling digits (NthElapsedTime); the Hydra head panels keep HydraElapsedTime.
        var header = new BaselineHeader { FillsTitle = showsCard };
        // PORT-ONLY (OD-0136 amendment 2, step 2 B): the header's children keep the fractional places the baselines give them (as TimelineBaselineRow does), so the letters sit on the font's baseline.
        var titleHost = new Grid { UseLayoutRounding = false }; header.Children.Add(titleHost);
        var elapsedView = new NthElapsedTime(startedAt, finishedAt, running); var glide = new TranslateTransform(); elapsedView.RenderTransform = glide; elapsedView.UseLayoutRounding = false; header.Children.Add(elapsedView);
        var timeGlide = new AnimatedDouble(x => glide.X = x);
        // :2129-2135 the chevron, only once there is something to open, turning down while the line is open.
        var chevron = SwiftImage.SystemName("chevron.right").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)).ForegroundStyle(SwiftColor.Tertiary); chevron.UseLayoutRounding = false;
        var turn = new AnimatedDouble(angle => chevron.RenderTransform = new RotateTransform(angle)); turn.Set(isExpanded ? 90 : 0, null);
        var chevronFade = new AnimatedDouble(opacity => chevron.Opacity = opacity);
        var inner = new StackPanel { Spacing = 10, Children = { header } };
        var frame = new Border { CornerRadius = new CornerRadius(TimelineMetrics.PillRadius), Background = Brushes.Transparent, Child = inner, HorizontalAlignment = HorizontalAlignment.Left }.RoundedRectangle(TimelineMetrics.PillRadius, RoundedCornerStyle.Continuous); // :2148 .contentShape(RoundedRectangle(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))
        Control? title = null; NthWorkingTitle? thinking = null; NthBeadsHold? hold = null; var holdRoom = 1.0; string? titleText = null; bool? titleRunning = null; HydraProgressBar? bar = null; ValueList<TimelineEntry>? barEntries = null; bool? barCard = null; var chevronShown = false; var drawn = false; var leaving = 0;
        GrowingBody? opened = null; StackPanel? openedDetails = null; StackPanel? openedThinking = null; WorkSteps? openedSteps = null;
        // PORT-ONLY (UI-THEME-047): every draw builds the thinking markdown again, so the veils of its steps (SwiftUI's @State, kept by the ForEach's
        // offsets, :2158) live here for as long as the steps stay open on screen.
        var thinkingVeils = new MarkdownVeils();
        DetachedFromVisualTree += (_, _) => thinkingVeils.Reset();
        Button button = null!;
        button = PlainControls.Button(frame, () =>
        {
            if (!canExpand) return;
            // :2107-2119 both directions run the one animation: the card closes with the glide it opens with.
            Transaction.WithAnimation(Animation.Snappy(0.24), () => { isExpanded = !isExpanded; if (isExpanded) ThreadTimeline.GetRevealTimelineEnd(this)?.Follow(); Draw(); });
        });
        button.HorizontalAlignment = HorizontalAlignment.Left; // :2106 VStack(alignment: .leading) hugs its content; the button never stretches to the row's width.
        column.Children.Add(button);
        // :2155-2176 the opened body (opened, above) lives across renders while it stays open: its structural ifs (:2156, :2169) keep their
        // views, so their transitions play on insertion and removal only, never again on a new render.
        void Draw(bool force = true)
        {
            var elapsed = Math.Max(0, (now - startedAt).TotalSeconds); var word = WorkingWords.Word(seed, (long)elapsed);
            var label = liveWork.Any(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Tool) ? WorkGroupSummary.Text(liveWork) : running ? word + "\u2026" : "Earlier steps"; // :2100-2105
            if (!force && titleText == label && titleRunning == running) return;
            // :2182 .animation(Self.change, value: label): a new label takes the place of the old one over the one motion for what changes on the line.
            if (titleText != label || titleRunning != running)
            {
                // PORT-ONLY (OD-0136 amendment 2, step 2 B): a running line shows the thinking glyph and its word in breathing letters, one title that stays while the line
                // runs (a new word arrives letter by letter inside it); a line that is not running shows its label as plain text, as before.
                if (running && thinking is not null && ReferenceEquals(title, thinking)) thinking.Word = label;
                else
                {
                    var previous = title;
                    // Calculi WU5 (ED-NEW-calculi-wu5-2): a title that leaves with a fade gives its beads up first, into a hold at their place that does not fade
                    // (the beads never fade, never leave the screen for a frame); the label stands after the hold, where the running word stood.
                    // ED-NEW-calculi-rv-1: once the beads are done in the hold, its room closes and the label glides to the place a view made again gives it.
                    if (previous is NthWorkingTitle leaving)
                    {
                        var made = false;
                        if (leaving.Glyph is not null && hold is null) { titleHost.Children.Add(hold = new NthBeadsHold()); made = true; }
                        leaving.ReleaseBeads(hold?.Slot);
                        if (made && hold is not null)
                            NthWorkingBeads.For(runtime).CloseRoom(hold.Slot, titleHost, room => { holdRoom = room; if (title is HydraWorkingTitle shown) shown.Margin = new Thickness(NthBeadsHold.Indent * room, 0, 0, 0); });
                    }
                    thinking = running ? new NthWorkingTitle(label, NthWorkingBeads.For(runtime)) : null;
                    Control next = thinking is not null ? thinking : new HydraWorkingTitle(label, false, TextAlignment.Left); title = next; // :2104 the earlier segment's label at rest draws no breathing
                    if (hold is not null && thinking is null) next.Margin = new Thickness(NthBeadsHold.Indent * holdRoom, 0, 0, 0);
                    int Before() => hold is not null && titleHost.Children.IndexOf(hold) is var at && at >= 0 ? at : titleHost.Children.Count; // the hold stays last (the header reads the first child's baseline)
                    if (previous is null) titleHost.Children.Insert(Before(), next);
                    else Transaction.WithAnimation(Change, () => { SwiftTransition.Remove(titleHost, previous); SwiftTransition.Insert(titleHost, Before(), next.Transition(SwiftTransition.Opacity)); });
                }
                titleText = label; titleRunning = running;
            }
            // :2144-2147 the card keeps an even 14 on both sides so its bar sits centred; the badge is a pill like the others in the column.
            frame.Padding = showsCard ? new Thickness(14, 11) : new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical);
            frame.Width = showsCard ? CardWidth : double.NaN; header.FillsTitle = showsCard;
            // :2129-2135 the chevron comes with something to open and goes with it; :2128, :2183 the time glides over to make room, or back.
            var motion = drawn ? Change : null;
            if (canExpand != chevronShown)
            {
                chevronShown = canExpand; leaving++;
                var comingBack = header.Leaving.Remove(chevron); // a chevron still on its way out comes back from the opacity it has
                if (canExpand) { turn.Set(isExpanded ? 90 : 0, null); if (!header.Children.Contains(chevron)) header.Children.Add(chevron); }
                else if (motion is null) header.Children.Remove(chevron);
                else
                {
                    // :2133 `.transition(.opacity)` under :2183 `.animation(Self.change, value: canExpand)`: the chevron fades out where it stood, in nobody's layout, and the
                    // time glides over to its new place; it leaves the header when the fade is done.
                    header.Leaving.Add(chevron); chevronFade.Set(0, motion);
                    var token = leaving; var frames = FrameClock.Source.Current; var since = SwiftClock.Now; Action<TimeSpan>? tick = null;
                    tick = _ =>
                    {
                        if (token != leaving) return;
                        if ((SwiftClock.Now - since).TotalSeconds >= motion.Duration) { header.Leaving.Remove(chevron); header.Children.Remove(chevron); }
                        else frames.RequestFrame(tick!);
                    };
                    frames.RequestFrame(tick);
                }
                header.InvalidateMeasure();
                chevron.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var room = header.FillsTitle ? 8 + chevron.DesiredSize.Width : 0;
                if (canExpand) { if (!comingBack) chevronFade.Set(0, null); chevronFade.Set(1, motion); }
                if (room > 0) { timeGlide.Set(canExpand ? room : -room, null); timeGlide.Set(0, motion); }
            }
            // :2138-2140 the bar is the card's; the badge is the line above alone. It is made again only when what it draws changed.
            if (!showsCard) { if (bar is not null) { inner.Children.Remove(bar); bar = null; barEntries = null; } barCard = false; }
            else if (bar is not null && barCard == true && !Equals(barEntries, turnEntries)) { bar.UpdateEntries(turnEntries); barEntries = turnEntries; } // :2139 the bar is handed the entries again and goes on
            else if (bar is null || barCard != true)
            {
                if (bar is not null) inner.Children.Remove(bar);
                bar = new HydraProgressBar(runtime, startedAt, finishedAt, running ? HydraHeadInfo.Status.Running : HydraHeadInfo.Status.Stopped, Chrome.Accent, turnEntries); barEntries = turnEntries; barCard = true; inner.Children.Add(bar);
            }
            turn.Set(isExpanded ? 90 : 0, Transaction.Current);
            ToolTip.SetTip(button, canExpand ? isExpanded ? "Hide these steps" : "Show these steps" : ""); Avalonia.Automation.AutomationProperties.SetHelpText(button, canExpand ? "Shows what the agent is doing" : "");
            if (isExpanded && canExpand)
            {
                if (opened is null) { openedDetails = new StackPanel { Spacing = TimelineMetrics.RowSpacing }; opened = new GrowingBody { LeadingSpace = TimelineMetrics.RowSpacing, Child = new Border { Padding = new Thickness(14, 0, 14, 11), Child = openedDetails } }; column.Children.Add(opened); opened.Open(Transaction.Current); }
                var details = openedDetails!;
                if (thinkingSteps.Count != 0) // :2156-2168
                {
                    if (openedThinking is null)
                    {
                        openedThinking = new StackPanel { Spacing = TimelineMetrics.RowSpacing, Margin = new Thickness(TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, 0, 0, 0) };
                        MarkdownView.SetMarkdownPointSize(openedThinking, 12); MarkdownView.SetMarkdownDimmed(openedThinking, true);
                        SwiftTransition.Insert(details, 0, openedThinking.Transition(SoftAppearTransition));
                    }
                    // ED-NEW-lean-14: each step is the view at its place (`ForEach` by offset); a step whose text and streaming flag are the same keeps its view (MarkdownView's
                    // `==`, :21-23), and only a step that changed is made again, at its place, with the same veils (a streamed delta remakes the last step alone).
                    var steps = openedThinking.Children; var stepPlace = 0;
                    foreach (var step in thinkingSteps)
                    {
                        if (stepPlace < steps.Count && steps[stepPlace] is MarkdownView shown && shown.Text == step.Text && shown.IsStreaming == step.IsStreaming) { stepPlace++; continue; }
                        var view = new MarkdownView(step.Text, step.IsStreaming, thinkingVeils.Root.Child("s" + stepPlace));
                        if (stepPlace < steps.Count) steps.RemoveAt(stepPlace); steps.Insert(stepPlace, view);
                        stepPlace++;
                    }
                    if (steps.Count > stepPlace) steps.RemoveRange(stepPlace, steps.Count - stepPlace);
                }
                else if (openedThinking is not null) { SwiftTransition.Remove(details, openedThinking); openedThinking = null; thinkingVeils.Reset(); }
                if (liveWork.Count != 0) // :2169-2172
                {
                    if (openedSteps is null) { openedSteps = new WorkSteps(liveWork, runtime, new Binding<bool>(() => showsAllSteps, value => showsAllSteps = value), workingDirectory); SwiftTransition.Insert(details, details.Children.Count, openedSteps.Transition(SoftAppearTransition)); }
                    else openedSteps.Update(liveWork);
                }
                else if (openedSteps is not null) { SwiftTransition.Remove(details, openedSteps); openedSteps = null; }
            }
            else if (opened is not null) { var closing = opened; opened = null; openedDetails = null; openedThinking = null; openedSteps = null; thinkingVeils.Reset(); closing.Close(Transaction.Current, () => column.Children.Remove(closing)); }
            drawn = true;
        }
        applyInputs = (thinking, work, entries, card, start, finish) =>
        {
            thinkingSteps = thinking; liveWork = work; turnEntries = entries; showsCard = card; canExpand = thinking.Count != 0 || work.Count != 0;
            if (start != startedAt || finish != finishedAt)
            {
                // :2065,:2074 the line's times handed again (a running line a note splits off becomes an earlier segment's, finished): the line, its fold, its opened body,
                // its clock and its bar are the views they were (:2125 HydraElapsedTime and :2139 HydraProgressBar stand in the same place of the body, so they keep their
                // state), handed the new times and status; the clock's words swap over the line's own motion (:2182, OD-0076), and its one-second task follows `running`.
                startedAt = start; finishedAt = finish; startedAtValue = start; finishedAtValue = finish; running = finish is null; lineState.Running = running;
                elapsedView.Update(start, finish, running);
                bar?.UpdateTimes(start, finish, running ? HydraHeadInfo.Status.Running : HydraHeadInfo.Status.Stopped);
            }
            Draw();
        };
        Draw(); // :2185-2188. The source retains chronological, but ThreadTimeline always passes false.
        this.Task(() => FrontMonitor.Shared.Current.IsVisible && lineState.Running, async active =>
        {
            if (!active) return; // :2191 the elapsed-time label ticks only while someone can see it, and only while the line runs
            while (await SwiftTask.TrySleep(TimeSpan.FromSeconds(1))) await MainActor.Run(() => { Ticks++; now = SwiftDate.Now; Draw(false); });
        }); // :2189-2196
    }

    // :2122. The title fills only the card's surplus width; every child aligns on its first text baseline.
    private sealed class BaselineHeader : Panel
    {
        private bool fillsTitle;
        public bool FillsTitle { get => fillsTitle; set { if (fillsTitle == value) return; fillsTitle = value; InvalidateMeasure(); } }
        /// <summary>Children on their way out (:2133 `.transition(.opacity)`): still drawn where they stood, in nobody's layout, so what is left lays out as if they were gone.</summary>
        public HashSet<Control> Leaving { get; } = [];
        private Control[] Present => Children.Where(child => !Leaving.Contains(child)).ToArray();
        private static double Baseline(Control child) => child switch { TextBlock text => text.TextLayout.TextLines.FirstOrDefault()?.Baseline ?? child.DesiredSize.Height, IBaselineProvider provider => provider.Baseline, Decorator { Child: { } content } => Baseline(content), Panel panel when panel.Children.Count > 0 => Baseline(panel.Children[0]), _ => child.DesiredSize.Height };
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var present = Present;
            var other = present.Skip(1).Sum(child => child.DesiredSize.Width) + Math.Max(0, present.Length - 1) * 8;
            if (fillsTitle && double.IsFinite(availableSize.Width) && present.Length > 0) present[0].Measure(new Size(Math.Max(0, availableSize.Width - other), availableSize.Height));
            var above = present.Select(Baseline).DefaultIfEmpty().Max(); var below = present.Select(child => child.DesiredSize.Height - Baseline(child)).DefaultIfEmpty().Max();
            return new Size(fillsTitle && double.IsFinite(availableSize.Width) ? availableSize.Width : present.Sum(child => child.DesiredSize.Width) + Math.Max(0, present.Length - 1) * 8, above + below);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var present = Present; var baseline = present.Select(Baseline).DefaultIfEmpty().Max(); var x = 0.0; var other = present.Skip(1).Sum(child => child.DesiredSize.Width) + Math.Max(0, present.Length - 1) * 8;
            for (var i = 0; i < present.Length; i++) { var child = present[i]; var width = i == 0 && fillsTitle ? Math.Max(0, finalSize.Width - other) : child.DesiredSize.Width; child.Arrange(new Rect(x, baseline - Baseline(child), width, child.DesiredSize.Height)); x += width + 8; }
            foreach (var child in Children.Where(Leaving.Contains)) child.Arrange(new Rect(fillsTitle ? finalSize.Width - child.DesiredSize.Width : x, baseline - Baseline(child), child.DesiredSize.Width, child.DesiredSize.Height));
            return finalSize;
        }
    }
}

/// <summary>The opened body of the working line (:2155-2176), which the card's own animation grows to its height on opening and folds away on closing
/// (:2107-2119 `withAnimation(.snappy(duration: 0.24))` over `isExpanded`: the card is a stack whose height follows what it holds). It fades with its height.</summary>
internal sealed class GrowingBody : Decorator
{
    private double revealed = 1;
    private IFrameSource? source;
    private Action<TimeSpan>? pending;

    /// <summary>The space the body brings above it into the stack (the stack's spacing, :2106).</summary>
    public double LeadingSpace { get; init; }

    public GrowingBody() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null) return default;
        Child.Measure(availableSize);
        return new Size(Child.DesiredSize.Width, (LeadingSpace + Child.DesiredSize.Height) * revealed);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, LeadingSpace, finalSize.Width, Child.DesiredSize.Height));
        return finalSize;
    }

    private void Set(double value) { revealed = Math.Max(0, value); Opacity = Math.Clamp(value, 0, 1); InvalidateMeasure(); }

    public void Open(Animation? animation) { Set(animation is null ? 1 : 0); if (animation is not null) Run(1, animation, null); }

    public void Close(Animation? animation, Action done) { if (animation is null) { done(); return; } Run(0, animation, done); }

    private void Run(double to, Animation animation, Action? done)
    {
        Stop();
        var from = revealed; var started = SwiftClock.Now; var frames = FrameClock.Source.Current; source = frames;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            var elapsed = (SwiftClock.Now - started).TotalSeconds; var complete = elapsed >= animation.Duration;
            Set(complete ? to : from + (to - from) * animation.Progress(elapsed));
            if (complete) { pending = null; done?.Invoke(); } else frames.RequestFrame(frame!);
        };
        pending = frame; frames.RequestFrame(frame);
    }

    private void Stop() { if (pending is { } frame) source?.CancelFrame(frame); pending = null; }
}

/// <summary>:2201-2204 <c>struct ThinkingStep: Equatable</c>.</summary>
/// <param name="Text">:2190 <c>var text: String</c>.</param>
/// <param name="IsStreaming">:2191 <c>var isStreaming: Bool</c>.</param>
public sealed record ThinkingStep(string Text, bool IsStreaming);

/// <summary>:2209-2227 <c>@MainActor @Observable final class TimelineScrollState</c>.</summary>
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
        JumpRequest++; // :2218-2220

    // :2212-2214 func noteComposerAreaGrowth()
    public void NoteComposerAreaGrowth() =>
        AreaGrowthRequest++; // :2224-2226
}

/// <summary>:2235-2244 <c>@MainActor final class TimelineRevealBox: Equatable</c>. Swift's <c>==</c> is identity
/// (<c>===</c>, :2229-2231), which is a C# class's default equality.</summary>
public sealed class TimelineRevealBox
{
    // :2224 var action: () -> Void = {}
    public Action Action { get; set; } = () => { };

    // :2227 var follow: () -> Void = {}
    public Action Follow { get; set; } = () => { };
}

/// <summary>:2246-2248 <c>private struct RevealTimelineEndKey: EnvironmentKey</c>: the default of
/// ThreadTimeline.RevealTimelineEndProperty.</summary>
file static class RevealTimelineEndKey
{
    // :2247 static let defaultValue: TimelineRevealBox? = nil
    public static TimelineRevealBox? DefaultValue => null;
}

/// <summary>:2261-2315 <c>private struct WorkingBlockView: View</c>.</summary>
public sealed class WorkingBlockView : Decorator
{
    private readonly ThreadRuntime runtime;
    private ValueList<TimelineEntry> liveWork;
    private readonly string? workingDirectory;
    private readonly bool chronological;
    private ValueList<TimelineEntry>? earlierEntries;

    /// <param name="runtime">:2251 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="liveWork">:2253 <c>let liveWork: [TimelineEntry]</c>.</param>
    /// <param name="workingDirectory">:2254 <c>let workingDirectory: String?</c>.</param>
    /// <param name="chronological">:2255 <c>var chronological = false</c>.</param>
    /// <param name="earlierEntries">:2258 <c>var earlierEntries: [TimelineEntry]?</c>.</param>
    // :2260-2274 body
    public WorkingBlockView(ThreadRuntime runtime, ValueList<TimelineEntry> liveWork, string? workingDirectory, bool chronological = false, ValueList<TimelineEntry>? earlierEntries = null)
    {
        this.runtime = runtime; this.liveWork = liveWork; this.workingDirectory = workingDirectory; this.chronological = chronological; this.earlierEntries = earlierEntries;
        this.Track(() => (AppEnvironment.GetAppModel(this).Settings.ShowsWorkingCard, AppEnvironment.GetAppModel(this).Settings.ShowReasoning, this.earlierEntries is null && AppEnvironment.GetAppModel(this).Settings.ShowReasoning ? Thinking(runtime) : new ValueList<ThinkingStep>([])), Apply); // :2267-2285
    }

    private void Apply((bool ShowsWorkingCard, bool ShowReasoning, ValueList<ThinkingStep>) values)
    {
        var thinking = earlierEntries is null && values.ShowReasoning ? values.Item3 : new([]); var entries = earlierEntries ?? TurnEntries(runtime);
        var startedAt = earlierEntries?.FirstOrDefault()?.Item.Date ?? runtime.TurnStartedAt; var finishedAt = earlierEntries?.LastOrDefault()?.Item.Date;
        // :2261-2274 the line is one view for the life of the block: a running line and an earlier segment's are the same line with other times (a live clock and a bar in
        // motion, or a clock stopped), and its @State, the fold above all, goes through the change. (`now`, the app's clock when the turn's start is not known, is the same
        // line's own start: the source's `.now` is the moment the body ran.)
        if (Child is WorkingIndicator indicator) indicator.UpdateValues(thinking, liveWork, entries, values.ShowsWorkingCard, startedAt ?? indicator.StartedAt, finishedAt);
        else Child = new WorkingIndicator(runtime, startedAt ?? SwiftDate.Now, WorkingWords.Seed(SwiftUUID.UuidString(runtime.ThreadID)), thinking, liveWork, entries, values.ShowsWorkingCard, finishedAt, chronological, workingDirectory);
    }

    /// <summary>The view with new inputs (:1990-1999 the turn's block hands it its live work and its entries again): its state, the line's expansion above all, stays.</summary>
    internal void Update(ValueList<TimelineEntry> nextLiveWork, ValueList<TimelineEntry>? nextEarlierEntries)
    {
        liveWork = nextLiveWork; earlierEntries = nextEarlierEntries;
        var settings = AppEnvironment.GetAppModel(this).Settings;
        Apply((settings.ShowsWorkingCard, settings.ShowReasoning, earlierEntries is null && settings.ShowReasoning ? Thinking(runtime) : new ValueList<ThinkingStep>([])));
    }

    // :2278-2287 private static func turnEntries(of:) -> [TimelineEntry]
    private static ValueList<TimelineEntry> TurnEntries(ThreadRuntime of)
    {
        // ED-NEW-lean-14: walked back from the end by index, with no copy of the whole timeline (Enumerable.Reverse buffered all of it).
        var all = of.Entries; if (all.Count == 0 || all[^1].TurnID is not { } turn) return new([]); var rows = new List<TimelineEntry>();
        for (var index = all.Count - 1; index >= 0; index--) { var entry = all[index]; if (entry.TurnID is not { } entryTurn) continue; if (entryTurn != turn) break; rows.Add(entry); } rows.Reverse(); return new(rows); // :2290-2299
    }
    // :2301-2314
    private static ValueList<ThinkingStep> Thinking(ThreadRuntime runtime) => new(TurnEntries(runtime).Where(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Reasoning && entry.Item.TimelineItemContent is TimelineItem.Content.Reasoning { Value.Text.Length: > 0 }).Select(entry => { var reasoning = ((TimelineItem.Content.Reasoning)entry.Item.TimelineItemContent).Value; return new ThinkingStep(reasoning.Text, reasoning.IsStreaming); }));
}

/// <summary>:2320-2326 <c>private struct ScrollFreeze: ViewModifier</c>.</summary>
/// <param name="Tracking">:2309 <c>let tracking: TimelineScrollTracking</c>.</param>
public sealed record ScrollFreeze(TimelineScrollTracking Tracking)
{
    // :2311-2313 func body(content:)
    public Control Body(Control content) =>
        content.Track(() => Tracking.IsReaderScrolling, scrolling => content.IsHitTestVisible = !scrolling); // :2320-2325
}
