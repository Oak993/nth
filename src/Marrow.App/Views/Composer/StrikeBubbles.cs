using System.Globalization;

namespace Marrow.App.Views.Composer;

// E:917-1010; SPEC K-128..K-136. The A2 public maths remain intact until the new lanes are wired at ST-3.
public sealed partial class StrikeBubbleLanes
{
    private readonly int seed;
    private readonly double width, height;
    private readonly StrikeBubbleLane[] lanes;
    private readonly StrikeBubbleLane[] history;
    private readonly int[] heads, kept;
    public int NormalCount { get; }
    public int NucleationCount { get; }
    public int Count => lanes.Length;
    public bool Simmer { get; }
    public int Restarts { get; private set; }

    public StrikeBubbleLanes(string configuration, double width, double height)
    {
        var p = configuration.Split(','); seed = int.Parse(p[0], CultureInfo.InvariantCulture); if (seed == 0) seed = 1;
        NormalCount = int.Parse(p[1], CultureInfo.InvariantCulture); NucleationCount = int.Parse(p[2], CultureInfo.InvariantCulture); Simmer = p[3] == "1";
        this.width = width; this.height = height;
        lanes = new StrikeBubbleLane[NormalCount + (Simmer ? NucleationCount : 0)]; history = new StrikeBubbleLane[lanes.Length * 16]; heads = new int[lanes.Length]; kept = new int[lanes.Length];
    }

    public static int Stride(int n)
    {
        static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
        if (Gcd(7, n) == 1) return 7; if (Gcd(5, n) == 1) return 5; if (Gcd(3, n) == 1) return 3; if (Gcd(2, n) == 1) return 2; return 1;
    }

    public StrikeBubbleRise Rise(int i, int k)
    {
        var nuc = i >= NormalCount; var j = nuc ? i - NormalCount : i; var n = paletteBound ? nuc ? PaletteNucleationCount : PaletteNormalCount : nuc ? NucleationCount : NormalCount;
        var x = unchecked((uint)((seed + (nuc ? 977 : 0)) * 73856093) ^ (uint)((j + 1) * 19349663) ^ (uint)((k + 7) * 83492791)); if (x == 0) x = 1;
        double R() { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x % 100000 / 100000d; }
        for (var w = 0; w < 3; w++) R();
        if (nuc)
        {
            var size = 1.2 + R(); var outer = size + 2; var up = height * (0.5 + R() * 0.45); var slot = j * Stride(n) % n;
            var at = R() < 0.2 ? 3 + R() * 92 : 3 + (slot + R()) / n * 92;
            var cx = width * at / 100 + outer / 2; var cy = height - outer / 2 + 1 - R() * height * 0.08;
            var dur = 1.5 + R() * 1.1; var o = 0.5 + R() * 0.3; var s0 = 0.4 + R() * 0.2; var s1 = 0.75 + R() * 0.2; var s2 = 0.9 + R() * 0.25;
            var x1 = (R() - 0.5) * 3; var y1 = -up * 0.3; var x2 = (R() - 0.5) * 4; var y2 = -up * 0.62; var x3 = (R() - 0.5) * 5; var y3 = -up; var gap = R() * 0.9;
            var wa = (0.5 + 0.6 * R()) * height / 40; var wn = 2 + 1.5 * R(); var wp = R();
            return new(cx, cy, outer, dur, o, s0, s1, s2, x1, y1, x2, y2, x3, y3, gap, wa, wn, wp, true);
        }
        else
        {
            var kind = R(); var size = kind < 0.6 ? 1.4 + R() * 1.6 : kind < 1 - paletteLargeShare ? 3 + R() * 2 : 5 + R() * 3; var up = height * (0.55 + R() * 0.8);
            var side = (R() - 0.5) * 2; var flow = R() < 0.25;
            var x1 = flow ? 14 + R() * 24 : side * (4 + R() * 10); var x2 = flow ? 30 + R() * 40 : side * (2 + R() * 14) + (R() - 0.5) * 10; var x3 = flow ? 50 + R() * 60 : (R() - 0.5) * 30;
            var y1 = -up * (0.2 + R() * 0.2); var y2 = -up * (0.45 + R() * 0.25); var y3 = -up; var dur = (flow ? 3.6 : 1.8) + R() * 3.2;
            var slot = i * Stride(n) % n; var at = R() < 0.2 ? 3 + R() * 92 : 3 + (slot + R()) / n * 92; var outer = size + 2;
            var cx = width * at / 100 - (flow ? x3 / 2 : 0) + outer / 2; var cy = height - (-6 + R() * height * 0.45) - outer / 2;
            dur *= paletteRiseScale;
            var o = 0.55 + R() * 0.4; var s0 = 0.35 + R() * 0.3; var s1 = 0.7 + R() * 0.3; var s2 = 0.9 + R() * 0.35; var gap = R() * palettePause;
            var wa = (0.45 + 0.6 * R()) * height / 40; var wn = 1.5 + 1.5 * R(); var wp = R();
            return new(cx, cy, outer, dur, o, s0, s1, s2, x1, y1, x2, y2, x3, y3, gap, wa, wn, wp, false);
        }
    }

    private StrikeBubbleLane Lane(int i, double time)
    {
        var current = lanes[i];
        if (current.Ready && time < current.Start)
        {
            for (var j = kept[i] - 1; j >= 0; j--) { var old = history[i * 16 + (heads[i] + j) % 16]; if (time >= old.Start) return old; }
            current = default; heads[i] = kept[i] = 0; Restarts++;
        }
        if (!current.Ready)
        {
            var first = Rise(i, 0); current = new(0, first, -Rise(i, 1000).Gap / 1.4 * first.Dur, true); lanes[i] = current;
            if (time < current.Start) return current with { K = -1 };
        }
        while (time >= current.Start + current.Rise.Dur + current.Rise.Gap)
        {
            if (kept[i] == 16) { heads[i] = (heads[i] + 1) % 16; kept[i]--; }
            history[i * 16 + (heads[i] + kept[i]++) % 16] = current;
            current = new(current.K + 1, Rise(i, current.K + 1), current.Start + current.Rise.Dur + current.Rise.Gap, true);
        }
        lanes[i] = current; return current;
    }

    public StrikeBubbleNow Now(int i, double time, double fast)
    {
        if (paletteBound && (i < NormalCount ? i >= PaletteNormalCount : i-NormalCount >= PaletteNucleationCount)) return default;
        var lane = Lane(i, time); var b = lane.Rise; var p = (time - lane.Start) / b.Dur;
        if (p < 0 || p >= 1) return new(lane.K, 0, 0, 0, 0, 0, b.Outer);
        double tx, ty, scale;
        if (p < 0.38) { var q = Strike.Ease(p / 0.38); tx = b.X1 * q; ty = b.Y1 * q; scale = b.S0 + (b.S1 - b.S0) * q; }
        else if (p < 0.7) { var q = Strike.Ease((p - 0.38) / 0.32); tx = b.X1 + (b.X2 - b.X1) * q; ty = b.Y1 + (b.Y2 - b.Y1) * q; scale = b.S1; }
        else { var q = Strike.Ease((p - 0.7) / 0.3); tx = b.X2 + (b.X3 - b.X2) * q; ty = b.Y2 + (b.Y3 - b.Y2) * q; scale = b.S1 + (b.S2 - b.S1) * q; }
        var alpha = p < 0.12 ? b.O * Strike.Ease(p / 0.12) : p < 0.7 ? b.O : b.O * (1 - Strike.Ease((p - 0.7) / 0.3));
        var f = Simmer ? fast : 0; if (f > 0) tx += f * b.Wa * Math.Sin(6.283185 * (p * b.Wn + b.Wp)); if (b.Nuc) alpha *= f;
        return new(lane.K, b.Cx + tx, b.Cy + ty, scale, b.Outer / 2 * scale, alpha, b.Outer);
    }
}
