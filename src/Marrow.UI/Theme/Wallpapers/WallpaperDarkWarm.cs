namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The dark_warm rule of Match accent (OPTIONS-FORMAT 5): a warm accent never gives a brown or olive dark ground.</summary>
public sealed record WallpaperDarkWarm(double[] Band, double GroundHue, double MidHue, double GroundBelow, double MidBelow, double MidAmount,
    double Kc, double Dl, double BrightDl, double BrightKc);
