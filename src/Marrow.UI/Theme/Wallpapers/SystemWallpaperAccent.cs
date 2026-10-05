namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The Windows accent as the UI thread last read it (AccentColor1 of the platform's colour values), for the render thread to read.</summary>
public sealed class SystemWallpaperAccent : IWallpaperAccentSource
{
    public static SystemWallpaperAccent Shared { get; } = new();
    private string hex = WallpaperMatchPack.FallbackAccent;
    public event Action? Changed;
    public string Hex => Volatile.Read(ref hex);

    public void Update(byte r, byte g, byte b)
    {
        var next = $"#{r:x2}{g:x2}{b:x2}";
        if (Interlocked.Exchange(ref hex, next) != next) Changed?.Invoke();
    }
}
