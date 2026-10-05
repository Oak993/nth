using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>An accent seam a test sets by hand; setting a different value raises <see cref="Changed"/> as the system accent does.</summary>
internal sealed class FixedWallpaperAccent(string hex) : IWallpaperAccentSource
{
    private string current = hex;
    public event Action? Changed;
    public string Hex
    {
        get => current;
        set { if (current == value) return; current = value; Changed?.Invoke(); }
    }
}
