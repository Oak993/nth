using Marrow.Core.Models;

namespace Marrow.Core.Tests.Web;

// nth web surfaces, unit W1: the shared splitter's arithmetic (work/design/web-surfaces/DESIGN.md 4.2 and 13.1
// SplitterMathTests). Every expected number is the design's: steps 8 and 48 DIP, resistance 0.3 for at most 24 DIP,
// collapse 56 DIP past the limit, the sidebar's 210 / 268 / 440, the terminal's 140 / 260 / 760 with "under 84 closes",
// the chat gutter's 400 to 1,294, and Chrome.PanelSlide's spring (0.32 s, 0.9) or its 0.18 s ease-out.
[Trait("Decision", "ED-NEW-web-w1-1")]
public sealed class SplitterMathTests
{
    private static readonly SplitterLimits Sidebar = new(210, 440, 268)
    {
        Collapse = SplitterCollapse.BelowMinimum, CollapsedValue = 0, CollapsedText = "Sidebar hidden",
    };

    private static readonly SplitterLimits Terminal = new(140, 760, 260)
    {
        Collapse = SplitterCollapse.BelowMinimum, CollapsedValue = 0, Growth = -1, CollapsedText = "Terminal closed",
    };

    private static readonly SplitterLimits ChatGutter = new(400, 1200, 700)
    {
        Collapse = SplitterCollapse.AboveMaximum, CollapsedValue = 1294, CollapsedText = "Panes collapsed",
    };

    private static readonly SplitterLimits Plain = new(300, 900, 500);

    [Fact]
    public void Clamp_keeps_a_value_between_its_limits_and_never_inverts_them()
    {
        Assert.Equal(210, SplitterMath.Clamp(100, 210, 440));
        Assert.Equal(440, SplitterMath.Clamp(500, 210, 440));
        Assert.Equal(300, SplitterMath.Clamp(300, 210, 440));
        Assert.Equal(210, SplitterMath.Clamp(500, 210, 100)); // a maximum under the minimum is the minimum
    }

    [Fact]
    public void Past_a_limit_the_value_resists_at_three_tenths_and_gives_at_most_24()
    {
        Assert.Equal(300, SplitterMath.RubberBand(300, 210, 440));
        Assert.Equal(210 - 3, SplitterMath.RubberBand(200, 210, 440), 6);
        Assert.Equal(440 + 6, SplitterMath.RubberBand(460, 210, 440), 6);
        Assert.Equal(210 - 24, SplitterMath.RubberBand(210 - 80, 210, 440), 6); // 80 * 0.3 = 24, the most it gives
        Assert.Equal(210 - 24, SplitterMath.RubberBand(-1000, 210, 440), 6);
        Assert.Equal(440 + 24, SplitterMath.RubberBand(5000, 210, 440), 6);
    }

    [Fact]
    public void Inside_the_limits_a_drag_tracks_the_pointer_one_to_one()
    {
        var start = new SplitterState(500);
        foreach (var travel in new[] { -200.0, -37.5, 0, 1, 12.25, 400 })
        {
            var drag = SplitterMath.Drag(start, travel, Plain);
            Assert.Equal(500 + travel, drag.Shown, 9);
            Assert.Equal(500 + travel, drag.State.Value, 9);
            Assert.False(drag.State.IsCollapsed);
        }
    }

    [Fact]
    public void A_drag_past_a_limit_commits_the_limit_and_draws_the_rubber_band()
    {
        var drag = SplitterMath.Drag(new SplitterState(500), 500, Plain); // raw 1000, max 900
        Assert.Equal(900, drag.State.Value);
        Assert.Equal(900 + 24, drag.Shown, 6); // 100 * 0.3 = 30, held to 24
        var low = SplitterMath.Drag(new SplitterState(500), -230, Plain); // raw 270, min 300
        Assert.Equal(300, low.State.Value);
        Assert.Equal(300 - 9, low.Shown, 6);
    }

    [Fact]
    public void Under_Reduce_Motion_a_limit_is_a_hard_stop_with_no_rubber_band()
    {
        var drag = SplitterMath.Drag(new SplitterState(500), 500, Plain, reduceMotion: true);
        Assert.Equal(900, drag.Shown);
        Assert.Equal(900, drag.State.Value);
    }

    [Fact]
    public void Growth_minus_one_turns_downward_travel_into_a_smaller_trailing_pane()
    {
        // The terminal drawer: its height grows as its top edge moves up (negative y travel).
        Assert.Equal(300, SplitterMath.Drag(new SplitterState(260), -40, Terminal).State.Value);
        Assert.Equal(220, SplitterMath.Drag(new SplitterState(260), 40, Terminal).State.Value);
    }

    [Fact]
    public void A_collapsible_side_collapses_only_beyond_56_DIP_past_its_limit()
    {
        // The terminal: "under 84 closes" is 140 - 56.
        var held = SplitterMath.Drag(new SplitterState(260), 260 - 84, Terminal); // raw exactly 84
        Assert.False(held.State.IsCollapsed);
        Assert.Equal(140, held.State.Value);
        var closed = SplitterMath.Drag(new SplitterState(260), 260 - 83.9, Terminal); // raw 83.9
        Assert.True(closed.State.IsCollapsed);
        Assert.Equal(0, closed.State.Value);
        Assert.Equal(0, closed.Shown);
        Assert.Equal(260, closed.State.BeforeCollapse);

        var panes = SplitterMath.Drag(new SplitterState(700), 1200 + 56.5 - 700, ChatGutter);
        Assert.True(panes.State.IsCollapsed);
        Assert.Equal(1294, panes.Shown);
    }

    [Fact]
    public void A_side_that_cannot_collapse_only_clamps_however_far_the_pointer_goes()
    {
        var drag = SplitterMath.Drag(new SplitterState(500), -5000, Plain);
        Assert.False(drag.State.IsCollapsed);
        Assert.Equal(300, drag.State.Value);
        Assert.Equal(300 - 24, drag.Shown, 6);
    }

    [Fact]
    public void Dragging_a_collapsed_side_back_over_the_threshold_restores_it_under_the_pointer()
    {
        var collapsed = new SplitterState(1294, true, 640);
        var still = SplitterMath.Drag(collapsed, -30, ChatGutter); // raw 1264 > 1256
        Assert.True(still.State.IsCollapsed);
        Assert.Equal(640, still.State.BeforeCollapse); // still remembers the size before the collapse
        var back = SplitterMath.Drag(collapsed, -100, ChatGutter); // raw 1194, inside the limits
        Assert.False(back.State.IsCollapsed);
        Assert.Equal(1194, back.State.Value);
        Assert.Equal(1194, back.Shown);
    }

    [Fact]
    public void Collapse_then_restore_returns_the_exact_size_before_the_collapse()
    {
        foreach (var value in new[] { 211.0, 268, 300.25, 439.5, 440 })
        {
            var open = new SplitterState(value);
            var collapsed = SplitterMath.Apply(open, new SplitterCommand(SplitterCommandKind.Collapse), Sidebar);
            Assert.True(collapsed.IsCollapsed);
            Assert.Equal(0, collapsed.Value);
            var restored = SplitterMath.Apply(collapsed, new SplitterCommand(SplitterCommandKind.Restore), Sidebar);
            Assert.Equal(new SplitterState(value), restored);
        }
    }

    [Fact]
    public void Collapsing_at_the_minimum_restores_to_the_default()
    {
        var collapsed = SplitterMath.Apply(new SplitterState(210), new SplitterCommand(SplitterCommandKind.Collapse), Sidebar);
        Assert.Null(collapsed.BeforeCollapse);
        Assert.Equal(268, SplitterMath.Apply(collapsed, new SplitterCommand(SplitterCommandKind.Restore), Sidebar).Value);

        // A collapse by dragging remembers the size before the drag, or the default when the drag began at the minimum.
        var dragged = SplitterMath.Drag(new SplitterState(400), -400, Sidebar);
        Assert.Equal(400, SplitterMath.Apply(dragged.State, new SplitterCommand(SplitterCommandKind.Restore), Sidebar).Value);
        var fromMinimum = SplitterMath.Drag(new SplitterState(210), -400, Sidebar);
        Assert.Equal(268, SplitterMath.Apply(fromMinimum.State, new SplitterCommand(SplitterCommandKind.Restore), Sidebar).Value);
        var panes = SplitterMath.Drag(new SplitterState(1200), 400, ChatGutter);
        Assert.Equal(700, SplitterMath.Apply(panes.State, new SplitterCommand(SplitterCommandKind.Restore), ChatGutter).Value);
    }

    [Fact]
    public void Toggle_collapses_an_open_side_and_restores_a_collapsed_one_and_a_side_without_collapse_ignores_it()
    {
        var open = new SplitterState(300);
        var toggled = SplitterMath.Apply(open, new SplitterCommand(SplitterCommandKind.Toggle), Sidebar);
        Assert.True(toggled.IsCollapsed);
        Assert.Equal(open, SplitterMath.Apply(toggled, new SplitterCommand(SplitterCommandKind.Toggle), Sidebar));
        Assert.Equal(open, SplitterMath.Apply(open, new SplitterCommand(SplitterCommandKind.Collapse), Plain with { Default = 300 }));
        Assert.Equal(open, SplitterMath.Apply(open, new SplitterCommand(SplitterCommandKind.Restore), Sidebar));
    }

    [Fact]
    public void Double_click_and_Enter_reset_to_the_default_from_any_state()
    {
        var reset = new SplitterCommand(SplitterCommandKind.Reset);
        Assert.Equal(new SplitterState(268), SplitterMath.Apply(new SplitterState(410), reset, Sidebar));
        Assert.Equal(new SplitterState(268), SplitterMath.Apply(new SplitterState(0, true, 410), reset, Sidebar));
        Assert.Equal(new SplitterState(260), SplitterMath.Apply(new SplitterState(700), reset, Terminal));
    }

    [Fact]
    public void Arrow_steps_move_8_or_48_with_Shift_and_clamp_and_do_nothing_while_collapsed()
    {
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 8), SplitterMath.Command(SplitterKey.Right, false, false, SplitterOrientation.Vertical));
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, -48), SplitterMath.Command(SplitterKey.Left, true, false, SplitterOrientation.Vertical));
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 0, 8), SplitterMath.Command(SplitterKey.Down, false, false, SplitterOrientation.Horizontal));
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 0, -48), SplitterMath.Command(SplitterKey.Up, true, false, SplitterOrientation.Horizontal));
        // Off-axis arrows do nothing on a divider.
        Assert.Equal(SplitterCommand.None, SplitterMath.Command(SplitterKey.Up, false, false, SplitterOrientation.Vertical));
        Assert.Equal(SplitterCommand.None, SplitterMath.Command(SplitterKey.Right, false, false, SplitterOrientation.Horizontal));

        Assert.Equal(276, SplitterMath.Apply(new SplitterState(268), new SplitterCommand(SplitterCommandKind.Step, 8), Sidebar).Value);
        Assert.Equal(440, SplitterMath.Apply(new SplitterState(430), new SplitterCommand(SplitterCommandKind.Step, 48), Sidebar).Value);
        Assert.Equal(210, SplitterMath.Apply(new SplitterState(230), new SplitterCommand(SplitterCommandKind.Step, -48), Sidebar).Value);
        // The terminal: Up (negative y) grows the drawer.
        Assert.Equal(268, SplitterMath.Apply(new SplitterState(260), new SplitterCommand(SplitterCommandKind.Step, 0, -8), Terminal, SplitterOrientation.Horizontal).Value);
        var collapsed = new SplitterState(0, true, 300);
        Assert.Equal(collapsed, SplitterMath.Apply(collapsed, new SplitterCommand(SplitterCommandKind.Step, 8), Sidebar));
    }

    [Fact]
    public void Home_and_End_go_to_the_minimum_and_maximum_and_Enter_resets()
    {
        Assert.Equal(SplitterCommandKind.ToMinimum, SplitterMath.Command(SplitterKey.Home, false, false, SplitterOrientation.Vertical).Kind);
        Assert.Equal(SplitterCommandKind.ToMaximum, SplitterMath.Command(SplitterKey.End, false, false, SplitterOrientation.Horizontal).Kind);
        Assert.Equal(SplitterCommandKind.Reset, SplitterMath.Command(SplitterKey.Enter, false, false, SplitterOrientation.Vertical).Kind);
        Assert.Equal(new SplitterState(210), SplitterMath.Apply(new SplitterState(0, true, 300), new SplitterCommand(SplitterCommandKind.ToMinimum), Sidebar));
        Assert.Equal(new SplitterState(760), SplitterMath.Apply(new SplitterState(260), new SplitterCommand(SplitterCommandKind.ToMaximum), Terminal));
    }

    [Fact]
    public void Space_toggles_and_Ctrl_Arrow_toward_the_collapsible_side_collapses_it_and_away_restores_it()
    {
        var leading = SplitterSide.Leading;
        Assert.Equal(SplitterCommandKind.Toggle, SplitterMath.Command(SplitterKey.Space, false, false, SplitterOrientation.Vertical, collapseSide: leading).Kind);
        Assert.Equal(SplitterCommandKind.None, SplitterMath.Command(SplitterKey.Space, false, false, SplitterOrientation.Vertical).Kind);
        Assert.Equal(SplitterCommandKind.Collapse, SplitterMath.Command(SplitterKey.Left, false, true, SplitterOrientation.Vertical, collapseSide: leading).Kind);
        Assert.Equal(SplitterCommandKind.Restore, SplitterMath.Command(SplitterKey.Right, false, true, SplitterOrientation.Vertical, collapseSide: leading).Kind);
        // The terminal drawer collapses toward the bottom (trailing): Ctrl+Down closes it, Ctrl+Up brings it back.
        var trailing = SplitterMath.CollapseSide(Terminal);
        Assert.Equal(SplitterSide.Trailing, trailing);
        Assert.Equal(SplitterCommandKind.Collapse, SplitterMath.Command(SplitterKey.Down, false, true, SplitterOrientation.Horizontal, collapseSide: trailing).Kind);
        Assert.Equal(SplitterCommandKind.Restore, SplitterMath.Command(SplitterKey.Up, false, true, SplitterOrientation.Horizontal, collapseSide: trailing).Kind);
        // Ctrl with nothing collapsible is a plain step.
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 8), SplitterMath.Command(SplitterKey.Right, false, true, SplitterOrientation.Vertical));
    }

    [Fact]
    public void The_collapsible_side_follows_from_the_collapsing_end_and_the_growth()
    {
        Assert.Equal(SplitterSide.Leading, SplitterMath.CollapseSide(Sidebar));
        Assert.Equal(SplitterSide.Trailing, SplitterMath.CollapseSide(Terminal));
        Assert.Equal(SplitterSide.Trailing, SplitterMath.CollapseSide(ChatGutter));
        Assert.Equal(SplitterSide.Leading, SplitterMath.CollapseSide(ChatGutter with { Growth = -1 }));
        Assert.Equal(SplitterSide.None, SplitterMath.CollapseSide(Plain));
    }

    [Fact]
    public void Right_to_left_mirrors_Left_and_Right_so_they_follow_the_visual_direction()
    {
        // Visually right in a mirrored layout is toward the leading side: logical travel negative.
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, -8), SplitterMath.Command(SplitterKey.Right, false, false, SplitterOrientation.Vertical, mirrored: true));
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 48), SplitterMath.Command(SplitterKey.Left, true, false, SplitterOrientation.Vertical, mirrored: true));
        // The sidebar sits on the right in RTL: Ctrl+Right (toward it) collapses it.
        Assert.Equal(SplitterCommandKind.Collapse, SplitterMath.Command(SplitterKey.Right, false, true, SplitterOrientation.Vertical, true, SplitterSide.Leading).Kind);
        Assert.Equal(SplitterCommandKind.Restore, SplitterMath.Command(SplitterKey.Left, false, true, SplitterOrientation.Vertical, true, SplitterSide.Leading).Kind);
        // A horizontal divider is not mirrored.
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 0, 8), SplitterMath.Command(SplitterKey.Down, false, false, SplitterOrientation.Horizontal, mirrored: true));
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, -8, 0), SplitterMath.Command(SplitterKey.Right, false, false, SplitterOrientation.Both, mirrored: true));
    }

    [Fact]
    public void A_corner_moves_width_with_Left_and_Right_and_height_with_Up_and_Down()
    {
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 8, 0), SplitterMath.Command(SplitterKey.Right, false, false, SplitterOrientation.Both));
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, -48, 0), SplitterMath.Command(SplitterKey.Left, true, false, SplitterOrientation.Both));
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 0, 8), SplitterMath.Command(SplitterKey.Down, false, true, SplitterOrientation.Both));
        Assert.Equal(new SplitterCommand(SplitterCommandKind.Step, 0, -8), SplitterMath.Command(SplitterKey.Up, false, false, SplitterOrientation.Both));
        Assert.Equal(SplitterCommandKind.Reset, SplitterMath.Command(SplitterKey.Enter, false, false, SplitterOrientation.Both).Kind);
        Assert.Equal(SplitterCommandKind.None, SplitterMath.Command(SplitterKey.Home, false, false, SplitterOrientation.Both).Kind);
        Assert.Equal(SplitterCommandKind.None, SplitterMath.Command(SplitterKey.Space, false, false, SplitterOrientation.Both, collapseSide: SplitterSide.Leading).Kind);
    }

    [Fact]
    public void A_hidden_sidebar_reports_0_of_0_to_440_named_Sidebar_hidden()
    {
        var report = SplitterMath.Report(new SplitterState(0, true, 300), Sidebar);
        Assert.Equal(0, report.Value);
        Assert.Equal(0, report.Minimum);
        Assert.Equal(440, report.Maximum);
        Assert.True(report.IsCollapsed);
        Assert.Equal("Sidebar hidden", report.Text);
        Assert.Equal("Sidebar hidden", report.Readout);
        var open = SplitterMath.Report(new SplitterState(268), Sidebar);
        Assert.Equal((268, 0, 440, "268", "268 DIP", 61), (open.Value, open.Minimum, open.Maximum, open.Text, open.Readout, open.Percent));
    }

    [Fact]
    public void Collapsed_panes_report_the_far_end_of_a_range_that_includes_it()
    {
        var report = SplitterMath.Report(new SplitterState(1294, true), ChatGutter);
        Assert.Equal((1294, 400, 1294, "Panes collapsed", 100), (report.Value, report.Minimum, report.Maximum, report.Text, report.Percent));
    }

    [Fact]
    public void The_reported_value_always_lies_inside_its_range_for_any_input()
    {
        var random = new Random(4021);
        SplitterLimits[] all = [Sidebar, Terminal, ChatGutter, Plain];
        for (var i = 0; i < 10_000; i++)
        {
            var limits = all[random.Next(all.Length)];
            var state = new SplitterState(random.NextDouble() * 4000 - 1000, random.Next(4) == 0, random.Next(2) == 0 ? null : random.NextDouble() * 900);
            var travel = random.NextDouble() * 3000 - 1500;
            var drag = SplitterMath.Drag(state, travel, limits, random.Next(2) == 0);
            foreach (var report in new[] { SplitterMath.Report(state, limits), SplitterMath.Report(drag.State, limits), SplitterMath.Report(drag.Shown, drag.State.IsCollapsed, limits) })
            {
                Assert.InRange(report.Value, report.Minimum, report.Maximum);
                Assert.InRange(report.Percent, 0, 100);
            }
            Assert.InRange(drag.State.IsCollapsed ? limits.CollapsedValue : drag.State.Value, limits.RangeMinimum, limits.RangeMaximum);
            Assert.InRange(drag.Shown, Math.Min(limits.CollapsedValue, limits.Minimum - SplitterMath.MaximumGive), Math.Max(limits.CollapsedValue, limits.Maximum + SplitterMath.MaximumGive));
        }
    }

    [Fact]
    public void The_spring_is_PanelSlide_and_lands_on_its_target_without_overshooting_much()
    {
        var spring = new SplitterSpring(0);
        spring.SetTarget(100);
        var peak = 0.0;
        var elapsed = 0.0;
        while (spring.Step(1.0 / 120) && elapsed < 2) { elapsed += 1.0 / 120; peak = Math.Max(peak, spring.Value); }
        Assert.Equal(100, spring.Value);
        Assert.InRange(elapsed, 0.3, 0.8); // a 0.32 s response with damping 0.9 settles in about half a second
        Assert.InRange(peak, 99, 101); // damping 0.9: well under a percent of overshoot
        Assert.False(spring.IsMoving);
    }

    [Fact]
    public void A_new_target_bends_the_motion_keeping_its_velocity()
    {
        var spring = new SplitterSpring(0);
        spring.SetTarget(100);
        for (var i = 0; i < 6; i++) spring.Step(1.0 / 60);
        var velocity = spring.Velocity;
        var value = spring.Value;
        Assert.True(velocity > 0);
        spring.SetTarget(-50);
        Assert.Equal(velocity, spring.Velocity);
        Assert.Equal(value, spring.Value);
        spring.Step(1.0 / 60);
        Assert.True(spring.Value > value); // still carried forward for a frame before it turns
        while (spring.Step(1.0 / 60)) { }
        Assert.Equal(-50, spring.Value);
    }

    [Fact]
    public void Under_Reduce_Motion_a_target_runs_the_018_second_ease_out()
    {
        var spring = new SplitterSpring(0);
        spring.SetTarget(100, reduceMotion: true);
        spring.Step(0.09);
        Assert.Equal(100 * SplitterMath.EaseOut(0.5), spring.Value, 9);
        Assert.True(spring.IsMoving);
        spring.Step(0.09);
        Assert.Equal(100, spring.Value);
        Assert.False(spring.IsMoving);
        Assert.Equal(0.0, SplitterMath.EaseOut(0));
        Assert.Equal(1.0, SplitterMath.EaseOut(1));
        Assert.InRange(SplitterMath.EaseOut(0.5), 0.6, 0.7); // ease-out (0, 0, 0.58, 1) is past halfway at half time
    }

    [Fact]
    public void Pointer_down_settles_an_in_flight_spring_on_its_target_in_the_same_frame()
    {
        var model = new SplitterModel(Plain, new SplitterState(500));
        model.Apply(new SplitterCommand(SplitterCommandKind.Reset)); // springing nowhere: already 500
        model.Apply(new SplitterCommand(SplitterCommandKind.ToMaximum)); // springs from 500 to 900
        model.Shown.Step(0.06); // a toggle 60 ms earlier, still in flight
        Assert.True(model.Shown.IsMoving);
        Assert.InRange(model.Shown.Value, 501, 899);
        model.PointerDown();
        Assert.False(model.Shown.IsMoving);
        Assert.Equal(900, model.Shown.Value);
    }

    [Fact]
    public void A_drag_begun_during_a_spring_starts_from_the_settled_layout_and_tracks_one_to_one()
    {
        var model = new SplitterModel(Plain, new SplitterState(500));
        model.Apply(new SplitterCommand(SplitterCommandKind.ToMinimum)); // springs toward 300
        model.Shown.Step(0.06);
        model.BeginDrag();
        Assert.Equal(300, model.Shown.Value);
        model.Drag(40);
        Assert.False(model.Shown.IsMoving); // no spring while the pointer is down
        Assert.Equal(340, model.Shown.Value);
        model.Drag(41.5);
        Assert.Equal(341.5, model.Shown.Value);
    }

    [Fact]
    public void A_collapse_mid_drag_springs_then_tracking_resumes_and_release_springs_back_from_the_band()
    {
        var model = new SplitterModel(Terminal, new SplitterState(260));
        model.BeginDrag();
        Assert.True(model.Drag(200)); // raw 60: under 84, the drawer collapses
        Assert.True(model.State.IsCollapsed);
        Assert.True(model.Shown.IsMoving);
        Assert.Equal(0, model.Shown.Target);
        Assert.True(model.Drag(100)); // raw 160: dragged back, it restores toward the pointer with a spring
        Assert.False(model.State.IsCollapsed);
        Assert.Equal(160, model.Shown.Target);
        while (model.Shown.Step(1.0 / 60)) { }
        model.Drag(90); // landed: 1:1 again
        Assert.False(model.Shown.IsMoving);
        Assert.Equal(170, model.Shown.Value);
        model.Drag(-560); // raw 820, past the 760 maximum: the band
        Assert.Equal(760, model.State.Value);
        Assert.Equal(760 + 18, model.Shown.Value, 6);
        model.EndDrag();
        Assert.True(model.Shown.IsMoving);
        while (model.Shown.Step(1.0 / 60)) { }
        Assert.Equal(760, model.Shown.Value);
    }

    [Fact]
    public void New_limits_clamp_the_committed_value_and_the_drawn_one_at_rest()
    {
        var model = new SplitterModel(Plain, new SplitterState(850));
        model.Limits = Plain with { Maximum = 700 };
        Assert.Equal(700, model.State.Value);
        Assert.Equal(700, model.Shown.Value);
        Assert.Equal(new SplitterState(0, true, 300), new SplitterModel(Sidebar, new SplitterState(55, true, 300)).State);
    }

    [Fact]
    public void A_value_loaded_from_elsewhere_lands_at_once_clamped_and_never_moves_a_held_divider()
    {
        var model = new SplitterModel(Plain, new SplitterState(500));
        model.Apply(new SplitterCommand(SplitterCommandKind.ToMaximum)); // springing toward 900
        model.Shown.Step(0.06);
        model.Load(new SplitterState(640)); // another window wrote the setting: no spring from a stale place
        Assert.Equal(new SplitterState(640), model.State);
        Assert.False(model.Shown.IsMoving);
        Assert.Equal(640, model.Shown.Value);
        model.Load(new SplitterState(5000));
        Assert.Equal(900, model.State.Value);
        Assert.Equal(900, model.Shown.Value);

        model.BeginDrag();
        model.Drag(-100);
        model.Load(new SplitterState(400)); // the pointer owns the divider while it is held
        Assert.Equal(800, model.State.Value);
        Assert.Equal(800, model.Shown.Value);
    }
}
