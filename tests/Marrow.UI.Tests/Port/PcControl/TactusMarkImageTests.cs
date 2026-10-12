// PORT-ONLY: the Tactus mark as pixels for the tray and notifications (OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5): the
// hand-hinted 16 px drawings carry Ready, Working and Paused; 24 and 32 px draw the master (its lift pose while paused);
// fully covered pixels are the one colour, uncovered ones clear; the ICO holds a PNG per tray scale.
using Marrow.Core.Port.PcControl.Mark;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-11")]
public sealed class TactusMarkImageTests
{
    private static readonly SKColor Ink = new(0xF6, 0xF6, 0xF8);

    public static TheoryData<int, TactusSmallState> Cases() => new()
    {
        { 16, TactusSmallState.Ready },
        { 16, TactusSmallState.Working },
        { 16, TactusSmallState.Paused },
        { 24, TactusSmallState.Ready },
        { 24, TactusSmallState.Paused },
        { 32, TactusSmallState.Working },
        { 32, TactusSmallState.Paused },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Covered_pixels_are_the_one_colour_and_uncovered_pixels_are_clear(int pixels, TactusSmallState state)
    {
        using var bitmap = TactusMarkImage.Render(pixels, state, Ink);
        using var shape = Expected(pixels, state);
        var inside = 0;
        var outside = 0;
        for (var y = 0; y < pixels; y++)
        {
            for (var x = 0; x < pixels; x++)
            {
                var colour = bitmap.GetPixel(x, y);
                var coverage = TactusCoverage.Of(shape, x, y);
                if (coverage == 1)
                {
                    inside++;
                    Assert.Equal(Ink, colour);
                }
                else if (coverage == 0)
                {
                    outside++;
                    Assert.Equal(0, colour.Alpha);
                }
                else
                {
                    Assert.True(Math.Abs(colour.Alpha - (coverage * 255)) <= 64, $"edge ({x},{y}) alpha {colour.Alpha} for coverage {coverage}");
                }
            }
        }
        Assert.True(inside >= pixels * 3 / 2, "inside " + inside); // the 16 px Ready mark, the thinnest, has 29 whole pixels
        Assert.True(outside >= pixels * pixels / 2, "outside " + outside);
    }

    [Fact]
    public void At_16_px_Working_presses_the_cut_shut_and_Paused_lifts_and_shows_two_bars()
    {
        using var ready = TactusMarkImage.Render(16, TactusSmallState.Ready, Ink);
        using var working = TactusMarkImage.Render(16, TactusSmallState.Working, Ink);
        using var paused = TactusMarkImage.Render(16, TactusSmallState.Paused, Ink);

        Assert.True(Differences(ready, working) >= 3, "the cut closes");
        // The bars fill x 11-12 and 14-15 from y 11 down; the gap between them and the 1 px moat stay clear.
        foreach (var (x, y) in new[] { (11, 12), (12, 15), (14, 11), (15, 14) }) Assert.Equal(Ink, paused.GetPixel(x, y));
        foreach (var (x, y) in new[] { (13, 12), (13, 15), (10, 12), (10, 10) }) Assert.Equal(0, paused.GetPixel(x, y).Alpha);
        // Lifted 1 px: the tip's top row moves up.
        Assert.Equal(Top(ready) - 1, Top(paused));
    }

    [Fact]
    public void At_150_and_200_percent_the_master_draws_and_paused_is_its_lift_pose()
    {
        using var rest = TactusMarkImage.Render(24, TactusSmallState.Ready, Ink);
        using var working = TactusMarkImage.Render(24, TactusSmallState.Working, Ink);
        using var lift = TactusMarkImage.Render(24, TactusSmallState.Paused, Ink);

        Assert.Equal(0, Differences(rest, working));
        Assert.True(Differences(rest, lift) > 10, "the lift pose opens the cut");
    }

    [Fact]
    public void A_PNG_decodes_to_the_rendered_size()
    {
        foreach (var size in TactusMarkImage.IconSizes)
        {
            using var decoded = SKBitmap.Decode(TactusMarkImage.Png(size, TactusSmallState.Ready, Ink));
            Assert.Equal(size, decoded.Width);
            Assert.Equal(size, decoded.Height);
        }
    }

    [Fact]
    public void The_ICO_holds_a_PNG_for_16_24_and_32_px()
    {
        var ico = TactusMarkImage.Ico(TactusSmallState.Working, Ink);

        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        Assert.Equal(3, BitConverter.ToUInt16(ico, 4));
        var end = 6 + (16 * 3);
        for (var i = 0; i < 3; i++)
        {
            var entry = 6 + (16 * i);
            var size = new[] { 16, 24, 32 }[i];
            Assert.Equal(size, ico[entry]);
            Assert.Equal(size, ico[entry + 1]);
            Assert.Equal(1, BitConverter.ToUInt16(ico, entry + 4));
            Assert.Equal(32, BitConverter.ToUInt16(ico, entry + 6));
            var length = BitConverter.ToInt32(ico, entry + 8);
            var offset = BitConverter.ToInt32(ico, entry + 12);
            Assert.Equal(end, offset);
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, ico[offset..(offset + 4)]);
            using var decoded = SKBitmap.Decode(ico[offset..(offset + length)]);
            Assert.Equal(size, decoded.Width);
            end += length;
        }
        Assert.Equal(ico.Length, end);
    }

    [Fact]
    public void A_size_under_one_pixel_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TactusMarkImage.Render(0, TactusSmallState.Ready, Ink));

    /// <summary>The covered area, built independently with path operations: the mark, less the knock-out, plus the bars.</summary>
    private static SKPath Expected(int pixels, TactusSmallState state)
    {
        if (!TactusGlyph.UsesSmallDrawing(pixels))
        {
            return TactusMarkImage.Path(TactusGlyph.Draw(state == TactusSmallState.Paused ? TactusPose.Lift : TactusPose.Rest), pixels / 24f);
        }
        var scale = pixels / 16f;
        var drawing = TactusGlyph.SmallDrawing(state);
        var shape = TactusMarkImage.Path(drawing.Mark, scale);
        if (drawing.KnockOut is not { } k || drawing.Extra is not { } extra) return shape;
        using var square = new SKPath();
        square.AddRect(SKRect.Create(0, 0, pixels, pixels));
        using var hole = new SKPath();
        hole.AddRect(SKRect.Create((float)k.X * scale, (float)k.Y * scale, (float)k.Width * scale, (float)k.Height * scale));
        using var outsideHole = square.Op(hole, SKPathOp.Difference);
        using var kept = shape.Op(outsideHole, SKPathOp.Intersect);
        using var bars = TactusMarkImage.Path(extra, scale);
        shape.Dispose();
        return kept.Op(bars, SKPathOp.Union);
    }

    private static int Differences(SKBitmap a, SKBitmap b)
    {
        var count = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                if (Math.Abs(a.GetPixel(x, y).Alpha - b.GetPixel(x, y).Alpha) > 64) count++;
            }
        }
        return count;
    }

    private static int Top(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 64) return y;
            }
        }
        return -1;
    }
}
