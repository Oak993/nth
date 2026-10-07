// PORT-ONLY: multi-account step 3a (OD-0114..0119, multi-account.html "the usage light"). ONE colour scale for every account: a value per 1 percent of what is left
// (101 colours per theme), interpolated in OKLab between the anchors 100 percent emerald, 50 percent teal-blue, 25 percent amber and 5 percent soft red. Between teal-blue
// and amber the path is lifted in lightness. There are no bands and no brand colours.
namespace Marrow.Core.Port.MultiAccount.Capsule;

public static class UsageLight
{
    private static readonly string[] LightAnchors = ["0E9F7A", "1B86B6", "C27A0E", "C9473B"];
    private static readonly string[] DarkAnchors = ["3AD6A8", "4FB6E8", "F0A93C", "F0695A"];
    private static readonly double[] At = [1, 0.5, 0.25, 0.05];
    private static readonly (byte R, byte G, byte B)[] Light = Ramp(LightAnchors, 0.04);
    private static readonly (byte R, byte G, byte B)[] Dark = Ramp(DarkAnchors, 0.06);

    /// <summary>The colour for what is left of the account (0...1), on the nearest percent. Below 5 percent it is the 5 percent colour.</summary>
    public static (byte R, byte G, byte B) Color(double left, bool dark)
    {
        var percent = double.IsNaN(left) ? 100 : (int)Math.Round(Math.Clamp(left, 0, 1) * 100, MidpointRounding.AwayFromZero);
        return (dark ? Dark : Light)[percent];
    }

    /// <summary>Mixes two colours in OKLab, <paramref name="share"/> of <paramref name="from"/> (CSS <c>color-mix(in oklab, from share, to)</c>): the aurora's and the glow's second colours.</summary>
    public static (byte R, byte G, byte B) Mix((byte R, byte G, byte B) from, (byte R, byte G, byte B) to, double share)
    {
        var a = ToLab(from.R, from.G, from.B);
        var b = ToLab(to.R, to.G, to.B);
        var s = Math.Clamp(share, 0, 1);
        return FromLab(a[0] * s + b[0] * (1 - s), a[1] * s + b[1] * (1 - s), a[2] * s + b[2] * (1 - s));
    }

    private static (byte, byte, byte)[] Ramp(string[] anchors, double lift)
    {
        var p = anchors.Select(ToLab).ToArray();
        var output = new (byte, byte, byte)[101];
        for (var k = 0; k <= 100; k++)
        {
            var x = Math.Max(At[3], k / 100.0);
            var i = 0;
            while (i < 2 && x < At[i + 1]) i++;
            var t = (At[i] - x) / (At[i] - At[i + 1]);
            var a = p[i];
            var b = p[i + 1];
            var up = i == 1 ? lift * Math.Sin(Math.PI * t) : 0;
            output[k] = FromLab(a[0] + (b[0] - a[0]) * t + up, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t);
        }
        return output;
    }

    private static double Lin(double c) { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }

    private static byte Gam(double c)
    {
        var v = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Floor(Math.Max(0, Math.Min(1, v)) * 255 + 0.5);
    }

    private static double[] ToLab(string hex)
    {
        var n = Convert.ToInt32(hex, 16);
        return ToLab((byte)((n >> 16) & 255), (byte)((n >> 8) & 255), (byte)(n & 255));
    }

    private static double[] ToLab(byte red, byte green, byte blue)
    {
        double r = Lin(red), g = Lin(green), b = Lin(blue);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var q = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return [0.2104542553 * l + 0.793617785 * m - 0.0040720468 * q, 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * q, 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * q];
    }

    private static (byte, byte, byte) FromLab(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        var q = Math.Pow(lightness - 0.0894841775 * a - 1.291485548 * b, 3);
        return (Gam(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * q), Gam(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * q), Gam(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * q));
    }
}
