namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Lo and Hi are the design page's end words for the option's slider (animated Motion: "Calm" and "Lively"); null where the data has none.</summary>
public sealed record WallpaperOption(string Id, string Label, double Default, double Min, double Max, string? Lo = null, string? Hi = null);
