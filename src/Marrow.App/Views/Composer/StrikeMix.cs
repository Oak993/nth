namespace Marrow.App.Views.Composer;

public static class StrikeMix
{
    private static double H(int k, int seed, int salt) => Strike.Hash(k, unchecked(seed * 13 + salt));

    // strike-engine.js:352; all Mix styles use this rhythm, including A2.
    public static double StormAt(int k, int seed) => k * 2.4 + 0.45 + 0.38 * (2 * H(k, seed, 5) - 1) + 0.35 * Strike.Noise(k / 4.3, unchecked(seed + 71));

    private static int RareRaw(int k, int seed)
    {
        if (H(k, seed, 201) >= 0.067) return 0;
        var u = H(k, seed, 202);
        return u < 0.28 ? 1 : u < 0.56 ? 2 : u < 0.78 ? 3 : 4;
    }

    public static int Rare(int k, int seed) => (k & 1) == 0 ? RareRaw(k, seed) :
        RareRaw(unchecked(k - 1), seed) == 0 && RareRaw(unchecked(k + 1), seed) == 0 ? RareRaw(k, seed) : 0;

    private static bool A2Raw(int k, int seed) => Rare(k, seed) == 0 && H(k, seed, 203) < 0.3;

    public static bool A2(int k, int seed) => (k & 1) == 0 ? A2Raw(k, seed) :
        A2Raw(k, seed) && !A2Raw(unchecked(k - 1), seed) && !A2Raw(unchecked(k + 1), seed);

    public static int Style(int k, int seed) => Rare(k, seed) != 0 ? 3 : A2(k, seed) ? 0 : H(k, seed, 204) < 0.45 ? 1 : 2;

    private static int FamilyRaw(int k, int seed) => (int)Math.Floor(6 * H(k, seed, 90));

    public static int Family(int k, int seed)
    {
        if ((k & 1) == 0) return FamilyRaw(k, seed);
        var a = FamilyRaw(unchecked(k - 1), seed); var b = FamilyRaw(unchecked(k + 1), seed);
        var count = a == b ? 5 : 4;
        var selected = (int)Math.Floor(H(k, seed, 91) * count);
        for (var f = 0; f < 6; f++) if (f != a && f != b && selected-- == 0) return f;
        throw new InvalidOperationException("Mix family selection is outside its candidate set.");
    }

    // strike-engine.js:337, :460-534. Four value slots avoid a flicker array per plan.
    public static StrikeTiming Timing(int k, int seed, bool twin = false)
    {
        var style = Style(k, seed); var ev = style == 3 ? Rare(k, seed) : 0;
        double Hs(int salt) => H(k, seed, salt);
        var family = style == 0 ? -1 : Family(k, seed);
        var partial = style >= 2 && ev == 0 && Hs(61) < 1d / 7;
        var reach = partial ? 0.35 + 0.33 * Hs(62) : 0.9;
        var strength = style >= 2 ? 0.75 + 0.35 * Hs(63) : 1;
        var lead = style >= 2 ? 0.09 + 0.13 * Hs(64) : 0.14;
        var steps = style >= 2 ? 5 + (int)Math.Floor(5 * Hs(65)) : 7;
        var charge = ev == 3 ? 0 : 0.14 + 0.12 * Hs(131);
        StrikeFlicker f0, f1 = default, f2 = default, f3 = default;
        int count;
        if (style < 2)
        {
            f0 = new(0, 0.05, 1); f1 = new(0.075, 0.04, 0.6);
            count = Strike.Hash(k, unchecked(seed + 9)) > 0.45 ? 3 : 2;
            if (count == 3) f2 = new(0.15, 0.055, 0.85);
        }
        else
        {
            var r = Hs(66); count = r < 0.14 ? 1 : r < 0.5 ? 2 : r < 0.85 ? 3 : 4;
            f0 = new(0, 0.04 + 0.025 * Hs(67), 1);
            double at = 0;
            for (var j = 1; j < count; j++)
            {
                at += 0.05 + 0.07 * Hs(68 + 3 * j);
                var flicker = new StrikeFlicker(at, 0.035 + 0.03 * Hs(69 + 3 * j), 0.45 + 0.45 * Hs(70 + 3 * j));
                if (j == 1) f1 = flicker; else if (j == 2) f2 = flicker; else f3 = flicker;
            }
        }
        var onset = StormAt(k, seed);
        if (twin)
        {
            if (ev != 2) throw new ArgumentException("This Mix strike has no twin.", nameof(twin));
            onset += 0.12 + 0.13 * Hs(110);
            strength *= 0.8 + 0.15 * Hs(111);
            family = (family + 2 + (int)Math.Floor(3 * Hs(114))) % 6;
            f0 = new(0, 0.045, 1); f1 = new(0.06 + 0.04 * Hs(113), 0.04, 0.55); f2 = f3 = default;
            count = Hs(112) < 0.5 ? 1 : 2; ev = 0; charge = 0; lead = 0.06; steps = 3; reach = 0.9; partial = false;
        }
        return new StrikeTiming(k, onset, style, ev, family, partial, reach, strength, lead, steps, charge, count, f0, f1, f2, f3, twin);
    }

    public static StrikeSpark? Spark(int k, int seed)
    {
        if (H(k, seed, 120) >= 0.5) return null;
        var p = Timing(k, seed); var next = Timing(unchecked(k + 1), seed);
        var at = p.At + 1 + 0.4 * H(k, seed, 121);
        return new StrikeSpark(at, at + 0.12 > next.At - next.Lead - next.Charge - 0.05);
    }
}
