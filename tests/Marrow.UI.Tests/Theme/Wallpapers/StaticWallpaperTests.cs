using System.Text.Json;
using Avalonia;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-4")]
public sealed class StaticWallpaperTests
{
    public static IEnumerable<object[]> Walls() => StaticWallpaperPage.Golden.GetProperty("rows").EnumerateArray()
        .Select(w => new object[] { w.GetProperty("id").GetString()! });

    [Theory, MemberData(nameof(Walls))]
    public void Every_approved_static_shader_compiles_and_rasters(string id)
    {
        var w = WallpaperCatalogue.Get(WallpaperKind.Static, id);
        using var effect = WallpaperCatalogue.Compile(w);
        using var renderer = new WallpaperRenderer(new(WallpaperKind.Static, id));
        Assert.Contains(Frame(renderer), b => b != 0);
        using var premium = new WallpaperRenderer(new(WallpaperKind.Static, id, "accent", Options:
            new Dictionary<string, double> { ["dynamic"] = 1, ["focus"] = 1, ["grain"] = 1, ["persp"] = 1 }), true,
            new(new ManualTimeProvider(), () => "#D4501A", () => new Vector(1, 1)));
        Assert.Contains(Frame(premium), b => b != 0);
    }

    [Fact]
    public void Embedded_static_snapshot_has_all_90_exact_names_packs_options_and_shaders()
    {
        var walls = WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Static).ToArray();
        Assert.Equal(90, walls.Length);
        Assert.Equal(90, walls.Select(w => w.Name).Distinct(StringComparer.Ordinal).Count());
        using var resource = typeof(WallpaperCatalogue).Assembly.GetManifestResourceStream("Marrow.Wallpapers.static.json");
        Assert.NotNull(resource); using var bytes = new MemoryStream(); resource.CopyTo(bytes);
        Assert.Equal(StaticWallpaperPage.SnapshotBytes, bytes.ToArray());
        foreach (var source in StaticWallpaperPage.Snapshot.GetProperty("wallpapers").EnumerateArray())
        {
            var wall = walls.Single(w => w.Id == source.GetProperty("id").GetString());
            Assert.Equal(source.GetProperty("name").GetString(), wall.Name);
            Assert.Equal(source.GetProperty("desc").GetString(), wall.Desc);
            Assert.Equal(source.GetProperty("tier").GetString(), wall.Tier);
            Assert.Equal(wall.Packs[source.GetProperty("defaultPack").GetInt32()].Id, wall.DefaultPack);
            Assert.Contains(source.GetProperty("src").GetString()!, wall.Source, StringComparison.Ordinal);
            Assert.Equal(source.GetProperty("dyn").GetRawText(), wall.Static!.Data.GetProperty("dyn").GetRawText());
            foreach (var key in new[] { "roles", "hues", "light", "persp", "focus" })
                Assert.Equal(source.GetProperty(key).GetRawText(), wall.Static.Data.GetProperty(key).GetRawText());
            foreach (var (option, key) in wall.Options.Zip(new[] { "grain", "option" }))
            {
                var expected = source.GetProperty(key);
                Assert.Equal(expected.GetProperty("label").GetString(), option.Label);
                Assert.Equal(expected.GetProperty("def").GetDouble(), option.Default);
                Assert.Equal(expected.GetProperty("min").GetDouble(), option.Min);
                Assert.Equal(expected.GetProperty("max").GetDouble(), option.Max);
            }
            Assert.Equal(5, wall.Packs.Length);
            foreach (var (pack, i) in wall.Packs.Select((p, i) => (p, i)))
            {
                var expected = source.GetProperty("packs")[i];
                Assert.Equal(expected.GetProperty("name").GetString(), pack.Name);
                Assert.Equal(Hexes(expected, "dark"), pack.Dark);
                Assert.Equal(Hexes(expected, "light"), pack.Light);
            }
        }
        foreach (var name in new[] { "Northlight", "Corolla", "Atoll", "Barchan", "Inkwash", "Flax", "Confluence", "Aureole" })
            Assert.Single(walls, w => w.Name == name);
    }

    [Theory, MemberData(nameof(Walls))]
    public void Dynamic_palette_darkness_and_light_match_page_key_hours_and_interpolation(string id)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Static, id);
        foreach (var row in Row(id).GetProperty("dynamic").EnumerateArray().Concat(Row(id).GetProperty("dynamicAccent").EnumerateArray()))
        {
            var dark = row.GetProperty("dark").GetBoolean();
            var pack = row.TryGetProperty("hex", out var accent) ? StaticWallpaperPalette.MatchAccent(wall.Static!, accent.GetString()!)
                : wall.Packs[row.GetProperty("pack").GetInt32()];
            var palette = WallpaperPalette.For(pack, dark ? 1 : 0);
            var state = StaticWallpaperPalette.Dynamic(wall.Static!, palette, dark ? 1 : 0, row.GetProperty("hour").GetDouble(), 1);
            Close(row.GetProperty("pal"), state.Palette, 2e-6);
            Assert.Equal(row.GetProperty("night").GetDouble(), state.Dark, 9);
            Close(row.GetProperty("light"), state.Light, 1e-9);
        }
    }

    [Theory, MemberData(nameof(Walls))]
    public void Match_accent_resolves_exact_page_hexes_for_five_accents(string id)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Static, id);
        foreach (var row in Row(id).GetProperty("accent").EnumerateArray())
        {
            var pack = StaticWallpaperPalette.MatchAccent(wall.Static!, row.GetProperty("hex").GetString()!);
            Assert.Equal(Hexes(row, "dark"), pack.Dark);
            Assert.Equal(Hexes(row, "light"), pack.Light);
        }
    }

    [Fact]
    public void Warm_dark_rule_matches_page_at_each_boundary()
    {
        foreach (var row in StaticWallpaperPage.Golden.GetProperty("warm").EnumerateArray())
        {
            var roles = row.GetProperty("roles").EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
            var pack = StaticWallpaperPalette.ResolvePack(roles, [row.GetProperty("h").GetDouble()]);
            Assert.Equal(Hexes(row, "dark"), pack.Dark);
            Assert.Equal(Hexes(row, "light"), pack.Light);
        }
    }

    [Fact]
    public void Dynamic_dark_ground_hue_band_classifications_match_the_approved_page()
    {
        static bool Warm(IReadOnlyList<double> lab)
        {
            var h = (Math.Atan2(lab[2], lab[1]) * 180 / Math.PI + 360) % 360;
            return lab[0] < .46 && h > 20 && h < 120 && Math.Sqrt(lab[1] * lab[1] + lab[2] * lab[2]) > .01;
        }
        var warm = new int[2]; var checkedGrounds = 0;
        foreach (var row in StaticWallpaperPage.Golden.GetProperty("rows").EnumerateArray())
        {
            var wall = WallpaperCatalogue.Get(WallpaperKind.Static, row.GetProperty("id").GetString()!);
            foreach (var sample in row.GetProperty("dynamic").EnumerateArray().Concat(row.GetProperty("dynamicAccent").EnumerateArray()).Where(r => r.GetProperty("dark").GetBoolean()))
            {
                var expected = sample.GetProperty("pal").EnumerateArray().Skip(15).Select(x => x.GetDouble()).ToArray();
                var accentPack = sample.TryGetProperty("hex", out var accent);
                var pack = accentPack ? StaticWallpaperPalette.MatchAccent(wall.Static!, accent.GetString()!) : wall.Packs[sample.GetProperty("pack").GetInt32()];
                var palette = WallpaperPalette.For(pack, 1);
                var actual = StaticWallpaperPalette.Dynamic(wall.Static!, palette, 1, sample.GetProperty("hour").GetDouble(), 1)
                    .Palette.Skip(15).Select(x => (double)x).ToArray();
                Assert.Equal(Warm(expected), Warm(actual));
                if (Warm(actual)) warm[accentPack ? 1 : 0]++; checkedGrounds++;
            }
        }
        Assert.Equal(5400, checkedGrounds);
        // Finding 3: the approved no-brown rule also applies after Dynamic. The derived page oracle applies the recorded patch.
        Assert.Equal(new[] { 0, 0 }, warm);
    }

    [Theory, MemberData(nameof(Walls))]
    public void No_authored_or_accent_dark_ground_is_brown(string id)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Static, id);
        var packs = wall.Packs.Concat(Row(id).GetProperty("accent").EnumerateArray()
            .Select(row => StaticWallpaperPalette.MatchAccent(wall.Static!, row.GetProperty("hex").GetString()!)));
        foreach (var p in packs)
        {
            // sw-color.js rounds each sRGB channel to 8 bits. A boundary red can land above 20 degrees:
            // reject a warm ground only when the centre and every half-step corner are in the forbidden band.
            Assert.False(StaticWallpaperPage.IsUnambiguouslyWarmGround(p.Dark[5]), $"{id}/{p.Id}: {p.Dark[5]}");
        }
    }

    [Fact]
    public void Injectable_clock_accent_and_window_position_change_only_the_static_inputs()
    {
        var clock = new ManualTimeProvider(); var accent = "#005FB8"; var tilt = new Vector(1, 1);
        var inputs = new StaticWallpaperInputs(clock, () => accent, () => tilt);
        var pick = new WallpaperSelection(WallpaperKind.Static, "riptide", "accent", Options: new Dictionary<string, double>
            { ["dynamic"] = 1, ["persp"] = 1 });
        using var r = new WallpaperRenderer(pick, true, inputs);
        var first = Frame(r); Assert.Equal(1, r.Renders);
        Assert.Equal(first, Frame(r)); Assert.Equal(1, r.Renders);
        Assert.InRange(r.StaticState!.Tilt.Length, .01049, .01051);
        Assert.Equal(.015 * .7 / Math.Sqrt(2), r.StaticState.Tilt.X, 9);
        Assert.Equal(-.015 * .7 / Math.Sqrt(2), r.StaticState.Tilt.Y, 9);
        r.ReduceMotion = true; r.RefreshStaticInputs(); r.Advance(.55);
        Frame(r); Assert.Equal(Vector.Zero, r.StaticState!.Tilt);
        clock.Advance(TimeSpan.FromHours(6)); r.RefreshStaticInputs(); Assert.True(r.NeedsFrames);
        r.Advance(.55); Assert.NotEqual(first, Frame(r));
        accent = "#C42B3A"; r.RefreshStaticInputs(); Assert.True(r.NeedsFrames); r.Advance(.55);
        var changed = Frame(r); Assert.NotEqual(first, changed);
        tilt = Vector.Zero; r.RefreshStaticInputs(); r.Advance(.55); Frame(r);
        Assert.False(r.NeedsFrames); var count = r.Renders; Frame(r); Assert.Equal(count, r.Renders);
    }

    [Fact]
    public void Premium_static_playback_requests_no_frames_at_rest()
    {
        var clock = new ManualTimeProvider();
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, "riptide", "accent", Options:
            new Dictionary<string, double> { ["dynamic"] = 1, ["focus"] = .7, ["persp"] = 1 }), true,
            new(clock, () => "#005FB8", () => Vector.Zero));
        var frames = new ManualFrameSource(); using var playback = new WallpaperPlayback(r, frames, () => { });
        Frame(r); playback.Refresh(); Assert.Equal(0, frames.Pending);
        frames.Advance(TimeSpan.FromHours(1)); Assert.Equal(0, frames.Pending); Assert.Equal(1, r.Renders);
        clock.Advance(TimeSpan.FromHours(6)); r.RefreshStaticInputs(); playback.Refresh(); Assert.Equal(1, frames.Pending);
        for (var i = 0; i < 40; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.Equal(1, frames.Pending); // page clock follower: .16 s, snap only within .004 hours
        for (var i = 0; i < 60; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.Equal(0, frames.Pending);
    }

    [Fact]
    public void Per_wallpaper_settings_roundtrip_all_six_static_choices_and_match_accent()
    {
        var defaults = new InMemorySettingsStore(); var store = new WallpaperLibraryStore(defaults);
        var a = new WallpaperSelection(WallpaperKind.Static, "riptide", "accent", Options: new Dictionary<string, double>
            { ["option"] = .23, ["grain"] = .37, ["focus"] = .61, ["dynamic"] = 1, ["persp"] = 0 });
        var b = new WallpaperSelection(WallpaperKind.Static, "knit", "Oatmilk", Options: new Dictionary<string, double>
            { ["option"] = .8, ["grain"] = .2, ["focus"] = 1, ["dynamic"] = 0, ["persp"] = 1 });
        store.Selection = a; store.Selection = b; store.Selection = new();
        var reload = new WallpaperLibraryStore(defaults);
        Assert.Equal(WallpaperKind.None, reload.Selection.Kind);
        reload.Select(WallpaperKind.Static, "riptide");
        Assert.Equal("accent", reload.Selection.Pack); Assert.Equal(a.Options, reload.Selection.Options);
        reload.Select(WallpaperKind.Static, "knit"); Assert.Equal(b.Options, reload.Selection.Options);
        Assert.Equal(b.Pack, reload.Selection.Pack);
    }

    [Theory, InlineData("riptide", false), InlineData("knit", true)]
    public void Focus_uses_source_sigma_dream_glow_or_own_hook_and_grain_changes_pixels(string id, bool own)
    {
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, id, Options: new Dictionary<string, double>
            { ["focus"] = 1, ["grain"] = 0 }));
        var focused = Frame(r); var s = r.StaticState!;
        Assert.Equal(own ? 1 : 0, s.Focus); Assert.Equal(own ? 0 : .022 * 20, s.Sigma, 9);
        Assert.Equal(own ? 0 : 1, s.Dream);
        using var sharp = new WallpaperRenderer(new(WallpaperKind.Static, id, Options: new Dictionary<string, double>
            { ["focus"] = 0, ["grain"] = 0 }));
        Assert.NotEqual(Frame(sharp), focused);
        r.Set(new(WallpaperKind.Static, id, Options: new Dictionary<string, double> { ["focus"] = 1, ["grain"] = 1 }), true);
        r.Advance(.55); Assert.NotEqual(focused, Frame(r));
    }

    private static JsonElement Row(string id) => StaticWallpaperPage.Golden.GetProperty("rows").EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
    private static string[] Hexes(JsonElement row, string key) => row.GetProperty(key).EnumerateArray().Select(x => x.GetString()!).ToArray();
    private static void Close(JsonElement expected, IEnumerable<double> actual, double tolerance)
    {
        var a = actual.ToArray(); Assert.Equal(expected.GetArrayLength(), a.Length);
        Assert.All(expected.EnumerateArray().Select(x => x.GetDouble()).Zip(a), pair => Assert.InRange(Math.Abs(pair.First - pair.Second), 0, tolerance));
    }
    private static void Close(JsonElement expected, IEnumerable<float> actual, double tolerance) => Close(expected, actual.Select(x => (double)x), tolerance);
    private static byte[] Frame(WallpaperRenderer renderer)
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        renderer.Draw(surface.Canvas, 32, 20); using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }
}
