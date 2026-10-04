using Avalonia.Media;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// An independent Oklab mix toward white (Ottosson's published matrices, 2020; the values it gives are pinned against
/// E:/Droppy/work/scratch/effortslider2/oklab.py in SwiftColorPerceptualMixTests), for tests that need the colour SwiftUI's
/// Color.mix(with:by:) gives in its default perceptual space without reading it back from the port.
/// </summary>
public static class OklabReference
{
    public static Color MixWhite(Color colour, double by)
    {
        var (l0, a0, b0) = ToLab(colour.R / 255.0, colour.G / 255.0, colour.B / 255.0);
        var (l1, a1, b1) = ToLab(1, 1, 1);
        var (r, g, b) = FromLab(l0 + (l1 - l0) * by, a0 + (a1 - a0) * by, b0 + (b1 - b0) * by);
        return Color.FromArgb(colour.A, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    private static double Enc(double v) { v = Math.Clamp(v, 0, 1); return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055; }

    private static (double, double, double) ToLab(double r, double g, double b)
    {
        r = Lin(r); g = Lin(g); b = Lin(b);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s, 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s, 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static (double, double, double) FromLab(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
        return (Enc(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s), Enc(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s), Enc(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }
}
