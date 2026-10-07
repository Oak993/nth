namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Perspective's drive from the window (OPTIONS-FORMAT 3): window centre minus screen centre, over half the screen size, clamped to -1..1.</summary>
public static class WallpaperTilt
{
    public static (double X, double Y) From(double windowCentreX, double windowCentreY, double screenWidth, double screenHeight)
    {
        if (!(screenWidth > 0) || !(screenHeight > 0) || !double.IsFinite(windowCentreX) || !double.IsFinite(windowCentreY)) return (0, 0);
        return (Math.Clamp((windowCentreX - screenWidth / 2) / (screenWidth / 2), -1, 1), Math.Clamp((windowCentreY - screenHeight / 2) / (screenHeight / 2), -1, 1));
    }
}
