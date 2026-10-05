namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The look at one time of day and theme (the page's <c>look</c>): per-slot offsets, the tint as an a/b vector, and the sun offset.</summary>
public sealed record WallpaperLook(double[] DL, double[] KC, double[] DH, double TintX, double TintY, double SunX, double SunY);
