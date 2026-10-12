// PORT-ONLY: Tactus pointers on screen (OD-0144, OD-0145, ED-NEW-pcctl-21; DESIGN.md 3.1 overlay row, 4.5, 10.1, 10.8;
// critique C19): a visible point opens a pointer window on its monitor, in that monitor's pixels, placed once for a glide's
// span and once at rest, never per frame; a point the user cannot see, or a monitor with an exclusive-fullscreen app,
// parks the pointer with no window; a pointer lingers 2 s after its app's work ends (not while paused), leaves, and its
// window closes 1 s later; a press ripples where it lands; frames are wanted only while something moves. All on fakes.
using Avalonia;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Core.Port.PcControl.Pointer;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-21")]
public sealed class PointerOverlaysTests
{
    private const string Excel = "c:\\office\\excel.exe";

    private double now = 1;

    [Fact]
    public void A_visible_point_opens_a_window_on_its_monitor_placed_once_per_glide_and_once_at_rest()
    {
        var (overlays, surfaces, _) = Make();

        overlays.Aimed(Aim(2400, 600));
        Assert.True(overlays.Frame());

        var surface = Assert.Single(surfaces.Opened);
        Assert.Equal((new PixelRect(2340, 540, 132, 132), 1.5), surface.Places[0]);
        var (pose, origin) = surface.Draws[^1];
        Assert.Equal(new Point(280, 360), origin);
        Assert.Equal(new PillVector(320, 400), pose.Tip);

        now = 3;
        Assert.False(overlays.Frame());
        Assert.Single(surface.Places);

        overlays.Aimed(Aim(2700, 900));
        for (var ms = 0; ms <= 200; ms += 16)
        {
            now = 3 + (ms / 1000.0);
            Assert.True(overlays.Frame());
        }
        Assert.Equal(2, surface.Places.Count);
        now = 4;
        overlays.Frame();
        Assert.Equal(3, surface.Places.Count);
        Assert.Equal(new PixelRect(2640, 840, 132, 132), surface.Places[^1].Rect);
        Assert.Single(surfaces.Opened);
    }

    [Fact]
    public void A_point_the_user_cannot_see_or_a_monitor_in_exclusive_fullscreen_parks_it_with_no_window()
    {
        var (overlays, surfaces, screens) = Make();

        overlays.Aimed(Aim(400, 300, visible: false));
        overlays.Frame();
        Assert.True(overlays.Parked(Excel));
        Assert.Empty(surfaces.Opened);

        screens.Fullscreen.Add(0);
        overlays.Aimed(Aim(400, 300));
        overlays.Frame();
        Assert.True(overlays.Parked(Excel));
        Assert.Empty(surfaces.Opened);

        screens.Fullscreen.Clear();
        overlays.Aimed(Aim(400, 300));
        overlays.Frame();
        Assert.False(overlays.Parked(Excel));
        Assert.Single(surfaces.Opened);
    }

    [Fact]
    public void It_lingers_2_s_after_its_app_s_work_ends_then_leaves_and_its_window_closes_1_s_later()
    {
        var (overlays, surfaces, _) = Make();
        overlays.Aimed(Aim(400, 300));
        overlays.Frame();
        var surface = surfaces.Opened[0];

        now = 2;
        overlays.Active([]);
        now = 3.99;
        Assert.True(overlays.Frame());
        Assert.Equal(PointerPhase.Shown, overlays.Track(Excel)!.Phase(now));
        now = 4;
        overlays.Frame();
        Assert.Equal(PointerPhase.Leaving, overlays.Track(Excel)!.Phase(now));
        now = 4 + PointerTrack.LeaveSeconds + 0.5;
        Assert.True(overlays.Frame());
        Assert.False(surface.Closed);
        now = 4 + PointerTrack.LeaveSeconds + 1;
        Assert.False(overlays.Frame());
        Assert.True(surface.Closed);
        Assert.Null(overlays.Track(Excel));
    }

    [Fact]
    public void A_paused_app_s_pointer_stays_while_it_has_no_work()
    {
        var (overlays, _, _) = Make();
        overlays.Aimed(Aim(400, 300));
        overlays.Frame();

        overlays.Paused(Excel, true);
        overlays.Active([]);
        now = 10;
        overlays.Frame();

        Assert.Equal(PointerPhase.Shown, overlays.Track(Excel)!.Phase(now));
        Assert.Equal(PointerTrack.PausedOpacity, overlays.Track(Excel)!.Pose(now).Opacity, 9);
    }

    [Fact]
    public void A_press_ripples_where_the_pointer_lands_and_frames_are_wanted_until_it_ends()
    {
        var (overlays, surfaces, _) = Make();
        var wanted = 0;
        overlays.FramesWanted += () => wanted++;
        overlays.Aimed(Aim(400, 300));
        now = 3;
        overlays.Frame();

        overlays.Aimed(Aim(900, 600, PcPointerAct.Press));
        Assert.Equal(2, wanted);
        var lands = PointerGlide.Start(3, new PillVector(400, 300), new PillVector(900, 600), PillVector.Zero).LandsAt();
        now = lands + 0.1;
        Assert.True(overlays.Frame());
        Assert.True(surfaces.Opened[0].Draws[^1].Pose.Rippling);
        now = lands + 2;
        Assert.False(overlays.Frame());
    }

    [Fact]
    public void A_DIP_rect_becomes_its_monitor_s_pixels_rounded_outward()
    {
        var monitor = new PcMonitor(1, new PcRect(1920, 0, 2880, 1620), new PcRect(1920, 0, 2880, 1560), 144, false);

        Assert.Equal(1.5, PointerOverlays.Scale(monitor));
        Assert.Equal(new PixelRect(1935, 15, 145, 145), PointerOverlays.Pixels(monitor, new PillRect(10.2, 10.1, 96.3, 96), 1.5));
    }

    private (PointerOverlays Overlays, PcFakePointerSurfaces Surfaces, PcFakePointerScreens Screens) Make()
    {
        var surfaces = new PcFakePointerSurfaces();
        var screens = new PcFakePointerScreens();
        return (new PointerOverlays(surfaces, screens, () => now, () => false), surfaces, screens);
    }

    private static PcPointerAim Aim(int x, int y, PcPointerAct act = PcPointerAct.Move, bool visible = true) =>
        new(Excel, "excel", Excel, 0x1a, new PcPoint(x, y), act, visible);
}
