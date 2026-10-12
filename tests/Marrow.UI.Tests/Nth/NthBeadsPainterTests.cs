using Marrow.UI.Port.Nth.Beads;
using SkiaSharp;
using Xunit;
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;

namespace Marrow.UI.Tests.Nth;

// Calculi WU3 (ED-NEW-calculi-wu3-1; DESIGN.md sections 9 and 10): the beads' painter drawn off screen and read back. Tokens in
// dark, light and high contrast; the merge keeps its brightest silver and never dims; the merge happens in the beads' own layer, so
// a white and a black backdrop give the same bead pixels; at 100% Pedes reads as three beads at 12 to 18 DIP; the neck ramp
// changes at most 20 pixels per 0.002 step; and over whole stress sessions nothing is drawn outside the envelope.
public sealed class NthBeadsPainterTests
{
    private static NthBeadsSample Beads(params (double X, double Y, double S)[] b)
    {
        var s = new NthBeadsSample { Present = true, Glint = -1 };
        s.B0 = new(b[0].X, b[0].Y, b[0].S, 1);
        s.B1 = b.Length > 1 ? new(b[1].X, b[1].Y, b[1].S, 1) : new(0, 0, 0, 1);
        s.B2 = b.Length > 2 ? new(b[2].X, b[2].Y, b[2].S, 1) : new(0, 0, 0, 1);
        return s;
    }

    private static NthBeadsSample Hub(NthBeadsHub hub) =>
        Beads((HubPoint(hub, 0).X, HubPoint(hub, 0).Y, 1), (HubPoint(hub, 1).X, HubPoint(hub, 1).Y, 1), (HubPoint(hub, 2).X, HubPoint(hub, 2).Y, 1));

    /// <summary>A bitmap of the envelope at <paramref name="size"/> DIP drawn at <paramref name="scale"/> device px per DIP, with
    /// <paramref name="pad"/> device px of margin on every side.</summary>
    private static SKBitmap Render(NthBeadsPainter painter, in NthBeadsSample s, double size, bool dark, SKColor? contrast = null,
        double scale = 1, int pad = 0, SKColor? backdrop = null)
    {
        var env = NthBeadsPainter.EnvelopeSize(size);
        var bitmap = new SKBitmap(new SKImageInfo((int)Math.Ceiling(env.Width * scale) + 2 * pad, (int)Math.Ceiling(env.Height * scale) + 2 * pad,
            SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(backdrop ?? SKColors.Transparent);
        canvas.Translate(pad, pad);
        canvas.Scale((float)scale);
        painter.Paint(canvas, s, size, dark, contrast);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>Device pixel of an n-unit point.</summary>
    private static (int X, int Y) Px(double x, double y, double size, double scale = 1, int pad = 0)
    {
        var u = NthBeadsPainter.Unit(size) * scale;
        return ((int)Math.Floor(pad + (x - EnvelopeX0) * u), (int)Math.Floor(pad + (y - EnvelopeY0) * u));
    }

    private static void Near(SKColor expected, SKColor actual, int tolerance, string what) =>
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance
            && Math.Abs(expected.Blue - actual.Blue) <= tolerance, $"{what}: expected {expected}, got {actual}");

    private static double Luma(SKColor c) => .2126 * c.Red + .7152 * c.Green + .0722 * c.Blue;

    /// <summary>The peak silver of the fully covered pixels: the brightest in dark, the deepest in light.</summary>
    private static double Peak(SKBitmap bmp, bool dark)
    {
        var p = dark ? 0.0 : 255.0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha == 255) p = dark ? Math.Max(p, Luma(c)) : Math.Min(p, Luma(c));
            }
        return p;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    public void The_control_is_the_envelope_and_the_small_size_rule_ramps_from_18_to_28_DIP()
    {
        var env = NthBeadsPainter.EnvelopeSize(18);
        Assert.Equal(18.9, env.Width, 1);
        Assert.Equal(22.1, env.Height, 1);
        Assert.Equal(.8, NthBeadsPainter.RadiusFactor(12), 12);
        Assert.Equal(.8, NthBeadsPainter.RadiusFactor(18), 12);
        Assert.Equal(.9, NthBeadsPainter.RadiusFactor(23), 12);
        Assert.Equal(1, NthBeadsPainter.RadiusFactor(28), 12);
        Assert.Equal(1, NthBeadsPainter.RadiusFactor(64), 12);
        Assert.True(NthBeadsPainter.IsFlat(12, false) && NthBeadsPainter.IsFlat(13.9, false) && !NthBeadsPainter.IsFlat(14, false));
        Assert.True(NthBeadsPainter.IsFlat(32, true));
        using var painter = new NthBeadsPainter();
        // the cut path is the engine's own wire: its measured length within Skia's arc flattening, and every hub point exact
        Assert.True(Math.Abs(WireLength - painter.MeasuredWireLength) < 2e-3, $"measured {painter.MeasuredWireLength}");
        Assert.Equal(painter.MeasuredWireLength, painter.Along(WireLength), 5);
        Assert.Equal(SLeft, painter.Along(SLeft), 9);
        Assert.Equal((painter.MeasuredWireLength) / 2, painter.Along(SCrown), 5);
        Assert.Equal(painter.MeasuredWireLength - Foot, painter.Along(SRight), 5);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    [InlineData(true)]
    [InlineData(false)]
    public void A_bead_is_the_logo_beads_radial_silver_in_the_theme_tokens(bool dark)
    {
        using var painter = new NthBeadsPainter();
        var ink = NthBeadsInk.For(dark);
        Assert.Equal(dark ? new SKColor(0xff, 0xff, 0xff) : new SKColor(0x7d, 0x85, 0x96), ink.Hi);
        Assert.Equal(dark ? new SKColor(0xd6, 0xda, 0xe2) : new SKColor(0x3a, 0x40, 0x4d), ink.Mid);
        Assert.Equal(dark ? new SKColor(0x9a, 0xa3, 0xb5) : new SKColor(0x1b, 0x1f, 0x27), ink.Lo);
        Assert.Equal(dark ? new SKColor(0xc4, 0xca, 0xd5) : new SKColor(0x35, 0x3b, 0x48), ink.Neck);
        Assert.Equal(dark ? new SKColor(0xd3, 0xd8, 0xe0) : new SKColor(0x34, 0x3a, 0x47), ink.Flat);
        const double size = 150;
        var r = BeadRadius * NthBeadsPainter.RadiusFactor(size);
        using var bmp = Render(painter, Beads((0, .3, 1)), size, dark);
        // the gradient's centre (x - 0.28r, y - 0.40r) is the hi stop; 0.8r from it (the 0.5 stop of a 1.6r gradient) is the mid
        var (hx, hy) = Px(-.28 * r, .3 - .4 * r, size);
        Near(ink.Hi, bmp.GetPixel(hx, hy), 4, "hi");
        var len = Math.Sqrt(.28 * .28 + .4 * .4);
        var (mx, my) = Px(-.28 * r + .28 / len * .8 * r, .3 - .4 * r + .4 / len * .8 * r, size);
        Near(ink.Mid, bmp.GetPixel(mx, my), 6, "mid");
        Assert.Equal(255, bmp.GetPixel(mx, my).Alpha);
        var (ox, oy) = Px(r * 1.1, .3, size);
        Assert.Equal(0, bmp.GetPixel(ox, oy).Alpha);
        // under 14 DIP: one flat fill in the Mark colour (drawn at 8 px per DIP to read it)
        using var small = Render(painter, Beads((0, .3, 1)), 12, dark, scale: 8);
        var (fx, fy) = Px(0, .3, 12, 8);
        Near(ink.Flat, small.GetPixel(fx, fy), 1, "flat");
        var (gx, gy) = Px(-.28 * BeadRadius * .8, .3 - .4 * BeadRadius * .8, 12, 8);
        Near(ink.Flat, small.GetPixel(gx, gy), 1, "flat has no gradient");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    public void High_contrast_is_one_flat_system_colour_with_no_gradient_and_no_glint()
    {
        using var painter = new NthBeadsPainter();
        var text = new SKColor(0xff, 0xff, 0x00);
        var s = Beads((-.2, .3, 1), (.2, .3, 1), (1, Foot, 1));
        s.Glint = .5;
        s.StrokeCount = 1; s.Stroke0From = 0; s.Stroke0To = SLeft;
        foreach (var dark in new[] { true, false })
        {
            using var bmp = Render(painter, s, 64, dark, text);
            var opaque = 0;
            for (var y = 0; y < bmp.Height; y++)
                for (var x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.Alpha == 0) continue;
                    Assert.True(c.Red == text.Red && c.Green == text.Green && c.Blue == text.Blue, $"({x},{y}) is {c}");
                    if (c.Alpha == 255) opaque++;
                }
            Assert.True(opaque > 400, $"{opaque} opaque pixels");
        }
    }

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    [InlineData(true)]
    [InlineData(false)]
    public void A_merge_never_dims_the_silver_and_its_peak_stays_constant(bool dark)
    {
        using var painter = new NthBeadsPainter();
        const double size = 150;
        var r = BeadRadius * NthBeadsPainter.RadiusFactor(size);
        // bead A stays put while bead B glides into it, from apart to one: the glyph's peak silver (dark: its brightest pixel, light:
        // its deepest) is A's own the whole way, never dimmed or washed by the merge and never past either bead's own
        using var single = Render(painter, Beads((0, .3, 1)), size, dark);
        var peakA = Peak(single, dark);
        for (var d = 2.4 * r; d >= 0; d -= .1 * r)
        {
            using var alone = Render(painter, Beads((d, .3, 1)), size, dark);
            using var pair = Render(painter, Beads((0, .3, 1), (d, .3, 1)), size, dark);
            double p = Peak(pair, dark), pb = Peak(alone, dark);
            if (dark) Assert.True(p >= peakA - 1 && p <= Math.Max(peakA, pb) + 1, $"peak {p:F1} at d {d / r:F1}r, A {peakA:F1}, B {pb:F1}");
            else Assert.True(p <= peakA + 1 && p >= Math.Min(peakA, pb) - 1, $"peak {p:F1} at d {d / r:F1}r, A {peakA:F1}, B {pb:F1}");
        }
        // inside the overlap every pixel keeps the brighter (dark: lighten) or the darker (light: darken) of the two beads' silver
        using var a = Render(painter, Beads((-.2, .3, 1)), size, dark);
        using var b = Render(painter, Beads((.2, .3, 1)), size, dark);
        using var m = Render(painter, Beads((-.2, .3, 1), (.2, .3, 1)), size, dark);
        var checkedPx = 0;
        for (var y = 0; y < m.Height; y++)
            for (var x = 0; x < m.Width; x++)
            {
                SKColor ca = a.GetPixel(x, y), cb = b.GetPixel(x, y), cm = m.GetPixel(x, y);
                if (ca.Alpha != 255 || cb.Alpha != 255) continue;
                checkedPx++;
                Assert.Equal(255, cm.Alpha);
                if (dark) Assert.True(cm.Red >= Math.Max(ca.Red, cb.Red) - 2 && cm.Green >= Math.Max(ca.Green, cb.Green) - 2 && cm.Blue >= Math.Max(ca.Blue, cb.Blue) - 2, $"dimmed at ({x},{y}): {cm} vs {ca} {cb}");
                else Assert.True(cm.Red <= Math.Min(ca.Red, cb.Red) + 2 && cm.Green <= Math.Min(ca.Green, cb.Green) + 2 && cm.Blue <= Math.Min(ca.Blue, cb.Blue) + 2, $"washed at ({x},{y}): {cm} vs {ca} {cb}");
            }
        Assert.True(checkedPx > 120, $"{checkedPx} overlap pixels");
    }

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    [InlineData(true)]
    [InlineData(false)]
    public void A_merged_pair_over_white_and_over_black_gives_identical_bead_pixels(bool dark)
    {
        using var painter = new NthBeadsPainter();
        const double size = 150;
        var pair = Beads((-.2, .3, 1), (.2, .3, 1), (-.2, .3, 1));
        using var white = Render(painter, pair, size, dark, backdrop: SKColors.White);
        using var black = Render(painter, pair, size, dark, backdrop: SKColors.Black);
        using var clear = Render(painter, pair, size, dark);
        var worst = 0;
        foreach (var x in new[] { -.2, 0, .2 })
        {
            var (px, py) = Px(x, .3, size);
            SKColor w = white.GetPixel(px, py), k = black.GetPixel(px, py);
            worst = Math.Max(worst, Math.Max(Math.Abs(w.Red - k.Red), Math.Max(Math.Abs(w.Green - k.Green), Math.Abs(w.Blue - k.Blue))));
        }
        Assert.True(worst <= 2, $"the three sample points differ by {worst}");
        // and every pixel the beads cover fully (bead, overlap and neck) is the same over both backdrops
        var full = 0;
        for (var y = 0; y < clear.Height; y++)
            for (var x = 0; x < clear.Width; x++)
            {
                if (clear.GetPixel(x, y).Alpha != 255) continue;
                full++;
                SKColor w = white.GetPixel(x, y), k = black.GetPixel(x, y);
                Assert.True(Math.Abs(w.Red - k.Red) <= 2 && Math.Abs(w.Green - k.Green) <= 2 && Math.Abs(w.Blue - k.Blue) <= 2, $"({x},{y}) {w} vs {k}");
            }
        Assert.True(full > 900, $"{full} covered pixels");
        Assert.True(painter.Layers > 0 && painter.Necks > 0, "the pair merged in the layer with a neck");
    }

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(18)]
    public void At_100_percent_Pedes_reads_as_three_beads_with_a_clear_pixel_between_them(double size)
    {
        using var painter = new NthBeadsPainter();
        foreach (var dark in new[] { true, false })
        {
            const int pad = 3;
            using var bmp = Render(painter, Hub(NthBeadsHub.Pedes), size, dark, pad: pad);
            var row = Px(0, Foot, size, 1, pad).Y;
            // the canvas's own count: a bead is a run of alpha >= 128, a clear pixel is alpha < 96
            int blobs = 0, run = 0, minGap = 99;
            var inside = false;
            for (var x = 0; x < bmp.Width; x++)
            {
                var a = bmp.GetPixel(x, row).Alpha;
                if (a >= 128) { if (!inside) { blobs++; if (blobs > 1) minGap = Math.Min(minGap, run); } inside = true; run = 0; }
                else { if (inside) run = 0; inside = false; if (a < 96) run++; }
            }
            Assert.True(blobs == 3, $"{size} DIP ({(dark ? "dark" : "light")}): {blobs} beads");
            Assert.True(minGap >= 1, $"{size} DIP: gap {minGap} px");
        }
    }

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    [InlineData(true)]
    [InlineData(false)]
    public void The_neck_ramp_changes_at_most_20_pixels_per_0_002_step_as_two_beads_sizes_part(bool dark)
    {
        using var painter = new NthBeadsPainter();
        const double size = 150;
        var bg = Marrow.UI.Port.Nth.NthPalette.For(dark).Raise;
        int[]? prev = null;
        var worst = 0;
        var necksAt0 = -1;
        for (var i = 0; i <= 200; i++)
        {
            var ds = i * .002;
            var before = painter.Necks;
            using var bmp = Render(painter, Beads((0, .3, 1), (BeadRadius * 1.72, .3, 1 - ds), (0, .3, 1)), size, dark, backdrop: bg);
            if (i == 0) necksAt0 = painter.Necks - before;
            var img = new int[bmp.Width * bmp.Height];
            for (var y = 0; y < bmp.Height; y++) for (var x = 0; x < bmp.Width; x++) { var c = bmp.GetPixel(x, y); img[y * bmp.Width + x] = c.Red << 16 | c.Green << 8 | c.Blue; }
            if (prev is not null)
            {
                var n = 0;
                for (var p = 0; p < img.Length; p++)
                {
                    int a = img[p], b = prev[p];
                    if (Math.Max(Math.Abs((a >> 16) - (b >> 16)), Math.Max(Math.Abs((a >> 8 & 255) - (b >> 8 & 255)), Math.Abs((a & 255) - (b & 255)))) > 24) n++;
                }
                worst = Math.Max(worst, n);
            }
            prev = img;
        }
        Assert.True(necksAt0 > 0, "touching equal beads are joined by a neck");
        Assert.True(worst <= 20, $"{worst} pixels changed visibly in one 0.002 step");
        Assert.Equal(0, NthBeadsPainter.NeckStrength(.24));
        Assert.Equal(1, NthBeadsPainter.NeckStrength(.12));
        Assert.Equal(.5, NthBeadsPainter.NeckStrength(.18), 12);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    public void Scriptio_cuts_its_stroke_from_the_wire_and_a_spent_stretch_is_a_bead_sized_dot()
    {
        using var painter = new NthBeadsPainter();
        const double size = 64;
        var s = Beads((0, 0, 1));
        s.Hidden = true;
        s.StrokeCount = 1; s.Stroke0From = 0; s.Stroke0To = SLeft; // the left stem only
        using var stem = Render(painter, s, size, true);
        var l = Px(-1, .8, size); var rr = Px(1, .8, size); var c = Px(0, -1, size);
        Assert.Equal(255, stem.GetPixel(l.X, l.Y).Alpha);
        Assert.Equal(0, stem.GetPixel(rr.X, rr.Y).Alpha);
        Assert.Equal(0, stem.GetPixel(c.X, c.Y).Alpha); // hidden: the bead is not drawn, and the cut stops at the shoulder
        // the three stretches spent to their hub points: three dots the size of a bead on the shoulders and the crown
        s.StrokeCount = 3;
        s.Stroke0From = s.Stroke0To = SLeft; s.Stroke1From = s.Stroke1To = SCrown; s.Stroke2From = s.Stroke2To = SRight;
        using var dots = Render(painter, s, size, true);
        using var beads = Render(painter, Hub(NthBeadsHub.Arcus), size, true);
        int dotPx = 0, beadPx = 0;
        for (var y = 0; y < dots.Height; y++) for (var x = 0; x < dots.Width; x++) { if (dots.GetPixel(x, y).Alpha >= 128) dotPx++; if (beads.GetPixel(x, y).Alpha >= 128) beadPx++; }
        Assert.True(Math.Abs(dotPx - beadPx) <= beadPx * .03, $"dots {dotPx} px, beads {beadPx} px");
        // the flow offset moves the stroke with the beads
        s.StrokeCount = 1; s.Stroke0From = 0; s.Stroke0To = SLeft; s.FlowX = .5;
        using var moved = Render(painter, s, size, true);
        var lm = Px(-.5, .8, size);
        Assert.Equal(255, moved.GetPixel(lm.X, lm.Y).Alpha);
        Assert.Equal(0, moved.GetPixel(l.X - 6, l.Y).Alpha);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    public void Nothing_is_drawn_outside_the_envelope_over_whole_stress_sessions()
    {
        using var painter = new NthBeadsPainter();
        var cases = new (double Size, double Scale)[] { (18, 1), (32, 2), (12, 1.5) };
        const int pad = 4;
        long frames = 0, outside = 0;
        string? first = null;
        for (var seed = 1; seed <= 16; seed++)
        {
            var session = NthBeadsStress.Run(seed, 40, .25);
            var (size, scale) = cases[seed % cases.Length];
            var dark = seed % 2 == 0;
            var env = NthBeadsPainter.EnvelopeSize(size);
            double ex0 = pad, ey0 = pad, ex1 = pad + env.Width * scale, ey1 = pad + env.Height * scale;
            using var bmp = new SKBitmap(new SKImageInfo((int)Math.Ceiling(env.Width * scale) + 2 * pad, (int)Math.Ceiling(env.Height * scale) + 2 * pad,
                SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bmp);
            for (var i = 0; i < session.Rec.Count; i += 4) // every 60 Hz frame
            {
                var sample = session.Rec[i];
                Assert.False(sample.Flight); // pair A has no flight
                canvas.Clear(SKColors.Transparent);
                canvas.Save();
                canvas.Translate(pad, pad);
                canvas.Scale((float)scale);
                painter.Paint(canvas, sample, size, dark);
                canvas.Restore();
                canvas.Flush();
                frames++;
                var px = bmp.GetPixelSpan();
                for (var y = 0; y < bmp.Height; y++)
                    for (var x = 0; x < bmp.Width; x++)
                    {
                        // only pixels that lie wholly outside the envelope's box
                        if (x + 1 > ex0 && x < ex1 && y + 1 > ey0 && y < ey1) continue;
                        if (px[(y * bmp.Width + x) * 4 + 3] == 0) continue;
                        outside++;
                        first ??= $"seed {seed}, sample {i} ({sample.MoveName}), pixel ({x},{y})";
                    }
            }
        }
        Assert.True(frames > 30000, $"{frames} frames");
        Assert.True(outside == 0, $"{outside} pixels outside the envelope, first at {first}");
        Assert.True(painter.Layers > 0 && painter.Necks > 0, "the sessions merged beads");
    }
}
