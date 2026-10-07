namespace Marrow.App.Views.Composer;

// E:539-605 + P:348-390/491-532/730-781; SPEC K-087..K-105, K-139..K-143.
public sealed class StrikeStorm(int seed)
{
    public StrikePlans Plans { get; } = new(seed);
    private readonly StrikeResolvedFrame frame = new();

    public StrikeResolvedFrame Resolve(StrikeGeometry g, double time, double pageTime, double fast, double ink)
    {
        frame.Clear(); frame.Opacity = Strike.Round(fast * 1000) / 1000d; frame.LightOpacity = Strike.Round(frame.Opacity * (1 - ink) * 1000) / 1000d;
        if (frame.Opacity <= 0.002) return frame;
        var js = (int)Math.Floor(time * 30); var ja = 0.8 * g.K; var k0 = (int)Math.Floor(time / 2.4); var busy = false;
        for (var k = k0 - 2; k <= k0 + 1; k++)
        {
            var p = Plans.Get(g, k); Bolt(p); if (p.Twin is { } twin) Bolt(twin);
        }
        Wash(g.Knob, g.Middle, g.Height * 0.9, 0.12 + 0.04 * Math.Sin(pageTime * 2.6), 1.2);
        if (!busy) for (var k = k0 - 2; k <= k0; k++)
        {
            if (StrikeMix.Spark(k, seed) is not { Blocked: false } spark) continue;
            var f = (time - spark.At) * 9; if (f < 0 || f >= 1) continue;
            var p = Plans.Get(g, k); var a = 0.6 * (1 - f) * (1 - f); if (a < 0.006) continue;
            var line = new StrikePolyline(p.SparkPoints, 0, 4, k, false, 4, 0); var trace = new StrikeTrace(line, 0, 3, 0, 0);
            frame.AddLight(new(1, a, 0, 0, 0, 0, trace, 4));
            if (a >= 0.15) frame.AddLit(new(trace, 0.6 + 0.6 * ink, g.K));
            Mask(0, 0, trace, 4 * g.K, a); Mask(0, 1, trace, 2.4 * g.K, a); Mask(0, 2, trace, Math.Max(0.8, 1.05 * g.K), a);
        }
        return frame;

        void Bolt(StrikePlan p)
        {
            var b = p.Timing; var a = time - b.At; if (a < -b.Lead || a > 1.9) return; if (a < 0.5) busy = true;
            var m = p.Channels[0];
            if (a < 0)
            {
                var j = Math.Floor((a + b.Lead) / b.Lead * b.Steps) + 1; var q = Math.Min(1, j / b.Steps) * b.Reach; var tip = Strike.Round(q * p.LastMain);
                Wash(m.Points[tip * 2], m.Points[tip * 2 + 1], g.Height * 0.7, 0.2, 1.4);
                if (p.ChannelCount > 1)
                {
                    var f = p.Channels[1]; var ft = q * p.LastMain <= f.Split ? -1 : Strike.Round(Math.Min(1, (q * p.LastMain - f.Split) / Math.Max(1, p.LastMain * b.Reach - f.Split)) * b.Reach * (f.Count / 2 - 1));
                    if (ft > 0) Wash(f.Points[ft * 2], f.Points[ft * 2 + 1], g.Height * 0.6, 0.14, 1.4);
                }
            }
            else
            {
                var pulse = b.Pulse(a);
                if (pulse > 0.01)
                {
                    Wash(p.FlashX, p.FlashY, g.Height * 1.4, pulse * 0.5 * (b.Partial ? 0.6 : 1) * (b.Event == 4 ? 0.7 : 1), 1.6);
                    if (b.Event == 3) Wash(g.Knob, g.Middle, g.Height * 0.9, pulse * 0.25, 1.3);
                    if (b.Partial) Wash(m.Points[p.Extent * 2], m.Points[p.Extent * 2 + 1], g.Height * 0.55, pulse * 0.35, 1.2);
                }
            }
            Plume(p, g, a, js, ja);
        }
        void Wash(double x, double y, double radius, double alpha, double sx)
        {
            if (alpha >= 0.006) frame.AddLight(new(0, alpha, x, y, radius, sx, default, 0));
        }
    }

    private void Mask(int set, int layer, StrikeTrace trace, double width, double alpha)
    {
        if (alpha >= 0.004) frame.Masks.Add(new(set, layer, trace, width, FixedAlpha(Math.Min(1, alpha))));
    }

    // Number.toFixed(3) rounds the exact binary input, not its already-rounded product by 1000.
    // Positive bounded drawing alphas: mantissa * 1000 fits ulong. Legacy Strike.Fixed3 stays unchanged.
    internal static double FixedAlpha(double value)
    {
        if (value == 0) return 0;
        var bits = (ulong)BitConverter.DoubleToInt64Bits(value); var exponent = (int)((bits >> 52) & 2047); var shift = 1075 - exponent;
        if (shift >= 64) return 0;
        var numerator = ((bits & 0x000fffffffffffffUL) | 0x0010000000000000UL) * 1000;
        var whole = numerator >> shift; var remainder = numerator & ((1UL << shift) - 1);
        if (remainder >= 1UL << (shift - 1)) whole++;
        return whole / 1000d;
    }

    private void Plume(StrikePlan p, StrikeGeometry g, double age, int js, double ja)
    {
        var b = p.Timing; var plume = StrikePlume.Geometry(p, g, seed); var lastM = p.LastMain;
        for (var trail = 0; trail < 3; trail++)
        {
            var a = age - trail / 30d; var jn = js - trail; var weight = trail == 0 ? 1 : trail == 1 ? 0.42 : 0.18;
            if (a < -b.Lead || a > 1.9) continue;
            double front, strength, ghost = 0;
            if (a < 0)
            {
                var j = Math.Floor((a + b.Lead) / b.Lead * b.Steps) + 1; front = Strike.Round(Math.Min(1, j / b.Steps) * b.Reach * lastM) / (double)lastM; strength = 0.32;
            }
            else { front = b.Partial ? b.Reach : 1; strength = Math.Min(1, b.Pulse(a)); ghost = trail == 0 ? Math.Exp(-a / (b.Partial ? 0.35 : 0.5)) : 0; }
            if (strength < 0.01 && ghost < 0.01) continue;
            for (var index = 0; index < plume.Count; index++)
            {
                var line = plume.Lines[index]; if (plume.U[line.Start] > front + 1e-6) continue;
                var end = line.N; while (end > 0 && plume.U[line.Start + end] > front + 1e-6) end--; if (end < 1) continue;
                var sc = line.Id == 0 ? 1 : line.Level == 9 ? 0.35 : line.Level == 0 ? 0.45 + 0.55 * line.Weight : (0.45 + 0.55 * line.Weight / Math.Pow(0.62, line.Level)) * Math.Pow(0.62, line.Level);
                var flicker = line.Id == 0 ? 0.85 + 0.15 * Strike.Hash(jn, 5) : Strike.Hash(unchecked(jn * 7 + line.Id), 991) < 0.16 ? 0.12 : 0.65 + 0.35 * Strike.Hash(unchecked(jn * 3 + line.Id), 992);
                var alpha = strength * sc * flicker * weight; var jp = unchecked(jn * 31 + line.Id); var jitter = ja * (line.Id == 0 ? 1 : 0.6);
                var wo = (line.Id == 0 ? b.Event == 4 ? 6 : 8 : 4.5 * line.Weight) * g.K; var wi = (line.Id == 0 ? 2.4 : 1.7 * line.Weight) * g.K; var wc = Math.Max(0.5, (line.Id == 0 ? 1.05 : 0.8 * line.Weight) * g.K);
                var polyline = new StrikePolyline(plume.Points, line.Start, line.N + 1, b.K, b.IsTwin, 3, index);
                if (trail == 0 && alpha >= 0.15) frame.AddLit(new(new(polyline, 0, end, jp, jitter), wi / 2 + 0.3 * g.K));
                if (trail > 0 && line.Level != 0) continue;
                if (alpha >= 0.004)
                {
                    var mid = line.Level >= 1 ? end : Math.Max(1, Strike.Round(end * 0.6)); var first = new StrikeTrace(polyline, 0, mid, jp, jitter); var second = new StrikeTrace(polyline, mid, end, jp, jitter);
                    if (trail == 0 && line.Level <= 1) { Mask(1, 0, first, wo, alpha); if (end > mid) Mask(1, 0, second, wo * 0.6, alpha * 0.75); }
                    Mask(1, 1, first, wi, alpha); if (end > mid) Mask(1, 1, second, wi * 0.6, alpha * 0.75);
                    Mask(1, 2, first, wc, alpha); if (end > mid) Mask(1, 2, second, wc * 0.6, alpha * 0.75);
                }
                if (ghost > 0.01 && line.Level <= 1)
                {
                    var trace = new StrikeTrace(polyline, 0, end, 0, 0); var gs = ghost * (line.Id == 0 ? 1 : 0.6);
                    Mask(1, 0, trace, (line.Id == 0 ? 4 + a * 9 : 2.5 + a * 6) * g.K, Math.Min(1, gs * 1.57));
                    Mask(1, 1, trace, (line.Id == 0 ? 1.6 : 1.1) * g.K, Math.Min(1, gs * 0.61));
                }
            }
        }
    }
}
