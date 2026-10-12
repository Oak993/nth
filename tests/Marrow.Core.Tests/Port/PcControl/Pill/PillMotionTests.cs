// PORT-ONLY: the pill's whole pose over time (OD-0145, ED-NEW-pcctl-24; DESIGN.md 10.3, 10.6 "Glass, motion and one
// clock", 10.11 "MotionDriver"): the width grows about the anchor, every change keeps each curve's value and speed, the
// lift and the arrive and leave scales are poses (no fade; Reduce Motion fades at full size), the DPI change is a scale
// spring in place, the drawn body and the input shape are the same rect, and done means the final pose exactly.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class PillMotionTests
{
    private static PillMotion Centred(double width = 300, double now = 10) =>
        PillMotion.Still(new PillPose(new PillVector(960, 8), 0.5, width, 48, 0, 1, 1, 1, 1), now);

    [Fact]
    public void A_width_change_grows_about_the_anchor_on_the_width_spring()
    {
        var motion = Centred().Widen(10, 440, reduceMotion: false);
        var spring = PillCurve.Spring(300, 440, 0, PillCurve.WidthResponse, 10);

        foreach (var t in new[] { 10.0, 10.05, 10.1, 10.2 })
        {
            var pose = motion.At(t);
            Assert.Equal(spring.Value(t), pose.Width, 9);
            Assert.Equal(960, pose.Body.X + (pose.Body.Width / 2), 9);
        }
        Assert.Equal(new PillRect(740, 8, 440, 48), motion.Final.Body);
        Assert.False(motion.AtRest);
    }

    [Fact]
    public void Left_and_right_anchors_grow_away_from_their_edge()
    {
        var left = PillMotion.Still(new PillPose(new PillVector(16, 8), 0, 300, 48, 0, 1, 1, 1, 1), 0).Widen(0, 440, false);
        var right = PillMotion.Still(new PillPose(new PillVector(1904, 8), 1, 300, 48, 0, 1, 1, 1, 1), 0).Widen(0, 440, false);

        Assert.Equal(16, left.At(0.1).Body.X, 9);
        Assert.Equal(1904, right.At(0.1).Body.Right, 9);
        Assert.Equal(0, left.At(0.1).LocalLeft, 9);
        Assert.Equal(-right.At(0.1).Width, right.At(0.1).LocalLeft, 9);
    }

    [Fact]
    public void A_retarget_keeps_each_curve_s_value_and_speed()
    {
        var first = Centred().Widen(10, 440, false).TravelTo(10, new PillVector(400, 8), false);
        var second = first.Widen(10.1, 360, false).TravelTo(10.1, new PillVector(1500, 8), false);

        var before = first.At(10.1);
        var after = second.At(10.1);
        Assert.Equal(before.Width, after.Width, 9);
        Assert.Equal(before.Anchor.X, after.Anchor.X, 9);

        // the speed carries over: the instantaneous slope (a microsecond on) is the same on both
        const double h = 1e-6;
        var slope1 = (first.At(10.1 + h).Width - first.At(10.1).Width) / h;
        var slope2 = (second.At(10.1 + h).Width - second.At(10.1).Width) / h;
        Assert.True(Math.Abs(slope1 - slope2) <= Math.Abs(slope1) * 0.01 + 1, $"width speed {slope1} then {slope2}");
        var travel1 = (first.At(10.1 + h).Anchor.X - first.At(10.1).Anchor.X) / h;
        var travel2 = (second.At(10.1 + h).Anchor.X - second.At(10.1).Anchor.X) / h;
        Assert.True(Math.Abs(travel1 - travel2) <= Math.Abs(travel1) * 0.01 + 1, $"travel speed {travel1} then {travel2}");
    }

    [Fact]
    public void The_lift_is_a_scale_about_the_body_s_centre_and_reduce_motion_has_none()
    {
        var lifted = Centred(440).Lifted(10, true, reduceMotion: false);
        var end = lifted.Final;

        Assert.Equal(PillMotion.LiftScale, end.Scale, 9);
        var drawn = end.Drawn;
        Assert.Equal(960, drawn.X + (drawn.Width / 2), 9);
        Assert.Equal(32, drawn.Y + (drawn.Height / 2), 9);
        Assert.Equal(440 * PillMotion.LiftScale, drawn.Width, 9);

        var reduced = Centred(440).Lifted(10, true, reduceMotion: true);
        Assert.True(reduced.AtRest);
        Assert.Equal(1, reduced.At(10.5).Scale, 9);
    }

    [Fact]
    public void Arrive_grows_out_of_the_anchor_at_full_opacity_and_leave_shrinks_into_it_accelerating()
    {
        var hidden = PillMotion.Hidden(new PillVector(960, 8), 0.5, 440, 48, 0);
        Assert.Equal(PillPresence.Gone, hidden.Presence);

        var arriving = hidden.Arrive(0, reduceMotion: false);
        Assert.Equal(PillPresence.Arriving, arriving.Presence);
        var early = arriving.At(0.05);
        Assert.InRange(early.Presence, 0.01, 0.6);
        Assert.Equal(1, early.Opacity, 9);
        var drawn = early.Drawn;
        Assert.Equal(960, drawn.X + (drawn.Width / 2), 9);
        Assert.Equal(8, drawn.Y, 9);
        Assert.Equal(1, arriving.Final.Presence, 9);

        var shown = arriving.Frozen(2);
        var leaving = shown.Leave(2, reduceMotion: false);
        Assert.Equal(PillPresence.Leaving, leaving.Presence);
        var quarter = 1 - leaving.At(2 + (PillMotion.LeaveSeconds / 4)).Presence;
        var half = 1 - leaving.At(2 + (PillMotion.LeaveSeconds / 2)).Presence;
        Assert.True(half > 2 * quarter, "the leave accelerates");
        Assert.Equal(0, leaving.At(2 + PillMotion.LeaveSeconds).Presence, 9);
        Assert.Equal(1, leaving.At(2.1).Opacity, 9);
    }

    [Fact]
    public void Leaving_then_arriving_again_grows_back_from_the_current_size_without_a_jump()
    {
        var leaving = Centred(440).Leave(10, false);
        var mid = leaving.At(10.09).Presence;
        var back = leaving.Arrive(10.09, false);

        Assert.Equal(mid, back.At(10.09).Presence, 9);
        Assert.True(back.At(10.2).Presence > mid);
    }

    [Fact]
    public void With_reduce_motion_arrive_and_leave_are_150_ms_opacity_eases_at_full_size()
    {
        var arriving = PillMotion.Hidden(new PillVector(960, 8), 0.5, 440, 48, 0).Arrive(0, reduceMotion: true);

        Assert.Equal(1, arriving.At(0.01).Scale, 9);
        Assert.InRange(arriving.At(0.05).Opacity, 0.01, 0.99);
        Assert.Equal(1, arriving.At(PillMotion.ReducedSeconds).Opacity, 9);

        var leaving = arriving.Frozen(1).Leave(1, reduceMotion: true);
        Assert.Equal(1, leaving.At(1.05).Scale, 9);
        Assert.Equal(0, leaving.At(1 + PillMotion.ReducedSeconds).Opacity, 9);
    }

    [Fact]
    public void A_dpi_change_is_a_scale_spring_in_place_from_the_same_physical_size()
    {
        // 96 to 144 DPI: the pill keeps its DIP size, so it starts at 96/144 of it (the old physical size) and springs to 1.
        var motion = Centred(440).Rescaled(10, 96.0 / 144, reduceMotion: false);

        Assert.Equal(96.0 / 144, motion.At(10).Scale, 9);
        Assert.Equal(1, motion.Final.Scale, 9);
        Assert.Equal(960, motion.At(10.05).Drawn.X + (motion.At(10.05).Drawn.Width / 2), 9);
        Assert.True(Centred(440).Rescaled(10, 96.0 / 144, reduceMotion: true).AtRest);
    }

    [Fact]
    public void The_input_shape_is_the_drawn_body_and_done_shows_the_final_pose_exactly()
    {
        var motion = Centred().Widen(10, 440, false).TravelTo(10, new PillVector(400, 8), false).Fold(10, true, false);

        foreach (var t in new[] { 10.0, 10.1, 10.3 })
            Assert.Equal(motion.At(t).Drawn, motion.Shape.At(t));
        var done = motion.DoneAt();
        Assert.InRange(done, 10.2, 11.5);
        Assert.True(motion.Done(done));
        Assert.False(motion.Done(done - 0.001));
        Assert.Equal(motion.Final, motion.At(done));
        Assert.True(motion.Settled(done));
        Assert.Equal(motion.Final.Drawn, motion.Shape.Final);
    }

    [Fact]
    public void The_fold_s_settle_and_end_are_measured_in_the_cap_s_DIP_not_in_its_fraction()
    {
        var motion = Centred().Fold(10, true, false);

        // 0.1 of the fraction is 8.8 DIP of cap: a spring there is far from done
        var t = 10.0;
        while (motion.At(t).Cap < 0.9) t += 0.001;
        Assert.False(motion.Done(t));
        Assert.True(motion.At(t).Cap < 1);
    }

    [Fact]
    public void A_held_frame_drops_the_travel_and_keeps_the_other_curves()
    {
        var motion = Centred().Widen(10, 440, false).TravelTo(10, new PillVector(400, 8), false);
        var held = motion.Held(10.05, new PillVector(500, 20));

        Assert.False(held.Travels);
        Assert.Equal(new PillVector(500, 20), held.At(10.2).Anchor);
        Assert.Equal(motion.At(10.2).Width, held.At(10.2).Width, 9);
    }

    [Fact]
    public void A_new_anchor_side_keeps_the_body_where_it_is()
    {
        var motion = Centred(440);
        var right = motion.Reanchored(10, 1);

        Assert.Equal(motion.At(10).Body, right.At(10).Body);
        Assert.Equal(1180, right.At(10).Anchor.X, 9);
    }

    [Fact]
    public void The_places_follow_the_canvas_frame_stop_and_group_ride_the_right_edge_and_the_cap_folds_under_a_clip()
    {
        var parts = new PillParts(GroupWidth: 28, StopWidth: 170, CapWidth: 91, ShowsStop: true);
        var open = PillPlaces.Of(new PillPose(new PillVector(960, 8), 0.5, 440, 48, 0, 1, 1, 1, 1), parts);

        Assert.Equal(-220, open.BodyLeft, 9);
        Assert.Equal(220, open.BodyRight, 9);
        Assert.Equal(-212, open.RowLeft, 9);
        Assert.Equal(170, open.StopVisible, 9);
        Assert.Equal(220 - 6 - 170, open.StopLeft, 9);
        Assert.Equal(open.StopLeft - 6 - 28, open.GroupLeft, 9);
        Assert.Equal(440 - PillLayout.Chrome - 34 - 176, open.TextClip, 9);

        var folded = PillPlaces.Of(new PillPose(new PillVector(960, 8), 0.5, 440, 48, 1, 1, 1, 1, 1), parts);
        Assert.Equal(79, folded.StopVisible, 9);
        Assert.Equal(220 - 6 - 79, folded.StopLeft, 9);
        Assert.Equal(open.TextClip + 91, folded.TextClip, 9);

        // a Stop that does not show keeps its capsule at the right edge (it scales away there) but takes no room
        var noStop = PillPlaces.Of(new PillPose(new PillVector(960, 8), 0.5, 300, 48, 0, 1, 1, 1, 1), parts with { ShowsStop = false, GroupWidth = 0 });
        Assert.Equal(170, noStop.StopVisible, 9);
        Assert.Equal(150 - 6 - 170, noStop.StopLeft, 9);
        Assert.Equal(300 - PillLayout.Chrome, noStop.TextClip, 9);
        var groupOnly = PillPlaces.Of(new PillPose(new PillVector(960, 8), 0.5, 300, 48, 0, 1, 1, 1, 1), parts with { ShowsStop = false });
        Assert.Equal(150 - 6 - 28, groupOnly.GroupLeft, 9);
    }
}
