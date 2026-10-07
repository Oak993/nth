using Avalonia;
namespace Marrow.UI.Theme.Wallpapers;
internal sealed record WallpaperSnapshot(WallpaperSelection Selection, bool Dark, bool ReduceMotion, bool Visible, Size Size, TimeProvider Clock,
    WallpaperEnvironment? Environment = null);
