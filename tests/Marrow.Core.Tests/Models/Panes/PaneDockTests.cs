using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 3.1 "Terminal ... can be dragged into the grid", 3.6 "Drag a header to re-dock" (web surfaces W4; ED-NEW-web-w4-1):
// where a lifted pane lands. A pointer over a cell's body swaps (a drawer terminal takes that slot's place), over a cell's
// header joins that slot as a tab, over the empty part of the workspace opens the grid for a drawer terminal, over the chat's
// lower half sends a grid terminal back to its drawer; and nowhere else. A release writes a state the solver can draw.
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class PaneDockTests
{
    private const double Header = 36;
    private static readonly PaneKind Tasks = PaneKind.BackgroundTasks, Tabula = PaneKind.Artifacts, Visus = PaneKind.Browser;

    private static PaneWorkspaceState Drawer(params PaneKind[] panes) =>
        panes.Aggregate(PaneWorkspaceState.Default.Open(PaneKind.Terminal), (state, kind) => state.Open(kind));

    private static PaneLayoutResult Solve(PaneWorkspaceState state, double width = 1400, double height = 800) => PaneLayout.Solve(state, width, height);

    private static (double X, double Y) Centre(PaneRect rect) => (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    [Fact]
    public void A_drawer_terminal_over_a_cells_body_or_header_targets_that_cell()
    {
        var state = Drawer(Tasks, Visus);
        var layout = Solve(state);
        var tasks = layout.BoundsOf(Tasks)!.Value;
        var (x, y) = Centre(tasks);
        Assert.Equal(new PaneDrop(PaneDropKind.Body, Tasks), PaneDock.TargetAt(state, layout, PaneKind.Terminal, x, y, Header));
        Assert.Equal(new PaneDrop(PaneDropKind.Header, Tasks), PaneDock.TargetAt(state, layout, PaneKind.Terminal, x, tasks.Y + 10, Header));
        Assert.Equal(new PaneDrop(PaneDropKind.Body, Tasks), PaneDock.TargetAt(state, layout, PaneKind.Terminal, x, tasks.Y + Header, Header));
        var visus = layout.BoundsOf(Visus)!.Value;
        Assert.Equal(new PaneDrop(PaneDropKind.Body, Visus), PaneDock.TargetAt(state, layout, PaneKind.Terminal, Centre(visus).X, Centre(visus).Y, Header));
    }

    [Fact]
    public void The_chat_the_gaps_and_the_outside_are_not_targets_while_the_grid_has_cells()
    {
        var state = Drawer(Tasks);
        var layout = Solve(state);
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, layout.Chat.X + 100, layout.Chat.Y + 100, Header).IsNone);
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, layout.Chat.Right + 4, 100, Header).IsNone, "the 8 DIP gap");
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, -5, -5, Header).IsNone);
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, double.NaN, 10, Header).IsNone);
    }

    [Fact]
    public void With_no_cell_in_the_grid_the_part_right_of_the_chat_opens_it_for_a_drawer_terminal()
    {
        var state = Drawer();
        var layout = Solve(state);
        // The chat fills the workspace, so the right 40 % is the zone.
        var left = layout.Width * (1 - PaneDock.GridZoneShare);
        Assert.Equal(new PaneDrop(PaneDropKind.Grid), PaneDock.TargetAt(state, layout, PaneKind.Terminal, left + 1, 300, Header));
        Assert.Equal(new PaneDrop(PaneDropKind.Grid), PaneDock.TargetAt(state, layout, PaneKind.Terminal, layout.Width - 2, 10, Header));
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, left - 5, 300, Header).IsNone);
        // The outline is where the terminal would then be: a cell of the grid, right of a readable chat.
        var outline = PaneDock.Outline(state, layout, PaneKind.Terminal, new PaneDrop(PaneDropKind.Grid), 260, Header);
        Assert.NotNull(outline);
        Assert.True(outline!.Value.X >= PaneKinds.ChatMinWidth);
        Assert.True(outline.Value.Width >= PaneKinds.Size(PaneKind.Terminal).MinWidth - 1e-6);
        var landed = PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Grid));
        Assert.False(landed.IsTerminalInDrawer);
        Assert.Equal(new[] { PaneKind.Terminal }, landed.GridPanes);
        Assert.True(landed.TerminalPrefersGrid);
    }

    [Fact]
    public void A_workspace_too_narrow_for_the_terminal_beside_the_chat_gives_no_grid_target()
    {
        var state = Drawer();
        var layout = Solve(state, width: 600);
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, 590, 100, Header).IsNone);
    }

    [Fact]
    public void A_grid_terminal_over_the_chats_lower_half_targets_the_drawer_and_an_outline_sits_at_its_foot()
    {
        var state = Drawer(Tasks).MoveTerminalToGrid();
        var layout = Solve(state);
        var chat = layout.Chat;
        Assert.Equal(new PaneDrop(PaneDropKind.Drawer), PaneDock.TargetAt(state, layout, PaneKind.Terminal, chat.X + 50, chat.Bottom - 20, Header));
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, chat.X + 50, chat.Y + chat.Height * 0.3, Header).IsNone, "the upper half is not the drawer");
        var outline = PaneDock.Outline(state, layout, PaneKind.Terminal, new PaneDrop(PaneDropKind.Drawer), 260, Header)!.Value;
        Assert.Equal(chat.X, outline.X);
        Assert.Equal(chat.Width, outline.Width);
        Assert.Equal(chat.Bottom, outline.Bottom, 6);
        Assert.Equal(260, outline.Height);
        // The drawer is never outlined shorter than its minimum nor so tall that the chat loses its 180 above it.
        Assert.Equal(PaneDock.DrawerMinimumHeight, PaneDock.Outline(state, layout, PaneKind.Terminal, new PaneDrop(PaneDropKind.Drawer), 20, Header)!.Value.Height);
        Assert.Equal(chat.Height - PaneDock.ChatReserveHeight, PaneDock.Outline(state, layout, PaneKind.Terminal, new PaneDrop(PaneDropKind.Drawer), 9000, Header)!.Value.Height, 6);
        var landed = PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Drawer));
        Assert.True(landed.IsTerminalInDrawer);
        Assert.DoesNotContain(PaneKind.Terminal, landed.GridPanes);
        Assert.False(landed.TerminalPrefersGrid);
    }

    [Fact]
    public void Only_the_terminal_has_the_grid_and_the_drawer_targets()
    {
        var state = Drawer(Tasks, Visus);
        var layout = Solve(state);
        Assert.True(PaneDock.TargetAt(state, layout, Tasks, layout.Chat.X + 50, layout.Chat.Bottom - 20, Header).IsNone, "Tasks has no drawer");
        Assert.Equal(state, PaneDock.Apply(state, Tasks, new PaneDrop(PaneDropKind.Drawer)));
        Assert.Equal(state, PaneDock.Apply(state, Tasks, new PaneDrop(PaneDropKind.Grid)));
        var grid = state.MoveTerminalToGrid();
        Assert.Equal(grid, PaneDock.Apply(grid, PaneKind.Terminal, new PaneDrop(PaneDropKind.Grid)));
    }

    [Fact]
    public void A_drawer_terminal_that_would_narrow_the_workspace_has_no_target_and_cannot_open_the_grid()
    {
        // Tasks alone is tiled from 688 DIP (400 + 8 + 280); with the terminal (320) it needs 728.
        var state = Drawer(Tasks);
        var layout = Solve(state, width: 710);
        Assert.Equal(PaneLayoutMode.Tiles, layout.Mode);
        var tasks = layout.BoundsOf(Tasks)!.Value;
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, Centre(tasks).X, Centre(tasks).Y, Header).IsNone);
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, Centre(tasks).X, tasks.Y + 5, Header).IsNone);
        Assert.False(PaneDock.Fits(state, layout, PaneKind.Terminal, new PaneDrop(PaneDropKind.Body, Tasks)));
        Assert.False(PaneDock.Fits(state, layout, PaneKind.Terminal, new PaneDrop(PaneDropKind.Header, Tasks)));
        Assert.False(PaneDock.CanOpenInGrid(state, 710, 800));
        Assert.True(PaneDock.CanOpenInGrid(state, 740, 800));
        var wide = Solve(state, width: 900);
        Assert.True(PaneDock.Fits(state, wide, PaneKind.Terminal, new PaneDrop(PaneDropKind.Body, Tasks)));
        Assert.False(PaneDock.Fits(state, wide, PaneKind.Terminal, PaneDrop.None));
        // Only a terminal in the drawer is added to the grid: the other moves never narrow, and CanOpenInGrid is about the drawer.
        var inGrid = state.MoveTerminalToGrid();
        Assert.False(PaneDock.CanOpenInGrid(inGrid, 900, 800));
        Assert.True(PaneDock.Fits(inGrid, Solve(inGrid, width: 900), PaneKind.Terminal, new PaneDrop(PaneDropKind.Drawer)));
    }

    [Fact]
    public void A_pane_never_targets_its_own_cell_and_a_closed_pane_lifts_nothing()
    {
        var state = Drawer(Tasks).MoveTerminalToGrid();
        var layout = Solve(state);
        var own = layout.BoundsOf(PaneKind.Terminal)!.Value;
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, Centre(own).X, Centre(own).Y, Header).IsNone);
        Assert.True(PaneDock.TargetAt(state, layout, Visus, 10, 10, Header).IsNone, "Visus is not open");
        var closed = PaneWorkspaceState.Default.Open(Tasks);
        Assert.True(PaneDock.TargetAt(closed, Solve(closed), PaneKind.Terminal, 1300, 300, Header).IsNone, "the terminal is closed");
    }

    [Fact]
    public void An_expanded_or_narrow_workspace_takes_no_drops()
    {
        var expanded = Drawer(Tasks, Visus).Expand(Tasks);
        var layout = Solve(expanded);
        Assert.NotNull(layout.Expanded);
        Assert.True(PaneDock.TargetAt(expanded, layout, PaneKind.Terminal, 900, 300, Header).IsNone);
        var narrow = Drawer(Tasks, Visus);
        var small = Solve(narrow, width: 700);
        Assert.Equal(PaneLayoutMode.Narrow, small.Mode);
        Assert.True(PaneDock.TargetAt(narrow, small, PaneKind.Terminal, 500, 300, Header).IsNone);
    }

    [Fact]
    public void A_side_chat_column_is_not_a_target()
    {
        var state = Drawer(Tasks).Open(PaneKind.SideChat);
        var layout = Solve(state, width: 1800);
        var side = layout.BoundsOf(PaneKind.SideChat)!.Value;
        Assert.True(layout.CellOf(PaneKind.SideChat)!.IsSideColumn);
        Assert.True(PaneDock.TargetAt(state, layout, PaneKind.Terminal, Centre(side).X, Centre(side).Y, Header).IsNone);
    }

    [Fact]
    public void Releasing_a_drawer_terminal_on_a_body_takes_that_slot_and_on_a_header_joins_it_as_a_tab()
    {
        var state = Drawer(Tasks, Visus);
        var body = PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Body, Visus));
        Assert.Equal(new[] { Tasks, PaneKind.Terminal, Visus }, body.GridPanes);
        Assert.False(body.IsTerminalInDrawer);
        var first = PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Body, Tasks));
        Assert.Equal(new[] { PaneKind.Terminal, Tasks, Visus }, first.GridPanes);
        var header = PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Header, Visus));
        Assert.Equal(new[] { Tasks, Visus, PaneKind.Terminal }, header.GridPanes);
        Assert.Equal(2, header.Order.Count);
        Assert.True(header.Order[1].IsGroup);
        Assert.Equal(PaneKind.Terminal, header.RecentTabs[0]);
        Assert.True(body.TerminalPrefersGrid && header.TerminalPrefersGrid);
    }

    [Fact]
    public void Releasing_a_grid_terminal_on_a_body_swaps_and_on_a_header_joins_and_a_tab_leaves_its_group()
    {
        var state = Drawer(Tasks, Visus, Tabula).MoveTerminalToGrid();
        var order = state.GridPanes.ToArray();
        var swapped = PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Body, Tasks));
        Assert.Equal(state.Swap(PaneKind.Terminal, Tasks).GridPanes, swapped.GridPanes);
        Assert.NotEqual(order, swapped.GridPanes.ToArray());
        var joined = PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Header, Visus));
        Assert.Equal(state.JoinAsTab(PaneKind.Terminal, Visus), joined);
        var left = PaneDock.Apply(joined, PaneKind.Terminal, new PaneDrop(PaneDropKind.Body, Tasks));
        Assert.All(left.Order, slot => Assert.False(slot.IsGroup));
        Assert.Equal(4, left.GridPanes.Count());
    }

    [Fact]
    public void A_drop_that_does_not_apply_leaves_the_state_alone()
    {
        var state = Drawer(Tasks);
        Assert.Equal(state, PaneDock.Apply(state, PaneKind.Terminal, PaneDrop.None));
        Assert.Equal(state, PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Body, Visus))); // Visus has no slot
        Assert.Equal(state, PaneDock.Apply(state, PaneKind.Terminal, new PaneDrop(PaneDropKind.Drawer))); // the terminal is already in the drawer
    }

    [Fact]
    public void Outlines_fall_where_the_cells_are()
    {
        var state = Drawer(Tasks, Visus);
        var layout = Solve(state);
        var tasks = layout.BoundsOf(Tasks)!.Value;
        Assert.Equal(tasks, PaneDock.Outline(state, layout, PaneKind.Terminal, new PaneDrop(PaneDropKind.Body, Tasks), 260, Header));
        Assert.Equal(tasks with { Height = Header }, PaneDock.Outline(state, layout, PaneKind.Terminal, new PaneDrop(PaneDropKind.Header, Tasks), 260, Header));
        Assert.Null(PaneDock.Outline(state, layout, PaneKind.Terminal, PaneDrop.None, 260, Header));
    }

    [Fact]
    public void Fuzz_every_release_writes_a_state_the_solver_can_draw_with_the_terminal_in_exactly_one_place()
    {
        var random = new Random(40404);
        var kinds = new[] { Tasks, PaneKind.Changes, Tabula, Visus, PaneKind.App, PaneKind.Transcript };
        for (var round = 0; round < 3000; round++)
        {
            var state = PaneWorkspaceState.Default;
            foreach (var kind in kinds.OrderBy(_ => random.Next()).Take(random.Next(0, 5))) state = state.Open(kind);
            if (random.Next(3) > 0) state = state.Open(PaneKind.Terminal);
            if (state.IsOpen(PaneKind.Terminal) && random.Next(3) == 0) state = state.MoveTerminalToGrid();
            if (random.Next(5) == 0) state = state.Open(PaneKind.SideChat);
            var width = random.Next(500, 2600);
            var height = random.Next(400, 1500);
            var layout = PaneLayout.Solve(state, width, height);
            for (var move = 0; move < 8; move++)
            {
                var x = random.NextDouble() * (width + 40) - 20;
                var y = random.NextDouble() * (height + 40) - 20;
                var drop = PaneDock.TargetAt(state, layout, PaneKind.Terminal, x, y, Header);
                var after = PaneDock.Apply(state, PaneKind.Terminal, drop).Normalized();
                if (drop.IsNone)
                {
                    Assert.Equal(state.Normalized(), after);
                    continue;
                }
                var places = (after.IsTerminalInDrawer ? 1 : 0) + (after.SlotIndexOf(PaneKind.Terminal) >= 0 ? 1 : 0);
                Assert.Equal(1, places);
                Assert.Equal(state.Normalized().Order.Sum(slot => slot.Panes.Count) + (state.Normalized().IsTerminalInDrawer ? 1 : 0), after.Order.Sum(slot => slot.Panes.Count) + (after.IsTerminalInDrawer ? 1 : 0));
                var drawn = PaneLayout.Solve(after, width, height);
                Assert.True(drawn.Chat.Width >= PaneKinds.ChatMinWidth - 1e-6 || drawn.Mode == PaneLayoutMode.Narrow);
                if (drop.Kind == PaneDropKind.Grid) Assert.Equal(PaneLayoutMode.Tiles, drawn.Mode);
                Assert.Equal(after, PaneDock.Apply(after, PaneKind.Terminal, PaneDrop.None));
            }
        }
    }
}
