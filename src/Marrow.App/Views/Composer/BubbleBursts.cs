using SkiaSharp;

namespace Marrow.App.Views.Composer;

// OD-0078 / OD-0084, required again by the owner with OD-0090 (2026-10-03: "every bubble the strike touches must burst on
// the spot, on every maker, and the burst must be visible on pale liquids"): the locked engine's contact rule and burst
// (strike-engine.js events / near / process / contact, and drawBubbles' burst), on the port's fast mode (the A2 strikes at
// their own times, OD-0073) and its bubbles (BubbleLanes: the v22 bubbles, riseOf / bubbleNow, at real time).
//   Contact: when the lit channel first reaches a bubble. Each of the leader's seven steps lights the newly reached part of
//   the channel (from one vertex before the last tip to the new tip); the strike instant lights the whole channel and its
//   five branches. A bubble is touched when its circle comes within the lit half-width (0.6 k) of a lit segment, tested
//   where the bubble is at that very instant; every leader step and strike instant since the last frame is tested, in time
//   order, so none is missed between frames. A touched bubble pops on the spot and its current rise ends there (it comes
//   back only with its next rise).
//   Burst: a ring of arcs widening and fading in 0.19 s (white; on a pale liquid the same arcs 1.4 px wider in the neutral
//   rgba(20,22,30, .5 al ink) under them, jewel-knob.html drawBubbles), a bright pop and a glow in the bolt's light, and a
//   few glints in its inner tint.

/// <summary>One pop: where and when, the bubble's radius and opacity then, its turn of the ring and its own number.</summary>
public readonly record struct BubbleBurst(double T, double X, double Y, double R, double A, double Rot, int N, int K, int I, int Rise);

/// <summary>A track's bursts (UI thread). It keeps its own bubble lanes, so the drawing's lanes are never read across threads.</summary>
public sealed class BubbleBursts
{
    /// <summary>The lit channel's half-width, in tube scales k (the bolt's white core and its shimmer).</summary>
    public const double HalfWidth = 0.6;

    /// <summary>A burst's life, s.</summary>
    public const double Life = 0.19;

    private readonly Dictionary<(int Lane, int Rise), double> dead = new(128);
    private readonly List<BubbleBurst> bursts = new(128);
    private readonly List<(double T, int K, int Step)> events = new(128);
    private readonly (int Lane, int Rise)[] expiredKeys = new (int, int)[128];
    private readonly double[][] glints = Enumerable.Range(0, 128).Select(_ => new double[12]).ToArray();

    internal IReadOnlyCollection<(int Lane, int Rise)> DeadKeys => dead.Keys;
    private double? processedTo;
    private BubbleLanes? lanes;

    /// <summary>Every burst so far, in order (tests: contacts equal bursts).</summary>
    public Action<BubbleBurst>? OnBurst { get; set; }

    /// <summary>The bursts still showing (up to 0.4 s old).</summary>
    public IReadOnlyList<BubbleBurst> Recent => bursts;

    /// <summary>True when bubble <paramref name="lane"/>'s rise <paramref name="rise"/> has burst.</summary>
    public bool IsDead(int lane, int rise) => dead.ContainsKey((lane, rise));

    /// <summary>The popped rises (lane, rise), for the drawing to leave out.</summary>
    public (int Lane, int Rise)[] Dead() => [.. dead.Keys];

    private BubbleLanes Lanes(int seed, double width, double height)
    {
        if (lanes is null || lanes.Width != width || lanes.Height != height || lanes.Seed != seed) lanes = new BubbleLanes(seed, width, height);
        return lanes;
    }

    // events(g, B) for an A2 strike: the leader's seven steps at -0.14 + (j - 1) 0.02 s, then the strike at 0
    private static double EventAge(int step) => step < 7 ? -Strike.Lead + step * Strike.Lead / 7 : 0;

    /// <summary>
    /// process(tl, g, t): tests every leader step and strike instant in (the last frame, <paramref name="t"/>], in time order.
    /// A jump back in time or a gap over 0.75 s starts again: nothing dead, no bursts, and the last 6 s replayed.
    /// </summary>
    public void Process(StrikeChannels channels, StrikeGeometry g, int strikeSeed, int bubbleSeed, double trackWidth, double t)
    {
        var bubbles = Lanes(bubbleSeed, trackWidth, g.Height);
        if (processedTo is not { } from || t < from - 1e-9 || t - from > 0.75) { dead.Clear(); bursts.Clear(); from = t - 6; }
        if (t > from)
        {
            events.Clear();
            for (var k = (int)Math.Floor(from / Strike.Period) - 2; k <= (int)Math.Floor(t / Strike.Period) + 1; k++)
            {
                var at = Strike.StrikeAt(k, strikeSeed);
                for (var step = 0; step <= 7; step++)
                {
                    var te = at + EventAge(step);
                    if (te > from && te <= t) events.Add((te, k, step));
                }
            }
            events.Sort((p, q) => p.T.CompareTo(q.T));
            foreach (var (te, k, step) in events) Contact(channels, g, strikeSeed, bubbles, te, k, step);
        }
        processedTo = t;
        for (var i = bursts.Count - 1; i >= 0; i--) if (t - bursts[i].T > 0.4) bursts.RemoveAt(i);
        if (dead.Count > 64)
        {
            var count = 0;
            foreach (var item in dead) if (t - item.Value > 10) expiredKeys[count++] = item.Key;
            for (var i = 0; i < count; i++) dead.Remove(expiredKeys[i]);
        }
    }

    private void Contact(StrikeChannels channels, StrikeGeometry g, int seed, BubbleLanes bubbles, double te, int k, int step)
    {
        var main = channels.MainFrame(g, k);
        var last = main.Length / 2 - 1;
        var hw = HalfWidth * g.K;
        for (var i = 0; i < bubbles.Count; i++)
        {
            if (bubbles.AtFrame(i, te) is not { } now) continue;
            var (centre, radius, opacity) = LiquidTrack.BubbleAt(now.Rise, g.Height, now.Progress);
            if (opacity < 0.12) continue;
            var key = (i, now.K);
            if (dead.ContainsKey(key)) continue;
            var lim = radius + hw;
            bool hit;
            if (step < 7)
            {
                // the leader's step j = step + 1 lights from one vertex before the last tip to the new tip
                var tip = Tip(step + 1, last);
                var prev = step == 0 ? 0 : Tip(step, last);
                hit = Near(centre.X, centre.Y, main, Math.Max(0, prev - 1), tip, lim);
            }
            else
            {
                hit = Near(centre.X, centre.Y, main, 0, last, lim);
                for (var b = 0; b < 5 && !hit; b++) hit = Near(centre.X, centre.Y, channels.BranchFrame(g, k, b), 0, 5, lim);
            }
            if (!hit) continue;
            dead[key] = te;
            var n = (int)Math.Floor(Strike.Hash(i * 977 + now.K, seed) * 1e6);
            var burst = new BubbleBurst(te, centre.X, centre.Y, radius, opacity, n % 628 / 100.0, n, k, i, now.K);
            bursts.Add(burst);
            OnBurst?.Invoke(burst);
        }
    }

    // the leader's tip after step j of seven: round(min(1, j / 7) 0.9 last)
    private static int Tip(int j, int last) => Strike.Round(Math.Min(1, j / 7.0) * 0.9 * last);

    /// <summary>near(): is (px, py) within <paramref name="lim"/> of the polyline's vertices i0..i1.</summary>
    public static bool Near(double px, double py, double[] pts, int i0, int i1, double lim)
    {
        var l2 = lim * lim;
        if (i1 <= i0) { double dx0 = pts[i0 * 2] - px, dy0 = pts[i0 * 2 + 1] - py; return dx0 * dx0 + dy0 * dy0 <= l2; }
        for (var i = i0; i < i1; i++)
        {
            double ax = pts[i * 2], ay = pts[i * 2 + 1], bx = pts[i * 2 + 2], by = pts[i * 2 + 3];
            if (px < Math.Min(ax, bx) - lim || px > Math.Max(ax, bx) + lim) continue;
            double dx = bx - ax, dy = by - ay, l = dx * dx + dy * dy;
            var u = l > 0 ? ((px - ax) * dx + (py - ay) * dy) / l : 0;
            u = u < 0 ? 0 : u > 1 ? 1 : u;
            double qx = ax + dx * u - px, qy = ay + dy * u - py;
            if (qx * qx + qy * qy <= l2) return true;
        }
        return false;
    }

    /// <summary>
    /// The burst's light in the bolt's layer (drawBubbles, on the lightning's canvas): the pop (a bright point gone in its
    /// first 27 %), a glow (60 %), and three or four glints flying off in the inner tint.
    /// </summary>
    public void Light(List<StrikeOp> ops, StrikeGeometry g, StrikeTint tint, double t)
    {
        double sk = Math.Sqrt(g.K);
        var glint = 0;
        foreach (var b in bursts)
        {
            var u = (t - b.T) / Life;
            if (u < 0 || u >= 1) continue;
            var r0 = Math.Max(b.R, 1.6);
            if (u < 0.27) Pool(ops, tint, b.X, b.Y, r0 + 3.5 * sk, 0.55 * Math.Pow(1 - u / 0.27, 2));
            if (u < 0.6) Pool(ops, tint, b.X, b.Y, r0 + 5 * g.K, 0.34 * Math.Pow(1 - u / 0.6, 2));
            int m = 3 + b.N % 2;
            double e2 = 1 - (1 - u) * (1 - u), ga = Math.Pow(1 - u, 2) * 0.95;
            // one path of m discs filled once, as the engine fills them (overlapping glints do not add up)
            var discs = glints[glint++];
            for (var j = 0; j < m; j++)
            {
                var an = b.Rot + j * 2 * Math.PI / m + (Strike.Hash(b.N, j) - 0.5) * 0.9;
                var d = r0 + (2.5 + 4 * Strike.Hash(b.N, j + 7)) * sk * e2;
                discs[j * 3] = b.X + d * Math.Cos(an); discs[j * 3 + 1] = b.Y + d * Math.Sin(an);
                discs[j * 3 + 2] = (0.6 + 0.45 * Strike.Hash(b.N, j + 13)) * (1 - 0.5 * u) * Math.Max(0.8, g.K);
            }
            ops.Add(StrikeOp.Discs(discs, m * 3, tint.Inner, Strike.Fixed3(ga)));
        }

        static void Pool(List<StrikeOp> ops, StrikeTint tint, double x, double y, double r, double a)
        {
            if (a < 0.006) return;
            ops.Add(StrikeOp.Wash(x, y, r, r, Math.Min(1, a), tint.Wash));
        }
    }

    /// <summary>One ring of a burst at <paramref name="t"/>: its radius, its arcs (start angle, sweep) and the stroke's width
    /// and alpha; null outside its life.</summary>
    public static (double Radius, int Arcs, double Start, double Span, double Gap, double Width, double Alpha)? Ring(BubbleBurst b, double k, double t)
    {
        var u = (t - b.T) / Life;
        if (u < 0 || u >= 1) return null;
        double sk = Math.Sqrt(k), e = 1 - Math.Pow(1 - u, 3), r0 = Math.Max(b.R, 1.6), rr = r0 + (2.6 + 1.1 * r0) * sk * e;
        int n = 5 + b.N % 3;
        double span = 2 * Math.PI / n, gap = span * (0.12 + 0.5 * e), al = Math.Min(1, 0.35 + b.A) * 0.9 * Math.Pow(1 - u, 1.4);
        return (rr, n, b.Rot + 0.35 * e + gap / 2, span, gap, Math.Max(0.45, 1.25 - 0.8 * u), al);
    }

    [ThreadStatic] private static SKPaint? ringPaint;
    [ThreadStatic] private static SKPath? ringPath;

    /// <summary>Draws the rings at <paramref name="t"/> (render thread): on a pale liquid the neutral arcs 1.4 px wider at
    /// .5 al ink under the white ones.</summary>
    public static void DrawRings(SKCanvas canvas, IReadOnlyList<BubbleBurst> rings, double k, double t, double ink)
    {
        if (rings.Count == 0) return;
        // the render thread's own paint and path, kept (no managed objects a frame, ED-252)
        var paint = ringPaint ??= new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        var path = ringPath ??= new SKPath();
        for (var r = 0; r < rings.Count; r++)
        {
            var b = rings[r];
            if (Ring(b, k, t) is not { } ring) continue;
            path.Rewind();
            var oval = new SKRect((float)(b.X - ring.Radius), (float)(b.Y - ring.Radius), (float)(b.X + ring.Radius), (float)(b.Y + ring.Radius));
            for (var j = 0; j < ring.Arcs; j++)
            {
                var a0 = ring.Start + j * ring.Span;
                path.AddArc(oval, (float)(a0 * 180 / Math.PI), (float)((ring.Span - ring.Gap) * 180 / Math.PI));
            }
            if (ink >= 0.01)
            {
                paint.StrokeWidth = (float)(ring.Width + 1.4);
                paint.Color = new SKColor(20, 22, 30, (byte)Math.Round(255 * Strike.Fixed3(ring.Alpha * 0.5 * ink)));
                canvas.DrawPath(path, paint);
            }
            paint.StrokeWidth = (float)ring.Width;
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Strike.Fixed3(ring.Alpha)));
            canvas.DrawPath(path, paint);
        }
    }
}
