namespace Marrow.App.Tests.Fixtures;

// VT-17 test fixture: sRGB <-> OKLCH written out independently of the port (Ottosson, 2020; CSS Color 4 for the hue rules), so the countdown's colours are compared to
// a second derivation, not to themselves.
internal static class OklchReference
{
    internal static (double L, double C, double H) FromSrgb(double r, double g, double b)
    {
        static double Lin(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        double lr = Lin(r), lg = Lin(g), lb = Lin(b);
        var l = Math.Cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
        var m = Math.Cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
        var s = Math.Cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);
        var L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        var a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        var bb = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        var h = Math.Atan2(bb, a) * 180 / Math.PI;
        return (L, Math.Sqrt(a * a + bb * bb), h < 0 ? h + 360 : h);
    }

    /// <summary>The sRGB of an OKLCH colour; one outside sRGB keeps its lightness and hue and its chroma is reduced to the largest that fits (CSS Color 4's gamut mapping, by bisection).</summary>
    internal static (double R, double G, double B) ToSrgb(double L, double C, double H)
    {
        static (double R, double G, double B) Linear(double L, double C, double H)
        {
            var a = C * Math.Cos(H * Math.PI / 180);
            var b = C * Math.Sin(H * Math.PI / 180);
            var l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
            var m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
            var s = Math.Pow(L - 0.0894841775 * a - 1.2914855480 * b, 3);
            return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s, -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
        }
        static bool In((double R, double G, double B) c) => new[] { c.R, c.G, c.B }.All(v => v is >= -1e-9 and <= 1 + 1e-9);
        static double Enc(double v)
        {
            v = Math.Clamp(v, 0, 1);
            return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        }
        var rgb = Linear(L, C, H);
        if (!In(rgb))
        {
            double lo = 0, hi = C;
            for (var i = 0; i < 60; i++)
            {
                var mid = (lo + hi) / 2;
                if (In(Linear(L, mid, H))) lo = mid; else hi = mid;
            }
            rgb = Linear(L, lo, H);
        }
        return (Enc(rgb.R), Enc(rgb.G), Enc(rgb.B));
    }

    /// <summary>
    /// The interpolated OKLCH of two colours at p: lightness and chroma linear and the hue along the shorter arc; when one end is nearly colourless (chroma under 0.02, whose
    /// hue is not meaningful) the path is the straight line between the two in Oklab, which is the same glide with the colourless end's hue taken from the other, and exact at p = 0.
    /// </summary>
    internal static (double L, double C, double H) Mix((double L, double C, double H) from, (double L, double C, double H) to, double p)
    {
        if (from.C < 0.02 || to.C < 0.02)
        {
            static (double A, double B) Ab((double L, double C, double H) c) => (c.C * Math.Cos(c.H * Math.PI / 180), c.C * Math.Sin(c.H * Math.PI / 180));
            var (a0, b0) = Ab(from);
            var (a1, b1) = Ab(to);
            var a = a0 + (a1 - a0) * p;
            var b = b0 + (b1 - b0) * p;
            var h = Math.Atan2(b, a) * 180 / Math.PI;
            return (from.L + (to.L - from.L) * p, Math.Sqrt(a * a + b * b), h < 0 ? h + 360 : h);
        }
        var delta = ((to.H - from.H + 540) % 360) - 180;
        return (from.L + (to.L - from.L) * p, from.C + (to.C - from.C) * p, (from.H + delta * p + 360) % 360);
    }
}
