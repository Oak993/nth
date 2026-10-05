namespace Marrow.UI.Theme.Wallpapers;

public sealed record WallpaperSelection(WallpaperKind Kind = WallpaperKind.None, string? Id = null,
    string? Pack = null, double Speed = 1, IReadOnlyDictionary<string, double>? Options = null);
