// PORT-ONLY: one overlay band per monitor (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.6 "One overlay per monitor", "Hit
// testing", 10.3 "Grab in flight", "Displays"): a band on demand across the work area's full width, its height per state
// (grow before the motion, shrink after it), no window call per frame, the region at rest and the grab zone while moving,
// the topmost order set again only on show, an ask or a cover, the band closed 2 s after the pill hides, and display
// changes moving or closing bands. Every window and Win32 call is a fake.
using Avalonia;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class PillOverlaysTests
{
    private double now = 100;

    private (PillOverlays Overlays, PcFakePillBands Bands, PcFakeBandNative Native, PcGrabZone Grab) Make(OverlayHitTestKind kind = OverlayHitTestKind.Region)
    {
        var bands = new PcFakePillBands();
        var native = new PcFakeBandNative();
        var grab = new PcGrabZone();
        var overlays = new PillOverlays(bands, native, kind, () => now, grab, () => 5_000, k => k == OverlayHitTestKind.StyleFlip ? new StyleFlipHitTest(native, runThread: false) : OverlayHitTests.Create(k, native));
        return (overlays, bands, native, grab);
    }

    private static PillShape Travel(double start) =>
        PillShape.Moving(PillTravel.Start(start, new PillVector(16, 56), new PillVector(740, 56), PillVector.Zero, false), null, 440, null, 48);

    [Fact]
    public void A_band_opens_on_demand_across_the_work_area_s_full_width_with_a_shadow_window_for_the_region_test()
    {
        var (overlays, bands, _, _) = Make();

        overlays.Show(PcTestMonitors.Right);
        overlays.Show(PcTestMonitors.Right);

        var opened = Assert.Single(bands.Opened);
        Assert.Equal(new PixelRect(1920, 72, 2880, 180), opened.Rect);
        Assert.NotEqual((nint)0, opened.Band.Shadow);
        Assert.Equal(2, opened.Band.Reasserts);
        Assert.Equal(120, overlays.Height(PcTestMonitors.Right.Id));
        Assert.Equal(2, overlays.Handles.Count);
    }

    [Fact]
    public void A_growing_band_is_resized_before_the_motion_and_a_shrinking_one_after_it_ends()
    {
        var (overlays, bands, native, _) = Make();
        overlays.Show(PcTestMonitors.Main);
        var band = bands.Opened[0].Band;
        native.Calls.Clear();

        Assert.Equal(PillBandResize.BeforeMotion, overlays.State(PcTestMonitors.Main.Id, PillBandState.Card, cardHeight: 300));
        Assert.Equal(new[] { $"bounds {band.Input:x} 0,0,1920,460", $"bounds {band.Shadow:x} 0,0,1920,460" }, native.Calls);
        native.Calls.Clear();

        Assert.Equal(PillBandResize.AfterMotion, overlays.State(PcTestMonitors.Main.Id, PillBandState.Pill));
        Assert.Empty(native.Calls);
        Assert.Equal(460, overlays.Height(PcTestMonitors.Main.Id));

        overlays.MotionEnded(PcTestMonitors.Main.Id);
        Assert.Equal(120, overlays.Height(PcTestMonitors.Main.Id));
        Assert.Equal(2, native.Calls.Count);
    }

    [Fact]
    public void A_shrink_that_is_taken_back_before_the_motion_ends_makes_no_call()
    {
        var (overlays, _, native, _) = Make();
        overlays.Show(PcTestMonitors.Main, PillBandState.Card, cardHeight: 300);
        native.Calls.Clear();

        overlays.State(PcTestMonitors.Main.Id, PillBandState.Pill);
        overlays.State(PcTestMonitors.Main.Id, PillBandState.Card, cardHeight: 300);
        overlays.MotionEnded(PcTestMonitors.Main.Id);

        Assert.Empty(native.Calls);
        Assert.Equal(460, overlays.Height(PcTestMonitors.Main.Id));
    }

    [Fact]
    public void A_travel_s_frames_make_no_window_calls_the_region_empties_at_its_start_and_returns_once_it_settles()
    {
        var (overlays, bands, native, grab) = Make();
        overlays.Show(PcTestMonitors.Main);
        var input = bands.Opened[0].Band.Input;
        overlays.Shape(PcTestMonitors.Main.Id, PillShape.Resting(new PillRect(16, 8, 440, 48)), now);
        native.Calls.Clear();

        var travel = Travel(now);
        overlays.Shape(PcTestMonitors.Main.Id, travel, now);
        Assert.True(grab.Active);
        Assert.Equal(new[] { $"region {input:x} empty" }, native.Calls);

        // The view draws every frame from the curve itself; nothing here runs per frame, so no call happens until the settle.
        overlays.Shape(PcTestMonitors.Main.Id, travel, now + 0.5);

        Assert.False(grab.Active);
        Assert.Equal(new[] { $"region {input:x} empty", $"region {input:x} 738,54,444,52 r52" }, native.Calls);
    }

    [Fact]
    public void Only_the_region_test_publishes_the_grab_zone_and_only_from_the_pill_s_own_monitor()
    {
        var (pixel, _, _, pixelGrab) = Make(OverlayHitTestKind.Pixel);
        pixel.Show(PcTestMonitors.Main);
        pixel.Shape(PcTestMonitors.Main.Id, Travel(now), now);
        Assert.False(pixelGrab.Active);

        var (region, _, _, regionGrab) = Make();
        region.Show(PcTestMonitors.Right);
        region.Shape(PcTestMonitors.Right.Id, Travel(now), now, owner: false);
        Assert.False(regionGrab.Active);
        region.Shape(PcTestMonitors.Right.Id, Travel(now), now, owner: true);
        Assert.True(regionGrab.Active);
    }

    [Fact]
    public void The_style_flip_band_is_one_window_and_takes_pointer_records()
    {
        var (overlays, bands, native, _) = Make(OverlayHitTestKind.StyleFlip);
        overlays.Show(PcTestMonitors.Main);
        overlays.Shape(PcTestMonitors.Main.Id, PillShape.Resting(new PillRect(740, 8, 440, 48)), now);

        overlays.Pointer(960, 30, now);
        ((StyleFlipHitTest)overlays.HitTest(PcTestMonitors.Main.Id)!).Process();

        Assert.Equal((nint)0, bands.Opened[0].Band.Shadow);
        Assert.Contains($"through {bands.Opened[0].Band.Input:x} False", native.Calls);
    }

    [Fact]
    public void The_topmost_order_is_set_again_only_on_show_an_ask_or_a_cover()
    {
        var (overlays, bands, _, _) = Make();
        overlays.Show(PcTestMonitors.Main);
        var band = bands.Opened[0].Band;

        overlays.Shape(PcTestMonitors.Main.Id, Travel(now), now);
        overlays.State(PcTestMonitors.Main.Id, PillBandState.Dragging);
        Assert.Equal(1, band.Reasserts);

        overlays.Reassert();
        overlays.Covered(PcTestMonitors.Main.Id);
        Assert.Equal(3, band.Reasserts);
    }

    [Fact]
    public void A_band_closes_2_s_after_the_pill_hides_unless_it_shows_again()
    {
        var (overlays, bands, native, grab) = Make();
        overlays.Show(PcTestMonitors.Main);
        overlays.Shape(PcTestMonitors.Main.Id, Travel(now), now);
        var band = bands.Opened[0].Band;

        overlays.Hide(PcTestMonitors.Main.Id);
        Assert.False(grab.Active);
        Assert.Null(native.Regions[band.Input]);
        now += 1.5;
        Assert.True(overlays.Tick());
        overlays.Show(PcTestMonitors.Main);
        now += 1;
        Assert.False(overlays.Tick());
        Assert.False(band.Closed);

        overlays.Hide(PcTestMonitors.Main.Id);
        now += 2;
        Assert.False(overlays.Tick());
        Assert.True(band.Closed);
        Assert.Empty(overlays.Monitors);
    }

    [Fact]
    public void A_display_change_moves_a_band_to_its_monitor_s_new_work_area_and_an_unplug_closes_it()
    {
        var (overlays, bands, native, _) = Make();
        overlays.Show(PcTestMonitors.Main);
        overlays.Show(PcTestMonitors.Right);
        var main = bands.Opened[0].Band;
        var right = bands.Opened[1].Band;
        native.Calls.Clear();
        var taller = PcTestMonitors.Main with { WorkArea = new Marrow.Core.Port.PcControl.PcRect(0, 48, 1920, 1032) };

        overlays.Displays(new MonitorChange([], [PcTestMonitors.Right], [taller]));

        Assert.True(right.Closed);
        Assert.False(main.Closed);
        Assert.Equal(new PixelRect(0, 48, 1920, 120), native.Bounds[main.Input]);
        Assert.Equal(new[] { PcTestMonitors.Main.Id }, overlays.Monitors);
    }
}
