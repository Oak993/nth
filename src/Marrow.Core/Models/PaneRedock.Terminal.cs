// PORT-ONLY: web surfaces W4, the terminal between the chat's drawer and the pane grid (E:/Droppy/work/design/web-surfaces/DESIGN.md
// 3.1 "Terminal ... stays the chat's drawer by default; can be dragged into the grid", 3.6 "The chat column is not draggable. The
// terminal can be dragged out of its drawer into the grid and back"; ED-NEW-web-w4-1). No Swift counterpart. W3b's re-dock
// (PaneRedock) moves a pane between the slots of the grid; the terminal has two more places, and so two more kinds of target: from
// its drawer it can land on a cell (taking that slot's place, or joining it as a tab) or open the grid when the grid is empty;
// from a cell it can go back to the drawer. Each target carries the workspace the drop produces, as W3b's do, so the window draws
// the outlines and runs the result, and nothing here knows a control.

namespace Marrow.Core.Models;

public static partial class PaneRedock
{
    /// <summary>The drawer's own default height, where the drawer outline stands when the window does not say.</summary>
    public const double DefaultDrawerHeight = 260;

    /// <summary>The drawer is never outlined shorter than its own minimum (TerminalPanel's 140).</summary>
    public const double DrawerMinimumHeight = 140;

    /// <summary>The chat above the drawer keeps at least this much height (the drawer's own limit).</summary>
    public const double ChatReserveHeight = 180;

    /// <summary>Whether the terminal in its drawer can be lifted: a tiled, unexpanded workspace (the grid is somewhere to go).</summary>
    public static bool CanLiftFromDrawer(PaneWorkspaceState state, PaneLayoutResult layout) =>
        layout.Mode == PaneLayoutMode.Tiles && layout.Expanded is null && state.Normalized().IsTerminalInDrawer;

    /// <summary>
    /// The drop targets for the terminal lifted from its drawer: another cell's header (join it as a tab), another cell's body (the
    /// terminal takes that slot's place and the rest shift one later), and, when the grid has no cell yet, the part of the workspace
    /// where the terminal then opens it. A target is left out where the terminal would not fit beside a readable chat: the workspace
    /// would turn into narrow tabs. Headers come first, as in <see cref="Targets"/>.
    /// </summary>
    public static IReadOnlyList<PaneDropTarget> DrawerTargets(PaneWorkspaceState state, PaneLayoutResult layout)
    {
        var s = state.Normalized();
        if (!CanLiftFromDrawer(s, layout)) return [];
        var headers = new List<PaneDropTarget>();
        var bodies = new List<PaneDropTarget>();
        var grid = new List<PaneDropTarget>();
        foreach (var cell in layout.Cells)
        {
            var slot = s.SlotIndexOf(cell.Shown);
            if (cell.IsSideColumn || !PaneKinds.IsGridKind(cell.Shown) || slot < 0) continue;
            var header = new PaneRect(cell.Bounds.X, cell.Bounds.Y, cell.Bounds.Width, Math.Min(PaneKinds.HeaderHeight, cell.Bounds.Height));
            var body = new PaneRect(cell.Bounds.X, cell.Bounds.Y + header.Height, cell.Bounds.Width, Math.Max(0, cell.Bounds.Height - header.Height));
            var joined = s.MoveTerminalToGrid().JoinAsTab(PaneKind.Terminal, cell.Shown);
            if (Tiled(joined, layout)) headers.Add(new PaneDropTarget(PaneDropKind.Header, cell.Shown, header, joined));
            var taken = s.MoveTerminalToGrid(slot);
            if (Tiled(taken, layout)) bodies.Add(new PaneDropTarget(PaneDropKind.Body, cell.Shown, body, taken));
        }
        if (layout.Cells.Count == 0)
        {
            var opened = s.MoveTerminalToGrid();
            var after = PaneLayout.Solve(opened, layout.Width, layout.Height, layout.RightToLeft);
            if (after.Mode == PaneLayoutMode.Tiles && after.BoundsOf(PaneKind.Terminal) is { } where)
                grid.Add(new PaneDropTarget(PaneDropKind.Grid, PaneKind.Terminal, where, opened));
        }
        return [.. headers, .. bodies, .. grid];
    }

    /// <summary>Whether the drawer terminal can open the grid at all in a workspace of this size (the drawer header's Move to the panes).</summary>
    public static bool CanOpenInGrid(PaneWorkspaceState state, double width, double height)
    {
        var s = state.Normalized();
        if (!s.IsTerminalInDrawer) return false;
        var after = PaneLayout.Solve(s.MoveTerminalToGrid(), width, height);
        return after.Mode == PaneLayoutMode.Tiles && after.BoundsOf(PaneKind.Terminal) is not null;
    }

    /// <summary>
    /// The drawer, outlined at the chat column's foot (solver coordinates): the drawer's stored height, kept between its own minimum
    /// and what leaves the chat its 180 DIP above it.
    /// </summary>
    public static PaneRect DrawerBounds(PaneLayoutResult layout, double drawerHeight)
    {
        var chat = layout.Chat;
        var height = Math.Max(DrawerMinimumHeight, Math.Min(drawerHeight, Math.Max(DrawerMinimumHeight, chat.Height - ChatReserveHeight)));
        height = Math.Min(height, chat.Height);
        return new PaneRect(chat.X, chat.Bottom - height, chat.Width, height);
    }

    // The terminal lifted from a cell can also go back to its drawer: one more target, the drawer's outline.
    private static IEnumerable<PaneDropTarget> DrawerTargetFor(PaneWorkspaceState s, PaneLayoutResult layout, PaneKind dragged, double drawerHeight)
    {
        if (dragged != PaneKind.Terminal || s.SlotIndexOf(PaneKind.Terminal) < 0) yield break;
        yield return new PaneDropTarget(PaneDropKind.Drawer, PaneKind.Terminal, DrawerBounds(layout, drawerHeight), s.MoveTerminalToDrawer());
    }

    private static bool Tiled(PaneWorkspaceState result, PaneLayoutResult layout) =>
        PaneLayout.Solve(result, layout.Width, layout.Height, layout.RightToLeft).Mode == PaneLayoutMode.Tiles;
}
