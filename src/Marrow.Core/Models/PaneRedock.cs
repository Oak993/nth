// PORT-ONLY: web surfaces W3b, dragging a pane header to re-dock it (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.6 "Drag
// a header to re-dock", 4.2 "Limits ... Past a minimum it resists (0.3 x rubber band for 24 DIP)"; ED-NEW-web-w3b-2). No
// Swift counterpart. Pure: which drops a layout can express for a lifted pane, where they are, what each does to the
// workspace state, and how far a collapsible limit gives under the pointer. The window draws the outlines and runs the
// result; nothing here knows a control.

namespace Marrow.Core.Models;

/// <summary>Where on a cell a lifted pane can be dropped (3.6).</summary>
public enum PaneDropKind
{
    /// <summary>The cell's body: the two slots swap; a pane that is a tab in a group leaves it and takes that slot's place.</summary>
    Body,

    /// <summary>The cell's header: the pane joins that slot as a tab.</summary>
    Header,

    /// <summary>Web W4: the empty part of the workspace, where the terminal lifted from its drawer opens the grid.</summary>
    Grid,

    /// <summary>Web W4: the chat column's foot, where the terminal lifted from the grid goes back to its drawer.</summary>
    Drawer,
}

/// <summary>One drop target: its rectangle (solver coordinates), the pane it names and the workspace the drop produces.</summary>
public sealed record PaneDropTarget(PaneDropKind Kind, PaneKind Target, PaneRect Bounds, PaneWorkspaceState Result);

/// <summary>The drops of a layout, and the give of a collapsible limit under a drag.</summary>
public static partial class PaneRedock
{
    /// <summary>How far past the pointer's travel a pane lifted from its header follows before it counts as lifted (DESIGN 3.6: 4 DIP).</summary>
    public const double LiftDistance = 4;

    /// <summary>The scale of a lifted pane (3.6).</summary>
    public const double LiftScale = 1.02;

    /// <summary>
    /// Whether <paramref name="pane"/> can be lifted: the workspace is tiled and not expanded, and the pane is a grid pane
    /// the layout draws. The chat column, Side chat and the terminal's drawer are not draggable (3.6).
    /// </summary>
    public static bool CanLift(PaneWorkspaceState state, PaneLayoutResult layout, PaneKind pane)
    {
        var s = state.Normalized();
        return layout.Mode == PaneLayoutMode.Tiles && layout.Expanded is null && s.SlotIndexOf(pane) >= 0
            && layout.CellOf(pane) is { IsSideColumn: false } && layout.IsShown(pane);
    }

    /// <summary>
    /// The drop targets for lifting <paramref name="dragged"/>, only for moves the state can express: another cell's body
    /// (swap), another cell's header (join as a tab), and, for a pane that is a tab in a group, its own cell's body (leave
    /// the group). Header targets come first so a hit test prefers them where they cover a body target.
    /// </summary>
    public static IReadOnlyList<PaneDropTarget> Targets(PaneWorkspaceState state, PaneLayoutResult layout, PaneKind dragged, double drawerHeight = DefaultDrawerHeight)
    {
        var s = state.Normalized();
        if (!CanLift(s, layout, dragged)) return [];
        var own = layout.CellOf(dragged)!;
        var headers = new List<PaneDropTarget>();
        var bodies = new List<PaneDropTarget>();
        foreach (var cell in layout.Cells)
        {
            if (cell.IsSideColumn) continue;
            var header = new PaneRect(cell.Bounds.X, cell.Bounds.Y, cell.Bounds.Width, Math.Min(PaneKinds.HeaderHeight, cell.Bounds.Height));
            var body = new PaneRect(cell.Bounds.X, cell.Bounds.Y + header.Height, cell.Bounds.Width, Math.Max(0, cell.Bounds.Height - header.Height));
            if (ReferenceEquals(cell, own) || cell.Tabs.Contains(dragged))
            {
                // Its own cell: only "leave the group" is a move (the pane's own slot is an explicit tab group).
                var slot = s.Order[s.SlotIndexOf(dragged)];
                if (!slot.IsGroup) continue;
                var mate = slot.Panes.First(pane => pane != dragged);
                var leave = s.DropOnBody(dragged, mate);
                if (!leave.Equals(s)) bodies.Add(new PaneDropTarget(PaneDropKind.Body, mate, body, leave));
                continue;
            }
            var target = cell.Shown;
            if (s.SlotIndexOf(target) < 0) continue;
            var joined = s.JoinAsTab(dragged, target);
            if (!joined.Equals(s)) headers.Add(new PaneDropTarget(PaneDropKind.Header, target, header, joined));
            var swapped = s.DropOnBody(dragged, target);
            if (!swapped.Equals(s)) bodies.Add(new PaneDropTarget(PaneDropKind.Body, target, body, swapped));
        }
        return [.. headers, .. bodies, .. DrawerTargetFor(s, layout, dragged, drawerHeight)];
    }

    /// <summary>The target under (<paramref name="x"/>, <paramref name="y"/>): a header before a body; null over nothing.</summary>
    public static PaneDropTarget? Hit(IReadOnlyList<PaneDropTarget> targets, double x, double y)
    {
        foreach (var target in targets)
        {
            var r = target.Bounds;
            if (x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom) return target;
        }
        return null;
    }

    /// <summary>
    /// How far a collapsible limit gives under a pointer that went <paramref name="raw"/> (the gutter's value units): the
    /// overshoot times 0.3, at most 24 DIP, signed in the direction of the overshoot, and zero once the overshoot reaches
    /// the 56 DIP collapse threshold (the side has folded by then), inside the limits, on a gutter that cannot collapse, and
    /// under Reduce Motion (4.2). Only a collapsible end gives: no pane is squeezed below its minimum where nothing can fold.
    /// </summary>
    public static double RubberGive(PaneGutter gutter, double raw, bool reduceMotion)
    {
        if (reduceMotion || !gutter.CanCollapse || gutter.IsCollapsed || !double.IsFinite(raw)) return 0;
        var past = gutter.CollapsesPastMaximum;
        var over = past ? raw - gutter.NormalMaximum : gutter.NormalMinimum - raw;
        if (over <= 0 || over > PaneKinds.CollapseThreshold) return 0;
        var give = Math.Min(SplitterMath.MaximumGive, over * SplitterMath.Resistance);
        return past ? give : -give;
    }
}
