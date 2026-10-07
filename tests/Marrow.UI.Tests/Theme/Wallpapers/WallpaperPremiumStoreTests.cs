using Marrow.Testing;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperPremiumStoreTests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Dynamic_perspective_focus_and_the_match_pack_persist_and_reload()
    {
        var defaults = new InMemorySettingsStore(); var store = new WallpaperLibraryStore(defaults);
        store.Selection = new(WallpaperKind.Animated, "borealis", "match", 1.5, Dynamic: true, Persp: true, Focus: 0.4);
        var reload = new WallpaperLibraryStore(defaults).Selection;
        Assert.Equal("match", reload.Pack); Assert.True(reload.Dynamic); Assert.True(reload.Persp); Assert.Equal(0.4, reload.Focus); Assert.Equal(1.5, reload.Speed);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Unset_choices_take_the_wallpapers_own_defaults_and_bad_ones_are_repaired()
    {
        var layered = WallpaperCatalogue.All.First(w => w.Kind == WallpaperKind.Animated && w.Premium!.Perspective.Default);
        var flat = WallpaperCatalogue.All.First(w => w.Kind == WallpaperKind.Animated && !w.Premium!.Perspective.Default);
        var a = WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, layered.Id)); var b = WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, flat.Id));
        Assert.True(a.Persp); Assert.False(b.Persp); Assert.False(a.Dynamic); Assert.Equal(layered.Premium!.FocusDefault, a.Focus);
        Assert.Equal(1, WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, flat.Id, Focus: 7)).Focus);
        Assert.Equal(0, WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, flat.Id, Focus: -2)).Focus);
        Assert.Equal(flat.Premium!.FocusDefault, WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, flat.Id, Focus: double.NaN)).Focus);
        Assert.Equal(flat.DefaultPack, WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, flat.Id, "no-such-pack")).Pack);
        Assert.True(WallpaperLibraryStore.Normalize(new(WallpaperKind.Static, "riptide", Dynamic: true, Persp: true, Focus: 1)).Dynamic); // Finding 1: both collections have premium choices.
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Each_wallpaper_remembers_its_own_choices_when_another_is_picked()
    {
        var defaults = new InMemorySettingsStore(); var store = new WallpaperLibraryStore(defaults);
        store.Selection = new(WallpaperKind.Animated, "borealis", "match", Dynamic: true, Persp: true, Focus: 0.4, Speed: 2);
        store.Selection = new(WallpaperKind.Animated, "molten", Dynamic: false, Focus: 0.1);
        store.Selection = new(WallpaperKind.Static, "riptide");
        var reload = new WallpaperLibraryStore(defaults);
        var borealis = reload.Recall(WallpaperKind.Animated, "borealis"); var molten = reload.Recall(WallpaperKind.Animated, "molten");
        Assert.Equal("match", borealis.Pack); Assert.True(borealis.Dynamic); Assert.True(borealis.Persp); Assert.Equal(0.4, borealis.Focus); Assert.Equal(2, borealis.Speed);
        Assert.False(molten.Dynamic); Assert.Equal(0.1, molten.Focus);
        Same(WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, "lagoon")), reload.Recall(WallpaperKind.Animated, "lagoon"));
        Assert.Equal(WallpaperKind.Static, reload.Selection.Kind);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Malformed_remembered_choices_fall_back_to_defaults_without_throwing()
    {
        var defaults = new InMemorySettingsStore(); defaults.Set("wallpaperLibraryChoices", "{ not json");
        var store = new WallpaperLibraryStore(defaults);
        Same(WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, "borealis")), store.Recall(WallpaperKind.Animated, "borealis"));
        Assert.Equal(WallpaperKind.None, store.Recall(WallpaperKind.None, null).Kind);
    }

    // a selection holds its options in a dictionary, which a record compares by reference: compare the values
    private static void Same(WallpaperSelection expected, WallpaperSelection actual)
    {
        Assert.Equal(expected with { Options = null }, actual with { Options = null });
        Assert.Equal(expected.Options!.OrderBy(o => o.Key), actual.Options!.OrderBy(o => o.Key));
    }
}
