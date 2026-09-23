using Avalonia;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// F.LoadingMark: the loading mark of OD-0016 and OD-0018 and the boot sequence, as pure functions of time.
// Expected values come from the decisions' numbers (ED-097, ED-102, ED-103, each cited per test), the icon geometry of
// src/Marrow.App/Assets/icon.svg and, where the decisions name no number, the reference
// porting/design/loading_mark/boot_sequence.html (cited as boot_sequence.html:<line>) and loading_premium.html card B.
// Times are seconds, lengths are the icon's 1024 box unless a test says DIP.
// PORT-ONLY surface these tests fix, in src/Marrow.UI/Port/LoadingMark.cs, namespace Marrow.UI.Port:
//   static CometEasing: Clamp, Smooth, OutCubic, OutQuart, InOutCubic, OutBack
//   static CometMotion: TurnSeconds, WindSeconds, RingSeconds, RingStartRadius, RingEndRadius, RingStartOpacity,
//       RingWidth (DIP), RingColor, Trails [(OffsetDegrees, Blur, Opacity)], TrailFactor(speed), RingRadius(age),
//       RingOpacity(age), Running(t) (in-app: a pulse at 0 and every 2.4 s), Still (reduce motion)
//   CometState(Angle, Speed, TrailFactor, CoreScale, GlowOpacity, GlowScale, LastPulse, RingAge)
//   LoadingMark: Opacity(sinceWaitStart), Motion(sinceWaitStart, reduceMotion)
//   BootWindowLayout(Rect Frame, double CornerRadius, IReadOnlyList<Rect> ContentAreas)
//   BootSequence(BootWindowLayout window, bool reduceMotion): MarkSize, Centre, IconToStage, GatherParticles,
//       WindowParticles, FinalRest(readyAt), MainWindowShownAt(readyAt), StageClosesAt(readyAt), At(t, readyAt),
//       OutlineWidth, OutlineColor, OutlineOpacity, OutlineGlow
//   BootFrame(Phase, MarkOpacity, TileOpacity, TileScale, ArmReveal, CoreReveal, Comet, EdgeShare, AllShare,
//       OutlineOpacity, ContentOpacity, ContentScale, MainWindowShown, StageClosed)
public sealed class LoadingMarkTests
{
    private const double Tau = 2 * Math.PI;

    // One coordinate of a CSS cubic-bezier at curve parameter s (the curve's own definition, not a solver).
    private static double Bez(double s, double p1, double p2) =>
        3 * (1 - s) * (1 - s) * s * p1 + 3 * (1 - s) * s * s * p2 + s * s * s;

    private static double BezSlope(double s, double p1, double p2) =>
        3 * (1 - s) * (1 - s) * p1 + 6 * (1 - s) * s * (p2 - p1) + 3 * s * s * (1 - p2);

    private static double Smooth(double e0, double e1, double x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // The reference preview's stage: a 1000 by 625 DIP desk with the 780 by 480 main window centred, corner 14, and its
    // content areas (boot_sequence.html:93, :166-175, in CSS px).
    private static BootWindowLayout ReferenceWindow()
    {
        double x = 110, y = 72.5, w = 780, h = 480, side = 232;
        return new BootWindowLayout(new Rect(x, y, w, h), 14,
        [
            new Rect(x + 14, y + 52, side - 28, 150),
            new Rect(x + 14, y + 230, side - 28, 110),
            new Rect(x + side + 40, y + 70, w - side - 80, 220),
            new Rect(x + side + 48, y + h - 84, w - side - 96, 52),
            new Rect(x + 20, y + 8, w - 40, 28),
        ]);
    }

    // icon.svg: the three arms, "M p1 A 236.58 236.58 0 0 1 p2", gradient p1 -> p2 from c0 to c1.
    private static readonly (Point P1, Point P2, Color C0, Color C1)[] Arms =
    [
        (new Point(392.88, 217.16), new Point(654.59, 415.82), Color.Parse("#9CBBFF"), Color.Parse("#4F7DFF")),
        (new Point(826.91, 556.26), new Point(524, 683.58), Color.Parse("#CBA6FF"), Color.Parse("#8A5CF6")),
        (new Point(316.22, 762.59), new Point(357.41, 436.6), Color.Parse("#74F2D0"), Color.Parse("#2FC6A0")),
    ];

    // SVG arc semantics: large-arc 0, sweep 1, so the centre is the one from which p1 -> p2 turns by less than pi in
    // the positive angle direction.
    private static (Point Centre, double A0, double Sweep) ArcOf(Point p1, Point p2)
    {
        const double r = 236.58;
        double mx = (p1.X + p2.X) / 2, my = (p1.Y + p2.Y) / 2, dx = p2.X - p1.X, dy = p2.Y - p1.Y, half = Math.Sqrt(dx * dx + dy * dy) / 2;
        double hh = Math.Sqrt(r * r - half * half), nx = -dy / (2 * half), ny = dx / (2 * half);
        foreach (var sgn in new[] { 1.0, -1.0 })
        {
            double cx = mx + sgn * hh * nx, cy = my + sgn * hh * ny, a0 = Math.Atan2(p1.Y - cy, p1.X - cx);
            var d = Math.Atan2(p2.Y - cy, p2.X - cx) - a0;
            while (d < 0) d += Tau;
            if (d < Math.PI) return (new Point(cx, cy), a0, d);
        }
        throw new InvalidOperationException("no arc");
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static void Near(Point expected, Point actual, double tolerance)
    {
        Assert.True(Dist(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    private static double Wrap(double a)
    {
        while (a <= -Math.PI) a += Tau;
        while (a > Math.PI) a -= Tau;
        return a;
    }

    // ---- ED-102 (1) to (3): the Comet motion ----

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_arms_turn_one_full_turn_every_2_4_seconds_on_the_cubic_bezier_from_rest_to_rest()
    {
        Assert.Equal(2.4, CometMotion.TurnSeconds);
        for (var n = 0; n < 3; n++) Assert.Equal(Tau * n, CometMotion.Running(2.4 * n).Angle, 9);
        Assert.Equal(Math.PI, CometMotion.Running(1.2).Angle, 7); // the curve is symmetric about its middle
        foreach (var s in new[] { 0.1, 0.25, 0.4, 0.6, 0.75, 0.9 })
        {
            double x = Bez(s, .45, .55), y = Bez(s, .05, .95); // cubic-bezier(.45, .05, .55, .95)
            Assert.Equal(Tau * y, CometMotion.Running(x * 2.4).Angle, 7);
            Assert.Equal(Tau * (1 + y), CometMotion.Running(2.4 + x * 2.4).Angle, 7);
        }
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_turn_speed_is_the_angular_speed_relative_to_its_mean_and_the_trails_follow_it()
    {
        // speed = d(eased)/d(linear) = y'(s) / x'(s); about 0 at rest, about 2 mid-turn (ED-102 (2)).
        Assert.Equal(1.425 / .825, CometMotion.Running(1.2).Speed, 6);
        Assert.Equal(.05 / .45, CometMotion.Running(0).Speed, 6);
        foreach (var s in new[] { 0.2, 0.35, 0.65, 0.8 })
        {
            double x = Bez(s, .45, .55), speed = BezSlope(s, .05, .95) / BezSlope(s, .45, .55);
            var state = CometMotion.Running(x * 2.4);
            Assert.Equal(speed, state.Speed, 6);
            Assert.Equal(Math.Clamp((speed - .35) / 1.4, 0, 1), state.TrailFactor, 6);
        }
        // trail factor = clamp((speed - .35) / 1.4)
        Assert.Equal(0, CometMotion.TrailFactor(.35));
        Assert.Equal(0, CometMotion.TrailFactor(0));
        Assert.Equal(.5, CometMotion.TrailFactor(1.05), 12);
        Assert.Equal(1, CometMotion.TrailFactor(2));
        Assert.Equal(0, CometMotion.Running(0).TrailFactor);
        Assert.Equal((1.425 / .825 - .35) / 1.4, CometMotion.Running(1.2).TrailFactor, 6);
        // two copies rotated back 14 and 30 degrees, blurred 10 and 20 units, at .42 and .2
        Assert.Equal(new[] { (14.0, 10.0, .42), (30.0, 20.0, .2) }, CometMotion.Trails);
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_core_pulses_exactly_once_per_turn_at_the_instant_the_turn_comes_to_rest()
    {
        var pulses = new SortedSet<double>();
        for (var i = 0; i < 7 * 120; i++) // 7 s at 120 frames a second
        {
            var t = i / 120.0;
            var state = CometMotion.Running(t);
            Assert.NotNull(state.LastPulse);
            pulses.Add(Math.Round(state.LastPulse!.Value, 9));
        }
        Assert.Equal([0, 2.4, 4.8], pulses);
        foreach (var p in pulses) Assert.Equal(0, Wrap(CometMotion.Running(p).Angle), 9); // at rest
        // one ring leaves at each pulse and lives 1.9 s
        Assert.Equal(1.9, CometMotion.RingSeconds);
        Assert.Equal(0, CometMotion.Running(2.4).RingAge!.Value, 9);
        Assert.Equal(.5, CometMotion.Running(2.9).RingAge!.Value, 9);
        Assert.Equal(1.89, CometMotion.Running(1.89).RingAge!.Value, 9);
        Assert.Null(CometMotion.Running(1.9).RingAge);
        Assert.Null(CometMotion.Running(2.39).RingAge);
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_core_draws_in_before_rest_then_rises_to_1_14_and_relaxes_as_exp_minus_7s_while_the_glow_flashes()
    {
        Assert.Equal(.28, CometMotion.WindSeconds);
        // the pulse instant: the core is drawn in to .9, the glow dimmed, nothing risen yet
        var at = CometMotion.Running(2.4);
        Assert.Equal(.9, at.CoreScale, 9);
        Assert.Equal(.55, at.GlowOpacity, 9);
        Assert.Equal(1, at.GlowScale, 9);
        // .06 s later the rise is complete: 1 + .14 exp(-7 s); glow opacity + 1.1, scale + .5, same decay
        var risen = CometMotion.Running(2.46);
        var flash = Math.Exp(-7 * .06);
        Assert.Equal(1 + .14 * flash, risen.CoreScale, 6);
        Assert.Equal(.55 + 1.1 * flash, risen.GlowOpacity, 6);
        Assert.Equal(1 + .5 * flash, risen.GlowScale, 6);
        // mid-turn: relaxed to 1
        Assert.Equal(1 + .14 * Math.Exp(-7 * 1.5), CometMotion.Running(3.9).CoreScale, 9);
        // over the last .28 s: scale to .9 and glow dimmed by 45 %, half way at .14 s before rest
        var wind = CometMotion.Running(4.8 - .14);
        var f2 = Math.Exp(-7 * (2.4 - .14));
        Assert.Equal((1 + .14 * f2) * (1 - .1 * .5), wind.CoreScale, 6);
        Assert.Equal(.55 * (1 - .45 * .5) + 1.1 * f2, wind.GlowOpacity, 6);
        Assert.Equal(1, CometMotion.Running(4.8 - .28).CoreScale, 6);
        Assert.Equal(.9, CometMotion.Running(4.8 - 1e-9).CoreScale, 6);
        Assert.Equal(.55 * .55, CometMotion.Running(4.8 - 1e-9).GlowOpacity, 6);
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_ring_grows_from_96_to_346_over_1_9_seconds_on_its_bezier_as_its_opacity_falls_from_0_6_to_0()
    {
        Assert.Equal(96, CometMotion.RingStartRadius);
        Assert.Equal(346, CometMotion.RingEndRadius);
        Assert.Equal(.6, CometMotion.RingStartOpacity);
        Assert.Equal(3, CometMotion.RingWidth); // DIP
        Assert.Equal(Color.Parse("#DCE6FF"), CometMotion.RingColor);
        Assert.Equal(96, CometMotion.RingRadius(0), 9);
        Assert.Equal(346, CometMotion.RingRadius(1.9), 9);
        Assert.Equal(.6, CometMotion.RingOpacity(0), 9);
        Assert.Equal(0, CometMotion.RingOpacity(1.9), 9);
        foreach (var s in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
        {
            double x = Bez(s, .2, .3), y = Bez(s, .6, 1); // cubic-bezier(.2, .6, .3, 1)
            Assert.Equal(96 + 250 * y, CometMotion.RingRadius(x * 1.9), 6);
            Assert.Equal(.6 * (1 - y), CometMotion.RingOpacity(x * 1.9), 6);
        }
    }

    // ---- ED-097 (3), (5), ED-102 (6): in-app sites ----

    [Fact]
    [Trait("Decision", "ED-097")]
    [Trait("Decision", "ED-102")]
    public void In_app_the_mark_shows_nothing_for_400_ms_then_fades_in_over_0_2_s_and_starts_with_a_pulse()
    {
        foreach (var t in new[] { 0, .1, .399, .4 }) Assert.Equal(0, LoadingMark.Opacity(t));
        Assert.Equal(.5, LoadingMark.Opacity(.5), 9);
        foreach (var t in new[] { .6, 1, 30 }) Assert.Equal(1, LoadingMark.Opacity(t), 9);
        // loading_premium.html card B: the animation starts when the mark appears, and its keyframe 0 % is the pulse
        // (bCore 0 %, bRing 0 %), so the first pulse is at the instant the mark appears.
        Assert.Equal(0, LoadingMark.Motion(.4, reduceMotion: false).LastPulse!.Value, 9);
        foreach (var t in new[] { .4, .5, 1.6, 2.8, 3.1, 7.3 })
            Assert.Equal(CometMotion.Running(t - .4), LoadingMark.Motion(t, reduceMotion: false));
    }

    [Fact]
    [Trait("Decision", "ED-097")]
    [Trait("Decision", "ED-102")]
    [Trait("Rule", "R-VIEW-6")]
    public void Under_reduce_motion_the_in_app_mark_is_drawn_still()
    {
        var still = CometMotion.Still;
        Assert.Equal(0, still.Angle);
        Assert.Equal(0, still.Speed);
        Assert.Equal(0, still.TrailFactor);
        Assert.Equal(1, still.CoreScale);
        Assert.Equal(1, still.GlowScale);
        Assert.Equal(.6, still.GlowOpacity); // boot_sequence.html:319, the still mark's glow
        Assert.Null(still.LastPulse);
        Assert.Null(still.RingAge);
        foreach (var t in new[] { 0, .4, .6, 1.6, 2.8, 9.1 }) Assert.Equal(still, LoadingMark.Motion(t, reduceMotion: true));
        // the no-flash rule is not motion and still applies
        Assert.Equal(0, LoadingMark.Opacity(.3));
        Assert.Equal(1, LoadingMark.Opacity(.6), 9);
    }

    // ---- ED-103 (3): turns until ready ----

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_final_rest_is_the_end_of_the_first_turn_that_ends_at_or_after_the_app_is_ready()
    {
        Assert.Equal(double.PositiveInfinity, BootSequence.FinalRest(null));
        Assert.Equal(3.85, BootSequence.FinalRest(0), 9); // the first turn always runs
        Assert.Equal(3.85, BootSequence.FinalRest(2.5), 9);
        Assert.Equal(3.85, BootSequence.FinalRest(3.85), 9); // at the end counts
        Assert.Equal(6.25, BootSequence.FinalRest(3.86), 9);
        Assert.Equal(6.25, BootSequence.FinalRest(6), 9); // turns end at 3.85, 6.25, 8.65: 6.25 is the first at or after 6
        Assert.Equal(8.65, BootSequence.FinalRest(7), 9);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Decision", "ED-102")]
    public void The_boot_mark_pulses_at_ignition_and_at_the_start_of_each_extra_turn_and_not_at_the_final_rest()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var pulses = new SortedSet<double>();
        for (var i = 0; i < 9 * 120; i++)
        {
            if (boot.At(i / 120.0, readyAt: 7).Comet.LastPulse is { } p) pulses.Add(Math.Round(p, 9));
        }
        Assert.Equal([1.45, 3.85, 6.25], pulses);
        Assert.Null(boot.At(1.44, 7).Comet.LastPulse);
        var rest = boot.At(8.7, 7).Comet;
        Assert.Equal(6.25, rest.LastPulse!.Value, 9);
        Assert.Null(rest.RingAge);
        Assert.Equal(0, Wrap(rest.Angle), 9);
        Assert.Equal(0, rest.Speed);
        // no draw-in before the final rest, where the mark breaks up instead
        Assert.Equal(1 + .14 * Math.Exp(-7 * 2.35), boot.At(8.6, 7).Comet.CoreScale, 9);
        // but a draw-in before an extra turn's pulse
        Assert.Equal((1 + .14 * Math.Exp(-7 * 2.35)) * (1 - .1 * Smooth(5.97, 6.25, 6.2)), boot.At(6.2, 7).Comet.CoreScale, 9);
        // while the app is not ready the turns go on
        var waiting = new SortedSet<double>();
        for (var i = 0; i < 12 * 30; i++)
        {
            if (boot.At(i / 30.0, readyAt: null).Comet.LastPulse is { } p) waiting.Add(Math.Round(p, 9));
        }
        Assert.Equal([1.45, 3.85, 6.25, 8.65, 11.05], waiting);
        Assert.Equal(BootPhase.Turning, boot.At(100, null).Phase);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Decision", "ED-102")]
    public void The_boot_phases_are_gather_turning_at_rest_disintegrating_and_closed_at_their_boundaries()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var rest = BootSequence.FinalRest(2.5);
        Assert.Equal(BootPhase.Gather, boot.At(0, 2.5).Phase);
        Assert.Equal(BootPhase.Gather, boot.At(1.449, 2.5).Phase);
        Assert.Equal(BootPhase.Turning, boot.At(1.45, 2.5).Phase);
        Assert.Equal(BootPhase.Turning, boot.At(rest - 1e-6, 2.5).Phase);
        Assert.Equal(BootPhase.AtRest, boot.At(rest, 2.5).Phase);
        Assert.Equal(BootPhase.AtRest, boot.At(rest + .1 - 1e-6, 2.5).Phase);
        Assert.Equal(BootPhase.Disintegrating, boot.At(rest + .1, 2.5).Phase);
        var close = boot.StageClosesAt(2.5);
        Assert.Equal(BootPhase.Disintegrating, boot.At(close - 1e-6, 2.5).Phase);
        Assert.Equal(BootPhase.Closed, boot.At(close, 2.5).Phase);
        Assert.True(boot.At(close, 2.5).StageClosed);
        Assert.False(boot.At(close - 1e-6, 2.5).StageClosed);
        // about 5.4 s in all when the app is ready by 3.85 s: the last landing is .14 + .72 to .58 + .98 s after the
        // disintegration starts, and the stage closes .35 s later
        Assert.InRange(close, 3.95 + .14 + .72 + .35, 3.95 + .58 + .98 + .35);
    }

    // ---- ED-102 (4): gather ----

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_gather_has_60_particles_per_arm_along_its_arc_and_across_80_percent_of_the_stroke_and_36_in_the_core()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var particles = boot.GatherParticles;
        Assert.Equal(216, particles.Count);
        for (var k = 0; k < 3; k++)
        {
            var arm = particles.Where(p => p.Group == k).ToList();
            Assert.Equal(60, arm.Count);
            var (p1, p2, c0, c1) = Arms[k];
            var (centre, a0, sweep) = ArcOf(p1, p2);
            foreach (var p in arm)
            {
                Assert.InRange(Dist(p.Target, centre), 236.58 - 36.8, 236.58 + 36.8);
                var along = Math.Atan2(p.Target.Y - centre.Y, p.Target.X - centre.X) - a0;
                while (along < 0) along += Tau;
                Assert.InRange(along, 0, sweep);
                // the colour of the arm's gradient (p1 -> p2) at that point
                double gx = p2.X - p1.X, gy = p2.Y - p1.Y;
                var f = Math.Clamp(((p.Target.X - p1.X) * gx + (p.Target.Y - p1.Y) * gy) / (gx * gx + gy * gy), 0, 1);
                Assert.InRange((double)p.Color.R, c0.R + (c1.R - c0.R) * f - 1, c0.R + (c1.R - c0.R) * f + 1);
                Assert.InRange((double)p.Color.G, c0.G + (c1.G - c0.G) * f - 1, c0.G + (c1.G - c0.G) * f + 1);
                Assert.InRange((double)p.Color.B, c0.B + (c1.B - c0.B) * f - 1, c0.B + (c1.B - c0.B) * f + 1);
            }
        }
        var core = particles.Where(p => p.Group == 3).ToList();
        Assert.Equal(36, core.Count);
        foreach (var p in core)
        {
            Assert.Equal(Color.Parse("#ECF2FF"), p.Color);
            Assert.InRange(Dist(p.Target, new Point(512, 512)), 0, 82);
        }
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void Gather_particles_start_on_a_ring_of_820_to_1080_behind_their_target_angle_with_the_decided_delays_and_flights()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        foreach (var p in boot.GatherParticles)
        {
            Assert.InRange(p.StartRadius, 820, 1080);
            Assert.InRange(p.TargetAngle - p.StartAngle, 1.3, 2.2);
            Assert.Equal(Dist(p.Target, new Point(512, 512)), p.TargetRadius, 9);
            Assert.Equal(Math.Atan2(p.Target.Y - 512, p.Target.X - 512), p.TargetAngle, 9);
            Near(new Point(512 + Math.Cos(p.StartAngle) * p.StartRadius, 512 + Math.Sin(p.StartAngle) * p.StartRadius), p.Position(0), 1e-9);
            Assert.InRange(p.Size, 9, 20);
            if (p.Group < 3)
            {
                Assert.InRange(p.Delay, .06 + .09 * p.Group, .06 + .09 * p.Group + .34);
                Assert.InRange(p.Duration, .7, .98);
            }
            else
            {
                Assert.InRange(p.Delay, .5, .72);
                Assert.InRange(p.Duration, .6, .75);
            }
        }
        // the same particles every launch
        var again = new BootSequence(ReferenceWindow(), reduceMotion: false);
        Assert.Equal(boot.GatherParticles.Select(p => (p.Target, p.Delay, p.StartRadius)), again.GatherParticles.Select(p => (p.Target, p.Delay, p.StartRadius)));
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void A_gather_particle_flies_in_on_ease_out_cubic_radius_and_ease_out_quart_angle_shrinks_to_55_percent_and_melts_out()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        foreach (var p in boot.GatherParticles)
        {
            var mid = p.Delay + p.Duration / 2;
            double r = p.StartRadius + (p.TargetRadius - p.StartRadius) * (1 - .125), a = p.StartAngle + (p.TargetAngle - p.StartAngle) * (1 - .0625);
            Near(new Point(512 + Math.Cos(a) * r, 512 + Math.Sin(a) * r), p.Position(mid), 1e-6);
            Assert.Equal(p.Size * (1 - .45 * .5), p.Radius(mid), 9);
            var landing = p.Delay + p.Duration;
            Near(p.Target, p.Position(landing), 1e-6);
            Near(p.Target, p.Position(landing + 5), 1e-6);
            Assert.Equal(p.Size * .55, p.Radius(landing), 9);
            Assert.Equal(1, p.Opacity(landing), 9);
            // melts out over .35 of its flight time
            Assert.Equal(.5, p.Opacity(p.Delay + 1.175 * p.Duration), 6);
            Assert.Equal(0, p.Opacity(p.Delay + 1.35 * p.Duration), 9);
            Assert.Equal(0, p.Landed(p.Delay));
            Assert.Equal(1, p.Landed(landing), 9);
        }
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void Each_arm_and_the_core_fade_in_with_the_share_of_their_particles_landed()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var start = boot.At(0, null);
        Assert.Equal([0.0, 0, 0], start.ArmReveal);
        Assert.Equal(0, start.CoreReveal);
        // every arm particle has landed by .06 + .18 + .34 + .98 = 1.56 s, every core particle by .72 + .75 = 1.47 s
        var landed = boot.At(1.6, null);
        Assert.Equal([1.0, 1, 1], landed.ArmReveal);
        Assert.Equal(1, landed.CoreReveal, 9);
        // in between: the share landed, a particle counting from .7 to all of its flight (boot_sequence.html:338)
        foreach (var t in new[] { .8, 1.0, 1.2 })
        {
            var frame = boot.At(t, null);
            for (var k = 0; k < 4; k++)
            {
                var group = boot.GatherParticles.Where(p => p.Group == k).ToList();
                var share = Smooth(0, 1, group.Sum(p => Smooth(.7, 1, p.Landed(t))) / group.Count);
                Assert.Equal(share, k < 3 ? frame.ArmReveal[k] : frame.CoreReveal, 9);
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_tile_rises_in_from_0_55_to_1_2_seconds_from_scale_0_94_on_ease_out_back()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        Assert.Equal(0, boot.At(.3, null).TileOpacity);
        Assert.Equal(0, boot.At(.55, null).TileOpacity);
        Assert.Equal(1, boot.At(1.2, null).TileOpacity, 9);
        Assert.Equal(.94, boot.At(.3, null).TileScale, 9);
        Assert.Equal(.94, boot.At(.55, null).TileScale, 9);
        Assert.Equal(1, boot.At(1.2, null).TileScale, 9);
        // ease-out-back (1.4): 1 + 2.4 (t - 1)^3 + 1.4 (t - 1)^2, overshooting 1 before it settles
        var back = 1 + 2.4 * Math.Pow(-.2, 3) + 1.4 * Math.Pow(-.2, 2);
        Assert.Equal(.94 + .06 * back, boot.At(.55 + .65 * .8, null).TileScale, 9);
        Assert.True(boot.At(.55 + .65 * .8, null).TileScale > 1);
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void Ignition_at_1_45_seconds_is_the_first_pulse_and_the_core_draws_in_before_it()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var ignition = boot.At(1.45, null).Comet;
        Assert.Equal(1.45, ignition.LastPulse!.Value, 9);
        Assert.Equal(.9, ignition.CoreScale, 9);
        Assert.Equal(0, ignition.Angle, 9);
        var before = boot.At(1.45 - .14, null);
        Assert.Equal(before.CoreReveal * (1 - .1 * .5), before.Comet.CoreScale, 6);
        Assert.Equal((.25 + .3 * before.CoreReveal) * (1 - .45 * .5), before.Comet.GlowOpacity, 6);
        Assert.Equal(0, before.Comet.Angle);
        Assert.Null(before.Comet.RingAge);
        // the first turn is the Comet turn, started at ignition
        Assert.Equal(Math.PI, boot.At(1.45 + 1.2, null).Comet.Angle, 7);
    }

    // ---- ED-103 (4), (5): disintegration and the window ----

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_mark_fades_out_over_0_22_seconds_from_0_1_seconds_after_the_final_rest()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var dis = BootSequence.FinalRest(2.5) + .1;
        Assert.Equal(1, boot.At(dis, 2.5).MarkOpacity, 9);
        Assert.Equal(.5, boot.At(dis + .11, 2.5).MarkOpacity, 6);
        Assert.Equal(0, boot.At(dis + .22, 2.5).MarkOpacity, 9);
        Assert.Equal(0, boot.At(dis + .22, 2.5).TileOpacity, 9);
        Assert.Equal([0.0, 0, 0], boot.At(dis + .22, 2.5).ArmReveal);
        Assert.Equal(1, boot.At(3.9, 2.5).MarkOpacity);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_mark_breaks_into_the_216_gather_particles_and_84_from_the_tile_and_the_outermost_58_percent_go_to_the_edge()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var d = boot.WindowParticles;
        Assert.Equal(300, d.Count);
        Assert.Equal(210, BootSequence.MarkSize);
        Assert.Equal(ReferenceWindow().Frame.Center, boot.Centre);
        Near(boot.Centre, boot.IconToStage(new Point(512, 512)), 1e-9);
        Near(boot.Centre - new Point(105, 105), boot.IconToStage(new Point(0, 0)), 1e-9);
        // every gather particle's landing point is a source
        var sources = d.Select(p => p.Source).ToList();
        foreach (var g in boot.GatherParticles) Assert.Contains(sources, s => Dist(s, boot.IconToStage(g.Target)) < 1e-9);
        // the tile's 84: 56 on its edge (72 or 952), 28 inside it
        var tile = d.Where(p => !boot.GatherParticles.Any(g => Dist(p.Source, boot.IconToStage(g.Target)) < 1e-9)).ToList();
        Assert.Equal(84, tile.Count);
        var scale = 210 / 1024.0;
        bool OnEdge(double v) => Math.Abs(v - 72) < 1e-6 || Math.Abs(v - 952) < 1e-6;
        var icon = tile.Select(p => new Point((p.Source.X - boot.Centre.X) / scale + 512, (p.Source.Y - boot.Centre.Y) / scale + 512)).ToList();
        Assert.Equal(56, icon.Count(q => (OnEdge(q.X) && q.Y >= 72 - 1e-6 && q.Y <= 952 + 1e-6) || (OnEdge(q.Y) && q.X >= 72 - 1e-6 && q.X <= 952 + 1e-6)));
        Assert.Equal(28, icon.Count(q => q.X > 72 + 1e-6 && q.X < 952 - 1e-6 && q.Y > 72 + 1e-6 && q.Y < 952 - 1e-6));
        // 58 % to the edge, and they are the ones furthest from the centre
        var edge = d.Where(p => p.IsEdge).ToList();
        var inner = d.Where(p => !p.IsEdge).ToList();
        Assert.Equal(174, edge.Count);
        Assert.True(edge.Min(p => Dist(p.Source, boot.Centre)) >= inner.Max(p => Dist(p.Source, boot.Centre)));
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Each_set_is_paired_with_its_targets_by_angle_about_the_centre_so_the_flight_order_never_crosses()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        foreach (var set in new[] { true, false })
        {
            var ordered = boot.WindowParticles.Where(p => p.IsEdge == set)
                .OrderBy(p => Math.Atan2(p.Source.Y - boot.Centre.Y, p.Source.X - boot.Centre.X)).ToList();
            var targetAngles = ordered.Select(p => Math.Atan2(p.Target.Y - boot.Centre.Y, p.Target.X - boot.Centre.X)).ToList();
            for (var i = 1; i < targetAngles.Count; i++) Assert.True(targetAngles[i] >= targetAngles[i - 1], $"set edge={set}: pair {i} out of angle order");
        }
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Edge_particles_go_to_evenly_spaced_points_on_the_rounded_outline_and_the_rest_to_the_content_areas()
    {
        var layout = ReferenceWindow();
        var boot = new BootSequence(layout, reduceMotion: false);
        var f = layout.Frame;
        const double r = 14;
        double OutlineDistance(Point p)
        {
            // distance from p to the rounded rectangle's outline
            double cx = Math.Clamp(p.X, f.X + r, f.Right - r), cy = Math.Clamp(p.Y, f.Y + r, f.Bottom - r);
            var inCornerZone = (p.X < f.X + r || p.X > f.Right - r) && (p.Y < f.Y + r || p.Y > f.Bottom - r);
            if (inCornerZone) return Math.Abs(Dist(p, new Point(cx, cy)) - r);
            return Math.Min(Math.Min(Math.Abs(p.X - f.X), Math.Abs(p.X - f.Right)), Math.Min(Math.Abs(p.Y - f.Y), Math.Abs(p.Y - f.Bottom)));
        }
        var edge = boot.WindowParticles.Where(p => p.IsEdge).Select(p => p.Target).ToList();
        var spacing = (2 * (f.Width + f.Height - 4 * r) + Tau * r) / edge.Count;
        foreach (var t in edge)
        {
            Assert.True(OutlineDistance(t) < 1e-6, $"{t} is not on the outline");
            var nearest = edge.Where(o => o != t).Min(o => Dist(o, t));
            Assert.InRange(nearest, .9 * spacing, spacing + 1e-6);
        }
        foreach (var p in boot.WindowParticles.Where(p => !p.IsEdge))
            Assert.Contains(layout.ContentAreas, a => a.Contains(p.Target));
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void A_window_particle_moves_18_to_54_DIP_straight_out_then_flies_a_curve_bent_up_to_40_DIP_and_melts_out()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        foreach (var p in boot.WindowParticles)
        {
            Assert.InRange(p.Delay, p.IsEdge ? .14 : .38, p.IsEdge ? .34 : .58);
            Assert.InRange(p.Duration, .72, .98);
            Assert.InRange(Math.Abs(p.Bend), 0, 40);
            Assert.Equal(p.Delay + p.Duration, p.LandsAt, 12);
            var kick = Dist(p.Source, p.Kick);
            Assert.InRange(kick, 18, 54);
            // straight out from the centre
            double outward = Math.Atan2(p.Source.Y - boot.Centre.Y, p.Source.X - boot.Centre.X), dir = Math.Atan2(p.Kick.Y - p.Source.Y, p.Kick.X - p.Source.X);
            Assert.Equal(0, Wrap(dir - outward), 9);
            Near(p.Source, p.Position(0), 1e-9);
            Near(p.Target, p.Position(p.LandsAt), 1e-6);
            Assert.Equal(0, p.Opacity(0));
            Assert.Equal(0, p.Opacity(p.LandsAt + .35), 9);
            Assert.Equal(p.Size * 210 / 1024.0 * .7, p.Radius(p.LandsAt), 9); // shrinks by 30 % as it lands (boot_sequence.html:376)
            if (!p.IsEdge)
            {
                // the out-move is done (.28 s, ease-out-cubic) before the flight starts
                Near(p.Kick, p.Position(.28), 1e-9);
                Near(p.Kick, p.Position(p.Delay), 1e-9);
                Assert.Equal(1, p.Opacity(.2 + 1e-9), 6);
                // half way (ease-in-out-cubic(.5) = .5): the quadratic curve from the kick point, bent by Bend to one side
                double mx = (p.Kick.X + p.Target.X) / 2, my = (p.Kick.Y + p.Target.Y) / 2, dx = p.Target.X - p.Kick.X, dy = p.Target.Y - p.Kick.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                var control = new Point(mx - dy / len * p.Bend, my + dx / len * p.Bend);
                Near(new Point(.25 * p.Kick.X + .5 * control.X + .25 * p.Target.X, .25 * p.Kick.Y + .5 * control.Y + .25 * p.Target.Y), p.Position(p.Delay + p.Duration / 2), 1e-6);
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_outline_glows_with_the_edge_share_the_window_shows_when_the_outline_has_landed_and_its_content_follows_the_share_of_all()
    {
        Assert.Equal(1.5, BootSequence.OutlineWidth);
        Assert.Equal(Color.Parse("#BED2FF"), BootSequence.OutlineColor);
        Assert.Equal(.85, BootSequence.OutlineOpacity);
        Assert.Equal(14, BootSequence.OutlineGlow);
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var dis = BootSequence.FinalRest(2.5) + .1;
        var first = boot.At(dis, 2.5);
        Assert.Equal(0, first.EdgeShare);
        Assert.Equal(0, first.OutlineOpacity);
        Assert.Equal(0, first.ContentOpacity);
        Assert.Equal(.985, first.ContentScale, 9);
        Assert.False(first.MainWindowShown);
        var shown = dis + boot.WindowParticles.Where(p => p.IsEdge).Max(p => p.LandsAt);
        Assert.Equal(shown, boot.MainWindowShownAt(2.5), 9);
        Assert.False(boot.At(shown - 1e-3, 2.5).MainWindowShown);
        Assert.True(boot.At(shown, 2.5).MainWindowShown);
        Assert.Equal(1, boot.At(shown, 2.5).EdgeShare, 9);
        // content opacity follows the share of all landed, from 45 % to 100 %
        foreach (var td in new[] { .6, .9, 1.1, 1.3, 1.5 })
        {
            var frame = boot.At(dis + td, 2.5);
            var all = boot.WindowParticles.Sum(p => Smooth(.85, 1, p.Fly(td))) / 300;
            var edges = boot.WindowParticles.Where(p => p.IsEdge).Sum(p => Smooth(.85, 1, p.Fly(td))) / 174;
            Assert.Equal(all, frame.AllShare, 9);
            Assert.Equal(edges, frame.EdgeShare, 9);
            Assert.Equal(Smooth(.45, 1, all), frame.ContentOpacity, 9);
            Assert.Equal(Smooth(0, 1, edges) * (1 - .85 * Smooth(.55, 1, all)), frame.OutlineOpacity, 9);
        }
        var last = dis + boot.WindowParticles.Max(p => p.LandsAt);
        var landed = boot.At(last, 2.5);
        Assert.Equal(1, landed.AllShare, 9);
        Assert.Equal(1, landed.ContentOpacity, 9);
        Assert.Equal(1, landed.ContentScale, 9);
        Assert.Equal(last + .35, boot.StageClosesAt(2.5), 9);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_content_scale_follows_out_cubic_of_the_content_opacity_in_mid_flight()
    {
        // ED-103 (5): the content's scale goes .985 to 1; the curve is boot_sequence.html:247,
        // s = .985 + .015 * outCubic(content), with outCubic(t) = 1 - (1 - t)^3 (:85). Review point 2: the ends alone
        // do not pin the curve, so the mid-flight frames are checked where the content is strictly between 0 and 1.
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: false);
        var dis = BootSequence.FinalRest(2.5) + .1;
        var midFlight = 0;
        for (var td = .5; td <= 1.6; td += .05)
        {
            var frame = boot.At(dis + td, 2.5);
            var all = boot.WindowParticles.Sum(p => Smooth(.85, 1, p.Fly(td))) / 300;
            var content = Smooth(.45, 1, all);
            Assert.Equal(.985 + .015 * (1 - Math.Pow(1 - content, 3)), frame.ContentScale, 9);
            if (content > .05 && content < .95)
            {
                midFlight++;
                // a linear curve would give .985 + .015 * content, which is below the out-cubic value here
                Assert.True(frame.ContentScale - (.985 + .015 * content) > 1e-4);
            }
        }
        Assert.True(midFlight >= 3, $"only {midFlight} mid-flight frames");
    }

    // ---- ED-102 (5), ED-103 (6): reduce motion ----

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Decision", "ED-102")]
    [Trait("Rule", "R-VIEW-6")]
    public void Under_reduce_motion_the_still_mark_fades_in_shows_for_2_4_seconds_then_fades_as_the_window_fades_in()
    {
        var boot = new BootSequence(ReferenceWindow(), reduceMotion: true);
        Assert.Empty(boot.GatherParticles);
        Assert.Empty(boot.WindowParticles);
        Assert.Equal(0, boot.At(0, null).MarkOpacity);
        Assert.Equal(1, boot.At(.3, null).MarkOpacity, 9);
        Assert.Equal(1, boot.At(2.4, null).MarkOpacity, 9);
        Assert.Equal(0, boot.At(2.7, null).MarkOpacity, 9);
        foreach (var t in new[] { 0, .3, 1.45, 2.4, 3.85 })
        {
            var frame = boot.At(t, 1);
            Assert.Equal(CometMotion.Still, frame.Comet);
            Assert.Equal(1, frame.TileScale);
            Assert.Equal(frame.MarkOpacity, frame.TileOpacity, 12);
        }
        Assert.Equal(0, boot.At(2.5, null).ContentOpacity);
        Assert.Equal(1, boot.At(2.9, null).ContentOpacity, 9);
        Assert.False(boot.At(2.49, null).MainWindowShown);
        Assert.True(boot.At(2.5, null).MainWindowShown);
        Assert.Equal(BootPhase.StillMark, boot.At(2.89, null).Phase);
        Assert.Equal(BootPhase.Closed, boot.At(2.9, null).Phase);
        Assert.Equal(2.5, boot.MainWindowShownAt(null), 9);
        Assert.Equal(2.9, boot.StageClosesAt(null), 9);
    }
}
