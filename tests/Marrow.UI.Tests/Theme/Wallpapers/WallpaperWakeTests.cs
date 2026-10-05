using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperWakeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Lively = WallpaperCatalogue.All.First(w => w.Kind == WallpaperKind.Animated && w.Premium!.Dynamic.Light[0].TintAmount > 0.15 && w.Id != "borealis").Id;

    private static (WallpaperRenderer R, WallpaperPlayback P, WallpaperWakeSource Wake, ManualFrameSource Frames) Rig(ManualTimeProvider clock, FixedWallpaperAccent accent, WallpaperSelection pick)
    {
        var env = new WallpaperEnvironment(clock, accent, new FixedWallpaperPosition());
        var frames = new ManualFrameSource(); var r = new WallpaperRenderer(pick, true, env) { ReduceMotion = true };
        var p = new WallpaperPlayback(r, frames, () => { });
        var wake = new WallpaperWakeSource(env, p.Wake); wake.Update(WallpaperLibraryStore.Normalize(pick).Dynamic == true);
        return (r, p, wake, frames);
    }

    private static void Run(ManualFrameSource frames, int count = 40) { for (var i = 0; i < count; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60)); }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_renderer_no_longer_reads_the_clock_or_the_accent_on_every_frame()
    {
        var clock = new ManualTimeProvider(Noon); var accent = new FixedWallpaperAccent("#005fb8");
        using var r = new WallpaperRenderer(new(WallpaperKind.Animated, Lively, "match", Dynamic: true), true, new(clock, accent, new FixedWallpaperPosition())) { ReduceMotion = true };
        var target = r.TargetPalette; clock.Advance(TimeSpan.FromHours(6)); accent.Hex = "#f7630c";
        for (var i = 0; i < 120; i++) r.Advance(1.0 / 60);
        Assert.Equal(target, r.TargetPalette); Assert.False(r.NeedsFrames);
        r.Wake(); Assert.NotEqual(target, r.TargetPalette); Assert.True(r.NeedsFrames);
        var again = r.TargetPalette; r.Wake(); Assert.Equal(again, r.TargetPalette);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void An_idle_animated_wallpaper_under_reduce_motion_wakes_when_the_clock_crosses_a_minute_and_goes_idle_again()
    {
        var clock = new ManualTimeProvider(Noon); var (r, p, wake, frames) = Rig(clock, new("#005fb8"), new(WallpaperKind.Animated, Lively, Dynamic: true));
        using var _ = r; using var __ = p; using var ___ = wake;
        p.Refresh(); Assert.Equal(0, frames.Pending); var noon = WallpaperFrames.Draw(r);
        frames.Advance(TimeSpan.FromHours(1)); Assert.Equal(0, frames.Pending); Assert.Equal(noon, WallpaperFrames.Draw(r));
        clock.Advance(TimeSpan.FromHours(6)); Assert.Equal(1, frames.Pending);
        Run(frames); Assert.Equal(0, frames.Pending); Assert.False(r.NeedsFrames);
        using var dusk = new WallpaperRenderer(new(WallpaperKind.Animated, Lively, Dynamic: true), true, new(clock, new FixedWallpaperAccent("#005fb8"), new FixedWallpaperPosition())) { ReduceMotion = true };
        Assert.Equal(WallpaperFrames.Draw(dusk), WallpaperFrames.Draw(r)); Assert.True(WallpaperFrames.Diff(noon, WallpaperFrames.Draw(r)) >= 3);
        Assert.Equal(1, clock.ActiveTimers);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void A_hidden_window_is_brought_up_to_date_by_the_wake_without_asking_for_a_frame()
    {
        var clock = new ManualTimeProvider(Noon); var (r, p, wake, frames) = Rig(clock, new("#005fb8"), new(WallpaperKind.Animated, Lively, Dynamic: true));
        using var _ = r; using var __ = p; using var ___ = wake;
        r.Visible = false; p.Refresh(); clock.Advance(TimeSpan.FromHours(6)); Assert.Equal(0, frames.Pending); Assert.False(r.NeedsFrames);
        r.Visible = true; Assert.False(r.NeedsFrames);
        using var dusk = new WallpaperRenderer(new(WallpaperKind.Animated, Lively, Dynamic: true), true, new(clock, new FixedWallpaperAccent("#005fb8"), new FixedWallpaperPosition())) { ReduceMotion = true };
        Assert.Equal(WallpaperFrames.Draw(dusk), WallpaperFrames.Draw(r));
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void An_idle_wallpaper_on_the_match_pack_wakes_when_the_accent_changes()
    {
        var clock = new ManualTimeProvider(Noon); var accent = new FixedWallpaperAccent("#005fb8");
        var (r, p, wake, frames) = Rig(clock, accent, new(WallpaperKind.Animated, "borealis", "match"));
        using var _ = r; using var __ = p; using var ___ = wake;
        p.Refresh(); Assert.Equal(0, frames.Pending); Assert.Equal(0, clock.ActiveTimers); var before = WallpaperFrames.Draw(r);
        accent.Hex = "#f7630c"; Assert.Equal(1, frames.Pending);
        Run(frames); Assert.Equal(0, frames.Pending);
        using var orange = new WallpaperRenderer(new(WallpaperKind.Animated, "borealis", "match"), true, new(clock, new FixedWallpaperAccent("#f7630c"), new FixedWallpaperPosition())) { ReduceMotion = true };
        Assert.Equal(WallpaperFrames.Draw(orange), WallpaperFrames.Draw(r)); Assert.True(WallpaperFrames.Diff(before, WallpaperFrames.Draw(r)) >= 3);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void A_static_wallpaper_at_rest_arms_no_timer_requests_no_frame_and_ignores_an_accent_change()
    {
        var clock = new ManualTimeProvider(Noon); var accent = new FixedWallpaperAccent("#005fb8");
        var (r, p, wake, frames) = Rig(clock, accent, new(WallpaperKind.Static, "riptide"));
        using var _ = r; using var __ = p; using var ___ = wake;
        p.Refresh(); accent.Hex = "#f7630c"; clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(0, clock.ActiveTimers); Assert.Equal(0, frames.Pending); Assert.False(r.NeedsFrames);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void A_wake_after_the_playback_is_disposed_does_nothing()
    {
        var clock = new ManualTimeProvider(Noon); var (r, p, wake, frames) = Rig(clock, new("#005fb8"), new(WallpaperKind.Animated, Lively, Dynamic: true));
        using var _ = r; p.Dispose(); clock.Advance(TimeSpan.FromHours(2)); Assert.Equal(0, frames.Pending); wake.Dispose();
    }
}
