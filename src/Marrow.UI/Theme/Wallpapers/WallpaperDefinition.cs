namespace Marrow.UI.Theme.Wallpapers;

public sealed record WallpaperDefinition(WallpaperKind Kind, string Id, string Name, string Tier, string Desc,
    WallpaperPack[] Packs, WallpaperOption[] Options, double Period, string Source, string DefaultPack, WallpaperPremium? Premium = null, StaticWallpaperData? Static = null);
