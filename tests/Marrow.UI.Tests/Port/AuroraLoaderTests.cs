using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

// OD-0113. Expectations are the locked loading-aurora.html:765-837, not the C# output. OD-0136 amendments 2-3 (ED-NEW-nth-motion) replaced the three
// loaders with the nth primitives and removed the loader control and painter; the motion functions stay (the boot and the diff label use them).
public sealed class AuroraLoaderTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(.399, 0)]
    [InlineData(.4, 0)]
    [InlineData(.45, .15625)]
    [InlineData(.5, .5)]
    [InlineData(.6, 1)]
    [Trait("Decision", "ED-097")]
    public void The_four_hundred_ms_blank_and_two_hundred_ms_smooth_fade_follow_the_page(double time, double expected) =>
        Assert.Equal(expected, AuroraLoaderMotion.Visibility(time), 10);

    [Theory]
    [InlineData(0, .55)]
    [InlineData(.6, .775)]
    [InlineData(1.2, 1)]
    [InlineData(2.4, .55)]
    [Trait("Decision", "ED-097")]
    public void Reduce_motion_breathes_between_fifty_five_and_one_hundred_percent_in_two_point_four_seconds(double time, double expected) =>
        Assert.Equal(expected, AuroraLoaderMotion.Breath(time), 10);

    [Fact]
    [Trait("Decision", "ED-097")]
    public void Curtain_rays_use_the_twenty_four_columns_and_the_locked_fold_foot_length_and_envelope()
    {
        Assert.Equal(24, AuroraLoaderMotion.CurtainColumns);
        var ray = AuroraLoaderMotion.CurtainRay(48, 0, 0);
        Assert.Equal(3.84, ray.X, 10);
        Assert.Equal(31.68, ray.Foot, 10);
        Assert.Equal(5.76, ray.Length, 10);
        Assert.Equal(0, ray.Alpha);
        var mid = AuroraLoaderMotion.CurtainRay(48, .84, 12);
        double u = 12.0 / 23, ph = .35;
        double fold = .5 + .5 * Math.Sin(2 * Math.PI * (u * 1.1 - ph));
        double env = Math.Pow(Math.Sin(Math.PI * u), .7);
        Assert.Equal(3.84 + 40.32 * u, mid.X, 10);
        Assert.Equal(31.68 + 3.36 * Math.Sin(2 * Math.PI * (u * 1.4 + ph)), mid.Foot, 10);
        Assert.Equal(48 * (.12 + .36 * fold) * (.4 + .6 * env), mid.Length, 10);
        Assert.Equal((.35 + .65 * fold) * env, mid.Alpha, 10);
    }

    [Fact]
    [Trait("Decision", "ED-097")]
    public void Arc_turn_and_colour_have_independent_one_point_five_and_two_point_eight_second_periods()
    {
        Assert.Equal(-Math.PI / 2, AuroraLoaderMotion.ArcAngle(0), 10);
        Assert.Equal(Math.PI / 2, AuroraLoaderMotion.ArcAngle(.75), 10);
        Assert.Equal(AuroraLoaderMotion.ArcAngle(.37), AuroraLoaderMotion.ArcAngle(.37 + 1.5), 10);
        Assert.Equal(new SKColor(60, 240, 170), AuroraLoaderMotion.Palette(true, 0));
        Assert.Equal(new SKColor(30, 190, 160), AuroraLoaderMotion.Palette(true, 1.0 / 3));
        Assert.Equal(new SKColor(169, 90, 254), AuroraLoaderMotion.Palette(true, 2.0 / 3)); // locked JS modulo rounding then |0
        Assert.Equal(new SKColor(8, 158, 108), AuroraLoaderMotion.Palette(false, 0));
        Assert.Equal(new SKColor(10, 128, 150), AuroraLoaderMotion.Palette(false, 1.0 / 3));
        Assert.Equal(new SKColor(111, 72, 223), AuroraLoaderMotion.Palette(false, 2.0 / 3));
    }
}
