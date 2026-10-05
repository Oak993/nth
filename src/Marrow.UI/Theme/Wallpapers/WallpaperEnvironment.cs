using Avalonia;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>What a wallpaper reads from outside itself: the local clock (Dynamic), the Windows accent (Match accent) and the window position (Perspective).</summary>
public record WallpaperEnvironment(TimeProvider Clock, IWallpaperAccentSource Accent, IWallpaperWindowPosition Position)
{
    // WL-4 compatibility adapter; all consumers still receive the one environment seam.
    public WallpaperEnvironment(TimeProvider clock, Func<string> accent, Func<Vector> windowPosition)
        : this(clock, new WallpaperDelegateAccent(accent), new WallpaperDelegatePosition(windowPosition)) { }

    /// <summary>The real thing: the system clock, the Windows accent, and a window position of its own (a window feeds it).</summary>
    public static WallpaperEnvironment Create() => new(TimeProvider.System, SystemWallpaperAccent.Shared, new WallpaperWindowPosition());
}
