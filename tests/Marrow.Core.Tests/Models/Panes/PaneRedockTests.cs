using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models.Panes;

// nth web surfaces, unit W3b: re-docking a pane by its header (work/design/web-surfaces/DESIGN.md 3.6, 13.1) and the give of
// a collapsible limit under a drag (4.2). Drop targets exist only for moves the solver can express: another cell's body
// (swap), another cell's header (join as a tab) and, for a tab of a group, its own cell's body (leave the group). Every
// result is the state operation the keyboard uses, so a drop and Ctrl+Alt+Arrow cannot disagree.
[Trait("Decision", "ED-NEW-web-w3b-2")]
public sealed class PaneRedockTests
{
    private const double W = 1600;
    private const double H = 900;

    private static PaneWorkspaceState Open(params PaneKind[] kinds) => kinds.Aggregate(PaneWorkspaceState.Default, (state, kind) => state.Open(kind));

    private static PaneLayoutResult Solve(PaneWorkspaceState state, double width = W, double height = H) => PaneLayout.Solve(state, width, height);

    [Fact]
    public void A_tiled_pane_can_be_lifted_and_the_chat_side_chat_and_drawer_terminal_cannot()
    {
        var state = Open(PaneKind.BackgroundTasks, PaneKind.Browser, PaneKind.SideChat) with { IsTerminalInDrawer = true };
        var layout = Solve(state);
        Assert.True(PaneRedock.CanLift(state, layout, PaneKind.BackgroundTasks));
        Assert.True(PaneRedock.CanLift(state, layout, PaneKind.Browser));
        Assert.False(PaneRedock.CanLift(state, layout, PaneKind.Chat));
        Assert.False(PaneRedock.CanLift(state, layout, PaneKind.SideChat));
        Assert.False(PaneRedock.CanLift(state, layout, PaneKind.Terminal));
        Assert.False(PaneRedock.CanLift(state, layout, PaneKind.Changes)); // not open
        Assert.Empty(PaneRedock.Targets(state, layout, PaneKind.Chat));
        Assert.Empty(PaneRedock.Targets(state, layout, PaneKind.SideChat));
    }

    [Fact]
    public void An_expanded_workspace_and_narrow_mode_have_no_drops()
    {
        var state = Open(PaneKind.BackgroundTasks, PaneKind.Browser);
        Assert.Empty(PaneRedock.Targets(state.Expand(PaneKind.Browser), Solve(state.Expand(PaneKind.Browser)), PaneKind.BackgroundTasks));
        var narrow = Solve(state, 600, H);
        Assert.Equal(PaneLayoutMode.Narrow, narrow.Mode);
        Assert.Empty(PaneRedock.Targets(state, narrow, PaneKind.Browser));
        Assert.False(PaneRedock.CanLift(state, narrow, PaneKind.Browser));
    }

    [Fact]
    public void Another_cells_body_swaps_and_its_header_joins_as_a_tab()
    {
        var state = Open(PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Browser); // Tasks | Changes over a wide Visus
        var layout = Solve(state);
        var targets = PaneRedock.Targets(state, layout, PaneKind.Changes);
        Assert.Equal(4, targets.Count);

        var tasksBody = targets.Single(t => t.Kind == PaneDropKind.Body && t.Target == PaneKind.BackgroundTasks);
        Assert.Equal(new[] { PaneKind.Changes, PaneKind.BackgroundTasks, PaneKind.Browser }, tasksBody.Result.GridPanes);
        var visusHeader = targets.Single(t => t.Kind == PaneDropKind.Header && t.Target == PaneKind.Browser);
        Assert.Equal(2, visusHeader.Result.Order.Count);
        Assert.Equal(new[] { PaneKind.Browser, PaneKind.Changes }, visusHeader.Result.Order[1].Panes);
        Assert.Equal(PaneKind.Changes, visusHeader.Result.RecentTabs[0]); // the joined pane shows

        // The rectangles are the cell's: a 36 DIP header over the body, inside the solved cell.
        var cell = layout.CellOf(PaneKind.Browser)!;
        Assert.Equal(new PaneRect(cell.Bounds.X, cell.Bounds.Y, cell.Bounds.Width, PaneKinds.HeaderHeight), visusHeader.Bounds);
        var visusBody = targets.Single(t => t.Kind == PaneDropKind.Body && t.Target == PaneKind.Browser);
        Assert.Equal(new PaneRect(cell.Bounds.X, cell.Bounds.Y + 36, cell.Bounds.Width, cell.Bounds.Height - 36), visusBody.Bounds);

        // The pane's own cell is no target when it is not in a group.
        Assert.DoesNotContain(targets, t => t.Target == PaneKind.Changes);
        Assert.All(targets, t => Assert.NotEqual(state, t.Result));
    }

    [Fact]
    public void A_header_wins_where_it_covers_a_body()
    {
        var state = Open(PaneKind.BackgroundTasks, PaneKind.Browser);
        var layout = Solve(state);
        var targets = PaneRedock.Targets(state, layout, PaneKind.BackgroundTasks);
        var visus = layout.CellOf(PaneKind.Browser)!.Bounds;
        Assert.Equal(PaneDropKind.Header, PaneRedock.Hit(targets, visus.X + 20, visus.Y + 10)!.Kind);
        Assert.Equal(PaneDropKind.Body, PaneRedock.Hit(targets, visus.X + 20, visus.Y + 60)!.Kind);
        Assert.Equal(PaneDropKind.Header, PaneRedock.Hit(targets, visus.X + 20, visus.Y + 36)!.Kind); // the edge belongs to the header
        Assert.Null(PaneRedock.Hit(targets, 5, 5)); // over the chat: nothing
        Assert.Null(PaneRedock.Hit([], 100, 100));
    }

    [Fact]
    public void A_tab_of_a_group_can_leave_it_by_its_own_body_and_every_other_cell_still_takes_it()
    {
        var state = Open(PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Browser).JoinAsTab(PaneKind.Changes, PaneKind.BackgroundTasks);
        var layout = Solve(state);
        Assert.Equal(2, state.Order.Count);
        var targets = PaneRedock.Targets(state, layout, PaneKind.Changes);

        var leave = targets.Single(t => t.Target == PaneKind.BackgroundTasks && t.Kind == PaneDropKind.Body);
        Assert.Equal(layout.CellOf(PaneKind.Changes)!.Bounds.Y + 36, leave.Bounds.Y); // the group's own body
        Assert.Equal(new[] { PaneKind.Changes }, leave.Result.Order[0].Panes);
        Assert.Equal(new[] { PaneKind.BackgroundTasks }, leave.Result.Order[1].Panes);
        Assert.False(targets.Any(t => t.Kind == PaneDropKind.Header && t.Target == PaneKind.BackgroundTasks), "a header onto its own group is no move");

        var onVisus = targets.Single(t => t.Target == PaneKind.Browser && t.Kind == PaneDropKind.Body);
        Assert.Equal(new[] { PaneKind.BackgroundTasks }, onVisus.Result.Order[0].Panes);
        Assert.Equal(new[] { PaneKind.Changes }, onVisus.Result.Order[1].Panes); // leaves the group, takes Visus's place
        Assert.Equal(new[] { PaneKind.Browser }, onVisus.Result.Order[2].Panes); // and Visus shifts by one
    }

    [Fact]
    public void Every_target_of_every_random_workspace_is_a_state_the_solver_can_draw()
    {
        var random = new Random(8675309);
        var drops = 0;
        for (var i = 0; i < 2_000; i++)
        {
            var state = PaneLayoutChecks.RandomState(random);
            double w = 900 + random.NextDouble() * 2400, h = 500 + random.NextDouble() * 1200;
            var layout = PaneLayout.Solve(state, w, h);
            foreach (var pane in state.Normalized().GridPanes)
            {
                var targets = PaneRedock.Targets(state, layout, pane);
                if (!PaneRedock.CanLift(state, layout, pane)) Assert.Empty(targets);
                foreach (var target in targets)
                {
                    drops++;
                    Assert.NotEqual(state.Normalized(), target.Result);
                    Assert.True(target.Bounds.Width >= 0 && target.Bounds.Height >= 0, "negative target");
                    Assert.True(target.Bounds.X >= -1e-6 && target.Bounds.Right <= w + 1e-6 && target.Bounds.Y >= -1e-6 && target.Bounds.Bottom <= h + 1e-6, $"{target.Bounds} outside {w} x {h}");
                    PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(target.Result, PaneLayout.Solve(target.Result, w, h)));
                    var expected = state.Normalized().GridPanes.Order().ToList();
                    if (target.Kind == PaneDropKind.Drawer)
                    {
                        // Web W4 (ED-NEW-web-w4-1): the terminal's one move that leaves the grid, back to the chat's drawer.
                        Assert.Equal(PaneKind.Terminal, pane);
                        Assert.True(target.Result.IsTerminalInDrawer);
                        expected.Remove(PaneKind.Terminal);
                    }
                    Assert.Equal(expected, target.Result.GridPanes.Order().ToList()); // nothing lost or gained
                }
            }
        }
        Assert.True(drops > 1_000, $"{drops} drops drawn");
    }

    // ------------------------------------------------------------------ the give of a collapsible limit

    private static PaneGutter ChatGutter(PaneWorkspaceState state) => Solve(state).Gutter(PaneGutterKeys.Chat)!;

    [Fact]
    public void A_collapsible_limit_gives_a_third_of_the_overshoot_up_to_24_DIP_then_folds_at_56()
    {
        var gutter = ChatGutter(Open(PaneKind.Browser));
        var high = gutter.NormalMaximum;
        Assert.Equal(0, PaneRedock.RubberGive(gutter, high, false), 6);
        Assert.Equal(0, PaneRedock.RubberGive(gutter, high - 40, false), 6); // inside the range
        Assert.Equal(3, PaneRedock.RubberGive(gutter, high + 10, false), 6); // 0.3 x 10
        Assert.Equal(15, PaneRedock.RubberGive(gutter, high + 50, false), 6);
        Assert.Equal(16.8, PaneRedock.RubberGive(gutter, high + 56, false), 6); // the last DIP before it folds
        Assert.Equal(0, PaneRedock.RubberGive(gutter, high + 57, false), 6); // folded: the side is a rail now
        Assert.Equal(0, PaneRedock.RubberGive(gutter, high + 200, false), 6);
    }

    [Fact]
    public void A_limit_that_cannot_collapse_gives_nothing_and_neither_does_Reduce_Motion_or_a_collapsed_gutter()
    {
        var state = Open(PaneKind.BackgroundTasks, PaneKind.Browser);
        var layout = Solve(state);
        var cols = layout.Gutters.FirstOrDefault(g => !g.CanCollapse);
        Assert.NotNull(cols);
        Assert.False(cols.CanCollapse);
        Assert.Equal(0, PaneRedock.RubberGive(cols, cols.NormalMinimum - 10, false));
        Assert.Equal(0, PaneRedock.RubberGive(cols, cols.NormalMaximum + 10, false));

        var chat = ChatGutter(state);
        Assert.Equal(0, PaneRedock.RubberGive(chat, chat.NormalMaximum + 10, reduceMotion: true));
        Assert.Equal(0, PaneRedock.RubberGive(chat, double.NaN, false));
        var collapsed = Solve(state.Collapse(PaneGutterKeys.Chat, null)).Gutter(PaneGutterKeys.Chat)!;
        Assert.True(collapsed.IsCollapsed);
        Assert.Equal(0, PaneRedock.RubberGive(collapsed, collapsed.NormalMaximum + 10, false));
    }

    [Fact]
    public void Side_chat_gives_below_its_minimum_in_the_other_direction()
    {
        var state = Open(PaneKind.Browser, PaneKind.SideChat);
        var side = Solve(state, 1800, 900).Gutter(PaneGutterKeys.Side)!;
        Assert.True(side.CanCollapse && !side.CollapsesPastMaximum);
        Assert.Equal(-3, PaneRedock.RubberGive(side, side.NormalMinimum - 10, false), 6);
        Assert.Equal(-16.8, PaneRedock.RubberGive(side, side.NormalMinimum - 56, false), 6); // 56 x 0.3: the 24 DIP cap is never reached before the fold
        Assert.Equal(0, PaneRedock.RubberGive(side, side.NormalMinimum - 57, false), 6);
    }

    [Fact]
    public void The_gutter_reports_its_range_with_the_collapsed_position_and_names_the_state()
    {
        var open = ChatGutter(Open(PaneKind.Browser));
        var limits = open.Limits;
        Assert.Equal(SplitterCollapse.AboveMaximum, limits.Collapse);
        var report = SplitterMath.Report(open.Value, false, limits, open.ValueText, open.ValueText);
        Assert.Equal(open.Minimum, report.Minimum, 6);
        Assert.Equal(open.Maximum, report.Maximum, 6);
        Assert.InRange(report.Value, report.Minimum, report.Maximum);

        var collapsed = ChatGutter(Open(PaneKind.Browser).Collapse(PaneGutterKeys.Chat, null));
        var collapsedReport = SplitterMath.Report(collapsed.Value, true, collapsed.Limits, collapsed.ValueText, collapsed.ValueText);
        Assert.True(collapsedReport.IsCollapsed);
        Assert.Equal("Panes collapsed", collapsedReport.Text);
        Assert.Equal(collapsedReport.Maximum, collapsedReport.Value, 6);

        var side = Solve(Open(PaneKind.Browser, PaneKind.SideChat), 1800, 900).Gutter(PaneGutterKeys.Side)!;
        Assert.Equal(SplitterCollapse.BelowMinimum, side.Limits.Collapse);
        Assert.Equal(PaneKinds.Rail, side.Limits.RangeMinimum);
    }
}
