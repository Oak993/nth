using Xunit.Abstractions;

namespace Marrow.App.Tests.Wallpapers;

public sealed class StaticWallpaperFidelityTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Wallpapers() => WallpaperReferences.Wallpapers("static");

    [WallpaperFidelityTheory("static")]
    [MemberData(nameof(Wallpapers))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_static_wallpaper_matches_its_approved_pack_references(string wallpaper) =>
        WallpaperFidelityAssertions.Compare("static", wallpaper, false, output);

    [WallpaperFidelityTheory("static", true)]
    [MemberData(nameof(Wallpapers))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_static_wallpaper_matches_dusk_accent_and_full_focus(string wallpaper) =>
        WallpaperFidelityAssertions.Compare("static", wallpaper, true, output);
}
