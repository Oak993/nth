// PORT-ONLY, owner-ruled addition (OD-0106 Countdown C, its amendments and its OKLCH glide; OD-0099 Q-V-6 B; first-release voice typing, VT-17): the glide's colour space (Ottosson, 2020; CSS Color 4's hue rules).
namespace Marrow.App.Port.Voice.Countdown;

/// <summary>
/// sRGB to OKLab and back, and the glide between two colours: lightness and chroma linear and the hue along the shorter arc, so a hue turns cleanly with even lightness and
/// no grey or brown midpoint. A nearly colourless end (chroma under 0.02: its hue is not meaningful) glides along the straight line in OKLab to the other colour, which is
/// the same path with the colourless end taking the other's hue, and is exactly the colour itself at the start.
/// </summary>
internal static class Oklch
{
    private const double Colourless = 0.02;

    internal readonly record struct Lab(double L, double A, double B)
    {
        internal double Chroma => Math.Sqrt(A * A + B * B);

        internal double Hue => Math.Atan2(B, A) * 180 / Math.PI;
    }

    internal static Lab FromSrgb(double r, double g, double b)
    {
        double lr = Decode(r), lg = Decode(g), lb = Decode(b);
        var l = Math.Cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
        var m = Math.Cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
        var s = Math.Cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);
        return new Lab(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s, 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>
    /// The colour as sRGB. A colour outside sRGB (a brightened red) keeps its lightness and hue and gives up chroma until it fits (the largest chroma that does), instead of
    /// being clamped per channel, which would shift its hue and flatten the lift.
    /// </summary>
    internal static (double R, double G, double B) ToSrgb(Lab lab)
    {
        var (r, g, b) = Linear(lab);
        if (!Fits(r, g, b))
        {
            double low = 0, high = 1;
            for (var i = 0; i < 40; i++)
            {
                var mid = (low + high) / 2;
                var (mr, mg, mb) = Linear(lab with { A = lab.A * mid, B = lab.B * mid });
                if (Fits(mr, mg, mb)) low = mid; else high = mid;
            }
            (r, g, b) = Linear(lab with { A = lab.A * low, B = lab.B * low });
        }
        return (Encode(r), Encode(g), Encode(b));
    }

    private static bool Fits(double r, double g, double b) => r is >= -1e-9 and <= 1 + 1e-9 && g is >= -1e-9 and <= 1 + 1e-9 && b is >= -1e-9 and <= 1 + 1e-9;

    private static (double R, double G, double B) Linear(Lab lab)
    {
        var l = Math.Pow(lab.L + 0.3963377774 * lab.A + 0.2158037573 * lab.B, 3);
        var m = Math.Pow(lab.L - 0.1055613458 * lab.A - 0.0638541728 * lab.B, 3);
        var s = Math.Pow(lab.L - 0.0894841775 * lab.A - 1.2914855480 * lab.B, 3);
        return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    /// <summary>The colour p of the way from <paramref name="from"/> to <paramref name="to"/> (p 0 to 1; 0 is exactly <paramref name="from"/>, 1 exactly <paramref name="to"/>).</summary>
    internal static Lab Glide(Lab from, Lab to, double p)
    {
        var l = from.L + (to.L - from.L) * p;
        if (from.Chroma < Colourless || to.Chroma < Colourless) return new Lab(l, from.A + (to.A - from.A) * p, from.B + (to.B - from.B) * p);
        var chroma = from.Chroma + (to.Chroma - from.Chroma) * p;
        var delta = ((to.Hue - from.Hue + 540) % 360) - 180;
        var hue = (from.Hue + delta * p) * Math.PI / 180;
        return new Lab(l, chroma * Math.Cos(hue), chroma * Math.Sin(hue));
    }

    private static double Decode(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double Encode(double linear)
    {
        var v = Math.Clamp(linear, 0, 1);
        return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
    }
}
