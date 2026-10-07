namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Where Match accent reads the Windows accent colour: "#rrggbb". Read on the render thread, so an implementation must be thread-safe.</summary>
public interface IWallpaperAccentSource
{
    string Hex { get; }

    /// <summary>Raised, on any thread, when <see cref="Hex"/> has changed to a different colour.</summary>
    event Action? Changed;
}
