namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Where Perspective reads the window's place on the screen, as a tilt of -1..1 on each axis. Read on the render thread, so an implementation must be thread-safe.</summary>
public interface IWallpaperWindowPosition
{
    (double X, double Y) Tilt { get; }
}
