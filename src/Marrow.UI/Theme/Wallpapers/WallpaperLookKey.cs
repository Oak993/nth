namespace Marrow.UI.Theme.Wallpapers;

/// <summary>One key look of the Dynamic option (OPTIONS-FORMAT 2): six lightness offsets, chroma factors and hue turns, and a tint (hue, amount).</summary>
public sealed record WallpaperLookKey(double[] DL, double[] KC, double[] DH, double TintHue, double TintAmount);
