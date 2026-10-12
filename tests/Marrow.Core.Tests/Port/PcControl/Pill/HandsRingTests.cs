// PORT-ONLY: the hands-off ring's fill (ED-NEW-pcctl-38; DESIGN.md 4.5 "Hands-off behaviour"; canvas pc-control-ui
// `ringTick`): it follows the gate's stillness up at once, drains toward a lower one at one full ring per 260 ms and never
// jumps down, fills the rest in 90 ms once the borrow runs, counts a stalled frame as 64 ms, and starts empty again.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class HandsRingTests
{
    [Fact]
    public void It_follows_the_stillness_up_at_once_and_drains_smoothly_when_the_user_moves()
    {
        var ring = new HandsRing();
        Assert.Equal(0.4, ring.Next(0.4, 0.016, running: false), 9);
        Assert.Equal(0.9, ring.Next(0.9, 0.016, running: false), 9);

        // the user moves: the stillness is 0, and the fill goes down 0.016 / 0.26 a frame, never at once
        var before = ring.Fill;
        var frames = 0;
        while (ring.Fill > 0)
        {
            var next = ring.Next(0, 0.016, running: false);
            Assert.Equal(Math.Max(0, before - (0.016 / HandsRing.DrainSeconds)), next, 9);
            before = next;
            frames++;
        }
        Assert.Equal((int)Math.Ceiling(0.9 / (0.016 / HandsRing.DrainSeconds)), frames);

        // a drain stops where the stillness is
        ring.Next(1, 0.016, running: false);
        Assert.Equal(0.95, ring.Next(0.95, 0.016, running: false), 9);
    }

    [Fact]
    public void Once_the_borrow_runs_it_fills_the_rest_in_90_ms_and_a_stalled_frame_counts_as_64_ms()
    {
        var ring = new HandsRing();
        ring.Next(0.5, 0.016, running: false);
        Assert.Equal(0.5 + (0.045 / HandsRing.RunSeconds), ring.Next(0, 0.045, running: true), 9);
        Assert.Equal(1, ring.Next(0, 0.045, running: true), 9);

        var stalled = new HandsRing();
        stalled.Next(1, 0.016, running: false);
        Assert.Equal(1 - (HandsRing.MaxStepSeconds / HandsRing.DrainSeconds), stalled.Next(0, 2, running: false), 9);
        Assert.Equal(1 - (HandsRing.MaxStepSeconds / HandsRing.DrainSeconds), stalled.Next(double.NaN, double.NaN, running: false), 9);

        stalled.Reset();
        Assert.Equal(0, stalled.Fill);
        Assert.Equal(1, stalled.Next(7, 0.016, running: false));
    }
}
