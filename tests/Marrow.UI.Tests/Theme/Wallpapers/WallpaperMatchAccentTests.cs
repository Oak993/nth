using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperMatchAccentTests
{
    public static IEnumerable<object[]> Walls() => WallpaperPageFixture.Root.GetProperty("walls").EnumerateArray().Select(w => new object[] { w.GetProperty("id").GetString()! });
    public static IEnumerable<object[]> Every() => WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Animated).Select(w => new object[] { w.Id });

    [Theory]
    [MemberData(nameof(Walls))]
    [Trait("Rule", "R-TEST-1")]
    public void The_sixth_pack_equals_the_pages_matchOf_for_each_fixture_accent(string id)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Animated, id);
        var fixture = WallpaperPageFixture.Root.GetProperty("walls").EnumerateArray().Single(w => w.GetProperty("id").GetString() == id).GetProperty("match");
        var exact = 0; var total = 0; var ramp = 0;
        foreach (var accent in fixture.EnumerateObject())
        {
            var pack = WallpaperMatchPack.Build(wall, accent.Name);
            Assert.Equal("match", pack.Id); Assert.Equal("Match accent", pack.Name);
            var band = wall.Premium!.Match.DarkWarm.Band; var hue = Lch(accent.Name).H;
            foreach (var (expected, got, isDark) in new[] { (accent.Value.GetProperty("dark"), pack.Dark, true), (accent.Value.GetProperty("light"), pack.Light, false) })
                for (var i = 0; i < 6; i++)
                {
                    // the dark_warm falling edge is the one place the app departs from the page (see the sweep test and ED-NEW-cloud-wl-3)
                    var before = Wrap(hue + wall.Premium.Match.Off[i]);
                    if (isDark && before > band[2] && before < band[3]) { ramp++; continue; }
                    var want = expected[i].GetString()!; total++; if (want == got[i]) exact++;
                    for (var c = 0; c < 3; c++) Assert.InRange(Math.Abs(Convert.ToInt32(want.Substring(1 + 2 * c, 2), 16) - Convert.ToInt32(got[i].Substring(1 + 2 * c, 2), 16)), 0, 1);
                }
        }
        Assert.True(exact >= total * 0.95, $"{id}: {exact}/{total} hex values identical to the page");
        Assert.True(ramp <= 3, $"{id}: {ramp} fixture slots on the falling edge");
    }

    /// <summary>Every dark ground slot at every accent hue (5 degree steps): the whole circle, the dark_warm band's falling edge (yellow-green, 100 to 120 degrees) included.</summary>
    [Theory]
    [MemberData(nameof(Walls))]
    [Trait("Rule", "R-TEST-1")]
    public void The_port_equals_the_page_for_a_24_accent_hue_sweep_except_on_the_dark_warm_falling_edge(string id)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Animated, id); var band = wall.Premium!.Match.DarkWarm.Band;
        var sweep = WallpaperPageFixture.Root.GetProperty("walls").EnumerateArray().Single(w => w.GetProperty("id").GetString() == id).GetProperty("sweep");
        var compared = 0; var edge = 0;
        foreach (var accent in sweep.EnumerateObject())
        {
            var pack = WallpaperMatchPack.Build(wall, accent.Name);
            for (var i = 0; i < 6; i++)
            {
                AssertWithinOne(accent.Value.GetProperty("light")[i].GetString()!, pack.Light[i]);
                var before = Wrap(Lch(accent.Name).H + wall.Premium.Match.Off[i]);
                // the falling edge of the band is the one place the app differs from the page: the page's half-turned colour passes through brown there
                if (before > band[2] && before < band[3]) { edge++; continue; }
                AssertWithinOne(accent.Value.GetProperty("dark")[i].GetString()!, pack.Dark[i]); compared++;
            }
        }
        Assert.True(compared >= 120, $"{id}: {compared} dark slots compared, {edge} on the falling edge");
    }

    [Theory]
    [MemberData(nameof(Every))]
    [Trait("Rule", "R-TEST-1")]
    public void A_warm_accent_never_gives_a_brown_or_olive_dark_ground_in_any_wallpaper(string id) => Assert.Empty(BrownGrounds(WallpaperCatalogue.Get(WallpaperKind.Animated, id)).Browns);

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_hue_band_check_examines_thousands_of_ground_slots_across_the_library()
        => Assert.True(WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Animated).Sum(w => BrownGrounds(w).Examined) >= 1500);

    private static (int Examined, List<string> Browns) BrownGrounds(WallpaperDefinition wall)
    {
        var warm = wall.Premium!.Match.DarkWarm; var examined = 0; var browns = new List<string>();
        foreach (var hue in Enumerable.Range(0, 72).Select(i => i * 5.0))
        {
            var accent = Accent(hue); var pack = WallpaperMatchPack.Build(wall, accent);
            for (var slot = 0; slot < 6; slot++)
            {
                // the dark ground slots of the palette: the ones the dark_warm rule guards (role lightness under ground_below)
                if (wall.Premium.Roles[slot][0] >= warm.GroundBelow) continue;
                var before = Wrap(Lch(accent).H + wall.Premium.Match.Off[slot]);
                examined++;
                if (Brown(pack.Dark[slot])) browns.Add($"{wall.Id} accent hue {hue} slot {slot} (hue {before:F0} before the rule): dark ground {pack.Dark[slot]} is brown or olive ({Describe(pack.Dark[slot])})");
            }
        }
        return (examined, browns);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_hue_band_check_catches_the_brown_a_warm_accent_makes_without_the_rule_and_a_cool_accent_is_untouched()
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Animated, "borealis"); var warm = wall.Premium!.Match.DarkWarm;
        var unruled = wall with { Premium = wall.Premium with { Match = wall.Premium.Match with { DarkWarm = warm with { Band = [400, 401, 402, 403] } } } };
        var amber = Accent(60);
        var browns = Enumerable.Range(0, 6).Count(i => wall.Premium.Roles[i][0] < warm.GroundBelow && Brown(WallpaperMatchPack.Build(unruled, amber).Dark[i]));
        Assert.True(browns > 0, "the check has something to catch without the rule");
        var cool = Accent(250);
        Assert.Equal(WallpaperMatchPack.Build(unruled, cool).Dark, WallpaperMatchPack.Build(wall, cool).Dark);
        Assert.Equal(WallpaperMatchPack.Build(unruled, amber).Light, WallpaperMatchPack.Build(wall, amber).Light);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_renderer_reads_the_accent_through_its_seam_and_eases_to_a_new_one()
    {
        var accent = new FixedWallpaperAccent("#005fb8");
        var env = new WallpaperEnvironment(TimeProvider.System, accent, new FixedWallpaperPosition());
        var pick = new WallpaperSelection(WallpaperKind.Animated, "borealis", "match");
        using var r = new WallpaperRenderer(pick, true, env) { ReduceMotion = true };
        using var blue = new WallpaperRenderer(pick, true, env) { ReduceMotion = true };
        var wall = WallpaperCatalogue.Get(WallpaperKind.Animated, "borealis");
        using var viaPack = new WallpaperRenderer(new(WallpaperKind.Animated, "borealis", "polar"), true);
        Assert.Equal(WallpaperPalette.For(WallpaperMatchPack.Build(wall, "#005fb8"), 1), r.TargetPalette);
        var start = WallpaperFrames.At(r, 2); accent.Hex = "#f7630c"; r.Wake(); r.Advance(1.0 / 60); Assert.True(r.NeedsFrames);
        var previous = WallpaperFrames.Draw(r);
        for (var i = 0; i < 40; i++) { r.Advance(1.0 / 60); var next = WallpaperFrames.Draw(r); Assert.InRange(WallpaperFrames.Diff(previous, next), 0, 20); previous = next; }
        using var orange = new WallpaperRenderer(pick, true, new(TimeProvider.System, new FixedWallpaperAccent("#f7630c"), new FixedWallpaperPosition())) { ReduceMotion = true };
        Assert.Equal(WallpaperFrames.Draw(orange), WallpaperFrames.Draw(r)); Assert.True(WallpaperFrames.Diff(start, WallpaperFrames.At(r, 2)) >= 3);
        Assert.False(r.NeedsFrames);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Match_is_a_valid_pack_for_animated_wallpapers_only()
    {
        Assert.Equal("match", WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, "borealis", "match")).Pack);
        var staticWall = WallpaperCatalogue.All.First(w => w.Kind == WallpaperKind.Static);
        Assert.Equal(staticWall.DefaultPack, WallpaperLibraryStore.Normalize(new(WallpaperKind.Static, staticWall.Id, "match")).Pack);
    }

    private static void AssertWithinOne(string expected, string actual)
    {
        for (var c = 0; c < 3; c++) Assert.InRange(Math.Abs(Convert.ToInt32(expected.Substring(1 + 2 * c, 2), 16) - Convert.ToInt32(actual.Substring(1 + 2 * c, 2), 16)), 0, 1);
    }

    private static double Wrap(double d) => (d + 540) % 360 - 180;

    private static string Accent(double hue)
    {
        var (l, c) = (0.62, 0.15); var a = c * Math.Cos(hue * Math.PI / 180); var b = c * Math.Sin(hue * Math.PI / 180);
        var lp = Math.Pow(l + 0.3963377774 * a + 0.2158037573 * b, 3); var mp = Math.Pow(l - 0.1055613458 * a - 0.0638541728 * b, 3); var sp = Math.Pow(l - 0.0894841775 * a - 1.291485548 * b, 3);
        var rgb = new[] { 4.0767416621 * lp - 3.3077115913 * mp + 0.2309699292 * sp, -1.2684380046 * lp + 2.6097574011 * mp - 0.3413193965 * sp, -0.0041960863 * lp - 0.7034186147 * mp + 1.707614701 * sp };
        return "#" + string.Concat(rgb.Select(v => { v = Math.Clamp(v, 0, 1); v = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055; return ((int)Math.Round(v * 255)).ToString("x2", System.Globalization.CultureInfo.InvariantCulture); }));
    }

    // OKLCH hue-band check: dark (L under 0.5), with enough chroma to read as a hue, between orange (30 degrees) and olive (110)
    private static bool Brown(string hex) { var (l, c, h) = Lch(hex); return l < 0.5 && c > 0.03 && h is >= 30 and <= 110; }
    private static string Describe(string hex) { var (l, c, h) = Lch(hex); return $"L {l:F2} C {c:F3} h {h:F0}"; }

    private static (double L, double C, double H) Lch(string hex)
    {
        var lab = WallpaperPalette.Lab(hex);
        return (lab[0], double.Hypot(lab[1], lab[2]), (Math.Atan2(lab[2], lab[1]) * 180 / Math.PI + 360) % 360);
    }
}
