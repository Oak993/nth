// PORT-ONLY, owner-ruled addition (OD-0150; ED-NEW-lightv2-6): "Wallpaper dim near composer". While a composer light is on
// and a wallpaper shows, a soft dark veil in the composer's shape lies over the wallpaper and under the light, fading out over
// about the light's spread, so the light shines out and the wallpaper's own colours do not mix into it (the owner, 2026-10-11).
// Its amount is worked out from the wallpaper under the composer: brighter and busier wallpapers are dimmed more, dark and calm
// ones less; the strength slider scales that amount (50 %, the default, is the amount itself).

namespace Marrow.Core.Models.ComposerLights;

/// <summary>The wallpaper the composer sits on, as the light's host knows it.</summary>
/// <param name="Showing">A wallpaper (a picture or a library wallpaper) is the window's backdrop.</param>
/// <param name="Animated">It moves (an animated library wallpaper): it is measured again every <see cref="WallpaperDim.AnimatedRemeasureSeconds"/>.</param>
/// <param name="Revision">Changes when the wallpaper or the window's size changes: it is measured again.</param>
public readonly record struct WallpaperState(bool Showing = false, bool Animated = false, int Revision = 0);

/// <summary>The veil's amount, from the wallpaper's measure and the settings.</summary>
public static class WallpaperDim
{
    /// <summary>The amount used until the wallpaper under the composer has been measured.</summary>
    public const double Unmeasured = .3;

    /// <summary>The most the veil darkens (its alpha), whatever the strength.</summary>
    public const double Most = .85;

    /// <summary>An animated wallpaper is measured again this often (s).</summary>
    public const double AnimatedRemeasureSeconds = 3;

    /// <summary>The measuring grid (the region under the composer, scaled down to it).</summary>
    public const int GridWidth = 48, GridHeight = 16;

    private static double Smooth(double a, double b, double x) { var t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>
    /// The Auto amount (the veil's alpha at strength 50 %) for a wallpaper of mean relative <paramref name="luminance"/> (0..1)
    /// and <paramref name="busyness"/> (the mean step between neighbouring pixels' encoded luma, 0..1): .08 on a dark, calm
    /// wallpaper, up to .6 on a bright, busy one.
    /// </summary>
    public static double Auto(double luminance, double busyness) =>
        Math.Clamp(.08 + .32 * Smooth(.04, .55, luminance) + .2 * Smooth(.015, .12, busyness), 0, .6);

    /// <summary>The veil's alpha: the Auto amount scaled by the strength (50 % is the amount itself), or 0 with no light, no wallpaper, or the setting Off.</summary>
    public static double Target(ComposerLightSettings settings, WallpaperState wallpaper, double auto)
    {
        if (settings.Style == ComposerLightStyle.Off || !wallpaper.Showing || settings.WallpaperDim == WallpaperDimMode.Off) return 0;
        return Math.Min(Most, Math.Max(0, auto) * settings.WallpaperDimStrength / ComposerLightSettings.DefaultWallpaperDimStrength);
    }

    /// <summary>
    /// The mean relative luminance and the busyness of an RGBA (8 bits a channel, premultiplied, rows of <paramref name="width"/>)
    /// image: the mean of each pixel's linear luminance, and the mean absolute step of the encoded luma to the right and below.
    /// </summary>
    public static (double Luminance, double Busyness) Measure(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4) return (0, 0);
        var luma = new double[width * height];
        double sum = 0;
        for (var i = 0; i < width * height; i++)
        {
            double a = rgba[i * 4 + 3] / 255.0;
            if (a <= 0) { luma[i] = 0; continue; }
            double r = rgba[i * 4] / 255.0 / a, g = rgba[i * 4 + 1] / 255.0 / a, b = rgba[i * 4 + 2] / 255.0 / a;
            sum += (.2126 * Math.Pow(Math.Min(r, 1), 2.2) + .7152 * Math.Pow(Math.Min(g, 1), 2.2) + .0722 * Math.Pow(Math.Min(b, 1), 2.2)) * a;
            luma[i] = (.2126 * r + .7152 * g + .0722 * b) * a;
        }
        double steps = 0; var count = 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var v = luma[y * width + x];
                if (x + 1 < width) { steps += Math.Abs(luma[y * width + x + 1] - v); count++; }
                if (y + 1 < height) { steps += Math.Abs(luma[(y + 1) * width + x] - v); count++; }
            }
        return (sum / (width * height), count == 0 ? 0 : steps / count);
    }
}
