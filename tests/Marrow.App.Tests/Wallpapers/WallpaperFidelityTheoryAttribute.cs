namespace Marrow.App.Tests.Wallpapers;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WallpaperFidelityTheoryAttribute : TheoryAttribute
{
    public WallpaperFidelityTheoryAttribute(string collection, bool premium = false)
    {
        // Finding 6: a closed dependency gate must fail a test, never hide its cases as skips.
        // Render performs the binding check and reports the reason for every affected wallpaper.
        _ = collection; _ = premium;
    }
}
