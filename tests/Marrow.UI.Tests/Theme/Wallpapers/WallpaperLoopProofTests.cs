using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperLoopProofTests
{
    private static readonly double[] KeyHours = [6, 12, 18, 0];

    public static IEnumerable<object[]> Animated() => WallpaperCatalogue.All.Where(w => w.Kind == WallpaperKind.Animated).Select(w => new object[] { w.Id });

    /// <summary>Frame 0 against frame T at most 2/255, for every pack and Match accent, dark and light, with the options at both ends,
    /// the four key looks, full tilt and full focus: the loop proof the page runs, on the CPU raster.</summary>
    [Theory]
    [MemberData(nameof(Animated))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_wallpaper_loops_in_every_pack_theme_option_end_look_tilt_and_focus(string id)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Animated, id); var proofs = 0; var worst = 0;
        var pos = new FixedWallpaperPosition { Tilt = (1, 1) };
        var clock = new SettableWallpaperClock();
        var env = new WallpaperEnvironment(clock, new FixedWallpaperAccent("#f7630c"), pos);
        using var r = new WallpaperRenderer(new(WallpaperKind.Animated, id), true, env);
        foreach (var pack in wall.Packs.Select(p => p.Id).Append("match"))
            foreach (var dark in new[] { false, true })
                foreach (var end in new[] { 0.0, 1.0 })
                {
                    var options = wall.Options.ToDictionary(o => o.Id, _ => end);
                    var states = new List<(string Name, WallpaperSelection Pick, double Hour)>
                    {
                        ("plain", new(WallpaperKind.Animated, id, pack, Options: options, Dynamic: false, Persp: false, Focus: 0), 12),
                    };
                    states.AddRange(KeyHours.Select(h => ($"look {h}", new WallpaperSelection(WallpaperKind.Animated, id, pack, Options: options, Dynamic: true, Persp: false, Focus: 0), h)));
                    states.Add(("full tilt", new(WallpaperKind.Animated, id, pack, Options: options, Dynamic: false, Persp: true, Focus: 0), 12));
                    states.Add(("full focus", new(WallpaperKind.Animated, id, pack, Options: options, Dynamic: false, Persp: false, Focus: 1), 12));
                    states.Add(("everything", new(WallpaperKind.Animated, id, pack, Options: options, Dynamic: true, Persp: true, Focus: 1), 18));
                    foreach (var (name, pick, hour) in states)
                    {
                        clock.Now = new DateTimeOffset(2026, 10, 5, (int)hour % 24, 0, 0, TimeSpan.Zero);
                        r.Set(pick, dark); r.Advance(0); r.Settle();
                        var diff = WallpaperFrames.Diff(WallpaperFrames.At(r, 0), WallpaperFrames.At(r, wall.Period)); worst = Math.Max(worst, diff); proofs++;
                        Assert.True(diff <= 2, $"{id}/{pack}/{(dark ? "dark" : "light")}/options {end}/{name}: loop error {diff}/255");
                    }
                }
        Assert.Equal(6 * 2 * 2 * 8, proofs);
    }

    /// <summary>Frame 0 against frame T/2 differs by at least 3/255 in every pack, dark and light: the wallpaper really moves.</summary>
    [Theory]
    [MemberData(nameof(Animated))]
    [Trait("Rule", "R-TEST-1")]
    public void Every_wallpaper_really_moves_at_half_a_period_in_every_pack_and_theme(string id)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Animated, id);
        var env = new WallpaperEnvironment(TimeProvider.System, new FixedWallpaperAccent("#005fb8"), new FixedWallpaperPosition());
        using var r = new WallpaperRenderer(new(WallpaperKind.Animated, id), true, env);
        foreach (var pack in wall.Packs.Select(p => p.Id).Append("match"))
            foreach (var dark in new[] { false, true })
            {
                r.Set(new(WallpaperKind.Animated, id, pack), dark); r.Settle();
                var half = WallpaperFrames.Diff(WallpaperFrames.At(r, 0), WallpaperFrames.At(r, wall.Period / 2));
                Assert.True(half >= 3, $"{id}/{pack}/{(dark ? "dark" : "light")}: motion at T/2 is {half}/255");
            }
    }
}
