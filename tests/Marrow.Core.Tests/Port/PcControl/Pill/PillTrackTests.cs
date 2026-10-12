// PORT-ONLY: the controls' 1 ms key frames (OD-0145, ED-NEW-pcctl-24; DESIGN.md 10.6 "Fallback if the driver cannot set the
// content offset", 10.11 "MotionDriver"): sampled from the closed form every 1 ms, linear interpolation stays within
// DESIGN's bounds (0.15 DIP at 2,000 DIP), the last key is the final pose exactly, and a part that does not move needs no
// animation.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class PillTrackTests
{
    private static PillMotion Travel(double distance) =>
        PillMotion.Still(new PillPose(new PillVector(100, 8), 0.5, 440, 48, 0, 1, 1, 1, 1), 0).TravelTo(0, new PillVector(100 + distance, 8), false);

    [Theory]
    [InlineData(100, 0.003)]
    [InlineData(300, 0.014)]
    [InlineData(728, 0.044)]
    [InlineData(1200, 0.08)]
    [InlineData(2000, 0.15)]
    public void One_ms_key_frames_stay_within_design_s_bounds(double distance, double bound)
    {
        var motion = Travel(distance);
        var track = PillTrack.Sample(motion, pose => pose.Anchor.X);

        // DESIGN's bound is the interpolation against the closed form while the curve runs; the last millisecond carries
        // the end's snap to the target (within 0.1 DIP, the same snap the render-thread driver shows), checked on its own.
        var spring = PillSpring.Travel(0, new PillVector(100, 8), new PillVector(100 + distance, 8), PillVector.Zero);
        var worst = 0.0;
        for (var t = 0.0; t <= track.Duration - PillTrack.Step; t += 0.0001)
            worst = Math.Max(worst, Math.Abs(track.Interpolate(t) - spring.Value(t).X));
        // DESIGN's figures are rounded to the shown digits; allow the last digit
        Assert.True(worst <= bound * 1.1 + 0.001, $"{distance} DIP: worst {worst:F4} DIP against {bound}");
        var last = track.Duration - PillTrack.Step;
        Assert.True(Math.Abs(track.Interpolate(last) - spring.Value(last).X) <= PillSpring.EndPixels + 0.001);
        Assert.True(Math.Abs(track.Last - spring.Value(track.Duration).X) <= PillSpring.EndPixels);
    }

    [Fact]
    public void The_keys_run_from_the_start_to_the_end_and_the_last_is_the_final_value_exactly()
    {
        var motion = Travel(728);
        var track = PillTrack.Sample(motion, pose => pose.Anchor.X);

        Assert.Equal(0, track.Start, 9);
        Assert.Equal(motion.DoneAt() - motion.Start, track.Duration, 9);
        Assert.Equal(0f, track.Keys[0]);
        Assert.Equal(1f, track.Keys[^1]);
        Assert.Equal(828, track.Last, 9);
        Assert.Equal(828, track.Interpolate(99), 9);
        Assert.Equal(100, track.Interpolate(-1), 9);
        for (var i = 1; i < track.Keys.Count; i++) Assert.True(track.Keys[i] >= track.Keys[i - 1]);
        Assert.Equal((int)Math.Ceiling(track.Duration / PillTrack.Step - 1e-9) + 1, track.Keys.Count);
    }

    [Fact]
    public void A_part_that_does_not_move_is_constant()
    {
        var motion = Travel(728);

        Assert.True(PillTrack.Sample(motion, pose => pose.Width).Constant);
        Assert.False(PillTrack.Sample(motion, pose => pose.Anchor.X).Constant);
    }

    [Fact]
    public void A_motion_at_rest_samples_two_equal_keys()
    {
        var still = PillMotion.Still(new PillPose(new PillVector(100, 8), 0.5, 440, 48, 0, 1, 1, 1, 1), 5);
        var track = PillTrack.Sample(still, pose => pose.Width);

        Assert.Equal(2, track.Keys.Count);
        Assert.Equal(0, track.Duration, 9);
        Assert.True(track.Constant);
    }
}
