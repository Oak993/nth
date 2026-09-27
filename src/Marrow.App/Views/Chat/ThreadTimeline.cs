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
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Chat/ThreadTimeline.swift (B3 52bc792), :1-2327.
// B3 owned behavior is ported. Exact external/platform gaps are documented in ThreadTimelineTests STOP #1-5.
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
    private ScrollViewer? currentScroll;
    private readonly Dictionary<string, Control> laidOutRows = [];
    private readonly Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)> rowViews = [];
    private readonly Dictionary<string, (QuestionRequest Request, Control Row)> questionViews = []; // :393-397, stable ForEach identity
    private TimelineScrollState? state;
    private ThreadRuntime? ownerRuntime;
    private Action? redraw;
    private Action<TimeSpan>? scrollFrame;
    private IFrameSource? scrollFrames;
    private TaskHandle? initialHistoryTask; // :594-607
    private bool initialHistoryStarted;
    // :363,436-441. Own the scroll presenter, as SidebarView does, rather than depending on a themed template.
    private static readonly FuncControlTemplate<ScrollViewer> TimelineScrollTemplate = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] };
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = false });
        return presenter.RegisterInNameScope(scope);
    });
    private long? seenAutoStartedTurnToken; // :83-85
    private bool? seenRunning;
    private string? seenLastEntryID;
    private readonly (ThreadRuntime Runtime, ChromeScrollModel Chrome, TimelineScrollState State, string? Project, string? Directory, bool Rewind, double Height, bool Minimap) inputs; // :26-35
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
        var host = new Border(); Child = host;
        void Draw()
        {
            if (runtime.IsLoadingHistory)
            {
                // :134-135; OD-0016 / ED-097. LoadingMark owns the 400 ms blank period and 0.2 s fade, and cancels its frames on detach.
                if (host.Child is not LoadingMark) host.Child = new LoadingMark { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                return;
            }
            var model = AppEnvironment.GetAppModel(this);
            var team = model.Helpers(runtime.ThreadID).Where(thread => thread.IsHydraHead).ToArray();
            // STOP #1 is only the external roster extraction, not this unit's finish ordering and cut.
            var finished = team.Length == 0 ? new ValueList<HydraHeadInfo>([]) : HydraHeadsWorkingRow.FinishedHeads(team);
            var pills = SplitHeadPills(finished, runtime.IsRunning ? runtime.TurnStartedAt : null);
            var blocks = TimelineBlockCache.Cache(runtime.ThreadID).Blocks(runtime.Entries, model.Settings.ShowReasoning, runtime.IsRunning, MergeID(runtime.IsHydraMerging, runtime.HydraMergeNoteID), pills);
            var lastID = runtime.Questions.LastOrDefault() is { } question ? "question-" + question.Id : blocks.LastOrDefault()?.Id;
            if (seenLastEntryID != lastID) { seenLastEntryID = lastID; NoteArrival(lastID, blocks); }
            var heads = model.Children(runtime.ThreadID).Where(thread => thread.IsHydraHead).ToArray();
            MarkdownView.SetHydraMentionPersonas(this, TimelineBlockCache.Cache(runtime.ThreadID).MentionPersonas(heads, model.Thread(runtime.ThreadID) is { } thread && model.HydraIsOn(thread)));
            MarkdownView.SetHydraMentionTargets(this, MentionTargets(heads)); // :123-131,209-210. URL hook remains STOP #4.
            if (blocks.Count == 0 && !runtime.IsRunning)
            {
                host.Child = new NewThreadPrompt(runtime.ThreadID, projectName); scrollChrome.Update(0); scrollState.ShowsJumpButton = false; // :136-141
            }
            else
            {
                // STOP #1: external row rendering is not substituted by fabricated content.
                var rows = new StackPanel { Spacing = TimelineMetrics.RowSpacing };
                var rewindable = RewindableIDs(supportsRewind, runtime.IsRunning, runtime.Turns.Select(turn => turn.Id));
                foreach (var id in laidOutRows.Keys) tracking.SetVisible(id, false); laidOutRows.Clear();
                foreach (var block in blocks.Skip(WindowStartIndex(blocks)))
                {
                    var context = new RowContext(workingDirectory, block.TurnID is { } id && rewindable.Contains(id), false);
                    var hadIdentity = rowViews.TryGetValue(block.Id, out var existing);
                    var row = hadIdentity && existing.Block == block && existing.Context == context ? existing.Row : new DisplayBlockView(block, runtime, context).Transition(Transition(block));
                    if (row.Parent is Panel parent) parent.Children.Remove(row);
                    rowViews[block.Id] = (block, context, row);
                    laidOutRows[block.Id] = row;
                    if (hadIdentity) rows.Children.Add(row); else SwiftTransition.Insert(rows, rows.Children.Count, row);
                }
                foreach (var gone in rowViews.Keys.Except(laidOutRows.Keys).ToArray()) rowViews.Remove(gone);
                foreach (var request in runtime.Questions)
                {
                    var id = "question-" + request.Id; var hadIdentity = questionViews.TryGetValue(id, out var existing);
                    var badge = hadIdentity && existing.Request == request ? existing.Row : new QuestionBadgeRow(request, runtime) { Name = id }.Transition(RowTransition);
                    if (badge.Parent is Panel parent) parent.Children.Remove(badge);
                    questionViews[id] = (request, badge);
                    if (hadIdentity) rows.Children.Add(badge); else SwiftTransition.Insert(rows, rows.Children.Count, badge);
                } // :393-397
                foreach (var gone in questionViews.Keys.Except(runtime.Questions.Select(request => "question-" + request.Id)).ToArray()) questionViews.Remove(gone);
                new ScrollFreeze(tracking).Body(rows);
                if (tracking.SetOutline(blocks.Skip(WindowStartIndex(blocks)).Select(block => (block.Id, block.HasUserMessage)).ToArray()))
                {
                    tracking.ArmBlankWatch(); if (tracking.IsPinnedToBottom && !tracking.IsArriving) tracking.ArmViewportRefresh();
                } // :346-354
                var body = new StackPanel(); var hidden = WindowStartIndex(blocks);
                Button? earlier = hidden > 0 ? EarlierPill(hidden, () => { LoadEarlier(blocks); Draw(); }) : null;
                if (earlier is not null) body.Children.Add(earlier);
                body.Children.Add(rows);
                var sentinel = new Border { Height = 1, Background = Brushes.Transparent }; body.Children.Add(sentinel); // :402-409
                var resize = AppEnvironment.GetWindowLiveResize(this);
                var width = SteppedWidth(resize.IsReshaping, Bounds.Width, model.Sidebar.IsSliding, paneWidthAtSlideStart, model.Sidebar.SlideDelta);
                var column = CreateColumn(body, SteppedHeight(columnHeight, 0, 0), width);
                var scroll = new ScrollViewer { Content = column, Template = TimelineScrollTemplate, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; // :384-440
                if (currentScroll is null) tracking.HoldEnd(1.5); var oldOffset = currentScroll?.Offset ?? default;
                currentScroll = scroll; viewportHeight = column.MinHeight;
                scroll.SizeChanged += (_, _) => { viewportHeight = SteppedHeight(scroll.Bounds.Height, 0, 0); column.MinHeight = viewportHeight; }; // :446-449
                var oldMetrics = new ScrollMetrics();
                scroll.ScrollChanged += (_, change) =>
                {
                    var next = ScrollMetrics.From(scroll.Extent.Height, scroll.Viewport.Height, scroll.Offset.Y, 0, 0, scroll.Offset.Y + scroll.Viewport.Height / 2);
                    // :439-441. Avalonia has no content-size anchor; retain the bottom-relative position before
                    // classifying movement, and apply the offset next turn, never from the geometry callback.
                    if (oldMetrics.ContentHeight > 0 && anchorsBottomOnGrowth && (change.ExtentDelta.Y != 0 || change.ViewportDelta.Y != 0))
                    {
                        var offset = Math.Clamp(next.Offset + change.ExtentDelta.Y - change.ViewportDelta.Y, 0, Math.Max(0, next.ContentHeight - next.ContainerHeight));
                        if (Math.Abs(offset - next.Offset) > 1)
                        {
                            next = ScrollMetrics.From(next.ContentHeight, next.ContainerHeight, offset, 0, 0, offset + next.ContainerHeight / 2);
                            MainActor.Post(() => { if (ReferenceEquals(currentScroll, scroll)) scroll.Offset = new Vector(scroll.Offset.X, offset); });
                        }
                    }
                    NoteMetrics(oldMetrics, next, resize.IsActive); oldMetrics = next; scrollChrome.Update(next.Travel);
                    foreach (var pair in laidOutRows) tracking.SetVisible(pair.Key, VisibleFraction(pair.Value, scroll) >= 0.001);
                    SetEndVisible(VisibleFraction(sentinel, scroll) >= 0.01);
                    if (earlier is not null && VisibleFraction(earlier, scroll) >= 0.1 && !historyLoadPending) { LoadEarlier(blocks); MainActor.Post(Draw); }
                };
                var overlay = new Grid { Children = { scroll } };
                if (CreateRail(blocks, showsMinimap, columnHeight) is { } rail) overlay.Children.Add(rail);
                host.Child = overlay; MainActor.Post(() => { if (!ReferenceEquals(currentScroll, scroll)) return; if (tracking.IsPinnedToBottom) SnapEnd(); else scroll.Offset = oldOffset; });
                if (!initialHistoryStarted) StartInitialHistoryLoad(blocks); // :594-607; task starts only with a timeline, never with the empty prompt.
            }
        }
        this.Track(() => (runtime.IsLoadingHistory, runtime.Entries, runtime.IsRunning, runtime.IsHydraMerging, runtime.HydraMergeNoteID, AppEnvironment.GetAppModel(this).Settings.ShowReasoning), _ => Draw());
        this.Track(() => runtime.Questions, _ => Draw());
        redraw = Draw;
        DetachedFromVisualTree += (_, _) => { initialHistoryTask?.Cancel(); initialHistoryTask = null; initialHistoryStarted = false; tracking.EndActivity(); if (scrollFrame is not null) scrollFrames?.CancelFrame(scrollFrame); scrollFrame = null; };
        this.Track(() => scrollState.JumpRequest, _ => { tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; EndWithAnimation(scrollState.JumpRequest == 0 ? null : Animation.Smooth(0.35)); });
        this.Track(() => runtime.IsRunning, running => { if (seenRunning is { } previous && previous != running) NoteTurnChange(running, runtime.AutoStartedTurnToken); seenRunning = running; });
        this.Track(() => scrollState.AreaGrowthRequest, _ => { if (scrollState.AreaGrowthRequest != 0) ComposerGrew(AppEnvironment.GetWindowLiveResize(this).IsActive); });
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window window) { FrontMonitor.Shared.Current.Observe(window); tracking.Probe = this; }
        };
        this.Track(() => FrontMonitor.Shared.Current.Revision, _ => { if (FrontMonitor.Shared.Current.Concerns(TopLevel.GetTopLevel(this) as Avalonia.Controls.Window)) tracking.NoteReturnToFront(); });
        this.Track(() => AppEnvironment.GetWindowLiveResize(this).IsReshaping, active => { if (!active) SettleAfterResize(); else if (scrollFrame is not null) { scrollFrames?.CancelFrame(scrollFrame); scrollFrame = null; } });
        this.Track(() => AppEnvironment.GetAppModel(this).Sidebar.IsSliding, sliding => { if (sliding) paneWidthAtSlideStart = Bounds.Width; });
    }

    // :59-67. No lower clamp in the ordinary width branch; only the sidebar-slide target is clamped to 12.
    private static double? SteppedWidth(bool reshaping, double paneWidth, bool sliding, double slideStart, double slideDelta) => !reshaping || paneWidth <= 0 ? null : sliding && slideStart > 0 ? Math.Min(860, Math.Max(ResizeStep, slideStart + slideDelta)) : Math.Min(860, Math.Floor(paneWidth / ResizeStep) * ResizeStep);
    // :446-449,891-893
    private static double SteppedHeight(double height, double topInset, double bottomInset) => Math.Floor(Math.Max(0, height - topInset - bottomInset) / ResizeStep) * ResizeStep;
    // :416-422,325. The star row is Spacer(minLength: 0), not a fixed gap.
    private Control CreateColumn(Control rows, double viewportHeight, double? layoutWidth)
    {
        var stack = new Grid { RowDefinitions = new RowDefinitions("*,Auto") }; Grid.SetRow(rows, 1); stack.Children.Add(new Border()); stack.Children.Add(rows);
        var content = new Border { MaxWidth = 820, Margin = new Thickness(20, Chrome.ContentTopInset, 20, 18), Child = stack, HorizontalAlignment = HorizontalAlignment.Stretch };
        var column = new Grid { MinHeight = viewportHeight, Children = { content } }; if (layoutWidth is { } width) column.Width = width; return column;
    }
    // :272-278
    private int WindowStartIndex(IReadOnlyList<DisplayBlock> blocks)
    {
        if (windowStartID is not null) for (var index = 0; index < blocks.Count; index++) if (blocks[index].Id == windowStartID) return index;
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

    // :317-321 static let rowTransition: AnyTransition
    public static SwiftTransition RowTransition =>
        // :317-321; Chrome.swift:940-944. Decompose the modifier's exact layer effects so the shared transition
        // interpolates them; its Bool-only modifier path otherwise applies identity immediately.
        SwiftTransition.Asymmetric(SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y: 4)).Animation(Animation.SoftAppear), SwiftTransition.Identity);

    // :824-826 private static func jumpTolerance(_:) -> CGFloat
    private static double JumpTolerance(double viewportHeight) =>
        Math.Max(48, viewportHeight / 4); // :833-835

    // :180-200. Width gating belongs to ChatView, which supplies showsMinimap.
    private static bool ShouldShowMinimap(bool enabled, IReadOnlyList<DisplayBlock> blocks) => enabled && blocks.Count(block => block.HasUserMessage) > 1;
    // :179-208
    private Control? CreateRail(IReadOnlyList<DisplayBlock> blocks, bool enabled, double columnHeight) => !ShouldShowMinimap(enabled, blocks) ? null : new TimelineMinimapColumn(blocks, tracking, Math.Max(0, columnHeight - WindowChrome.TitlebarHeight), (id, animated) => Jump(id, blocks, animated)) { Width = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, WindowChrome.TitlebarHeight, 0, 0), RenderTransform = new TranslateTransform(-ChatView.GetReserveSlideLeading(this) - ChatView.GetReserveSlideOffset(this), 0) };
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
    private static double VisibleFraction(Control row, ScrollViewer scroll)
    {
        var point = row.TranslatePoint(default, scroll); if (point is null || row.Bounds.Width <= 0 || row.Bounds.Height <= 0) return 0;
        var rect = new Rect(point.Value, row.Bounds.Size); var intersection = rect.Intersect(new Rect(scroll.Viewport)); return intersection.Width * intersection.Height / (rect.Width * rect.Height);
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
    // :242-252. Avalonia offsets are not SwiftUI state; run the source curve on the shared frame clock.
    private void ScrollTo(Vector target, Animation? animation)
    {
        if (scrollFrame is not null) scrollFrames?.CancelFrame(scrollFrame); scrollFrame = null;
        if (currentScroll is not { } scroll) return; if (animation is null) { scroll.Offset = target; return; }
        var origin = scroll.Offset; var started = SwiftClock.Now; scrollFrames = FrameClock.Source.Current;
        Action<TimeSpan>? frame = null; frame = _ => { var elapsed = (SwiftClock.Now - started).TotalSeconds; var complete = elapsed >= animation.Duration; var progress = complete ? 1 : animation.Progress(elapsed); scroll.Offset = origin + (target - origin) * progress; if (complete) scrollFrame = null; else scrollFrames.RequestFrame(frame!); };
        scrollFrame = frame; scrollFrames.RequestFrame(frame);
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
        var anchorMove = anchorsBottomOnGrowth ? next.ContentHeight - old.ContentHeight : 0; var readerMoved = Math.Abs(next.Offset - old.Offset - anchorMove) > 1;
        if (readerMoved) tracking.NoteReaderMovement(); var scrolled = tracking.IsUserScrolling || (!tracking.IsArriving && readerMoved && next.ContainerHeight == old.ContainerHeight && !tracking.IsCoasting); var atRest = !tracking.IsUserScrolling && !tracking.IsCoasting;
        if (atRest && !scrolled) { var shrink = Math.Max(0, old.ContentHeight - next.ContentHeight); var drift = Math.Abs(next.Offset - old.Offset - anchorMove); if (tracking.NoteDrift(shrink + drift, JumpTolerance(viewportHeight))) tracking.ArmViewportRefresh(); if (shrink > 1) tracking.ArmBlankWatch(); } else if (scrolled) tracking.ResetDrift();
        if (next.DistanceFromBottom < -1 && atRest && (next.ContentHeight != old.ContentHeight || next.ContainerHeight != old.ContainerHeight)) MainActor.Post(SnapEnd);
        else if (historyLoadPending && next.ContentHeight > old.ContentHeight) { historyLoadPending = false; anchorsBottomOnGrowth = tracking.IsPinnedToBottom || next.DistanceFromBottom < 48; if (tracking.IsPinnedToBottom && next.DistanceFromBottom > 1) MainActor.Post(SnapEnd); }
        else if (scrolled) tracking.IsPinnedToBottom = anchorsBottomOnGrowth = next.DistanceFromBottom < 48;
        else if ((tracking.IsPinnedToBottom || old.DistanceFromBottom < 48) && atRest && Math.Abs(next.DistanceFromBottom) > 1 && !liveResizeActive) { tracking.IsPinnedToBottom = anchorsBottomOnGrowth = true; MainActor.Post(SnapEnd); }
    }
    // :26-35. ChatView's external mounting boundary must use these eight inputs; this view reads no live title/effort/mode.
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
    private void ResetThread() { windowStartID = null; anchorsBottomOnGrowth = true; historyLoadPending = false; tracking.ClearVisible(); }
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
        pills ??= new(); var wanted = new Key { Entries = @for.ToArray(), TurnEnds = @for.Count(entry => entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd), IsRunning = isRunning, HydraMergeID = hydraMergeID, Pills = pills, ShowReasoning = showReasoning }; // :1720-1737
        if (key is not null && wanted.TurnEnds == key.TurnEnds && wanted.SameFlags(key) && wanted.SamePrefix(key) && wanted.Entries.Count == key.Entries.Count) return built.Blocks; // :1738
        if (key is not null && wanted.Extends(key) && BuildTail(@for, key.Entries.Count, showReasoning, isRunning, hydraMergeID, pills) is { } tail) built = tail;
        else { meta = TimelineMeta.Build(@for); built = DisplayBlock.Build(@for, 0, [], meta, showReasoning, isRunning, hydraMergeID, pills); }
        key = wanted; return built.Blocks; // :1739-1746
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
        public bool SamePrefix(Key old) => Entries.Count >= old.Entries.Count && old.Entries.Select((entry, index) => ReferenceEquals(entry, Entries[index])).All(equal => equal);
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
        // :1902-1935. STOP #1 only at external leaf constructors; dispatch and arguments are owned.
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

    // :1874-1882 private static func splitTurnEntries(_:) -> (userEntries:, content:)
    private static (ValueList<TimelineEntry> UserEntries, ValueList<TimelineEntry> Content) SplitTurnEntries(IReadOnlyList<TimelineEntry> entries) =>
        (new(entries.Where(entry => entry.TimelineEntryKind == TimelineEntry.Kind.User)), new(entries.Where(entry => entry.TimelineEntryKind != TimelineEntry.Kind.User))); // :1887-1895
}

/// <summary>:1942-2002 <c>private struct TurnRunningBlock: View</c>.</summary>
public sealed class TurnRunningBlock : Decorator
{
    /// <param name="runtime">:1931 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="entries">:1932 <c>let entries: [TimelineEntry]</c>.</param>
    /// <param name="showsWorking">:1933 <c>let showsWorking: Bool</c>.</param>
    /// <param name="context">:1934 <c>let context: RowContext</c>.</param>
    // :1939-1989 body
    public TurnRunningBlock(ThreadRuntime runtime, ValueList<TimelineEntry> entries, bool showsWorking, RowContext context)
    {
        var partition = Partition(new TurnGroupCache().Groups(entries)); var stack = new StackPanel { Spacing = TimelineMetrics.RowSpacing }; Child = stack;
        foreach (var group in partition.Rows) SwiftTransition.Insert(stack, stack.Children.Count, new TurnRow(group, runtime, context).Transition(ThreadTimeline.RowTransition));
        if (partition.Heads is { } heads) SwiftTransition.Insert(stack, stack.Children.Count, new TurnRow(heads, runtime, context).Transition(ThreadTimeline.RowTransition));
        if (showsWorking || partition.Work.Count != 0) SwiftTransition.Insert(stack, stack.Children.Count, new WorkingBlockView(runtime, partition.Work, context.WorkingDirectory, context.Chronological, showsWorking ? null : entries).Transition(ThreadTimeline.RowTransition)); // :1977-2001
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
    public TurnRow(TimelineGroup group, ThreadRuntime runtime, RowContext context) =>
        Child = new TimelineGroupView(group, runtime, null, false, context); // :2033-2036
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
        // :2053-2070. Every external leaf retains its exact arguments; STOP #1 until TimelineRows ports it.
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
    }
}

/// <summary>:2075-2197 <c>private struct WorkingIndicator: View</c>.</summary>
public sealed class WorkingIndicator : Decorator
{
    private Action<ValueList<ThinkingStep>, ValueList<TimelineEntry>, ValueList<TimelineEntry>, bool>? applyInputs;
    // :2080-2092. Updating view values retains the indicator's local expansion state.
    internal void UpdateValues(ValueList<ThinkingStep> thinkingSteps, ValueList<TimelineEntry> liveWork, ValueList<TimelineEntry> turnEntries, bool showsCard) => applyInputs?.Invoke(thinkingSteps, liveWork, turnEntries, showsCard);
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
        string? workingDirectory = null)
    {
        var isExpanded = chronological; var showsAllSteps = chronological; var now = SwiftDate.Now;
        var canExpand = thinkingSteps.Count != 0 || liveWork.Count != 0; var running = finishedAt is null;
        var shell = new Border { CornerRadius = new CornerRadius(TimelineMetrics.PillRadius), Background = SwiftColor.Quaternary.Opacity(0.32).Brush(this) }; Child = shell;
        string? drawnLabel = null;
        void Draw(bool force = true)
        {
            var elapsed = Math.Max(0, (now - startedAt).TotalSeconds); var word = WorkingWords.Word(seed, (long)elapsed);
            // STOP #2 at the external summary only: no guessed tool labels.
            var label = liveWork.Any(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Tool) ? WorkGroupSummary.Text(liveWork) : running ? word + "\u2026" : "Earlier steps"; // :2100-2105
            if (!force && drawnLabel == label) return; drawnLabel = label;
            var column = new StackPanel { Spacing = TimelineMetrics.RowSpacing };
            var header = new BaselineHeader(showsCard);
            header.Children.Add(new HydraWorkingTitle(label, running, TextAlignment.Left)); header.Children.Add(new HydraElapsedTime(startedAt, finishedAt, running));
            if (canExpand) { var chevron = SwiftImage.SystemName("chevron.right").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)).ForegroundStyle(SwiftColor.Tertiary).Transition(SwiftTransition.Opacity); chevron.RenderTransform = new RotateTransform(isExpanded ? 90 : 0); header.Children.Add(chevron); }
            var inner = new StackPanel { Spacing = 10, Children = { header } };
            if (showsCard) inner.Children.Add(new HydraProgressBar(runtime, startedAt, finishedAt, running ? HydraHeadInfo.Status.Running : HydraHeadInfo.Status.Stopped, Chrome.Accent, turnEntries));
            var frame = new Border { Padding = showsCard ? new Thickness(14, 11) : new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical), CornerRadius = new CornerRadius(TimelineMetrics.PillRadius), Background = Brushes.Transparent, Child = inner, HorizontalAlignment = HorizontalAlignment.Left }; // :2147 .contentShape(RoundedRectangle(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))
            if (showsCard) frame.Width = CardWidth;
            var button = PlainControls.Button(frame, () => { if (!canExpand) return; Transaction.WithAnimation(Animation.Snappy(0.24), () => { isExpanded = !isExpanded; if (isExpanded) ThreadTimeline.GetRevealTimelineEnd(this)?.Follow(); Draw(); }); });
            button.HorizontalAlignment = HorizontalAlignment.Left; // :2106 VStack(alignment: .leading) hugs its content; the button never stretches to the row's width.
            ToolTip.SetTip(button, canExpand ? isExpanded ? "Hide these steps" : "Show these steps" : ""); Avalonia.Automation.AutomationProperties.SetHelpText(button, canExpand ? "Shows what the agent is doing" : ""); column.Children.Add(button);
            if (isExpanded && canExpand)
            {
                var details = new StackPanel { Spacing = TimelineMetrics.RowSpacing };
                if (thinkingSteps.Count != 0)
                {
                    var thinking = new StackPanel { Spacing = TimelineMetrics.RowSpacing, Margin = new Thickness(TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, 0, 0, 0) };
                    MarkdownView.SetMarkdownPointSize(thinking, 12); MarkdownView.SetMarkdownDimmed(thinking, true);
                    foreach (var step in thinkingSteps) thinking.Children.Add(new MarkdownView(step.Text, step.IsStreaming)); SwiftTransition.Insert(details, details.Children.Count, thinking.Transition(ThreadTimeline.RowTransition));
                }
                // STOP #2: WorkSteps remains the external implementation, with the original binding and arguments.
                if (liveWork.Count != 0) details.Children.Add(new WorkSteps(liveWork, runtime, new Binding<bool>(() => showsAllSteps, value => showsAllSteps = value), workingDirectory));
                column.Children.Add(new Border { Padding = new Thickness(14, 0, 14, 11), Child = details });
            }
            shell.Child = column;
        }
        applyInputs = (thinking, work, entries, card) => { thinkingSteps = thinking; liveWork = work; turnEntries = entries; showsCard = card; canExpand = thinking.Count != 0 || work.Count != 0; Draw(); };
        Draw(); // :2185-2188. The source retains chronological, but ThreadTimeline always passes false.
        this.Task(() => FrontMonitor.Shared.Current.IsVisible && running, async active => { if (!active) return; while (await SwiftTask.TrySleep(TimeSpan.FromSeconds(1))) await MainActor.Run(() => { now = SwiftDate.Now; Draw(false); }); }); // :2189-2196
    }

    // :2122. The title fills only the card's surplus width; every child aligns on its first text baseline.
    private sealed class BaselineHeader(bool fillsTitle) : Panel
    {
        private static double Baseline(Control child) => child switch { TextBlock text => text.TextLayout.TextLines.FirstOrDefault()?.Baseline ?? child.DesiredSize.Height, Decorator { Child: { } content } => Baseline(content), Panel panel when panel.Children.Count > 0 => Baseline(panel.Children[0]), _ => child.DesiredSize.Height };
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var other = Children.Skip(1).Sum(child => child.DesiredSize.Width) + Math.Max(0, Children.Count - 1) * 8;
            if (fillsTitle && double.IsFinite(availableSize.Width) && Children.Count > 0) Children[0].Measure(new Size(Math.Max(0, availableSize.Width - other), availableSize.Height));
            var above = Children.Select(Baseline).DefaultIfEmpty().Max(); var below = Children.Select(child => child.DesiredSize.Height - Baseline(child)).DefaultIfEmpty().Max();
            return new Size(fillsTitle && double.IsFinite(availableSize.Width) ? availableSize.Width : Children.Sum(child => child.DesiredSize.Width) + Math.Max(0, Children.Count - 1) * 8, above + below);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var baseline = Children.Select(Baseline).DefaultIfEmpty().Max(); var x = 0.0; var other = Children.Skip(1).Sum(child => child.DesiredSize.Width) + Math.Max(0, Children.Count - 1) * 8;
            for (var i = 0; i < Children.Count; i++) { var child = Children[i]; var width = i == 0 && fillsTitle ? Math.Max(0, finalSize.Width - other) : child.DesiredSize.Width; child.Arrange(new Rect(x, baseline - Baseline(child), width, child.DesiredSize.Height)); x += width + 8; } return finalSize;
        }
    }
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
    /// <param name="runtime">:2251 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="liveWork">:2253 <c>let liveWork: [TimelineEntry]</c>.</param>
    /// <param name="workingDirectory">:2254 <c>let workingDirectory: String?</c>.</param>
    /// <param name="chronological">:2255 <c>var chronological = false</c>.</param>
    /// <param name="earlierEntries">:2258 <c>var earlierEntries: [TimelineEntry]?</c>.</param>
    // :2260-2274 body
    public WorkingBlockView(ThreadRuntime runtime, ValueList<TimelineEntry> liveWork, string? workingDirectory, bool chronological = false, ValueList<TimelineEntry>? earlierEntries = null)
    {
        this.Track(() => (AppEnvironment.GetAppModel(this).Settings.ShowsWorkingCard, AppEnvironment.GetAppModel(this).Settings.ShowReasoning, earlierEntries is null && AppEnvironment.GetAppModel(this).Settings.ShowReasoning ? Thinking(runtime) : new ValueList<ThinkingStep>([])), values =>
        {
            var thinking = earlierEntries is null && values.ShowReasoning ? values.Item3 : new([]); var entries = earlierEntries ?? TurnEntries(runtime);
            if (Child is WorkingIndicator indicator) indicator.UpdateValues(thinking, liveWork, entries, values.ShowsWorkingCard);
            else Child = new WorkingIndicator(runtime, earlierEntries?.FirstOrDefault()?.Item.Date ?? runtime.TurnStartedAt ?? SwiftDate.Now, WorkingWords.Seed(SwiftUUID.UuidString(runtime.ThreadID)), thinking, liveWork, entries, values.ShowsWorkingCard, earlierEntries?.LastOrDefault()?.Item.Date, chronological, workingDirectory);
        }); // :2267-2285
    }

    // :2278-2287 private static func turnEntries(of:) -> [TimelineEntry]
    private static ValueList<TimelineEntry> TurnEntries(ThreadRuntime of)
    {
        if (of.Entries.LastOrDefault()?.TurnID is not { } turn) return new([]); var rows = new List<TimelineEntry>();
        foreach (var entry in of.Entries.Reverse()) { if (entry.TurnID is not { } entryTurn) continue; if (entryTurn != turn) break; rows.Add(entry); } rows.Reverse(); return new(rows); // :2290-2299
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
