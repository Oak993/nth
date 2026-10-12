using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W4 (work/design/web-surfaces/DESIGN.md 3.1 "Terminal ... stays the chat's drawer by default; can be dragged
// into the grid", 3.6 "Press on a header's empty area and move 4 DIP: the pane lifts (scale 1.02, deeper shadow). Drop targets
// appear as outlines ... The target under the pointer fills softly. Release: the solver runs with the new order and everything
// springs into place. Esc or releasing over nothing springs the pane home ... The terminal can be dragged out of its drawer into
// the grid and back"; 14 W4 "terminal drags between drawer and grid"): a header press lifts the terminal after 4 DIP, it follows
// the pointer 1:1, every place it can land is outlined and the one under the pointer is lit, and a release writes the state
// PaneDock names and springs from where the pane was dropped.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class TerminalDockTests(HeadlessSession headless)
{
    // Layout rounds an edge to a device pixel, so a drawn coordinate is within a pixel of the number it was computed from.
    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1, $"expected {expected}, got {actual}");

    private static PaneWorkspaceState InGrid(params PaneKind[] panes) =>
        panes.Aggregate(PaneWorkspaceState.Default with { TerminalPrefersGrid = true }, (state, kind) => state.Open(kind));

    private static PaneWorkspaceState InDrawer(params PaneKind[] panes) =>
        panes.Aggregate(PaneWorkspaceState.Default.Open(PaneKind.Terminal), (state, kind) => state.Open(kind));

    private static Point HeaderPoint(PaneWorkspaceRig rig, PaneKind kind)
    {
        var header = rig.BoundsOf(rig.Card(kind).Header);
        return new Point(header.X + header.Width * 0.5, header.Y + header.Height / 2);
    }

    private static Point Centre(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static (Rect Tasks, Rect Terminal) TwoCells(PaneWorkspaceRig rig) =>
        (rig.Workspace.BoundsOf(PaneKind.BackgroundTasks)!.Value, rig.Workspace.BoundsOf(PaneKind.Terminal)!.Value);

    [Fact]
    public void A_terminal_header_lifts_the_card_after_4_dip_and_it_follows_the_pointer_one_to_one() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var card = rig.Card(PaneKind.Terminal);
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(3, 0));
        Assert.False(rig.Workspace.IsDocking, "3 DIP is not a drag");
        rig.Move(press + new Vector(0, 3.9));
        Assert.False(rig.Workspace.IsDocking, "4 DIP is the slop");
        rig.Move(press + new Vector(6, 2));
        Assert.True(rig.Workspace.IsDocking);
        Assert.Equal(new Vector(6, 2), card.Shift);
        Assert.True(card.IsLiftedLook);
        Assert.Equal(40, card.ZIndex);

        // 1:1: the card is at the pointer on every move, with no frame run between them (no spring lags the pointer).
        foreach (var delta in new[] { new Vector(-120, 80), new Vector(-300, 5), new Vector(40, -20) })
        {
            rig.Move(press + delta);
            Assert.Equal(delta, card.Shift);
        }
        // The lift itself springs: scale 1 to 1.02.
        rig.Settle();
        Assert.Equal(PaneWorkspace.LiftScale, card.Scale, 3);
        Assert.Equal(new Vector(40, -20), card.Shift);
        rig.Up(press + new Vector(40, -20));
        rig.Settle();
        Assert.False(rig.Workspace.IsDocking);
    });

    [Fact]
    public void Every_other_cell_and_the_drawer_are_outlined_and_only_the_one_under_the_pointer_is_lit() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var (tasks, _) = TwoCells(rig);
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(10, 10));
        Assert.True(rig.Workspace.IsDocking);
        var outlines = rig.Workspace.DockOutlines;
        Assert.Equal(2, outlines.Count);
        Assert.Contains(outlines, outline => outline.Kind == PaneDropKind.Body && outline.Pane == PaneKind.BackgroundTasks);
        Assert.Contains(outlines, outline => outline.Kind == PaneDropKind.Drawer);
        Assert.All(outlines, outline => Assert.Equal(0, outline.Level, 3));

        rig.Move(Centre(tasks));
        Assert.Equal(new PaneDrop(PaneDropKind.Body, PaneKind.BackgroundTasks), rig.Workspace.DockDrop);
        rig.Settle();
        Assert.Equal(1, rig.Workspace.DockOutlines.Single(outline => outline.Kind == PaneDropKind.Body).Level, 3);
        Assert.Equal(0, rig.Workspace.DockOutlines.Single(outline => outline.Kind == PaneDropKind.Drawer).Level, 3);

        // The header strip is its own target (join as a tab).
        rig.Move(new Point(tasks.X + tasks.Width / 2, tasks.Y + 10));
        Assert.Equal(new PaneDrop(PaneDropKind.Header, PaneKind.BackgroundTasks), rig.Workspace.DockDrop);

        var inChat = new Point(tasks.X - 40, tasks.Y + 100);
        rig.Move(inChat);
        Assert.True(rig.Workspace.DockDrop.IsNone, "the chat column's upper half is nothing");
        rig.Settle();
        Assert.All(rig.Workspace.DockOutlines, outline => Assert.Equal(0, outline.Level, 3));
        rig.Up(inChat);
        rig.Settle();
        Assert.Empty(rig.Workspace.DockOutlines);
    });

    [Fact]
    public void Releasing_over_another_cell_swaps_and_the_card_springs_from_where_it_was_dropped() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(width: 2000, height: 700, state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var before = rig.Workspace.State;
        var (tasks, terminal) = TwoCells(rig);
        Assert.True(terminal.X > tasks.X + 100, "a window this wide tiles the two cells side by side");
        var card = rig.Card(PaneKind.Terminal);
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(-60, 30));
        var drop = new Point(tasks.Right - 30, tasks.Y + tasks.Height / 2); // the cell's right part: the card is then well clear of its new place
        rig.Move(drop);
        var dropped = card.Bounds.TopLeft + card.Shift; // where the lifted card is drawn: its cell's corner and how far it was carried
        rig.Up(drop);

        var after = Assert.Single(rig.Changes);
        Assert.Equal(before.Swap(PaneKind.Terminal, PaneKind.BackgroundTasks).GridPanes, after.GridPanes);
        Assert.Equal(new[] { PaneKind.Terminal, PaneKind.BackgroundTasks }, after.GridPanes);
        // The first drawn frame of the new layout is where the card was dropped, not its new place: the move springs from there.
        Assert.True(Math.Abs(dropped.X - card.Bounds.X) <= 1 && Math.Abs(dropped.Y - card.Bounds.Y) <= 1, $"drawn at {card.Bounds.TopLeft}, dropped at {dropped}"); // layout rounds to a device pixel
        var home = rig.ExpectedBounds(PaneKind.Terminal);
        Assert.True(Math.Abs(card.Bounds.X - home.X) > 100, "it has still to travel");
        Assert.True(rig.Workspace.IsMoving);
        var seen = 0;
        while (rig.Workspace.IsMoving && seen < 200)
        {
            rig.Frame();
            seen++;
        }
        Assert.InRange(seen, 8, 80);
        Assert.False(rig.Workspace.IsDocking);
        Assert.Equal(default, card.Shift);
        Assert.False(card.IsLiftedLook);
        Assert.Equal(1, card.Scale, 3);
        Near(home.X, card.Bounds.X);
        Near(tasks.X, card.Bounds.X); // the terminal now holds the cell Tasks had
        Near(tasks.Y, card.Bounds.Y);
    });

    [Fact]
    public void Releasing_on_a_header_joins_that_cell_as_a_tab() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var (tasks, _) = TwoCells(rig);
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(-30, 0));
        var over = new Point(tasks.X + tasks.Width / 2, tasks.Y + 12);
        rig.Move(over);
        rig.Up(over);
        rig.Settle();
        var after = Assert.Single(rig.Changes);
        var slot = Assert.Single(after.Order);
        Assert.True(slot.IsGroup);
        Assert.Equal(new[] { PaneKind.BackgroundTasks, PaneKind.Terminal }, slot.Panes);
        Assert.Equal(2, rig.Card(PaneKind.Terminal).Header.TabStrip.Children.Count);
    });

    [Fact]
    public void Releasing_over_the_chats_lower_half_sends_the_terminal_back_to_the_drawer() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(-5, -5));
        var chat = rig.Workspace.ChatBounds;
        var over = new Point(chat.X + 120, chat.Bottom - 30);
        rig.Move(over);
        Assert.Equal(new PaneDrop(PaneDropKind.Drawer), rig.Workspace.DockDrop);
        rig.Settle();
        Assert.Equal(1, rig.Workspace.DockOutlines.Single(outline => outline.Kind == PaneDropKind.Drawer).Level, 3);
        rig.Up(over);
        rig.Settle();
        var after = Assert.Single(rig.Changes);
        Assert.True(after.IsTerminalInDrawer);
        Assert.DoesNotContain(PaneKind.Terminal, after.GridPanes);
        Assert.False(after.TerminalPrefersGrid);
        Assert.Null(rig.Workspace.CardOf(PaneKind.Terminal));
        Assert.False(rig.Workspace.IsDocking);
    });

    [Fact]
    public void Releasing_over_nothing_springs_the_card_home_and_reports_no_change() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var card = rig.Card(PaneKind.Terminal);
        var home = card.Bounds;
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(-200, 60));
        Assert.Equal(new Vector(-200, 60), card.Shift);
        rig.Up(press + new Vector(-200, 60));
        Assert.Empty(rig.Changes);
        Assert.True(rig.Workspace.IsMoving);
        var last = 200.0;
        var frames = 0;
        while (rig.Workspace.IsMoving && frames++ < 200)
        {
            rig.Frame();
            Assert.True(Math.Abs(card.Shift.X) <= last + 4, "it goes home one way, up to the spring's ripple");
            last = Math.Abs(card.Shift.X);
        }
        Assert.Equal(default, card.Shift);
        Assert.Equal(home, card.Bounds);
        Assert.False(rig.Workspace.IsDocking);
        Assert.False(card.IsLiftedLook);
    });

    [Fact]
    public void Esc_springs_the_pane_home_from_wherever_the_keyboard_is_and_the_release_after_it_does_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var (tasks, _) = TwoCells(rig);
        var card = rig.Card(PaneKind.Terminal);
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(Centre(tasks));
        Assert.Equal(PaneDropKind.Body, rig.Workspace.DockDrop.Kind);
        rig.Key(Key.Escape);
        Assert.Empty(rig.Changes);
        rig.Settle();
        Assert.False(rig.Workspace.IsDocking);
        Assert.Equal(default, card.Shift);
        rig.Move(press + new Vector(-300, 0)); // the pointer is still down: nothing follows it now
        rig.Up(press);
        rig.Settle();
        Assert.Empty(rig.Changes);
        Assert.Equal(default, card.Shift);
    });

    [Fact]
    public void A_header_button_a_narrow_workspace_and_an_expanded_pane_never_start_a_drag() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var card = rig.Card(PaneKind.Terminal);
        var close = rig.BoundsOf(card.Header.CloseButton);
        var onClose = Centre(close);
        rig.Down(onClose);
        rig.Move(onClose + new Vector(-30, 0));
        rig.Move(onClose + new Vector(-60, 0));
        Assert.False(rig.Workspace.IsDocking, "the Close button takes its own press");
        rig.Up(onClose + new Vector(-60, 0));
        Assert.Empty(rig.Changes);

        rig.Workspace.ToggleExpanded(PaneKind.Terminal);
        rig.Settle();
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(30, 0));
        rig.Move(press + new Vector(60, 0));
        Assert.False(rig.Workspace.IsDocking, "an expanded pane is not re-docked");
        rig.Up(press);
        rig.Workspace.RestoreExpanded();
        rig.Settle();

        rig.Window.Width = 700;
        rig.Layout();
        rig.Settle();
        Assert.Equal(PaneLayoutMode.Narrow, rig.Workspace.Layout!.Mode);
        var narrow = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(narrow);
        rig.Move(narrow + new Vector(30, 0));
        rig.Move(narrow + new Vector(60, 0));
        Assert.False(rig.Workspace.IsDocking, "narrow mode has no grid to drop in");
        rig.Up(narrow);
    });

    [Fact]
    public void Under_Reduce_Motion_the_card_lifts_without_growing() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reduceMotion: true, state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var card = rig.Card(PaneKind.Terminal);
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(20, 20));
        rig.Settle();
        Assert.True(rig.Workspace.IsDocking);
        Assert.Equal(1, card.Scale, 3);
        Assert.True(card.IsLiftedLook, "the deeper shadow still shows it is lifted");
        rig.Up(press + new Vector(20, 20));
        rig.Settle();
        Assert.False(rig.Workspace.IsDocking);
    });

    [Fact]
    public void A_state_change_from_elsewhere_ends_the_drag_where_it_is() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InGrid(PaneKind.BackgroundTasks, PaneKind.Terminal));
        var card = rig.Card(PaneKind.Terminal);
        var press = HeaderPoint(rig, PaneKind.Terminal);
        rig.Down(press);
        rig.Move(press + new Vector(-50, 0));
        Assert.True(rig.Workspace.IsDocking);
        rig.Workspace.SetState(rig.Workspace.State.Open(PaneKind.Browser));
        rig.Layout();
        Assert.False(rig.Workspace.IsDocking);
        Assert.Equal(default, card.Shift);
        Assert.Empty(rig.Workspace.DockOutlines);
        rig.Up(press);
        rig.Settle();
        Assert.True(rig.Workspace.State.IsOpen(PaneKind.Browser));
        Assert.Empty(rig.Changes);
    });

    [Fact]
    public void A_drawers_header_lifts_a_ghost_of_the_pane_and_a_drop_in_the_empty_grid_opens_it_there() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InDrawer());
        rig.ChatColumn.Drawer.IsVisible = true;
        rig.Layout();
        using var gesture = PaneDockGesture.Attach(rig.ChatColumn.DrawerHeader, PaneKind.Terminal, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        var drawer = rig.BoundsOf(rig.ChatColumn.Drawer);
        var header = rig.BoundsOf(rig.ChatColumn.DrawerHeader);
        var press = Centre(header);
        rig.Down(press);
        rig.Move(press + new Vector(2, 1));
        Assert.False(rig.Workspace.IsDocking);
        rig.Move(press + new Vector(10, -30));
        Assert.True(rig.Workspace.IsDocking);
        var ghost = Assert.IsType<PaneCard>(rig.Workspace.DockGhost);
        rig.Layout();
        Assert.Equal(drawer.Size, ghost.Bounds.Size);
        Near(drawer.X + 10, ghost.Bounds.X); // 1:1, like a card
        Near(drawer.Y - 30, ghost.Bounds.Y);
        Assert.False(ghost.IsHitTestVisible);
        var outline = Assert.Single(rig.Workspace.DockOutlines);
        Assert.Equal(PaneDropKind.Grid, outline.Kind);
        Assert.True(rig.Workspace.DockDrop.IsNone);

        var inGrid = new Point(rig.Window.Width - 100, 300);
        rig.Move(inGrid);
        Assert.Equal(new PaneDrop(PaneDropKind.Grid), rig.Workspace.DockDrop);
        rig.Up(inGrid);
        var after = Assert.Single(rig.Changes);
        Assert.False(after.IsTerminalInDrawer);
        Assert.Equal(new[] { PaneKind.Terminal }, after.GridPanes);
        Assert.True(after.TerminalPrefersGrid);

        // The ghost springs into the cell the pane fades in at, then goes.
        var frames = 0;
        rig.Layout();
        var card = rig.Card(PaneKind.Terminal);
        Assert.NotNull(rig.Workspace.DockGhost);
        while ((rig.Workspace.IsMoving || rig.Workspace.IsDocking) && frames++ < 300) rig.Frame();
        rig.Settle();
        Assert.Null(rig.Workspace.DockGhost);
        Assert.False(rig.Workspace.IsDocking);
        Assert.Equal(1, card.Opacity, 3);
        Near(rig.ExpectedBounds(PaneKind.Terminal).X, card.Bounds.X);
        Assert.Empty(rig.Workspace.DockOutlines);
    });

    [Fact]
    public void A_drawer_dropped_over_a_cell_takes_that_slot_and_one_dropped_over_nothing_goes_home() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: InDrawer(PaneKind.BackgroundTasks, PaneKind.Browser));
        rig.ChatColumn.Drawer.IsVisible = true;
        rig.Layout();
        using var gesture = PaneDockGesture.Attach(rig.ChatColumn.DrawerHeader, PaneKind.Terminal, () => rig.Workspace, () => rig.ChatColumn.Drawer);
        var header = rig.BoundsOf(rig.ChatColumn.DrawerHeader);
        var press = Centre(header);

        // Nowhere: the chat column, then release.
        rig.Down(press);
        rig.Move(press + new Vector(30, -40));
        Assert.True(rig.Workspace.IsDocking);
        Assert.True(rig.Workspace.DockDrop.IsNone);
        var ghost = rig.Workspace.DockGhost!;
        rig.Up(press + new Vector(30, -40));
        Assert.Empty(rig.Changes);
        rig.Settle();
        Assert.Null(rig.Workspace.DockGhost);
        Assert.True(rig.Workspace.State.IsTerminalInDrawer);

        // On the Visus cell's body: the terminal takes that slot, Visus shifts one later.
        var visus = rig.Workspace.BoundsOf(PaneKind.Browser)!.Value;
        rig.Down(press);
        rig.Move(press + new Vector(50, -10));
        rig.Move(Centre(visus));
        Assert.Equal(new PaneDrop(PaneDropKind.Body, PaneKind.Browser), rig.Workspace.DockDrop);
        rig.Up(Centre(visus));
        rig.Settle();
        var after = Assert.Single(rig.Changes);
        Assert.Equal(new[] { PaneKind.BackgroundTasks, PaneKind.Terminal, PaneKind.Browser }, after.GridPanes);
        Assert.False(after.IsTerminalInDrawer);
        Assert.NotNull(rig.Workspace.CardOf(PaneKind.Terminal));
        _ = ghost;
    });
}
