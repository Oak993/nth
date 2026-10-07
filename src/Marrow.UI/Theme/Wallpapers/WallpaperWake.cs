namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The message that wakes the compositor handler's renderer: the clock crossed a minute, or the accent changed.</summary>
internal sealed class WallpaperWake
{
    public static readonly WallpaperWake Instance = new();
    private WallpaperWake() { }
}
