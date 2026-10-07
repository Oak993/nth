using Avalonia;

namespace Marrow.UI.Theme.Wallpapers;

internal sealed class WallpaperDelegatePosition(Func<Vector> read) : IWallpaperWindowPosition
{
    public (double X, double Y) Tilt { get { var p = read(); return (p.X, p.Y); } }
}
