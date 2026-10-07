namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Focus (OPTIONS-FORMAT 4): a Gaussian of sigma 0.035 * H * focus^1.4 pixels, blurred only above 0.35 px.</summary>
public static class WallpaperFocus
{
    public const double MinSigma = 0.35;
    public static double Sigma(double height, double focus) => focus > 0 ? 0.035 * height * Math.Pow(focus, 1.4) : 0;
}
