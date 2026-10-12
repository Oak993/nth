// PORT-ONLY: the Tactus pointer's glide (OD-0144, ED-NEW-pcctl-21; DESIGN.md 4.5, 5.3 "glide curve and duration
// bounds"): from rest every glide is within 0.5 DIP of its target in 180 ms (40 DIP or less) to 260 ms (800 DIP or more),
// not before; it is never linear and never overshoots from rest; a new target keeps its place and velocity, and a reversal
// at speed runs on at most 2 DIP before it turns.
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Core.Port.PcControl.Pointer;

namespace Marrow.Core.Tests.Port.PcControl.Pointer;

[Trait("Decision", "ED-NEW-pcctl-21")]
public sealed class PointerGlideTests
{
    [Theory]
    [InlineData(40, 0.18)]
    [InlineData(300, 0.18 + (0.08 * 260 / 760))]
    [InlineData(800, 0.26)]
    [InlineData(1600, 0.26)]
    public void From_rest_a_glide_lands_within_half_a_DIP_in_its_time_and_not_before(double distance, double seconds)
    {
        var glide = PointerGlide.Start(10, new PillVector(100, 100), new PillVector(100 + distance, 100), PillVector.Zero);

        Assert.Equal(seconds, PointerGlide.LandingSeconds(distance), 9);
        Assert.InRange((glide.Value(10 + seconds) - glide.Target).Length, 0.49, PointerGlide.Landing + 1e-9);
        Assert.True((glide.Value(10 + seconds - 0.01) - glide.Target).Length > PointerGlide.Landing);
        Assert.InRange(glide.LandsAt(), 10 + seconds - 0.02, 10 + seconds + 0.0015);
        Assert.True(glide.Landed(glide.LandsAt()));
    }

    [Fact]
    public void A_short_hop_lands_sooner_than_180_ms_and_every_glide_by_260_ms()
    {
        var hop = PointerGlide.Start(0, PillVector.Zero, new PillVector(12, 0), PillVector.Zero);
        var far = PointerGlide.Start(0, PillVector.Zero, new PillVector(2400, 900), PillVector.Zero);

        Assert.True(hop.LandsAt() <= PointerGlide.ShortSeconds);
        Assert.True(far.LandsAt() <= PointerGlide.LongSeconds + 0.0015);
    }

    [Fact]
    public void It_is_never_linear_and_never_passes_its_target_from_rest()
    {
        var glide = PointerGlide.Start(0, PillVector.Zero, new PillVector(600, 0), PillVector.Zero);
        var last = 0.0;
        var firstStep = glide.Value(1 / 60.0).X;

        for (var ms = 1; ms <= 400; ms++)
        {
            var x = glide.Value(ms / 1000.0).X;
            Assert.True(x >= last - 1e-9, $"went back at {ms} ms");
            Assert.True(x <= 600 + 1e-9, $"passed the target at {ms} ms");
            last = x;
        }
        // It starts gently (no jump in the first frame) and is past halfway at a third of its time (an ease out).
        Assert.InRange(firstStep, 1, 120);
        Assert.True(glide.Value(PointerGlide.LandingSeconds(600) / 3).X > 300);
    }

    [Fact]
    public void A_new_target_keeps_its_place_and_velocity_and_a_reversal_at_speed_runs_on_at_most_2_DIP()
    {
        var glide = PointerGlide.Start(0, PillVector.Zero, new PillVector(800, 0), PillVector.Zero);
        var at = 0.05;

        var back = glide.Retarget(at, new PillVector(100, 0));

        Assert.Equal(glide.Value(at).X, back.Value(at).X, 9);
        Assert.Equal(glide.Velocity(at).X, back.Velocity(at).X, 6);
        var path = Enumerable.Range(0, 600).Select(ms => back.Value(at + (ms / 1000.0)).X).ToArray();
        Assert.InRange(path.Max() - back.Value(at).X, 0, PointerGlide.MaxExcursion + 1e-6);
        Assert.True(path.Min() >= 100 - 1e-6);
        Assert.True(back.Landed(at + 0.6));
    }

    [Fact]
    public void Its_end_draws_exactly_the_target()
    {
        var glide = PointerGlide.Start(0, PillVector.Zero, new PillVector(333.3, 41.7), PillVector.Zero);

        Assert.NotEqual(glide.Target, glide.Presented(0.2));
        Assert.Equal(glide.Target, glide.Presented(1));
        Assert.True(glide.Ended(1));
    }
}
