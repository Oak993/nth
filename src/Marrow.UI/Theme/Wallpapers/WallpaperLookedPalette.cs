namespace Marrow.UI.Theme.Wallpapers;

/// <summary>A palette (OKLab x 6) after the time-of-day look, with the light's offset in p units for <c>u_sun</c>.</summary>
public readonly record struct WallpaperLookedPalette(float[] Palette, (double X, double Y) Sun);
