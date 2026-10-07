namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The premium options data of an animated wallpaper: roles of the six slots (Ld, Cd, Ll, Cl), Dynamic looks, Perspective, Focus, Match accent.</summary>
public sealed record WallpaperPremium(double[][] Roles, WallpaperDynamic Dynamic, WallpaperPerspective Perspective, double FocusDefault, bool DynamicDefault, WallpaperMatch Match);
