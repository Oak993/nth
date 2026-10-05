using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

// OD-0113. Expectations are the locked loading-aurora.html:765-837, not the C# output.
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

    [Theory]
    [InlineData(AuroraLoaderKind.CurtainFold, 48, 48, 2.4)]
    [InlineData(AuroraLoaderKind.FlowArc, 40, 40, 42)]
    [InlineData(AuroraLoaderKind.AuroraSheen, 180, 88, 1.8)]
    [Trait("Decision", "ED-097")]
    public void Loops_are_seamless_and_reduce_motion_holds_the_page_pose(AuroraLoaderKind kind, int width, int height, double period)
    {
        using var painter = new AuroraLoaderPainter();
        using var a = SKSurface.Create(new SKImageInfo(width, height));
        using var b = SKSurface.Create(new SKImageInfo(width, height));
        painter.Draw(a.Canvas, kind, width, height, .73, true, false);
        painter.Draw(b.Canvas, kind, width, height, .73 + period, true, false);
        using var first = a.Snapshot();
        using var second = b.Snapshot();
        Assert.Equal(first.Encode().ToArray(), second.Encode().ToArray());
        a.Canvas.Clear(); b.Canvas.Clear();
        painter.Draw(a.Canvas, kind, width, height, 0, false, true);
        painter.Draw(b.Canvas, kind, width, height, 2.4, false, true);
        using var stillA = a.Snapshot();
        using var stillB = b.Snapshot();
        Assert.Equal(stillA.Encode().ToArray(), stillB.Encode().ToArray());
    }

    [Theory]
    [InlineData(AuroraLoaderKind.CurtainFold, 48, 48, 1)]
    [InlineData(AuroraLoaderKind.CurtainFold, 48, 48, 1.25)]
    [InlineData(AuroraLoaderKind.CurtainFold, 48, 48, 1.5)]
    [InlineData(AuroraLoaderKind.CurtainFold, 48, 48, 2)]
    [InlineData(AuroraLoaderKind.FlowArc, 40, 40, 1)]
    [InlineData(AuroraLoaderKind.FlowArc, 40, 40, 1.25)]
    [InlineData(AuroraLoaderKind.FlowArc, 40, 40, 1.5)]
    [InlineData(AuroraLoaderKind.FlowArc, 40, 40, 2)]
    [InlineData(AuroraLoaderKind.AuroraSheen, 180, 88, 1)]
    [InlineData(AuroraLoaderKind.AuroraSheen, 180, 88, 1.25)]
    [InlineData(AuroraLoaderKind.AuroraSheen, 180, 88, 1.5)]
    [InlineData(AuroraLoaderKind.AuroraSheen, 180, 88, 2)]
    [Trait("Decision", "ED-097")]
    public void Drawing_allocates_no_managed_bytes_after_warm_up(AuroraLoaderKind kind, int width, int height, double scale)
    {
        using var painter = new AuroraLoaderPainter();
        using var surface = SKSurface.Create(new SKImageInfo((int)(width * scale), (int)(height * scale)));
        // SKSurface.Canvas performs a global wrapper lookup which can allocate a lock waiter under contention.
        // The compositor already owns its canvas; this test supplies the same stable canvas to the painter.
        var canvas = surface.Canvas;
        canvas.Scale((float)scale);
        for (var i = 0; i < 180; i++) painter.Draw(canvas, kind, width, height, i / 60.0, true, false);
        var frameBytes = new long[144];
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 144; i++)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            painter.Draw(canvas, kind, width, height, i / 60.0, true, false);
            frameBytes[i] = GC.GetAllocatedBytesForCurrentThread() - start;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"Allocated {allocated} bytes: {string.Join(", ", frameBytes.Select((bytes, i) => (bytes, i)).Where(x => x.bytes != 0))}");
    }
}
