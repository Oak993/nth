using SkiaSharp;

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
// A frame is resolved on the UI thread into a short list of drawing steps (StrikeOp), computed from a channel built once
// per strike and a jittered copy made once per 30th of a second; the render thread strokes and fills them into one
// layer screen-blended over the liquid (LiquidTrack), under the tube's gloss and rims. Everything is a function of time,
// so the same moment always draws the same.

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
public sealed record StrikeGeometry(double Width, double Height, double Knob, double KnobRadius)
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
/// </summary>
public sealed class StrikeOp
{
    private StrikeOp(StrikeOpKind kind, double[] points, double width, SKColor color, double alpha, double x, double y, double rx, double ry)
    {
        Kind = kind; Points = points; Width = width; Color = color; Alpha = alpha; X = x; Y = y; Rx = rx; Ry = ry;
    }

    public StrikeOpKind Kind { get; }
    public double[] Points { get; }
    public double Width { get; }
    public SKColor Color { get; }
    public double Alpha { get; }
    public double X { get; }
    public double Y { get; }
    public double Rx { get; }
    public double Ry { get; }

    public static StrikeOp Stroke(double[] points, double width, SKColor color, double alpha) => new(StrikeOpKind.Stroke, points, width, color, alpha, 0, 0, 0, 0);

    public static StrikeOp Wash(double x, double y, double rx, double ry, double alpha, SKColor color) => new(StrikeOpKind.Wash, [], 0, color, alpha, x, y, rx, ry);

    /// <summary>OD-0078: a burst's glints, discs (x, y, r triples) filled once as one path in a colour at an alpha.</summary>
    public static StrikeOp Discs(double[] discs, SKColor color, double alpha) => new(StrikeOpKind.Disc, discs, 0, color, alpha, 0, 0, 0, 0);
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

    /// <summary>
    /// The canvas's channel(): n = max(2, ceil(|x1 - x0| / (5k + 2))) segments from (x0, y0) to (x1, y1); vertex i at
    /// u = i / n is lifted by sin(pi u)^0.55 * amp * fbm(x / k, seed) and kept within 0.14 h to 0.86 h.
    /// </summary>
    public static double[] Channel(StrikeGeometry g, double x0, double y0, double x1, double y1, int seed, double amp)
    {
        var step = 5 * g.K + 2;
        var n = Math.Max(2, (int)Math.Ceiling(Math.Abs(x1 - x0) / step));
        double lo = g.Height * 0.14, hi = g.Height * 0.86;
        var points = new double[(n + 1) * 2];
        for (var i = 0; i <= n; i++)
        {
            var u = (double)i / n;
            var x = x0 + (x1 - x0) * u;
            var envelope = Math.Pow(Math.Sin(Math.PI * u), 0.55);
            var y = y0 + (y1 - y0) * u + envelope * amp * Fbm(x / g.K, seed);
            points[i * 2] = x; points[i * 2 + 1] = Math.Min(hi, Math.Max(lo, y));
        }
        return points;
    }

    /// <summary>
    /// The canvas's branch(): six points from vertex <paramref name="at"/>, (12 to 42) k long toward <paramref name="forward"/>,
    /// rising or falling (three in four toward the far side of the middle) by half to nearly all of the room left.
    /// </summary>
    public static double[] Branch(StrikeGeometry g, double[] points, int at, int seed, int forward)
    {
        double bx = points[at * 2], by = points[at * 2 + 1];
        var direction = by > g.Middle ? (Hash(seed, 3) < 0.75 ? -1 : 1) : (Hash(seed, 3) < 0.75 ? 1 : -1);
        var room = direction < 0 ? by - g.Height * 0.12 : g.Height * 0.88 - by;
        var length = (12 + Hash(seed, 5) * 30) * g.K;
        var dy = direction * Math.Max(3, room * (0.5 + Hash(seed, 7) * 0.45));
        var branch = new double[12];
        branch[0] = bx; branch[1] = by;
        for (var i = 1; i <= 5; i++)
        {
            var u = i / 5.0;
            branch[i * 2] = bx + forward * length * u + (Hash(seed, 20 + i) - 0.5) * 4 * g.K;
            branch[i * 2 + 1] = by + dy * Math.Pow(u, 0.8) + (Hash(seed, 40 + i) - 0.5) * 3 * g.K;
        }
        return branch;
    }

    /// <summary>The canvas's trace(): vertices i0..i1, each inner vertex of the whole line moved by (hash(i, js) - 0.5) 2 ja.</summary>
    public static double[] Trace(double[] points, int i0, int i1, int js, double ja)
    {
        var last = points.Length / 2 - 1;
        var traced = new double[(i1 - i0 + 1) * 2];
        for (var i = i0; i <= i1; i++)
        {
            var jitter = ja != 0 && i > 0 && i < last ? (Hash(i, js) - 0.5) * 2 * ja : 0;
            traced[(i - i0) * 2] = points[i * 2]; traced[(i - i0) * 2 + 1] = points[i * 2 + 1] + jitter;
        }
        return traced;
    }

    // ---- drawing (render thread)

    private static readonly float[] WashStops = [0, 0.45f, 1];

    /// <summary>
    /// Strokes and fills <paramref name="ops"/> in order, source-over, into <paramref name="canvas"/> (in px). The caller
    /// wraps them in one layer screen-blended over the liquid, as the canvas's mix-blend-mode: screen.
    /// </summary>
    public static void Render(SKCanvas canvas, IReadOnlyList<StrikeOp> ops)
    {
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Butt };
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        using var path = new SKPath();
        foreach (var op in ops)
        {
            if (op.Kind == StrikeOpKind.Stroke)
            {
                path.Rewind();
                path.MoveTo((float)op.Points[0], (float)op.Points[1]);
                for (var i = 2; i < op.Points.Length; i += 2) path.LineTo((float)op.Points[i], (float)op.Points[i + 1]);
                stroke.StrokeWidth = (float)op.Width;
                stroke.ColorF = new SKColorF(op.Color.Red / 255f, op.Color.Green / 255f, op.Color.Blue / 255f, (float)op.Alpha);
                canvas.DrawPath(path, stroke);
            }
            else if (op.Kind == StrikeOpKind.Disc)
            {
                fill.Shader = null;
                fill.ColorF = new SKColorF(op.Color.Red / 255f, op.Color.Green / 255f, op.Color.Blue / 255f, (float)op.Alpha);
                path.Rewind();
                for (var i = 0; i + 2 < op.Points.Length; i += 3) path.AddCircle((float)op.Points[i], (float)op.Points[i + 1], (float)op.Points[i + 2]);
                canvas.DrawPath(path, fill); // non-zero fill: where glints overlap, once
            }
            else
            {
                var c = op.Color;
                SKColor[] colors = [c.WithAlpha(255), c.WithAlpha(89), c.WithAlpha(0)]; // rgba(wash, 1 / 0.35 / 0)
                var local = SKMatrix.CreateScale((float)(op.Rx / 100), (float)(op.Ry / 100)).PostConcat(SKMatrix.CreateTranslation((float)op.X, (float)op.Y));
                using var shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 100, colors, WashStops, SKShaderTileMode.Clamp, local);
                fill.Shader = shader;
                fill.ColorF = new SKColorF(1, 1, 1, (float)Math.Min(1, op.Alpha));
                canvas.DrawRect(new SKRect((float)(op.X - op.Rx), (float)(op.Y - op.Ry), (float)(op.X + op.Rx), (float)(op.Y + op.Ry)), fill);
                fill.Shader = null;
            }
        }
    }
}

/// <summary>
/// One track's strikes: its seed, each strike's channel and branches built once (for as long as the strike is drawn and
/// the geometry holds), and the shimmer's jittered copies made once per 30th of a second. UI thread only.
/// </summary>
public sealed class StrikeChannels(int seed)
{
    private StrikeGeometry? geometry;
    private readonly Dictionary<int, double[]> mains = [];
    private readonly Dictionary<(int K, int B), double[]> branches = [];
    private readonly Dictionary<(int K, int Part, int End), double[]> traces = [];
    private int shimmer = int.MinValue;

    public int Seed => seed;

    /// <summary>How many channels this track has built (tests: a strike's channel is built once).</summary>
    public int ChannelsBuilt { get; private set; }

    private void Use(StrikeGeometry g)
    {
        if (Equals(geometry, g)) return;
        geometry = g; mains.Clear(); branches.Clear(); traces.Clear();
    }

    /// <summary>Strike k's channel: from the knob (g.ex) to the cap end at a height of its own (hash(k, S+1)), seed S*37 + k,
    /// amplitude 0.34 h.</summary>
    public double[] Main(StrikeGeometry g, int k)
    {
        Use(g);
        if (mains.TryGetValue(k, out var main)) return main;
        ChannelsBuilt++;
        return mains[k] = Strike.Channel(g, g.Knob, g.Middle, g.CapEnd, g.Middle + (Strike.Hash(k, unchecked(seed + 1)) - 0.5) * g.Height * 0.45,
            unchecked(seed * 37 + k), g.Height * 0.34);
    }

    /// <summary>Strike k's branch b (0 to 4): from vertex round((0.12 + 0.75 hash(5k+b, S+31)) last), toward the cap,
    /// seed 5k + b + 11 S.</summary>
    public double[] Branch(StrikeGeometry g, int k, int b)
    {
        Use(g);
        if (branches.TryGetValue((k, b), out var branch)) return branch;
        var main = Main(g, k);
        var last = main.Length / 2 - 1;
        var at = Strike.Round((0.12 + 0.75 * Strike.Hash(unchecked(k * 5 + b), unchecked(seed + 31))) * last);
        return branches[(k, b)] = Strike.Branch(g, main, at, unchecked(k * 5 + b + seed * 11), -1);
    }

    private double[] Traced(int k, int part, double[] points, int end, int js, double ja)
    {
        if (traces.TryGetValue((k, part, end), out var traced)) return traced;
        return traces[(k, part, end)] = Strike.Trace(points, 0, end, js, ja);
    }

    /// <summary>What the canvas's strike() draws at track time <paramref name="t"/>, in order.</summary>
    public IReadOnlyList<StrikeOp> Scene(StrikeGeometry g, double t, StrikeTint tint)
    {
        Use(g);
        var ops = new List<StrikeOp>(24);
        var k0 = (int)Math.Floor(t / Strike.Period);
        var js = (int)Math.Floor(t * Strike.ShimmerRate);
        if (js != shimmer) { shimmer = js; traces.Clear(); }
        foreach (var old in mains.Keys.Where(k => k < k0 - 1 || k > k0 + 1).ToArray())
        {
            mains.Remove(old);
            foreach (var key in branches.Keys.Where(key => key.K == old).ToArray()) branches.Remove(key);
        }
        var ja = 0.8 * g.K;
        var busy = false;
        for (var k = k0 - 1; k <= k0 + 1; k++)
        {
            var a = t - Strike.StrikeAt(k, seed);
            if (a < -Strike.Lead || a > Strike.Window) continue;
            if (a < 0.5) busy = true;
            var main = Main(g, k);
            var last = main.Length / 2 - 1;
            if (a < 0)
            {
                // the leader: stepped out of the knob, faint, a small light pool at its tip
                var tip = Strike.LeaderTip(a, last);
                Glow(ops, g, tint, Traced(k, -2, main, tip, js, ja * 1.4), 0.32, 4);
                Wash(ops, tint, main[tip * 2], main[tip * 2 + 1], g.Height * 0.7, 0.2, 1.4);
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
                Wash(ops, tint, g.Knob, g.Middle, g.Height * 1.4, intensity * 0.5, 1.6);
                Glow(ops, g, tint, Traced(k, -1, main, last, js, ja), Math.Min(1, intensity), 8);
                for (var b = 0; b < 5; b++)
                    Glow(ops, g, tint, Traced(k, b, Branch(g, k, b), 5, js + b, ja * 0.8), intensity * 0.75, 4);
            }
        }
        Wash(ops, tint, g.Knob, g.Middle, g.Height * 0.9, Strike.Ambient(t), 1.2);
        if (!busy)
        {
            for (var k = k0 - 1; k <= k0; k++)
            {
                if (Strike.SparkAt(k, seed) is not { } start) continue;
                var f = (t - start) * 9;
                if (f < 0 || f >= 1) continue;
                double y0 = g.Middle + (Strike.Hash(k, unchecked(seed + 4)) - 0.5) * g.KnobRadius * 1.1, x0 = g.Knob - g.KnobRadius * 0.85;
                var length = (6 + Strike.Hash(k, unchecked(seed + 6)) * 9) * g.K;
                double[] spark = [x0, y0, x0 - length * 0.4, y0 + (Strike.Hash(k, 8) - 0.5) * 5 * g.K, x0 - length * 0.75, y0 + (Strike.Hash(k, 9) - 0.5) * 6 * g.K,
                    x0 - length, y0 + (Strike.Hash(k, 10) - 0.5) * 7 * g.K];
                Glow(ops, g, tint, spark, 0.6 * (1 - f) * (1 - f), 4);
            }
        }
        return ops;
    }

    // glow(a, wide): the halo at wide k, the inner stroke at 2.4 k, the white core at max(0.8, 1.05 k)
    private static void Glow(List<StrikeOp> ops, StrikeGeometry g, StrikeTint tint, double[] points, double a, double wide)
    {
        if (a < 0.006) return;
        ops.Add(StrikeOp.Stroke(points, wide * g.K, tint.Outer, Strike.Fixed3(a * 0.14)));
        ops.Add(StrikeOp.Stroke(points, 2.4 * g.K, tint.Inner, Strike.Fixed3(a * 0.36)));
        ops.Add(StrikeOp.Stroke(points, Math.Max(0.8, 1.05 * g.K), SKColors.White, Strike.Fixed3(Math.Min(1, a * 0.92))));
    }

    // wash(x, y, r, a, sx): the light pool, radii sx r by r, at min(1, a)
    private static void Wash(List<StrikeOp> ops, StrikeTint tint, double x, double y, double r, double a, double sx)
    {
        if (a < 0.006) return;
        ops.Add(StrikeOp.Wash(x, y, sx * r, r, Math.Min(1, a), tint.Wash));
    }
}
