// PORT-ONLY: the pill's other curves and Reduce Motion (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3, 10.4, 10.11): the size
// springs' constants, a spring's settle and end, Reduce Motion's 150 ms ease up to 200 DIP and its 120 ms fade through
// above (one step at 60 ms, the opacity down to zero there), and the morph's body fades, card height and fixed edge.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillCurveTests
{
    [Theory]
    [InlineData(PillCurve.WidthResponse, 503.5, 44.9)]
    [InlineData(PillCurve.ExpandResponse, 438.6, 41.9)]
    [InlineData(PillCurve.CollapseResponse, 584.0, 48.3)]
    public void The_size_springs_have_the_designs_constants(double response, double stiffness, double damping)
    {
        var w = 2 * Math.PI / response;

        // The design gives k and c to one decimal (k = 503.55 for 0.28 s is written 503.5).
        Assert.Equal(stiffness, w * w, 0.1);
        Assert.Equal(damping, 2 * w, 0.05);
        Assert.Equal(w, PillCurve.Spring(200, 440, 0, response, 0).Omega, 9);
    }

    [Fact]
    public void A_spring_settles_without_passing_its_target_and_its_end_shows_the_target()
    {
        var curve = PillCurve.Spring(200, 440, 0, PillCurve.WidthResponse, 1);

        Assert.Equal(200, curve.Value(1));
        Assert.All(Enumerable.Range(0, 1_000), ms => Assert.True(curve.Value(1 + (ms / 1000.0)) <= 440 + 1e-9));
        Assert.False(curve.Settled(1.1));
        Assert.True(curve.Settled(1.6));
        Assert.True(curve.Done(2));
        Assert.True(PillCurve.Still(5, 0).Done(0));
    }

    [Fact]
    public void A_spring_with_velocity_is_stiffened_so_it_passes_at_most_its_cap()
    {
        var curve = PillCurve.Spring(300, 440, 8_000, PillCurve.WidthResponse, 0);

        Assert.True(curve.Omega > 2 * Math.PI / PillCurve.WidthResponse);
        Assert.True(PillSpring.Excursion(300 - 440, 8_000, curve.Omega) <= 0.5 + 1e-6);
        Assert.Equal(8_000, curve.Velocity(0));
    }

    [Fact]
    public void Reduce_motion_eases_a_travel_of_up_to_200_DIP_in_150_ms_with_no_velocity()
    {
        var travel = PillTravel.Start(0, new PillVector(800, 8), new PillVector(681, 8), new PillVector(-3_000, 0), reduceMotion: true);

        Assert.Null(travel.Spring);
        Assert.Equal(800, travel.Value(0).X);
        Assert.Equal(681, travel.Value(0.150).X, 9);
        Assert.InRange(travel.Value(0.075).X, 681, 800);
        Assert.Equal(PillVector.Zero, travel.Velocity(0.05));
        Assert.Equal(1, travel.Opacity(0.075));
        Assert.False(travel.VisuallySettled(0.149));
        Assert.True(travel.VisuallySettled(0.150));
    }

    [Fact]
    public void Reduce_motion_fades_a_longer_travel_through_with_one_step_at_60_ms()
    {
        var travel = PillTravel.Start(0, new PillVector(800, 8), new PillVector(16, 8), PillVector.Zero, reduceMotion: true);

        Assert.Equal(800, travel.Value(0.059).X);
        Assert.Equal(16, travel.Value(0.060).X);
        Assert.Equal(1, travel.Opacity(0));
        Assert.Equal(0, travel.Opacity(0.060), 9);
        Assert.Equal(0.5, travel.Opacity(0.090), 9);
        Assert.Equal(1, travel.Opacity(0.120));
        Assert.True(travel.VisuallySettled(0.120));
    }

    [Fact]
    public void Without_reduce_motion_a_travel_is_the_spring()
    {
        var travel = PillTravel.Start(0, new PillVector(800, 8), new PillVector(16, 8), new PillVector(-500, 0), reduceMotion: false);

        Assert.NotNull(travel.Spring);
        Assert.Equal(-500, travel.Velocity(0).X);
        Assert.Equal(1, travel.Opacity(0.06));
    }

    [Fact]
    public void The_ease_is_cubic_bezier_0_2_0_0_1()
    {
        Assert.Equal(0, PillEase.Standard(0));
        Assert.Equal(1, PillEase.Standard(1));
        Assert.True(PillEase.Standard(0.5) > 0.8, "fast out, slow in: " + PillEase.Standard(0.5));
        Assert.True(PillEase.Standard(0.1) < PillEase.Standard(0.2));
    }

    [Fact]
    public void The_card_body_fades_in_from_120_to_300_ms_rising_6_DIP_and_out_in_the_first_100_ms()
    {
        Assert.Equal(0, PillMorph.BodyOpacity(true, 0.119));
        Assert.Equal(0.5, PillMorph.BodyOpacity(true, 0.210), 9);
        Assert.Equal(1, PillMorph.BodyOpacity(true, 0.300));
        Assert.Equal(6, PillMorph.BodyOffset(0.1));
        Assert.Equal(0, PillMorph.BodyOffset(0.3));
        Assert.Equal(1, PillMorph.BodyOpacity(false, 0));
        Assert.Equal(0, PillMorph.BodyOpacity(false, 0.100));
    }

    [Fact]
    public void The_card_is_440_DIP_wide_at_most_420_high_and_keeps_its_anchors_edge()
    {
        Assert.Equal(294, PillMorph.CardHeight(294, 1032, 8));
        Assert.Equal(420, PillMorph.CardHeight(600, 1032, 8));
        Assert.Equal(300 - 16 - 8, PillMorph.CardHeight(600, 300, 8));
        Assert.Equal(16, PillMorph.CardLeft(PillAnchor.Left, 16, 320));
        Assert.Equal(1904 - 440, PillMorph.CardLeft(PillAnchor.Right, 1584, 320));
        Assert.Equal(960 - 220, PillMorph.CardLeft(PillAnchor.Centre, 800, 320));
    }
}
