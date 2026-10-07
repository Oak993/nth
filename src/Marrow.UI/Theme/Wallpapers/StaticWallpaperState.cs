using Avalonia;

namespace Marrow.UI.Theme.Wallpapers;

public sealed record StaticWallpaperState(float[] Palette, double Dark, double[] Light,
    Vector Tilt = default, double Focus = 0, double Sigma = 0, double Dream = 0);
