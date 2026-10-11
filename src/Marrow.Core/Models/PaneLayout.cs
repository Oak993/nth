using Marrow.Core.Port;

// PORT-ONLY: web surfaces W2, the pane solver (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.2 "The tiling rule
// (deterministic)", 3.3, 3.5, 3.8, 4.2; ED-NEW-web-w2-1; owner 2026-10-11 "Web Surfaces Approved"). No Swift
// counterpart. A pure function of the workspace state and the workspace size: same inputs, same layout, every time.
// It follows the approved canvas's solve() and lay() (E:/Droppy/work/design/web-surfaces/index.html) and adds what the
// design asks and the canvas leaves out: per-kind maximums everywhere (the chat takes what capped cells leave),
// explicit tab groups, two rows only when their minimum heights fit, and right-to-left mirroring.

namespace Marrow.Core.Models;

/// <summary>A rectangle in DIP, origin at the workspace's top-left (top-right when mirrored).</summary>
public readonly record struct PaneRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>The same rectangle in a workspace of <paramref name="width"/> read right to left.</summary>
    public PaneRect Mirrored(double width) => this with { X = width - X - Width };

    /// <summary>Whether the two share area (touching edges do not count).</summary>
    public bool Overlaps(PaneRect other, double tolerance = 1e-6) =>
        X < other.Right - tolerance && other.X < Right - tolerance && Y < other.Bottom - tolerance && other.Y < Bottom - tolerance;
}

public enum PaneLayoutMode
{
    /// <summary>Chat column, optional Side chat column and the grid (3.2).</summary>
    Tiles,

    /// <summary>One surface at a time under a segmented tab strip (3.8).</summary>
    Narrow,
}

/// <summary>The axis a gutter moves along: X for a vertical divider (widths), Y for a horizontal one (heights).</summary>
public enum PaneGutterAxis
{
    X,
    Y,
}

/// <summary>
/// One cell of the layout: a pane, an explicit tab group (one slot of two or more panes), or panes that did not fit
/// and joined the last cell as tabs (several slots). <see cref="Shown"/> is the tab on top.
/// </summary>
public sealed record PaneCell(ValueList<PaneKind> Tabs, PaneKind Shown, PaneRect Bounds, int SlotCount, bool IsSideColumn)
{
    /// <summary>The header shows tabs.</summary>
    public bool HasTabs => Tabs.Count > 1;
}

/// <summary>A collapsed side: a 22 DIP rail with the icons of the panes it holds (3.5).</summary>
public sealed record PaneRail(string Key, PaneRect Bounds, ValueList<PaneKind> Panes);

/// <summary>
/// A boundary between two siblings (3.2 rule 10, 4.2), with what the shared Splitter needs: where it is, the value it
/// moves (the size in DIP of <see cref="Subject"/>), the range including any collapsed position, the range without it,
/// the default, the collapse state, and the words for screen readers. <c>Value == ValueAt(Position)</c>, so a pointer
/// position maps straight to a value (mirrored layouts included).
/// </summary>
public sealed record PaneGutter(
    string Key,
    PaneGutterAxis Axis,
    double Position,
    double SpanStart,
    double SpanLength,
    PaneKind Subject,
    double Value,
    double Minimum,
    double Maximum,
    double NormalMinimum,
    double NormalMaximum,
    double DefaultValue,
    bool CanCollapse,
    bool IsCollapsed,
    double CollapsedValue,
    double Base,
    double ValueOrigin,
    double ValueSign,
    string? CoupledKey,
    double CoupledTotal,
    ValueList<PaneKind> Leading,
    ValueList<PaneKind> Trailing,
    string Label,
    string ValueText)
{
    /// <summary>The collapsed position lies past the maximum (the chat gutter) rather than under the minimum (Side chat).</summary>
    public bool CollapsesPastMaximum => CanCollapse && CollapsedValue > NormalMaximum;

    /// <summary>The value the gutter would have with its centre at <paramref name="position"/> on its axis.</summary>
    public double ValueAt(double position) => ValueOrigin + ValueSign * position;

    /// <summary>The centre position on its axis for <paramref name="value"/>.</summary>
    public double PositionOf(double value) => (value - ValueOrigin) / ValueSign;
}

/// <summary>What <see cref="PaneLayout.Solve"/> returns: every rectangle, gutter, rail and tab group.</summary>
public sealed record PaneLayoutResult(
    PaneLayoutMode Mode,
    double Width,
    double Height,
    bool RightToLeft,
    PaneRect Chat,
    PaneRect? Grid,
    int GridColumns,
    ValueList<PaneCell> Cells,
    ValueList<PaneGutter> Gutters,
    ValueList<PaneRail> Rails,
    ValueMap<PaneKind, PaneRect> Bounds,
    ValueSet<PaneKind> Shown,
    PaneKind? Expanded,
    PaneRect? ExpandedBounds,
    ValueList<PaneKind> NarrowTabs,
    PaneKind NarrowSelection,
    PaneRect? NarrowStrip)
{
    /// <summary>The pane's rectangle (a hidden tab shares its cell's), or null when it is closed or on a rail.</summary>
    public PaneRect? BoundsOf(PaneKind kind) => Bounds.TryGetValue(kind, out var rect) ? rect : null;

    /// <summary>Whether the pane is drawn: the chat, a cell's shown tab, the Side chat column, or the narrow selection.</summary>
    public bool IsShown(PaneKind kind) => Shown.Contains(kind);

    public PaneCell? CellOf(PaneKind kind) => Cells.FirstOrDefault(cell => cell.Tabs.Contains(kind));

    public PaneGutter? Gutter(string key) => Gutters.FirstOrDefault(gutter => gutter.Key == key);

    public PaneRail? Rail(string key) => Rails.FirstOrDefault(rail => rail.Key == key);
}

/// <summary>The deterministic tiling rule of DESIGN 3.2, and the gutter operations that write its ratios.</summary>
public static class PaneLayout
{
    private const double G = PaneKinds.Gap;

    /// <summary>
    /// Lays out the workspace (the detail area: window minus sidebar) of <paramref name="width"/> x
    /// <paramref name="height"/> DIP. Bad state is normalised and every ratio clamped; nothing throws.
    /// </summary>
    public static PaneLayoutResult Solve(PaneWorkspaceState state, double width, double height, bool rightToLeft = false)
    {
        var solver = new Solver(state.Normalized(), Sanitize(width), Sanitize(height));
        solver.Run();
        return solver.Result(rightToLeft);
    }

    /// <summary>
    /// The workspace width under which the layout becomes narrow tabs (3.2 rule 2): ChatMin + Gap + the largest minimum
    /// width among the open grid slots. Zero when nothing but the chat is open.
    /// </summary>
    public static double NarrowThreshold(PaneWorkspaceState state)
    {
        var s = state.Normalized();
        var panes = s.IsSideChatOpen ? s.GridPanes.Append(PaneKind.SideChat) : s.GridPanes;
        var widest = panes.Select(k => PaneKinds.Size(k).MinWidth).DefaultIfEmpty(-1).Max();
        return widest < 0 ? 0 : PaneKinds.ChatMinWidth + G + widest;
    }

    /// <summary>
    /// Sets a gutter to <paramref name="value"/> (the size of its subject in DIP), clamped to its range without the
    /// collapsed position, and stores the ratio. A collapsed side is restored first.
    /// </summary>
    public static PaneWorkspaceState SetGutter(PaneWorkspaceState state, double width, double height, string key, double value, bool rightToLeft = false)
    {
        var s = state.Normalized();
        var gutter = Solve(s, width, height, rightToLeft).Gutter(key);
        if (gutter is null || !double.IsFinite(value)) return s;
        if (gutter.IsCollapsed)
        {
            s = s.Restore(key);
            gutter = Solve(s, width, height, rightToLeft).Gutter(key);
            if (gutter is null) return s;
        }
        if (gutter.Base <= 0) return s;
        var v = Clamp(value, gutter.NormalMinimum, gutter.NormalMaximum);
        s = s.WithRatio(key, v / gutter.Base);
        return gutter.CoupledKey is { } coupled ? s.WithRatio(coupled, (gutter.CoupledTotal - v) / gutter.Base) : s;
    }

    /// <summary>
    /// A pointer drag of a gutter to <paramref name="value"/> (4.2): inside the range it tracks 1:1; past the collapsible
    /// side's minimum by <see cref="PaneKinds.CollapseThreshold"/> that side collapses; dragging a collapsed side back
    /// over its minimum restores it to its size before the collapse (tracking resumes on the next move).
    /// <paramref name="origin"/> is the state when the drag began: the size remembered for restore is the one before
    /// the drag, not the minimum the drag squeezed it to.
    /// </summary>
    public static PaneWorkspaceState Drag(PaneWorkspaceState state, double width, double height, string key, double value, PaneWorkspaceState? origin = null, bool rightToLeft = false)
    {
        var s = state.Normalized();
        var gutter = Solve(s, width, height, rightToLeft).Gutter(key);
        if (gutter is null || !double.IsFinite(value)) return s;
        if (gutter.CanCollapse)
        {
            var pastMaximum = gutter.CollapsesPastMaximum;
            if (gutter.IsCollapsed)
            {
                var back = pastMaximum ? value <= gutter.NormalMaximum : value >= gutter.NormalMinimum;
                return back ? s.Restore(key) : s;
            }
            var beyond = pastMaximum
                ? value > gutter.NormalMaximum + PaneKinds.CollapseThreshold
                : value < gutter.NormalMinimum - PaneKinds.CollapseThreshold;
            if (beyond) return Collapse(s, width, height, key, origin, rightToLeft);
        }
        return SetGutter(s, width, height, key, value, rightToLeft);
    }

    /// <summary>
    /// Collapses a gutter's collapsible side (Space, Ctrl+Arrow toward it, or a drag past the threshold), remembering
    /// the ratio it had in <paramref name="origin"/> (the current state when null), or nothing when that side was at its
    /// minimum, so restore uses the default (3.2 rule 11).
    /// </summary>
    public static PaneWorkspaceState Collapse(PaneWorkspaceState state, double width, double height, string key, PaneWorkspaceState? origin = null, bool rightToLeft = false)
    {
        var s = state.Normalized();
        var gutter = Solve(s, width, height, rightToLeft).Gutter(key);
        if (gutter is not { CanCollapse: true, IsCollapsed: false }) return s;
        var source = origin?.Normalized() ?? s;
        var before = origin is null ? gutter : Solve(source, width, height, rightToLeft).Gutter(key);
        double? ratio = null;
        if (before is { IsCollapsed: false } && source.Ratios.TryGetValue(key, out var stored) && !AtCollapsibleMinimum(before))
            ratio = stored;
        return s.Collapse(key, ratio);
    }

    /// <summary>Space on a focused splitter: collapses or restores its collapsible side.</summary>
    public static PaneWorkspaceState ToggleCollapse(PaneWorkspaceState state, double width, double height, string key, bool rightToLeft = false)
    {
        var s = state.Normalized();
        return s.Collapsed.Contains(key) ? s.Restore(key) : Collapse(s, width, height, key, null, rightToLeft);
    }

    /// <summary>Double-click or Enter: the gutter springs to its default ratio (a coupled row ratio resets with it).</summary>
    public static PaneWorkspaceState Reset(PaneWorkspaceState state, double width, double height, string key, bool rightToLeft = false)
    {
        var s = state.Normalized();
        var coupled = Solve(s, width, height, rightToLeft).Gutter(key)?.CoupledKey;
        s = s.Restore(key).WithoutRatio(key);
        return coupled is null ? s : s.WithoutRatio(coupled);
    }

    /// <summary>The collapsible side sits at its minimum (within 1 DIP, as the canvas's atMin).</summary>
    public static bool AtCollapsibleMinimum(PaneGutter gutter) => gutter.CollapsesPastMaximum
        ? gutter.Value >= gutter.NormalMaximum - 1
        : gutter.Value <= gutter.NormalMinimum + 1;

    /// <summary>The canvas's clamp: the lower bound wins when the bounds cross.</summary>
    internal static double Clamp(double value, double low, double high) => Math.Max(low, Math.Min(high, value));

    private static double Sanitize(double length) => double.IsFinite(length) && length > 0 ? length : 0;

    /// <summary>Panes that share one cell: one slot, or the slots that joined the last cell as tabs.</summary>
    private sealed class Group(List<PaneKind> tabs, int slots, PaneWorkspaceState state)
    {
        public List<PaneKind> Tabs { get; } = tabs;

        public int Slots { get; } = slots;

        public double MinWidth { get; } = tabs.Max(k => PaneKinds.Size(k).MinWidth);

        public double MaxWidth { get; } = tabs.Min(k => PaneKinds.Size(k).MaxWidth);

        public double MinHeight { get; } = tabs.Max(k => PaneKinds.Size(k).MinHeight);

        // The most recently chosen member shows; with none chosen, the last (the canvas's tabSel default).
        public PaneKind Shown { get; } = state.RecentTabs.FirstOrDefault(tabs.Contains, tabs[^1]);

        public static Group Merge(IReadOnlyList<Group> groups, PaneWorkspaceState state) => groups.Count == 1
            ? groups[0]
            : new([.. groups.SelectMany(g => g.Tabs)], groups.Sum(g => g.Slots), state);
    }

    /// <summary>The grid's arrangement at one width: columns, rows, and the widest it can be with every cell capped.</summary>
    private readonly record struct Plan(bool Two, int Rows, double MaxWidth);

    /// <summary>Everything right of the chat at one chat width.</summary>
    private readonly record struct Region(double Chat, double SideWidth, double SideMax, double SideBase, double GridWidth, Plan Plan);

    private sealed class Solver(PaneWorkspaceState s, double w, double h)
    {
        private readonly List<PaneCell> cells = [];
        private readonly List<PaneGutter> gutters = [];
        private readonly List<PaneRail> rails = [];
        private readonly Dictionary<PaneKind, PaneRect> bounds = [];
        private readonly HashSet<PaneKind> shown = [];
        private PaneLayoutMode mode = PaneLayoutMode.Tiles;
        private PaneRect chat = new(0, 0, w, h);
        private PaneRect? grid;
        private int columns;
        private List<PaneKind> narrowTabs = [];
        private PaneKind narrowSelection = PaneKind.Chat;
        private PaneRect? narrowStrip;
        private List<Group> gridGroups = [];
        private Group? side;
        private List<Group> layCells = [];
        private bool sideColumn;
        private bool sideCollapsed;
        private double gridMin1;

        private double Ratio(string key, double fallback) =>
            s.Ratios.TryGetValue(key, out var r) && double.IsFinite(r) ? r : fallback;

        private static double Min1(IEnumerable<Group> groups) => groups.Select(g => g.MinWidth).DefaultIfEmpty(0).Max();

        public void Run()
        {
            gridGroups = [.. s.Order.Select(slot => new Group([.. slot.Panes], 1, s))];
            side = s.IsSideChatOpen ? new Group([PaneKind.SideChat], 1, s) : null;
            List<Group> all = side is null ? gridGroups : [.. gridGroups, side];
            if (all.Count > 0 && w < PaneKinds.ChatMinWidth + G + Min1(all))
            {
                Narrow(all);
                return;
            }
            Show(PaneKind.Chat, chat);
            if (all.Count == 0) return;

            var sideMin = PaneKinds.Size(PaneKind.SideChat).MinWidth;
            var length = w - G;
            gridMin1 = Min1(gridGroups);
            sideColumn = side is not null && gridGroups.Count > 0 && w - PaneKinds.ChatMinWidth - G >= sideMin + G + gridMin1;
            layCells = side is not null && !sideColumn ? all : gridGroups;
            sideCollapsed = sideColumn && s.Collapsed.Contains(PaneGutterKeys.Side);
            // The chat's limit keeps Side chat's room even while Side chat is a rail, so restoring it never squeezes the
            // grid (the canvas drags to the same limit); the rail's spare width goes to the grid.
            var workspaceMin = sideColumn ? sideMin + G + gridMin1 : Min1(layCells);
            var chatHigh = length - workspaceMin;
            var chatDefault = PaneKinds.DefaultChatRatio(all.Count);
            // The chat gutter's range is what the chat can end up at: capped cells hand their spare width to the chat, so
            // its least and greatest widths are the solved ones at the two ends of the stored-ratio clamp.
            var chatLow = Math.Max(PaneKinds.ChatMinWidth, RegionAt(PaneKinds.ChatMinWidth).Chat);
            var chatTop = Math.Max(chatHigh, RegionAt(chatHigh).Chat);
            var collapsedAt = w - PaneKinds.Rail - G;
            var chatLeading = new ValueList<PaneKind>([PaneKind.Chat]);
            var trailing = new ValueList<PaneKind>(all.SelectMany(g => g.Tabs));

            if (s.Collapsed.Contains(PaneGutterKeys.Chat))
            {
                chat = new(0, 0, collapsedAt, h);
                Show(PaneKind.Chat, chat);
                rails.Add(new(PaneGutterKeys.Chat, new(collapsedAt + G, 0, PaneKinds.Rail, h), trailing));
                gutters.Add(ChatGutter(collapsedAt, true));
                return;
            }

            var requested = PaneLayout.Clamp(length * Ratio(PaneGutterKeys.Chat, chatDefault), PaneKinds.ChatMinWidth, chatHigh);
            var region = RegionAt(requested);
            chat = new(0, 0, region.Chat, h);
            Show(PaneKind.Chat, chat);
            gutters.Add(ChatGutter(region.Chat, false));

            var gridX = region.Chat + G;
            if (sideColumn)
            {
                var sideBase = region.SideBase;
                if (sideCollapsed)
                    rails.Add(new(PaneGutterKeys.Side, new(w - PaneKinds.Rail, 0, PaneKinds.Rail, h), new([PaneKind.SideChat])));
                else
                {
                    var rect = new PaneRect(w - region.SideWidth, 0, region.SideWidth, h);
                    cells.Add(new(new([PaneKind.SideChat]), PaneKind.SideChat, rect, 1, true));
                    Show(PaneKind.SideChat, rect);
                }
                var sideValue = sideCollapsed ? PaneKinds.Rail : region.SideWidth;
                gutters.Add(new(
                    PaneGutterKeys.Side, PaneGutterAxis.X, w - sideValue - G / 2, 0, h, PaneKind.SideChat,
                    sideValue, PaneKinds.Rail, region.SideMax, sideMin, region.SideMax,
                    PaneLayout.Clamp(sideBase * PaneKinds.DefaultSideRatio, sideMin, region.SideMax),
                    true, sideCollapsed, PaneKinds.Rail, sideBase, w - G / 2, -1, null, 0,
                    new(gridGroups.SelectMany(g => g.Tabs)), new([PaneKind.SideChat]),
                    "Resize panes and Side chat",
                    sideCollapsed ? "Side chat collapsed" : PaneKinds.RoundText(region.SideWidth) + " DIP"));
            }
            grid = new PaneRect(gridX, 0, region.GridWidth, h);
            LayGrid(gridX, 0, region.GridWidth, h, region.Plan);

            PaneGutter ChatGutter(double value, bool collapsed) => new(
                PaneGutterKeys.Chat, PaneGutterAxis.X, value + G / 2, 0, h, PaneKind.Chat,
                value, chatLow, collapsedAt, chatLow, chatTop,
                PaneLayout.Clamp(length * chatDefault, chatLow, chatTop),
                true, collapsed, collapsedAt, length, -G / 2, 1, null, 0, chatLeading, trailing,
                "Resize Chat and panes", collapsed ? "Panes collapsed" : PaneKinds.RoundText(value) + " DIP");
        }

        // The side column and the grid for a requested chat width. A grid wider than its cells' maximums allow shrinks to
        // that width and the chat takes the rest (3.2: space is never left empty); shrinking can only drop it from two
        // columns to one, so this settles in at most two steps.
        private Region RegionAt(double requestedChat)
        {
            var rest = w - requestedChat - G;
            double sideWidth = 0, sideMax = 0, sideBase = 0, available = rest;
            if (sideColumn)
            {
                sideBase = rest - G;
                sideMax = Math.Min(PaneKinds.Size(PaneKind.SideChat).MaxWidth, sideBase - gridMin1);
                sideWidth = sideCollapsed
                    ? PaneKinds.Rail
                    : PaneLayout.Clamp(sideBase * Ratio(PaneGutterKeys.Side, PaneKinds.DefaultSideRatio), PaneKinds.Size(PaneKind.SideChat).MinWidth, sideMax);
                available = sideBase - sideWidth;
            }
            var width = available;
            var plan = PlanAt(width);
            for (var step = 0; step < 4 && plan.MaxWidth < width; step++)
            {
                width = plan.MaxWidth;
                plan = PlanAt(width);
            }
            return new(requestedChat + (available - width), sideWidth, sideMax, sideBase, width, plan);
        }

        // 3.2 rules 6 to 8 at one grid width.
        private Plan PlanAt(double width)
        {
            var c = layCells;
            var n = c.Count;
            if (n == 0) return new(false, 0, double.PositiveInfinity);
            var rest = n >= 4 ? Group.Merge(c.GetRange(3, n - 3), s) : null;
            var two = false;
            if (n >= 2)
            {
                var fits = c[0].MinWidth + G + c[1].MinWidth <= width;
                if (n == 3) fits &= c[2].MinWidth <= width;
                if (rest is not null) fits &= c[2].MinWidth + G + rest.MinWidth <= width;
                var heights = n == 2 || Math.Max(c[0].MinHeight, c[1].MinHeight) + G + Math.Max(c[2].MinHeight, rest?.MinHeight ?? 0) <= h;
                two = fits && heights && (n >= 3 || width >= h * 1.1);
            }
            if (two)
            {
                var top = c[0].MaxWidth + G + c[1].MaxWidth;
                var max = n == 2 ? top : n == 3 ? Math.Min(top, c[2].MaxWidth) : Math.Min(top, c[2].MaxWidth + G + rest!.MaxWidth);
                return new(true, 2, max);
            }
            var rows = RowsFor(h);
            return new(false, rows, RowGroups(rows).Min(g => g.MaxWidth));
        }

        // 3.2 rule 8: as many stacked rows as fit their minimum heights, at most 3; the rest join the last row as tabs.
        private int RowsFor(double height)
        {
            var c = layCells;
            for (var k = Math.Min(3, c.Count); k >= 1; k--)
            {
                var need = c.Take(k - 1).Sum(g => g.MinHeight) + Group.Merge(c.GetRange(k - 1, c.Count - k + 1), s).MinHeight + (k - 1) * G;
                if (need <= height) return k;
            }
            return 1;
        }

        private List<Group> RowGroups(int rows) => [.. layCells.Take(rows - 1), Group.Merge(layCells.GetRange(rows - 1, layCells.Count - rows + 1), s)];

        private void LayGrid(double x, double y, double width, double height, Plan plan)
        {
            var c = layCells;
            var n = c.Count;
            if (n == 0) return;
            if (plan.Two)
            {
                columns = 2;
                if (n == 2)
                {
                    SplitX(PaneGutterKeys.Cols, x, y, width, height, c[0], c[1]);
                    return;
                }
                var bottom = n == 3 ? c[2] : Group.Merge(c.GetRange(2, n - 2), s);
                var rest = n >= 4 ? Group.Merge(c.GetRange(3, n - 3), s) : null;
                var length = height - G;
                var topMin = Math.Max(c[0].MinHeight, c[1].MinHeight);
                var topMax = length - bottom.MinHeight;
                var topHeight = PaneLayout.Clamp(length * Ratio(PaneGutterKeys.Row1, 0.5), topMin, Math.Max(topMin, topMax));
                gutters.Add(new(
                    PaneGutterKeys.Row1, PaneGutterAxis.Y, y + topHeight + G / 2, x, width, c[0].Shown,
                    topHeight, topMin, topMax, topMin, topMax, PaneLayout.Clamp(length * 0.5, topMin, topMax),
                    false, false, 0, length, -(y + G / 2), 1, null, 0,
                    new([.. c[0].Tabs, .. c[1].Tabs]), new(bottom.Tabs), "Resize top and bottom panes",
                    Percent(topHeight, length)));
                SplitX(PaneGutterKeys.Top, x, y, width, topHeight, c[0], c[1]);
                var bottomY = y + topHeight + G;
                var bottomHeight = height - topHeight - G;
                if (rest is null) Put(c[2], new(x, bottomY, width, bottomHeight));
                else SplitX(PaneGutterKeys.Bottom, x, bottomY, width, bottomHeight, c[2], rest);
                return;
            }

            columns = 1;
            var m = plan.Rows;
            var rows = RowGroups(m);
            var minimums = rows.Select(g => g.MinHeight).ToArray();
            var free = height - (m - 1) * G;
            double[] heights;
            if (m == 1) heights = [free];
            else if (m == 2)
            {
                var h1 = PaneLayout.Clamp(free * Ratio(PaneGutterKeys.Row1, 0.5), minimums[0], free - minimums[1]);
                heights = [h1, free - h1];
            }
            else
            {
                var h1 = PaneLayout.Clamp(free * Ratio(PaneGutterKeys.Row1, 1.0 / 3), minimums[0], free - minimums[1] - minimums[2]);
                var h2 = PaneLayout.Clamp(free * Ratio(PaneGutterKeys.Row2, 1.0 / 3), minimums[1], free - h1 - minimums[2]);
                heights = [h1, h2, free - h1 - h2];
            }
            var top0 = y;
            for (var i = 0; i < m; i++)
            {
                Put(rows[i], new(x, top0, width, heights[i]));
                top0 += heights[i] + G;
            }
            if (m >= 2)
            {
                var max1 = m == 3 ? heights[0] + heights[1] - minimums[1] : free - minimums[1];
                gutters.Add(new(
                    PaneGutterKeys.Row1, PaneGutterAxis.Y, y + heights[0] + G / 2, x, width, rows[0].Shown,
                    heights[0], minimums[0], max1, minimums[0], max1,
                    PaneLayout.Clamp(free * (m == 3 ? 1.0 / 3 : 0.5), minimums[0], max1),
                    false, false, 0, free, -(y + G / 2), 1, m == 3 ? PaneGutterKeys.Row2 : null, m == 3 ? heights[0] + heights[1] : 0,
                    new(rows[0].Tabs), new(rows[1].Tabs), Label(rows[0], rows[1]), PaneKinds.RoundText(heights[0]) + " DIP"));
            }
            if (m == 3)
            {
                var y2 = y + heights[0] + G;
                var max2 = free - heights[0] - minimums[2];
                gutters.Add(new(
                    PaneGutterKeys.Row2, PaneGutterAxis.Y, y2 + heights[1] + G / 2, x, width, rows[1].Shown,
                    heights[1], minimums[1], max2, minimums[1], max2, PaneLayout.Clamp(free / 3, minimums[1], max2),
                    false, false, 0, free, -(y2 + G / 2), 1, null, 0,
                    new(rows[1].Tabs), new(rows[2].Tabs), Label(rows[1], rows[2]), PaneKinds.RoundText(heights[1]) + " DIP"));
            }
        }

        // Two cells side by side; the gutter's limits come from both sides' minimums and maximums (3.2 rule 10).
        private void SplitX(string key, double x, double y, double width, double height, Group a, Group b)
        {
            var length = width - G;
            var low = Math.Max(a.MinWidth, length - b.MaxWidth);
            var high = Math.Min(length - b.MinWidth, a.MaxWidth);
            if (low > high)
            {
                low = a.MinWidth;
                high = Math.Max(low, length - b.MinWidth);
            }
            var first = PaneLayout.Clamp(length * Ratio(key, 0.5), low, high);
            Put(a, new(x, y, first, height));
            Put(b, new(x + first + G, y, length - first, height));
            gutters.Add(new(
                key, PaneGutterAxis.X, x + first + G / 2, y, height, a.Shown,
                first, low, high, low, high, PaneLayout.Clamp(length * 0.5, low, high),
                false, false, 0, length, -(x + G / 2), 1, null, 0,
                new(a.Tabs), new(b.Tabs), Label(a, b), Percent(first, length)));
        }

        private static string Label(Group a, Group b) => $"Resize {PaneKinds.ShortName(a.Shown)} and {PaneKinds.ShortName(b.Shown)}";

        private static string Percent(double value, double length) => PaneKinds.RoundText(length > 0 ? value / length * 100 : 0) + " %";

        private void Put(Group group, PaneRect rect)
        {
            cells.Add(new(new(group.Tabs), group.Shown, rect, group.Slots, false));
            foreach (var tab in group.Tabs) bounds[tab] = rect;
            shown.Add(group.Shown);
        }

        private void Show(PaneKind kind, PaneRect rect)
        {
            bounds[kind] = rect;
            shown.Add(kind);
        }

        // 3.8: Chat, then the open panes in order, one surface at a time under the tab strip.
        private void Narrow(List<Group> all)
        {
            mode = PaneLayoutMode.Narrow;
            narrowTabs = [PaneKind.Chat, .. all.SelectMany(g => g.Tabs)];
            narrowSelection = narrowTabs.Contains(s.NarrowSelection) ? s.NarrowSelection : PaneKind.Chat;
            narrowStrip = new PaneRect(0, 0, w, PaneKinds.NarrowTabStrip);
            var surface = new PaneRect(0, PaneKinds.NarrowTabStrip, w, Math.Max(0, h - PaneKinds.NarrowTabStrip));
            chat = surface;
            foreach (var tab in narrowTabs) bounds[tab] = surface;
            shown.Add(narrowSelection);
        }

        public PaneLayoutResult Result(bool rightToLeft)
        {
            var expanded = mode == PaneLayoutMode.Tiles && s.Expanded is { } e ? e : (PaneKind?)null;
            PaneRect? expandedBounds = expanded is null ? null : new PaneRect(0, 0, w, h);
            if (!rightToLeft)
                return new(mode, w, h, false, chat, grid, columns, new(cells), new(gutters), new(rails),
                    new(bounds), new(shown), expanded, expandedBounds, new(narrowTabs), narrowSelection, narrowStrip);
            return new(
                mode, w, h, true, chat.Mirrored(w), grid?.Mirrored(w), columns,
                new(cells.Select(c => c with { Bounds = c.Bounds.Mirrored(w) })),
                new(gutters.Select(Mirror)),
                new(rails.Select(r => r with { Bounds = r.Bounds.Mirrored(w) })),
                new(bounds.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Mirrored(w)))),
                new(shown), expanded, expandedBounds?.Mirrored(w), new(narrowTabs), narrowSelection, narrowStrip?.Mirrored(w));
        }

        // Right to left: dividers mirror and the pointer's value runs the other way on the X axis.
        private PaneGutter Mirror(PaneGutter g) => g.Axis == PaneGutterAxis.X
            ? g with { Position = w - g.Position, ValueOrigin = g.ValueOrigin + g.ValueSign * w, ValueSign = -g.ValueSign }
            : g with { SpanStart = w - g.SpanStart - g.SpanLength };
    }
}
