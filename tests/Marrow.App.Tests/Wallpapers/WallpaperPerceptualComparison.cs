using SkiaSharp;

namespace Marrow.App.Tests.Wallpapers;

// ED-NEW-codex-wl-5 item 3: unit-scale OKLab Euclidean distance, never a percent or RGB average.
public static class WallpaperPerceptualComparison
{
    public const double MeanLimit = 0.010;
    public const double PixelLimit = 0.080;

    public static (double Mean, double Max, int X, int Y) Measure(SKBitmap reference, SKBitmap actual)
    {
        if (reference.Width != actual.Width || reference.Height != actual.Height) throw new InvalidDataException("Wallpaper frame dimensions differ");
        double sum = 0, max = 0;
        int maxX = 0, maxY = 0;
        for (var y = 0; y < reference.Height; y++)
            for (var x = 0; x < reference.Width; x++)
            {
                var a = reference.GetPixel(x, y);
                var b = actual.GetPixel(x, y);
                if (a.Alpha != 255 || b.Alpha != 255) throw new InvalidDataException(FormattableString.Invariant($"Non-opaque wallpaper pixel at ({x},{y})"));
                var delta = Delta(a, b);
                sum += delta;
                if (delta > max) { max = delta; maxX = x; maxY = y; }
            }
        return (sum / (reference.Width * reference.Height), max, maxX, maxY);
    }

    public static bool Within(double mean, double max) => double.IsFinite(mean) && double.IsFinite(max) && mean >= 0 && max >= mean && mean <= MeanLimit && max <= PixelLimit;

    public static double Delta(SKColor a, SKColor b)
    {
        var p = Lab(a);
        var q = Lab(b);
        return Math.Sqrt(Math.Pow(p.L - q.L, 2) + Math.Pow(p.A - q.A, 2) + Math.Pow(p.B - q.B, 2));
    }

    private static (double L, double A, double B) Lab(SKColor c)
    {
        var r = Linear(c.Red); var g = Linear(c.Green); var b = Linear(c.Blue);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
