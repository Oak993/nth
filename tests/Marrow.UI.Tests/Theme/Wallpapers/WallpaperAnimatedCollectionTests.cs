using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperAnimatedCollectionTests
{
    public static IEnumerable<object[]> Animated() => WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Animated).Select(w => new object[] { w.Id });

    [Theory]
    [MemberData(nameof(Animated))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_one_of_the_72_animated_shaders_compiles_as_a_runtime_effect(string id)
    {
        using var effect = WallpaperCatalogue.Compile(WallpaperCatalogue.Get(WallpaperKind.Animated, id));
        Assert.NotNull(effect);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_animated_collection_is_72_wallpapers_24_to_a_tier_with_unique_ids()
    {
        var all = WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Animated).ToArray();
        Assert.Equal(72, all.Length); Assert.Equal(72, all.Select(w => w.Id).Distinct().Count());
        foreach (var tier in new[] { "vibrant", "balanced", "minimal" }) Assert.Equal(24, all.Count(w => w.Tier == tier));
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Every_animated_wallpaper_carries_its_roles_looks_perspective_focus_and_match_data()
    {
        foreach (var w in WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Animated))
        {
            var p = w.Premium; Assert.NotNull(p);
            Assert.Equal(6, p.Roles.Length); Assert.All(p.Roles, r => Assert.Equal(4, r.Length));
            Assert.Equal(4, p.Dynamic.Light.Length); Assert.Equal(4, p.Dynamic.Dark.Length); Assert.Equal(4, p.Dynamic.Sun.Length);
            foreach (var key in p.Dynamic.Light.Concat(p.Dynamic.Dark)) { Assert.Equal(6, key.DL.Length); Assert.Equal(6, key.KC.Length); Assert.Equal(6, key.DH.Length); }
            Assert.Contains(p.Perspective.Mode, new[] { "layers", "pan" });
            Assert.InRange(p.FocusDefault, 0, 0.6);
            Assert.Equal(6, p.Match.Off.Length); Assert.Equal(0, p.Match.Off[p.Match.Hero]);
        }
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Layered_wallpapers_default_perspective_on_and_flat_ones_off()
    {
        var all = WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Animated).ToArray();
        Assert.Equal(41, all.Count(w => w.Premium!.Perspective.Mode == "layers")); Assert.Equal(31, all.Count(w => w.Premium!.Perspective.Mode == "pan"));
        Assert.All(all, w => Assert.Equal(w.Premium!.Perspective.Mode == "layers", w.Premium.Perspective.Default));
    }
}
