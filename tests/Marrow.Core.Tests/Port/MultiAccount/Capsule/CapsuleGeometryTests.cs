using Marrow.Core.Port.MultiAccount.Capsule;

namespace Marrow.Core.Tests.Port.MultiAccount.Capsule;

// MA-9 (step 3a): points along the capsule's outline (the plain arc, the segmented rim, the orbit, the dial) and colour mixes in OKLab (the aurora, the glow).
[Trait("Feature", "MA-9")]
[Trait("Rule", "R-TEST-4")]
public sealed class CapsuleGeometryTests
{
    private static void Near((double X, double Y) point, double x, double y)
    {
        Assert.Equal(x, point.X, 3);
        Assert.Equal(y, point.Y, 3);
    }

    [Fact]
    public void The_perimeter_is_two_straights_and_a_circle_of_the_inset_radius()
    {
        Assert.Equal(2 * (34 - 22) + 2 * Math.PI * (11 - 0.6), CapsuleLayout.Perimeter(34, 22, 0.6), 6);
    }

    [Fact]
    public void From_the_top_middle_the_outline_runs_clockwise_right_along_the_top_down_round_the_right_end_back_along_the_bottom_and_up_the_left_end()
    {
        Near(CapsuleLayout.Along(34, 22, 0.6, false, 0), 17, 0.6);
        var p = CapsuleLayout.Perimeter(34, 22, 0.6);
        Near(CapsuleLayout.Along(34, 22, 0.6, false, (17 - 11) / p), 23, 0.6);
        Near(CapsuleLayout.Along(34, 22, 0.6, false, (6 + Math.PI * 10.4 / 2) / p), 23 + 10.4, 11);
        Near(CapsuleLayout.Along(34, 22, 0.6, false, (6 + Math.PI * 10.4) / p), 23, 21.4);
        Near(CapsuleLayout.Along(34, 22, 0.6, false, (6 + Math.PI * 10.4 + 12) / p), 11, 21.4);
        Near(CapsuleLayout.Along(34, 22, 0.6, false, 1), 17, 0.6);
    }

    [Fact]
    public void From_the_left_middle_the_rim_climbs_the_left_end_first()
    {
        var p = CapsuleLayout.Perimeter(36, 22, 0.75);
        Near(CapsuleLayout.Along(36, 22, 0.75, true, 0), 0.75, 11);
        var q = 11 - 0.75;
        Near(CapsuleLayout.Along(36, 22, 0.75, true, (Math.PI * q / 2) / p), 11, 0.75);
        Near(CapsuleLayout.Along(36, 22, 0.75, true, (Math.PI * q / 2 + 14) / p), 25, 0.75);
        Near(CapsuleLayout.Along(36, 22, 0.75, true, 1), 0.75, 11);
    }

    [Fact]
    public void The_dial_circle_starts_at_the_top_and_runs_clockwise()
    {
        Near(CapsuleLayout.CircleAlong(11, 11, 9.9, 0), 11, 1.1);
        Near(CapsuleLayout.CircleAlong(11, 11, 9.9, 0.25), 20.9, 11);
        Near(CapsuleLayout.CircleAlong(11, 11, 9.9, 0.5), 11, 20.9);
        Near(CapsuleLayout.CircleAlong(11, 11, 9.9, 0.75), 1.1, 11);
    }

    [Fact]
    public void A_fraction_outside_the_lap_wraps_round()
    {
        var a = CapsuleLayout.Along(44, 22, 3.1, false, 0.3);
        var b = CapsuleLayout.Along(44, 22, 3.1, false, 1.3);
        Near(b, a.X, a.Y);
        var c = CapsuleLayout.Along(44, 22, 3.1, false, -0.7);
        Near(c, a.X, a.Y);
    }

    [Theory]
    [InlineData(58, 214, 168, 255, 255, 255, 0.64, "147,230,199")]
    [InlineData(240, 105, 90, 255, 255, 255, 0.64, "251,162,149")]
    [InlineData(14, 159, 122, 127, 227, 255, 0.5, "80,193,187")]
    [InlineData(194, 122, 14, 183, 149, 255, 0.55, "188,139,147")]
    public void An_oklab_mix_is_the_pages_own(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2, double share, string expected)
    {
        var mixed = UsageLight.Mix((r1, g1, b1), (r2, g2, b2), share);
        Assert.Equal(expected, mixed.R + "," + mixed.G + "," + mixed.B);
    }

    [Fact]
    public void A_mix_of_one_share_is_the_first_colour_and_none_is_the_second()
    {
        Assert.Equal((58, 214, 168), UsageLight.Mix((58, 214, 168), (255, 255, 255), 1));
        Assert.Equal((255, 255, 255), UsageLight.Mix((58, 214, 168), (255, 255, 255), 0));
    }
}
