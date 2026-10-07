namespace Marrow.UI.Theme.Wallpapers;

/// <summary>ED-397: reapply the approved static resolvePack no-brown rule after Dynamic.</summary>
internal static class WallpaperGroundHue
{
    public static double Resolve(double lightness, double chroma, double hue)
    {
        var h = ((hue % 360) + 360) % 360;
        return lightness < .46 && h > 20 && h < 120
            ? chroma < .05 ? 262 : h <= 78 ? 8 + (h - 20) * .2 : 172
            : hue;
    }
}
