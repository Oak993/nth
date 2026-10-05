using System.Globalization;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The snapshot page's gamut fit and OKLab to sRGB hex (lib-page.js labLin, inGamut, fitLab, lab2hex): chroma is lowered, never lightness or hue.</summary>
internal static class WallpaperOklch
{
    private static double Cube(double x) => x * x * x;

    private static (double R, double G, double B) Linear(double l, double a, double b)
    {
        var lp = Cube(l + 0.3963377774 * a + 0.2158037573 * b);
        var mp = Cube(l - 0.1055613458 * a - 0.0638541728 * b);
        var sp = Cube(l - 0.0894841775 * a - 1.291485548 * b);
        return (4.0767416621 * lp - 3.3077115913 * mp + 0.2309699292 * sp,
            -1.2684380046 * lp + 2.6097574011 * mp - 0.3413193965 * sp,
            -0.0041960863 * lp - 0.7034186147 * mp + 1.707614701 * sp);
    }

    private static bool InGamut(double l, double a, double b)
    {
        var (r, g, bl) = Linear(l, a, b);
        static bool Ok(double v) => v >= -0.0005 && v <= 1.0005;
        return Ok(r) && Ok(g) && Ok(bl);
    }

    /// <summary>The OKLab colour of lightness, chroma and hue (radians), with the chroma lowered until it is in the sRGB gamut.</summary>
    public static (double L, double A, double B) Fit(double lightness, double chroma, double hue)
    {
        var l = Math.Clamp(lightness, 0, 1); var c0 = Math.Max(0, chroma);
        var cos = Math.Cos(hue); var sin = Math.Sin(hue); var c = c0;
        if (!InGamut(l, c0 * cos, c0 * sin))
        {
            double lo = 0, hi = c0;
            for (var i = 0; i < 16; i++)
            {
                var mid = (lo + hi) / 2;
                if (InGamut(l, mid * cos, mid * sin)) lo = mid; else hi = mid;
            }
            c = lo;
        }
        return (l, c * cos, c * sin);
    }

    public static string Hex((double L, double A, double B) lab)
    {
        var (r, g, b) = Linear(lab.L, lab.A, lab.B);
        static string Channel(double v)
        {
            v = Math.Clamp(v, 0, 1); v = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return ((int)Math.Floor(v * 255 + 0.5)).ToString("x2", CultureInfo.InvariantCulture);
        }
        return "#" + Channel(r) + Channel(g) + Channel(b);
    }
}
