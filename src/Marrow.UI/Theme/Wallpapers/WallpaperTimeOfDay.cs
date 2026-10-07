namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Dynamic (OPTIONS-FORMAT 2), a line-for-line port of the snapshot page's look, applyLook and palFor (lib-page.js).</summary>
public static class WallpaperTimeOfDay
{
    private const double Deg = Math.PI / 180;
    // night 00:00 to dawn, dawn to day, day to dusk, dusk to night; keys at 06:00 dawn, 12:00 day, 18:00 dusk, 24:00 night
    private static readonly int[][] Segments = [[3, 0], [0, 1], [1, 2], [2, 3]];

    /// <summary>The look at a local hour (0..24, cyclic) and a darkness (0 light .. 1 dark): the smoothstep blend of the two neighbouring keys.</summary>
    public static WallpaperLook Look(WallpaperDynamic d, double hour, double dark)
    {
        var c = ((hour % 24) + 24) % 24; var seg = Segments[Math.Min(3, (int)Math.Floor(c / 6))];
        var u = c % 6 / 6; var s = u * u * (3 - 2 * u);
        double Lin(WallpaperLookKey[] set, Func<WallpaperLookKey, double[]> f, int i) => (1 - s) * f(set[seg[0]])[i] + s * f(set[seg[1]])[i];
        double Log(WallpaperLookKey[] set, int i) => Math.Exp((1 - s) * Math.Log(set[seg[0]].KC[i]) + s * Math.Log(set[seg[1]].KC[i]));
        var dl = new double[6]; var kc = new double[6]; var dh = new double[6];
        for (var i = 0; i < 6; i++)
        {
            dl[i] = (1 - dark) * Lin(d.Light, k => k.DL, i) + dark * Lin(d.Dark, k => k.DL, i);
            kc[i] = Math.Pow(Log(d.Light, i), 1 - dark) * Math.Pow(Log(d.Dark, i), dark);
            dh[i] = (1 - dark) * Lin(d.Light, k => k.DH, i) + dark * Lin(d.Dark, k => k.DH, i);
        }
        (double X, double Y) Tint(WallpaperLookKey[] set)
        {
            var a = set[seg[0]]; var b = set[seg[1]];
            return ((1 - s) * a.TintAmount * Math.Cos(a.TintHue * Deg) + s * b.TintAmount * Math.Cos(b.TintHue * Deg),
                (1 - s) * a.TintAmount * Math.Sin(a.TintHue * Deg) + s * b.TintAmount * Math.Sin(b.TintHue * Deg));
        }
        var tl = Tint(d.Light); var td = Tint(d.Dark); var sa = d.Sun[seg[0]]; var sb = d.Sun[seg[1]];
        return new(dl, kc, dh, (1 - dark) * tl.X + dark * td.X, (1 - dark) * tl.Y + dark * td.Y, (1 - s) * sa[0] + s * sb[0], (1 - s) * sa[1] + s * sb[1]);
    }

    /// <summary>
    /// The palette after a look: the tint mixes each colour toward a colour of the tint hue (chroma 0.12) in the a/b plane, as a photo
    /// filter does, and gives back 60% of the chroma that took away; then chroma is multiplied, hue turned and lightness added.
    /// </summary>
    public static float[] Apply(float[] palette, WallpaperLook look, double dark = 0)
    {
        var result = new float[18]; var amount = Math.Min(1, double.Hypot(look.TintX, look.TintY));
        var tintHue = Math.Atan2(look.TintY, look.TintX); var ta = 0.12 * Math.Cos(tintHue); var tb = 0.12 * Math.Sin(tintHue);
        for (var i = 0; i < 6; i++)
        {
            double a = palette[i * 3 + 1], b = palette[i * 3 + 2];
            var c0 = double.Hypot(a, b) * look.KC[i];
            if (amount > 1e-6) { a += amount * (ta - a); b += amount * (tb - b); }
            var mixed = double.Hypot(a, b); var chroma = mixed + 0.6 * Math.Max(0, c0 - mixed);
            var l = palette[i * 3] + look.DL[i]; var hue = Math.Atan2(b, a) / Deg + look.DH[i];
            if (dark > .5) hue = WallpaperGroundHue.Resolve(l, chroma, hue);
            var f = WallpaperOklch.Fit(l, chroma, hue * Deg);
            result[i * 3] = (float)f.L; result[i * 3 + 1] = (float)f.A; result[i * 3 + 2] = (float)f.B;
        }
        return result;
    }

    /// <summary>The palette a pack shows at a darkness, with the look at a local hour (null: Dynamic off, the plain pack) and the light's offset.</summary>
    public static WallpaperLookedPalette PaletteFor(WallpaperDefinition wall, WallpaperPack pack, double dark, double? hour)
    {
        var palette = WallpaperPalette.For(pack, dark);
        if (hour is not { } h || wall.Premium is null) return new(palette, (0, 0));
        var look = Look(wall.Premium.Dynamic, h, dark);
        return new(Apply(palette, look, dark), (look.SunX, look.SunY));
    }
}
