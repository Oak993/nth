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
using Marrow.UI.Web;

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
// Narrow mode (web surfaces W3c, DESIGN 3.8, ED-NEW-web-w3c-2): when the solver says the window is too narrow for the chat and
// one pane, the workspace shows one surface at a time under a glass tab strip in the title band (Chat, then the open panes in
// order). A switch cross-fades with an 8 DIP slide on the same springs, a pane that opens is chosen and one that closes gives
// way to the chat; the surface not chosen is out of the layout and out of the accessibility tree once it has faded. The chat
// column then starts below the title band (its own chrome row under the strip), and every pane fills the solver's area.
//
// Web surfaces W3b adds (DESIGN 3.5 collapse, 3.6 re-dock, 3.7 keyboard, 4.2 limits; ED-NEW-web-w3b-1 to 4):
// - a gutter dragged past the collapsible side's minimum by 56 DIP folds that side into a 22 DIP rail (PaneRailStrip); a collapsible
//   limit gives a third of the overshoot, at most 24 DIP, before it folds (the rubber band, a render-time offset of the cards
//   beside the divider); a drag back out, a rail icon, the chevron, a double-click on the rail or Space on the splitter restores
//   the side to its size before the collapse. The folded panes fade out in place while the chat takes their room, and fade back
//   in at their restored places;
// - a header dragged 4 DIP lifts its pane (scale 1.02, the deeper shadow, a render-only translation); outlines show the drops the
//   solver can express (PaneRedock): another cell's body swaps, another cell's header joins as a tab, a tab of a group can
//   leave it; a release over a target springs the pane from where it is to its new place, a release over nothing or Esc
//   springs it home;
// - the keyboard: the pane commands of Shortcuts.cs on the window's key-down (the scope rule of ShortcutScopes applies: a web
//   view or the terminal gets every key but the reserved ones), F6 and Shift+F6 cycling the chat, the panes, the splitters
//   and the rails, Ctrl+Alt+Arrow and Ctrl+Alt+T moving and joining the focused pane.

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
    private readonly Dictionary<string, RailMotion> rails = [];
    private readonly Dictionary<string, Splitter> splitters = [];
    private readonly HashSet<PaneKind> unavailable = [];
    private readonly SplitterSpring chatWidth = new(0);
    private readonly PaneDropLayer dropLayer = new() { ZIndex = 30 };
    private readonly List<DropSlot> dropSlots = [];
    private readonly List<IDisposable> webLeases = [];
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
    private bool refreshPosted;
    private WindowLiveResize? resizeOwner;
    private Redock? redock;
    private bool redocking;
    private string? rubberKey;
    private double rubberGive;
    private int gestureSerial;
    private bool gestureOpen;
    private TopLevel? keyTop;
    private bool narrowMode;
    private bool narrowReserve;
    private bool chatHiding;
    private double chatTop;
    private PaneKind? narrowShown;

    public PaneWorkspace(Control chatColumn, Func<PaneKind, PaneContent?> factory)
    {
        ArgumentNullException.ThrowIfNull(chatColumn);
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        chat.Content = chatColumn;
        chat.RenderTransform = chatTranslate;
        strip.TabChosen += Select;
        Children.Add(strip); // first: the keyboard reaches the tabs before the surface they choose
        Children.Add(chat);
        foreach (var key in PaneGutterKeys.All)
        {
            var axis = key is PaneGutterKeys.Row1 or PaneGutterKeys.Row2 ? SplitterOrientation.Horizontal : SplitterOrientation.Vertical;
            var splitter = new Splitter(axis, new GutterTarget(this, key))
            {
                ShowsReadout = true,
                // The chat gutter folds every pane, the Side chat gutter folds Side chat; both fold the trailing side.
                CollapseSide = key is PaneGutterKeys.Chat or PaneGutterKeys.Side ? SplitterSide.Trailing : SplitterSide.None,
                IsVisible = false,
                ZIndex = 20,
            };
            splitters[key] = splitter;
            Children.Add(splitter);
        }
        Children.Add(dropLayer);
        ClipToBounds = false;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        DetachedFromVisualTree += (_, _) =>
        {
            CancelRedock();
            SettleAll();
            dragging = false;
            reflowing = false;
            UpdateResizing();
            UpdateHeld();
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

    /// <summary>A pane is lifted by its header (or still springing home).</summary>
    public bool IsRedocking => redock is not null;

    /// <summary>The pane lifted by its header now, or null.</summary>
    public PaneKind? RedockKind => redock?.Kind;

    /// <summary>The outlines of the drops open to the lifted pane (empty when none is lifted).</summary>
    public IReadOnlyList<PaneDropVisual> DropVisuals => [.. dropSlots.Where(slot => !slot.Fading).Select(slot => slot.Visual)];

    /// <summary>The outline under the pointer now, or null.</summary>
    public PaneDropVisual? HotDrop => redock?.Hot?.Visual;

    /// <summary>Moves keyboard focus to the chat (closing a focused pane; the working chip). Defaults to the composer.</summary>
    public Action? FocusChat { get; set; }

    /// <summary>
    /// Whether a pane kind can be opened by a chord in this build (a pane unit registered its content). Null: every kind can.
    /// A chord for a kind that cannot be drawn is not taken, so it reaches whatever else holds it.
    /// </summary>
    public Func<PaneKind, bool>? IsKindAvailable { get; set; }

    /// <summary>
    /// F6 asks this first (DESIGN 10.1): true when an open prompt took the focus, so F6 goes to the newest prompt before the
    /// normal pane cycle. The prompt units (Visus W8b) set it.
    /// </summary>
    public Func<bool>? PromptFirst { get; set; }

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

    /// <summary>The rail of a collapsed side (<see cref="PaneGutterKeys.Chat"/> or <see cref="PaneGutterKeys.Side"/>), or null.</summary>
    public PaneRailStrip? RailOf(string key) => rails.TryGetValue(key, out var motion) && motion.Wanted ? motion.Rail : null;

    /// <summary>The rail's rectangle on screen (in the workspace, under the title band), or null.</summary>
    public Rect? RailBounds(string key) => rails.TryGetValue(key, out var motion) && motion.Wanted ? motion.Bounds : null;

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

    /// <summary>The pane holding keyboard focus (focus inside its card), or null when the chat or nothing in the workspace has it.</summary>
    public PaneKind? FocusedPane => cards.Where(pair => !pair.Value.Leaving && pair.Value.Card.IsVisible && pair.Value.Card.IsKeyboardFocusWithin)
        .Select(pair => (PaneKind?)pair.Key).FirstOrDefault();

    /// <summary>
    /// Draws <paramref name="next"/>. Animated, the rectangles spring; otherwise they snap. Raises nothing: this is how
    /// the owner (the thread's runtime) tells the workspace its state.
    /// </summary>
    public void SetState(PaneWorkspaceState next, bool animate = true) => Apply(next, animate);

    /// <summary>A thread switch: new contents for every pane, the state drawn at once.</summary>
    public void Reload(Func<PaneKind, PaneContent?> contents, PaneWorkspaceState next)
    {
        factory = contents ?? throw new ArgumentNullException(nameof(contents));
        CancelRedock();
        SettleAll();
        foreach (var motion in cards.Values) RemoveCard(motion);
        cards.Clear();
        foreach (var motion in rails.Values) Children.Remove(motion.Rail);
        rails.Clear();
        unavailable.Clear();
        rubberKey = null;
        rubberGive = 0;
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

    /// <summary>Opens a closed pane or closes an open one; false when the kind cannot be drawn in this build (nothing changes).</summary>
    public bool Toggle(PaneKind kind)
    {
        if (kind == PaneKind.Chat) return false;
        if (state.IsOpen(kind))
        {
            Close(kind);
            return true;
        }
        if (IsKindAvailable is { } available && !available(kind)) return false;
        Open(kind);
        return true;
    }

    public void ToggleExpanded(PaneKind kind) => Commit(state.ToggleExpanded(kind));

    public void RestoreExpanded() => Commit(state.RestoreExpanded());

    public void Select(PaneKind kind) => Commit(kind == PaneKind.Chat ? state.Open(PaneKind.Chat) : state.Select(kind));

    /// <summary>Collapses a collapsible side (<see cref="PaneGutterKeys.Chat"/> folds every pane, <see cref="PaneGutterKeys.Side"/> Side chat) to its rail.</summary>
    public void Collapse(string key) => Commit(PaneLayout.Collapse(state, SolverWidth, SolverHeight, key));

    /// <summary>Restores a collapsed side to its size before the collapse (or the default when it was at its minimum).</summary>
    public void Restore(string key) => Commit(state.Restore(key));

    /// <summary>Ctrl+Alt+Left or Up: the pane's slot swaps with the one before it.</summary>
    public void MoveEarlier(PaneKind pane) => Commit(state.MoveEarlier(pane));

    /// <summary>Ctrl+Alt+Right or Down: the pane's slot swaps with the one after it.</summary>
    public void MoveLater(PaneKind pane) => Commit(state.MoveLater(pane));

    /// <summary>Ctrl+Alt+T: the pane joins the slot before it as a tab, or leaves its group.</summary>
    public void ToggleTabGroup(PaneKind pane) => Commit(state.ToggleTabGroup(pane));

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
        foreach (var motion in rails.Values) motion.Opacity.Settle();
        ghost?.Settle();
        foreach (var slot in dropSlots)
        {
            slot.Appear.Settle();
            slot.HotSpring.Settle();
        }
        ApplyLooks();
        InvalidateMeasure();
        if (moving) FinishMotion();
    }

    private bool Commit(PaneWorkspaceState next, bool animate = true)
    {
        if (!Apply(next, animate)) return false;
        StateChanged?.Invoke(state);
        return true;
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
                existing.Card.IsHitTestVisible = !existing.Folding;
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
            card.Header.DragLifted += e => BeginRedock(kind, e);
            card.Header.DragMoved += e => MoveRedock(kind, e);
            card.Header.DragDropped += e => EndRedock(kind, drop: true);
            card.Header.DragCanceled += () => EndRedock(kind, drop: false);
            cards[kind] = motion;
            Children.Insert(2, card);
        }
        foreach (var (kind, motion) in cards.ToList())
        {
            if (open.Contains(kind) || motion.Leaving) continue;
            if (redock?.Kind == kind) CancelRedock();
            motion.Leaving = true;
            motion.Folding = false;
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
        motion.Card.Header.CancelDrag();
        motion.Card.Detach();
        Children.Remove(motion.Card);
    }

    // The state the layout draws: panes this build cannot draw are left out, so no empty card ever shows.
    private PaneWorkspaceState Drawn() => Drawn(state);

    private PaneWorkspaceState Drawn(PaneWorkspaceState of)
    {
        var s = of;
        foreach (var kind in OpenKinds(of).ToList())
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
        var give = RubberOffset(result);

        // Narrow mode (DESIGN 3.8): one surface at a time under the tab strip. Every surface fills the solver's area, the strip
        // standing in the title band above it, and the chat column starts below the title band.
        var tiles = result.Mode == PaneLayoutMode.Tiles;
        var narrow = !tiles;
        var surface = narrow ? result.NarrowSelection : (PaneKind?)null;
        var before = narrowMode ? narrowShown : (PaneKind?)null;
        var slide = narrow ? SlideDirection(result, surface!.Value) : 0;
        var chatWanted = !narrow || surface == PaneKind.Chat;
        // The chat column: the full area alone, its tile otherwise (and the rest of the window beside a rail).
        var panes = tiles && (result.Shown.Any(kind => kind != PaneKind.Chat) || result.Rails.Count > 0);
        var chatTarget = (panes ? result.Chat.Width : size.Width) + (rubberKey == PaneGutterKeys.Chat ? give : 0);
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
        // The chat chrome row's trailing clearance follows the mode once the chat is the surface shown (a chat fading out keeps its own).
        if (reserveSet) narrowReserve = narrow;
        else if (chatWanted && narrow != narrowReserve)
        {
            SetCaptionReserve(panes ? result.Chat.Right : size.Width, animates: false);
            narrowReserve = narrow;
        }
        RetargetChat(narrow, chatWanted, slide, springs, reduce, size.Width);

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
            if (front == kind || (narrow && surface == kind)) target = area;
            else if (tiles && result.IsShown(kind) && result.BoundsOf(kind) is { } bounds)
                target = Nudge(result, bounds, give, top);
            var folded = tiles && target is null && result.Rails.Any(rail => rail.Panes.Contains(kind));
            header.IsExpanded = tiles && result.Expanded == kind;
            header.CanExpand = tiles;
            header.ShowsWorking = chatWorking && tiles && result.Expanded == kind;
            header.CanDrag = tiles && result.Expanded is null && PaneRedock.CanLift(drawn, result, kind);
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
                if (folded && motion.Visible && springs)
                {
                    // A folded side: the pane fades out where it is while the chat takes its room (one motion).
                    motion.Folding = true;
                    motion.Folded = true;
                    card.IsHitTestVisible = false;
                    motion.Opacity.SetTarget(0, reduce);
                    motion.Scale.SetTarget(reduce ? 1 : OpenScale, reduce);
                    continue;
                }
                if (folded && motion.Folding) continue;
                motion.Folded = folded;
                motion.Folding = false;
                motion.Visible = false;
                motion.Fresh = false;
                motion.Hiding = false;
                card.IsVisible = false;
                card.IsHitTestVisible = true;
                continue;
            }
            motion.Hiding = false;
            var appearing = !motion.Visible || !motion.Placed;
            var fadesIn = motion.Fresh || (motion.Folded && appearing);
            motion.Folding = false;
            card.IsHitTestVisible = true;
            if (appearing || !springs)
            {
                motion.SnapRect(rect);
                if (fadesIn && springs && !narrow)
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
                motion.Scale.SetTarget(redock is { Lifted: true } held && held.Kind == kind && !reduce ? PaneRedock.LiftScale : 1, reduce);
                motion.Slide.SetTarget(0, reduce);
            }
            motion.Fresh = false;
            motion.Folded = false;
            motion.Placed = true;
            motion.Visible = true;
            card.IsVisible = true;
        }

        SyncRails(result, top, springs, reduce);
        if (redock is { } lifted && !(lifted.FromDrawer ? PaneRedock.CanLiftFromDrawer(drawn, result) : PaneRedock.CanLift(drawn, result, lifted.Kind))) Dispatcher.UIThread.Post(CancelRedock);

        var showsGutters = tiles && result.Expanded is null;
        foreach (var (key, splitter) in splitters)
        {
            var gutter = showsGutters ? result.Gutter(key) : null;
            splitter.IsVisible = gutter is not null;
            if (gutter is null) continue;
            splitter.Label = gutter.Label;
        }
        PostSplitterRefresh();

        RetargetStrip(result, narrow, springs, reduce);
        narrowMode = narrow;
        narrowShown = surface;
        // A tab switch is a gesture over web panes like a lift is: their pages show a snapshot before anything fades over them
        // (DESIGN 2.4, 2.5, 3.8), and the springs wait for the captures. No web pane, or a gesture already open: they start at once.
        var ready = Task.CompletedTask;
        if (before is { } previous && surface is { } now && previous != now)
        {
            HandOffFocus(previous, now);
            if (springs && !gestureOpen) ready = BeginWebGesture();
        }

        ApplyLooks();
        if (springs && AnyMoving)
        {
            moving = true;
            UpdateHeld();
            if (ready.IsCompleted) RequestFrame();
            else _ = ready.ContinueWith(_ => Dispatcher.UIThread.Post(RequestFrame), TaskScheduler.Default);
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
                HideFolded();
                FinishHidden(size.Width);
                UpdateHeld();
                PostRemoval();
            }
        }
    }

    // The collapse rails (3.5): one per folded side, a 22 DIP strip at the window's edge that fades in as the panes fade out.
    private void SyncRails(PaneLayoutResult result, double top, bool springs, bool reduce)
    {
        foreach (var motion in rails.Values) motion.Wanted = false;
        foreach (var rail in result.Rails)
        {
            if (!rails.TryGetValue(rail.Key, out var motion))
            {
                var control = new PaneRailStrip(rail.Key) { IsVisible = false, ZIndex = 5 };
                var key = rail.Key;
                control.RestoreRequested += kind => RestoreFromRail(key, kind);
                motion = new RailMotion(control);
                rails[rail.Key] = motion;
                Children.Insert(2, control);
            }
            motion.Wanted = true;
            motion.Rail.SetPanes([.. rail.Panes]);
            motion.Bounds = new Rect(rail.Bounds.X, rail.Bounds.Y + top, rail.Bounds.Width, rail.Bounds.Height);
            motion.Rail.IsHitTestVisible = true;
            if (!motion.Rail.IsVisible && springs)
            {
                motion.Opacity.Snap(0);
                motion.Opacity.SetTarget(1, reduce);
            }
            else if (springs) motion.Opacity.SetTarget(1, reduce);
            else motion.Opacity.Snap(1);
            motion.Rail.IsVisible = true;
        }
        foreach (var (key, motion) in rails.ToList())
        {
            if (motion.Wanted) continue;
            motion.Rail.IsHitTestVisible = false;
            if (!springs || !motion.Rail.IsVisible)
            {
                RemoveRail(key, motion);
                continue;
            }
            motion.Opacity.SetTarget(0, reduce);
        }
    }

    private void RemoveRail(string key, RailMotion motion)
    {
        rails.Remove(key);
        Children.Remove(motion.Rail);
    }

    // A rail icon brings its side back to the size it had before the fold and goes to that pane; the chevron and a double-click
    // bring it back without choosing one.
    private void RestoreFromRail(string key, PaneKind? kind)
    {
        var restored = state.Restore(key);
        if (kind is { } pane && restored.IsOpen(pane)) restored = restored.Select(pane);
        var target = kind ?? (key == PaneGutterKeys.Side ? PaneKind.SideChat : state.GridPanes.FirstOrDefault());
        if (!Commit(restored)) return;
        Dispatcher.UIThread.Post(() => FocusPane(target), DispatcherPriority.Background);
    }

    // The folded panes' cards fade out in place; once their springs have landed they are hidden for good.
    private void HideFolded()
    {
        foreach (var motion in cards.Values)
        {
            if (!motion.Folding || motion.IsMoving) continue;
            motion.Folding = false;
            motion.Visible = false;
            motion.Card.IsVisible = false;
            motion.Card.IsHitTestVisible = true;
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

    private bool AnyMoving => chatWidth.IsMoving || chatOpacity.IsMoving || chatSlide.IsMoving || stripOpacity.IsMoving || cards.Values.Any(motion => motion.IsMoving)
        || rails.Values.Any(motion => motion.Opacity.IsMoving) || dropSlots.Any(slot => slot.Appear.IsMoving || slot.HotSpring.IsMoving)
        || ghost?.IsMoving == true;

    // The rubber band (4.2): how far, in layout DIP along x, the dragged gutter's divider stands past where its limit holds it.
    private double RubberOffset(PaneLayoutResult result)
    {
        if (rubberKey is null || rubberGive == 0 || result.Gutter(rubberKey) is not { Axis: PaneGutterAxis.X } gutter) return 0;
        return rubberGive / gutter.ValueSign;
    }

    // The rubber band moves the edges that stand at the divider: the cells on its leading side grow by `give` (or shrink),
    // those on its trailing side start `give` later. Cells elsewhere keep their rectangles.
    private Rect Nudge(PaneLayoutResult result, PaneRect bounds, double give, double top)
    {
        var rect = new Rect(bounds.X, bounds.Y + top, bounds.Width, bounds.Height);
        if (give == 0 || rubberKey is null || result.Gutter(rubberKey) is not { Axis: PaneGutterAxis.X, SpanLength: > 0 } gutter) return rect;
        if (bounds.Y >= gutter.SpanStart + gutter.SpanLength - 0.5 || bounds.Bottom <= gutter.SpanStart + 0.5) return rect;
        if (Math.Abs(bounds.Right - (gutter.Position - PaneKinds.Gap / 2)) < 0.5) return rect.WithWidth(Math.Max(0, rect.Width + give));
        if (Math.Abs(bounds.X - (gutter.Position + PaneKinds.Gap / 2)) < 0.5)
            return new Rect(rect.X + give, rect.Y, Math.Max(0, rect.Width - give), rect.Height);
        return rect;
    }

    private void SetRubber(string? key, double give)
    {
        if (rubberKey == key && rubberGive == give) return;
        rubberKey = key;
        rubberGive = give;
        dirty = true;
        InvalidateMeasure();
    }

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
        var held = moving || dragging || redocking;
        foreach (var motion in cards.Values) motion.Card.Header.IsHeld = held;
        foreach (var splitter in splitters.Values) splitter.IsHitTestVisible = !redocking;
        if (!held && gestureOpen) EndWebGesture();
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
            motion.Card.Scale = Math.Clamp(motion.Scale.Value, 0.9, 1.05);
            motion.Card.Slide = Math.Clamp(motion.Slide.Value, -NarrowSlide, NarrowSlide);
            motion.Card.Offset = new Vector(motion.DragX.Value, motion.DragY.Value);
            motion.Card.ZIndex = motion.Held ? 40 : motion.Lifted ? 10 : motion.Leaving ? 1 : 0;
        }
        foreach (var motion in rails.Values) motion.Rail.Opacity = Math.Clamp(motion.Opacity.Value, 0, 1);
        chat.Opacity = Math.Clamp(chatOpacity.Value, 0, 1);
        chatTranslate.X = Math.Clamp(chatSlide.Value, -NarrowSlide, NarrowSlide);
        strip.Opacity = Math.Clamp(stripOpacity.Value, 0, 1);
        foreach (var slot in dropSlots)
        {
            slot.Visual.Opacity = Math.Clamp(slot.Appear.Value, 0, 1);
            slot.Visual.Hot = slot.HotSpring.Value;
        }
        ApplyGhostLooks();
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
        foreach (var motion in rails.Values) any |= motion.Opacity.Step(dt);
        if (ghost is not null) any |= ghost.Step(dt);
        foreach (var slot in dropSlots)
        {
            any |= slot.Appear.Step(dt);
            any |= slot.HotSpring.Step(dt);
        }
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

    // Everything has landed: closed cards leave, folded ones are hidden, headers refit, the chat column lays out once at its new width.
    private void FinishMotion()
    {
        moving = false;
        var front = solved is { } result ? FrontOf(result) : null;
        foreach (var motion in cards.Values)
        {
            motion.Lifted = !motion.Leaving && front == motion.Card.Kind;
            motion.Card.IsExpandedLook = motion.Lifted;
            if (redock?.Kind == motion.Card.Kind) continue;
            motion.Held = false;
            motion.Card.IsLiftedLook = false;
        }
        HideFolded();
        FinishHidden(solvedSize.Width);
        FinishGhost();
        ApplyLooks();
        UpdateHeld();
        PostRemoval();
        var chatRight = solved is { Mode: PaneLayoutMode.Tiles } tiles && (tiles.Shown.Any(kind => kind != PaneKind.Chat) || tiles.Rails.Count > 0) ? tiles.Chat.Right : solvedSize.Width;
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
            foreach (var (key, motion) in rails.ToList())
            {
                if (motion.Wanted || motion.Opacity.IsMoving) continue;
                RemoveRail(key, motion);
            }
            foreach (var slot in dropSlots.Where(slot => slot.Fading && !slot.Appear.IsMoving).ToList())
            {
                dropSlots.Remove(slot);
                dropLayer.Children.Remove(slot.Visual);
            }
        });
    }

    // The splitters' readings (their value and its words) follow the layout that was just solved; a collapse by key or a state
    // change from outside has not reached them until the layout has run, so they are told once, after it.
    private void PostSplitterRefresh()
    {
        if (refreshPosted) return;
        refreshPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            refreshPosted = false;
            foreach (var splitter in splitters.Values)
                if (splitter.IsVisible) splitter.Refresh();
        }, DispatcherPriority.Render);
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

    private void FocusPane(PaneKind kind)
    {
        if (cards.TryGetValue(kind, out var motion) && motion.Card.IsVisible && !motion.Leaving) motion.Card.Focus(NavigationMethod.Tab);
        else MoveFocusToChat();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc in an expanded pane restores it (3.4) - from nth's own controls: a page or a shell that needs Esc keeps it (3.7).
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
        if (state.Expanded is not { } expanded || !cards.TryGetValue(expanded, out var motion) || !motion.Card.IsKeyboardFocusWithin) return;
        if (keyTop is not null && ShortcutScopeHost.ScopeOfFocus(keyTop) == ShortcutScope.Content) return;
        RestoreExpanded();
        e.Handled = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        keyTop = TopLevel.GetTopLevel(this);
        keyTop?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        keyTop?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        keyTop = null;
        base.OnDetachedFromVisualTree(e);
    }

    // The pane commands of Shortcuts.cs, anywhere in the window (DESIGN 3.7): the chord is the user's binding; in a content
    // scope only the reserved chords are taken, and every other key reaches the page or the shell.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || keyTop is null || PaneShortcuts.ChordOf(e) is not { } chord) return;
        var store = ShortcutStore.Shared.Current;
        if (ShortcutScopes.Resolve(store, chord) is not { } command || !PaneShortcuts.IsPaneCommand(command)) return;
        if (!ShortcutScopes.Takes(ShortcutScopeHost.ScopeOfFocus(keyTop), store, chord)) return;
        if (Perform(command)) e.Handled = true;
    }

    /// <summary>
    /// Runs a pane command (the chord's work, 3.7). <paramref name="origin"/> is the pane the key was pressed in when focus is
    /// not tracked by Avalonia (a web view reports its pane); otherwise the focused pane. True when the command was taken.
    /// </summary>
    public bool Perform(AppShortcut command, PaneKind? origin = null)
    {
        switch (command)
        {
            case AppShortcut.ToggleBrowser: return Toggle(PaneKind.Browser);
            case AppShortcut.ToggleSideChat: return Toggle(PaneKind.SideChat);
            case AppShortcut.ToggleArtifacts: return Toggle(PaneKind.Artifacts);
            case AppShortcut.ToggleBackgroundTasks: return Toggle(PaneKind.BackgroundTasks);
            case AppShortcut.NextPane: return CycleFocus(forward: true);
            case AppShortcut.PreviousPane: return CycleFocus(forward: false);
        }
        var pane = origin ?? FocusedPane;
        switch (command)
        {
            case AppShortcut.ClosePane:
                if (pane is not { } closing || closing == PaneKind.Chat) return false;
                Close(closing);
                return true;
            case AppShortcut.ExpandPane:
                if ((pane ?? state.Expanded) is not { } expanding || expanding == PaneKind.Chat) return false;
                ToggleExpanded(expanding); // a reflow does not move keyboard focus (3.3): a page keeps its caret
                return true;
            case AppShortcut.MovePaneEarlier:
            case AppShortcut.MovePaneLater:
                if (pane is not { } moved || state.SlotIndexOf(moved) < 0) return false;
                if (command == AppShortcut.MovePaneEarlier) MoveEarlier(moved);
                else MoveLater(moved);
                return true;
            case AppShortcut.JoinPaneTabs:
                if (pane is not { } joined || state.SlotIndexOf(joined) < 0) return false;
                ToggleTabGroup(joined);
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ F6

    /// <summary>
    /// The places F6 visits, in reading order (DESIGN 3.7): the chat, the open panes in their order, Side chat, then the
    /// splitters and the collapse rails. An expanded pane is the only stop (the chat runs behind it). In narrow mode the tab
    /// strip comes first and the one surface shown is the only other stop.
    /// </summary>
    public IReadOnlyList<Control> FocusStops() => [.. Stops().Select(stop => stop.Element)];

    private List<Stop> Stops()
    {
        var stops = new List<Stop>();
        if (solved is not { } result) return stops;
        Stop CardStop(CardMotion motion) => new(motion.Card, () => motion.Card.Focus(NavigationMethod.Tab));
        if (result.Expanded is { } expanded)
        {
            if (cards.TryGetValue(expanded, out var front) && front.Card.IsVisible) stops.Add(CardStop(front));
            return stops;
        }
        // Narrow mode (web W3c): the tab strip first, then the one surface shown; a chat that is not the surface is out of the layout.
        if (result.Mode == PaneLayoutMode.Narrow && strip.IsVisible)
            stops.Add(new Stop(strip, () => strip.TabButtons.FirstOrDefault(tab => tab.IsTabStop)?.Focus(NavigationMethod.Tab)));
        if (chat.IsVisible) stops.Add(new Stop(chat, MoveFocusToChat));
        var ordered = state.GridPanes.Append(PaneKind.SideChat);
        foreach (var kind in ordered)
            if (cards.TryGetValue(kind, out var motion) && !motion.Leaving && motion.Card.IsVisible && motion.Card.Opacity > 0) stops.Add(CardStop(motion));
        if (result.Mode != PaneLayoutMode.Tiles) return stops;
        foreach (var gutter in result.Gutters)
            if (splitters.TryGetValue(gutter.Key, out var splitter) && splitter.IsVisible) stops.Add(new Stop(splitter, () => splitter.Focus(NavigationMethod.Tab)));
        foreach (var rail in result.Rails)
            if (rails.TryGetValue(rail.Key, out var motion) && motion.Wanted)
            {
                var first = motion.Rail.IconButtons.FirstOrDefault() ?? motion.Rail.Chevron;
                stops.Add(new Stop(motion.Rail, () => first.Focus(NavigationMethod.Tab)));
            }
        return stops;
    }

    /// <summary>F6 (forward) or Shift+F6: focus goes to the next stop, wrapping; an open prompt takes it first (10.1). Always true.</summary>
    public bool CycleFocus(bool forward)
    {
        if (PromptFirst?.Invoke() == true) return true;
        var stops = Stops();
        if (stops.Count == 0) return false;
        var focused = keyTop?.FocusManager?.GetFocusedElement() as Visual;
        var at = focused is null ? -1 : stops.FindIndex(stop => ReferenceEquals(stop.Element, focused) || stop.Element.IsVisualAncestorOf(focused));
        var next = at < 0 ? (forward ? 0 : stops.Count - 1) : (at + (forward ? 1 : -1) + stops.Count) % stops.Count;
        stops[next].Focus();
        return true;
    }

    private sealed record Stop(Control Element, Action Focus);

    // ------------------------------------------------------------------ re-docking (DESIGN 3.6)

    private void BeginRedock(PaneKind kind, PointerEventArgs e)
    {
        cards.TryGetValue(kind, out var motion);
        if (redock is not null || dragging || solved is null || motion is null || motion.Leaving || !PaneRedock.CanLift(Drawn(), solved, kind))
        {
            motion?.Card.Header.CancelDrag();
            return;
        }
        SettleAll();
        var targets = PaneRedock.Targets(Drawn(), solved, kind, DrawerHeightNow); // web W4: the terminal's targets include its drawer
        var start = motion.Card.Header.TranslatePoint(motion.Card.Header.PressPoint, this) ?? e.GetPosition(this);
        var current = redock = new Redock(kind, motion, start) { Pointer = e.GetPosition(this) };
        redocking = true;
        var top = TopBand;
        foreach (var target in targets)
        {
            var visual = new PaneDropVisual(target);
            var slot = new DropSlot(target, new Rect(target.Bounds.X, target.Bounds.Y + top, target.Bounds.Width, target.Bounds.Height), visual);
            Canvas.SetLeft(visual, slot.Bounds.X);
            Canvas.SetTop(visual, slot.Bounds.Y);
            dropSlots.Add(slot);
            current.Slots.Add(slot);
            dropLayer.Children.Add(visual);
        }
        UpdateHeld();
        keyTop?.AddHandler(KeyDownEvent, OnRedockKey, RoutingStrategies.Tunnel);
        // The page under any pane is a snapshot before anything moves over it: the lift waits for the captures (2.4, 2.5).
        var ready = BeginWebGesture();
        if (ready.IsCompleted) Lift(current);
        else _ = ready.ContinueWith(_ => Dispatcher.UIThread.Post(() => Lift(current)), TaskScheduler.Default);
    }

    private void Lift(Redock current)
    {
        if (!ReferenceEquals(redock, current) || current.Lifted || current.Ended) return;
        current.Lifted = true;
        var reduce = ReduceMotion;
        var motion = current.Motion;
        motion.Held = true;
        motion.Card.IsLiftedLook = true;
        motion.Scale.SetTarget(reduce ? 1 : PaneRedock.LiftScale, reduce);
        foreach (var slot in current.Slots) slot.Appear.SetTarget(1, reduce);
        Follow(current);
        StartFrames();
    }

    private void MoveRedock(PaneKind kind, PointerEventArgs e)
    {
        if (redock is not { } current || current.Kind != kind || current.Ended) return;
        current.Pointer = e.GetPosition(this);
        if (current.Lifted) Follow(current);
    }

    // The lifted card follows the pointer one to one (a render transform, no layout) and the target under it lights up.
    private void Follow(Redock current)
    {
        var motion = current.Motion;
        motion.DragX.Snap(current.Pointer.X - current.Start.X);
        motion.DragY.Snap(current.Pointer.Y - current.Start.Y);
        var hit = PaneRedock.Hit([.. current.Slots.Select(slot => slot.Target)], current.Pointer.X, current.Pointer.Y - TopBand);
        var hot = hit is null ? null : current.Slots.First(slot => ReferenceEquals(slot.Target, hit));
        if (!ReferenceEquals(hot, current.Hot))
        {
            var reduce = ReduceMotion;
            current.Hot?.HotSpring.SetTarget(0, reduce);
            hot?.HotSpring.SetTarget(1, reduce);
            current.Hot = hot;
            StartFrames();
        }
        ApplyLooks();
    }

    private void OnRedockKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || redock is null) return;
        e.Handled = true;
        CancelRedock();
    }

    /// <summary>Esc, a closed pane or a thread switch: the lifted pane springs home and nothing changes.</summary>
    public void CancelRedock()
    {
        if (redock is not { } current) return;
        current.Motion.Card.Header.CancelDrag(); // raises DragCanceled, which ends the redock; without a lift it ends here
        if (ReferenceEquals(redock, current)) EndRedock(current.Kind, drop: false);
    }

    private void EndRedock(PaneKind kind, bool drop)
    {
        if (redock is not { } current || current.Kind != kind || current.Ended) return;
        current.Ended = true;
        redock = null;
        keyTop?.RemoveHandler(KeyDownEvent, OnRedockKey);
        if (current.FromDrawer)
        {
            EndDrawerRedock(current, drop);
            return;
        }
        var reduce = ReduceMotion;
        foreach (var slot in current.Slots)
        {
            slot.Fading = true;
            slot.Appear.SetTarget(0, reduce);
            slot.HotSpring.SetTarget(0, reduce);
        }
        var motion = current.Motion;
        var dropped = false;
        if (current.Lifted && drop && current.Hot is { } hot)
        {
            // The pane springs on from where it was let go: its slot starts at the dragged rectangle, the drag offset is spent.
            var layout = Shown(motion);
            var from = new Rect(layout.X + motion.DragX.Value, layout.Y + motion.DragY.Value, layout.Width, layout.Height);
            motion.SnapRect(from);
            motion.DragX.Snap(0);
            motion.DragY.Snap(0);
            redocking = false;
            dropped = Commit(hot.Target.Result);
            if (!dropped) motion.SnapRect(layout);
        }
        if (!dropped)
        {
            redocking = false;
            if (current.Lifted)
            {
                motion.DragX.SetTarget(0, reduce);
                motion.DragY.SetTarget(0, reduce);
                motion.Scale.SetTarget(1, reduce);
            }
            else motion.Held = false;
        }
        dirty = true;
        InvalidateMeasure();
        StartFrames();
        UpdateHeld();
        PostRemoval();
    }

    private void StartFrames()
    {
        moving = true;
        UpdateHeld();
        RequestFrame();
    }

    // DESIGN 2.4 and 2.5: before anything lifts or moves over a web rectangle, every web view in the workspace shows a fresh
    // snapshot of itself (the HWND draws above Avalonia, so a lifted card would otherwise slide under it); the views go live
    // again when the gesture ends. The task completes when the snapshots are on screen.
    private Task BeginWebGesture()
    {
        EndWebGesture();
        gestureOpen = true;
        var serial = ++gestureSerial;
        if (TopLevel.GetTopLevel(this) is not { } top) return Task.CompletedTask;
        var hosts = WebViewHost.HostsIn(top).Where(host => this.IsVisualAncestorOf(host)).ToList();
        return hosts.Count == 0 ? Task.CompletedTask : CollectLeases(serial, hosts);
    }

    private async Task CollectLeases(int serial, List<WebViewHost> hosts)
    {
        try
        {
            var got = await Task.WhenAll(hosts.Select(host => host.BeginGestureAsync()));
            if (serial != gestureSerial)
            {
                foreach (var lease in got) lease.Dispose();
                return;
            }
            webLeases.AddRange(got);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EndWebGesture()
    {
        gestureSerial++;
        gestureOpen = false;
        foreach (var lease in webLeases) lease.Dispose();
        webLeases.Clear();
    }

    // ------------------------------------------------------------------ layout

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
        if (ghost is { } lifted) lifted.Card.Measure(Shown(lifted).Size);
        foreach (var motion in rails.Values)
            if (motion.Rail.IsVisible) motion.Rail.Measure(motion.Bounds.Size);
        foreach (var (key, splitter) in splitters)
            if (splitter.IsVisible && GutterRect(key) is { } rect) splitter.Measure(rect.Size);
        dropLayer.Measure(size);
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
        if (ghost is { } lifted)
        {
            var rect = Shown(lifted);
            lifted.Card.Measure(rect.Size);
            lifted.Card.Arrange(rect);
        }
        foreach (var motion in rails.Values)
        {
            if (!motion.Rail.IsVisible) continue;
            motion.Rail.Measure(motion.Bounds.Size);
            motion.Rail.Arrange(motion.Bounds);
        }
        foreach (var (key, splitter) in splitters)
            if (splitter.IsVisible && GutterRect(key) is { } rect) splitter.Arrange(rect);
        dropLayer.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>A gutter's splitter rectangle in the workspace: 12 DIP across, centred on the gap.</summary>
    internal Rect? GutterRect(string key)
    {
        if (solved?.Gutter(key) is not { } gutter) return null;
        var half = Splitter.DividerThickness / 2;
        var top = TopBand;
        var position = gutter.Position + (key == rubberKey ? RubberOffset(solved) : 0);
        return gutter.Axis == PaneGutterAxis.X
            ? new Rect(position - half, top + gutter.SpanStart, Splitter.DividerThickness, Math.Max(0, gutter.SpanLength))
            : new Rect(gutter.SpanStart, top + position - half, Math.Max(0, gutter.SpanLength), Splitter.DividerThickness);
    }

    /// <summary>One card's springs: its rectangle, opacity and scale, and where a header drag has taken it.</summary>
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

        /// <summary>A header drag's travel from the slot (a render translation); it springs home on Esc.</summary>
        public SplitterSpring DragX { get; } = new(0);

        public SplitterSpring DragY { get; } = new(0);

        /// <summary>Opened since the last layout: it fades in.</summary>
        public bool Fresh { get; set; }

        /// <summary>Has had a rectangle.</summary>
        public bool Placed { get; set; }

        /// <summary>Drawn in the last layout.</summary>
        public bool Visible { get; set; }

        public bool Leaving { get; set; }

        /// <summary>Fading out because another narrow tab was chosen; it leaves the layout once it has.</summary>
        public bool Hiding { get; set; }

        /// <summary>Its side is folding into a rail: it fades out where it stands and is hidden when it has landed.</summary>
        public bool Folding { get; set; }

        /// <summary>Hidden because its side is folded into a rail: restoring it fades it back in.</summary>
        public bool Folded { get; set; }

        /// <summary>Drawn over the others (expanded, or moving back from it).</summary>
        public bool Lifted { get; set; }

        /// <summary>Held by its header (or springing home from it): drawn over everything.</summary>
        public bool Held { get; set; }

        public bool IsMoving => X.IsMoving || Y.IsMoving || W.IsMoving || H.IsMoving || Opacity.IsMoving || Scale.IsMoving || Slide.IsMoving || DragX.IsMoving || DragY.IsMoving;

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
            DragX.Settle();
            DragY.Settle();
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
            any |= DragX.Step(seconds);
            any |= DragY.Step(seconds);
            return any;
        }
    }

    /// <summary>A rail and its fade.</summary>
    private sealed class RailMotion(PaneRailStrip rail)
    {
        public PaneRailStrip Rail { get; } = rail;

        public SplitterSpring Opacity { get; } = new(0, epsilon: 0.002);

        /// <summary>In the layout now; false while it fades out.</summary>
        public bool Wanted { get; set; }

        public Rect Bounds { get; set; }
    }

    /// <summary>A drop outline and the springs that fade it in and light it.</summary>
    private sealed class DropSlot(PaneDropTarget target, Rect bounds, PaneDropVisual visual)
    {
        public PaneDropTarget Target { get; } = target;

        public Rect Bounds { get; } = bounds;

        public PaneDropVisual Visual { get; } = visual;

        public SplitterSpring Appear { get; } = new(0, epsilon: 0.002);

        public SplitterSpring HotSpring { get; } = new(0, epsilon: 0.002);

        public bool Fading { get; set; }
    }

    /// <summary>A pane lifted by its header.</summary>
    private sealed class Redock(PaneKind kind, CardMotion motion, Point start)
    {
        public PaneKind Kind { get; } = kind;

        /// <summary>Web W4: lifted from the chat's terminal drawer, so the motion is its ghost, not a card of the grid.</summary>
        public bool FromDrawer { get; init; }

        public CardMotion Motion { get; } = motion;

        public Point Start { get; } = start;

        public Point Pointer { get; set; } = start;

        public List<DropSlot> Slots { get; } = [];

        public DropSlot? Hot { get; set; }

        public bool Lifted { get; set; }

        public bool Ended { get; set; }
    }

    /// <summary>A gutter's splitter: the shared Splitter's commands on the solver's ratios.</summary>
    private sealed class GutterTarget(PaneWorkspace owner, string key) : SplitterTarget
    {
        private double dragBase;
        private PaneWorkspaceState? origin;

        private PaneGutter? Gutter => owner.solved?.Gutter(key);

        public override SplitterReport Report() => Gutter is { } gutter
            ? SplitterMath.Report(gutter.Value, gutter.IsCollapsed, gutter.Limits, text: gutter.ValueText, readout: gutter.ValueText)
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
            _ = owner.BeginWebGesture();
        }

        // 1:1 inside the limits; past a collapsible limit the divider gives a third of the overshoot (at most 24 DIP), and
        // past the collapse threshold the side folds, springing while the pointer goes on (4.2).
        public override void Move(Vector travel)
        {
            if (!owner.dragging || Gutter is not { } gutter) return;
            var along = gutter.Axis == PaneGutterAxis.X ? travel.X : travel.Y;
            var raw = dragBase + gutter.ValueSign * along;
            var next = PaneLayout.Drag(owner.state, owner.SolverWidth, owner.SolverHeight, key, raw, origin);
            var folds = next.Collapsed.Contains(key) != owner.state.Collapsed.Contains(key);
            var give = 0.0;
            if (!folds && PaneLayout.Solve(owner.Drawn(next), owner.SolverWidth, owner.SolverHeight).Gutter(key) is { } landed)
                give = PaneRedock.RubberGive(landed, raw, owner.ReduceMotion);
            // A fold or a restore springs, and the springs go on following the pointer until they have landed.
            owner.Apply(next, animate: folds || owner.moving);
            owner.SetRubber(give == 0 ? null : key, give);
        }

        public override void End(bool moved)
        {
            if (!owner.dragging) return;
            owner.dragging = false;
            // The divider gives back what the rubber band took: a spring from where it stands to the limit.
            if (owner.rubberKey is not null)
            {
                owner.rubberKey = null;
                owner.rubberGive = 0;
                owner.dirty = true;
                owner.pendingAnimate = true;
                owner.InvalidateMeasure();
            }
            owner.UpdateResizing();
            owner.UpdateHeld();
            if (origin is { } before && !before.Equals(owner.state)) owner.StateChanged?.Invoke(owner.state);
            origin = null;
        }

        public override void Reset() => owner.Commit(PaneLayout.Reset(owner.state, owner.SolverWidth, owner.SolverHeight, key));

        public override void Step(Vector delta)
        {
            if (Gutter is not { IsCollapsed: false } gutter) return; // a collapsed side comes back with Space, Ctrl+Arrow, Enter, Home or End
            var along = gutter.Axis == PaneGutterAxis.X ? delta.X : delta.Y;
            Set(gutter.Value + gutter.ValueSign * along);
        }

        public override void StepTo(bool maximum)
        {
            if (Gutter is not { } gutter) return;
            Set(maximum ? gutter.NormalMaximum : gutter.NormalMinimum);
        }

        // Space and Ctrl+Arrow toward the collapsible side fold it to its rail; away from it (or Space again) restore it.
        public override void Collapse(bool collapse) =>
            owner.Commit(collapse ? PaneLayout.Collapse(owner.state, owner.SolverWidth, owner.SolverHeight, key) : owner.state.Restore(key));

        public override bool CanSetValue => true;

        // Assistive technology sets the value; the range's collapsed end folds the side.
        public override void SetValue(double value)
        {
            if (Gutter is { CanCollapse: true } gutter && Math.Abs(value - gutter.CollapsedValue) < 0.5)
            {
                Collapse(true);
                return;
            }
            Set(value);
        }

        private void Set(double value) =>
            owner.Commit(PaneLayout.SetGutter(owner.state, owner.SolverWidth, owner.SolverHeight, key, value));
    }
}
