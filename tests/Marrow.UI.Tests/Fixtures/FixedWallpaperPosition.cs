using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>A window-position seam a test sets by hand: the tilt the window's place on the screen would give.</summary>
internal sealed class FixedWallpaperPosition : IWallpaperWindowPosition
{
    public (double X, double Y) Tilt { get; set; }
}
