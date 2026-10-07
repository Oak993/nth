using Marrow.Core.Port.MultiAccount.Capsule;

namespace Marrow.Core.Tests.Port.MultiAccount.Capsule;

// MA-9 (step 3a): the usage light on the 1% scale: one colour per percent of what is left, smooth, the page's anchors, never a brand colour.
[Trait("Feature", "MA-9")]
[Trait("Rule", "R-TEST-4")]
public sealed class UsageLightTests
{
    private static string Text((byte R, byte G, byte B) c) => c.R + "," + c.G + "," + c.B;

    [Fact]
    public void Every_percent_has_the_colour_the_pages_own_ramp_code_gives_in_both_themes()
    {
        for (var k = 0; k <= 100; k++)
        {
            Assert.Equal(UsageLightReference.Light[k], Text(UsageLight.Color(k / 100.0, dark: false)));
            Assert.Equal(UsageLightReference.Dark[k], Text(UsageLight.Color(k / 100.0, dark: true)));
        }
    }

    [Fact]
    public void The_anchors_are_emerald_at_full_teal_blue_at_half_amber_at_a_quarter_and_soft_red_near_the_handshake_point()
    {
        Assert.Equal("14,159,122", Text(UsageLight.Color(1, false)));
        Assert.Equal("27,134,182", Text(UsageLight.Color(0.5, false)));
        Assert.Equal("194,122,14", Text(UsageLight.Color(0.25, false)));
        Assert.Equal("201,71,59", Text(UsageLight.Color(0.05, false)));
        Assert.Equal("58,214,168", Text(UsageLight.Color(1, true)));
        Assert.Equal("79,182,232", Text(UsageLight.Color(0.5, true)));
        Assert.Equal("240,169,60", Text(UsageLight.Color(0.25, true)));
        Assert.Equal("240,105,90", Text(UsageLight.Color(0.05, true)));
    }

    [Fact]
    public void Below_the_last_anchor_it_stays_soft_red_and_out_of_range_input_is_kept_in_range()
    {
        Assert.Equal(UsageLight.Color(0.05, false), UsageLight.Color(0, false));
        Assert.Equal(UsageLight.Color(0.05, true), UsageLight.Color(-3, true));
        Assert.Equal(UsageLight.Color(1, false), UsageLight.Color(7, false));
        Assert.Equal(UsageLight.Color(1, false), UsageLight.Color(double.NaN, false));
    }

    [Fact]
    public void A_fraction_between_percents_takes_the_nearest_percent()
    {
        Assert.Equal(UsageLight.Color(0.37, false), UsageLight.Color(0.3699, false));
        Assert.Equal(UsageLight.Color(0.38, true), UsageLight.Color(0.3751, true));
    }

    [Fact]
    public void The_scale_is_smooth_with_no_bands_one_percent_never_jumps_the_colour()
    {
        foreach (var dark in new[] { false, true })
            for (var k = 1; k <= 100; k++)
            {
                var a = UsageLight.Color((k - 1) / 100.0, dark);
                var b = UsageLight.Color(k / 100.0, dark);
                var step = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                Assert.True(step <= 23, $"{(dark ? "dark" : "light")} {k}%: a step of {step}");
            }
    }

    [Fact]
    public void The_light_carries_no_brand_colour_of_a_service()
    {
        string[] brands = ["217,119,87", "16,163,127", "142,124,255", "43,179,163", "77,107,254"]; // the page's per-service hues (Claude, Codex, ...)
        foreach (var k in Enumerable.Range(0, 101))
        {
            Assert.DoesNotContain(Text(UsageLight.Color(k / 100.0, false)), brands);
            Assert.DoesNotContain(Text(UsageLight.Color(k / 100.0, true)), brands);
        }
    }
}
