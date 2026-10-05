using System.Text.Json;
using Marrow.Testing;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-3")]
[Trait("Feature", "WL-4")]
public sealed class WallpaperReconcileTests
{
    [Theory]
    [InlineData(WallpaperKind.Static, "riptide", "accent")]
    [InlineData(WallpaperKind.Animated, "borealis", "match")]
    public void Both_collections_use_one_persisted_choice_schema_and_the_same_environment(WallpaperKind kind, string id, string pack)
    {
        var defaults = new InMemorySettingsStore();
        var store = new WallpaperLibraryStore(defaults);
        store.Selection = new(kind, id, pack, Dynamic: true, Persp: false, Focus: .37);
        store.Selection = new();
        var remembered = new WallpaperLibraryStore(defaults).Recall(kind, id);
        Assert.True(remembered.Dynamic); Assert.False(remembered.Persp); Assert.Equal(.37, remembered.Focus);
        Assert.Null(defaults.Object("wallpaperLibraryStatic"));
        using var choices = JsonDocument.Parse((string)defaults.Object("wallpaperLibraryChoices")!);
        var choice = choices.RootElement.GetProperty($"{kind.ToString().ToLowerInvariant()}/{id}");
        Assert.True(choice.GetProperty("dynamic").GetBoolean());
        Assert.False(choice.GetProperty("options").TryGetProperty("dynamic", out _));
        var clock = new ManualTimeProvider();
        using var renderer = new WallpaperRenderer(remembered, true, new WallpaperEnvironment(clock, new FixedWallpaperAccent("#D4501A"), new FixedWallpaperPosition()));
        using var surface = SKSurface.Create(new SKImageInfo(32, 20)); renderer.Draw(surface.Canvas, 32, 20);
        clock.Advance(TimeSpan.FromHours(6)); renderer.Wake(); Assert.True(renderer.NeedsFrames);
    }

    [Fact]
    public void A_legacy_static_choice_migrates_to_the_shared_schema_without_an_active_wallpaper()
    {
        var defaults = new InMemorySettingsStore();
        defaults.Set("wallpaperLibraryStatic", """{"riptide":{"kind":"static","id":"riptide","pack":"accent","options":{"dynamic":1,"persp":0,"focus":0.37}}}""");
        var store = new WallpaperLibraryStore(defaults);
        var pick = store.Recall(WallpaperKind.Static, "riptide");
        Assert.True(pick.Dynamic); Assert.False(pick.Persp); Assert.Equal(.37, pick.Focus);
        store.Selection = pick;
        Assert.NotNull(defaults.Object("wallpaperLibraryChoices"));
    }

    [Fact]
    public void Every_migrated_or_remembered_choice_is_canonical_before_a_sibling_is_written()
    {
        var defaults = new InMemorySettingsStore();
        defaults.Set("wallpaperLibraryStatic", """{"riptide":{"kind":"static","id":"riptide","options":{"focus":0.37}},"saltflat":{"kind":"static","id":"saltflat","options":{"dynamic":1,"focus":0.4}}}""");
        defaults.Set("wallpaperLibraryChoices", """{"static/riptide":{"kind":"static","id":"riptide","focus":0.21,"options":{"focus":0.8}},"animated/medusa":{"kind":"animated","id":"borealis"}}""");
        var store = new WallpaperLibraryStore(defaults);
        Assert.Equal(.21, store.Recall(WallpaperKind.Static, "riptide").Focus);
        Assert.Equal("medusa", store.Recall(WallpaperKind.Animated, "medusa").Id);
        store.Selection = new(WallpaperKind.Animated, "borealis");
        using var choices = JsonDocument.Parse((string)defaults.Object("wallpaperLibraryChoices")!);
        foreach (var row in choices.RootElement.EnumerateObject())
        {
            var value = row.Value;
            Assert.Equal(row.Name, $"{value.GetProperty("kind").GetString()}/{value.GetProperty("id").GetString()}");
            foreach (var alias in new[] { "dynamic", "persp", "focus" })
                Assert.False(value.GetProperty("options").TryGetProperty(alias, out _));
        }
        Assert.True(choices.RootElement.GetProperty("static/saltflat").GetProperty("dynamic").GetBoolean());
    }

    [Theory]
    [InlineData(WallpaperKind.Static)]
    [InlineData(WallpaperKind.Animated)]
    public void Dynamic_and_match_keep_dark_grounds_out_of_the_brown_band_across_the_collection(WallpaperKind kind)
    {
        var failures = new List<string>();
        foreach (var wall in WallpaperCatalogue.All.Where(w => w.Kind == kind))
        foreach (var accent in new[] { "#005FB8", "#D4501A", "#B88A00", "#107C10", "#5D6A7B" })
        foreach (var pack in wall.Packs.Append(kind == WallpaperKind.Static ? StaticWallpaperPalette.MatchAccent(wall.Static!, accent) : WallpaperMatchPack.Build(wall, accent)))
        foreach (var hour in Enumerable.Range(0, 96).Select(i => i / 4.0))
        {
            var pal = kind == WallpaperKind.Static ? StaticWallpaperPalette.Dynamic(wall.Static!, WallpaperPalette.For(pack, 1), 1, hour, 1).Palette
                : WallpaperTimeOfDay.PaletteFor(wall, pack, 1, hour).Palette;
            var l = pal[15]; var a = pal[16]; var b = pal[17];
            var h = (Math.Atan2(b, a) * 180 / Math.PI + 360) % 360;
            if (l < .46 && Math.Sqrt(a * a + b * b) > .01 && h > 20 && h < 120) failures.Add($"{wall.Id}/{pack.Id}/{accent}@{hour}");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures.Take(20)));
    }
}
