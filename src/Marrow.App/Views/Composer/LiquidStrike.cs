using SkiaSharp;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// DEVIATION OD-0073 (owner ruling, 2026-10-02, "My choice"; ED-248): fast mode's look is variant A2 "Strike" of the locked
// canvas E:\Droppy\work\design\fast-mode-strike-A2-locked.dc.html (sha256 3f8ad01a...7a4408; the board's key 'B', drawn
// by strike() and strikeAt()), over the model's own liquid at every effort, solo and paired. It replaces the source's
// speed streaks and bolts (EffortSlider.swift:1151-1201). Spec: E:\Droppy\work\design\strike\SPEC.md. This file is the
// canvas's code, function for function:
//   a strike every 2.4 s slot, its onset jittered by up to 0.5 s; each strike its own jagged channel (seed S*37+k) from
//   the knob to the cap end, with five branches; a stepped leader 140 ms ahead; two or three flickers of decaying
//   brightness; the liquid's inner flash at the knob; a glow-out over 1.8 s; a faint light pool always on at the knob; a
//   knob spark between strikes in about half the cycles; the channel's shimmer re-rolled 30 times a second.
// A frame is resolved where it is drawn, on the render thread, into a short list of drawing steps (StrikeOp), computed
// from a channel built once per strike and a jittered copy made once per 30th of a second, and painted into one layer
// screen-blended over the liquid (LiquidTrack), under the tube's gloss and rims. ED-248 review: no frame allocates; every
// buffer, the steps' list and the wash shaders are kept and reused (StrikeChannels, StrikePainter). Everything is a
// function of time, so the same moment always draws the same.

/// <summary>A strike's three colours: the halo and glow-out, the inner stroke and glow-out core, and the light pools.
/// The core is always white. The same on dark and light (only what the screen blend sits on differs). OD-0090 (the owner,
/// 2026-10-03: "ensure the lightning strikes contrast with each different model's colour palette and bubbles too"):
/// <paramref name="Ink"/> (0 to 1), how pale the liquid is (jewel-knob.html inkOf): the bubbles on a pale liquid take the
/// pale-liquid bubble look by it; 0 on every dark and mid liquid, where the locked look is untouched. (The owner rejected the
/// deep rim under the bolt the same page first drew; the bolt's own treatment for pale liquids is still being designed.)</summary>
public readonly record struct StrikeTint(SKColor Outer, SKColor Inner, SKColor Wash, double Ink = 0)
{
    public static StrikeTint Claude { get; } = new(new SKColor(255, 146, 100), new SKColor(255, 222, 204), new SKColor(255, 168, 126));
    public static StrikeTint DeepSeek { get; } = new(new SKColor(116, 142, 255), new SKColor(224, 231, 255), new SKColor(146, 168, 255));
    public static StrikeTint Gemini { get; } = new(new SKColor(190, 160, 255), new SKColor(242, 234, 255), new SKColor(206, 184, 246));

    /// <summary>The canvas's TINT table: Claude, DeepSeek and Gemini (Antigravity's colours). DEVIATION OD-0090 (the locked
    /// Jewel knob page, knob-variants.js strikeTint): every other brand's tint is derived from its own liquid (this settles
    /// ED-248's open question, where they took Claude's), so the lightning, the liquid and the knob wear one maker.</summary>
    public static StrikeTint For(EffortBrand brand)
    {
        var i = (int)brand;
        if (tints[i] is { } kept) return kept;
        return (tints[i] = Of(JewelLiquid.Of(brand))).Value;
    }

    private static readonly StrikeTint?[] tints = new StrikeTint?[Enum.GetValues<EffortBrand>().Length];

    /// <summary>
    /// knob-variants.js strikeTint(liquid): the locked board's three tints kept as they are; any other maker's from its
    /// stops: the halo at OKLCH (0.78, max(1.05 C, 0.02)), the inner stroke at (0.95, 0.25 C), the pools at (0.82, 0.8 C),
    /// all at the hue 0.8 of the way to the light stop.
    /// </summary>
    public static StrikeTint Of(JewelLiquid q)
    {
        var ink = InkOf(q);
        switch (q.Maker)
        {
            case "Claude": return Claude with { Ink = ink };
            case "DeepSeek": return DeepSeek with { Ink = ink };
            case "Gemini": return Gemini with { Ink = ink };
        }
        double h = q.HueAt(0.8), c = q.C;
        return new StrikeTint(JewelColor.HexOf(0.78, Math.Max(c * 1.05, 0.02), h), JewelColor.HexOf(0.95, c * 0.25, h), JewelColor.HexOf(0.82, c * 0.8, h), ink);
    }

    /// <summary>
    /// jewel-knob.html inkOf(liquid).ink: how much contrast help the liquid needs, from white's WCAG contrast against its
    /// light stop: none at 2.9:1 or more, full at 1.9:1 or less.
    /// </summary>
    public static double InkOf(JewelLiquid q)
    {
        static double Lin(byte v) { var c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        var lum = 0.2126 * Lin(q.Light.Red) + 0.7152 * Lin(q.Light.Green) + 0.0722 * Lin(q.Light.Blue);
        return Math.Min(1, Math.Max(0, (2.9 - 1.05 / (lum + 0.05)) / 1.0));
    }

    /// <summary>knob-variants.js mixTint: two tints mixed channel by channel, each rounded (a model change's 0.6 s crossfade).</summary>
    public static StrikeTint Mix(StrikeTint a, StrikeTint b, double u)
    {
        static byte C(byte x, byte y, double u) => (byte)Math.Floor(x + (y - x) * u + 0.5);
        static SKColor M(SKColor x, SKColor y, double u) => new(C(x.Red, y.Red, u), C(x.Green, y.Green, u), C(x.Blue, y.Blue, u));
        return new StrikeTint(M(a.Outer, b.Outer, u), M(a.Inner, b.Inner, u), M(a.Wash, b.Wash, u), a.Ink + (b.Ink - a.Ink) * u); // mixTint2's ink
    }
}

/// <summary>
/// Where a strike runs: the liquid's size (px), where the bolt leaves the knob <paramref name="Knob"/> (x, px: the engine's
/// g.ex; on the slider the page's exAt, 2 px before the knob's centre on a quarter pixel) and the knob's radius. The
/// channel runs from there to the cap end at 0.32 h; <see cref="K"/> scales every width and length.

/// </summary>
public readonly record struct StrikeGeometry(double Width, double Height, double Knob, double KnobRadius)
{
    /// <summary>The size factor: clamp(h / 40, 0.62, 1).</summary>
    public double K => Math.Max(0.62, Math.Min(1, Height / 40));

    public double Middle => Height / 2;

    /// <summary>The channel's far end (the cap end), 0.32 h.</summary>
    public double CapEnd => Height * 0.32;
}

/// <summary>A stroke, a light pool, or (OD-0078) a burst's glints: discs filled as one path.</summary>
public enum StrikeOpKind { Stroke, Wash, Disc }

/// <summary>
/// One drawing step: a stroke of a polyline (x, y pairs) at a width in a colour at an alpha (the canvas's rgba, rounded
/// to three decimals as its toFixed(3) does), with round joins and butt caps; or a wash, the canvas's radial light pool
/// (wash@1 at 0, @0.35 at 0.45, @0 at 1) filling an ellipse of radii Rx by Ry at (X, Y), at an alpha.
/// A value over a buffer the scene reuses (ED-248 review: no allocation per frame), so a step is valid until its
/// <see cref="StrikeChannels"/> resolves the next scene; <see cref="Points"/> copies the line out for keeping.
/// </summary>
public readonly struct StrikeOp
{
    private readonly double[]? line;
    private readonly int count;

    private StrikeOp(StrikeOpKind kind, double[]? line, int count, double width, SKColor color, double alpha, double x, double y, double rx, double ry)
    {
        Kind = kind; this.line = line; this.count = count; Width = width; Color = color; Alpha = alpha; X = x; Y = y; Rx = rx; Ry = ry;
    }

    public StrikeOpKind Kind { get; }

    /// <summary>The stroke's polyline, x, y pairs, read in place.</summary>
    public ReadOnlySpan<double> Line => line is null ? default : line.AsSpan(0, count);

    /// <summary>A copy of the stroke's polyline (for inspection; the frame path reads <see cref="Line"/>).</summary>
    public double[] Points => Line.ToArray();

    public double Width { get; }
    public SKColor Color { get; }
    public double Alpha { get; }
    public double X { get; }
    public double Y { get; }
    public double Rx { get; }
    public double Ry { get; }

    public static StrikeOp Stroke(double[] points, double width, SKColor color, double alpha) => Stroke(points, points.Length, width, color, alpha);

    /// <summary>A stroke of the first <paramref name="count"/> values (x, y pairs) of <paramref name="points"/>.</summary>
    public static StrikeOp Stroke(double[] points, int count, double width, SKColor color, double alpha) =>
        new(StrikeOpKind.Stroke, points, count, width, color, alpha, 0, 0, 0, 0);

    public static StrikeOp Wash(double x, double y, double rx, double ry, double alpha, SKColor color) => new(StrikeOpKind.Wash, null, 0, 0, color, alpha, x, y, rx, ry);

    public static StrikeOp Discs(double[] discs, SKColor color, double alpha) => new(StrikeOpKind.Disc, discs, discs.Length, 0, color, alpha, 0, 0, 0, 0);
    public static StrikeOp Discs(double[] discs, int count, SKColor color, double alpha) => new(StrikeOpKind.Disc, discs, count, 0, color, alpha, 0, 0, 0, 0);

}

/// <summary>The canvas's strike maths (strikeAt, hash, vn, fbm, the pulses, the leader's steps, the spark's timing).</summary>
public static class Strike
{
    /// <summary>The nominal period, s: strike k's slot starts at 2.4 k.</summary>
    public const double Period = 2.4;

    /// <summary>How long the leader runs ahead of its strike, s.</summary>
    public const double Lead = 0.14;

    /// <summary>How long after its onset a strike is drawn (its glow-out), s.</summary>
    public const double Window = 1.8;

    /// <summary>How many times a second the channel's shimmer re-rolls.</summary>
    public const double ShimmerRate = 30;

    // OD-0084, strike-port SPEC K-016, locked strike-engine.js:230.
    public static double Ss(double e0, double e1, double x)
    {
        var u = Math.Min(1, Math.Max(0, (x - e0) / (e1 - e0)));
        return u * u * (3 - 2 * u);
    }

    private static readonly double[] easeTable = BuildEaseTable();

    // K-017, strike-engine.js:1010-1026: previous-root Newton table, not an exact curve solver.
    private static double[] BuildEaseTable()
    {
        static double X(double t) => 3 * (1 - t) * (1 - t) * t * 0.35 + 3 * (1 - t) * t * t * 0.55 + t * t * t;
        static double Y(double t) => 3 * (1 - t) * (1 - t) * t * 0.1 + 3 * (1 - t) * t * t + t * t * t;
        static double Dx(double t) => 3 * (1 - t) * (1 - t) * 0.35 + 6 * (1 - t) * t * 0.2 + 3 * t * t * 0.45;
        var table = new double[1025];
        double t = 0;
        for (var i = 0; i <= 1024; i++)
        {
            var x = i / 1024d;
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var delta = X(t) - x;
                if (Math.Abs(delta) < 1e-9) break;
                t = Math.Min(1, Math.Max(0, t - delta / Dx(t)));
            }
            table[i] = Y(t);
        }
        return table;
    }

    public static double Ease(double x)
    {
        var v = Math.Min(1, Math.Max(0, x)) * 1024;
        var i = (int)Math.Floor(v);
        return i >= 1024 ? easeTable[1024] : easeTable[i] + (easeTable[i + 1] - easeTable[i]) * (v - i);
    }

    /// <summary>The canvas's hash(a, b), in [0, 1): Math.imul as unchecked 32-bit multiplies, >>> as unsigned shifts.</summary>
    public static double Hash(int a, int b)
    {
        unchecked
        {
            var x = ((uint)a ^ (((uint)b + 0x632be5abu) * 0x27d4eb2du)) * 0x85ebca6bu;
            x ^= x >> 13; x *= 0xc2b2ae35u; x ^= x >> 16;
            return x / 4294967296.0;
        }
    }

    /// <summary>The canvas's vn(x, s): smoothstepped value noise in [-1, 1].</summary>
    public static double Noise(double x, int s)
    {
        var i = Math.Floor(x); var f = x - i; var u = f * f * (3 - 2 * f);
        var n = unchecked((int)(long)i);
        return (Hash(n, s) * (1 - u) + Hash(unchecked(n + 1), s) * u) * 2 - 1;
    }

    /// <summary>The canvas's fbm(x, s): three octaves at 110, 36 and 12 px.</summary>
    public static double Fbm(double x, int s) => 0.55 * Noise(x / 110, s) + 0.3 * Noise(x / 36, unchecked(s + 11)) + 0.15 * Noise(x / 12, unchecked(s + 23));

    /// <summary>strikeAt(k): 2.4 k + 0.2 + 0.5 hash(k, S+5), s.</summary>
    public static double StrikeAt(int k, int seed) => k * Period + 0.2 + Hash(k, unchecked(seed + 5)) * 0.5;

    /// <summary>A strike flickers three times when hash(k, S+9) > 0.45 (about 55 %), else twice.</summary>
    public static bool Triple(int k, int seed) => Hash(k, unchecked(seed + 9)) > 0.45;

    /// <summary>The brightness <paramref name="a"/> s after the onset: decaying pulses at 0 (1, 50 ms), 75 ms (0.6, 40 ms)
    /// and, for a triple, 150 ms (0.85, 55 ms), at most 1.1.</summary>
    public static double Intensity(double a, bool triple)
    {
        static double Pulse(double a, double t0, double d, double m) => a >= t0 ? Math.Exp(-(a - t0) / d) * m : 0;
        return Math.Min(1.1, Pulse(a, 0, 0.05, 1) + Pulse(a, 0.075, 0.04, 0.6) + (triple ? Pulse(a, 0.15, 0.055, 0.85) : 0));
    }

    /// <summary>The glow-out, e^(-a / 0.5).</summary>
    public static double Ghost(double a) => Math.Exp(-a / 0.5);

    /// <summary>The leader's tip (a vertex index) <paramref name="a"/> s before the onset (-0.14 to 0): seven 20 ms steps
    /// out of the knob to 90 % of the channel.</summary>
    public static int LeaderTip(double a, int last) => Round(Math.Min(1, (Math.Floor((a + Lead) / Lead * 7) + 1) / 7) * last * 0.9);

    /// <summary>The always-on light pool's strength at the knob, 0.12 + 0.04 sin(2.6 t).</summary>
    public static double Ambient(double t) => 0.12 + 0.04 * Math.Sin(t * 2.6);

    /// <summary>When strike k's knob spark starts (1.1 to 1.5 s after the onset), or null in the cycles without one
    /// (hash(k, S+3) >= 0.5).</summary>
    public static double? SparkAt(int k, int seed) =>
        Hash(k, unchecked(seed + 3)) >= 0.5 ? null : StrikeAt(k, seed) + 1.1 + Hash(k, unchecked(seed + 2)) * 0.4;

    /// <summary>Reduce motion's one still frame: 35 ms into strike 1.</summary>
    public static double StillTime(int seed) => StrikeAt(1, seed) + 0.035;

    /// <summary>JavaScript's Math.round for the indices here (halves round up).</summary>
    internal static int Round(double v) => (int)Math.Floor(v + 0.5);

    /// <summary>The canvas's colour alpha as written, toFixed(3).</summary>
    internal static double Fixed3(double v) => Math.Floor(v * 1000 + 0.5) / 1000;

    /// <summary>The canvas's channel()'s vertex count from x0 to x1: n + 1 for n = max(2, ceil(|x1 - x0| / (5k + 2))).</summary>
    public static int ChannelVertices(StrikeGeometry g, double x0, double x1) => Math.Max(2, (int)Math.Ceiling(Math.Abs(x1 - x0) / (5 * g.K + 2))) + 1;

    /// <summary>
    /// The canvas's channel(): n = max(2, ceil(|x1 - x0| / (5k + 2))) segments from (x0, y0) to (x1, y1); vertex i at
    /// u = i / n is lifted by sin(pi u)^0.55 * amp * fbm(x / k, seed) and kept within 0.14 h to 0.86 h.
    /// </summary>
    public static double[] Channel(StrikeGeometry g, double x0, double y0, double x1, double y1, int seed, double amp)
    {
        var points = new double[ChannelVertices(g, x0, x1) * 2];
        ChannelInto(points, g, x0, y0, x1, y1, seed, amp);
        return points;
    }

    /// <summary><see cref="Channel"/> written into <paramref name="points"/> (at least <see cref="ChannelVertices"/> pairs).</summary>
    public static void ChannelInto(Span<double> points, StrikeGeometry g, double x0, double y0, double x1, double y1, int seed, double amp)
    {
        var n = ChannelVertices(g, x0, x1) - 1;
        double lo = g.Height * 0.14, hi = g.Height * 0.86;
        for (var i = 0; i <= n; i++)
        {
            var u = (double)i / n;
            var x = x0 + (x1 - x0) * u;
            var envelope = Math.Pow(Math.Sin(Math.PI * u), 0.55);
            var y = y0 + (y1 - y0) * u + envelope * amp * Fbm(x / g.K, seed);
            points[i * 2] = x; points[i * 2 + 1] = Math.Min(hi, Math.Max(lo, y));
        }
    }

    /// <summary>
    /// The canvas's branch(): six points from vertex <paramref name="at"/>, (12 to 42) k long toward <paramref name="forward"/>,
    /// rising or falling (three in four toward the far side of the middle) by half to nearly all of the room left.
    /// </summary>
    public static double[] Branch(StrikeGeometry g, double[] points, int at, int seed, int forward)
    {
        var branch = new double[12];
        BranchInto(branch, g, points, at, seed, forward);
        return branch;
    }

    /// <summary><see cref="Branch"/> written into <paramref name="branch"/> (12 values).</summary>
    public static void BranchInto(Span<double> branch, StrikeGeometry g, ReadOnlySpan<double> points, int at, int seed, int forward)
    {
        double bx = points[at * 2], by = points[at * 2 + 1];
        var direction = by > g.Middle ? (Hash(seed, 3) < 0.75 ? -1 : 1) : (Hash(seed, 3) < 0.75 ? 1 : -1);
        var room = direction < 0 ? by - g.Height * 0.12 : g.Height * 0.88 - by;
        var length = (12 + Hash(seed, 5) * 30) * g.K;
        var dy = direction * Math.Max(3, room * (0.5 + Hash(seed, 7) * 0.45));
        branch[0] = bx; branch[1] = by;
        for (var i = 1; i <= 5; i++)
        {
            var u = i / 5.0;
            branch[i * 2] = bx + forward * length * u + (Hash(seed, 20 + i) - 0.5) * 4 * g.K;
            branch[i * 2 + 1] = by + dy * Math.Pow(u, 0.8) + (Hash(seed, 40 + i) - 0.5) * 3 * g.K;
        }
    }

    /// <summary>The canvas's trace(): vertices i0..i1, each inner vertex of the whole line moved by (hash(i, js) - 0.5) 2 ja.</summary>
    public static double[] Trace(double[] points, int i0, int i1, int js, double ja)
    {
        var traced = new double[(i1 - i0 + 1) * 2];
        TraceInto(traced, points, i0, i1, js, ja);
        return traced;
    }

    /// <summary><see cref="Trace"/> written into <paramref name="traced"/> (at least i1 - i0 + 1 pairs).</summary>
    public static void TraceInto(Span<double> traced, ReadOnlySpan<double> points, int i0, int i1, int js, double ja)
    {
        var last = points.Length / 2 - 1;
        for (var i = i0; i <= i1; i++)
        {
            var jitter = ja != 0 && i > 0 && i < last ? (Hash(i, js) - 0.5) * 2 * ja : 0;
            traced[(i - i0) * 2] = points[i * 2]; traced[(i - i0) * 2 + 1] = points[i * 2 + 1] + jitter;
        }
    }

    // ---- drawing (render thread)

    /// <summary>
    /// Strokes and fills <paramref name="ops"/> in order, source-over, into <paramref name="canvas"/> (in px), with a painter
    /// of its own (tests; a track keeps one painter, <see cref="StrikePainter"/>, for the life of its layers).
    /// </summary>
    public static void Render(SKCanvas canvas, IReadOnlyList<StrikeOp> ops)
    {
        using var painter = new StrikePainter();
        painter.Paint(canvas, ops);
    }
}

/// <summary>
/// Paints a scene's steps (ED-248 review: nothing allocated per frame). It keeps its stroke and fill paints, one path, and
/// one radial shader per wash colour (radius 100 px about the origin, the canvas's stops); a wash is drawn by moving and
/// scaling the canvas onto the light pool's ellipse, so one shader serves every pool of that colour at any size. Render
/// thread only; disposed with the layers that own it.
/// </summary>
public sealed class StrikePainter : IDisposable
{
    private static readonly SKRect WashRect = new(-100, -100, 100, 100);
    internal static readonly Lazy<SKRuntimeEffect> WashEffect = new(() => SKRuntimeEffect.CreateShader(
        "uniform float3 tint; half4 main(float2 p) { float r = length(p) / 100; float mid = 89.0 / 255.0; float a = r < 0.45 ? mix(1.0, mid, r / 0.45) : mix(mid, 0.0, clamp((r - 0.45) / 0.55, 0.0, 1.0)); return half4(tint * a, a); }",
        out var error) ?? throw new InvalidOperationException("Strike wash: " + error));
    private readonly SKPaint stroke = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Butt };
    private readonly SKPaint fill = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPath path = new();
    private readonly float[] washColour = new float[3];
    private LiquidTrack.PassUniforms? wash;

    /// <summary>Kept wash programs: one serves every colour, including a model crossfade.</summary>
    public int WashShadersMade { get; private set; }

    private nint Wash(SKColor c)
    {
        if (wash is null) { wash = new LiquidTrack.PassUniforms(WashEffect.Value); WashShadersMade++; }
        washColour[0] = c.Red / 255f; washColour[1] = c.Green / 255f; washColour[2] = c.Blue / 255f;
        wash.Set("tint", washColour);
        return wash.MakeShader();
    }

    /// <summary>
    /// Strokes and fills <paramref name="ops"/> in order, source-over, into <paramref name="canvas"/> (in px). The caller
    /// wraps them in one layer screen-blended over the liquid, as the canvas's mix-blend-mode: screen.
    /// </summary>
    public void Paint(SKCanvas canvas, IReadOnlyList<StrikeOp> ops)
    {
        for (var o = 0; o < ops.Count; o++)
        {
            var op = ops[o];
            if (op.Kind == StrikeOpKind.Stroke)
            {
                var line = op.Line;
                path.Rewind();
                path.MoveTo((float)line[0], (float)line[1]);
                for (var i = 2; i < line.Length; i += 2) path.LineTo((float)line[i], (float)line[i + 1]);
                stroke.StrokeWidth = (float)op.Width;
                stroke.ColorF = new SKColorF(op.Color.Red / 255f, op.Color.Green / 255f, op.Color.Blue / 255f, (float)op.Alpha);
                canvas.DrawPath(path, stroke);
            }
            else if (op.Kind == StrikeOpKind.Disc)
            {
                fill.Shader = null;
                fill.ColorF = new SKColorF(op.Color.Red / 255f, op.Color.Green / 255f, op.Color.Blue / 255f, (float)op.Alpha);
                path.Rewind();
                var discs = op.Line;
                for (var i = 0; i + 2 < discs.Length; i += 3) path.AddCircle((float)discs[i], (float)discs[i + 1], (float)discs[i + 2]);
                canvas.DrawPath(path, fill); // non-zero fill: where glints overlap, once
            }
            else
            {
                var shader = Wash(op.Color);
                SkiaShaders.SetShader(fill, shader);
                fill.ColorF = new SKColorF(1, 1, 1, (float)Math.Min(1, op.Alpha));
                var save = canvas.Save();
                try
                {
                    canvas.Translate((float)op.X, (float)op.Y);
                    canvas.Scale((float)(op.Rx / 100), (float)(op.Ry / 100));
                    canvas.DrawRect(WashRect, fill);
                }
                finally
                {
                    canvas.RestoreToCount(save);
                    SkiaShaders.SetShader(fill, 0); SkiaShaders.Release(shader);
                }
            }
        }
        fill.Shader = null;
    }

    public void Dispose()
    {
        fill.Shader = null;
        wash?.Dispose(); wash = null;
        stroke.Dispose(); fill.Dispose(); path.Dispose();
    }
}

/// <summary>
/// One track's strikes: its seed, each strike's channel and branches built once (for as long as the strike is drawn and
/// the geometry holds), and the shimmer's jittered copies made once per 30th of a second. ED-248 review: every buffer is
/// made when the geometry changes and reused after (four strike slots, k mod 4, of which a frame uses three; their traced
/// copies; the sparks; the scene's list), so a frame allocates nothing. One thread at a time: a track resolves its scene
/// on the render thread, under a lock on this object (<see cref="LiquidTrack"/>).
/// </summary>
public sealed class StrikeChannels(int seed)
{
    private const int Slots = 4;

    private sealed class Slot
    {
        public int K = int.MinValue;
        public double[] Main = [];
        public readonly double[][] Branches = [new double[12], new double[12], new double[12], new double[12], new double[12]];
        public int BranchesBuilt;                          // a bit per branch
        public double[] MainTrace = [];
        public (int K, int Js) MainTraced = (int.MinValue, 0);
        public double[] LeaderTrace = [];
        public (int K, int Js, int End) LeaderTraced = (int.MinValue, 0, 0);
        public readonly double[][] BranchTraces = [new double[12], new double[12], new double[12], new double[12], new double[12]];
        public readonly (int K, int Js)[] BranchTraced = new (int, int)[5];

        public void Forget()
        {
            BranchesBuilt = 0; MainTraced = (int.MinValue, 0); LeaderTraced = (int.MinValue, 0, 0);
            for (var b = 0; b < 5; b++) BranchTraced[b] = (int.MinValue, 0);
        }
    }

    private readonly Slot[] slots = [new(), new(), new(), new()];
    private readonly double[][] sparks = [new double[8], new double[8]];
    private readonly List<StrikeOp> ops = new(64);
    private readonly Dictionary<int, double[]> inspectedMain = new();
    private readonly Dictionary<(int K, int Branch), double[]> inspectedBranches = new();
    private StrikeGeometry geometry;
    private bool hasGeometry;

    public int Seed => seed;

    /// <summary>How many channels this track has built (tests: a strike's channel is built once).</summary>
    public int ChannelsBuilt { get; private set; }

    private void Use(StrikeGeometry g)
    {
        if (hasGeometry && geometry == g) return;
        geometry = g; hasGeometry = true;
        inspectedMain.Clear(); inspectedBranches.Clear();
        var length = Strike.ChannelVertices(g, g.Knob, g.CapEnd) * 2;
        foreach (var slot in slots)
        {
            slot.K = int.MinValue; slot.Forget();
            slot.Main = new double[length]; slot.MainTrace = new double[length]; slot.LeaderTrace = new double[length];
        }
    }

    // strike k's slot, its channel built (once while it holds k)
    private Slot SlotOf(StrikeGeometry g, int k)
    {
        var slot = slots[k & (Slots - 1)];
        if (slot.K == k) return slot;
        slot.K = k; slot.Forget();
        ChannelsBuilt++;
        Strike.ChannelInto(slot.Main, g, g.Knob, g.Middle, g.CapEnd, g.Middle + (Strike.Hash(k, unchecked(seed + 1)) - 0.5) * g.Height * 0.45,
            unchecked(seed * 37 + k), g.Height * 0.34);
        return slot;
    }

    /// <summary>Strike k's channel: from the knob (g.ex) to the cap end at a height of its own (hash(k, S+1)), seed S*37 + k,
    /// amplitude 0.34 h.</summary>

    public double[] Main(StrikeGeometry g, int k)
    {
        Use(g);
        if (!inspectedMain.TryGetValue(k, out var points))
            inspectedMain[k] = points = (double[])SlotOf(g, k).Main.Clone();
        return points;
    }

    internal double[] MainFrame(StrikeGeometry g, int k)
    {
        Use(g);
        return SlotOf(g, k).Main;
    }

    /// <summary>Strike k's branch b (0 to 4): from vertex round((0.12 + 0.75 hash(5k+b, S+31)) last), toward the cap,
    /// seed 5k + b + 11 S.</summary>
    public double[] Branch(StrikeGeometry g, int k, int b)
    {
        Use(g);
        if (!inspectedBranches.TryGetValue((k, b), out var points))
            inspectedBranches[(k, b)] = points = (double[])BranchOf(g, SlotOf(g, k), k, b).Clone();
        return points;
    }

    internal double[] BranchFrame(StrikeGeometry g, int k, int b)
    {
        Use(g);
        return BranchOf(g, SlotOf(g, k), k, b);
    }

    private double[] BranchOf(StrikeGeometry g, Slot slot, int k, int b)
    {
        var branch = slot.Branches[b];
        if ((slot.BranchesBuilt & (1 << b)) != 0) return branch;
        var last = slot.Main.Length / 2 - 1;
        var at = Strike.Round((0.12 + 0.75 * Strike.Hash(unchecked(k * 5 + b), unchecked(seed + 31))) * last);
        Strike.BranchInto(branch, g, slot.Main, at, unchecked(k * 5 + b + seed * 11), -1);
        slot.BranchesBuilt |= 1 << b;
        return branch;
    }

    /// <summary>
    /// What the canvas's strike() draws at track time <paramref name="t"/>, in order. The list, and the lines its steps point
    /// into, are this object's own: valid until the next call.
    /// </summary>
    public IReadOnlyList<StrikeOp> Scene(StrikeGeometry g, double t, StrikeTint tint)
    {
        Use(g);
        ops.Clear();
        var k0 = (int)Math.Floor(t / Strike.Period);
        var js = (int)Math.Floor(t * Strike.ShimmerRate);
        var ja = 0.8 * g.K;
        var busy = false;
        for (var k = k0 - 1; k <= k0 + 1; k++)
        {
            var a = t - Strike.StrikeAt(k, seed);
            if (a < -Strike.Lead || a > Strike.Window) continue;
            if (a < 0.5) busy = true;
            var slot = SlotOf(g, k);
            var main = slot.Main;
            var last = main.Length / 2 - 1;
            if (a < 0)
            {
                // the leader: stepped out of the knob, faint, a small light pool at its tip
                var tip = Strike.LeaderTip(a, last);
                if (slot.LeaderTraced != (k, js, tip))
                {
                    Strike.TraceInto(slot.LeaderTrace, main, 0, tip, js, ja * 1.4);
                    slot.LeaderTraced = (k, js, tip);
                }
                Glow(g, tint, slot.LeaderTrace, (tip + 1) * 2, 0.32, 4);
                Wash(tint, main[tip * 2], main[tip * 2 + 1], g.Height * 0.7, 0.2, 1.4);
                continue;
            }
            var intensity = Strike.Intensity(a, Strike.Triple(k, seed));
            var ghost = Strike.Ghost(a);
            if (ghost > 0.01)
            {
                // the glow-out: the channel without its shimmer, widening as it fades
                ops.Add(StrikeOp.Stroke(main, (4 + a * 9) * g.K, tint.Outer, Strike.Fixed3(ghost * 0.22)));
                ops.Add(StrikeOp.Stroke(main, 1.6 * g.K, tint.Inner, Strike.Fixed3(ghost * 0.22)));
            }
            if (intensity > 0.01)
            {
                // the flicker: the liquid's inner flash at the knob, the channel, its five branches
                Wash(tint, g.Knob, g.Middle, g.Height * 1.4, intensity * 0.5, 1.6);
                if (slot.MainTraced != (k, js))
                {
                    Strike.TraceInto(slot.MainTrace, main, 0, last, js, ja);
                    slot.MainTraced = (k, js);
                }
                Glow(g, tint, slot.MainTrace, main.Length, Math.Min(1, intensity), 8);
                for (var b = 0; b < 5; b++)
                {
                    var traced = slot.BranchTraces[b];
                    if (slot.BranchTraced[b] != (k, js))
                    {
                        Strike.TraceInto(traced, BranchOf(g, slot, k, b), 0, 5, js + b, ja * 0.8);
                        slot.BranchTraced[b] = (k, js);
                    }
                    Glow(g, tint, traced, 12, intensity * 0.75, 4);
                }
            }
        }
        Wash(tint, g.Knob, g.Middle, g.Height * 0.9, Strike.Ambient(t), 1.2);
        if (!busy)
        {
            for (var k = k0 - 1; k <= k0; k++)
            {
                if (Strike.SparkAt(k, seed) is not { } start) continue;
                var f = (t - start) * 9;
                if (f < 0 || f >= 1) continue;
                double y0 = g.Middle + (Strike.Hash(k, unchecked(seed + 4)) - 0.5) * g.KnobRadius * 1.1, x0 = g.Knob - g.KnobRadius * 0.85;
                var length = (6 + Strike.Hash(k, unchecked(seed + 6)) * 9) * g.K;
                var spark = sparks[k - (k0 - 1)];
                spark[0] = x0; spark[1] = y0;
                spark[2] = x0 - length * 0.4; spark[3] = y0 + (Strike.Hash(k, 8) - 0.5) * 5 * g.K;
                spark[4] = x0 - length * 0.75; spark[5] = y0 + (Strike.Hash(k, 9) - 0.5) * 6 * g.K;
                spark[6] = x0 - length; spark[7] = y0 + (Strike.Hash(k, 10) - 0.5) * 7 * g.K;
                Glow(g, tint, spark, 8, 0.6 * (1 - f) * (1 - f), 4);
            }
        }
        return ops;
    }

    // glow(a, wide): the halo at wide k, the inner stroke at 2.4 k, the white core at max(0.8, 1.05 k)
    private void Glow(StrikeGeometry g, StrikeTint tint, double[] points, int count, double a, double wide)
    {
        if (a < 0.006) return;
        ops.Add(StrikeOp.Stroke(points, count, wide * g.K, tint.Outer, Strike.Fixed3(a * 0.14)));
        ops.Add(StrikeOp.Stroke(points, count, 2.4 * g.K, tint.Inner, Strike.Fixed3(a * 0.36)));
        ops.Add(StrikeOp.Stroke(points, count, Math.Max(0.8, 1.05 * g.K), SKColors.White, Strike.Fixed3(Math.Min(1, a * 0.92))));
    }

    // wash(x, y, r, a, sx): the light pool, radii sx r by r, at min(1, a)
    private void Wash(StrikeTint tint, double x, double y, double r, double a, double sx)
    {
        if (a < 0.006) return;
        ops.Add(StrikeOp.Wash(x, y, sx * r, r, Math.Min(1, a), tint.Wash));
    }
}

/// <summary>A fast track's lightning for one frame: the track's strikes, where and when, in its tint. The scene is resolved
/// where it is drawn (the render thread, under a lock on <see cref="Channels"/>), so its reused buffers are never shared
/// across threads (ED-248 review).</summary>
public readonly record struct StrikeFrame(StrikeChannels Channels, StrikeGeometry Geometry, double Time, StrikeTint Tint);
