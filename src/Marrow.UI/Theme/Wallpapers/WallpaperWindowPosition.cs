namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The live window position: the UI thread updates it when the window moves, the render thread reads the tilt.</summary>
public sealed class WallpaperWindowPosition : IWallpaperWindowPosition
{
    private (double X, double Y) tilt;
    private readonly object gate = new();

    public (double X, double Y) Tilt { get { lock (gate) return tilt; } }

    /// <summary>The window's centre and the screen's size, in the same units, with the screen's top-left corner as the origin.</summary>
    public void Update(double windowCentreX, double windowCentreY, double screenWidth, double screenHeight)
    {
        var next = WallpaperTilt.From(windowCentreX, windowCentreY, screenWidth, screenHeight);
        lock (gate) tilt = next;
    }
}
