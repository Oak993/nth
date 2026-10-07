using Marrow.UI.Port;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperPlaybackTests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Static_playback_requests_zero_frames_at_rest_then_stops_after_a_pack_turn()
    {
        var frames = new ManualFrameSource(); var draws = 0;
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, "riptide"));
        using var p = new WallpaperPlayback(r, frames, () => draws++);
        p.Refresh(); Assert.Equal(0, frames.Pending);
        r.Set(new(WallpaperKind.Static, "riptide", "Lagoon"), true); p.Refresh(); Assert.Equal(1, frames.Pending);
        for (var i = 0; i < 40; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.InRange(draws, 30, 40); Assert.Equal(0, frames.Pending);
        frames.Advance(TimeSpan.FromHours(1)); Assert.Equal(0, frames.Pending);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Playback_withdraws_hidden_reduced_and_disposed_requests_and_resumes_from_current_phase()
    {
        var frames = new ManualFrameSource(); var draws = 0;
        using var r = new WallpaperRenderer(new(WallpaperKind.Animated, "borealis"));
        var p = new WallpaperPlayback(r, frames, () => draws++);
        p.Refresh(); Assert.Equal(1, frames.Pending);
        for (var i = 0; i < 10; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.Equal(10, draws); var phase = r.Phase;
        r.Visible = false; p.Refresh(); Assert.Equal(0, frames.Pending);
        frames.Advance(TimeSpan.FromHours(1)); Assert.Equal(phase, r.Phase);
        r.Visible = true; p.Refresh(); frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.InRange(r.Phase - phase, 0, 1.0 / 60 + 1e-6);
        r.ReduceMotion = true; p.Refresh(); Assert.Equal(0, frames.Pending);
        r.ReduceMotion = false; p.Refresh(); Assert.Equal(1, frames.Pending);
        p.Dispose(); Assert.Equal(0, frames.Pending);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Wallpaper_switch_and_clear_crossfade_from_the_current_picture()
    {
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, "riptide"));
        var start = Frame(r);
        r.Set(new(WallpaperKind.Static, "meadow"), true); Assert.Equal(start, Frame(r));
        var previous = start;
        for (var i = 0; i < 34; i++) { r.Advance(1.0 / 60); var next = Frame(r); Assert.InRange(Diff(previous, next), 0, 20); previous = next; }
        using var target = new WallpaperRenderer(new(WallpaperKind.Static, "meadow")); Assert.Equal(Frame(target), Frame(r));
        r.Set(new(), true); Assert.Equal(previous, Frame(r));
        for (var i = 0; i < 34; i++) { r.Advance(1.0 / 60); var next = Frame(r); Assert.InRange(Diff(previous, next), 0, 20); previous = next; }
        Assert.All(previous, x => Assert.Equal(0, x)); Assert.False(r.NeedsFrames);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Options_ease_from_current_value_and_are_clamped_to_catalogue_ranges()
    {
        using var r = new WallpaperRenderer(new(WallpaperKind.Static, "riptide"));
        var start = Frame(r); var previous = start;
        r.Set(new(WallpaperKind.Static, "riptide", Options: new Dictionary<string, double> { ["grain"] = 0, ["option"] = 100 }), true);
        Assert.Equal(start, Frame(r)); Assert.True(r.NeedsFrames);
        for (var i = 0; i < 34; i++) { r.Advance(1.0 / 60); var next = Frame(r); Assert.InRange(Diff(previous, next), 0, 20); previous = next; }
        using var target = new WallpaperRenderer(new(WallpaperKind.Static, "riptide", Options: new Dictionary<string, double> { ["grain"] = 0, ["option"] = 1 }));
        Assert.Equal(Frame(target), Frame(r)); Assert.False(r.NeedsFrames); Assert.True(Diff(start, Frame(r)) >= 3);
    }

    private static byte[] Frame(WallpaperRenderer r)
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        r.Draw(surface.Canvas, 32, 20);
        using var image = surface.Snapshot(); using var pixels = SKBitmap.FromImage(image); return pixels.Bytes;
    }
    private static int Diff(byte[] a, byte[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
}
