using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Port.Updates;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using NthMotion = Marrow.UI.Port.Nth.NthMotion;
using Marrow.UI.Port.Settings;
using SkiaSharp;
using Xunit;

namespace Marrow.App.Tests.Nth;

// nth motion pack, group B: the download, update and first-run sites (steps 3, 4, 10 and 15 of nth-motion-review.html). The pack's primitives are drawn on the render thread, so these tests assert that each
// site hosts the right primitive at the right size and drives it with the right state, and (for the About install capsule, which is drawn by a painter of this group) read the painter's pixels against
// values derived from the spec's build notes.

/// <summary>The About install capsule's painter into a bitmap: positions and colours come from the spec (step 10 build notes), not from a picture of the code.</summary>
public sealed class NthSitesBInstallCapsulePixelTests
{
    private const int W = 168, H = 26;

    private static SKBitmap Render(double value, bool dark, bool calm, double now, double? doneAgo = null)
    {
        var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var painter = new UpdateInstallCapsulePainter();
        var state = new UpdateInstallCapsuleState(NthSpring.At(value), doneAgo is { } ago ? now - ago : null);
        painter.Draw(canvas, new NthFrame(new Size(W, H), dark, calm, TimeProvider.System, 0, state), now);
        return bitmap;
    }

    // vertical gradient --ftop to --fbot over the capsule's height, sampled at the pixel's centre
    private static SKColor FillAt(NthPalette palette, int y)
    {
        var t = (y + .5) / H;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return new SKColor(Mix(palette.FillTop.Red, palette.FillBottom.Red), Mix(palette.FillTop.Green, palette.FillBottom.Green), Mix(palette.FillTop.Blue, palette.FillBottom.Blue));
    }

    private static void Near(SKColor expected, SKColor actual, int tolerance, string what)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance && Math.Abs(expected.Blue - actual.Blue) <= tolerance,
            $"{what}: expected {expected}, drawn {actual}");
    }

    [Theory]
    [Trait("Decision", "OD-0136")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_capsule_is_the_track_with_the_metal_fill_ending_in_a_round_nose_at_p_times_W(bool dark)
    {
        var palette = NthPalette.For(dark);
        using var bitmap = Render(.5, dark, calm: true, now: 100);
        // 168 x 26, r 13: the corners of the box are outside the capsule, the middle of each end is inside it
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(W - 1, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(0, H - 1).Alpha);
        Assert.Equal(255, bitmap.GetPixel(13, 13).Alpha);
        // past the fill, the capsule is --track
        Near(palette.Track, bitmap.GetPixel(150, 13), 1, "track");
        // inside the fill, the vertical gradient --ftop to --fbot (the label is drawn in the middle: x = 10 is clear of it)
        foreach (var y in new[] { 3, 13, 22 }) Near(FillAt(palette, y), bitmap.GetPixel(10, y), 3, $"fill at y {y}");
        Assert.True(bitmap.GetPixel(10, 3).Red > bitmap.GetPixel(10, 22).Red, "the fill is lighter on top");
        // the window's right end is a round nose of radius 13 at x = p * W = 84: at y = 1.5 it has come in by 13 - sqrt(13^2 - 11.5^2) = 6.94 px, so x = 77.06 is where the fill stops on that row
        Near(FillAt(palette, 1), bitmap.GetPixel(72, 1), 3, "inside the nose");
        Near(palette.Track, bitmap.GetPixel(82, 1), 1, "outside the nose");
        // at mid height the nose reaches x = 84
        Near(palette.Track, bitmap.GetPixel(88, 6), 1, "past the nose at y 6");
    }

    [Theory]
    [Trait("Decision", "OD-0136")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_label_is_drawn_twice_the_text_colour_on_the_track_and_the_on_colour_inside_the_fill(bool dark)
    {
        var palette = NthPalette.For(dark);
        using var bitmap = Render(.5, dark, calm: true, now: 100);
        int Count(int x0, int x1, Func<SKColor, bool> test)
        {
            var count = 0;
            for (var x = x0; x < x1; x++)
                for (var y = 6; y < 20; y++)
                    if (test(bitmap.GetPixel(x, y))) count++;
            return count;
        }
        // right of the nose (x > 84) the glyphs are --text on --track; the capsule's centre is the label's centre (the label is about 80 px wide at 11.5 px)
        var onTrack = Count(88, 125, c => Math.Abs(c.Red - palette.Text.Red) + Math.Abs(c.Green - palette.Text.Green) + Math.Abs(c.Blue - palette.Text.Blue) <= 60);
        Assert.True(onTrack >= 12, $"{onTrack} pixels of --text on the track side of the label");
        // left of the nose the glyphs are --on on the fill
        var inFill = Count(44, 80, c => Math.Abs(c.Red - palette.On.Red) + Math.Abs(c.Green - palette.On.Green) + Math.Abs(c.Blue - palette.On.Blue) <= 60);
        Assert.True(inFill >= 12, $"{inFill} pixels of --on in the fill side of the label");
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_light_passes_inside_the_fill_only_white_at_half_in_light_and_at_85_percent_in_dark_and_stops_at_100_percent()
    {
        foreach (var dark in new[] { false, true })
        {
            var palette = NthPalette.For(dark);
            const double p = .5;
            var fillWidth = p * W;
            // find a moment when the band's centre is inside the fill (the cycle is 2.6 s, travelling for 72 % of it)
            double? centre = null; double at = 0;
            for (var t = .1; t < 1.9 && centre is null; t += .05)
                if (NthMotion.BarLight(t, fillWidth, W, p, false) is { } c && c > 20 && c < fillWidth - 20) { centre = c; at = t; }
            Assert.NotNull(centre);
            using var plain = Render(p, dark, calm: true, now: at);
            using var lit = Render(p, dark, calm: false, now: at);
            var x = (int)Math.Round(centre!.Value); const int y = 3; // above the label
            var baseline = plain.GetPixel(x, y);
            var shown = lit.GetPixel(x, y);
            // the band is --fill-light (white at 50 % / 85 %) at its centre over the fill
            var alpha = palette.FillLight.Alpha / 255.0;
            SKColor Over(SKColor under) => new((byte)Math.Round(under.Red + (255 - under.Red) * alpha), (byte)Math.Round(under.Green + (255 - under.Green) * alpha), (byte)Math.Round(under.Blue + (255 - under.Blue) * alpha));
            Near(Over(baseline), shown, 8, $"light at its centre (dark {dark})");
            // the track beyond the fill carries no light
            Near(plain.GetPixel(150, y), lit.GetPixel(150, y), 0, "track beyond the fill");
            // at 100 % the light stops: the same moment draws the plain fill
            using var fullLit = Render(1, dark, calm: false, now: at);
            using var fullPlain = Render(1, dark, calm: true, now: at);
            Near(fullPlain.GetPixel(60, y), fullLit.GetPixel(60, y), 0, "full capsule has no light");
        }
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void At_100_percent_the_working_label_gives_way_to_the_done_label_over_a_quarter_of_a_second()
    {
        // the two labels cross-fade: before the done moment only the working label; after 0.25 s only the done label; the labels differ, so the pictures do
        using var working = Render(1, false, calm: true, now: 100);
        using var done = Render(1, false, calm: true, now: 100, doneAgo: 1);
        using var half = Render(1, false, calm: true, now: 100, doneAgo: .125);
        long Diff(SKBitmap a, SKBitmap b)
        {
            long sum = 0;
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                    sum += Math.Abs(a.GetPixel(x, y).Red - b.GetPixel(x, y).Red);
            return sum;
        }
        Assert.True(Diff(working, done) > 800, "the done label is a different picture");
        // halfway, the picture is between the two (the cross-fade is a fade, not a cut)
        Assert.True(Diff(working, half) > 0 && Diff(half, done) > 0);
        Assert.True(Diff(working, half) < Diff(working, done) && Diff(half, done) < Diff(working, done));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_capsule_painter_moves_only_while_the_fill_springs_or_the_light_passes_and_for_the_cross_fade()
    {
        using var painter = new UpdateInstallCapsulePainter();
        NthFrame Frame(UpdateInstallCapsuleState state, bool calm = false) => new(new Size(W, H), false, calm, TimeProvider.System, 0, state);
        Assert.False(painter.Moving(Frame(new(NthSpring.At(0), null)), 5)); // nothing to show, nothing to draw
        Assert.True(painter.Moving(Frame(new(NthSpring.At(.5), null)), 5)); // the light passes inside a partial fill
        Assert.False(painter.Moving(Frame(new(NthSpring.At(.5), null), calm: true), 5)); // calm: no light
        Assert.True(painter.Moving(Frame(new(NthSpring.At(.5).Retarget(.9, 4.9), null), calm: true), 5)); // the spring is on its way
        Assert.False(painter.Moving(Frame(new(NthSpring.At(1), 0)), 5)); // done long ago: at rest
        Assert.True(painter.Moving(Frame(new(NthSpring.At(1), 4.9)), 5)); // just done: the label cross-fades
    }
}
