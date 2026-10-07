namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Dynamic, Persp (Perspective) and Focus are both collections' premium options; null takes the wallpaper's own default.</summary>
public sealed record WallpaperSelection(WallpaperKind Kind = WallpaperKind.None, string? Id = null,
    string? Pack = null, double Speed = 1, IReadOnlyDictionary<string, double>? Options = null,
    bool? Dynamic = null, bool? Persp = null, double? Focus = null);
