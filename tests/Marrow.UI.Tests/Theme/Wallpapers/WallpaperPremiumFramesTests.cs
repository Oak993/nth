using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperPremiumFramesTests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void A_static_wallpaper_requests_no_frames_at_rest_whatever_the_environment_seams_say()
    {
        var frames = new ManualFrameSource(); var accent = new FixedWallpaperAccent("#005fb8"); var pos = new FixedWallpaperPosition(); var clock = new ManualTimeProvider();
        var env = new WallpaperEnvironment(clock, accent, pos);
        foreach (var id in new[] { "riptide", "meadow", "afternoon" })
        {
            using var r = new WallpaperRenderer(new(WallpaperKind.Static, id), true, env);
            using var p = new WallpaperPlayback(r, frames, () => { });
            p.Refresh(); Assert.Equal(0, frames.Pending);
            accent.Hex = "#f7630c"; pos.Tilt = (1, 1); clock.Advance(TimeSpan.FromHours(7));
            frames.Advance(TimeSpan.FromHours(1)); p.Refresh(); Assert.Equal(0, frames.Pending); Assert.False(r.NeedsFrames);
            var renders = r.Renders; WallpaperFrames.Draw(r); WallpaperFrames.Draw(r); Assert.Equal(renders + 1, r.Renders);
        }
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void An_animated_wallpaper_under_reduce_motion_requests_frames_only_while_a_change_eases()
    {
        var frames = new ManualFrameSource(); var accent = new FixedWallpaperAccent("#005fb8");
        using var r = new WallpaperRenderer(new(WallpaperKind.Animated, "borealis", "match", Dynamic: true, Focus: 0), true, new(new ManualTimeProvider(), accent, new FixedWallpaperPosition())) { ReduceMotion = true };
        using var p = new WallpaperPlayback(r, frames, () => { });
        p.Refresh(); Assert.Equal(0, frames.Pending);
        r.Set(new(WallpaperKind.Animated, "borealis", "match", Dynamic: true, Focus: 0.5), true); p.Refresh(); Assert.Equal(1, frames.Pending);
        for (var i = 0; i < 40; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.Equal(0, frames.Pending); Assert.False(r.NeedsFrames);
    }
}
