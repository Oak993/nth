// PORT-ONLY: the pill's travel spring (OD-0145, ED-NEW-pcctl-13 item 6; DESIGN.md 10.3, 10.11): the closed form against a
// fine integration, the exact excursion against a 1 ms scan, the 3 DIP cap over a grid of throws with the velocity kept
// whole, every travel from rest landing within 1 DIP at 400 ms, the design's responses, continuity when grabbed or
// retargeted, and the settle predicates.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillSpringTests
{
    private static readonly double[] Distances = [0, 10, 59, 60, 119, 200, 437, 610, 778, 1_200, 2_000, 4_000];
    private static readonly double[] Velocities = [-8_000, -6_000, -3_000, -1_200, -300, 0, 300, 1_200, 3_000, 6_000, 8_000];

    [Fact]
    public void The_closed_form_matches_a_fine_numerical_integration_within_a_hundredth_of_a_DIP()
    {
        foreach (var (x0, v0) in new[] { (400.0, 0.0), (-728.0, 2_500.0), (60.0, -6_000.0), (0.0, 3_000.0), (1_500.0, 8_000.0) })
        {
            var spring = PillSpring.Travel(0, new PillVector(x0, 0), PillVector.Zero, new PillVector(v0, 0));
            var w = spring.Omega;
            double x = x0, v = v0;
            const double h = 1e-5;
            double A(double p, double q) => (-w * w * p) - (2 * w * q);
            for (var step = 1; step <= 60_000; step++)
            {
                // Runge-Kutta 4 at 10 microseconds, compared every 10 ms.
                var k1x = v;
                var k1v = A(x, v);
                var k2x = v + (h / 2 * k1v);
                var k2v = A(x + (h / 2 * k1x), v + (h / 2 * k1v));
                var k3x = v + (h / 2 * k2v);
                var k3v = A(x + (h / 2 * k2x), v + (h / 2 * k2v));
                var k4x = v + (h * k3v);
                var k4v = A(x + (h * k3x), v + (h * k3v));
                x += h / 6 * (k1x + (2 * k2x) + (2 * k3x) + k4x);
                v += h / 6 * (k1v + (2 * k2v) + (2 * k3v) + k4v);
                if (step % 1_000 == 0) Assert.True(Math.Abs(x - spring.Value(step * h).X) <= 0.01, $"x0 {x0} v0 {v0} at {step * h}: {x} vs {spring.Value(step * h).X}");
            }
        }
    }

    [Fact]
    public void The_exact_excursion_matches_a_1_ms_scan()
    {
        foreach (var x0 in new[] { -500.0, -60, 0, 60, 500 })
        {
            foreach (var v0 in new[] { -5_000.0, -800, 0, 800, 5_000 })
            {
                foreach (var w in new[] { 12.0, 20, 30 })
                {
                    var s = Math.Sign(v0);
                    double Past(double t) => (Math.Exp(-w * t) * (x0 + ((v0 + (w * x0)) * t)) * s) - Math.Max(0, x0 * s);
                    var scanned = 0.0;
                    var best = 0.0;
                    for (var t = 0.0; t <= 2; t += 0.001)
                    {
                        if (Past(t) > scanned) (scanned, best) = (Past(t), t);
                    }
                    // A 1 ms sample can miss a sharp peak by more than 0.01 DIP (5,000 DIP/s at w 30): the scan's best
                    // millisecond is searched again in 1 us steps, and the exact value is never below the coarse scan.
                    var refined = scanned;
                    for (var t = Math.Max(0, best - 0.001); t <= best + 0.001; t += 0.000_001) refined = Math.Max(refined, Past(t));
                    var exact = PillSpring.Excursion(x0, v0, w);
                    Assert.Equal(v0 == 0 ? 0 : refined, exact, 0.01);
                    Assert.True(v0 == 0 || exact >= scanned - 1e-9);
                }
            }
        }
    }

    [Fact]
    public void Over_a_grid_of_throws_no_travel_goes_more_than_3_DIP_past_and_the_velocity_is_kept_whole()
    {
        foreach (var d in Distances)
        {
            foreach (var v in Velocities)
            {
                foreach (var side in new[] { 1, -1 })
                {
                    var from = new PillVector(side * d, 0);
                    var spring = PillSpring.Travel(5, from, PillVector.Zero, new PillVector(v, 0));

                    Assert.Equal(v, spring.Velocity(5).X);
                    Assert.True(spring.Omega >= spring.DistanceOmega, "never softer than the distance spring");
                    Assert.True(PillSpring.Excursion(from.X, v, spring.Omega) <= PillSpring.MaxExcursion + 1e-6, $"d {side * d} v {v}");
                    // And sampled: how far it goes in the throw's direction past the farther of the anchor and the release
                    // point; from rest, how far it crosses the anchor.
                    var s = Math.Sign(v);
                    var past = 0.0;
                    for (var t = 5.0; t < 7; t += 0.0005)
                    {
                        var x = spring.Value(t).X;
                        past = Math.Max(past, s != 0 ? (x * s) - Math.Max(0, from.X * s) : -x * Math.Sign(from.X));
                    }
                    Assert.True(past <= PillSpring.MaxExcursion + 0.01, $"sampled {past} for d {side * d} v {v}");
                }
            }
        }
    }

    [Fact]
    public void From_rest_every_travel_is_within_1_DIP_of_its_anchor_at_400_ms()
    {
        for (var d = 60.0; d <= 4_000; d += 37)
        {
            var spring = PillSpring.Travel(0, new PillVector(d, 0), PillVector.Zero, PillVector.Zero);

            Assert.True(Math.Abs(spring.Value(0.401).X) <= 1, $"{d} DIP at 401 ms: {spring.Value(0.401).X}");
            Assert.True(Math.Abs(spring.Value(0.399).X) > 1, $"{d} DIP at 399 ms: {spring.Value(0.399).X}");
        }
    }

    [Theory]
    [InlineData(10, 0.416)]
    [InlineData(60, 0.416)]
    [InlineData(100, 0.379)]
    [InlineData(200, 0.338)]
    [InlineData(364, 0.310)]
    [InlineData(728, 0.283)]
    [InlineData(1_200, 0.266)]
    [InlineData(2_000, 0.251)]
    [InlineData(4_000, 0.234)]
    public void Short_moves_are_gentle_and_long_ones_brisk(double distance, double response) =>
        Assert.Equal(response, 2 * Math.PI / PillSpring.OmegaForDistance(distance), 0.001);

    [Fact]
    public void Both_axes_share_one_stiffness_so_the_path_is_one_curve()
    {
        // 100 DIP out on x, thrown back at the 8,000 DIP/s clamp: the distance spring alone would pass the anchor by about
        // 100 DIP, so x stiffens it, and y (from rest) runs on the same stiffness.
        var spring = PillSpring.Travel(0, new PillVector(100, 90), PillVector.Zero, new PillVector(-8_000, 0));

        Assert.True(PillSpring.Excursion(100, -8_000, spring.DistanceOmega) > 50);
        Assert.True(spring.Omega > spring.DistanceOmega, "the hard x throw stiffens both axes");
        Assert.True(PillSpring.Excursion(90, 0, spring.Omega) == 0);
        Assert.True(PillSpring.Excursion(100, -8_000, spring.Omega) <= 3 + 1e-6);
    }

    [Fact]
    public void A_grab_or_a_retarget_mid_travel_keeps_value_and_velocity_continuous()
    {
        var first = PillSpring.Travel(0, new PillVector(-700, 40), PillVector.Zero, new PillVector(1_500, -200));
        var at = 0.137;

        var second = first.Retarget(at, new PillVector(600, 0));

        Assert.Equal(first.Value(at).X, second.Value(at).X, 9);
        Assert.Equal(first.Value(at).Y, second.Value(at).Y, 9);
        Assert.Equal(first.Velocity(at).X, second.Velocity(at).X, 6);
        Assert.Equal(first.Velocity(at).Y, second.Velocity(at).Y, 6);
    }

    [Fact]
    public void Visually_settled_is_within_1_DIP_under_20_DIP_s_and_the_curve_ends_on_the_target()
    {
        var spring = PillSpring.Travel(0, new PillVector(500, 0), PillVector.Zero, PillVector.Zero);

        Assert.False(spring.VisuallySettled(0.2));
        var settled = Enumerable.Range(0, 2_000).Select(ms => ms / 1000.0).First(spring.VisuallySettled);
        Assert.InRange(settled, 0.39, 0.45);
        var ended = Enumerable.Range(0, 2_000).Select(ms => ms / 1000.0).First(t => spring.Ended(t, 1.5));
        Assert.True(ended > settled);
        Assert.Equal(PillVector.Zero, spring.Presented(ended, 1.5));
        Assert.NotEqual(PillVector.Zero, spring.Presented(ended - 0.05, 1.5));
    }
}
