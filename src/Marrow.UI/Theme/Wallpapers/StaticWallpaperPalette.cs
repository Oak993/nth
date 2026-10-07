namespace Marrow.UI.Theme.Wallpapers;

/// <summary>sw-color.js and sw-gl.body.js colour half, with the same gamut and hex quantization.</summary>
public static class StaticWallpaperPalette
{
    public const string AccentPack = "accent";
    private static readonly string[] keys = ["night", "dawn", "day", "dusk"];
    private static readonly double[] nightness = [1, .45, 0, .5];
    public static double Smooth(double t) => t * t * (3 - 2 * t);

    public static StaticWallpaperLook LookAt(StaticWallpaperData data, double hours)
    {
        var t = ((hours % 24) + 24) % 24;
        var i = (int)Math.Floor(t / 6); var f = Smooth((t - i * 6) / 6);
        var a = data.Data.GetProperty("dyn").GetProperty(keys[i]);
        var b = data.Data.GetProperty("dyn").GetProperty(keys[(i + 1) % 4]);
        double Mix(string k) => a.GetProperty(k).GetDouble() + (b.GetProperty(k).GetDouble() - a.GetProperty(k).GetDouble()) * f;
        var la = a.TryGetProperty("light", out var al) ? StaticWallpaperData.Numbers(al) : data.Light;
        var lb = b.TryGetProperty("light", out var bl) ? StaticWallpaperData.Numbers(bl) : data.Light;
        return new(Mix("l"), Mix("c"), Mix("h"), la[0] + (lb[0] - la[0]) * f, la[1] + (lb[1] - la[1]) * f,
            nightness[i] + (nightness[(i + 1) % 4] - nightness[i]) * f);
    }

    public static StaticWallpaperLook HomeLook(StaticWallpaperData data, double dark)
    {
        var d = LookAt(data, 12); var n = LookAt(data, 0);
        return new(d.L + (n.L - d.L) * dark, d.C + (n.C - d.C) * dark, d.H + (n.H - d.H) * dark,
            d.X + (n.X - d.X) * dark, d.Y + (n.Y - d.Y) * dark, dark);
    }

    public static StaticWallpaperState Dynamic(StaticWallpaperData data, float[] palette, double dark, double hours, double strength)
    {
        if (!(strength > .0005)) return new(palette, dark, (double[])data.Light.Clone());
        var a = LookAt(data, hours); var h = HomeLook(data, dark);
        var dl = (a.L - h.L) * strength; var cr = Math.Pow(a.C / Math.Max(.2, h.C), strength);
        var dh = (a.H - h.H) * strength; var result = new float[18];
        for (var i = 0; i < 6; i++)
        {
            var offset = i * 3; var aa = palette[offset + 1]; var bb = palette[offset + 2];
            var c = Math.Sqrt((double)aa * aa + (double)bb * bb);
            var hue = (Math.Atan2(bb, aa) * 180 / Math.PI + 360) % 360;
            var l = palette[offset] + dl; var chroma = c * cr; var shiftedHue = hue + dh;
            if (dark > .5) shiftedHue = WallpaperGroundHue.Resolve(l, chroma, shiftedHue);
            var lab = LchToLab(l, chroma, shiftedHue, 22);
            for (var j = 0; j < 3; j++) result[offset + j] = (float)lab[j];
        }
        return new(result, Math.Clamp(dark + (a.Night - h.Night) * strength, 0, 1),
            [data.Light[0] + (a.X - h.X) * strength, data.Light[1] + (a.Y - h.Y) * strength]);
    }

    public static WallpaperPack MatchAccent(StaticWallpaperData data, string hex)
    {
        var a = WallpaperPalette.Lab(hex); var chroma = Math.Sqrt(a[1] * a[1] + a[2] * a[2]);
        var hue = (Math.Atan2(a[2], a[1]) * 180 / Math.PI + 360) % 360;
        double[] Side(int mode)
        {
            var m = 0; var best = 9.0;
            for (var i = 0; i < 5; i++)
            {
                var d = Math.Abs(data.Roles[i][mode * 2] - a[0]);
                if (d < best) { best = d; m = i; }
            }
            var hues = mode == 0 ? data.DarkHues : data.LightHues;
            return data.Roles.Select((_, i) =>
            {
                var delta = ((hues[i % hues.Length] - hues[m % hues.Length]) % 360 + 540) % 360 - 180;
                var offset = Math.Clamp(delta * .5, -60, 60);
                return ((hue + offset) % 360 + 360) % 360;
            }).ToArray();
        }
        return ResolvePack(data.Roles, Side(0), Side(1), Math.Clamp(chroma / .16, .3, 1.15));
    }

    public static WallpaperPack ResolvePack(double[][] roles, double[] hues, double[]? lightHues = null, double chromaScale = 1)
    {
        var dark = new string[6]; var light = new string[6]; lightHues ??= hues;
        for (var i = 0; i < 6; i++)
        {
            var r = roles[i]; var hue = hues[i % hues.Length]; var hw = ((hue % 360) + 360) % 360;
            var c = r[1] * chromaScale; var hd = hue;
            if (r[0] < .46 && hw > 20 && hw < 120) hd = c < .05 ? 262 : hw <= 78 ? 8 + (hw - 20) * .2 : 172;
            else if (r[0] < .7 && c < .085 && hw > 20 && hw < 120) hd = 318;
            dark[i] = LchToHex(r[0], c, hd);
            light[i] = LchToHex(r[2], r[3] * chromaScale, lightHues[i % lightHues.Length]);
        }
        return new(AccentPack, "Match accent", dark, light);
    }

    private static double[] Linear(double l, double a, double b)
    {
        var ll = l + .3963377774 * a + .2158037573 * b;
        var m = l - .1055613458 * a - .0638541728 * b;
        var s = l - .0894841775 * a - 1.291485548 * b;
        ll = ll * ll * ll; m = m * m * m; s = s * s * s;
        return [4.0767416621 * ll - 3.3077115913 * m + .2309699292 * s,
            -1.2684380046 * ll + 2.6097574011 * m - .3413193965 * s,
            -.0041960863 * ll - .7034186147 * m + 1.707614701 * s];
    }

    private static double[] LchToLab(double lightness, double chroma, double hue, int steps)
    {
        lightness = Math.Clamp(lightness, .001, .999);
        var radians = hue * Math.PI / 180; var ca = Math.Cos(radians); var sa = Math.Sin(radians);
        var lo = 0.0; var hi = Math.Max(0, chroma);
        bool Fits(double c) => Linear(lightness, c * ca, c * sa).All(x => x >= -1e-5 && x <= 1 + 1e-5);
        if (!Fits(hi))
        {
            for (var i = 0; i < steps; i++) { var mid = (lo + hi) / 2; if (Fits(mid)) lo = mid; else hi = mid; }
            hi = lo;
        }
        return [lightness, hi * ca, hi * sa];
    }

    private static string LchToHex(double l, double c, double h)
    {
        var lab = LchToLab(l, c, h, 24); var rgb = Linear(lab[0], lab[1], lab[2]);
        static int Encode(double x)
        {
            x = Math.Clamp(x, 0, 1); var s = x <= .0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - .055;
            return (int)Math.Floor(Math.Clamp(s, 0, 1) * 255 + .5);
        }
        return FormattableString.Invariant($"#{Encode(rgb[0]):x2}{Encode(rgb[1]):x2}{Encode(rgb[2]):x2}");
    }
}
