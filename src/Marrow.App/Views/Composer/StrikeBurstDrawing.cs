namespace Marrow.App.Views.Composer;

// E:1058-1079 / P:686-727. Retained geometry, explicit colour alpha; canvas state belongs to the painter.
public sealed class StrikeBurstDrawing
{
    public StrikeBurstArc[] Arcs { get; } = new StrikeBurstArc[7];
    public StrikeBurstGlint[] Glints { get; } = new StrikeBurstGlint[4];
    public StrikeLight[] Washes { get; } = new StrikeLight[2];
    public int ArcCount { get; private set; }
    public int GlintCount { get; private set; }
    public int RingCount { get; private set; }
    public int WashCount { get; private set; }
    public double RingWidth { get; private set; }
    public double RingAlpha { get; private set; }
    public double DarkRingAlpha { get; private set; }
    public double GlintAlpha { get; private set; }
    public double InkGlintAlpha { get; private set; }

    public void Resolve(StrikeGeometry g, StrikeBurst burst, double time, StrikeInk ink)
    {
        ArcCount = GlintCount = RingCount = WashCount = 0;
        var u = (time - burst.T) / 0.19; if (u < 0 || u >= 1) return;
        var sk = Math.Sqrt(g.K); var e = 1 - Math.Pow(1 - u, 3); var r0 = Math.Max(burst.Radius, 1.6);
        var radius = r0 + (2.6 + 1.1 * r0) * sk * e; var n = 5 + burst.N % 3;
        var span = 6.283185 / n; var gap = span * (0.12 + 0.5 * e); var alpha = Math.Min(1, 0.35 + burst.Alpha) * 0.9 * Math.Pow(1 - u, 1.4);
        RingWidth = Math.Max(0.45, 1.25 - 0.8 * u); RingAlpha = StrikeStorm.FixedAlpha(alpha); DarkRingAlpha = StrikeStorm.FixedAlpha(alpha * 0.5 * ink.Amount);
        RingCount = ink.Amount >= 0.01 ? 2 : 1; ArcCount = n;
        for (var j = 0; j < n; j++) { var start = burst.Rotation + 0.35 * e + j * span + gap / 2; Arcs[j] = new(burst.X, burst.Y, radius, start, start + span - gap); }
        if (u < 0.27) Wash(r0 + 3.5 * sk, 0.55 * Math.Pow(1 - u / 0.27, 2));
        if (u < 0.6) Wash(r0 + 5 * g.K, 0.34 * Math.Pow(1 - u / 0.6, 2));
        var m = 3 + burst.N % 2; var e2 = 1 - (1 - u) * (1 - u); var ga = Math.Pow(1 - u, 2) * 0.95;
        GlintAlpha = StrikeStorm.FixedAlpha(ga); InkGlintAlpha = StrikeStorm.FixedAlpha(ga * 0.55 * ink.Amount); GlintCount = m;
        for (var j = 0; j < m; j++)
        {
            var angle = burst.Rotation + j * 6.283185 / m + (Strike.Hash(burst.N, j) - 0.5) * 0.9;
            var distance = r0 + (2.5 + 4 * Strike.Hash(burst.N, j + 7)) * sk * e2;
            var size = (0.6 + 0.45 * Strike.Hash(burst.N, j + 13)) * (1 - 0.5 * u) * Math.Max(0.8, g.K);
            Glints[j] = new(burst.X + distance * Math.Cos(angle), burst.Y + distance * Math.Sin(angle), size);
        }
        void Wash(double r, double a) { if (a >= 0.006) Washes[WashCount++] = new(0, Math.Min(1, a), burst.X, burst.Y, r, 1, default, 0, 1); }
    }
}
