// PORT-ONLY: Calculi (ED-NEW-calculi-wu3-1; DESIGN.md sections 9, 10 and 11). Draws one NthBeadsSample in nth's material: the
// logo bead's radial silver, merged bead on bead (lighten in dark, darken in light) inside one bounded layer, Sato metaball necks
// in the neck silver with the smoothstep strength ramp, Scriptio's stroke cut from the wire path with SKPathMeasure, a flat fill
// under 14 DIP and in high contrast, and the small-size radius rule. Ported 1:1 from the canvas's drawGlyph and metaball
// (working-motion/index.html). Every paint, shader and path is made once: a frame allocates nothing managed.
using Avalonia;
using SkiaSharp;
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;

namespace Marrow.UI.Port.Nth.Beads;

/// <summary>What <see cref="NthBeadsPainter.Draw"/> reads from <see cref="NthFrame.State"/>: the n box in DIP and, in a
/// high-contrast theme, the system's text colour (SystemColors.WindowText, CSS CanvasText).</summary>
public readonly record struct NthBeadsLook(double Size, SKColor? Contrast = null);

/// <summary>One theme's bead tokens (section 10).</summary>
public sealed record NthBeadsInk(SKColor Hi, SKColor Mid, SKColor Lo, SKColor Neck, SKColor Flat, SKColor Glint)
{
    /// <summary>Near-black glass: the logo's exact stops.</summary>
    public static readonly NthBeadsInk Dark = new(new(0xff, 0xff, 0xff), new(0xd6, 0xda, 0xe2), new(0x9a, 0xa3, 0xb5),
        new(0xc4, 0xca, 0xd5), new(0xd3, 0xd8, 0xe0), NthPalette.Alpha(255, 255, 255, .95));

    /// <summary>Light glass: the Fill family, darkened for contrast.</summary>
    public static readonly NthBeadsInk Light = new(new(0x7d, 0x85, 0x96), new(0x3a, 0x40, 0x4d), new(0x1b, 0x1f, 0x27),
        new(0x35, 0x3b, 0x48), new(0x34, 0x3a, 0x47), NthPalette.Alpha(255, 255, 255, .92));

    public static NthBeadsInk For(bool dark) => dark ? Dark : Light;
}

/// <summary>The beads' painter. <see cref="Paint"/> draws a sample into the control's own box (the motion envelope, origin at its
/// top-left); <see cref="Draw"/> samples <see cref="Engine"/>, which the host advances on pacer slots only (section 11).</summary>
public sealed class NthBeadsPainter : NthSkiaPainter
{
    /// <summary>Under this n box size (DIP) the beads are a flat fill.</summary>
    public const double FlatBelow = 14;
    /// <summary>The bead layer's margin past the beads' own bounding box, in DIP.</summary>
    public const double LayerMargin = 2;

    private readonly SKPaint bead = new() { IsAntialias = true }, neck = new() { IsAntialias = true }, glint = new() { IsAntialias = true };
    private readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint dot = new() { IsAntialias = true }, layer = new();
    private readonly SKShader beadDark, beadLight, strokeDark, strokeLight, glintDark, glintLight;
    private readonly SKPath wire = new(), cut = new(), neckPath = new();
    private readonly SKPathMeasure measure;
    private readonly Circle[] circles = new Circle[3];
    private bool disposed;

    /// <summary>The engine <see cref="Draw"/> samples (owned by the host's handler, on the render thread).</summary>
    public NthBeadsEngine? Engine { get; set; }

    /// <summary>Paints that used the bead layer, and necks drawn (diagnostics for the tests).</summary>
    public int Layers { get; private set; }
    public int Necks { get; private set; }

    public NthBeadsPainter()
    {
        // The wire as NthMark builds the n's path (M left foot, up the stem, the arch, down the stem), in n units, so its arc length
        // is the engine's wire parameter: GetSegment(from, to) is exactly the stretch the engine names.
        wire.MoveTo(-1, (float)Foot); wire.LineTo(-1, 0);
        wire.ArcTo(new SKRect(-1, -1, 1, 1), 180, 180, false); // over the crown (an exact half circle; the radii form is ambiguous)
        wire.LineTo(1, (float)Foot);
        measure = new SKPathMeasure(wire, false, 256); // n units are big: measure to 1/256 of a unit, not to a whole one
        // One unit-radius radial per theme: centre (-0.28, -0.40), radius 1.6, stops 0 / 0.5 / 1 (NthMark's gBead, section 10).
        beadDark = Radial(NthBeadsInk.Dark); beadLight = Radial(NthBeadsInk.Light);
        strokeDark = Linear(NthBeadsInk.Dark); strokeLight = Linear(NthBeadsInk.Light);
        glintDark = Glint(NthBeadsInk.Dark); glintLight = Glint(NthBeadsInk.Light);
    }

    /// <summary>The wire path's measured length (<see cref="WireLength"/> to within Skia's arc flattening, 1.3e-3 units).</summary>
    public double MeasuredWireLength => measure.Length;

    /// <summary>The measured distance along the wire path of the engine's wire parameter <paramref name="s"/>: the stems are exact,
    /// the arch is scaled to its measured length, so the shoulders, the crown and the feet land exactly where the beads do.</summary>
    public double Along(double s)
    {
        var arch = measure.Length - 2 * Foot;
        if (s <= Foot) return s;
        if (s <= Foot + Math.PI) return Foot + (s - Foot) * arch / Math.PI;
        return s - Math.PI + arch;
    }

    /// <summary>The small-size rule (section 9): 80% of the radius at 18 DIP and below, 100% from 28 DIP.</summary>
    public static double RadiusFactor(double size) => NthBeadsMoves.Clamp(.8 + .2 * (size - 18) / 10, .8, 1);

    /// <summary>Flat fill under 14 DIP and in high contrast.</summary>
    public static bool IsFlat(double size, bool contrast) => size < FlatBelow || contrast;

    /// <summary>DIP per n unit for an n box of <paramref name="size"/> DIP.</summary>
    public static double Unit(double size) => size / Box;

    /// <summary>The control's box: the motion envelope at <paramref name="size"/> (18.9 x 22.1 DIP at 18).</summary>
    public static Size EnvelopeSize(double size) => new((EnvelopeX1 - EnvelopeX0) * Unit(size), (EnvelopeY1 - EnvelopeY0) * Unit(size));

    /// <summary>A neck's strength as two beads' scales part: 1 - smoothstep((|ds| - 0.12) / 0.12).</summary>
    public static double NeckStrength(double ds) => 1 - NthBeadsMoves.Smooth((Math.Abs(ds) - .12) / .12);

    public override bool Moving(NthFrame frame, double now) => Engine is { IsStill: false };

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (Engine is not { } engine || frame.State is not NthBeadsLook look) return;
        var sample = engine.Frame(now);
        Paint(canvas, sample, look.Size, frame.Dark, look.Contrast);
    }

    /// <summary>Draws <paramref name="s"/> with the n box at <paramref name="size"/> DIP into the envelope box at the canvas's
    /// origin. <paramref name="contrast"/> is the high-contrast text colour (flat, no gradient, no glint).</summary>
    public void Paint(SKCanvas canvas, in NthBeadsSample s, double size, bool dark, SKColor? contrast = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!s.Present || size <= 0) return;
        var hc = contrast.HasValue;
        var flat = IsFlat(size, hc);
        var ink = NthBeadsInk.For(dark);
        var flatColor = hc ? contrast!.Value : ink.Flat;
        double u = Unit(size), r0 = BeadRadius * RadiusFactor(size), fx = s.FlowX, fy = s.FlowY;

        var saved = canvas.Save();
        // n units from here on: the envelope's top-left is the control's origin, the flow offset moves every bead and the stroke
        canvas.Translate((float)((fx - EnvelopeX0) * u), (float)((fy - EnvelopeY0) * u));
        canvas.Scale((float)u);

        if (s.StrokeCount > 0) Stroke(canvas, s, r0, flat, flatColor, dark);

        var n = 0;
        if (!s.Hidden)
            for (var i = 0; i < 3; i++)
            {
                var b = s[i];
                if (b.S > .002) circles[n++] = new Circle(b.X, b.Y, r0 * b.S, b.S, b.O);
            }
        if (n > 0) Beads(canvas, n, u, flat, flatColor, ink, dark, s.Glint);
        canvas.RestoreToCount(saved);
    }

    private void Stroke(SKCanvas canvas, in NthBeadsSample s, double r0, bool flat, SKColor flatColor, bool dark)
    {
        stroke.StrokeWidth = (float)(2 * r0);
        if (flat) { stroke.Shader = null; stroke.Color = flatColor; dot.Shader = null; dot.Color = flatColor; }
        else { var g = dark ? strokeDark : strokeLight; stroke.Shader = g; stroke.Color = SKColors.White; dot.Shader = g; dot.Color = SKColors.White; }
        cut.Rewind();
        var any = false;
        for (var i = 0; i < s.StrokeCount && i < 3; i++)
        {
            var (a, b) = i switch { 0 => (s.Stroke0From, s.Stroke0To), 1 => (s.Stroke1From, s.Stroke1To), _ => (s.Stroke2From, s.Stroke2To) };
            if (b - a < 1e-3)
            {
                // a stretch shrunk to a point is the round cap alone: a bead-sized dot on the wire
                var p = Wire((a + b) / 2);
                canvas.DrawCircle((float)p.X, (float)p.Y, (float)r0, dot);
                continue;
            }
            any |= measure.GetSegment((float)Along(a), (float)Along(b), cut, true);
        }
        if (any) canvas.DrawPath(cut, stroke);
    }

    private void Beads(SKCanvas canvas, int n, double u, bool flat, SKColor flatColor, NthBeadsInk ink, bool dark, double g)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, alpha = 1;
        for (var i = 0; i < n; i++)
        {
            ref readonly var c = ref circles[i];
            x0 = Math.Min(x0, c.X - c.R); y0 = Math.Min(y0, c.Y - c.R); x1 = Math.Max(x1, c.X + c.R); y1 = Math.Max(y1, c.Y + c.R);
            alpha = Math.Min(alpha, c.O);
        }
        alpha = NthBeadsMoves.Clamp(alpha);
        if (alpha <= 0) return;

        // Do any two beads touch (within a device-independent pixel, so their edge pixels merge too) or join by a neck?
        var touch = false;
        var hasNeck = false;
        var pad = 1 / u;
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                ref readonly var a = ref circles[i];
                ref readonly var b = ref circles[j];
                var d = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (d < a.R + b.R + pad) touch = true;
                if (NeckStrength(a.S - b.S) > 0 && d <= (a.R + b.R) * 1.2 && d > Math.Abs(a.R - b.R)) hasNeck = true;
            }

        // One bounded layer over the beads' own box only when they merge (or fade, Option C): inside it they lighten or darken
        // bead on bead, never against the glass behind; it lands on the page with source-over at the beads' one opacity.
        var layered = touch || hasNeck || alpha < 1;
        var saved = canvas.Save();
        if (layered)
        {
            var m = LayerMargin / u;
            var box = new SKRect((float)(x0 - m), (float)(y0 - m), (float)(x1 + m), (float)(y1 + m));
            if (alpha < 1) { layer.Color = SKColors.White.WithAlpha((byte)Math.Round(alpha * 255)); canvas.SaveLayer(box, layer); }
            else canvas.SaveLayer(box, null);
            Layers++;
        }

        if (hasNeck)
        {
            neck.Color = flat ? flatColor : ink.Neck;
            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                {
                    ref readonly var a = ref circles[i];
                    ref readonly var b = ref circles[j];
                    var k = NeckStrength(a.S - b.S);
                    if (k <= 0 || !Metaball(a, b, k, neckPath)) continue;
                    canvas.DrawPath(neckPath, neck);
                    Necks++;
                }
        }

        if (flat) { bead.Shader = null; bead.Color = flatColor; bead.BlendMode = SKBlendMode.SrcOver; }
        else
        {
            bead.Shader = dark ? beadDark : beadLight; bead.Color = SKColors.White;
            bead.BlendMode = layered ? (dark ? SKBlendMode.Lighten : SKBlendMode.Darken) : SKBlendMode.SrcOver;
        }
        for (var i = 0; i < n; i++)
        {
            ref readonly var c = ref circles[i];
            var one = canvas.Save();
            canvas.Translate((float)c.X, (float)c.Y);
            canvas.Scale((float)c.R);
            canvas.DrawCircle(0, 0, 1, bead);
            canvas.RestoreToCount(one);
        }
        bead.BlendMode = SKBlendMode.SrcOver;
        if (layered) canvas.Restore();

        // The glint (Condensatio / Recessus, option C only): a soft light crossing the biggest bead, inside its disc.
        // Flat (small sizes, high contrast) has none.
        if (g > 0 && g < 1 && !flat)
        {
            var big = 0;
            for (var i = 1; i < n; i++) if (circles[i].R > circles[big].R) big = i;
            ref readonly var c = ref circles[big];
            double px = c.X + c.R * (-.75 + 1.5 * g) * .7, py = c.Y + c.R * (-.75 + 1.5 * g) * .55, gr = c.R * .42;
            glint.Shader = dark ? glintDark : glintLight;
            glint.Color = SKColors.White.WithAlpha((byte)Math.Round(alpha * 255));
            var one = canvas.Save();
            canvas.Translate((float)px, (float)py);
            canvas.Scale((float)gr);
            canvas.DrawCircle((float)((c.X - px) / gr), (float)((c.Y - py) / gr), (float)(c.R / gr), glint);
            canvas.RestoreToCount(one);
        }
        canvas.RestoreToCount(saved);
    }

    /// <summary>Sato's metaball neck between two beads (index.html metaball), into <paramref name="path"/> (rewound). False when
    /// the beads are too far apart, nested, or the neck has no strength left.</summary>
    private static bool Metaball(in Circle c1, in Circle c2, double k, SKPath path)
    {
        double r1 = c1.R, r2 = c2.R, dx = c2.X - c1.X, dy = c2.Y - c1.Y, d = Math.Sqrt(dx * dx + dy * dy), lim = (r1 + r2) * 1.2;
        if (r1 <= 0 || r2 <= 0 || d > lim || d <= Math.Abs(r1 - r2)) return false;
        var v = .5 * (1 - NthBeadsMoves.Smooth((d - (r1 + r2) * .85) / ((r1 + r2) * .35))) * k;
        if (v <= .001) return false;
        double u1 = 0, u2 = 0;
        if (d < r1 + r2)
        {
            u1 = Math.Acos(NthBeadsMoves.Clamp((r1 * r1 + d * d - r2 * r2) / (2 * r1 * d), -1, 1));
            u2 = Math.Acos(NthBeadsMoves.Clamp((r2 * r2 + d * d - r1 * r1) / (2 * r2 * d), -1, 1));
        }
        double ab = Math.Atan2(dy, dx), ms = Math.Acos(NthBeadsMoves.Clamp((r1 - r2) / d, -1, 1));
        double a1 = ab + u1 + (ms - u1) * v, a2 = ab - u1 - (ms - u1) * v;
        double a3 = ab + Math.PI - u2 - (Math.PI - u2 - ms) * v, a4 = ab - Math.PI + u2 + (Math.PI - u2 - ms) * v;
        double p1x = c1.X + r1 * Math.Cos(a1), p1y = c1.Y + r1 * Math.Sin(a1), p2x = c1.X + r1 * Math.Cos(a2), p2y = c1.Y + r1 * Math.Sin(a2);
        double p3x = c2.X + r2 * Math.Cos(a3), p3y = c2.Y + r2 * Math.Sin(a3), p4x = c2.X + r2 * Math.Cos(a4), p4y = c2.Y + r2 * Math.Sin(a4);
        var tr = r1 + r2;
        var d2 = Math.Min(v * 2.4, Math.Sqrt((p1x - p3x) * (p1x - p3x) + (p1y - p3y) * (p1y - p3y)) / tr) * Math.Min(1, d * 2 / tr);
        double h1 = r1 * d2, h2 = r2 * d2;
        path.Rewind();
        path.MoveTo((float)p1x, (float)p1y);
        path.CubicTo((float)(p1x + h1 * Math.Cos(a1 - Math.PI / 2)), (float)(p1y + h1 * Math.Sin(a1 - Math.PI / 2)),
            (float)(p3x + h2 * Math.Cos(a3 + Math.PI / 2)), (float)(p3y + h2 * Math.Sin(a3 + Math.PI / 2)), (float)p3x, (float)p3y);
        path.LineTo((float)p4x, (float)p4y);
        path.CubicTo((float)(p4x + h2 * Math.Cos(a4 - Math.PI / 2)), (float)(p4y + h2 * Math.Sin(a4 - Math.PI / 2)),
            (float)(p2x + h1 * Math.Cos(a2 + Math.PI / 2)), (float)(p2y + h1 * Math.Sin(a2 + Math.PI / 2)), (float)p2x, (float)p2y);
        path.Close();
        return true;
    }

    private readonly record struct Circle(double X, double Y, double R, double S, double O);

    private static SKShader Radial(NthBeadsInk ink) =>
        SKShader.CreateRadialGradient(new SKPoint(-.28f, -.40f), 1.6f, [ink.Hi, ink.Mid, ink.Lo], [0, .5f, 1], SKShaderTileMode.Clamp);

    // The stroke's silver runs down the glyph: from the crown's top edge (y -1.34) to the feet, stops 0 / 0.55 / 1.
    private static SKShader Linear(NthBeadsInk ink) =>
        SKShader.CreateLinearGradient(new SKPoint(0, -1.34f), new SKPoint(0, (float)Foot), [ink.Hi, ink.Mid, ink.Lo], [0, .55f, 1], SKShaderTileMode.Clamp);

    private static SKShader Glint(NthBeadsInk ink) =>
        SKShader.CreateRadialGradient(new SKPoint(0, 0), 1, [ink.Glint, new SKColor(255, 255, 255, 0)], [0, 1], SKShaderTileMode.Clamp);

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        bead.Shader = null; stroke.Shader = null; dot.Shader = null; glint.Shader = null;
        foreach (var shader in new[] { beadDark, beadLight, strokeDark, strokeLight, glintDark, glintLight }) shader.Dispose();
        measure.Dispose(); wire.Dispose(); cut.Dispose(); neckPath.Dispose();
        bead.Dispose(); neck.Dispose(); glint.Dispose(); stroke.Dispose(); dot.Dispose(); layer.Dispose();
        base.Dispose();
    }
}
