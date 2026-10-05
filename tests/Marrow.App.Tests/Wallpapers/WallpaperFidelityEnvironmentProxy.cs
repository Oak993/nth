using System.Reflection;

namespace Marrow.App.Tests.Wallpapers;

// Implements only WL-3's public environment getters before their interface types land on staging.
// This supplies fixed test inputs; the actual WallpaperRenderer still performs every render.
public class WallpaperFidelityEnvironmentProxy : DispatchProxy
{
    public object Value { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name is "get_Hex" or "get_Tilt"
        ? Value : throw new InvalidOperationException("Unsupported wallpaper environment member: " + targetMethod?.Name);
}
