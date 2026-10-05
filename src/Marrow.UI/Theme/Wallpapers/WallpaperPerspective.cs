namespace Marrow.UI.Theme.Wallpapers;

/// <summary>OPTIONS-FORMAT 3: "layers" (depth, on by default) or "pan" (flat, off by default).</summary>
public sealed record WallpaperPerspective(string Mode, bool Default);
