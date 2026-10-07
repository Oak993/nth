using Avalonia;
using Marrow.Testing;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-4")]
public sealed class StaticWallpaperMotionTests
{
    [Fact]
    public void Local_midnight_follows_the_nearby_night_key_instead_of_sweeping_back_through_noon()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 23, 59, 0, TimeSpan.Zero));
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, "riptide", "Coral", Options:
            new Dictionary<string, double> { ["dynamic"] = 1, ["persp"] = 0, ["grain"] = 0, ["option"] = 0, ["focus"] = 0 }),
            true, new(clock, () => "#005FB8", () => Vector.Zero));
        using var surface = SKSurface.Create(new SKImageInfo(32, 20)); r.Draw(surface.Canvas, 32, 20);
        clock.Advance(TimeSpan.FromMinutes(1)); r.RefreshStaticInputs(); r.Advance(.16); r.Draw(surface.Canvas, 32, 20);
        var expected = StaticWallpaperPage.Golden.GetProperty("midnight"); var actual = r.StaticState!;
        Assert.Equal(expected.GetProperty("dark").GetDouble(), actual.Dark, 9);
        Assert.Equal(expected.GetProperty("light")[0].GetDouble(), actual.Light[0], 9);
        Assert.Equal(expected.GetProperty("light")[1].GetDouble(), actual.Light[1], 9);
        Assert.All(expected.GetProperty("palette").EnumerateArray().Select(x => x.GetDouble()).Zip(actual.Palette),
            p => Assert.InRange(Math.Abs(p.First - p.Second), 0, 2e-6));
    }

    [Fact]
    public void Theme_dynamic_focus_grain_option_and_tilt_follow_actual_page_transients()
    {
        var clock = new ManualTimeProvider(); var position = Vector.Zero;
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, "riptide", "Coral", Options:
            new Dictionary<string, double> { ["grain"] = 0, ["option"] = 0, ["focus"] = 0, ["dynamic"] = 0, ["persp"] = 1 }),
            true, new(clock, () => "#005FB8", () => position));
        using var surface = SKSurface.Create(new SKImageInfo(32, 20)); r.Draw(surface.Canvas, 32, 20);
        clock.Advance(TimeSpan.FromHours(6)); position = new Vector(1, 1);
        r.Set(new(WallpaperKind.Static, "riptide", "Lagoon", Options:
            new Dictionary<string, double> { ["grain"] = 1, ["option"] = 1, ["focus"] = .37, ["dynamic"] = 1, ["persp"] = 1 }), false);
        foreach (var row in StaticWallpaperPage.Golden.GetProperty("motion").EnumerateArray())
        {
            r.Advance(row.GetProperty("dt").GetDouble()); r.Draw(surface.Canvas, 32, 20);
            var s = r.StaticState!;
            var expected = row.GetProperty("palette").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            Assert.Equal(expected.Length, s.Palette.Length);
            Assert.All(expected.Zip(s.Palette), p => Assert.InRange(Math.Abs(p.First - p.Second), 0, 2e-6));
            Assert.Equal(row.GetProperty("dark").GetDouble(), s.Dark, 8);
            Assert.Equal(row.GetProperty("light")[0].GetDouble(), s.Light[0], 8);
            Assert.Equal(row.GetProperty("light")[1].GetDouble(), s.Light[1], 8);
            Assert.Equal(row.GetProperty("tilt")[0].GetDouble(), s.Tilt.X, 8);
            Assert.Equal(row.GetProperty("tilt")[1].GetDouble(), s.Tilt.Y, 8);
            Assert.Equal(row.GetProperty("focus").GetDouble(), s.Focus, 8);
            Assert.Equal(row.GetProperty("sigma").GetDouble(), s.Sigma, 8);
            Assert.Equal(row.GetProperty("dream").GetDouble(), s.Dream, 8);
            Assert.Equal(row.GetProperty("grain").GetDouble(), r.StaticGrain, 8);
            Assert.Equal(row.GetProperty("option").GetDouble(), r.StaticOption, 8);
        }
        Assert.False(r.NeedsFrames);
    }
}
