using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 13.1 PaneLayoutTests (web surfaces W2; ED-NEW-web-w2-1, traceability alias ED-NEW-webpanes-1): the tiling rule
// of 3.2 at the listed sizes, per-kind minimums and maximums, rank insertion, explicit tab groups, tabs overflow, ratio
// clamps, collapse thresholds, the narrow switch, right-to-left mirroring and determinism. The invariants each layout
// must keep are PaneLayoutChecks.Problems.
[Trait("Decision", "ED-NEW-webpanes-1")]
public sealed class PaneLayoutTests
{
    private static readonly double[] Widths = [600, 688, 768, 900, 1200, 1600, 2560];
    private static readonly double[] Heights = [500, 800, 1400];

    [Fact]
    public void Every_pane_count_from_0_to_7_with_and_without_side_chat_keeps_the_rules_at_every_listed_size()
    {
        var problems = new List<string>();
        var counts = new int[8];
        for (var mask = 0; mask < 1 << PaneKinds.GridKinds.Count; mask++)
        {
            var panes = PaneKinds.GridKinds.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            counts[panes.Count]++;
            foreach (var side in new[] { false, true })
            {
                var state = panes.Aggregate(PaneWorkspaceState.Default, (s, k) => s.Open(k));
                if (side) state = state.Open(PaneKind.SideChat);
                foreach (var w in Widths)
                    foreach (var h in Heights)
                    {
                        var ltr = PaneLayout.Solve(state, w, h);
                        problems.AddRange(PaneLayoutChecks.Problems(state, ltr).Select(p => $"{string.Join('+', panes)} side={side} {w}x{h}: {p}"));
                        problems.AddRange(PaneLayoutChecks.MirrorProblems(ltr, PaneLayout.Solve(state, w, h, rightToLeft: true)).Select(p => $"{string.Join('+', panes)} rtl {w}x{h}: {p}"));
                    }
            }
        }
        Assert.Equal(new[] { 1, 7, 21, 35, 35, 21, 7, 1 }, counts);
        PaneLayoutChecks.AssertNone(problems);
    }

    [Fact]
    public void The_chat_never_drops_under_400_and_never_collapses()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks).Open(PaneKind.Artifacts).Open(PaneKind.Browser).Open(PaneKind.SideChat)
            .WithRatio(PaneGutterKeys.Chat, 0);
        for (var w = 600.0; w <= 2560; w += 0.5)
        {
            var layout = PaneLayout.Solve(state, w, 800);
            if (layout.Mode == PaneLayoutMode.Narrow) continue;
            Assert.True(layout.Chat.Width >= PaneKinds.ChatMinWidth, $"{w}: chat {layout.Chat.Width}");
        }
        // Dragging the chat gutter to zero stops at the chat's minimum; there is no collapse on the chat's side.
        var dragged = PaneLayout.Drag(state.WithoutRatio(PaneGutterKeys.Chat), 1600, 800, PaneGutterKeys.Chat, -500);
        Assert.Empty(dragged.Collapsed);
        Assert.Equal(PaneKinds.ChatMinWidth, PaneLayout.Solve(dragged, 1600, 800).Chat.Width, 9);
    }

    [Fact]
    public void Each_kind_keeps_its_own_minimum_and_maximum_from_the_3_2_table()
    {
        Assert.Equal(new PaneSize(400, 240, double.PositiveInfinity), PaneKinds.Size(PaneKind.Chat));
        Assert.Equal(new PaneSize(360, 240, double.PositiveInfinity), PaneKinds.Size(PaneKind.Browser));
        Assert.Equal(new PaneSize(320, 200, double.PositiveInfinity), PaneKinds.Size(PaneKind.Artifacts));
        Assert.Equal(new PaneSize(320, 200, double.PositiveInfinity), PaneKinds.Size(PaneKind.App));
        Assert.Equal(new PaneSize(280, 240, 640), PaneKinds.Size(PaneKind.SideChat));
        Assert.Equal(new PaneSize(320, 150, double.PositiveInfinity), PaneKinds.Size(PaneKind.Terminal));
        Assert.Equal(new PaneSize(280, 150, 560), PaneKinds.Size(PaneKind.BackgroundTasks));
        Assert.Equal(new PaneSize(280, 150, double.PositiveInfinity), PaneKinds.Size(PaneKind.Changes));
        Assert.Equal(new PaneSize(280, 150, 720), PaneKinds.Size(PaneKind.Transcript));
        Assert.Equal((8.0, 22.0, 56.0), (PaneKinds.Gap, PaneKinds.Rail, PaneKinds.CollapseThreshold));

        // Alone on a 2560 DIP workspace, a capped pane sits at its maximum and the chat takes the rest.
        foreach (var (kind, max) in new[] { (PaneKind.BackgroundTasks, 560.0), (PaneKind.Transcript, 720.0), (PaneKind.SideChat, 640.0) })
        {
            var layout = PaneLayout.Solve(PaneWorkspaceState.Default.Open(kind), 2560, 900);
            Assert.Equal(max, layout.BoundsOf(kind)!.Value.Width, 9);
            Assert.Equal(2560 - PaneKinds.Gap - max, layout.Chat.Width, 9);
        }
        // A slot's minimum is the largest minimum in it, its maximum the smallest maximum.
        var grouped = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks).Open(PaneKind.Browser).JoinAsTab(PaneKind.Browser, PaneKind.BackgroundTasks);
        var cell = PaneLayout.Solve(grouped, 2560, 900).CellOf(PaneKind.Browser)!;
        Assert.Equal(560, cell.Bounds.Width, 9);
        Assert.Equal(768, PaneLayout.NarrowThreshold(grouped));
    }

    [Fact]
    public void When_every_cell_in_a_row_is_at_its_maximum_the_chat_takes_the_remaining_width()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks).Open(PaneKind.Transcript);
        var layout = PaneLayout.Solve(state, 2560, 900);
        Assert.Equal(2, layout.GridColumns);
        Assert.Equal(560, layout.BoundsOf(PaneKind.BackgroundTasks)!.Value.Width, 9);
        Assert.Equal(720, layout.BoundsOf(PaneKind.Transcript)!.Value.Width, 9);
        Assert.Equal(2560 - 8 - 560 - 8 - 720, layout.Chat.Width, 9);
        Assert.Equal(layout.Chat.Width, layout.Gutter(PaneGutterKeys.Chat)!.NormalMinimum, 9);
        PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(state, layout));

        // With Side chat in its own column the grid is capped the same way and Side chat keeps its stored share.
        var withSide = state.Open(PaneKind.SideChat);
        var sideLayout = PaneLayout.Solve(withSide, 3840, 900);
        Assert.Equal(560 + 8 + 720, sideLayout.Grid!.Value.Width, 9);
        PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(withSide, sideLayout));
    }

    [Fact]
    public void Opening_tasks_tabula_and_visus_in_any_order_gives_tasks_and_tabula_over_a_wide_visus()
    {
        PaneKind[] three = [PaneKind.Browser, PaneKind.Artifacts, PaneKind.BackgroundTasks];
        foreach (var order in Permutations(three))
        {
            var state = order.Aggregate(PaneWorkspaceState.Default.Open(PaneKind.SideChat), (s, k) => s.Open(k));
            Assert.Equal(new[] { PaneKind.BackgroundTasks, PaneKind.Artifacts, PaneKind.Browser }, state.GridPanes);
            var layout = PaneLayout.Solve(state, 1324, 848);
            Rect(layout, PaneKind.BackgroundTasks, out var tasks);
            Rect(layout, PaneKind.Artifacts, out var tabula);
            Rect(layout, PaneKind.Browser, out var visus);
            Assert.Equal(tasks.Y, tabula.Y);
            Assert.True(tasks.X < tabula.X && visus.Y > tasks.Bottom);
            Assert.Equal(tasks.X, visus.X, 9);
            Assert.Equal(tabula.Right, visus.Right, 9);
        }
        // Ranks: Tasks 0, Changes and Transcript 1, Tabula and App 2, Visus 3; equal ranks keep their opening order.
        var all = new[] { PaneKind.Browser, PaneKind.App, PaneKind.Transcript, PaneKind.Artifacts, PaneKind.Changes, PaneKind.BackgroundTasks }
            .Aggregate(PaneWorkspaceState.Default, (s, k) => s.Open(k));
        Assert.Equal(new[] { PaneKind.BackgroundTasks, PaneKind.Transcript, PaneKind.Changes, PaneKind.App, PaneKind.Artifacts, PaneKind.Browser }, all.GridPanes);
    }

    [Fact]
    public void An_explicit_tab_group_is_one_slot_with_one_cell_showing_its_most_recent_tab()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.Changes).Open(PaneKind.Artifacts).Open(PaneKind.Browser)
            .JoinAsTab(PaneKind.Artifacts, PaneKind.Changes);
        Assert.Equal(2, state.Order.Count);
        var layout = PaneLayout.Solve(state, 1600, 900);
        var group = layout.CellOf(PaneKind.Artifacts)!;
        Assert.Equal(new[] { PaneKind.Changes, PaneKind.Artifacts }, group.Tabs);
        Assert.Equal(1, group.SlotCount);
        Assert.Equal(PaneKind.Artifacts, group.Shown);
        Assert.False(layout.IsShown(PaneKind.Changes));
        Assert.Equal(group.Bounds, layout.BoundsOf(PaneKind.Changes));
        Assert.Equal(2, layout.Cells.Count);
        // Two slots: the chat's default share is the two-slot 0.42.
        Assert.Equal((1600 - 8) * 0.42, layout.Chat.Width, 9);
        // Choosing the other tab shows it; the group keeps its order.
        var chosen = PaneLayout.Solve(state.Select(PaneKind.Changes), 1600, 900).CellOf(PaneKind.Artifacts)!;
        Assert.Equal(PaneKind.Changes, chosen.Shown);
        Assert.Equal(group.Tabs, chosen.Tabs);
        PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(state, layout));
    }

    [Fact]
    public void Five_or_more_cells_join_the_last_cell_as_tabs_and_rows_that_do_not_fit_join_the_last_row()
    {
        var state = new[] { PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Artifacts, PaneKind.Browser, PaneKind.App, PaneKind.Transcript }
            .Aggregate(PaneWorkspaceState.Default, (s, k) => s.Open(k));
        Assert.Equal(new[] { PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Transcript, PaneKind.Artifacts, PaneKind.App, PaneKind.Browser }, state.GridPanes);
        var layout = PaneLayout.Solve(state, 2400, 1000);
        Assert.Equal(2, layout.GridColumns);
        Assert.Equal(4, layout.Cells.Count);
        var last = layout.Cells[^1];
        Assert.Equal(state.GridPanes.Skip(3), last.Tabs);
        Assert.Equal(3, last.SlotCount);
        Assert.Equal(PaneKind.App, last.Shown); // the most recently opened of the three
        PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(state, layout));

        // One column, 600 DIP tall: Tasks and Changes fit as rows above a third row holding the rest as tabs.
        var column = PaneLayout.Solve(state, 1000, 600);
        Assert.Equal(1, column.GridColumns);
        Assert.Equal(3, column.Cells.Count);
        Assert.Equal(state.GridPanes.Skip(2), column.Cells[2].Tabs);
        PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(state, column));

        // At most three rows, however tall.
        Assert.Equal(3, PaneLayout.Solve(state, 1000, 3000).Cells.Count);
    }

    [Fact]
    public void Stored_ratios_out_of_range_are_clamped_and_never_break_a_minimum()
    {
        var baseState = new[] { PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Artifacts, PaneKind.Browser }
            .Aggregate(PaneWorkspaceState.Default.Open(PaneKind.SideChat), (s, k) => s.Open(k));
        foreach (var ratio in new[] { -1, 0, 1, 2, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300 })
        {
            var map = PaneGutterKeys.All.ToDictionary(k => k, _ => ratio);
            var state = baseState with { Ratios = new ValueMap<string, double>(map) };
            foreach (var (w, h) in new[] { (1600.0, 900.0), (1000.0, 500.0), (2560.0, 1400.0) })
            {
                var layout = PaneLayout.Solve(state, w, h);
                PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(state, layout));
            }
        }
        // The chat's stored share is held to [ChatMin, W - Gap - WorkspaceMin].
        var wide = PaneLayout.Solve(baseState.WithRatio(PaneGutterKeys.Chat, 0.99), 1600, 900);
        var chat = wide.Gutter(PaneGutterKeys.Chat)!;
        Assert.Equal(chat.NormalMaximum, wide.Chat.Width, 9);
        Assert.Equal(1600 - 8 - (280 + 8 + 360), chat.NormalMaximum, 9);
        // SetGutter clamps to the range without the collapsed position and writes the ratio of the gutter's base.
        var set = PaneLayout.SetGutter(baseState, 1600, 900, PaneGutterKeys.Chat, 5000);
        Assert.Empty(set.Collapsed);
        Assert.Equal(chat.NormalMaximum / chat.Base, set.Ratios[PaneGutterKeys.Chat], 12);
    }

    [Fact]
    public void A_drag_collapses_a_side_only_56_DIP_past_its_minimum_and_restores_when_dragged_back_over_it()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks).Open(PaneKind.Browser).Open(PaneKind.SideChat)
            .WithRatio(PaneGutterKeys.Chat, 0.4).WithRatio(PaneGutterKeys.Side, 0.33);
        const double w = 1600, h = 900;
        var chat = PaneLayout.Solve(state, w, h).Gutter(PaneGutterKeys.Chat)!;
        Assert.True(chat.CanCollapse && chat.CollapsesPastMaximum);

        Assert.Empty(PaneLayout.Drag(state, w, h, PaneGutterKeys.Chat, chat.NormalMaximum + 56, state).Collapsed);
        var collapsed = PaneLayout.Drag(state, w, h, PaneGutterKeys.Chat, chat.NormalMaximum + 56.5, state);
        Assert.Contains(PaneGutterKeys.Chat, collapsed.Collapsed);
        Assert.Equal(0.4, collapsed.PreCollapse[PaneGutterKeys.Chat]);
        // Hysteresis: between the minimum and the threshold it stays collapsed; over the minimum it restores to 0.4.
        Assert.Equal(collapsed, PaneLayout.Drag(collapsed, w, h, PaneGutterKeys.Chat, chat.NormalMaximum + 1));
        var restored = PaneLayout.Drag(collapsed, w, h, PaneGutterKeys.Chat, chat.NormalMaximum);
        Assert.Empty(restored.Collapsed);
        Assert.Equal(0.4, restored.Ratios[PaneGutterKeys.Chat]);
        Assert.Equal(PaneLayout.Solve(state, w, h), PaneLayout.Solve(restored, w, h));

        var side = PaneLayout.Solve(state, w, h).Gutter(PaneGutterKeys.Side)!;
        Assert.False(side.CollapsesPastMaximum);
        Assert.Equal(280, side.NormalMinimum);
        Assert.Empty(PaneLayout.Drag(state, w, h, PaneGutterKeys.Side, 280 - 56, state).Collapsed);
        var sideCollapsed = PaneLayout.Drag(state, w, h, PaneGutterKeys.Side, 280 - 56.5, state);
        var folded = PaneLayout.Solve(sideCollapsed, w, h);
        Assert.Equal(new PaneRect(w - 22, 0, 22, h), folded.Rail(PaneGutterKeys.Side)!.Bounds);
        Assert.Equal(PaneKinds.Rail, folded.Gutter(PaneGutterKeys.Side)!.Value);
        Assert.Equal(sideCollapsed, PaneLayout.Drag(sideCollapsed, w, h, PaneGutterKeys.Side, 279));
        Assert.Equal(PaneLayout.Solve(state, w, h), PaneLayout.Solve(PaneLayout.Drag(sideCollapsed, w, h, PaneGutterKeys.Side, 280), w, h));

        // Inner gutters clamp at both sides' minimums and never collapse (the approved canvas).
        var row = PaneLayout.Solve(state, w, h).Gutters.Single(g => g.Key is not (PaneGutterKeys.Chat or PaneGutterKeys.Side));
        Assert.False(row.CanCollapse);
        var squeezed = PaneLayout.Drag(state, w, h, row.Key, -1000);
        Assert.Equal(row.NormalMinimum, PaneLayout.Solve(squeezed, w, h).Gutter(row.Key)!.Value, 9);
    }

    [Fact]
    public void Collapse_and_restore_return_to_the_size_before_the_collapse_or_to_the_default_from_the_minimum()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.Artifacts).Open(PaneKind.Browser).Open(PaneKind.SideChat).WithRatio(PaneGutterKeys.Side, 0.4);
        const double w = 1800, h = 900;
        var round = PaneLayout.ToggleCollapse(PaneLayout.ToggleCollapse(state, w, h, PaneGutterKeys.Side), w, h, PaneGutterKeys.Side);
        Assert.Equal(state, round);

        // At the minimum, restore uses the default share.
        var atMinimum = PaneLayout.SetGutter(state, w, h, PaneGutterKeys.Side, 280);
        Assert.True(PaneLayout.AtCollapsibleMinimum(PaneLayout.Solve(atMinimum, w, h).Gutter(PaneGutterKeys.Side)!));
        var back = PaneLayout.ToggleCollapse(PaneLayout.ToggleCollapse(atMinimum, w, h, PaneGutterKeys.Side), w, h, PaneGutterKeys.Side);
        Assert.False(back.Ratios.ContainsKey(PaneGutterKeys.Side));
        var expected = PaneLayout.Solve(state.WithoutRatio(PaneGutterKeys.Side), w, h).Gutter(PaneGutterKeys.Side)!.Value;
        Assert.Equal(expected, PaneLayout.Solve(back, w, h).Gutter(PaneGutterKeys.Side)!.Value, 9);
        Assert.Equal((w - (w - 8) * 0.36 - 16) * 0.30, expected, 9);

        // A drag that squeezed the side to its minimum before collapsing remembers the size the drag started from.
        var squeezed = PaneLayout.Drag(state, w, h, PaneGutterKeys.Side, 280, state);
        var dragged = PaneLayout.Drag(squeezed, w, h, PaneGutterKeys.Side, 100, state);
        Assert.Equal(0.4, dragged.PreCollapse[PaneGutterKeys.Side]);
        Assert.Equal(PaneLayout.Solve(state, w, h), PaneLayout.Solve(PaneLayout.ToggleCollapse(dragged, w, h, PaneGutterKeys.Side), w, h));

        // Double-click resets to the default, collapsed or not; opening a pane restores a collapsed workspace.
        var reset = PaneLayout.Reset(PaneLayout.ToggleCollapse(state, w, h, PaneGutterKeys.Chat), w, h, PaneGutterKeys.Chat);
        Assert.Empty(reset.Collapsed);
        Assert.False(reset.Ratios.ContainsKey(PaneGutterKeys.Chat));
        var reopened = PaneLayout.ToggleCollapse(state, w, h, PaneGutterKeys.Chat).Open(PaneKind.BackgroundTasks);
        Assert.Empty(reopened.Collapsed);
    }

    [Fact]
    public void The_layout_turns_to_narrow_tabs_under_chat_minimum_plus_gap_plus_the_widest_open_minimum()
    {
        var tasks = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks);
        Assert.Equal(688, PaneLayout.NarrowThreshold(tasks));
        Assert.Equal(PaneLayoutMode.Narrow, PaneLayout.Solve(tasks, 687.99, 800).Mode);
        Assert.Equal(PaneLayoutMode.Tiles, PaneLayout.Solve(tasks, 688, 800).Mode);
        var visus = tasks.Open(PaneKind.Browser);
        Assert.Equal(768, PaneLayout.NarrowThreshold(visus));
        Assert.Equal(PaneLayoutMode.Narrow, PaneLayout.Solve(visus, 767.99, 800).Mode);
        Assert.Equal(PaneLayoutMode.Tiles, PaneLayout.Solve(visus, 768, 800).Mode);
        Assert.Equal(688, PaneLayout.NarrowThreshold(PaneWorkspaceState.Default.Open(PaneKind.SideChat)));
        Assert.Equal(0, PaneLayout.NarrowThreshold(PaneWorkspaceState.Default));
        Assert.Equal(PaneLayoutMode.Tiles, PaneLayout.Solve(PaneWorkspaceState.Default, 320, 320).Mode);

        // Opening a pane in narrow mode selects its tab; closing it returns to Chat.
        var opened = visus.Open(PaneKind.Artifacts);
        var narrow = PaneLayout.Solve(opened, 700, 800);
        Assert.Equal(new[] { PaneKind.Chat, PaneKind.BackgroundTasks, PaneKind.Artifacts, PaneKind.Browser }, narrow.NarrowTabs);
        Assert.Equal(PaneKind.Artifacts, narrow.NarrowSelection);
        Assert.Equal(new PaneRect(0, 0, 700, 44), narrow.NarrowStrip);
        Assert.Equal(PaneKind.Chat, PaneLayout.Solve(opened.Close(PaneKind.Artifacts), 700, 800).NarrowSelection);
    }

    [Fact]
    public void Right_to_left_mirrors_every_rectangle_and_runs_the_pointer_the_other_way()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks).Open(PaneKind.Changes).Open(PaneKind.Artifacts).Open(PaneKind.Browser).Open(PaneKind.SideChat);
        foreach (var (w, h) in new[] { (1600.0, 900.0), (1000.0, 1400.0), (2560.0, 500.0) })
        {
            var ltr = PaneLayout.Solve(state, w, h);
            var rtl = PaneLayout.Solve(state, w, h, rightToLeft: true);
            PaneLayoutChecks.AssertNone(PaneLayoutChecks.MirrorProblems(ltr, rtl));
            PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(state, rtl));
            Assert.Equal(w, rtl.Chat.Right, 9);
        }
        // A drag reads the same in both directions: the side gutter moved 40 DIP toward the chat widens Side chat by 40.
        var side = PaneLayout.Solve(state, 1600, 900, rightToLeft: true).Gutter(PaneGutterKeys.Side)!;
        var moved = PaneLayout.Drag(state, 1600, 900, PaneGutterKeys.Side, side.ValueAt(side.Position + 40), rightToLeft: true);
        Assert.Equal(side.Value + 40, PaneLayout.Solve(moved, 1600, 900, rightToLeft: true).Gutter(PaneGutterKeys.Side)!.Value, 9);
    }

    [Fact]
    public void The_same_inputs_give_the_same_layout_in_a_thousand_random_cases()
    {
        var random = new Random(20261011);
        for (var i = 0; i < 1000; i++)
        {
            var state = PaneLayoutChecks.RandomState(random);
            var copy = state with
            {
                Order = new ValueList<PaneSlot>(state.Order.Select(s => new PaneSlot(new ValueList<PaneKind>(s.Panes.ToArray())))),
                Ratios = new ValueMap<string, double>(state.Ratios.Reverse()),
            };
            double w = 320 + random.NextDouble() * 3520, h = 320 + random.NextDouble() * 3520;
            var rtl = random.Next(2) == 0;
            var first = PaneLayout.Solve(state, w, h, rtl);
            Assert.Equal(first, PaneLayout.Solve(state, w, h, rtl));
            Assert.Equal(first, PaneLayout.Solve(copy, w, h, rtl));
        }
    }

    [Fact]
    public void Expand_covers_the_whole_workspace_over_the_unchanged_tiles()
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks).Open(PaneKind.Browser);
        var tiles = PaneLayout.Solve(state, 1600, 900);
        var expanded = PaneLayout.Solve(state.Expand(PaneKind.Browser), 1600, 900);
        Assert.Equal(PaneKind.Browser, expanded.Expanded);
        Assert.Equal(new PaneRect(0, 0, 1600, 900), expanded.ExpandedBounds);
        Assert.Equal(tiles.Cells, expanded.Cells);
        Assert.Equal(tiles.Chat, expanded.Chat);
        Assert.Null(PaneLayout.Solve(state.Expand(PaneKind.Browser), 700, 900).Expanded);
        Assert.Null(state.Expand(PaneKind.Chat).Expanded);
    }

    [Fact]
    public void Bad_data_never_throws_and_unknown_panes_are_dropped_from_view()
    {
        var state = new PaneWorkspaceState
        {
            Order = new ValueList<PaneSlot>(
            [
                PaneSlot.Of((PaneKind)42, PaneKind.Browser), PaneSlot.Of(PaneKind.Chat), PaneSlot.Of(), PaneSlot.Of(PaneKind.Browser, PaneKind.SideChat),
                PaneSlot.Of(PaneKind.Terminal),
            ]),
            IsTerminalInDrawer = true,
            Ratios = new ValueMap<string, double>(new Dictionary<string, double> { ["chat"] = double.NaN, ["future"] = 3 }),
            Collapsed = new ValueSet<string>(["future"]),
            Expanded = (PaneKind)77,
            NarrowSelection = (PaneKind)99,
            RecentTabs = new ValueList<PaneKind>([(PaneKind)5000, PaneKind.Browser, PaneKind.Browser]),
        };
        var normal = state.Normalized();
        Assert.Equal(new[] { PaneKind.Browser, PaneKind.Terminal }, normal.GridPanes);
        Assert.False(normal.IsTerminalInDrawer);
        Assert.False(normal.Ratios.ContainsKey("chat"));
        Assert.Null(normal.Expanded);
        Assert.Equal(PaneKind.Chat, normal.NarrowSelection);
        Assert.Equal(new[] { PaneKind.Browser }, normal.RecentTabs);
        foreach (var (w, h) in new[] { (0.0, 0.0), (double.NaN, 800.0), (320.0, 320.0), (1600.0, 900.0), (-5.0, double.PositiveInfinity) })
        {
            var layout = PaneLayout.Solve(state, w, h);
            Assert.DoesNotContain((PaneKind)42, layout.Bounds.Keys);
            PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(state, layout));
        }
    }

    private static void Rect(PaneLayoutResult layout, PaneKind kind, out PaneRect rect)
    {
        Assert.True(layout.IsShown(kind), $"{kind} not shown");
        rect = layout.BoundsOf(kind)!.Value;
    }

    private static IEnumerable<PaneKind[]> Permutations(PaneKind[] items) => items.Length <= 1
        ? [items]
        : items.SelectMany((item, i) => Permutations([.. items.Where((_, j) => j != i)]).Select(rest => (PaneKind[])[item, .. rest]));
}
