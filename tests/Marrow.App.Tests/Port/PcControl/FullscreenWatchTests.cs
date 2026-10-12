// PORT-ONLY: full-screen apps (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Auto-dodge" rows 4 and 5, critique C19):
// presentation mode shades every monitor, exclusive full screen only the foreground window's, a caption-less window that
// fills its monitor is borderless full screen, Tactus's overlays never count, and the state is read at most every 500 ms.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class FullscreenWatchTests
{
    [Fact]
    public void Presentation_mode_shades_every_monitor_and_exclusive_full_screen_only_its_own()
    {
        var onRight = new PcRect(1920, 0, 2880, 1620);

        Assert.Equal(MonitorShade.NothingDrawn, FullscreenWatch.Classify(4, null, true, PcTestMonitors.Main));
        Assert.Equal(MonitorShade.NothingDrawn, FullscreenWatch.Classify(4, null, true, PcTestMonitors.Right));
        Assert.Equal(MonitorShade.NothingDrawn, FullscreenWatch.Classify(3, onRight, false, PcTestMonitors.Right));
        Assert.Equal(MonitorShade.Clear, FullscreenWatch.Classify(3, onRight, false, PcTestMonitors.Main));
    }

    [Fact]
    public void A_caption_less_window_that_fills_its_monitor_is_borderless_full_screen()
    {
        Assert.Equal(MonitorShade.BorderlessFullscreen, FullscreenWatch.Classify(2, new PcRect(0, 0, 1920, 1080), false, PcTestMonitors.Main));
        Assert.Equal(MonitorShade.Clear, FullscreenWatch.Classify(2, new PcRect(0, 0, 1920, 1080), true, PcTestMonitors.Main));
        Assert.Equal(MonitorShade.Clear, FullscreenWatch.Classify(5, new PcRect(0, 0, 1920, 1032), false, PcTestMonitors.Main));
        Assert.Equal(MonitorShade.Clear, FullscreenWatch.Classify(2, new PcRect(0, 0, 1920, 1080), false, PcTestMonitors.Right));
    }

    [Fact]
    public void The_state_is_read_at_most_every_500_ms_and_the_overlays_never_count()
    {
        var source = new PcFakeFullscreen { State = 2, Front = 0x99, Rect = new PcRect(0, 0, 1920, 1080), Caption = false };
        var now = 10.0;
        var watch = new FullscreenWatch(source, () => now);
        watch.Ignore.Add(0x99);

        Assert.Equal(MonitorShade.Clear, watch.State(PcTestMonitors.Main));
        source.Front = 0x50;
        now += 0.2;
        Assert.Equal(MonitorShade.Clear, watch.State(PcTestMonitors.Main));
        Assert.Equal(1, source.Reads);

        now += 0.4;
        Assert.Equal(MonitorShade.BorderlessFullscreen, watch.State(PcTestMonitors.Main));
        Assert.Equal(2, source.Reads);

        source.Rect = new PcRect(100, 100, 800, 600);
        watch.Invalidate();
        Assert.Equal(MonitorShade.Clear, watch.State(PcTestMonitors.Main));
        Assert.Equal(3, source.Reads);
    }
}
