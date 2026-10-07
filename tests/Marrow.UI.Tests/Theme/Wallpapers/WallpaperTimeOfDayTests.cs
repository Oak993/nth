using System.Text.Json;
using Marrow.Testing;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperTimeOfDayTests
{
    private static readonly WallpaperPack Plain = new("plain", "Plain", [], []);

    public static IEnumerable<object[]> Walls() => WallpaperPageFixture.Root.GetProperty("walls").EnumerateArray().Select(w => new object[] { w.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(Walls))]
    [Trait("Rule", "R-TEST-1")]
    public void Dynamic_palette_and_sun_match_the_pages_numbers_at_every_fixture_clock(string id)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Animated, id);
        var fixture = WallpaperPageFixture.Root.GetProperty("walls").EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        var accent = WallpaperPageFixture.Root.GetProperty("accents")[0].GetString()!;
        var checks = 0;
        foreach (var row in fixture.GetProperty("looks").EnumerateArray())
        {
            var packId = row.GetProperty("pack").GetString()!; var dark = row.GetProperty("dark").GetDouble();
            var pack = packId == "match" ? WallpaperMatchPack.Build(wall, accent) : wall.Packs.Single(p => p.Id == packId);
            var plain = WallpaperTimeOfDay.PaletteFor(wall, pack, dark, null);
            AssertClose(row.GetProperty("off"), plain.Palette); Assert.Equal((0.0, 0.0), plain.Sun);
            foreach (var at in row.GetProperty("clocks").EnumerateArray())
            {
                var got = WallpaperTimeOfDay.PaletteFor(wall, pack, dark, at.GetProperty("c").GetDouble());
                AssertClose(at.GetProperty("pal"), got.Palette);
                Assert.Equal(at.GetProperty("sun")[0].GetDouble(), got.Sun.X, 1e-6); Assert.Equal(at.GetProperty("sun")[1].GetDouble(), got.Sun.Y, 1e-6);
                checks++;
            }
        }
        Assert.Equal(60, checks);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_fixture_has_a_wallpaper_with_a_moving_light_so_the_sun_track_is_proved()
    {
        Assert.Contains(WallpaperPageFixture.Root.GetProperty("walls").EnumerateArray(), w => w.GetProperty("looks").EnumerateArray()
            .SelectMany(r => r.GetProperty("clocks").EnumerateArray()).Any(c => c.GetProperty("sun")[0].GetDouble() != 0 || c.GetProperty("sun")[1].GetDouble() != 0));
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_look_is_the_smoothstep_blend_of_the_two_key_looks_and_cyclic_over_midnight()
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Animated, "borealis"); var d = wall.Premium!.Dynamic;
        var dawn = WallpaperTimeOfDay.Look(d, 6, 0); Assert.Equal(d.Light[0].DL[0], dawn.DL[0], 1e-12); Assert.Equal(d.Light[0].KC[0], dawn.KC[0], 1e-12);
        var dayDark = WallpaperTimeOfDay.Look(d, 12, 1); Assert.Equal(d.Dark[1].DL[0], dayDark.DL[0], 1e-12);
        var mid = WallpaperTimeOfDay.Look(d, 9, 0); var s = 0.5;
        Assert.Equal((1 - s) * d.Light[0].DL[2] + s * d.Light[1].DL[2], mid.DL[2], 1e-12);
        Assert.Equal(Math.Exp((1 - s) * Math.Log(d.Light[0].KC[2]) + s * Math.Log(d.Light[1].KC[2])), mid.KC[2], 1e-12);
        var night = WallpaperTimeOfDay.Look(d, 0, 1); var wrapped = WallpaperTimeOfDay.Look(d, 24, 1); var negative = WallpaperTimeOfDay.Look(d, -24, 1);
        Assert.Equal(night.DL, wrapped.DL); Assert.Equal(night.DL, negative.DL);
        var quarter = WallpaperTimeOfDay.Look(d, 21, 0); var u = 0.5; Assert.Equal((1 - u) * d.Light[2].DL[1] + u * d.Light[3].DL[1], quarter.DL[1], 1e-12);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Dynamic_follows_the_injected_local_clock_and_eases_to_it()
    {
        var wall = WallpaperCatalogue.All.First(w => w.Kind == WallpaperKind.Animated && w.Premium!.Dynamic.Light[0].TintAmount > 0.15 && w.Id != "borealis");
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var env = new WallpaperEnvironment(clock, new FixedWallpaperAccent("#005fb8"), new FixedWallpaperPosition());
        var pick = new WallpaperSelection(WallpaperKind.Animated, wall.Id, Dynamic: true);
        using var r = new WallpaperRenderer(pick, true, env) { ReduceMotion = true };
        var noon = WallpaperFrames.Draw(r);
        using var plain = new WallpaperRenderer(pick with { Dynamic = false }, true, env) { ReduceMotion = true };
        clock.Advance(TimeSpan.FromHours(6)); Assert.Equal(noon, WallpaperFrames.Draw(r));
        r.Wake(); var previous = noon; r.Advance(1.0 / 60);
        for (var i = 0; i < 34; i++) { var next = WallpaperFrames.Draw(r); Assert.InRange(WallpaperFrames.Diff(previous, next), 0, 20); previous = next; r.Advance(1.0 / 60); }
        using var dusk = new WallpaperRenderer(pick, true, env) { ReduceMotion = true };
        Assert.Equal(WallpaperFrames.Draw(dusk), WallpaperFrames.Draw(r)); Assert.True(WallpaperFrames.Diff(noon, WallpaperFrames.Draw(r)) >= 3);
        Assert.False(r.NeedsFrames);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Dynamic_off_is_the_plain_pack_and_the_light_stays_at_its_designed_place()
    {
        var wall = WallpaperCatalogue.All.First(w => w.Kind == WallpaperKind.Animated && w.Premium!.Dynamic.Sun.Any(s => s[0] != 0 || s[1] != 0));
        var env = new WallpaperEnvironment(new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeSpan.Zero)), new FixedWallpaperAccent("#005fb8"), new FixedWallpaperPosition());
        using var off = new WallpaperRenderer(new(WallpaperKind.Animated, wall.Id, Dynamic: false), true, env);
        using var on = new WallpaperRenderer(new(WallpaperKind.Animated, wall.Id, Dynamic: true), true, env);
        Assert.Equal((0.0, 0.0), off.Sun); Assert.NotEqual((0.0, 0.0), on.Sun);
        Assert.True(WallpaperFrames.Diff(WallpaperFrames.At(off, 3), WallpaperFrames.At(on, 3)) >= 3);
        on.Set(new(WallpaperKind.Animated, wall.Id, Dynamic: false), true); on.Settle();
        Assert.Equal(WallpaperFrames.At(off, 3), WallpaperFrames.At(on, 3)); Assert.Equal((0.0, 0.0), on.Sun);
    }

    private static void AssertClose(JsonElement expected, float[] actual)
    {
        Assert.Equal(18, actual.Length);
        for (var i = 0; i < 18; i++) Assert.Equal(expected[i].GetDouble(), actual[i], 1e-5);
    }
}
