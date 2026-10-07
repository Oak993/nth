namespace Marrow.UI.Theme.Wallpapers;

internal sealed class WallpaperDelegateAccent(Func<string> read) : IWallpaperAccentSource
{
    public string Hex => read();
    // Delegate inputs are polled only on explicit refresh; production uses the event source.
    public event Action? Changed { add { } remove { } }
}
