using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-17 (DESIGN.md rev 5, 9.4 "Layout", "Narrow widths (under 640 px) turn the chart into an indented list"; the canvas's .org; ED-NEW-dextra-as17-1): where the
// org chart puts Dextra's node, the Asturs' and the connectors that join them, at wide and narrow widths and with 0 to 6 Asturs.
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-1")]
public sealed class OrgChartLayoutTests
{
    [Fact]
    public void At_640_px_and_up_it_is_the_chart_and_under_640_it_is_the_list()
    {
        Assert.Equal(OrgLayoutMode.List, OrgChartLayout.ModeFor(639.9));
        Assert.Equal(OrgLayoutMode.Chart, OrgChartLayout.ModeFor(640));
        Assert.Equal(OrgLayoutMode.List, OrgChartLayout.ModeFor(375));
        Assert.Equal(OrgLayoutMode.Chart, OrgChartLayout.ModeFor(1500));
    }

    [Fact]
    public void Nodes_are_measured_at_the_canvas_widths_in_the_chart_and_across_the_list_in_the_narrow_form()
    {
        Assert.Equal((220.0, 176.0), OrgChartLayout.Widths(900));
        Assert.Equal((375.0, 345.0), OrgChartLayout.Widths(375));
        Assert.Equal((0.0, 0.0), OrgChartLayout.Widths(0));
    }

    [Fact]
    public void Three_Asturs_sit_in_one_centred_row_under_Dextra_with_the_canvas_gaps()
    {
        var plan = OrgChartLayout.Plan(900, 120, OrgPlanChecks.Heights(3));

        Assert.Equal(OrgLayoutMode.Chart, plan.Mode);
        Assert.Equal(new OrgRect(340, 8, 220, 120), plan.Root);                                  // centred, 8 px from the top
        Assert.Equal([new OrgRect(174, 162, 176, 150), new OrgRect(362, 162, 176, 150), new OrgRect(550, 162, 176, 150)], plan.Kids); // 34 px lane, 12 px gaps
        Assert.Equal(316, plan.Height);                                                           // 162 + 150 + 4
        Assert.Null(OrgPlanChecks.NodesProblem(plan));
        Assert.Null(OrgPlanChecks.ConnectorsProblem(plan));
    }

    [Fact]
    public void The_stem_runs_straight_down_to_an_Astur_directly_under_Dextra_and_the_others_hang_from_a_bus_17_px_above_the_row()
    {
        var plan = OrgChartLayout.Plan(900, 120, OrgPlanChecks.Heights(3));

        Assert.Equal(
        [
            [new OrgPoint(450, 128), new OrgPoint(450, 162)],                                         // the stem goes on into the middle node
            [new OrgPoint(450, 145), new OrgPoint(262, 145), new OrgPoint(262, 162)],                 // the left arm: along the bus, down
            [new OrgPoint(450, 145), new OrgPoint(638, 145), new OrgPoint(638, 162)],
        ], plan.Connectors.Select(c => c.Points.ToArray()).ToArray());
    }

    [Fact]
    public void An_even_row_has_a_stem_that_stops_at_the_bus_and_arms_with_the_nearer_nodes_hanging_straight_down()
    {
        var plan = OrgChartLayout.Plan(900, 120, OrgPlanChecks.Heights(4));

        Assert.Equal([80.0, 268.0, 456.0, 644.0], plan.Kids.Select(k => k.X));
        Assert.Equal(
        [
            [new OrgPoint(450, 128), new OrgPoint(450, 145)],                                         // no node under Dextra: the stem stops at the bus
            [new OrgPoint(450, 145), new OrgPoint(168, 145), new OrgPoint(168, 162)],                 // the left arm ends at the farthest node
            [new OrgPoint(356, 145), new OrgPoint(356, 162)],                                         // the nearer one hangs from it
            [new OrgPoint(450, 145), new OrgPoint(732, 145), new OrgPoint(732, 162)],
            [new OrgPoint(544, 145), new OrgPoint(544, 162)],
        ], plan.Connectors.Select(c => c.Points.ToArray()).ToArray());
        foreach (var kid in plan.Kids) Assert.True(OrgPlanChecks.Reaches(plan, kid), kid.ToString());
        Assert.True(OrgPlanChecks.Connected(plan));
    }

    [Fact]
    public void Nodes_in_a_row_are_as_tall_as_the_tallest_so_a_big_text_size_never_leaves_a_ragged_row()
    {
        var plan = OrgChartLayout.Plan(900, 120, [100, 180, 140]);

        Assert.All(plan.Kids, kid => Assert.Equal(180, kid.Height));
        Assert.Equal(8 + 120 + 34 + 180 + 4, plan.Height);
    }

    [Fact]
    public void No_Astur_leaves_only_Dextra_with_no_connector()
    {
        var plan = OrgChartLayout.Plan(900, 120, []);

        Assert.Empty(plan.Kids);
        Assert.Empty(plan.Connectors);
        Assert.Equal(new OrgRect(340, 8, 220, 120), plan.Root);
        Assert.Equal(132, plan.Height);
    }

    [Fact]
    public void Six_Asturs_wrap_into_two_equal_rows_at_640_px_and_the_second_row_hangs_from_a_spine_in_the_margin()
    {
        var plan = OrgChartLayout.Plan(640, 120, OrgPlanChecks.Heights(6));

        Assert.Equal(3, OrgChartLayout.PerRow(640));
        Assert.Equal([44.0, 232.0, 420.0, 44.0, 232.0, 420.0], plan.Kids.Select(k => k.X));        // 3 and 3, each row centred
        Assert.Equal([162.0, 162.0, 162.0, 324.0, 324.0, 324.0], plan.Kids.Select(k => k.Y));      // 12 px between rows
        Assert.Equal(478, plan.Height);                                                           // 324 + 150 + 4
        var spine = plan.Connectors.Single(c => c.Points.Count == 5).Points;                      // stem's bus, along to the margin lane, down it, along the last bus, down
        Assert.Equal([new OrgPoint(320, 145), new OrgPoint(38, 145), new OrgPoint(38, 318), new OrgPoint(508, 318), new OrgPoint(508, 324)], spine);
        Assert.Null(OrgPlanChecks.NodesProblem(plan));
        Assert.Null(OrgPlanChecks.ConnectorsProblem(plan));
        Assert.True(OrgPlanChecks.Connected(plan));
        foreach (var kid in plan.Kids) Assert.True(OrgPlanChecks.Reaches(plan, kid), kid.ToString());
    }

    [Fact]
    public void Five_Asturs_wrap_as_three_and_two_and_the_short_row_stays_centred()
    {
        var plan = OrgChartLayout.Plan(700, 120, OrgPlanChecks.Heights(5));

        Assert.Equal([3, 2], new[] { plan.Kids.Count(k => k.Y < 200), plan.Kids.Count(k => k.Y > 200) });
        var secondRow = plan.Kids.Where(k => k.Y > 200).ToList();
        Assert.Equal((700 - (2 * 176 + 12)) / 2.0, secondRow[0].X);
        Assert.Null(OrgPlanChecks.NodesProblem(plan));
        Assert.Null(OrgPlanChecks.ConnectorsProblem(plan));
    }

    [Fact]
    public void Three_rows_keep_every_node_joined_through_one_spine()
    {
        var plan = OrgChartLayout.Plan(640, 100, [90, 90, 90, 120, 120, 120, 70, 70, 70]);

        Assert.Equal(3, plan.Kids.Select(k => k.Y).Distinct().Count());
        Assert.Null(OrgPlanChecks.NodesProblem(plan));
        Assert.Null(OrgPlanChecks.ConnectorsProblem(plan));
        Assert.True(OrgPlanChecks.Connected(plan));
        foreach (var kid in plan.Kids) Assert.True(OrgPlanChecks.Reaches(plan, kid), kid.ToString());
    }

    [Fact]
    public void Every_width_and_team_size_keeps_nodes_apart_connectors_off_nodes_and_every_node_joined()
    {
        for (var width = 640.0; width <= 1500; width += 23)
        for (var asturs = 0; asturs <= AsturShapes.MostAsturs; asturs++)
        {
            var heights = Enumerable.Range(0, asturs).Select(i => 120.0 + 17 * i).ToList();
            var plan = OrgChartLayout.Plan(width, 130, heights);
            var where = $"{width} px, {asturs} Asturs: ";
            Assert.True(OrgPlanChecks.NodesProblem(plan) is null, where + OrgPlanChecks.NodesProblem(plan));
            Assert.True(OrgPlanChecks.ConnectorsProblem(plan) is null, where + OrgPlanChecks.ConnectorsProblem(plan));
            Assert.True(OrgPlanChecks.Connected(plan), where + "the connectors are not one tree");
            foreach (var kid in plan.Kids) Assert.True(OrgPlanChecks.Reaches(plan, kid), where + kid);
        }
    }

    [Fact]
    public void The_narrow_list_indents_the_Asturs_under_Dextra_with_a_trunk_and_an_elbow_to_each()
    {
        var plan = OrgChartLayout.Plan(375, 90, OrgPlanChecks.Heights(3, 80));

        Assert.Equal(OrgLayoutMode.List, plan.Mode);
        Assert.Equal(new OrgRect(0, 8, 375, 90), plan.Root);
        Assert.Equal([new OrgRect(30, 108, 345, 80), new OrgRect(30, 196, 345, 80), new OrgRect(30, 284, 345, 80)], plan.Kids);   // 10 px under Dextra, 8 px apart
        Assert.Equal(368, plan.Height);
        Assert.Equal(
        [
            [new OrgPoint(18, 148), new OrgPoint(30, 148)],
            [new OrgPoint(18, 236), new OrgPoint(30, 236)],
            [new OrgPoint(18, 98), new OrgPoint(18, 324), new OrgPoint(30, 324)],                 // the trunk ends in the last elbow
        ], plan.Connectors.Select(c => c.Points.ToArray()).ToArray());
        Assert.Null(OrgPlanChecks.NodesProblem(plan));
        Assert.Null(OrgPlanChecks.ConnectorsProblem(plan));
        Assert.True(OrgPlanChecks.Connected(plan));
        foreach (var kid in plan.Kids) Assert.True(OrgPlanChecks.Reaches(plan, kid), kid.ToString());
    }

    [Fact]
    public void The_list_holds_at_every_narrow_width_down_to_the_smallest_a_pane_can_be()
    {
        for (var width = 240.0; width < 640; width += 31)
        for (var asturs = 0; asturs <= AsturShapes.MostAsturs; asturs++)
        {
            var plan = OrgChartLayout.Plan(width, 90, Enumerable.Range(0, asturs).Select(i => 70.0 + 9 * i).ToList());
            var where = $"{width} px, {asturs} Asturs: ";
            Assert.Equal(OrgLayoutMode.List, plan.Mode);
            Assert.True(OrgPlanChecks.NodesProblem(plan) is null, where + OrgPlanChecks.NodesProblem(plan));
            Assert.True(OrgPlanChecks.ConnectorsProblem(plan) is null, where + OrgPlanChecks.ConnectorsProblem(plan));
        }
    }

    [Fact]
    public void A_mirrored_plan_is_the_same_plan_flipped_inside_its_width_for_a_right_to_left_window()
    {
        var plan = OrgChartLayout.Plan(640, 120, OrgPlanChecks.Heights(4));
        var flipped = plan.Mirrored();

        Assert.Equal(plan.Kids.Select(k => 640 - k.Right), flipped.Kids.Select(k => k.X));
        Assert.Equal(plan.Root.CenterX, 640 - flipped.Root.CenterX, 6);
        Assert.Null(OrgPlanChecks.NodesProblem(flipped));
        Assert.Null(OrgPlanChecks.ConnectorsProblem(flipped));
        Assert.Equal(plan.Mirrored().Mirrored().Kids, plan.Kids);
    }

    [Fact]
    public void A_bend_is_rounded_by_8_px_or_by_less_where_a_leg_is_shorter()
    {
        Assert.Equal(8, OrgChartLayout.BendRadius(new OrgPoint(0, 0), new OrgPoint(100, 0), new OrgPoint(100, 50)));
        Assert.Equal(6, OrgChartLayout.BendRadius(new OrgPoint(0, 0), new OrgPoint(100, 0), new OrgPoint(100, 6)));
        Assert.Equal(4, OrgChartLayout.BendRadius(new OrgPoint(96, 0), new OrgPoint(100, 0), new OrgPoint(100, 60)));
    }
}
