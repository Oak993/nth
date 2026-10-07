using Avalonia;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Host seams: local civil time, raw Windows accent, and normalized window position (y down).</summary>
public sealed record StaticWallpaperInputs(TimeProvider Time, Func<string> AccentReader, Func<Vector> WindowPosition)
    : WallpaperEnvironment(Time, AccentReader, WindowPosition)
{
    // The JSON's preview accent is also the standalone CPU renderer's default. The view supplies Windows values.
    public static StaticWallpaperInputs Default { get; } = new(TimeProvider.System, () => "#005FB8", () => Vector.Zero);
}
