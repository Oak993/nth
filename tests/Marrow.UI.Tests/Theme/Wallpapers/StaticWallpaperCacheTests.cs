using Avalonia;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-4")]
public sealed class StaticWallpaperCacheTests
{
    [Fact]
    public void Window_motion_during_pack_crossfade_does_not_restart_the_palette_timeline()
    {
        var position = Vector.Zero; var clock = new ManualTimeProvider();
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, "riptide", "Coral"), true,
            new(clock, () => "#005FB8", () => position));
        using var surface = SKSurface.Create(new SKImageInfo(32, 20)); r.Draw(surface.Canvas, 32, 20);
        r.Set(new(WallpaperKind.Static, "riptide", "Lagoon"), true);
        for (var i = 0; i < 34; i++)
        {
            position = new Vector(Math.Sin(i * .2), Math.Cos(i * .2));
            r.RefreshStaticInputs(); r.Advance(1.0 / 60); r.Draw(surface.Canvas, 32, 20);
        }
        var expected = WallpaperPalette.For(WallpaperCatalogue.Get(WallpaperKind.Static, "riptide").Packs.Single(p => p.Id == "Lagoon"), 1);
        Assert.Equal(expected, r.StaticState!.Palette);
        Assert.True(r.NeedsFrames); // tilt still follows the moving window
    }

    [Fact]
    public void Invalid_saved_static_entries_do_not_hide_valid_siblings_or_crash_startup()
    {
        var defaults = new InMemorySettingsStore();
        defaults.Set("wallpaperLibraryStatic", """
            {"riptide":null,"knit":{"kind":"static","id":"knit","pack":"Oatmilk","options":{"focus":0.37}}}
            """);
        var store = new WallpaperLibraryStore(defaults); store.Select(WallpaperKind.Static, "knit");
        Assert.Equal("Oatmilk", store.Selection.Pack); Assert.Equal(.37, store.Selection.Options!["focus"]);
        store.Select(WallpaperKind.Static, "riptide"); Assert.Equal("Coral", store.Selection.Pack);
    }

    [Fact]
    public void Disabled_options_ignore_their_seams_and_identical_inputs_leave_the_bake_idle()
    {
        var clock = new ManualTimeProvider(); var accent = "#005FB8"; var position = new Vector(1, 1);
        var selection = new WallpaperSelection(WallpaperKind.Static, "riptide", Options:
            new Dictionary<string, double> { ["dynamic"] = 0, ["persp"] = 0 });
        using var r = new WallpaperRenderer(selection, true, new(clock, () => accent, () => position));
        var frames = new ManualFrameSource(); using var p = new WallpaperPlayback(r, frames, () => { });
        using var surface = SKSurface.Create(new SKImageInfo(32, 20));
        r.Draw(surface.Canvas, 32, 20); Assert.Equal(1, r.Renders);
        clock.Advance(TimeSpan.FromHours(6)); accent = "#C42B3A"; position = Vector.Zero;
        r.RefreshStaticInputs(); r.Set(selection, true); p.Refresh(); r.Draw(surface.Canvas, 32, 20);
        Assert.Equal(1, r.Renders); Assert.Equal(0, frames.Pending); Assert.False(r.NeedsFrames);
        r.ReduceMotion = true; r.Draw(surface.Canvas, 32, 20);
        Assert.Equal(1, r.Renders); Assert.Equal(0, frames.Pending);
        r.Draw(surface.Canvas, 32, 21); Assert.Equal(2, r.Renders);
        r.Set(selection, false); p.Refresh(); Assert.Equal(1, frames.Pending);
        for (var i = 0; i < 40; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.Equal(1, frames.Pending); // theme follower: .16 s, snap within .002, independent of the palette's .55 s
        for (var i = 0; i < 60; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
        r.Draw(surface.Canvas, 32, 21); Assert.Equal(3, r.Renders); Assert.Equal(0, frames.Pending);
    }

    [Theory, MemberData(nameof(StaticWallpaperTests.Walls), MemberType = typeof(StaticWallpaperTests))]
    public void Every_static_wallpaper_retains_all_settings_across_restart(string id)
    {
        var defaults = new InMemorySettingsStore(); var store = new WallpaperLibraryStore(defaults);
        var expected = new Dictionary<string, double> { ["grain"] = .21, ["option"] = .72, ["focus"] = .37, ["dynamic"] = 1, ["persp"] = 0 };
        store.Selection = new(WallpaperKind.Static, id, "accent", Options: expected);
        store.Selection = new(); store = new(defaults); store.Select(WallpaperKind.Static, id);
        Assert.Equal("accent", store.Selection.Pack); Assert.Equal(expected, store.Selection.Options);
    }
}
