using Marrow.UI.Port.Nth;
using SkiaSharp;

namespace Marrow.UI.Tests.Nth;

// OD-0136 amendment 2 (ED-NEW-nth-motion): the pure timing and geometry of the nth motion pack, read at fixed clock times
// against the numbers in the spec page's build notes (E:/Droppy/work/design/nth-brand/nth-motion-review.html, STEPS[].build).
public sealed class NthMotionTimingTests
{
    private const double Tolerance = 1e-6;

    [Theory]
    [InlineData(.4, 0, .2, 1)]
    [InlineData(.65, 0, .35, 1)]
    [InlineData(.2, .8, .2, 1)]
    [InlineData(.3, 1.3, .5, 1)]
    public void Css_curves_start_at_zero_end_at_one_and_match_their_bezier(double x1, double y1, double x2, double y2)
    {
        var curve = new NthCurve(x1, y1, x2, y2);
        Assert.Equal(0, curve.Ease(0), 9);
        Assert.Equal(1, curve.Ease(1), 9);
        Assert.Equal(0, curve.Ease(-3), 9);
        Assert.Equal(1, curve.Ease(7), 9);
        // a point of the bezier itself: (x(t), y(t)) at t = 0.3 must ease x(t) to y(t)
        static double B(double a, double b, double t) => 3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        foreach (var t in new[] { .1, .3, .5, .8 })
            Assert.Equal(B(y1, y2, t), curve.Ease(B(x1, x2, t)), 6);
    }

    [Fact]
    public void Symmetric_curves_pass_through_the_middle() =>
        Assert.Equal(.5, NthEase.InOut.Ease(.5), 6);

    [Fact]
    public void The_spring_is_critically_damped_with_stiffness_110_and_never_overshoots()
    {
        Assert.Equal(Math.Sqrt(110), NthSpring.Omega, 12);
        var spring = new NthSpring(0, 0, 1, 10);
        Assert.Equal(0, spring.Value(10), 12);
        Assert.Equal(0, spring.Value(9), 12);
        var last = 0.0;
        for (var t = 10.0; t < 12; t += 1 / 60.0)
        {
            var v = spring.Value(t);
            Assert.True(v >= last - 1e-12 && v <= 1 + 1e-12, $"{t}: {v}");
            last = v;
        }
        // x(t) = 1 - (1 + w t) e^(-w t)
        var w = NthSpring.Omega;
        Assert.Equal(1 - (1 + w * .25) * Math.Exp(-w * .25), spring.Value(10.25), 9);
        Assert.True(spring.Settled(11.5));
        Assert.False(spring.Settled(10.2));
    }

    [Fact]
    public void A_retargeted_spring_keeps_its_value_and_speed()
    {
        var spring = new NthSpring(0, 0, 1, 0);
        var moved = spring.Retarget(.5, .1);
        Assert.Equal(spring.Value(.1), moved.Value(.1), 12);
        Assert.Equal(spring.VelocityAt(.1), moved.VelocityAt(.1), 9);
        Assert.Equal(.5, moved.Value(5), 6);
    }

    [Fact]
    public void Calm_values_reach_their_target_in_a_tenth_of_a_second()
    {
        var spring = new NthSpring(.2, 0, .8, 3, Calm: true);
        Assert.Equal(.2, spring.Value(3), 12);
        Assert.Equal(.8, spring.Value(3.1), 12);
        Assert.True(spring.Value(3.05) is > .2 and < .8);
    }

    [Fact]
    public void The_tile_trace_runs_from_twelve_o_clock_on_a_squircle_of_n_4()
    {
        const double half = 6;
        var top = NthMotion.TilePoint(half, 0);
        Assert.Equal(0, top.X, 5); Assert.Equal(-half, top.Y, 5);
        var right = NthMotion.TilePoint(half, NthMotion.TilePoints / 4);
        Assert.Equal(half, right.X, 4); Assert.Equal(0, right.Y, 4);
        // |x|^4 + |y|^4 = half^4 at every point
        for (var i = 0; i < NthMotion.TilePoints; i++)
        {
            var p = NthMotion.TilePoint(half, i);
            Assert.Equal(Math.Pow(half, 4), Math.Pow(Math.Abs(p.X), 4) + Math.Pow(Math.Abs(p.Y), 4), 2);
        }
        Assert.Equal(1.6, NthMotion.SpinnerStroke(12)); Assert.Equal(2, NthMotion.SpinnerStroke(16)); Assert.Equal(2.4, NthMotion.SpinnerStroke(20));
        Assert.Equal((16 - 2 - 1) / 2.0, NthMotion.TileHalf(16));
    }

    [Fact]
    public void The_spinner_head_and_tail_follow_the_one_point_six_second_cycle()
    {
        const double L = 100;
        // u = 0: head and tail at 0, the line at its 4 % minimum; it fades in over 0.3 s (.2,.8,.2,1)
        var start = NthMotion.Spinner(0, L, false);
        Assert.Equal(.04 * L, start.Length, 9);
        Assert.Equal(0, start.Alpha, 9);
        Assert.Equal(1, NthMotion.Spinner(.3, L, false).Alpha, 9);
        // u = 0.38 (0.608 s): the tail has not left, the head is at L * io(0.38 / 0.62)
        var t = .38 * 1.6;
        var mid = NthMotion.Spinner(t, L, false);
        Assert.Equal(L * NthEase.InOut.Ease(.38 / .62), mid.Length, 6);
        Assert.Equal((L * t / 4.5) % L, mid.Start, 6);
        // u = 0.62: the head has arrived; the tail is at io(0.24/0.62)
        var full = NthMotion.Spinner(.62 * 1.6 + 3.2, L, false);
        Assert.Equal(L - L * NthEase.InOut.Ease(.24 / .62), full.Length, 6);
    }

    [Fact]
    public void The_calm_spinner_is_still_and_breathes_between_sixty_and_a_hundred_percent()
    {
        var a = NthMotion.Spinner(0, 80, true);
        var b = NthMotion.Spinner(1.2, 80, true);
        Assert.Equal(0, a.Start); Assert.Equal(.3 * 80, a.Length, 9); Assert.Equal(a.Start, b.Start); Assert.Equal(a.Length, b.Length);
        Assert.Equal(1, a.Alpha, 9);
        Assert.Equal(.6, b.Alpha, 9);
        Assert.Equal(1, NthMotion.Spinner(2.4, 80, true).Alpha, 9);
    }

    [Fact]
    public void The_known_bar_light_travels_72_percent_of_two_point_six_seconds_and_rests()
    {
        const double W = 200, fill = 100;
        var half = NthMotion.BarLightHalf(W);
        Assert.Equal(60, half);
        Assert.Equal(15, NthMotion.BarLightHalf(50));
        Assert.Equal(-half, NthMotion.BarLight(0, fill, W, .5, false)!.Value, 9);
        Assert.Equal(-half + (fill + 2 * half) * NthEase.Glide.Ease(.5), NthMotion.BarLight(.36 * 2.6, fill, W, .5, false)!.Value, 6);
        Assert.Null(NthMotion.BarLight(.8 * 2.6, fill, W, .5, false));
        Assert.Null(NthMotion.BarLight(.1, fill, W, .5, true));
        Assert.Null(NthMotion.BarLight(.1, W, W, 1, false));
    }

    [Fact]
    public void The_bar_fill_fades_in_over_its_first_one_and_a_half_heights() =>
        Assert.Equal((0.0, .5, 1.0), (NthMotion.BarFillAlpha(0, 4), NthMotion.BarFillAlpha(3, 4), NthMotion.BarFillAlpha(6, 4)));

    [Fact]
    public void The_unknown_bar_runs_its_head_and_tail_over_one_point_eight_seconds_and_stands_still_when_calm()
    {
        const double W = 200, h = 4;
        var u = .5; var t = u * 1.8;
        var segment = NthMotion.BarIndeterminate(t, W, h, false);
        Assert.Equal(W * NthEase.Standard.Ease(u / .66), segment.X1, 6);
        Assert.Equal(W * NthEase.Standard.Ease((u - .34) / .66), segment.X0, 6);
        Assert.Equal(1, segment.Alpha, 9);
        var calm0 = NthMotion.BarIndeterminate(0, W, h, true);
        var calm1 = NthMotion.BarIndeterminate(1.2, W, h, true);
        Assert.Equal((70.0, 130.0), (calm0.X0, calm0.X1));
        Assert.Equal((calm0.X0, calm0.X1), (calm1.X0, calm1.X1));
        Assert.Equal(1, calm0.Alpha, 9); Assert.Equal(.55, calm1.Alpha, 9);
    }

    [Fact]
    public void The_ring_done_pops_the_disc_fades_the_ring_and_draws_the_tick()
    {
        Assert.Equal((1.75, 2.0, 2.25), (NthMotion.RingStroke(14), NthMotion.RingStroke(18), NthMotion.RingStroke(20)));
        var start = NthMotion.RingDone(0, false);
        Assert.Equal(0, start.DiscScale, 9); Assert.Equal(1, start.RingAlpha, 9); Assert.Equal(0, start.Tick, 9);
        Assert.Equal(1, NthMotion.RingDone(.36, false).DiscScale, 9);
        Assert.Equal(0, NthMotion.RingDone(.33, false).RingAlpha, 9);
        Assert.Equal(1, NthMotion.RingDone(.46, false).Tick, 9);
        Assert.Equal(0, NthMotion.RingDone(.16, false).Tick, 9);
        // the pop overshoots (.3,1.3,.5,1)
        Assert.True(NthMotion.RingDone(.25, false).DiscScale > 1);
        var calm = NthMotion.RingDone(.1, true);
        Assert.Equal(1, calm.DiscScale); Assert.Equal(.5, calm.DiscAlpha, 9);
        Assert.Equal(new SKPoint(-.42f, .02f), NthMotion.DiscTick[0]);
        Assert.Equal(1.3, NthMotion.DiscTickStroke(4)); Assert.Equal(2.4, NthMotion.DiscTickStroke(10), 9);
    }

    [Fact]
    public void The_text_light_crosses_the_word_in_three_quarters_of_its_period_and_never_runs_calm()
    {
        const double w = 120;
        Assert.Equal(-w / 2, NthMotion.Shine(0, w, false)!.Value, 9);
        Assert.Equal(1.5 * w, NthMotion.Shine(.75 * 2.4 - 1e-9, w, false)!.Value, 4);
        Assert.Null(NthMotion.Shine(2, w, false));
        Assert.Null(NthMotion.Shine(.3, w, true));
        Assert.Equal(30, NthMotion.ShineHalf);
    }

    [Fact]
    public void The_row_tick_is_the_build_note_path_drawn_in_point_32_seconds()
    {
        Assert.Equal([new SKPoint(3, 7.2f), new SKPoint(5.6f, 9.8f), new SKPoint(11, 4.6f)], NthMotion.RowTick);
        Assert.Equal(1.8, NthMotion.RowTickStroke);
        Assert.Equal(0, NthMotion.RowTickIn(0, false).Drawn, 9);
        Assert.Equal(1, NthMotion.RowTickIn(.32, false).Drawn, 9);
    }

    [Fact]
    public void The_codex_link_flows_at_fourteen_px_a_second_closes_its_gaps_and_stands_still_when_calm()
    {
        var (dash, gap, phase) = NthMotion.Link(.25, null, false);
        Assert.Equal((2.5, 4.5), (dash, gap));
        Assert.Equal(3.5, phase, 9);
        Assert.Equal(NthMotion.Link(.25, null, false).Phase, NthMotion.Link(.75, null, false).Phase, 9); // one 7 px period per 0.5 s
        Assert.Equal(0, NthMotion.Link(1, .35, false).Gap, 9);
        Assert.Equal(NthMotion.Link(0, null, true).Phase, NthMotion.Link(3, null, true).Phase);
    }

    [Fact]
    public void The_send_glyphs_launch_pop_and_drop_on_the_spec_timings()
    {
        Assert.Equal(-20, NthMotion.ArrowLaunch(.3, false).Y, 9);
        Assert.Equal(0, NthMotion.ArrowLaunch(0, false).Y, 9);
        Assert.Equal(1, NthMotion.StopIn(.52, false).Scale, 2);
        Assert.Equal(0, NthMotion.ArrowDrop(.5, false).Y, 9);
        Assert.Equal(.9, NthMotion.Press(.16, true), 9);
        Assert.Equal(1, NthMotion.Press(.16, false), 9);
    }
}
