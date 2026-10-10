using Avalonia;
using Marrow.Testing;
using Marrow.UI.Port.Nth;
using SkiaSharp;

namespace Marrow.UI.Tests.Nth;

// OD-0136 amendment 2 (ED-NEW-nth-motion): each primitive's painter drawn off screen at fixed clock times, both themes, and
// read back against the spec tokens (nth-motion-review.html :root and [data-theme=dark]); plus no allocation per frame once warm.
public sealed class NthPainterTests
{
    private const int Scale = 4;
    private static readonly ManualTimeProvider Time = new();

    private static NthFrame Frame(double w, double h, bool dark, object? state, bool calm = false) =>
        new(new Size(w, h), dark, calm, Time, 0, state);

    private static SKBitmap Render(NthSkiaPainter painter, NthFrame frame, double now)
    {
        var bitmap = new SKBitmap(new SKImageInfo((int)(frame.Size.Width * Scale), (int)(frame.Size.Height * Scale), SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(Scale);
        painter.Draw(canvas, frame, now);
        canvas.Flush();
        return bitmap;
    }

    private static void Near(SKColor expected, SKColor actual, int tolerance = 3)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance
            && Math.Abs(expected.Blue - actual.Blue) <= tolerance, $"expected {expected}, got {actual}");
    }

    private static int Count(SKBitmap bitmap, Func<SKColor, bool> match)
    {
        var n = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (match(bitmap.GetPixel(x, y))) n++;
        return n;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_spinner_draws_its_track_and_its_line_in_the_theme_tokens(bool dark)
    {
        using var painter = new NthSpinnerPainter();
        var palette = NthPalette.For(dark);
        Assert.Equal(dark ? new SKColor(0x2f, 0x34, 0x3e) : new SKColor(0xd9, 0xdc, 0xe2), palette.Track);
        Assert.Equal(dark ? new SKColor(0xd3, 0xd8, 0xe0) : new SKColor(0x34, 0x3a, 0x47), palette.Mark);
        // 0.5 s in: fully faded in, a line of about a third of the tile
        using var bitmap = Render(painter, Frame(16, 16, dark, NthSpinnerInk.Mark), .5);
        Assert.True(Count(bitmap, c => c.Alpha == 255 && Math.Abs(c.Red - palette.Mark.Red) <= 2 && Math.Abs(c.Blue - palette.Mark.Blue) <= 2) > 40, "the line");
        Assert.True(Count(bitmap, c => c.Alpha == 255 && Math.Abs(c.Red - palette.Track.Red) <= 2 && Math.Abs(c.Blue - palette.Track.Blue) <= 2) > 40, "the track");
        Assert.Equal(0, bitmap.GetPixel(8 * Scale, 8 * Scale).Alpha); // a hollow tile
        // the top centre of the tile (12 o'clock): the track's ring passes through y = 8 - half
        var half = NthMotion.TileHalf(16);
        Assert.Equal(255, bitmap.GetPixel(8 * Scale, (int)((8 - half) * Scale)).Alpha);
    }

    [Fact]
    public void The_spinner_starts_invisible_and_fades_in_over_three_tenths_of_a_second()
    {
        using var painter = new NthSpinnerPainter();
        using var first = Render(painter, Frame(16, 16, false, NthSpinnerInk.Mark), 0);
        Assert.Equal(0, Count(first, c => c.Alpha > 0));
        using var later = Render(painter, Frame(16, 16, false, NthSpinnerInk.Mark), .3);
        Assert.True(Count(later, c => c.Alpha > 0) > 100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_known_bar_draws_fill_over_track_and_clips_to_its_capsule(bool dark)
    {
        using var painter = new NthBarPainter();
        var palette = NthPalette.For(dark);
        // 2.2 s: the light rests (it travels the first 72 % of 2.6 s)
        using var bitmap = Render(painter, Frame(200, 4, dark, new NthBarState(NthSpring.At(.5), false)), 2.2);
        var mid = bitmap.GetPixel(150 * Scale, 2 * Scale);
        Near(palette.Track, mid);
        Assert.Equal(255, mid.Alpha);
        // the fill at its vertical middle: between --ftop and --fbot
        var fill = bitmap.GetPixel(50 * Scale, 2 * Scale);
        Assert.Equal(255, fill.Alpha);
        Assert.InRange(fill.Red, Math.Min(palette.FillTop.Red, palette.FillBottom.Red), Math.Max(palette.FillTop.Red, palette.FillBottom.Red));
        Assert.NotEqual(palette.Track.Red, fill.Red);
        // the capsule's round left end: its corner pixel stays clear
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void The_unknown_bar_calm_stands_in_the_middle_third()
    {
        using var painter = new NthBarPainter();
        var palette = NthPalette.Light;
        using var bitmap = Render(painter, Frame(200, 4, false, new NthBarState(NthSpring.At(0), true), calm: true), 0);
        Assert.NotEqual(palette.Track.Red, bitmap.GetPixel(100 * Scale, 2 * Scale).Red); // segment 70..130
        Near(palette.Track, bitmap.GetPixel(30 * Scale, 2 * Scale));
        Near(palette.Track, bitmap.GetPixel(170 * Scale, 2 * Scale));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_ring_done_ends_on_a_strong_disc_with_an_on_colour_tick(bool dark)
    {
        using var painter = new NthRingPainter();
        var palette = NthPalette.For(dark);
        using var bitmap = Render(painter, Frame(18, 18, dark, new NthRingState(NthSpring.At(1), 0, false)), 1);
        // the disc off the tick (lower right quadrant, away from the tick's strokes)
        Near(palette.Strong, bitmap.GetPixel((int)(9 * Scale), (int)(14.5 * Scale)));
        // the tick's middle vertex (-0.13, 0.31) of r
        var r = NthMotion.DiscRadius(18);
        Near(palette.On, bitmap.GetPixel((int)((9 - .13 * r) * Scale), (int)((9 + .31 * r) * Scale)), 12);
    }

    [Fact]
    public void A_ring_in_progress_draws_its_arc_clockwise_from_twelve_o_clock()
    {
        using var painter = new NthRingPainter();
        var palette = NthPalette.Light;
        using var bitmap = Render(painter, Frame(18, 18, false, new NthRingState(NthSpring.At(.25), null, false)), 1);
        var radius = NthMotion.RingRadius(18);
        // 1:30 (45 degrees clockwise) lies on the arc, 7:30 on the track
        var at = (int)((9 + radius * Math.Sin(Math.PI / 4)) * Scale);
        var up = (int)((9 - radius * Math.Cos(Math.PI / 4)) * Scale);
        Near(palette.Mark, bitmap.GetPixel(at, up));
        var back = (int)((9 - radius * Math.Sin(Math.PI / 4)) * Scale);
        var down = (int)((9 + radius * Math.Cos(Math.PI / 4)) * Scale);
        Near(palette.Track, bitmap.GetPixel(back, down));
    }

    [Fact]
    public void The_status_slot_waits_with_a_hollow_ring_and_ends_on_the_row_tick()
    {
        using var painter = new NthStatusPainter();
        var palette = NthPalette.Light;
        using var waiting = Render(painter, Frame(16, 16, false, new NthStatusState(NthStatus.Waiting, NthStatus.None, double.NegativeInfinity, double.NegativeInfinity, NthDoneMark.Tick, 14)), 1);
        Assert.Equal(0, waiting.GetPixel(8 * Scale, 8 * Scale).Alpha);
        Near(palette.Text3, waiting.GetPixel(8 * Scale, (int)((8 - 3.25) * Scale)));
        using var done = Render(painter, Frame(16, 16, false, new NthStatusState(NthStatus.Done, NthStatus.Working, 0, 0, NthDoneMark.Tick, 14)), 1);
        // the tick's corner (5.6, 9.8) in the 14 px box centred in 16
        Near(palette.Text2, done.GetPixel((int)((1 + 5.6) * Scale), (int)((1 + 9.8) * Scale - 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_send_face_is_a_graphite_or_silver_disc_with_an_on_colour_arrow(bool dark)
    {
        using var painter = new NthSendPainter();
        var palette = NthPalette.For(dark);
        using var bitmap = Render(painter, Frame(NthSendFace.Size, NthSendFace.Size, dark, new NthSendState(false, double.NegativeInfinity, false, double.NegativeInfinity, 1)), 5);
        // the disc between its arrow and its edge: the --stop to --sbot gradient
        var side = bitmap.GetPixel((int)(8 * Scale), 16 * Scale);
        Assert.Equal(255, side.Alpha);
        Assert.InRange(side.Red, Math.Min(palette.Stop.Red, palette.StopBottom.Red) - 2, Math.Max(palette.Stop.Red, palette.StopBottom.Red) + 2);
        // the arrow's shaft (x = 8 of the page's 16 box, scaled about the centre): the --on colour, and its tip and tail make
        // the arrow ArrowHeight tall (tip y 3.5, tail y 13 of the box, plus the round caps)
        Near(palette.On, bitmap.GetPixel(16 * Scale, (int)(18 * Scale)), 10);
        var k = NthSendFace.ArrowScale;
        Near(palette.On, bitmap.GetPixel(16 * Scale, (int)((16 + (3.5 - 8) * k + .3) * Scale)), 40);
        Near(palette.On, bitmap.GetPixel(16 * Scale, (int)((16 + (13 - 8) * k - .3) * Scale)), 40);
        Assert.Equal(16, (13 - 3.5) * k + NthSendFace.ArrowStroke, 9);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void A_running_send_face_shows_the_stop_square_in_place_of_the_arrow()
    {
        using var painter = new NthSendPainter();
        var palette = NthPalette.Light;
        using var bitmap = Render(painter, Frame(32, 32, false, new NthSendState(true, 0, false, double.NegativeInfinity, 1)), .62);
        // the square: StopSize wide, centred; its middle and near its corner are --on, the arrow's tip above it is gone
        var half = NthSendFace.StopSize / 2;
        Near(palette.On, bitmap.GetPixel(16 * Scale, 16 * Scale), 6);
        Near(palette.On, bitmap.GetPixel((int)((16 + half - 1.5) * Scale), (int)((16 + half - 1.5) * Scale)), 10);
        Assert.True(Math.Abs(bitmap.GetPixel((int)((16 + half + 1) * Scale), 16 * Scale).Red - palette.On.Red) > 40, "the square ends at StopSize");
        var tip = bitmap.GetPixel(16 * Scale, (int)((16 - half - 1.5) * Scale));
        Assert.True(Math.Abs(tip.Red - palette.On.Red) > 40, $"arrow tip gone: {tip}");
    }

    public static TheoryData<string> Painters() => ["spinner", "bar", "indeterminate", "ring", "status", "list", "diff", "plate", "send", "tube", "link", "glyph"];

    [Theory]
    [MemberData(nameof(Painters))]
    public void Painters_allocate_nothing_per_frame_once_warm(string name)
    {
        var (painter, frame) = name switch
        {
            "spinner" => ((NthSkiaPainter)new NthSpinnerPainter(), Frame(16, 16, false, NthSpinnerInk.Mark)),
            "bar" => (new NthBarPainter(), Frame(200, 4, false, new NthBarState(new NthSpring(0, 0, .6, 0), false))),
            "indeterminate" => (new NthBarPainter(), Frame(200, 3, true, new NthBarState(NthSpring.At(0), true))),
            "ring" => (new NthRingPainter(), Frame(18, 18, false, new NthRingState(new NthSpring(0, 0, 1, 0), .5, false))),
            "status" => (new NthStatusPainter(), Frame(16, 16, false, new NthStatusState(NthStatus.Done, NthStatus.Working, .5, 0, NthDoneMark.Tick, 14))),
            "list" => (new NthSkeletonPainter(), Frame(300, 200, false, new NthSkeletonState(NthSkeletonKind.List, 5, null, null))),
            "diff" => (new NthSkeletonPainter(), Frame(300, 160, true, new NthSkeletonState(NthSkeletonKind.Diff, 8, null, null))),
            "plate" => (new NthSkeletonPainter(), Frame(80, 80, false, new NthSkeletonState(NthSkeletonKind.Plate, 1, null, null))),
            "send" => (new NthSendPainter(), Frame(32, 32, false, new NthSendState(true, 0, false, double.NegativeInfinity, 1))),
            "tube" => (new NthTubePainter(), Frame(168, 12, false, new NthTubeState(null, null))),
            "link" => (new NthLinkPainter(), Frame(120, 16, false, new NthLinkState(null))),
            _ => (new NthThinkingGlyphPainter(), Frame(14, 14, false, null)),
        };
        using (painter)
        {
            using var surface = SKSurface.Create(new SKImageInfo((int)frame.Size.Width * 2, (int)frame.Size.Height * 2));
            var canvas = surface.Canvas;
            for (var i = 0; i < 120; i++) { canvas.Clear(); painter.Draw(canvas, frame, i / 60.0); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 120; i < 240; i++) { canvas.Clear(); painter.Draw(canvas, frame, i / 60.0); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }

    [Fact]
    public void Painters_stop_asking_for_frames_once_their_motion_has_ended()
    {
        using var ring = new NthRingPainter();
        var done = Frame(18, 18, false, new NthRingState(NthSpring.At(1), 0, false));
        Assert.True(ring.Moving(done, .2));
        Assert.False(ring.Moving(done, .5));
        using var status = new NthStatusPainter();
        var tick = Frame(16, 16, false, new NthStatusState(NthStatus.Done, NthStatus.Working, 0, 0, NthDoneMark.Tick, 14));
        Assert.True(status.Moving(tick, .3));
        Assert.False(status.Moving(tick, .6));
        using var bar = new NthBarPainter();
        Assert.False(bar.Moving(Frame(200, 4, false, new NthBarState(NthSpring.At(1), false)), 10));
        Assert.True(bar.Moving(Frame(200, 4, false, new NthBarState(NthSpring.At(0), true)), 10));
        using var link = new NthLinkPainter();
        Assert.False(link.Moving(Frame(120, 16, false, new NthLinkState(null), calm: true), 1));
        Assert.False(link.Moving(Frame(120, 16, false, new NthLinkState(0)), 1));
    }
}
