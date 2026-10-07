using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperPerspectiveTests
{
    private const int W = 320, H = 200;
    private static WallpaperDefinition Layered => WallpaperCatalogue.All.First(w => w.Kind == WallpaperKind.Animated && w.Premium!.Perspective.Mode == "layers");
    private static WallpaperDefinition Flat => WallpaperCatalogue.All.First(w => w.Kind == WallpaperKind.Animated && w.Premium!.Perspective.Mode == "pan");

    private static (WallpaperRenderer R, FixedWallpaperPosition Pos) Rig(WallpaperDefinition wall, bool? persp = null, bool reduce = false)
    {
        var pos = new FixedWallpaperPosition();
        var r = new WallpaperRenderer(new(WallpaperKind.Animated, wall.Id, Persp: persp), true, new(TimeProvider.System, new FixedWallpaperAccent("#005fb8"), pos)) { ReduceMotion = reduce };
        return (r, pos);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void A_layered_wallpaper_tilts_by_default_and_the_picture_follows_the_window_position()
    {
        var (r, pos) = Rig(Layered); using var _ = r;
        r.Advance(0); var centred = WallpaperFrames.At(r, 5, W, H);
        pos.Tilt = (1, 1); r.Advance(1.0 / 60); r.Settle();
        Assert.Equal((1.0, 1.0), r.Tilt);
        Assert.True(WallpaperFrames.Diff(centred, WallpaperFrames.At(r, 5, W, H)) >= 1, "the layers moved");
        pos.Tilt = (-1, 0.5); r.Advance(1.0 / 60); r.Settle(); Assert.Equal((-1.0, 0.5), r.Tilt);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void A_flat_wallpaper_is_off_by_default_and_pans_as_a_whole_when_the_viewer_turns_it_on()
    {
        var (r, pos) = Rig(Flat); using var _ = r; pos.Tilt = (1, 1); r.Advance(1.0 / 60); r.Settle();
        Assert.Equal((0.0, 0.0), r.Tilt);
        var still = WallpaperFrames.At(r, 5, W, H);
        var (on, pos2) = Rig(Flat, persp: true); using var __ = on; pos2.Tilt = (1, 1); on.Advance(1.0 / 60); on.Settle();
        Assert.Equal((1.0, 1.0), on.Tilt);
        Assert.True(WallpaperFrames.Diff(still, WallpaperFrames.At(on, 5, W, H)) >= 1, "the whole picture panned");
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Turning_it_off_for_a_layered_wallpaper_gives_the_untilted_picture_exactly()
    {
        var (off, pos) = Rig(Layered, persp: false); using var _ = off; pos.Tilt = (1, -1); off.Advance(1.0 / 60); off.Settle();
        var (centred, _) = Rig(Layered); using var __ = centred;
        Assert.Equal(WallpaperFrames.At(centred, 7, W, H), WallpaperFrames.At(off, 7, W, H)); Assert.Equal((0.0, 0.0), off.Tilt);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Tilt_is_off_under_reduce_motion_at_once_whatever_the_window_position()
    {
        var (r, pos) = Rig(Layered, persp: true); using var _ = r;
        pos.Tilt = (1, 1); r.Advance(1.0 / 60); r.Settle(); Assert.Equal((1.0, 1.0), r.Tilt);
        var tilted = WallpaperFrames.At(r, 5, W, H);
        r.ReduceMotion = true; Assert.Equal((0.0, 0.0), r.Tilt);
        for (var i = 0; i < 5; i++) { r.Advance(1.0 / 60); Assert.Equal((0.0, 0.0), r.Tilt); }
        var (flat, _) = Rig(Layered, persp: true, reduce: true); using var __ = flat;
        Assert.Equal(WallpaperFrames.At(flat, 5, W, H), WallpaperFrames.At(r, 5, W, H));
        Assert.True(WallpaperFrames.Diff(tilted, WallpaperFrames.At(r, 5, W, H)) >= 1);
        var (born, pos3) = Rig(Layered, persp: true, reduce: true); using var ___ = born; pos3.Tilt = (1, 1); born.Advance(1.0 / 60); born.Settle(); Assert.Equal((0.0, 0.0), born.Tilt);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Tilt_eases_with_a_0_35_s_time_constant_and_never_jumps()
    {
        var (r, pos) = Rig(Layered); using var _ = r; r.Advance(0); pos.Tilt = (1, -1);
        var previous = 0.0;
        for (var i = 1; i <= 21; i++)
        {
            r.Advance(1.0 / 60); Assert.InRange(r.Tilt.X - previous, 0, 0.05); previous = r.Tilt.X;
        }
        Assert.InRange(r.Tilt.X, 0.62, 0.65); Assert.Equal(-r.Tilt.X, r.Tilt.Y, 1e-12);
        for (var i = 0; i < 120; i++) r.Advance(1.0 / 60);
        Assert.Equal((1.0, -1.0), r.Tilt);
        pos.Tilt = (0, 0); for (var i = 0; i < 6; i++) { r.Advance(1.0 / 60); Assert.InRange(r.Tilt.X, 0.5, 1); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_tilt_is_clamped_to_plus_or_minus_one_and_never_touches_the_loop_time()
    {
        var (r, pos) = Rig(Layered); using var _ = r; pos.Tilt = (9, -9); r.Advance(1.0 / 60); r.Settle(); Assert.Equal((1.0, -1.0), r.Tilt);
        var w = Layered; Assert.True(WallpaperFrames.Diff(WallpaperFrames.At(r, 0), WallpaperFrames.At(r, w.Period)) <= 2);
    }

    [Theory]
    [InlineData(0, 0, 1000, 600, -1, -1)]
    [InlineData(500, 300, 1000, 600, 0, 0)]
    [InlineData(1000, 600, 1000, 600, 1, 1)]
    [InlineData(250, 450, 1000, 600, -0.5, 0.5)]
    [InlineData(5000, -5000, 1000, 600, 1, -1)]
    [Trait("Rule", "R-TEST-1")]
    public void The_window_centre_minus_the_screen_centre_over_half_the_screen_clamps_to_unit_tilt(double cx, double cy, double sw, double sh, double ex, double ey)
    {
        var (x, y) = WallpaperTilt.From(cx, cy, sw, sh);
        Assert.Equal(ex, x, 1e-12); Assert.Equal(ey, y, 1e-12);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_live_position_holder_hands_the_render_thread_the_last_tilt_it_was_given()
    {
        var holder = new WallpaperWindowPosition(); Assert.Equal((0.0, 0.0), holder.Tilt);
        holder.Update(750, 150, 1000, 600); Assert.Equal((0.5, -0.5), holder.Tilt);
    }
}
