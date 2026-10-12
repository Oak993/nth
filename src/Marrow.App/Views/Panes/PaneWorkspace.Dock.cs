using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.UI.Panes;

// PORT-ONLY: web surfaces W4, dragging the terminal between the chat's drawer and the pane grid (E:/Droppy/work/design/web-surfaces/
// DESIGN.md 3.1 "Terminal ... stays the chat's drawer by default; can be dragged into the grid", 3.6 "Press on a header's empty
// area and move 4 DIP: the pane lifts (scale 1.02, deeper shadow ...). Drop targets appear as outlines ... The target under the
// pointer fills softly. Release: the solver runs with the new order and everything springs into place. Esc or releasing over
// nothing springs the pane home ... The terminal can be dragged out of its drawer into the grid and back"; 2.6 motion;
// ED-NEW-web-w4-1). No Swift counterpart. The canvas's own drag (index.html "header drag to re-dock") is the model: a lifted
// pane follows the pointer 1:1 (a render transform), every place it can land shows a dashed outline, the one under the pointer
// a solid one with a soft fill, and a release springs. What a release does is PaneDock's (Marrow.Core): this file draws and
// routes the pointer. A card lifted from the grid is the real card; a drawer has its place in the chat column, so it lifts as a
// ghost (a copy of the pane's chrome with an empty body) that springs into the cell the real pane then fades in at.
// Only the terminal is dockable in this unit (W3b makes every header draggable with the same machinery).

namespace Marrow.App.Views.Panes;

public sealed partial class PaneWorkspace
{
    /// <summary>The press must travel this far before a header drag lifts the pane (3.6).</summary>
    public const double DockSlop = 4;

    /// <summary>The scale a lifted pane has (the canvas's 1.02).</summary>
    public const double LiftScale = 1.02;

    private const int DockZIndex = 40; // the canvas's .pane.lift z-index, above every outline

    private Dock? dock;
    private bool committing;
    private TopLevel? escapeScope;
    private readonly List<DockOutline> outlines = [];

    /// <summary>Which end of the dock drag the pane started from.</summary>
    internal enum DockSource
    {
        /// <summary>The pane's card in the grid.</summary>
        Card,

        /// <summary>The terminal's drawer in the chat column.</summary>
        Drawer,
    }

    /// <summary>A pane is being dragged (lifted, or springing home or into its new cell).</summary>
    public bool IsDocking => dock is not null;

    /// <summary>The drop target under the pointer now.</summary>
    public PaneDrop DockDrop => dock?.Drop ?? PaneDrop.None;

    /// <summary>The terminal drawer's stored height: where its drop outline falls. Defaults to the drawer's own 260.</summary>
    public Func<double>? DrawerHeight { get; set; }

    /// <summary>
    /// Whether the terminal drawer of <paramref name="from"/> (the thread's workspace state) can open the grid in this workspace's
    /// size (below it the workspace would be narrow tabs).
    /// </summary>
    internal bool CanDockTerminalToGrid(PaneWorkspaceState from) => PaneDock.CanOpenInGrid(from, SolverWidth, SolverHeight);

    /// <summary>The ghost card of a drawer drag, while it is drawn.</summary>
    internal PaneCard? DockGhost => dock?.Ghost?.Card;

    /// <summary>Every outline drawn for the drag in progress, with its rectangle and how strongly it is lit (0 dashed, 1 the target).</summary>
    internal IReadOnlyList<(PaneDropKind Kind, PaneKind Pane, Rect Bounds, double Level)> DockOutlines =>
        outlines.Select(outline => (outline.Kind, outline.Pane, outline.Bounds, outline.Level.Value)).ToList();

    /// <summary>The pane kinds a header drag can lift in this build (W3b widens it to every grid pane).</summary>
    internal static bool IsDockable(PaneKind kind) => kind == PaneKind.Terminal;

    /// <summary>
    /// Lifts <paramref name="kind"/> (3.6). <paramref name="drawer"/> is the terminal's drawer when the drag starts there, null
    /// when it starts from the pane's own card. False when nothing can be lifted now: a narrow or expanded workspace, no layout
    /// yet, a pane this build does not dock, or a drag already running.
    /// </summary>
    internal bool BeginDock(PaneKind kind, Point pointer, Control? drawer = null)
    {
        if (dock is not null || !IsDockable(kind) || solved is not { Mode: PaneLayoutMode.Tiles, Expanded: null } layout) return false;
        var fromDrawer = drawer is not null;
        var normal = state.Normalized();
        if (fromDrawer ? !(kind == PaneKind.Terminal && normal.IsTerminalInDrawer) : !cards.ContainsKey(kind) || cards[kind].Leaving || normal.SlotIndexOf(kind) < 0) return false;
        SettleAll(); // pointer-down settling (4.2): a drag begun during a reflow would lag the pointer by the spring's remaining distance
        var reduce = ReduceMotion;
        var next = new Dock { Kind = kind, Source = fromDrawer ? DockSource.Drawer : DockSource.Card, Start = pointer, Before = state };
        if (fromDrawer)
        {
            var origin = BoundsInWorkspace(drawer!);
            if (origin is not { Width: > 0, Height: > 0 } rect) return false;
            next.Origin = rect;
            var ghost = new PaneCard(kind, new PaneContent(new Border(), PaneKinds.Title(kind))) { IsHitTestVisible = false, IsLiftedLook = true, IsExpandedLook = false };
            ghost.Header.CanExpand = false;
            AutomationProperties.SetAccessibilityView(ghost, AccessibilityView.Raw); // a copy of the drawer's chrome: the drawer is what a screen reader has
            var motion = new CardMotion(ghost) { Placed = true, Visible = true };
            motion.SnapRect(rect);
            motion.Opacity.Snap(1);
            motion.Scale.Snap(1);
            motion.Scale.SetTarget(reduce ? 1 : LiftScale, reduce);
            ghost.ZIndex = DockZIndex;
            Children.Add(ghost);
            next.Ghost = motion;
            next.Origin = rect;
        }
        else
        {
            var motion = cards[kind];
            next.Origin = Shown(motion);
            motion.Card.IsLiftedLook = true;
            motion.Lifted = true;
            motion.Scale.SetTarget(reduce ? 1 : LiftScale, reduce);
        }
        dock = next;
        ApplyLooks(); // the lifted card is above the others from this moment
        BuildOutlines(layout, next);
        UpdateDockTarget(pointer);
        escapeScope = TopLevel.GetTopLevel(this);
        escapeScope?.AddHandler(KeyDownEvent, OnDockKey, RoutingStrategies.Tunnel); // Esc springs the pane home, wherever the keyboard is
        UpdateHeld();
        moving = true;
        RequestFrame();
        InvalidateArrange();
        return true;
    }

    /// <summary>The pointer moved with the pane lifted: it follows 1:1 and the target under it lights.</summary>
    internal void UpdateDock(Point pointer)
    {
        if (dock is not { Ending: false } current) return;
        var delta = pointer - current.Start;
        if (current.Source == DockSource.Card)
        {
            current.ShiftX.Snap(delta.X);
            current.ShiftY.Snap(delta.Y);
            cards[current.Kind].Card.Shift = new Vector(delta.X, delta.Y);
        }
        else if (current.Ghost is { } ghost)
        {
            ghost.X.Snap(current.Origin.X + delta.X);
            ghost.Y.Snap(current.Origin.Y + delta.Y);
            InvalidateArrange();
        }
        UpdateDockTarget(pointer);
    }

    /// <summary>The pointer was released (or lost). Over a target the pane lands there; over nothing it springs home.</summary>
    internal void EndDock(bool commit)
    {
        if (dock is not { Ending: false } current) return;
        StopListeningForEscape();
        current.Ending = true;
        var reduce = ReduceMotion;
        var drop = current.Drop;
        var next = commit && !drop.IsNone ? PaneDock.Apply(state, current.Kind, drop).Normalized() : state;
        var lands = !next.Equals(state);
        foreach (var outline in outlines) outline.Level.SetTarget(0, reduce);
        if (current.Source == DockSource.Card)
        {
            var motion = cards[current.Kind];
            if (lands)
            {
                // The displaced rectangle becomes the card's own, and the new layout springs from there (the canvas's c.x.snap(cx)).
                motion.X.Snap(motion.X.Value + current.ShiftX.Value);
                motion.Y.Snap(motion.Y.Value + current.ShiftY.Value);
                current.ShiftX.Snap(0);
                current.ShiftY.Snap(0);
                motion.Card.Shift = default;
            }
            else
            {
                current.ShiftX.SetTarget(0, reduce);
                current.ShiftY.SetTarget(0, reduce);
            }
            motion.Scale.SetTarget(1, reduce);
        }
        else if (current.Ghost is { } ghost)
        {
            var final = lands && PaneLayout.Solve(next, SolverWidth, SolverHeight).BoundsOf(current.Kind) is { } bounds
                ? new Rect(bounds.X, bounds.Y + TopBand, bounds.Width, bounds.Height)
                : current.Origin;
            ghost.SetRect(final, reduce);
            ghost.Scale.SetTarget(1, reduce);
            ghost.Opacity.SetTarget(0, reduce);
        }
        if (lands)
        {
            committing = true;
            try { Commit(next); }
            finally { committing = false; }
        }
        moving = true;
        UpdateHeld();
        RequestFrame();
        InvalidateMeasure();
    }

    /// <summary>Esc, a state change from elsewhere, a thread switch or a detach: the drag ends where it is, nothing committed.</summary>
    internal void CancelDock(bool immediately)
    {
        if (dock is null) return;
        if (!immediately && dock.Ending == false)
        {
            EndDock(false);
            return;
        }
        FinishDock();
    }

    private void StopListeningForEscape()
    {
        escapeScope?.RemoveHandler(KeyDownEvent, OnDockKey);
        escapeScope = null;
    }

    private void OnDockKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || dock is not { Ending: false }) return;
        e.Handled = true;
        EndDock(false);
    }

    private Rect? BoundsInWorkspace(Control control)
    {
        if (control.TranslatePoint(default, this) is not { } origin) return null;
        return new Rect(origin, control.Bounds.Size);
    }

    // The outlines of every place the pane can land (dashed), made once at the lift from the layout the drag began in.
    private void BuildOutlines(PaneLayoutResult layout, Dock current)
    {
        ClearOutlines();
        var normal = state.Normalized();
        var drawerHeight = DrawerHeight?.Invoke() ?? 260;
        void Add(PaneDropKind kind, PaneKind pane, PaneRect rect)
        {
            var outline = new DockOutline(kind, pane, new Rect(rect.X, rect.Y + TopBand, rect.Width, rect.Height), this);
            outlines.Add(outline);
            Children.Add(outline.Dashed);
            Children.Add(outline.Lit);
        }
        foreach (var cell in layout.Cells)
        {
            if (cell.IsSideColumn || cell.Tabs.Contains(current.Kind) || !PaneKinds.IsGridKind(cell.Shown) || normal.SlotIndexOf(cell.Shown) < 0) continue;
            if (!PaneDock.Fits(normal, layout, current.Kind, new PaneDrop(PaneDropKind.Body, cell.Shown))) continue; // it would narrow the workspace: no place to land
            Add(PaneDropKind.Body, cell.Shown, cell.Bounds);
        }
        if (current.Kind != PaneKind.Terminal) return;
        if (current.Source == DockSource.Drawer && layout.Cells.Count == 0)
        {
            var probe = new PaneDrop(PaneDropKind.Grid);
            if (PaneDock.Fits(normal, layout, current.Kind, probe)
                && PaneDock.Outline(normal, layout, current.Kind, probe, drawerHeight, PaneTokens.HeaderHeight) is { } grid) Add(PaneDropKind.Grid, PaneKind.Terminal, grid);
        }
        else if (current.Source == DockSource.Card
            && PaneDock.Outline(normal, layout, current.Kind, new PaneDrop(PaneDropKind.Drawer), drawerHeight, PaneTokens.HeaderHeight) is { } drawer)
            Add(PaneDropKind.Drawer, PaneKind.Terminal, drawer);
    }

    private void ClearOutlines()
    {
        foreach (var outline in outlines)
        {
            Children.Remove(outline.Dashed);
            Children.Remove(outline.Lit);
        }
        outlines.Clear();
    }

    // The drop under the pointer, and which outline is lit for it.
    private void UpdateDockTarget(Point pointer)
    {
        if (dock is not { } current || solved is not { } layout) return;
        var drop = PaneDock.TargetAt(state, layout, current.Kind, pointer.X, pointer.Y - TopBand, PaneTokens.HeaderHeight);
        var reduce = ReduceMotion;
        current.Drop = drop;
        foreach (var outline in outlines)
        {
            var lit = !drop.IsNone && outline.Matches(drop);
            outline.HeaderOnly = lit && drop.Kind == PaneDropKind.Header;
            outline.Level.SetTarget(lit ? 1 : 0, reduce);
        }
        moving = true;
        RequestFrame();
        InvalidateArrange();
    }

    // The frame loop's share: the outlines' light, the drag's own springs, the ghost's rectangle.
    private bool StepDock(double seconds)
    {
        if (dock is not { } current) return false;
        var any = false;
        foreach (var outline in outlines) any |= outline.Level.Step(seconds);
        if (current.Source == DockSource.Card && cards.TryGetValue(current.Kind, out var motion))
        {
            var shifting = current.ShiftX.Step(seconds);
            shifting |= current.ShiftY.Step(seconds);
            motion.Card.Shift = new Vector(current.ShiftX.Value, current.ShiftY.Value);
            any |= shifting;
        }
        if (current.Ghost is { } ghost) any |= ghost.Step(seconds);
        if (current.Ending && !any && !DockCardMoving(current)) FinishDock();
        return any;
    }

    private bool DockCardMoving(Dock current) =>
        current.Source == DockSource.Card && cards.TryGetValue(current.Kind, out var motion) && motion.IsMoving;

    // Pointer-down settling for the dock: everything in flight lands now.
    private void SettleDock()
    {
        if (dock is not { } current) return;
        foreach (var outline in outlines) outline.Level.Settle();
        current.ShiftX.Settle();
        current.ShiftY.Settle();
        current.Ghost?.Settle();
        if (current.Ending) FinishDock();
    }

    private bool DockMoving => dock is { } current
        && (outlines.Any(outline => outline.Level.IsMoving) || current.ShiftX.IsMoving || current.ShiftY.IsMoving || current.Ghost?.IsMoving == true || current.Ending);

    // The drag is over and everything has landed: the ghost and the outlines go, the card puts its look down.
    private void FinishDock()
    {
        if (dock is not { } current) return;
        StopListeningForEscape();
        ClearOutlines();
        if (current.Ghost is { } ghost)
        {
            ghost.Card.Detach();
            Children.Remove(ghost.Card);
        }
        if (cards.TryGetValue(current.Kind, out var motion))
        {
            motion.Card.Shift = default;
            motion.Card.IsLiftedLook = false;
        }
        dock = null;
        UpdateHeld();
        ApplyLooks();
        InvalidateMeasure();
    }

    private void MeasureDock(Size size)
    {
        if (dock is not { } current) return;
        foreach (var outline in outlines)
        {
            outline.Dashed.Measure(outline.Frame.Size);
            outline.Lit.Measure(outline.Frame.Size);
        }
        if (current.Ghost is { } ghost) ghost.Card.Measure(Shown(ghost).Size);
    }

    private void ArrangeDock()
    {
        if (dock is not { } current) return;
        foreach (var outline in outlines)
        {
            outline.Dashed.Arrange(outline.Frame);
            var lit = outline.HeaderOnly ? outline.Frame.WithHeight(Math.Min(PaneTokens.HeaderHeight - 12, outline.Frame.Height)) : outline.Frame;
            outline.Lit.Measure(lit.Size);
            outline.Lit.Arrange(lit);
            outline.Apply();
        }
        if (current.Ghost is { } ghost)
        {
            var rect = Shown(ghost);
            ghost.Card.Measure(rect.Size);
            ghost.Card.Arrange(rect);
        }
    }

    // Applies the ghost's looks (opacity, scale) with the cards'.
    private void ApplyDockLooks()
    {
        if (dock?.Ghost is not { } ghost) return;
        ghost.Card.Opacity = Math.Clamp(ghost.Opacity.Value, 0, 1);
        ghost.Card.Scale = Math.Clamp(ghost.Scale.Value, 0.9, 1.03);
    }

    /// <summary>The drag in flight: what was lifted, where it began, the target now and the springs that draw it.</summary>
    private sealed class Dock
    {
        public required PaneKind Kind { get; init; }

        public required DockSource Source { get; init; }

        public required Point Start { get; init; }

        public required PaneWorkspaceState Before { get; init; }

        public Rect Origin { get; set; }

        public PaneDrop Drop { get; set; }

        public CardMotion? Ghost { get; set; }

        public SplitterSpring ShiftX { get; } = new(0);

        public SplitterSpring ShiftY { get; } = new(0);

        public bool Ending { get; set; }
    }

    /// <summary>
    /// One place the lifted pane can land: a dashed outline always, and a solid one with a soft fill that fades in while the
    /// pointer is over it (the canvas's .dz and .drop; its 0.2 s background and border transition is the spring on
    /// <see cref="Level"/>, opacity only).
    /// </summary>
    private sealed class DockOutline
    {
        public DockOutline(PaneDropKind kind, PaneKind pane, Rect bounds, Control owner)
        {
            Kind = kind;
            Pane = pane;
            Bounds = bounds;
            Dashed = new DockFrame { Stroke = PaneTokens.Text1.Opacity(.22).Brush(owner), IsDashed = true, ZIndex = 30 };
            Lit = new DockFrame { Stroke = PaneTokens.Text1.Opacity(.6).Brush(owner), Fill = PaneTokens.Text1.Opacity(.07).Brush(owner), ZIndex = 31, Opacity = 0 };
        }

        public PaneDropKind Kind { get; }

        public PaneKind Pane { get; }

        public Rect Bounds { get; }

        /// <summary>The outline's rectangle: the cell inset by 6 DIP (the canvas's inset: 6px).</summary>
        public Rect Frame => Bounds.Deflate(6);

        public DockFrame Dashed { get; }

        public DockFrame Lit { get; }

        public bool HeaderOnly { get; set; }

        public SplitterSpring Level { get; } = new(0, epsilon: 0.002);

        public bool Matches(PaneDrop drop) => drop.Kind switch
        {
            PaneDropKind.Body or PaneDropKind.Header => Kind == PaneDropKind.Body && Pane == drop.Pane,
            PaneDropKind.Grid => Kind == PaneDropKind.Grid,
            PaneDropKind.Drawer => Kind == PaneDropKind.Drawer,
            _ => false,
        };

        public void Apply() => Lit.Opacity = Math.Clamp(Level.Value, 0, 1);
    }

    /// <summary>A rounded outline, dashed or solid, with an optional fill (the canvas's 9 DIP radius and 1.5 DIP line).</summary>
    private sealed class DockFrame : Control
    {
        public IBrush? Stroke { get; init; }

        public IBrush? Fill { get; init; }

        public bool IsDashed { get; init; }

        public DockFrame()
        {
            IsHitTestVisible = false;
            AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // decoration, not content
        }

        protected override Size MeasureOverride(Size availableSize) => availableSize.Width > 0 && availableSize.Height > 0 ? availableSize : default;

        public override void Render(DrawingContext context)
        {
            if (Bounds.Width < 2 || Bounds.Height < 2) return;
            var pen = new Pen(Stroke, 1.5, IsDashed ? new DashStyle([3, 3], 0) : null);
            context.DrawRectangle(Fill, pen, new Rect(Bounds.Size).Deflate(.75), 9, 9);
        }
    }
}
