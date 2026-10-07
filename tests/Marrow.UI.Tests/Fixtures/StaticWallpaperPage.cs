using System.Text.Json;
using System.Globalization;

namespace Marrow.UI.Tests.Fixtures;

internal static class StaticWallpaperPage
{
    private static string Root
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "Marrow.sln"))) return d.FullName;
            throw new InvalidOperationException("Repository fixture root is missing.");
        }
    }
    public static JsonElement Golden { get; } = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,
        "tests/Marrow.UI.Tests/Fixtures/static-wallpaper-page.json"))).RootElement.Clone();
    public static byte[] SnapshotBytes { get; } = File.ReadAllBytes(Path.Combine(Root,
        "porting/first-release/design-src/wallpaper-library/static/wallpapers.json"));
    public static JsonElement Snapshot { get; } = JsonDocument.Parse(SnapshotBytes).RootElement.Clone();

    public static bool IsUnambiguouslyWarmGround(string hex)
    {
        var n = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var r = (n >> 16) & 255; var g = (n >> 8) & 255; var b = n & 255;
        static bool Warm(double r, double g, double b)
        {
            static double Linear(double x) { x = Math.Clamp(x / 255, 0, 1); return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
            r = Linear(r); g = Linear(g); b = Linear(b);
            var l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
            var m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
            var s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
            var lightness = .2104542553 * l + .793617785 * m - .0040720468 * s;
            var a = 1.9779984951 * l - 2.428592205 * m + .4505937099 * s;
            var bb = .0259040371 * l + .7827717662 * m - .808675766 * s;
            var hue = (Math.Atan2(bb, a) * 180 / Math.PI + 360) % 360;
            return lightness < .46 && hue > 20 && hue < 120 && Math.Sqrt(a * a + bb * bb) > .01;
        }
        if (!Warm(r, g, b)) return false;
        return new[] { -.5, .5 }.All(dr => new[] { -.5, .5 }.All(dg => new[] { -.5, .5 }.All(db => Warm(r + dr, g + dg, b + db))));
    }
}
