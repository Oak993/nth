using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 3.1 "Terminal ... can be dragged into the grid", 3.6 "The terminal can be dragged out of its drawer into the grid and back"
// (web surfaces W4; ED-NEW-web-w4-1), on W3b's drop targets: the terminal lifted from its drawer lands on a cell's body (taking that
// slot's place), on a cell's header (joining it as a tab) or, with no cell in the grid, opens the grid; lifted from a cell it can go
// back to the drawer. Every target carries the workspace its drop produces and leaves the workspace tiled.
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class PaneRedockTerminalTests
{
    private static readonly PaneKind Tasks = PaneKind.BackgroundTasks, Visus = PaneKind.Browser, Tabula = PaneKind.Artifacts;

    private static PaneWorkspaceState Drawer(params PaneKind[] panes) =>
        panes.Aggregate(PaneWorkspaceState.Default.Open(PaneKind.Terminal), (state, kind) => state.Open(kind));

    private static PaneLayoutResult Solve(PaneWorkspaceState state, double width = 1400, double height = 800) => PaneLayout.Solve(state, width, height);

    private static (double X, double Y) Centre(PaneRect rect) => (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    [Fact]
    public void A_drawer_terminal_can_land_on_every_other_cells_body_or_header()
    {
        var state = Drawer(Tasks, Visus);
        var layout = Solve(state);
        var targets = PaneRedock.DrawerTargets(state, layout);
        Assert.Equal(4, targets.Count);
        Assert.Equal([PaneDropKind.Header, PaneDropKind.Header, PaneDropKind.Body, PaneDropKind.Body], targets.Select(target => target.Kind).ToArray()); // headers first, as in W3b
        var tasksBody = targets.Single(target => target.Kind == PaneDropKind.Body && target.Target == Tasks);
        var visusBody = targets.Single(target => target.Kind == PaneDropKind.Body && target.Target == Visus);
        var visusHeader = targets.Single(target => target.Kind == PaneDropKind.Header && target.Target == Visus);
        // A body takes that slot's place, the rest later; a header joins as a tab and shows.
        Assert.Equal(new[] { PaneKind.Terminal, Tasks, Visus }, tasksBody.Result.GridPanes);
        Assert.Equal(new[] { Tasks, PaneKind.Terminal, Visus }, visusBody.Result.GridPanes);
        Assert.Equal(new[] { Tasks, Visus, PaneKind.Terminal }, visusHeader.Result.GridPanes);
        Assert.True(visusHeader.Result.Order[1].IsGroup);
        Assert.Equal(PaneKind.Terminal, visusHeader.Result.RecentTabs[0]);
        Assert.All(targets, target =>
        {
            Assert.False(target.Result.IsTerminalInDrawer);
            Assert.True(target.Result.TerminalPrefersGrid);
            Assert.Equal(PaneLayoutMode.Tiles, Solve(target.Result).Mode);
        });
        // The rectangles are the cell's: a 36 DIP header over the body.
        var visus = layout.BoundsOf(Visus)!.Value;
        Assert.Equal(visus with { Height = PaneKinds.HeaderHeight }, visusHeader.Bounds);
        Assert.Equal(visus.Y + PaneKinds.HeaderHeight, visusBody.Bounds.Y);
        Assert.Equal(visus.Height - PaneKinds.HeaderHeight, visusBody.Bounds.Height, 6);
        // And a hit test prefers a header where one covers a body.
        var (x, y) = Centre(visusHeader.Bounds);
        Assert.Same(visusHeader, PaneRedock.Hit(targets, x, y));
        (x, y) = Centre(visusBody.Bounds);
        Assert.Same(visusBody, PaneRedock.Hit(targets, x, y));
        Assert.Null(PaneRedock.Hit(targets, layout.Chat.X + 5, layout.Chat.Y + 5));
    }

    [Fact]
    public void With_no_cell_in_the_grid_the_terminal_opens_it_beside_a_readable_chat()
    {
        var state = Drawer();
        var layout = Solve(state);
        var target = Assert.Single(PaneRedock.DrawerTargets(state, layout));
        Assert.Equal(PaneDropKind.Grid, target.Kind);
        Assert.Equal(PaneKind.Terminal, target.Target);
        Assert.True(target.Bounds.X >= PaneKinds.ChatMinWidth);
        Assert.True(target.Bounds.Width >= PaneKinds.Size(PaneKind.Terminal).MinWidth - 1e-6);
        Assert.Equal(new[] { PaneKind.Terminal }, target.Result.GridPanes);
        Assert.False(target.Result.IsTerminalInDrawer);
        Assert.Equal(PaneLayout.Solve(target.Result, layout.Width, layout.Height).BoundsOf(PaneKind.Terminal), target.Bounds);
        // Too narrow for a readable chat and the terminal side by side: no grid to open.
        Assert.Empty(PaneRedock.DrawerTargets(state, Solve(state, 600)));
        Assert.False(PaneRedock.CanOpenInGrid(state, 600, 800));
        Assert.True(PaneRedock.CanOpenInGrid(state, 800, 800));
    }

    [Fact]
    public void A_target_that_would_turn_the_workspace_into_narrow_tabs_is_left_out()
    {
        // Tasks alone is tiled from 688 DIP (400 + 8 + 280); with the terminal (320) beside it, from 728.
        var state = Drawer(Tasks);
        var tight = Solve(state, 710);
        Assert.Equal(PaneLayoutMode.Tiles, tight.Mode);
        Assert.Empty(PaneRedock.DrawerTargets(state, tight));
        Assert.False(PaneRedock.CanOpenInGrid(state, 710, 800));
        Assert.True(PaneRedock.CanOpenInGrid(state, 740, 800));
        Assert.Equal(2, PaneRedock.DrawerTargets(state, Solve(state, 900)).Count);
    }

    [Fact]
    public void Only_a_tiled_unexpanded_workspace_with_the_terminal_in_its_drawer_lifts_it()
    {
        var state = Drawer(Tasks, Visus);
        Assert.True(PaneRedock.CanLiftFromDrawer(state, Solve(state)));
        Assert.False(PaneRedock.CanLiftFromDrawer(state.Expand(Tasks), Solve(state.Expand(Tasks))));
        Assert.Empty(PaneRedock.DrawerTargets(state.Expand(Tasks), Solve(state.Expand(Tasks))));
        Assert.False(PaneRedock.CanLiftFromDrawer(state, Solve(state, 700)));
        var inGrid = state.MoveTerminalToGrid();
        Assert.False(PaneRedock.CanLiftFromDrawer(inGrid, Solve(inGrid)));
        Assert.Empty(PaneRedock.DrawerTargets(inGrid, Solve(inGrid)));
        var closed = PaneWorkspaceState.Default.Open(Tasks);
        Assert.False(PaneRedock.CanLiftFromDrawer(closed, Solve(closed)));
        Assert.False(PaneRedock.CanOpenInGrid(inGrid, 1400, 800));
    }

    [Fact]
    public void Side_chat_is_not_a_target()
    {
        var state = Drawer(Tasks).Open(PaneKind.SideChat);
        var layout = Solve(state, 1800);
        Assert.True(layout.CellOf(PaneKind.SideChat)!.IsSideColumn);
        Assert.DoesNotContain(PaneRedock.DrawerTargets(state, layout), target => target.Target == PaneKind.SideChat);
    }

    [Fact]
    public void A_grid_terminal_has_the_drawer_as_one_more_target_at_the_chats_foot_and_no_other_pane_does()
    {
        var state = Drawer(Tasks).MoveTerminalToGrid();
        var layout = Solve(state);
        var targets = PaneRedock.Targets(state, layout, PaneKind.Terminal, drawerHeight: 300);
        var drawer = Assert.Single(targets, target => target.Kind == PaneDropKind.Drawer);
        var chat = layout.Chat;
        Assert.Equal(PaneKind.Terminal, drawer.Target);
        Assert.Equal(new PaneRect(chat.X, chat.Bottom - 300, chat.Width, 300), drawer.Bounds);
        Assert.True(drawer.Result.IsTerminalInDrawer);
        Assert.DoesNotContain(PaneKind.Terminal, drawer.Result.GridPanes);
        Assert.False(drawer.Result.TerminalPrefersGrid);
        Assert.Contains(targets, target => target.Kind == PaneDropKind.Body && target.Target == Tasks); // W3b's own targets are still there
        var (x, y) = Centre(drawer.Bounds);
        Assert.Same(drawer, PaneRedock.Hit(targets, x, y));
        Assert.Null(PaneRedock.Hit(targets, chat.X + 40, chat.Y + 40)); // the chat's upper part is nothing
        Assert.DoesNotContain(PaneRedock.Targets(state, layout, Tasks), target => target.Kind == PaneDropKind.Drawer);
        // The default height is the drawer's own.
        Assert.Equal(PaneRedock.DefaultDrawerHeight, PaneRedock.Targets(state, layout, PaneKind.Terminal).Single(target => target.Kind == PaneDropKind.Drawer).Bounds.Height);
    }

    [Fact]
    public void The_drawer_outline_keeps_the_drawers_minimum_and_leaves_the_chat_its_room()
    {
        var layout = Solve(Drawer(Tasks).MoveTerminalToGrid());
        var chat = layout.Chat;
        Assert.Equal(PaneRedock.DrawerMinimumHeight, PaneRedock.DrawerBounds(layout, 20).Height);
        Assert.Equal(chat.Height - PaneRedock.ChatReserveHeight, PaneRedock.DrawerBounds(layout, 9000).Height, 6);
        Assert.Equal(260, PaneRedock.DrawerBounds(layout, 260).Height);
        Assert.Equal(chat.Bottom, PaneRedock.DrawerBounds(layout, 260).Bottom, 6);
        var low = Solve(Drawer(Tasks).MoveTerminalToGrid(), 1400, 300);
        Assert.True(PaneRedock.DrawerBounds(low, 260).Height <= low.Chat.Height);
    }

    [Fact]
    public void Fuzz_every_target_of_every_workspace_writes_a_state_the_solver_can_draw_with_the_terminal_in_exactly_one_place()
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
            var targets = state.Normalized().IsTerminalInDrawer
                ? PaneRedock.DrawerTargets(state, layout)
                : PaneRedock.Targets(state, layout, PaneKind.Terminal, random.Next(20, 900));
            foreach (var target in targets)
            {
                var after = target.Result;
                Assert.Equal(after.Normalized(), after);
                var places = (after.IsTerminalInDrawer ? 1 : 0) + (after.SlotIndexOf(PaneKind.Terminal) >= 0 ? 1 : 0);
                Assert.Equal(1, places);
                var drawn = PaneLayout.Solve(after, width, height);
                if (target.Kind != PaneDropKind.Drawer) Assert.Equal(PaneLayoutMode.Tiles, drawn.Mode);
                else Assert.True(target.Bounds.Height <= layout.Chat.Height + 1e-6 && target.Bounds.Y >= layout.Chat.Y - 1e-6);
                Assert.True(drawn.Chat.Width >= PaneKinds.ChatMinWidth - 1e-6 || drawn.Mode == PaneLayoutMode.Narrow);
                Assert.Equal(state.Normalized().Order.Sum(slot => slot.Panes.Count) + (state.Normalized().IsTerminalInDrawer ? 1 : 0),
                    after.Order.Sum(slot => slot.Panes.Count) + (after.IsTerminalInDrawer ? 1 : 0));
            }
        }
    }
}
