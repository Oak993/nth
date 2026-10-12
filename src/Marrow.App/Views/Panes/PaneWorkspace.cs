using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
// Narrow mode (web surfaces W3c, DESIGN 3.8, ED-NEW-web-w3c-1): when the solver says the window is too narrow for the chat and
// one pane, the workspace shows one surface at a time under a glass tab strip in the title band (Chat, then the open panes in
// order). A switch cross-fades with an 8 DIP slide on the same springs, a pane that opens is chosen and one that closes gives
// way to the chat; the surface not chosen is out of the layout and out of the accessibility tree once it has faded. The chat
// column then starts below the title band (its own chrome row under the strip), and every pane fills the solver's area.

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
public sealed class PaneWorkspace : Panel
{
    /// <summary>The window's margin to the right and bottom of the panes (the gap).</summary>
    public const double EdgeMargin = PaneKinds.Gap;

    /// <summary>The scale an opening pane grows from and a closing one shrinks toward (the canvas's 0.96).</summary>
    public const double OpenScale = 0.96;

    /// <summary>Panes start under the title band, clear of the caption buttons.</summary>
    public static double TopBand => WindowChrome.TitlebarHeight + PaneKinds.Gap;

    /// <summary>How far a narrow surface slides while it fades in or out (DESIGN 3.8: "cross-fades with an 8 DIP slide").</summary>
    public const double NarrowSlide = 8;

    /// <summary>
    /// The chat reflow every new workspace uses. Clip until spike (h) shows the live spring holds 8 ms per frame at p99
    /// with a 2,000-turn transcript while streaming (DESIGN 2.6); then Live.
    /// </summary>
    public static ChatReflow DefaultChatReflow { get; set; } = ChatReflow.Clip;

    private readonly ChatSlot chat = new();
    private readonly NarrowTabStrip strip = new() { IsVisible = false, ZIndex = 15 };
    private readonly SplitterSpring stripOpacity = new(0, epsilon: 0.002);
    private readonly SplitterSpring chatOpacity = new(1, epsilon: 0.002);
    private readonly SplitterSpring chatSlide = new(0);
    private readonly TranslateTransform chatTranslate = new();
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
    private bool narrowMode;
    private bool chatHiding;
    private double chatTop;
    private PaneKind? narrowShown;

    public PaneWorkspace(Control chatColumn, Func<PaneKind, PaneContent?> factory)
    {
        ArgumentNullException.ThrowIfNull(chatColumn);
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        chat.Content = chatColumn;
        chat.RenderTransform = chatTranslate;
        Children.Add(chat);
        strip.TabChosen += Select;
        Children.Add(strip);
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

    /// <summary>The chat column's rectangle on screen now (below the title band in narrow mode).</summary>
    public Rect ChatBounds => new(0, chatTop, ChatShownWidth, Math.Max(0, solvedSize.Height - chatTop));

    /// <summary>The window is too narrow for the chat and a pane side by side: one surface shows under the tab strip (DESIGN 3.8).</summary>
    public bool IsNarrow => narrowMode;

    /// <summary>The surface narrow mode shows (the chat or a pane), or null in tiles mode.</summary>
    public PaneKind? NarrowSurface => narrowMode ? narrowShown : null;

    /// <summary>The narrow tab strip (visible only in narrow mode, and while it fades).</summary>
    public NarrowTabStrip NarrowStrip => strip;

    /// <summary>The strip's rectangle in the title band: clear of the caption buttons, with the pill centred in it.</summary>
    public Rect StripBounds => new(
        Marrow.UI.Chrome.Chrome.ChromeHorizontalPadding,
        Marrow.UI.Chrome.Chrome.ChromeTopPadding + (Marrow.UI.Chrome.Chrome.CapsuleHeight - NarrowTabStrip.PillHeight) / 2,
        Math.Max(0, solvedSize.Width - Marrow.UI.Chrome.Chrome.ChromeHorizontalPadding - WindowChrome.CaptionTrailingReserve),
        NarrowTabStrip.PillHeight);

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
        SettleAll();
        foreach (var motion in cards.Values) RemoveCard(motion);
        cards.Clear();
        unavailable.Clear();
        chat.FinishNow();
        narrowMode = false;
        narrowShown = null;
        chatHiding = false;
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

    public void Select(PaneKind kind) => Commit(kind == PaneKind.Chat ? state.Open(PaneKind.Chat) : state.Select(kind));

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
        chatOpacity.Settle();
        chatSlide.Settle();
        stripOpacity.Settle();
        foreach (var motion in cards.Values) motion.Settle();
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

        // Narrow mode (DESIGN 3.8): one surface at a time under the tab strip. Every surface fills the solver's area, the strip
        // standing in the title band above it, and the chat column starts below the title band.
        var tiles = result.Mode == PaneLayoutMode.Tiles;
        var narrow = !tiles;
        var surface = narrow ? result.NarrowSelection : (PaneKind?)null;
        var before = narrowMode ? narrowShown : (PaneKind?)null;
        var slide = narrow ? SlideDirection(result, surface!.Value) : 0;
        var chatWanted = !narrow || surface == PaneKind.Chat;
        var panes = tiles && result.Shown.Any(kind => kind != PaneKind.Chat);
        var chatTarget = panes ? result.Chat.Width : size.Width;
        var reserveSet = false;
        if (!chatWanted && springs && chat.IsVisible)
        {
            // The chat fades out at the width it has; it takes the full width once it is gone (FinishHidden).
        }
        else if (springs)
        {
            if (Math.Abs(chatWidth.Target - chatTarget) > 0.01)
            {
                if (Reflow == ChatReflow.Clip) chat.Reserve(ChatShownWidth);
                else
                {
                    SetCaptionReserve(panes ? result.Chat.Right : size.Width, animates: true);
                    reserveSet = true;
                }
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
            reserveSet = true;
        }
        if (narrow != narrowMode && !reserveSet) SetCaptionReserve(panes ? result.Chat.Right : size.Width, animates: false);
        RetargetChat(narrow, chatWanted, slide, springs, reduce, size.Width);

        var front = tiles ? result.Expanded : null;
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
            if (front == kind || (narrow && surface == kind)) target = area;
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
                if (motion.Hiding && springs) continue; // already fading out
                if (narrow && motion.Visible && springs)
                {
                    // The surface another tab replaced: it fades out and slides away, and leaves the layout once it has.
                    motion.Hiding = true;
                    card.IsHitTestVisible = false;
                    motion.Opacity.SetTarget(0, reduce);
                    motion.Slide.SetTarget(SlideStart(-slide, reduce), reduce);
                    continue;
                }
                motion.Visible = false;
                motion.Fresh = false;
                motion.Hiding = false;
                card.IsVisible = false;
                continue;
            }
            motion.Hiding = false;
            card.IsHitTestVisible = true;
            var appearing = !motion.Visible || !motion.Placed;
            if (appearing || !springs)
            {
                motion.SnapRect(rect);
                if (motion.Fresh && springs && !narrow)
                {
                    motion.Opacity.Snap(0);
                    motion.Scale.Snap(reduce ? 1 : OpenScale);
                    motion.Opacity.SetTarget(1, reduce);
                    motion.Scale.SetTarget(1, reduce);
                }
                else if (narrow && appearing && springs)
                {
                    // The surface a tab chose: it fades in and slides in from the side the tab lies on.
                    motion.Opacity.Snap(0);
                    motion.Scale.Snap(1);
                    motion.Slide.Snap(SlideStart(slide, reduce));
                    motion.Opacity.SetTarget(1, reduce);
                    motion.Slide.SetTarget(0, reduce);
                }
                else if (!springs || appearing)
                {
                    motion.Opacity.Snap(1);
                    motion.Scale.Snap(1);
                    motion.Slide.Snap(0);
                }
            }
            else
            {
                motion.SetRect(rect, reduce);
                motion.Opacity.SetTarget(1, reduce);
                motion.Scale.SetTarget(1, reduce);
                motion.Slide.SetTarget(0, reduce);
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

        RetargetStrip(result, narrow, springs, reduce);
        narrowMode = narrow;
        narrowShown = surface;
        if (before is { } previous && surface is { } now && previous != now) HandOffFocus(previous, now);

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
                FinishHidden(size.Width);
                foreach (var motion in cards.Values) motion.Lifted = motion.Card.IsExpandedLook = front == motion.Card.Kind;
                UpdateHeld();
                PostRemoval();
            }
        }
    }

    // The pane drawn over the others: the expanded one (in tiles; narrow mode has surfaces, not a pane over the rest).
    private static PaneKind? FrontOf(PaneLayoutResult result) => result.Mode == PaneLayoutMode.Tiles ? result.Expanded : null;

    // Which side a narrow surface comes from: toward a later tab it comes from the end of the strip, toward an earlier one from
    // the start. From the tiles the chat is the first tab; a surface with no earlier place (the chat, or one just closed) comes
    // from the side its own tab lies on.
    private int SlideDirection(PaneLayoutResult result, PaneKind next)
    {
        var tabs = result.NarrowTabs;
        var to = tabs.IndexOf(next);
        var origin = narrowMode ? (narrowShown is { } shown ? tabs.IndexOf(shown) : -1) : 0;
        if (origin < 0 || origin == to) return to <= 0 ? -1 : 1;
        return to > origin ? 1 : -1;
    }

    // The slide a surface starts from or leaves to: 8 DIP toward `direction`, none under Reduce Motion.
    private static double SlideStart(int direction, bool reduce) => reduce ? 0 : direction * NarrowSlide;

    // The chat column: below the title band in narrow mode (its chrome row under the strip), at the top otherwise; in narrow mode
    // a surface like the panes, faded and slid in when its tab is chosen and out when another is.
    private void RetargetChat(bool narrow, bool wanted, int slide, bool springs, bool reduce, double width)
    {
        var top = narrow ? WindowChrome.TitlebarHeight : 0;
        if (wanted)
        {
            var wasHidden = !chat.IsVisible;
            var appears = wasHidden || Math.Abs(chatTop - top) > 0.01 || chatHiding;
            chatTop = top;
            chatHiding = false;
            chat.IsHitTestVisible = true;
            chat.IsVisible = true;
            if (!springs)
            {
                chatOpacity.Snap(1);
                chatSlide.Snap(0);
                return;
            }
            if (appears && (wasHidden || (!chatOpacity.IsMoving && chatOpacity.Value >= 1)))
            {
                // A fresh appearance (a tab chose it, or it moved a title band down or up): from nothing, from the side its tab lies on.
                chatOpacity.Snap(0);
                chatSlide.Snap(narrow && wasHidden ? SlideStart(slide, reduce) : 0);
            }
            chatOpacity.SetTarget(1, reduce);
            chatSlide.SetTarget(0, reduce);
            return;
        }
        if (!chat.IsVisible || (chatHiding && springs)) return;
        if (springs)
        {
            chatHiding = true;
            chat.IsHitTestVisible = false;
            chatOpacity.SetTarget(0, reduce);
            chatSlide.SetTarget(SlideStart(-slide, reduce), reduce);
            return;
        }
        chatTop = top;
        HideChat(width);
    }

    private void HideChat(double width)
    {
        chatHiding = false;
        chat.IsVisible = false;
        chatOpacity.Snap(0);
        chatSlide.Snap(0);
        chatWidth.Snap(width);
        chat.FinishNow();
    }

    // The tab strip fades in with narrow mode and out with it (opacity only); it keeps its tabs while it fades.
    private void RetargetStrip(PaneLayoutResult result, bool narrow, bool springs, bool reduce)
    {
        if (narrow) strip.SetTabs(result.NarrowTabs, result.NarrowSelection);
        var target = narrow ? 1.0 : 0.0;
        if (springs) stripOpacity.SetTarget(target, reduce);
        else stripOpacity.Snap(target);
        strip.IsHitTestVisible = narrow;
        strip.IsVisible = narrow || stripOpacity.IsMoving || stripOpacity.Value > 0.001;
    }

    // Surfaces that have faded out leave the layout (and the accessibility tree); the chat takes the full width it will have.
    private void FinishHidden(double width)
    {
        if (chatHiding && !chatOpacity.IsMoving) HideChat(width);
        foreach (var motion in cards.Values)
        {
            if (!motion.Hiding || motion.IsMoving) continue;
            motion.Hiding = false;
            motion.Visible = false;
            motion.Card.IsVisible = false;
            motion.Opacity.Snap(0); // a surface that comes back is drawn afresh (the appearing branch of Retarget)
            motion.Slide.Snap(0);
        }
        if (!narrowMode && !stripOpacity.IsMoving && stripOpacity.Value <= 0.001) strip.IsVisible = false;
    }

    // Focus that was in the surface a tab replaced goes to the new one, so the keyboard is never left in a hidden surface.
    private void HandOffFocus(PaneKind leaving, PaneKind entering)
    {
        var had = leaving == PaneKind.Chat ? chat.IsKeyboardFocusWithin : cards.TryGetValue(leaving, out var motion) && motion.Card.IsKeyboardFocusWithin;
        if (!had) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (entering == PaneKind.Chat)
            {
                MoveFocusToChat();
                return;
            }
            var body = cards.TryGetValue(entering, out var target) ? target.Card.Content.Body : null;
            var focusable = body?.GetVisualDescendants().OfType<InputElement>().Prepend(body).FirstOrDefault(element => element.Focusable && element.IsEffectivelyVisible && element.IsEffectivelyEnabled);
            if (focusable is not null) focusable.Focus();
            else strip.TabButtons.FirstOrDefault(tab => tab.IsTabStop)?.Focus();
        });
    }

    private bool AnyMoving => chatWidth.IsMoving || chatOpacity.IsMoving || chatSlide.IsMoving || stripOpacity.IsMoving || cards.Values.Any(motion => motion.IsMoving);

    // The chat chrome row's trailing end keeps clear of the caption buttons only where the chat column runs under them. In
    // narrow mode the column starts below the title band, so nothing runs under them and the row keeps only its own padding.
    private void SetCaptionReserve(double chatRight, bool animates)
    {
        if (solved is { Mode: PaneLayoutMode.Narrow })
        {
            ChatView.SetChromeTrailingReserveAnimates(chat, false);
            ChatView.SetChromeTrailingReserve(chat, 0);
            return;
        }
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
            motion.Card.Scale = Math.Clamp(motion.Scale.Value, 0.9, 1);
            motion.Card.Slide = Math.Clamp(motion.Slide.Value, -NarrowSlide, NarrowSlide);
            motion.Card.ZIndex = motion.Lifted ? 10 : motion.Leaving ? 1 : 0;
        }
        chat.Opacity = Math.Clamp(chatOpacity.Value, 0, 1);
        chatTranslate.X = Math.Clamp(chatSlide.Value, -NarrowSlide, NarrowSlide);
        strip.Opacity = Math.Clamp(stripOpacity.Value, 0, 1);
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
        any |= chatOpacity.Step(dt);
        any |= chatSlide.Step(dt);
        any |= stripOpacity.Step(dt);
        foreach (var motion in cards.Values) any |= motion.Step(dt);
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
        FinishHidden(solvedSize.Width);
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
        chat.Measure(new Size(ChatShownWidth, Math.Max(0, size.Height - chatTop)));
        if (strip.IsVisible) strip.Measure(StripBounds.Size);
        foreach (var motion in cards.Values)
            if (motion.Card.IsVisible) motion.Card.Measure(Shown(motion).Size);
        foreach (var (key, splitter) in splitters)
            if (splitter.IsVisible && GutterRect(key) is { } rect) splitter.Measure(rect.Size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != solvedSize) Retarget(finalSize, false);
        chat.Arrange(new Rect(0, chatTop, ChatShownWidth, Math.Max(0, finalSize.Height - chatTop)));
        if (strip.IsVisible)
        {
            strip.Measure(StripBounds.Size);
            strip.Arrange(StripBounds);
        }
        foreach (var motion in cards.Values)
        {
            if (!motion.Card.IsVisible) continue;
            var rect = Shown(motion);
            motion.Card.Measure(rect.Size);
            motion.Card.Arrange(rect);
        }
        foreach (var (key, splitter) in splitters)
            if (splitter.IsVisible && GutterRect(key) is { } rect) splitter.Arrange(rect);
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

        /// <summary>The narrow slide (DIP): 8 toward the side a tab came from or goes to, 0 at rest.</summary>
        public SplitterSpring Slide { get; } = new(0, epsilon: 0.02);

        /// <summary>Fading out because another narrow tab was chosen; it leaves the layout once it has.</summary>
        public bool Hiding { get; set; }

        /// <summary>Opened since the last layout: it fades in.</summary>
        public bool Fresh { get; set; }

        /// <summary>Has had a rectangle.</summary>
        public bool Placed { get; set; }

        /// <summary>Drawn in the last layout.</summary>
        public bool Visible { get; set; }

        public bool Leaving { get; set; }

        /// <summary>Drawn over the others (expanded, or moving back from it).</summary>
        public bool Lifted { get; set; }

        public bool IsMoving => X.IsMoving || Y.IsMoving || W.IsMoving || H.IsMoving || Opacity.IsMoving || Scale.IsMoving || Slide.IsMoving;

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
            Slide.Settle();
        }

        public bool Step(double seconds)
        {
            var any = X.Step(seconds);
            any |= Y.Step(seconds);
            any |= W.Step(seconds);
            any |= H.Step(seconds);
            any |= Opacity.Step(seconds);
            any |= Scale.Step(seconds);
            any |= Slide.Step(seconds);
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
