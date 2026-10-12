// PORT-ONLY: the monitors the bands live on (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Displays", 10.6): the work area in
// DIP, the band's place, DIP to pixels rounded outward and back, the monitor at a point (else the nearest), the main one,
// and what changed between two readings (added, unplugged, a DPI or work-area change) by device identity.
using Avalonia;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class MonitorMapTests
{
    [Fact]
    public void A_150_percent_monitor_s_work_area_and_band_are_in_its_own_DIP()
    {
        var right = PcTestMonitors.Right;

        Assert.Equal(1.5, right.Scale);
        Assert.Equal(new PillArea(0, 48, 1920, 1032), right.WorkDip);
        Assert.Equal(new PixelPoint(1920, 72), right.Band.ScreenOrigin);
        Assert.Equal(new Point(0, 48), right.Band.DipOrigin);
    }

    [Fact]
    public void Dip_rects_go_to_screen_pixels_rounded_outward_and_back()
    {
        var right = PcTestMonitors.Right;

        Assert.Equal(new PixelRect(1920 + 1110, 84, 660, 72), right.ToScreen(new PillRect(740, 56, 440, 48)));
        Assert.Equal(new PixelRect(1920 + 1110, 84, 661, 73), right.ToScreen(new PillRect(740.1, 56.2, 440, 48)));
        Assert.Equal(new PillRect(740, 56, 440, 48), right.ToDip(new PcRect(1920 + 1110, 84, 660, 72)));
    }

    [Fact]
    public void The_monitor_at_a_point_else_the_nearest_and_the_main_one()
    {
        var source = new PcFakeOverlayMonitors { Now = [PcTestMonitors.Right, PcTestMonitors.Main] };
        var map = new MonitorMap(source);
        map.Refresh();

        Assert.Same(PcTestMonitors.Main, map.Main);
        Assert.Same(PcTestMonitors.Right, map.At(new PcPoint(2000, 10)));
        Assert.Same(PcTestMonitors.Main, map.At(new PcPoint(100, 1079)));
        Assert.Same(PcTestMonitors.Right, map.At(new PcPoint(2000, 1700)));
        Assert.Same(PcTestMonitors.Main, map.At(new PcPoint(-50, 500)));
        Assert.Same(PcTestMonitors.Right, map.ById(PcTestMonitors.Right.Id.ToLowerInvariant()));
        Assert.Null(map.ById("gone"));
    }

    [Fact]
    public void A_reading_says_what_was_added_unplugged_or_changed_by_device_identity()
    {
        var source = new PcFakeOverlayMonitors { Now = [PcTestMonitors.Main] };
        var map = new MonitorMap(source);
        var first = map.Refresh();
        Assert.Single(first.Added);

        source.Now = [PcTestMonitors.Main, PcTestMonitors.Right];
        Assert.Equal(new[] { PcTestMonitors.Right }, map.Refresh().Added);

        var rescaled = PcTestMonitors.Right with { Dpi = 192, WorkArea = new PcRect(1920, 0, 2880, 1560) };
        source.Now = [PcTestMonitors.Main, rescaled];
        var changed = map.Refresh();
        Assert.Equal(new[] { rescaled }, changed.Changed);
        Assert.Empty(changed.Added);

        source.Now = [PcTestMonitors.Main];
        var unplugged = map.Refresh();
        Assert.Equal(new[] { rescaled }, unplugged.Removed);

        Assert.False(map.Refresh().Any);
    }
}
