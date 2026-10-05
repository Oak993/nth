using Marrow.UI.Theme.Wallpapers;
using Marrow.Testing;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperLibraryTests
{
    public static IEnumerable<object[]> Shaders() => new (WallpaperKind Kind, string Id)[]
    {
        (WallpaperKind.Static, "riptide"),
        (WallpaperKind.Static, "corolla"),
        (WallpaperKind.Static, "prism"),
        (WallpaperKind.Static, "solstice"),
        (WallpaperKind.Static, "northlight"),
        (WallpaperKind.Static, "satin"),
        (WallpaperKind.Static, "orbit"),
        (WallpaperKind.Static, "atoll"),
        (WallpaperKind.Static, "radiant"),
        (WallpaperKind.Static, "spectrum"),
        (WallpaperKind.Static, "meadow"),
        (WallpaperKind.Static, "barchan"),
        (WallpaperKind.Static, "frost"),
        (WallpaperKind.Static, "cascade"),
        (WallpaperKind.Static, "folio"),
        (WallpaperKind.Static, "ripple"),
        (WallpaperKind.Static, "contour"),
        (WallpaperKind.Static, "watercolor"),
        (WallpaperKind.Static, "arches"),
        (WallpaperKind.Static, "strata"),
        (WallpaperKind.Static, "flax"),
        (WallpaperKind.Static, "washi"),
        (WallpaperKind.Static, "dawn"),
        (WallpaperKind.Static, "arc"),
        (WallpaperKind.Static, "inkwash"),
        (WallpaperKind.Static, "pebble"),
        (WallpaperKind.Static, "aureole"),
        (WallpaperKind.Static, "confluence"),
        (WallpaperKind.Static, "afternoon"),
        (WallpaperKind.Static, "vellum"),
        (WallpaperKind.Animated, "borealis"),
        (WallpaperKind.Animated, "molten"),
        (WallpaperKind.Animated, "velvet"),
        (WallpaperKind.Animated, "spectra"),
        (WallpaperKind.Animated, "bloom"),
        (WallpaperKind.Animated, "nebula"),
        (WallpaperKind.Animated, "lagoon"),
        (WallpaperKind.Animated, "iris"),
        (WallpaperKind.Animated, "dunes"),
        (WallpaperKind.Animated, "tides"),
        (WallpaperKind.Animated, "afterglow"),
        (WallpaperKind.Animated, "inkwell"),
        (WallpaperKind.Animated, "fluted"),
        (WallpaperKind.Animated, "meridian"),
        (WallpaperKind.Animated, "nacre"),
        (WallpaperKind.Animated, "ribbon"),
        (WallpaperKind.Animated, "mist"),
        (WallpaperKind.Animated, "daybreak"),
        (WallpaperKind.Animated, "stillwater"),
        (WallpaperKind.Animated, "linen"),
        (WallpaperKind.Animated, "fields"),
        (WallpaperKind.Animated, "lumen"),
        (WallpaperKind.Animated, "drift"),
        (WallpaperKind.Animated, "halo"),
    }.Select(x => new object[] { x.Kind, x.Id });

    public static IEnumerable<object[]> Loops() => Shaders().Where(x => (WallpaperKind)x[0] == WallpaperKind.Animated)
        .SelectMany(x => Enumerable.Range(0, 5).SelectMany(pack => new[] { false, true }.Select(dark => new object[] { x[1], pack, dark })));

    [Theory]
    [MemberData(nameof(Shaders))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_shader_compiles_with_error_reported(WallpaperKind kind, string id)
    {
        using var effect = WallpaperCatalogue.Compile(WallpaperCatalogue.Get(kind, id));
        Assert.NotNull(effect);
    }

    [Theory]
    [MemberData(nameof(Loops))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_animated_pack_loops_and_really_moves(string id, int pack, bool dark)
    {
        var w = WallpaperCatalogue.Get(WallpaperKind.Animated, id);
        using var r = new WallpaperRenderer(new(WallpaperKind.Animated, id, w.Packs[pack].Id), dark);
        var zero = Frame(r);
        r.Advance(w.Period / 2);
        var half = Frame(r);
        r.Advance(w.Period / 2);
        var loop = FrameAt(r, w.Period);
        Assert.True(Diff(zero, loop) <= 2, $"{id}/{pack}/{dark} loop error {Diff(zero, loop)}/255");
        Assert.True(Diff(zero, half) >= 3, $"{id}/{pack}/{dark} motion {Diff(zero, half)}/255");
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Catalogue_has_162_wallpapers_810_dual_palettes_and_source_labels()
    {
        Assert.Equal(90, WallpaperCatalogue.All.Count(x => x.Kind == WallpaperKind.Static));
        Assert.Equal(72, WallpaperCatalogue.All.Count(x => x.Kind == WallpaperKind.Animated));
        Assert.Equal(810, WallpaperCatalogue.All.Sum(x => x.Packs.Length));
        foreach (var w in WallpaperCatalogue.All)
        {
            Assert.NotEmpty(w.Name); Assert.NotEmpty(w.Desc); Assert.Contains(w.Tier, new[] { "vibrant", "balanced", "minimal" });
            Assert.Equal(5, w.Packs.Length); Assert.Equal(2, w.Options.Length);
            foreach (var p in w.Packs)
            {
                Assert.Equal(6, p.Dark.Length); Assert.Equal(6, p.Light.Length);
                foreach (var c in p.Dark.Concat(p.Light)) Assert.Matches("^#[a-fA-F0-9]{6}$", c);
            }
            foreach (var o in w.Options) { Assert.NotEmpty(o.Label); Assert.InRange(o.Default, o.Min, o.Max); }
        }
        var first = WallpaperCatalogue.Get(WallpaperKind.Static, "riptide");
        Assert.Equal("Riptide", first.Name); Assert.Equal("Grain", first.Options[0].Label);
        Assert.Equal(0.3, first.Options[0].Default); Assert.Equal("Shadow", first.Options[1].Label);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Pack_crossfade_is_continuous_on_every_frame_and_retargets_from_current_colour()
    {
        var w = WallpaperCatalogue.Get(WallpaperKind.Static, "riptide");
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, w.Id, w.Packs[0].Id));
        var start = Frame(r); var previous = start;
        r.Set(new(WallpaperKind.Static, w.Id, w.Packs[1].Id), true);
        Assert.Equal(start, Frame(r));
        for (var i = 0; i < 33; i++)
        {
            r.Advance(1.0 / 60); var next = Frame(r);
            Assert.InRange(Diff(previous, next), 0, 20); previous = next;
            if (i == 12) { r.Set(new(WallpaperKind.Static, w.Id, w.Packs[2].Id), true); Assert.Equal(next, Frame(r)); }
        }
        for (var i = 0; i < 33; i++) r.Advance(1.0 / 60);
        using var target = new WallpaperRenderer(new(WallpaperKind.Static, w.Id, w.Packs[2].Id));
        Assert.Equal(Frame(target), Frame(r)); Assert.True(Diff(start, Frame(r)) > 3); Assert.False(r.NeedsFrames);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Speed_change_keeps_picture_and_phase_continuous_and_is_bounded()
    {
        using var r = new WallpaperRenderer(new(WallpaperKind.Animated, "borealis"));
        r.Advance(7); var before = Frame(r); var phase = r.Phase;
        r.Set(new(WallpaperKind.Animated, "borealis", Speed: 2.5), true);
        Assert.Equal(phase, r.Phase); Assert.Equal(before, Frame(r));
        r.Advance(1.0 / 60); Assert.InRange(r.Phase - phase, 1.0 / 60, 2.5 / 60);
        Assert.InRange(Diff(before, Frame(r)), 0, 20);
        r.Set(new(WallpaperKind.Animated, "borealis", Speed: 100), true);
        phase = r.Phase; r.Advance(1); Assert.InRange(r.Phase - phase, 0.25, 2.5);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Static_cache_costs_no_frames_at_rest_and_invalidates_on_inputs()
    {
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, "riptide"));
        var before = Frame(r); var count = r.Renders;
        for (var i = 0; i < 60; i++) { Assert.False(r.NeedsFrames); r.Advance(1.0 / 60); Assert.Equal(before, Frame(r)); }
        Assert.Equal(count, r.Renders);
        Frame(r, 40, 24); Assert.Equal(count + 1, r.Renders);
        r.Set(new(WallpaperKind.Static, "riptide"), false); Assert.True(r.NeedsFrames);
        // Finding 4: .16 * ln(1/.002) = .99434 s, so the first idle frame is 60/60 s.
        for (var i = 0; i < 59; i++) { r.Advance(1.0 / 60); Frame(r); }
        Assert.True(r.NeedsFrames); r.Advance(1.0 / 60); Frame(r);
        Assert.False(r.NeedsFrames); Assert.True(Diff(before, Frame(r)) > 3);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Reduce_motion_and_hidden_windows_freeze_without_resume_jump()
    {
        using var r = new WallpaperRenderer(new(WallpaperKind.Animated, "borealis"));
        r.Advance(5); r.ReduceMotion = true; var still = Frame(r); var phase = r.Phase;
        Assert.False(r.NeedsFrames); r.Advance(100); Assert.Equal(still, Frame(r)); Assert.Equal(phase, r.Phase);
        r.ReduceMotion = false; r.Visible = false; Assert.False(r.NeedsFrames);
        r.Advance(100); Assert.Equal(phase, r.Phase);
        r.Visible = true; Assert.True(r.NeedsFrames); r.Advance(1.0 / 60);
        Assert.InRange(r.Phase - phase, 0, 2.5 / 60);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Selection_defaults_to_none_and_roundtrips_without_touching_custom_image()
    {
        var defaults = new InMemorySettingsStore(); defaults.Set("wallpaperFile", "kept.png");
        var store = new WallpaperLibraryStore(defaults); Assert.Equal(WallpaperKind.None, store.Selection.Kind);
        var pick = new WallpaperSelection(WallpaperKind.Animated, "borealis", "polar", 1.75, new Dictionary<string, double> { ["motion"] = 0.8, ["glow"] = 0.2 });
        store.Selection = pick;
        var reload = new WallpaperLibraryStore(defaults).Selection;
        Assert.Equal(pick.Kind, reload.Kind); Assert.Equal(pick.Id, reload.Id); Assert.Equal(pick.Pack, reload.Pack);
        Assert.Equal(pick.Speed, reload.Speed); Assert.Equal(pick.Options, reload.Options);
        Assert.Equal("kept.png", defaults.Object("wallpaperFile"));
        store.Selection = new(); Assert.Equal(WallpaperKind.None, new WallpaperLibraryStore(defaults).Selection.Kind);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Successful_custom_image_selection_clears_the_builtin_and_notifies_without_deleting_the_picture()
    {
        var defaults = new InMemorySettingsStore();
        using var scope = Marrow.Core.Port.UserDefaults.Standard.Override(defaults);
        var store = new WallpaperLibraryStore(defaults);
        using var library = WallpaperLibraryStore.Shared.Override(store);
        store.Selection = new(WallpaperKind.Static, "riptide");
        defaults.Set("wallpaperFile", "chosen.png"); var changes = 0;
        store.PropertyChanged += (_, _) => changes++;
        WallpaperLibraryStore.CustomImageInstalled();
        Assert.Equal(WallpaperKind.None, store.Selection.Kind); Assert.Equal(1, changes);
        Assert.Equal(WallpaperKind.None, new WallpaperLibraryStore(defaults).Selection.Kind);
        Assert.Equal("chosen.png", defaults.Object("wallpaperFile"));
    }

    private static byte[] Frame(WallpaperRenderer r, int width = 32, int height = 20)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        r.Draw(surface.Canvas, width, height);
        using var image = surface.Snapshot(); using var pixels = SKBitmap.FromImage(image);
        return pixels.Bytes;
    }
    private static byte[] FrameAt(WallpaperRenderer r, double time)
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        r.DrawFrameAt(surface.Canvas, 32, 20, time);
        using var image = surface.Snapshot(); using var pixels = SKBitmap.FromImage(image);
        return pixels.Bytes;
    }
    private static int Diff(byte[] a, byte[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
}
