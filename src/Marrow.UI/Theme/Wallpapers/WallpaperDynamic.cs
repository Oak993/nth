namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Two sets of four looks (index 0 dawn 06:00, 1 day 12:00, 2 dusk 18:00, 3 night 24:00) and the four-key light track in p units.</summary>
public sealed record WallpaperDynamic(WallpaperLookKey[] Light, WallpaperLookKey[] Dark, double[][] Sun);
