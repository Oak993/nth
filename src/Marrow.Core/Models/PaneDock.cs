// PORT-ONLY: web surfaces W4, where a lifted pane can land (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.1 "Terminal ...
// stays the chat's drawer by default; can be dragged into the grid", 3.6 "Drag a header to re-dock ... drop targets appear
// as outlines, and only for moves the solver can express (it reads an ordered list of slots): another cell's body (swap
// the two slots in the order), another cell's header (join that slot as a tab) ... The terminal can be dragged out of its
// drawer into the grid and back"; ED-NEW-web-w4-1). No Swift counterpart. A pure function of the workspace state and the
// solved layout: where the pointer is, what a release there would do, what the outline of that target is, and the state
// the release writes. The window only draws and routes the pointer; the rules are here, so they are testable without a
// window. The terminal is the one pane with the two extra targets (the grid, from the drawer; the drawer, from the grid).

namespace Marrow.Core.Models;

/// <summary>What a lifted pane would do if released at a point.</summary>
public enum PaneDropKind
{
    /// <summary>Nowhere: releasing springs the pane home.</summary>
    None,

    /// <summary>On another cell's body: the pane takes that slot's place (a swap, or an insert for a drawer terminal).</summary>
    Body,

    /// <summary>On another cell's header: the pane joins that slot as its last tab.</summary>
    Header,

    /// <summary>The drawer terminal into the grid by rank, because the grid has no cell to drop on yet.</summary>
    Grid,

    /// <summary>The grid terminal back to the chat column's drawer.</summary>
    Drawer,
}

/// <summary>A drop target: its kind and, for <see cref="PaneDropKind.Body"/> and <see cref="PaneDropKind.Header"/>, the pane whose cell it is.</summary>
public readonly record struct PaneDrop(PaneDropKind Kind, PaneKind Pane = PaneKind.Chat)
{
    public static PaneDrop None => default;

    public bool IsNone => Kind == PaneDropKind.None;
}

/// <summary>The drop rules of the pane workspace (DESIGN 3.6).</summary>
public static class PaneDock
{
    /// <summary>With no cell in the grid, the right 40 % of the workspace is where a drawer terminal opens the grid.</summary>
    public const double GridZoneShare = 0.4;

    /// <summary>The lower half of the chat column is where a grid terminal goes back to the drawer.</summary>
    public const double DrawerZoneShare = 0.5;

    /// <summary>The drawer is never outlined shorter than this (the terminal drawer's own minimum height).</summary>
    public const double DrawerMinimumHeight = 140;

    /// <summary>The chat above the drawer keeps at least this much height (the drawer's own limit).</summary>
    public const double ChatReserveHeight = 180;

    /// <summary>
    /// What a release at (<paramref name="x"/>, <paramref name="y"/>) in the solver's coordinates would do for
    /// <paramref name="dragged"/>. Only a tiled, unexpanded workspace takes drops. A pane never lands on its own cell.
    /// </summary>
    public static PaneDrop TargetAt(PaneWorkspaceState state, PaneLayoutResult layout, PaneKind dragged, double x, double y, double headerHeight)
    {
        var s = state.Normalized();
        if (layout.Mode != PaneLayoutMode.Tiles || layout.Expanded is not null || !double.IsFinite(x) || !double.IsFinite(y)) return PaneDrop.None;
        var fromDrawer = dragged == PaneKind.Terminal && s.IsTerminalInDrawer;
        var inGrid = s.SlotIndexOf(dragged) >= 0;
        if (!fromDrawer && !inGrid) return PaneDrop.None;

        foreach (var cell in layout.Cells)
        {
            if (!Contains(cell.Bounds, x, y)) continue;
            if (cell.Tabs.Contains(dragged)) return PaneDrop.None;
            // Side chat is not a slot of the order: it cannot be swapped with or joined.
            if (cell.IsSideColumn || !PaneKinds.IsGridKind(cell.Shown) || s.SlotIndexOf(cell.Shown) < 0) return PaneDrop.None;
            var drop = y < cell.Bounds.Y + headerHeight ? new PaneDrop(PaneDropKind.Header, cell.Shown) : new PaneDrop(PaneDropKind.Body, cell.Shown);
            return Fits(s, layout, dragged, drop) ? drop : PaneDrop.None;
        }

        if (dragged != PaneKind.Terminal) return PaneDrop.None;
        if (fromDrawer)
        {
            // An empty grid has no cell to drop on: the part of the workspace right of the chat (the right 40 % when the
            // chat fills it) opens the grid, if the terminal then fits beside a readable chat.
            if (layout.Cells.Count != 0) return PaneDrop.None;
            var left = layout.Chat.Right < layout.Width - 1 ? layout.Chat.Right : layout.Width * (1 - GridZoneShare);
            if (x < left || x > layout.Width || y < 0 || y > layout.Height) return PaneDrop.None;
            var open = new PaneDrop(PaneDropKind.Grid);
            return Fits(s, layout, dragged, open) && PaneLayout.Solve(Apply(s, dragged, open), layout.Width, layout.Height, layout.RightToLeft).BoundsOf(PaneKind.Terminal) is not null
                ? open : PaneDrop.None;
        }
        var chat = layout.Chat;
        return x >= chat.X && x <= chat.Right && y >= chat.Y + chat.Height * (1 - DrawerZoneShare) && y <= chat.Bottom
            ? new PaneDrop(PaneDropKind.Drawer) : PaneDrop.None;
    }

    /// <summary>
    /// Whether the workspace is still tiled, with every pane at its minimum, after <paramref name="dragged"/> is released on
    /// <paramref name="drop"/>. A terminal from the drawer adds a cell (or a tab whose minimum is the larger of the two): in a
    /// window that is just wide enough for the cells it has, that would switch the workspace to narrow tabs, so it is no target.
    /// A move inside the grid or back to the drawer never narrows it.
    /// </summary>
    public static bool Fits(PaneWorkspaceState state, PaneLayoutResult layout, PaneKind dragged, PaneDrop drop)
    {
        var s = state.Normalized();
        if (drop.IsNone || !(dragged == PaneKind.Terminal && s.IsTerminalInDrawer)) return !drop.IsNone;
        return PaneLayout.Solve(Apply(s, dragged, drop), layout.Width, layout.Height, layout.RightToLeft).Mode == PaneLayoutMode.Tiles;
    }

    /// <summary>Whether the drawer terminal can open the grid at all in a workspace of this size (the drawer header's Move to the panes).</summary>
    public static bool CanOpenInGrid(PaneWorkspaceState state, double width, double height)
    {
        var s = state.Normalized();
        if (!s.IsTerminalInDrawer) return false;
        var after = PaneLayout.Solve(s.MoveTerminalToGrid(), width, height);
        return after.Mode == PaneLayoutMode.Tiles && after.BoundsOf(PaneKind.Terminal) is not null;
    }

    /// <summary>The workspace after <paramref name="dragged"/> is released on <paramref name="drop"/> (unchanged for a drop that does not apply).</summary>
    public static PaneWorkspaceState Apply(PaneWorkspaceState state, PaneKind dragged, PaneDrop drop)
    {
        var s = state.Normalized();
        var terminal = dragged == PaneKind.Terminal;
        var fromDrawer = terminal && s.IsTerminalInDrawer;
        switch (drop.Kind)
        {
            case PaneDropKind.Body:
                if (fromDrawer) return s.SlotIndexOf(drop.Pane) is var at and >= 0 ? s.MoveTerminalToGrid(at) : s;
                return s.DropOnBody(dragged, drop.Pane);
            case PaneDropKind.Header:
                if (fromDrawer) return s.SlotIndexOf(drop.Pane) >= 0 ? s.MoveTerminalToGrid().JoinAsTab(PaneKind.Terminal, drop.Pane) : s;
                return s.JoinAsTab(dragged, drop.Pane);
            case PaneDropKind.Grid:
                return fromDrawer ? s.MoveTerminalToGrid() : s;
            case PaneDropKind.Drawer:
                return terminal && s.SlotIndexOf(PaneKind.Terminal) >= 0 ? s.MoveTerminalToDrawer() : s;
            default:
                return s;
        }
    }

    /// <summary>
    /// The outline a target shows while the pane is over it (solver coordinates): the whole cell for a body, its header
    /// strip for a header, where the terminal would open for the grid, and the drawer's rectangle at the chat's foot for the
    /// drawer. <paramref name="drawerHeight"/> is the terminal drawer's stored height.
    /// </summary>
    public static PaneRect? Outline(PaneWorkspaceState state, PaneLayoutResult layout, PaneKind dragged, PaneDrop drop, double drawerHeight, double headerHeight)
    {
        switch (drop.Kind)
        {
            case PaneDropKind.Body:
            case PaneDropKind.Header:
                if (layout.CellOf(drop.Pane) is not { } cell) return null;
                return drop.Kind == PaneDropKind.Body ? cell.Bounds : cell.Bounds with { Height = Math.Min(headerHeight, cell.Bounds.Height) };
            case PaneDropKind.Grid:
                var after = PaneLayout.Solve(Apply(state, dragged, drop), layout.Width, layout.Height, layout.RightToLeft);
                return after.BoundsOf(PaneKind.Terminal);
            case PaneDropKind.Drawer:
                var chat = layout.Chat;
                var height = Math.Max(DrawerMinimumHeight, Math.Min(drawerHeight, Math.Max(DrawerMinimumHeight, chat.Height - ChatReserveHeight)));
                height = Math.Min(height, chat.Height);
                return new PaneRect(chat.X, chat.Bottom - height, chat.Width, height);
            default:
                return null;
        }
    }

    private static bool Contains(PaneRect rect, double x, double y) => x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom;
}
