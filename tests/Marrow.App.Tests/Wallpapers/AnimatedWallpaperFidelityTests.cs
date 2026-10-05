using Xunit.Abstractions;

namespace Marrow.App.Tests.Wallpapers;

public sealed class AnimatedWallpaperFidelityTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Wallpapers() => WallpaperReferences.Wallpapers("animated");

    [WallpaperFidelityTheory("animated")]
    [MemberData(nameof(Wallpapers))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_animated_wallpaper_matches_its_approved_pack_and_phase_references(string wallpaper) =>
        WallpaperFidelityAssertions.Compare("animated", wallpaper, false, output);

    [WallpaperFidelityTheory("animated", true)]
    [MemberData(nameof(Wallpapers))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_animated_wallpaper_matches_dusk_accent_and_full_focus(string wallpaper) =>
        WallpaperFidelityAssertions.Compare("animated", wallpaper, true, output);
}
