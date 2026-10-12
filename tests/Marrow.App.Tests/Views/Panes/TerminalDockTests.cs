using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.UI.Panes;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W4 (work/design/web-surfaces/DESIGN.md 3.1 "Terminal ... stays the chat's drawer by default; can be dragged
// into the grid", 3.6 "The terminal can be dragged out of its drawer into the grid and back"; 14 W4 "terminal drags between drawer
// and grid"). W3b's header re-dock lifts a card of the grid; the terminal's card has one more outline in it, the drawer at the chat's
// foot, and a release there sends it back. The drawer lifts as a ghost over the same machinery: it follows the pointer 1:1, every cell
// and the empty grid are outlined, and a release springs the ghost into the cell the real pane fades in at.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class TerminalDockTests(HeadlessSession headless)
{
    private static PaneWorkspaceState InGrid(params PaneKind[] panes) =>
        panes.Aggregate(PaneWorkspaceState.Default with { TerminalPrefersGrid = true }, (state, kind) => state.Open(kind));

    private static PaneWorkspaceState InDrawer(params PaneKind[] panes) =>
        panes.Aggregate(PaneWorkspaceState.Default.Open(PaneKind.Terminal), (state, kind) => state.Open(kind));

    // Layout rounds an edge to a device pixel, so a drawn coordinate is within a pixel of the number it was computed from.
    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1, $"expected {expected}, got {actual}");

    private static Point Centre(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    // A target's centre in window coordinates: its rectangle is the solver's, under the title band.
    private static Point Over(PaneDropVisual visual) => new(visual.Target.Bounds.X + visual.Target.Bounds.Width / 2, PaneWorkspace.TopBand + visual.Target.Bounds.Y + visual.Target.Bounds.Height / 2);

    private static void Settled(PaneWorkspaceRig rig)
    {
        for (var i = 0; i < 60 && rig.Workspace.IsMoving; i++) rig.Frame();
    }

    private static Point LiftTerminal(PaneWorkspaceRig rig)
    {
        var press = rig.HeaderPoint(PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(6, 0));
        Assert.True(rig.Workspace.IsRedocking);
        Settled(rig);
        return press;
    }

    private static Point LiftDrawer(PaneWorkspaceRig rig)
    {
        var from = Centre(rig.BoundsOf(rig.ChatColumn.DrawerHeader));
        rig.Down(from);
        rig.Move(from + new Vector(2, 1));
        Assert.False(rig.Workspace.IsRedocking, "2 DIP is not a lift");
        rig.Move(from + new Vector(10, -30));
        Assert.True(rig.Workspace.IsRedocking);
        Settled(rig);
        return from;
    }

    private static PaneWorkspaceRig WithDrawer(PaneWorkspaceState state, int width = 1400)
    {
        var rig = new PaneWorkspaceRig(width: width, state: state);
        rig.ChatColumn.Drawer.IsVisible = true;
        rig.Layout();
        return rig;
    }

    [Fact]
    public void A_grid_terminals_header_drag_outlines_the_drawer_at_the_chats_foot_and_a_release_there_sends_it_back() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        rig.Workspace.DrawerHeight = () => 300;
        LiftTerminal(rig);
        var drawer = Assert.Single(rig.Workspace.DropVisuals, visual => visual.Target.Kind == PaneDropKind.Drawer);
        Assert.Equal(PaneKind.Terminal, drawer.Target.Target);
        var chat = rig.Workspace.ChatBounds;
        Near(chat.Width, drawer.Target.Bounds.Width);
        Near(300, drawer.Target.Bounds.Height);
        Near(rig.Expected().Chat.Bottom, drawer.Target.Bounds.Bottom);
        Assert.Contains(rig.Workspace.DropVisuals, visual => visual.Target.Kind == PaneDropKind.Body && visual.Target.Target == PaneKind.BackgroundTasks);

        var over = Over(drawer);
        rig.Move(over);
        Assert.Equal(PaneDropKind.Drawer, rig.Workspace.HotDrop!.Target.Kind);
        Settled(rig);
        Near(1, drawer.Hot);
        rig.Up(over);
        rig.Settle();

        var after = Assert.Single(rig.Changes);
        Assert.True(after.IsTerminalInDrawer);
        Assert.DoesNotContain(PaneKind.Terminal, after.GridPanes);
        Assert.False(after.TerminalPrefersGrid);
        Assert.Null(rig.Workspace.CardOf(PaneKind.Terminal));
        Assert.False(rig.Workspace.IsRedocking);
        Assert.Empty(rig.Workspace.DropVisuals);
    });

    [Fact]
    public void The_drawer_outline_keeps_its_own_minimum_and_leaves_the_chat_room_above_it() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var chat = rig.Expected().Chat;
        Assert.Equal(PaneRedock.DrawerMinimumHeight, PaneRedock.DrawerBounds(rig.Expected(), 20).Height);
        Assert.Equal(chat.Height - PaneRedock.ChatReserveHeight, PaneRedock.DrawerBounds(rig.Expected(), 9000).Height, 6);
        Assert.Equal(260, PaneRedock.DrawerBounds(rig.Expected(), PaneRedock.DefaultDrawerHeight).Height);
        Assert.Equal(chat.Bottom, PaneRedock.DrawerBounds(rig.Expected(), 260).Bottom, 6);
    });

    [Fact]
    public void A_drawers_header_lifts_a_ghost_of_the_pane_that_follows_the_pointer_one_to_one() => headless.OnUiThread(() =>
    {
        using var rig = WithDrawer(InDrawer(PaneKind.BackgroundTasks));
        using var gesture = DrawerLiftGesture.Attach(rig.ChatColumn.DrawerHeader, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        var drawer = rig.BoundsOf(rig.ChatColumn.Drawer);
        var from = LiftDrawer(rig);
        var ghost = Assert.IsType<PaneCard>(rig.Workspace.DrawerGhost);
        Assert.Equal(new Vector(10, -30), ghost.Offset);
        Near(drawer.Width, ghost.Bounds.Width);
        Near(drawer.Height, ghost.Bounds.Height);
        Near(drawer.X, ghost.Bounds.X);
        Near(drawer.Y, ghost.Bounds.Y);
        Assert.False(ghost.IsHitTestVisible);
        Assert.True(ghost.IsLiftedLook);
        Assert.Equal(40, ghost.ZIndex);
        Assert.Equal(PaneRedock.LiftScale, ghost.Scale, 2);
        foreach (var delta in new[] { new Vector(-120, 80), new Vector(300, 5), new Vector(40, -20) })
        {
            rig.Move(from + delta);
            Assert.Equal(delta, ghost.Offset);
        }
        Assert.False(rig.Workspace.SplitterOf(PaneGutterKeys.Chat).IsHitTestVisible, "no splitter takes the pointer while a pane is held");
        rig.Up(from + new Vector(40, -20));
        rig.Settle();
        Assert.False(rig.Workspace.IsRedocking);
        Assert.Null(rig.Workspace.DrawerGhost);
        Assert.Empty(rig.Changes);
    });

    [Fact]
    public void Every_cell_is_outlined_for_a_drawer_lift_and_a_release_over_a_body_takes_that_slot() => headless.OnUiThread(() =>
    {
        using var rig = WithDrawer(InDrawer(PaneKind.BackgroundTasks, PaneKind.Browser));
        using var gesture = DrawerLiftGesture.Attach(rig.ChatColumn.DrawerHeader, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        LiftDrawer(rig);
        var visuals = rig.Workspace.DropVisuals;
        Assert.Equal(4, visuals.Count); // Tasks and Visus, each a body and a header
        Assert.DoesNotContain(visuals, visual => visual.Target.Kind is PaneDropKind.Grid or PaneDropKind.Drawer);
        var visus = visuals.Single(visual => visual.Target.Kind == PaneDropKind.Body && visual.Target.Target == PaneKind.Browser);
        rig.Move(Over(visus));
        Assert.Equal((PaneDropKind.Body, PaneKind.Browser), (rig.Workspace.HotDrop!.Target.Kind, rig.Workspace.HotDrop.Target.Target));
        Settled(rig);
        Near(1, rig.Workspace.HotDrop.Hot);
        rig.Up(Over(visus));
        var after = Assert.Single(rig.Changes);
        Assert.Equal(new[] { PaneKind.BackgroundTasks, PaneKind.Terminal, PaneKind.Browser }, after.GridPanes);
        Assert.False(after.IsTerminalInDrawer);
        Assert.True(after.TerminalPrefersGrid);
        Assert.NotNull(rig.Workspace.CardOf(PaneKind.Terminal));
        Assert.NotNull(rig.Workspace.DrawerGhost);
        rig.Settle();
        Assert.Null(rig.Workspace.DrawerGhost);
        Assert.Equal(1, rig.Card(PaneKind.Terminal).Opacity, 3);
        var expected = rig.ExpectedBounds(PaneKind.Terminal);
        Near(expected.X, rig.Card(PaneKind.Terminal).Bounds.X);
        Near(expected.Y, rig.Card(PaneKind.Terminal).Bounds.Y);
    });

    [Fact]
    public void A_release_over_a_header_joins_that_cell_as_a_tab() => headless.OnUiThread(() =>
    {
        using var rig = WithDrawer(InDrawer(PaneKind.BackgroundTasks, PaneKind.Browser));
        using var gesture = DrawerLiftGesture.Attach(rig.ChatColumn.DrawerHeader, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        LiftDrawer(rig);
        var header = rig.Workspace.DropVisuals.Single(visual => visual.Target.Kind == PaneDropKind.Header && visual.Target.Target == PaneKind.Browser);
        var over = new Point(header.Target.Bounds.X + header.Target.Bounds.Width / 2, PaneWorkspace.TopBand + header.Target.Bounds.Y + 12);
        rig.Move(over);
        Assert.Equal(PaneDropKind.Header, rig.Workspace.HotDrop!.Target.Kind);
        rig.Up(over);
        rig.Settle();
        var after = Assert.Single(rig.Changes);
        var slot = after.Order.Single(candidate => candidate.Contains(PaneKind.Terminal));
        Assert.True(slot.IsGroup);
        Assert.Equal(new[] { PaneKind.Browser, PaneKind.Terminal }, slot.Panes);
        Assert.Equal(2, rig.Card(PaneKind.Terminal).Header.TabStrip.Children.Count);
    });

    [Fact]
    public void With_no_cell_in_the_grid_the_right_of_the_workspace_opens_it_and_the_ghost_springs_there() => headless.OnUiThread(() =>
    {
        using var rig = WithDrawer(InDrawer());
        using var gesture = DrawerLiftGesture.Attach(rig.ChatColumn.DrawerHeader, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        LiftDrawer(rig);
        var grid = Assert.Single(rig.Workspace.DropVisuals);
        Assert.Equal(PaneDropKind.Grid, grid.Target.Kind);
        Assert.True(grid.Target.Bounds.X >= PaneKinds.ChatMinWidth, "beside a readable chat");
        var over = Over(grid);
        rig.Move(over);
        Assert.Equal(PaneDropKind.Grid, rig.Workspace.HotDrop!.Target.Kind);
        rig.Up(over);
        var after = Assert.Single(rig.Changes);
        Assert.False(after.IsTerminalInDrawer);
        Assert.Equal(new[] { PaneKind.Terminal }, after.GridPanes);

        // The ghost springs into the cell while the real pane fades in there, then the ghost goes.
        var card = rig.Card(PaneKind.Terminal);
        Assert.Equal(0, card.Opacity, 2);
        var ghost = Assert.IsType<PaneCard>(rig.Workspace.DrawerGhost);
        var seen = new List<double>();
        while ((rig.Workspace.IsMoving || rig.Workspace.IsRedocking) && seen.Count < 300)
        {
            rig.Frame();
            seen.Add(ghost.Opacity);
        }
        Assert.InRange(seen.Count, 8, 120);
        Assert.Null(rig.Workspace.DrawerGhost);
        Assert.Equal(1, card.Opacity, 3);
        Assert.Equal(0, seen[^1], 2);
        Near(rig.ExpectedBounds(PaneKind.Terminal).X, card.Bounds.X);
        Assert.Empty(rig.Workspace.DropVisuals);
    });

    [Fact]
    public void A_release_over_nothing_or_Esc_springs_the_ghost_home_and_changes_nothing() => headless.OnUiThread(() =>
    {
        using var rig = WithDrawer(InDrawer(PaneKind.BackgroundTasks));
        using var gesture = DrawerLiftGesture.Attach(rig.ChatColumn.DrawerHeader, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        var from = LiftDrawer(rig);
        Assert.NotNull(rig.Workspace.DrawerGhost);
        rig.Move(from + new Vector(30, -60)); // in the chat column: no target
        Assert.Null(rig.Workspace.HotDrop);
        rig.Up(from + new Vector(30, -60));
        Assert.Empty(rig.Changes);
        rig.Settle();
        Assert.Null(rig.Workspace.DrawerGhost);
        Assert.True(rig.Workspace.State.IsTerminalInDrawer);

        LiftDrawer(rig);
        Assert.NotNull(rig.Workspace.DrawerGhost);
        rig.Key(Key.Escape);
        rig.Settle();
        Assert.False(rig.Workspace.IsRedocking);
        Assert.Null(rig.Workspace.DrawerGhost);
        Assert.Empty(rig.Changes);
    });

    [Fact]
    public void A_header_button_a_narrow_workspace_and_an_expanded_pane_never_lift_the_drawer() => headless.OnUiThread(() =>
    {
        using var rig = WithDrawer(InDrawer(PaneKind.BackgroundTasks));
        using var gesture = DrawerLiftGesture.Attach(rig.ChatColumn.DrawerHeader, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        // A button in the header takes its own press.
        var button = new Button { Width = 30, Height = 20, Content = "x", Background = Avalonia.Media.Brushes.Gray, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left }; // a background, so the press lands on the button
        rig.ChatColumn.DrawerHeader.Child = button;
        rig.Layout();
        var onButton = Centre(rig.BoundsOf(button));
        rig.Down(onButton);
        rig.Move(onButton + new Vector(30, -5));
        rig.Move(onButton + new Vector(60, -5));
        Assert.False(rig.Workspace.IsRedocking);
        rig.Up(onButton + new Vector(60, -5));
        rig.ChatColumn.DrawerHeader.Child = null;

        // An expanded pane has the whole workspace: nowhere to drop.
        rig.Workspace.ToggleExpanded(PaneKind.BackgroundTasks);
        rig.Settle();
        var header = rig.BoundsOf(rig.ChatColumn.DrawerHeader);
        var empty = new Point(header.X + 200, header.Y + 10);
        rig.Down(empty);
        rig.Move(empty + new Vector(30, 0));
        rig.Move(empty + new Vector(60, 0));
        Assert.False(rig.Workspace.IsRedocking, "an expanded workspace takes no drop");
        rig.Up(empty);
        rig.Workspace.RestoreExpanded();
        rig.Settle();
        rig.Changes.Clear(); // the expand and the restore were the test's own

        // A window tiled but too tight for the terminal beside the chat (Tasks alone tiles from 688, with the terminal from 728) has no place
        // to drop it: the lift is refused. So is one narrow enough to be tabs.
        foreach (var width in new[] { 710, 600 })
        {
            rig.Window.Width = width;
            rig.Layout();
            rig.Settle();
            var tight = Centre(rig.BoundsOf(rig.ChatColumn.DrawerHeader));
            rig.Down(tight);
            rig.Move(tight + new Vector(30, 0));
            rig.Move(tight + new Vector(60, 0));
            Assert.False(rig.Workspace.IsRedocking, $"{width} DIP");
            rig.Up(tight);
        }
        Assert.Empty(rig.Changes);
    });

    [Fact]
    public void A_cell_that_would_make_the_workspace_narrow_tabs_is_no_target_for_the_drawer() => headless.OnUiThread(() =>
    {
        // Tasks alone tiles from 688 DIP; the terminal beside it needs 728. At 710 the drawer has no cell to land on.
        using var rig = new PaneWorkspaceRig(width: 710, state: InDrawer(PaneKind.BackgroundTasks));
        Assert.Equal(PaneLayoutMode.Tiles, rig.Workspace.Layout!.Mode);
        Assert.False(rig.Workspace.CanDockTerminalToGrid(rig.Workspace.State));
        Assert.Empty(PaneRedock.DrawerTargets(rig.Workspace.State, rig.Workspace.Layout!));
        rig.Window.Width = 900;
        rig.Layout();
        rig.Settle();
        Assert.True(rig.Workspace.CanDockTerminalToGrid(rig.Workspace.State));
        Assert.Equal(2, PaneRedock.DrawerTargets(rig.Workspace.State, rig.Workspace.Layout!).Count); // a body and a header of Tasks
    });

    [Fact]
    public void A_thread_switch_ends_a_drawer_lift_where_it_is() => headless.OnUiThread(() =>
    {
        using var rig = WithDrawer(InDrawer(PaneKind.BackgroundTasks));
        using var gesture = DrawerLiftGesture.Attach(rig.ChatColumn.DrawerHeader, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        LiftDrawer(rig);
        Assert.NotNull(rig.Workspace.DrawerGhost);
        rig.Workspace.Reload(_ => null, PaneWorkspaceState.Default);
        rig.Settle();
        Assert.False(rig.Workspace.IsRedocking);
        Assert.Null(rig.Workspace.DrawerGhost);
        Assert.Empty(rig.Workspace.DropVisuals);
    });
}
