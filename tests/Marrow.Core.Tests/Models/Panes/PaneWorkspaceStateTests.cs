using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 3.2 rule 9, 3.3 to 3.6, 3.11 (web surfaces W2; ED-NEW-web-w2-1, traceability alias ED-NEW-webpanes-1): the
// workspace state's operations. Opening inserts by rank, a user's drag writes the order and later opens still insert
// by rank; swap, join as tab, leave a group, Ctrl+Alt+Arrow and Ctrl+Alt+T; the terminal's drawer and grid; expand;
// close; collapse memories; the old ThreadRuntime flags; and bad data normalised, never thrown.
[Trait("Decision", "ED-NEW-webpanes-1")]
public sealed class PaneWorkspaceStateTests
{
    private static readonly PaneKind Tasks = PaneKind.BackgroundTasks, Tabula = PaneKind.Artifacts, Visus = PaneKind.Browser;

    [Fact]
    public void Opening_inserts_before_the_first_slot_of_a_higher_rank_and_a_users_order_is_kept()
    {
        Assert.Equal(new[] { 0, 1, 1, 2, 2, 2, 3 }, new[] { Tasks, PaneKind.Changes, PaneKind.Transcript, Tabula, PaneKind.App, PaneKind.Terminal, Visus }.Select(PaneKinds.Rank));
        var state = PaneWorkspaceState.Default.Open(Visus).Open(Tasks).Open(Tabula);
        Assert.Equal(new[] { Tasks, Tabula, Visus }, state.GridPanes);
        // A drag writes the order; a later open still inserts by rank (before the first slot ranked higher than it).
        var swapped = state.Swap(Tasks, Visus);
        Assert.Equal(new[] { Visus, Tabula, Tasks }, swapped.GridPanes);
        Assert.Equal(new[] { PaneKind.Changes, Visus, Tabula, Tasks }, swapped.Open(PaneKind.Changes).GridPanes);
        // Opening an open pane chooses it and changes nothing else.
        var again = state.Open(Tasks);
        Assert.Equal(state.Order, again.Order);
        Assert.Equal(Tasks, again.RecentTabs[0]);
        Assert.Equal(Tasks, again.NarrowSelection);
        Assert.True(again.IsOpen(PaneKind.Chat));
        Assert.Equal(PaneKind.Chat, again.Open(PaneKind.Chat).NarrowSelection);
    }

    [Fact]
    public void Closing_a_pane_removes_it_from_its_group_and_gives_way_to_the_chat()
    {
        var state = PaneWorkspaceState.Default.Open(Tasks).Open(Tabula).Open(Visus).JoinAsTab(Tabula, Tasks).Expand(Tabula);
        Assert.Equal(Tabula, state.Expanded);
        var closed = state.Close(Tabula);
        Assert.Equal(new[] { Tasks, Visus }, closed.GridPanes);
        Assert.Equal(2, closed.Order.Count);
        Assert.False(closed.Order[0].IsGroup);
        Assert.Null(closed.Expanded);
        Assert.Equal(PaneKind.Chat, closed.NarrowSelection);
        Assert.DoesNotContain(Tabula, closed.RecentTabs);
        Assert.Equal(closed, closed.Close(PaneKind.Chat));
        Assert.Equal(closed.Close(Visus), closed.Toggle(Visus));
        Assert.True(closed.Toggle(Tabula).IsOpen(Tabula));
        Assert.Empty(PaneWorkspaceState.Default.Open(Tasks).Close(Tasks).Order);
    }

    [Fact]
    public void Dropping_on_a_body_swaps_single_panes_and_lets_a_tab_leave_its_group_into_that_place()
    {
        var state = PaneWorkspaceState.Default.Open(Tasks).Open(PaneKind.Changes).Open(Tabula).Open(Visus);
        Assert.Equal(new[] { Visus, PaneKind.Changes, Tabula, Tasks }, state.DropOnBody(Tasks, Visus).GridPanes);
        Assert.Equal(state, state.DropOnBody(Tasks, Tasks));
        Assert.Equal(state, state.DropOnBody(Tasks, PaneKind.App));

        var grouped = state.JoinAsTab(Tabula, Tasks);
        Assert.Equal(new[] { new[] { Tasks, Tabula }, new[] { PaneKind.Changes }, new[] { Visus } }, Slots(grouped));
        // Tabula leaves the group and takes Visus's place; Visus shifts by one.
        var left = grouped.DropOnBody(Tabula, Visus);
        Assert.Equal(new[] { new[] { Tasks }, new[] { PaneKind.Changes }, new[] { Tabula }, new[] { Visus } }, Slots(left));
        Assert.Equal(Tabula, left.RecentTabs[0]);
        // Dropped on its own group's body it takes the group's place.
        Assert.Equal(new[] { new[] { Tabula }, new[] { Tasks }, new[] { PaneKind.Changes }, new[] { Visus } }, Slots(grouped.DropOnBody(Tabula, Tasks)));
    }

    [Fact]
    public void Joining_as_a_tab_appends_to_the_target_slot_shows_the_pane_and_drops_an_emptied_slot()
    {
        var state = PaneWorkspaceState.Default.Open(Tasks).Open(Tabula).Open(Visus);
        var joined = state.JoinAsTab(Visus, Tasks);
        Assert.Equal(new[] { new[] { Tasks, Visus }, new[] { Tabula } }, Slots(joined));
        Assert.Equal(Visus, joined.RecentTabs[0]);
        Assert.True(joined.Order[0].IsGroup);
        Assert.Equal(0, joined.Order[0].Rank);
        Assert.Equal(new[] { new[] { Tasks, Visus, Tabula } }, Slots(joined.JoinAsTab(Tabula, Visus)));
        Assert.Equal(joined, joined.JoinAsTab(Visus, Tasks));
        Assert.Equal(joined, joined.JoinAsTab(Visus, PaneKind.App));
    }

    [Fact]
    public void Ctrl_Alt_Arrow_swaps_with_the_neighbouring_slot_and_Ctrl_Alt_T_joins_or_splits()
    {
        var state = PaneWorkspaceState.Default.Open(Tasks).Open(Tabula).Open(Visus);
        Assert.Equal(new[] { Tabula, Tasks, Visus }, state.MoveEarlier(Tabula).GridPanes);
        Assert.Equal(new[] { Tasks, Visus, Tabula }, state.MoveLater(Tabula).GridPanes);
        Assert.Equal(state, state.MoveEarlier(Tasks));
        Assert.Equal(state, state.MoveLater(Visus));
        Assert.Equal(state, state.MoveLater(PaneKind.App));

        var joined = state.ToggleTabGroup(Tabula);
        Assert.Equal(new[] { new[] { Tasks, Tabula }, new[] { Visus } }, Slots(joined));
        // In a group, Ctrl+Alt+T splits the pane out right after the group.
        Assert.Equal(new[] { new[] { Tasks }, new[] { Tabula }, new[] { Visus } }, Slots(joined.ToggleTabGroup(Tabula)));
        // The first slot joins the next one; a lone slot has nothing to join.
        Assert.Equal(new[] { new[] { Tabula, Tasks }, new[] { Visus } }, Slots(state.ToggleTabGroup(Tasks)));
        var lone = PaneWorkspaceState.Default.Open(Tasks);
        Assert.Equal(lone, lone.ToggleTabGroup(Tasks));
        // A whole group moves as one slot.
        Assert.Equal(new[] { new[] { Visus }, new[] { Tasks, Tabula } }, Slots(joined.MoveLater(Tasks)));
    }

    [Fact]
    public void The_terminal_opens_in_its_drawer_until_it_is_dragged_into_the_grid()
    {
        var state = PaneWorkspaceState.Default.Open(Tasks).Open(Visus);
        var drawer = state.Open(PaneKind.Terminal);
        Assert.True(drawer.IsTerminalInDrawer);
        Assert.True(drawer.IsOpen(PaneKind.Terminal));
        Assert.Equal(state.Order, drawer.Order);
        Assert.Equal(state.NarrowSelection, drawer.NarrowSelection);
        Assert.Null(drawer.Expand(PaneKind.Terminal).Expanded);

        var grid = drawer.MoveTerminalToGrid();
        Assert.False(grid.IsTerminalInDrawer);
        Assert.Equal(new[] { Tasks, PaneKind.Terminal, Visus }, grid.GridPanes);
        Assert.Equal(new[] { PaneKind.Terminal, Tasks, Visus }, drawer.MoveTerminalToGrid(0).GridPanes);
        Assert.Equal(new[] { Tasks, Visus, PaneKind.Terminal }, drawer.MoveTerminalToGrid(99).GridPanes);
        // Closed and reopened, it comes back in the grid, by rank.
        var reopened = grid.Close(PaneKind.Terminal).Open(PaneKind.Terminal);
        Assert.Equal(grid.GridPanes, reopened.GridPanes);
        Assert.False(reopened.IsTerminalInDrawer);
        var back = reopened.MoveTerminalToDrawer();
        Assert.True(back.IsTerminalInDrawer);
        Assert.Equal(new[] { Tasks, Visus }, back.GridPanes);
        Assert.True(back.Close(PaneKind.Terminal).Open(PaneKind.Terminal).IsTerminalInDrawer);
        Assert.False(back.Close(PaneKind.Terminal).IsOpen(PaneKind.Terminal));
    }

    [Fact]
    public void Expand_takes_one_open_pane_and_restores()
    {
        var state = PaneWorkspaceState.Default.Open(Tasks).Open(PaneKind.SideChat);
        Assert.Equal(PaneKind.SideChat, state.Expand(PaneKind.SideChat).Expanded);
        Assert.Equal(Tasks, state.ToggleExpanded(Tasks).Expanded);
        Assert.Null(state.ToggleExpanded(Tasks).ToggleExpanded(Tasks).Expanded);
        Assert.Null(state.Expand(Tasks).RestoreExpanded().Expanded);
        Assert.Null(state.Expand(Visus).Expanded);
        Assert.Null(state.Expand(PaneKind.Chat).Expanded);
        Assert.Null(state.Expand(PaneKind.SideChat).Close(PaneKind.SideChat).Expanded);
    }

    [Fact]
    public void Collapse_remembers_the_ratio_before_and_restore_puts_it_back_or_forgets_it()
    {
        var state = PaneWorkspaceState.Default.Open(Tasks).Open(PaneKind.SideChat).WithRatio(PaneGutterKeys.Side, 0.35);
        var collapsed = state.Collapse(PaneGutterKeys.Side, 0.35).WithRatio(PaneGutterKeys.Side, 0.1);
        Assert.Contains(PaneGutterKeys.Side, collapsed.Collapsed);
        Assert.Equal(collapsed, collapsed.Collapse(PaneGutterKeys.Side, 0.9));
        Assert.Equal(state, collapsed.Restore(PaneGutterKeys.Side));
        Assert.False(state.Collapse(PaneGutterKeys.Side, null).Restore(PaneGutterKeys.Side).Ratios.ContainsKey(PaneGutterKeys.Side));
        Assert.Equal(state, state.Restore(PaneGutterKeys.Side));
        // Closing Side chat keeps its collapse; reopening it restores it.
        var reopened = state.Collapse(PaneGutterKeys.Side, 0.35).Close(PaneKind.SideChat);
        Assert.Contains(PaneGutterKeys.Side, reopened.Collapsed);
        Assert.Empty(reopened.Open(PaneKind.SideChat).Collapsed);
        Assert.Equal(0.35, reopened.Open(PaneKind.SideChat).Ratios[PaneGutterKeys.Side]);
        Assert.Equal(state, state.WithRatio("chat", double.NaN));
        Assert.Equal(state, state.WithoutRatio("chat"));
    }

    [Fact]
    public void An_old_thread_maps_the_terminal_and_changes_flags_in_once()
    {
        Assert.Equal(PaneWorkspaceState.Default, PaneWorkspaceState.FromLegacyFlags(false, false));
        var both = PaneWorkspaceState.FromLegacyFlags(isTerminalVisible: true, isDiffVisible: true);
        Assert.True(both.IsTerminalInDrawer);
        Assert.Equal(new[] { PaneKind.Changes }, both.GridPanes);
        Assert.Equal(new[] { PaneKind.Changes }, PaneWorkspaceState.FromLegacyFlags(false, true).GridPanes);
        Assert.Equal("changes", PaneKinds.Id(PaneKind.Changes));
        Assert.Equal(PaneKind.Browser, PaneKinds.FromId("browser"));
        Assert.Null(PaneKinds.FromId("hologram"));
        Assert.Equal(new[] { "chat", "side", "tasks", "changes", "artifact", "browser", "terminal", "app", "transcript" }, Enum.GetValues<PaneKind>().Select(PaneKinds.Id));
    }

    [Fact]
    public void Names_follow_the_canvas()
    {
        Assert.Equal(new[] { "Chat", "Side chat", "Tasks", "Changes", "Tabula", "Visus", "Terminal", "App", "Transcript" }, Enum.GetValues<PaneKind>().Select(PaneKinds.ShortName));
        Assert.Equal("Background tasks", PaneKinds.Title(Tasks));
        Assert.Equal("Visus", PaneKinds.Title(Visus));
        Assert.Equal("Browser, Visus", PaneKinds.AccessibleName(Visus));
        Assert.Equal("Artifacts, Tabula", PaneKinds.AccessibleName(Tabula));
        Assert.Equal(new[] { 1.0, 0.50, 0.42, 0.36, 0.31, 0.31 }, Enumerable.Range(0, 6).Select(PaneKinds.DefaultChatRatio));
    }

    private static PaneKind[][] Slots(PaneWorkspaceState state) => [.. state.Order.Select(slot => slot.Panes.ToArray())];
}
