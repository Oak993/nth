using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Panes;

// PORT-ONLY: web surfaces W3a, the pane workspace host (E:/Droppy/work/design/web-surfaces/DESIGN.md 2.6 motion, 3.1-3.4
// layout, open, close, expand, 3.12 look, 4.2 dragging; ED-NEW-web-w3a-1, ED-NEW-web-w3a-2). No Swift counterpart. It
// draws a thread's PaneWorkspaceState: the chat column (its ChatSlot), one PaneCard per open pane, and one shared
// Splitter per solver gutter. The rectangles are PaneLayout.Solve's; this class only moves to them:
// - a state change springs every rectangle on Chrome.PanelSlide's spring (response 0.32, damping 0.9), each card's
//   x, y, width and height on its own interruptible spring, so a new change bends the motion instead of restarting it;
//   all springs share their history, so the 8 DIP gaps hold on every frame;
// - an opened pane appears at its final rectangle, fading in from scale 0.96; a closed one fades out toward 0.96 and
//   leaves when it has settled (the canvas's open and close). Reduce Motion: the 0.18 s ease-out and no scale;
// - a window resize, a sidebar slide and a gutter drag snap (the window's live resize disables springs, 2.6; a drag
//   tracks 1:1, 4.2); pressing a gutter settles everything in flight at once;
// - the chat column reflows live (re-wrapping each frame) or under the clip fallback (ChatSlot), whichever spike (h)
//   decides; DefaultChatReflow is Clip until it has run.
// Panes sit under the title band (the caption buttons' row) with an 8 DIP margin to the window's right and bottom; the
// chat column keeps the full height, as it does today. The layout is solved left to right: Avalonia mirrors the whole
// subtree under a right-to-left window, pointer travel included (the Splitter un-mirrors its own travel).

namespace Marrow.App.Views.Panes;

/// <summary>How the chat column follows a width spring (DESIGN 2.6, spike (h)).</summary>
public enum ChatReflow
{
    /// <summary>The transcript re-wraps every frame of the spring.</summary>
    Live,

    /// <summary>The transcript keeps its start layout under a springing clip and re-wraps once, cross-faded, at the end.</summary>
    Clip,
}

/// <summary>The detail area of a thread: the chat column and its panes.</summary>
public sealed partial class PaneWorkspace : Panel
{
    /// <summary>The window's margin to the right and bottom of the panes (the gap).</summary>
    public const double EdgeMargin = PaneKinds.Gap;

    /// <summary>The scale an opening pane grows from and a closing one shrinks toward (the canvas's 0.96).</summary>
    public const double OpenScale = 0.96;

    /// <summary>Panes start under the title band, clear of the caption buttons.</summary>
    public static double TopBand => WindowChrome.TitlebarHeight + PaneKinds.Gap;

    /// <summary>
    /// The chat reflow every new workspace uses. Clip until spike (h) shows the live spring holds 8 ms per frame at p99
    /// with a 2,000-turn transcript while streaming (DESIGN 2.6); then Live.
    /// </summary>
    public static ChatReflow DefaultChatReflow { get; set; } = ChatReflow.Clip;

    private readonly ChatSlot chat = new();
    private readonly Dictionary<PaneKind, CardMotion> cards = [];
    private readonly Dictionary<string, Splitter> splitters = [];
    private readonly HashSet<PaneKind> unavailable = [];
    private readonly SplitterSpring chatWidth = new(0);
    private Func<PaneKind, PaneContent?> factory;
    private PaneWorkspaceState state = PaneWorkspaceState.Default;
    private PaneLayoutResult? solved;
    private Size solvedSize;
    private bool dirty = true;
    private bool pendingAnimate;
    private Action<TimeSpan>? pendingFrame;
    private IFrameSource? frameSource;
    private TimeSpan? lastFrame;
    private bool moving;
    private bool dragging;
    private bool reflowing;
    private bool chatWorking;
    private bool removalPosted;
    private WindowLiveResize? resizeOwner;

    public PaneWorkspace(Control chatColumn, Func<PaneKind, PaneContent?> factory)
    {
        ArgumentNullException.ThrowIfNull(chatColumn);
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        chat.Content = chatColumn;
        Children.Add(chat);
        foreach (var key in PaneGutterKeys.All)
        {
            var axis = key is PaneGutterKeys.Row1 or PaneGutterKeys.Row2 ? SplitterOrientation.Horizontal : SplitterOrientation.Vertical;
            var splitter = new Splitter(axis, new GutterTarget(this, key))
            {
                ShowsReadout = true,
                CollapseSide = SplitterSide.None,
                IsVisible = false,
                ZIndex = 20,
            };
            splitters[key] = splitter;
            Children.Add(splitter);
        }
        ClipToBounds = false;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        DetachedFromVisualTree += (_, _) =>
        {
            CancelDock(immediately: true);
            SettleAll();
            dragging = false;
            reflowing = false;
            UpdateResizing();
        };
    }

    /// <summary>The user changed the workspace (open, close, expand, a tab, a gutter); raised once per change, a drag at its end.</summary>
    public event Action<PaneWorkspaceState>? StateChanged;

    /// <summary>The state drawn (normalised).</summary>
    public PaneWorkspaceState State => state;

    /// <summary>The layout the workspace is at or moving to, in solver coordinates (under the title band), or null before a layout.</summary>
    public PaneLayoutResult? Layout => solved;

    /// <summary>The chat column's slot.</summary>
    public ChatSlot Chat => chat;

    /// <summary>How the chat column follows a width spring.</summary>
    public ChatReflow Reflow { get; set; } = DefaultChatReflow;

    /// <summary>Any spring is still moving.</summary>
    public bool IsMoving => moving;

    /// <summary>A gutter is being dragged.</summary>
    public bool IsDragging => dragging;

    /// <summary>Moves keyboard focus to the chat (closing a focused pane; the working chip). Defaults to the composer.</summary>
    public Action? FocusChat { get; set; }

    /// <summary>A turn is running in the chat: an expanded pane's header shows the "Chat is working" chip (3.4).</summary>
    public bool IsChatWorking
    {
        get => chatWorking;
        set
        {
            if (chatWorking == value) return;
            chatWorking = value;
            foreach (var (kind, motion) in cards) motion.Card.Header.ShowsWorking = value && solved?.Expanded == kind;
        }
    }

    /// <summary>The card of an open (or still leaving) pane.</summary>
    public PaneCard? CardOf(PaneKind kind) => cards.TryGetValue(kind, out var motion) ? motion.Card : null;

    /// <summary>The cards present, leaving ones included.</summary>
    public IReadOnlyList<PaneCard> Cards => cards.Values.Select(motion => motion.Card).ToList();

    /// <summary>The splitter of a gutter key (hidden when the layout has no such gutter).</summary>
    public Splitter SplitterOf(string key) => splitters[key];

    /// <summary>The chat column's rectangle on screen now.</summary>
    public Rect ChatBounds => new(0, 0, ChatShownWidth, solvedSize.Height);

    /// <summary>A card's rectangle on screen now (in the workspace, under the title band), or null when it is not drawn.</summary>
    public Rect? BoundsOf(PaneKind kind) => cards.TryGetValue(kind, out var motion) && motion.Card.IsVisible ? Shown(motion) : null;

    /// <summary>A card's opacity and scale now.</summary>
    public (double Opacity, double Scale)? LookOf(PaneKind kind) => cards.TryGetValue(kind, out var motion) ? (motion.Card.Opacity, motion.Card.Scale) : null;

    /// <summary>
    /// Draws <paramref name="next"/>. Animated, the rectangles spring; otherwise they snap. Raises nothing: this is how
    /// the owner (the thread's runtime) tells the workspace its state.
    /// </summary>
    public void SetState(PaneWorkspaceState next, bool animate = true) => Apply(next, animate);

    /// <summary>A thread switch: new contents for every pane, the state drawn at once.</summary>
    public void Reload(Func<PaneKind, PaneContent?> contents, PaneWorkspaceState next)
    {
        factory = contents ?? throw new ArgumentNullException(nameof(contents));
        CancelDock(immediately: true);
        SettleAll();
        foreach (var motion in cards.Values) RemoveCard(motion);
        cards.Clear();
        unavailable.Clear();
        chat.FinishNow();
        state = PaneWorkspaceState.Default;
        if (!Apply(next, animate: false))
        {
            dirty = true;
            pendingAnimate = false;
            InvalidateMeasure();
        }
    }

    public void Open(PaneKind kind) => Commit(state.Open(kind));

    public void Close(PaneKind kind)
    {
        var hadFocus = cards.TryGetValue(kind, out var motion) && motion.Card.IsKeyboardFocusWithin;
        Commit(state.Close(kind));
        if (hadFocus) MoveFocusToChat();
    }

    public void ToggleExpanded(PaneKind kind) => Commit(state.ToggleExpanded(kind));

    public void RestoreExpanded() => Commit(state.RestoreExpanded());

    public void Select(PaneKind kind) => Commit(state.Select(kind));

    /// <summary>Pointer-down settling (4.2): every spring in flight lands on its target in this frame.</summary>
    public void SettleAll()
    {
        if (pendingFrame is not null)
        {
            frameSource?.CancelFrame(pendingFrame);
            pendingFrame = null;
        }
        lastFrame = null;
        chatWidth.Settle();
        foreach (var motion in cards.Values) motion.Settle();
        SettleDock();
        ApplyLooks();
        InvalidateMeasure();
        if (moving) FinishMotion();
    }

    private void Commit(PaneWorkspaceState next, bool animate = true)
    {
        if (Apply(next, animate)) StateChanged?.Invoke(state);
    }

    private bool Apply(PaneWorkspaceState? next, bool animate)
    {
        next = (next ?? PaneWorkspaceState.Default).Normalized();
        if (next.Equals(state)) return false;
        if (!committing) CancelDock(immediately: true); // a state that changes under a drag (a pane opened elsewhere) ends it
        state = next;
        SyncCards(animate);
        pendingAnimate = dirty ? pendingAnimate && animate : animate;
        dirty = true;
        InvalidateMeasure();
        return true;
    }

    private static IEnumerable<PaneKind> OpenKinds(PaneWorkspaceState s) =>
        s.IsSideChatOpen ? s.GridPanes.Append(PaneKind.SideChat) : s.GridPanes;

    // Cards come and go here, outside layout: a newly opened pane gets its card (or a leaving one comes back), a closed
    // one starts leaving (or goes at once without motion).
    private void SyncCards(bool animate)
    {
        var open = OpenKinds(state).ToHashSet();
        foreach (var kind in open)
        {
            if (cards.TryGetValue(kind, out var existing))
            {
                existing.Leaving = false;
                existing.Card.IsLeaving = false;
                existing.Card.IsHitTestVisible = true;
                continue;
            }
            if (unavailable.Contains(kind)) continue;
            PaneContent? content;
            try { content = factory(kind); }
            catch (Exception) { content = null; }
            if (content is null)
            {
                unavailable.Add(kind);
                continue;
            }
            var card = new PaneCard(kind, content) { IsVisible = false };
            var motion = new CardMotion(card) { Fresh = true };
            card.Header.CloseRequested += () => Close(kind);
            card.Header.ExpandRequested += () => ToggleExpanded(kind);
            card.Header.TabChosen += Select;
            card.Header.WorkingChipClicked += () =>
            {
                RestoreExpanded();
                MoveFocusToChat();
            };
            if (IsDockable(kind)) PaneDockGesture.Attach(card.Header, kind, () => this); // web W4: the terminal's header lifts the pane (3.6)
            cards[kind] = motion;
            Children.Insert(1, card);
        }
        foreach (var (kind, motion) in cards.ToList())
        {
            if (open.Contains(kind) || motion.Leaving) continue;
            motion.Leaving = true;
            motion.Card.IsLeaving = true;
            motion.Card.IsHitTestVisible = false;
            if (!animate || !FrameClock.IsBound || !motion.Card.IsVisible)
            {
                cards.Remove(kind);
                RemoveCard(motion);
            }
        }
    }

    private void RemoveCard(CardMotion motion)
    {
        motion.Card.Detach();
        Children.Remove(motion.Card);
    }

    // The state the layout draws: panes this build cannot draw are left out, so no empty card ever shows.
    private PaneWorkspaceState Drawn()
    {
        var s = state;
        foreach (var kind in OpenKinds(state).ToList())
            if (!cards.TryGetValue(kind, out var motion) || motion.Leaving) s = s.Close(kind);
        return s;
    }

    private bool ReduceMotion =>
        EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private WindowLiveResize? LiveResize => GetValue(AppEnvironment.WindowLiveResizeProperty);

    private double SolverWidth => Math.Max(0, solvedSize.Width - EdgeMargin);

    private double SolverHeight => Math.Max(0, solvedSize.Height - TopBand - EdgeMargin);

    private double ChatShownWidth => Math.Max(chatWidth.Value, Math.Min(chatWidth.Target, PaneKinds.ChatMinWidth));

    private static Rect Shown(CardMotion motion)
    {
        var size = PaneKinds.Size(motion.Card.Kind);
        var width = Math.Max(motion.W.Value, Math.Min(motion.W.Target, size.MinWidth));
        var height = Math.Max(motion.H.Value, Math.Min(motion.H.Target, size.MinHeight));
        return new Rect(motion.X.Value, motion.Y.Value, Math.Max(0, width), Math.Max(0, height));
    }

    // Moves every rectangle to the layout of the current state at `size`: springs when animated and the size is the
    // one solved before (a state change), snaps when the size changed (a window resize or a sidebar slide).
    private void Retarget(Size size, bool animate)
    {
        var first = solved is null;
        var sizeChanged = size != solvedSize;
        solvedSize = size;
        dirty = false;
        var drawn = Drawn();
        var result = PaneLayout.Solve(drawn, SolverWidth, SolverHeight);
        solved = result;
        var springs = animate && !first && !sizeChanged && FrameClock.IsBound && LiveResize?.IsActive != true;
        var reduce = ReduceMotion;
        var top = TopBand;
        var area = new Rect(0, top, SolverWidth, SolverHeight);

        // The chat column: the full area alone, its tile otherwise. Narrow mode keeps it full under the chosen pane until
        // W3c adds the narrow tab strip.
        var tiles = result.Mode == PaneLayoutMode.Tiles;
        var panes = tiles && result.Shown.Any(kind => kind != PaneKind.Chat);
        var chatTarget = panes ? result.Chat.Width : size.Width;
        if (springs)
        {
            if (Math.Abs(chatWidth.Target - chatTarget) > 0.01)
            {
                if (Reflow == ChatReflow.Clip) chat.Reserve(ChatShownWidth);
                else SetCaptionReserve(panes ? result.Chat.Right : size.Width, animates: true);
                chatWidth.SetTarget(chatTarget, reduce);
                reflowing = true;
                UpdateResizing();
            }
        }
        else
        {
            chatWidth.Snap(chatTarget);
            chat.FinishNow();
            SetCaptionReserve(panes ? result.Chat.Right : size.Width, animates: false);
        }

        var front = FrontOf(result);
        foreach (var (kind, motion) in cards)
        {
            var card = motion.Card;
            var header = card.Header;
            if (motion.Leaving)
            {
                motion.Opacity.SetTarget(0, reduce);
                motion.Scale.SetTarget(reduce ? 1 : OpenScale, reduce);
                if (!springs) motion.Settle();
                continue;
            }
            Rect? target = null;
            if (front == kind) target = area;
            else if (tiles && result.IsShown(kind) && result.BoundsOf(kind) is { } bounds)
                target = new Rect(bounds.X, bounds.Y + top, bounds.Width, bounds.Height);
            header.IsExpanded = tiles && result.Expanded == kind;
            header.CanExpand = tiles;
            header.ShowsWorking = chatWorking && tiles && result.Expanded == kind;
            var cell = result.CellOf(kind);
            header.SetTabs(tiles && cell is { HasTabs: true }
                ? cell.Tabs.Select(tab => new PaneHeaderTab(tab, PaneKinds.ShortName(tab), tab == cell.Shown)).ToList()
                : []);
            if (front == kind)
            {
                motion.Lifted = true;
                card.IsExpandedLook = true;
            }
            if (target is not { } rect)
            {
                motion.Visible = false;
                motion.Fresh = false;
                card.IsVisible = false;
                continue;
            }
            var appearing = !motion.Visible || !motion.Placed;
            if (appearing || !springs)
            {
                motion.SnapRect(rect);
                if (motion.Fresh && springs)
                {
                    motion.Opacity.Snap(0);
                    motion.Scale.Snap(reduce ? 1 : OpenScale);
                    motion.Opacity.SetTarget(1, reduce);
                    motion.Scale.SetTarget(1, reduce);
                }
                else if (!springs || appearing)
                {
                    motion.Opacity.Snap(1);
                    motion.Scale.Snap(1);
                }
            }
            else
            {
                motion.SetRect(rect, reduce);
                motion.Opacity.SetTarget(1, reduce);
                motion.Scale.SetTarget(1, reduce);
            }
            motion.Fresh = false;
            motion.Placed = true;
            motion.Visible = true;
            card.IsVisible = true;
        }

        var showsGutters = tiles && result.Expanded is null;
        foreach (var (key, splitter) in splitters)
        {
            var gutter = showsGutters ? result.Gutter(key) : null;
            splitter.IsVisible = gutter is not null;
            if (gutter is null) continue;
            splitter.Label = gutter.Label;
        }

        ApplyLooks();
        if (springs && AnyMoving)
        {
            moving = true;
            UpdateHeld();
            RequestFrame();
        }
        else if (!AnyMoving)
        {
            if (moving) FinishMotion();
            else
            {
                if (reflowing && chat.ReservedWidth is null)
                {
                    reflowing = false;
                    UpdateResizing();
                }
                foreach (var motion in cards.Values) motion.Lifted = motion.Card.IsExpandedLook = front == motion.Card.Kind;
                UpdateHeld();
                PostRemoval();
            }
        }
    }

    // The pane drawn over the others: the expanded one, or the narrow selection until W3c's tab strip.
    private static PaneKind? FrontOf(PaneLayoutResult result)
    {
        if (result.Mode == PaneLayoutMode.Narrow) return result.NarrowSelection == PaneKind.Chat ? null : result.NarrowSelection;
        return result.Expanded;
    }

    private bool AnyMoving => chatWidth.IsMoving || cards.Values.Any(motion => motion.IsMoving) || DockMoving;

    // The chat chrome row's trailing end keeps clear of the caption buttons only where the chat column runs under them.
    private void SetCaptionReserve(double chatRight, bool animates)
    {
        var overlap = WindowChrome.CaptionTrailingReserve - Math.Max(0, solvedSize.Width - chatRight);
        ChatView.SetChromeTrailingReserveAnimates(chat, animates);
        ChatView.SetChromeTrailingReserve(chat, overlap >= WindowChrome.CaptionTrailingReserve ? double.NaN : overlap);
    }

    private void UpdateHeld()
    {
        var held = moving || dragging;
        foreach (var motion in cards.Values) motion.Card.Header.IsHeld = held;
    }

    private void UpdateResizing()
    {
        var live = LiveResize;
        var resizing = dragging || reflowing;
        if (!ReferenceEquals(live, resizeOwner) && resizeOwner is not null) resizeOwner.IsWorkspaceResizing = false;
        resizeOwner = live;
        if (live is not null && live.IsWorkspaceResizing != resizing) live.IsWorkspaceResizing = resizing;
    }

    private void ApplyLooks()
    {
        foreach (var motion in cards.Values)
        {
            motion.Card.Opacity = Math.Clamp(motion.Opacity.Value, 0, 1);
            motion.Card.Scale = Math.Clamp(motion.Scale.Value, 0.9, 1.03);
            motion.Card.ZIndex = dock is { Source: DockSource.Card } lifting && lifting.Kind == motion.Card.Kind ? DockZIndex : motion.Lifted ? 10 : motion.Leaving ? 1 : 0;
        }
        ApplyDockLooks();
    }

    private void RequestFrame()
    {
        if (pendingFrame is not null) return;
        frameSource = FrameClock.Source.Current;
        pendingFrame = OnFrame;
        frameSource.RequestFrame(pendingFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        pendingFrame = null;
        var dt = lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.034) : 1.0 / 60;
        lastFrame = now;
        var any = chatWidth.Step(dt);
        foreach (var motion in cards.Values) any |= motion.Step(dt);
        any |= StepDock(dt);
        ApplyLooks();
        InvalidateMeasure();
        if (any)
        {
            RequestFrame();
            return;
        }
        lastFrame = null;
        FinishMotion();
    }

    // Everything has landed: closed cards leave, headers refit, the chat column lays out once at its new width.
    private void FinishMotion()
    {
        moving = false;
        var front = solved is { } result ? FrontOf(result) : null;
        foreach (var motion in cards.Values)
        {
            motion.Lifted = !motion.Leaving && front == motion.Card.Kind;
            motion.Card.IsExpandedLook = motion.Lifted;
        }
        ApplyLooks();
        UpdateHeld();
        PostRemoval();
        var chatRight = solved is { Mode: PaneLayoutMode.Tiles } tiles && tiles.Shown.Any(kind => kind != PaneKind.Chat) ? tiles.Chat.Right : solvedSize.Width;
        chat.EndReserve(() =>
        {
            SetCaptionReserve(chatRight, animates: false);
            reflowing = false;
            UpdateResizing();
        });
    }

    private void PostRemoval()
    {
        if (removalPosted) return;
        removalPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            removalPosted = false;
            foreach (var (kind, motion) in cards.ToList())
            {
                if (!motion.Leaving || motion.IsMoving) continue;
                cards.Remove(kind);
                RemoveCard(motion);
            }
        });
    }

    private void MoveFocusToChat()
    {
        if (FocusChat is { } focus)
        {
            focus();
            return;
        }
        var composer = chat.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.IsEffectivelyVisible && box.IsEnabled);
        if (composer is not null)
        {
            composer.Focus();
            return;
        }
        chat.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(element => element.Focusable && element.IsEffectivelyVisible)?.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc in an expanded pane restores it (3.4).
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
        if (state.Expanded is not { } expanded || !cards.TryGetValue(expanded, out var motion) || !motion.Card.IsKeyboardFocusWithin) return;
        RestoreExpanded();
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : solvedSize.Width,
            double.IsFinite(availableSize.Height) ? availableSize.Height : solvedSize.Height);
        if (dirty || size != solvedSize) Retarget(size, pendingAnimate && size == solvedSize);
        pendingAnimate = false;
        chat.Measure(new Size(ChatShownWidth, size.Height));
        foreach (var motion in cards.Values)
            if (motion.Card.IsVisible) motion.Card.Measure(Shown(motion).Size);
        foreach (var (key, splitter) in splitters)
            if (splitter.IsVisible && GutterRect(key) is { } rect) splitter.Measure(rect.Size);
        MeasureDock(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != solvedSize) Retarget(finalSize, false);
        chat.Arrange(new Rect(0, 0, ChatShownWidth, finalSize.Height));
        foreach (var motion in cards.Values)
        {
            if (!motion.Card.IsVisible) continue;
            var rect = Shown(motion);
            motion.Card.Measure(rect.Size);
            motion.Card.Arrange(rect);
        }
        foreach (var (key, splitter) in splitters)
            if (splitter.IsVisible && GutterRect(key) is { } rect) splitter.Arrange(rect);
        ArrangeDock();
        return finalSize;
    }

    /// <summary>A gutter's splitter rectangle in the workspace: 12 DIP across, centred on the gap.</summary>
    internal Rect? GutterRect(string key)
    {
        if (solved?.Gutter(key) is not { } gutter) return null;
        var half = Splitter.DividerThickness / 2;
        var top = TopBand;
        return gutter.Axis == PaneGutterAxis.X
            ? new Rect(gutter.Position - half, top + gutter.SpanStart, Splitter.DividerThickness, Math.Max(0, gutter.SpanLength))
            : new Rect(gutter.SpanStart, top + gutter.Position - half, Math.Max(0, gutter.SpanLength), Splitter.DividerThickness);
    }

    /// <summary>One card's springs: its rectangle, opacity and scale.</summary>
    private sealed class CardMotion(PaneCard card)
    {
        public PaneCard Card { get; } = card;

        public SplitterSpring X { get; } = new(0);

        public SplitterSpring Y { get; } = new(0);

        public SplitterSpring W { get; } = new(0);

        public SplitterSpring H { get; } = new(0);

        public SplitterSpring Opacity { get; } = new(0, epsilon: 0.002);

        public SplitterSpring Scale { get; } = new(1, epsilon: 0.0005);

        /// <summary>Opened since the last layout: it fades in.</summary>
        public bool Fresh { get; set; }

        /// <summary>Has had a rectangle.</summary>
        public bool Placed { get; set; }

        /// <summary>Drawn in the last layout.</summary>
        public bool Visible { get; set; }

        public bool Leaving { get; set; }

        /// <summary>Drawn over the others (expanded, or moving back from it).</summary>
        public bool Lifted { get; set; }

        public bool IsMoving => X.IsMoving || Y.IsMoving || W.IsMoving || H.IsMoving || Opacity.IsMoving || Scale.IsMoving;

        public void SnapRect(Rect rect)
        {
            X.Snap(rect.X);
            Y.Snap(rect.Y);
            W.Snap(rect.Width);
            H.Snap(rect.Height);
        }

        public void SetRect(Rect rect, bool reduce)
        {
            X.SetTarget(rect.X, reduce);
            Y.SetTarget(rect.Y, reduce);
            W.SetTarget(rect.Width, reduce);
            H.SetTarget(rect.Height, reduce);
        }

        public void Settle()
        {
            X.Settle();
            Y.Settle();
            W.Settle();
            H.Settle();
            Opacity.Settle();
            Scale.Settle();
        }

        public bool Step(double seconds)
        {
            var any = X.Step(seconds);
            any |= Y.Step(seconds);
            any |= W.Step(seconds);
            any |= H.Step(seconds);
            any |= Opacity.Step(seconds);
            any |= Scale.Step(seconds);
            return any;
        }
    }

    /// <summary>A gutter's splitter: the shared Splitter's commands on the solver's ratios.</summary>
    private sealed class GutterTarget(PaneWorkspace owner, string key) : SplitterTarget
    {
        private double dragBase;
        private PaneWorkspaceState? origin;

        private PaneGutter? Gutter => owner.solved?.Gutter(key);

        public override SplitterReport Report() => Gutter is { } gutter
            ? SplitterMath.Report(gutter.Value, false, new SplitterLimits(gutter.NormalMinimum, gutter.NormalMaximum, gutter.DefaultValue), text: gutter.ValueText)
            : new SplitterReport(0, 0, 0, false, 0, "", "");

        public override void Settle() => owner.SettleAll();

        public override void Begin()
        {
            owner.SettleAll();
            origin = owner.state;
            dragBase = Gutter?.Value ?? 0;
            owner.dragging = true;
            owner.UpdateResizing();
            owner.UpdateHeld();
        }

        public override void Move(Vector travel)
        {
            if (!owner.dragging || Gutter is not { } gutter) return;
            var along = gutter.Axis == PaneGutterAxis.X ? travel.X : travel.Y;
            owner.Apply(PaneLayout.SetGutter(owner.state, owner.SolverWidth, owner.SolverHeight, key, dragBase + gutter.ValueSign * along), animate: false);
        }

        public override void End(bool moved)
        {
            if (!owner.dragging) return;
            owner.dragging = false;
            owner.UpdateResizing();
            owner.UpdateHeld();
            if (origin is { } before && !before.Equals(owner.state)) owner.StateChanged?.Invoke(owner.state);
            origin = null;
        }

        public override void Reset() => owner.Commit(PaneLayout.Reset(owner.state, owner.SolverWidth, owner.SolverHeight, key));

        public override void Step(Vector delta)
        {
            if (Gutter is not { } gutter) return;
            var along = gutter.Axis == PaneGutterAxis.X ? delta.X : delta.Y;
            Set(gutter.Value + gutter.ValueSign * along);
        }

        public override void StepTo(bool maximum)
        {
            if (Gutter is not { } gutter) return;
            Set(maximum ? gutter.NormalMaximum : gutter.NormalMinimum);
        }

        // Collapsing a side to a rail is W3b's; this unit's gutters only resize.
        public override void Collapse(bool collapse)
        {
        }

        public override bool CanSetValue => true;

        public override void SetValue(double value) => Set(value);

        private void Set(double value) =>
            owner.Commit(PaneLayout.SetGutter(owner.state, owner.SolverWidth, owner.SolverHeight, key, value));
    }
}
