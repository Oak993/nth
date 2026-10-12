// PORT-ONLY: Tactus (ED-NEW-pcctl-24). The pill host and its fakes as one test uses them: the host, the overlays it shows
// the pill on, the fake bands and Win32 calls, the fake band surfaces with their glass, the monitor choice, and the clock.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed record PcPillHostRig(TactusPillHost Host, PillOverlays Overlays, PcFakePillBands Bands, PcFakeBandNative Native, PcFakePillSurfaces Surfaces, PillScreens Screens, ManualTimeProvider Clock)
{
    /// <summary>The host on two fake monitors (main and right) with fake bands, Win32 calls and surfaces, on a manual clock.</summary>
    public static PcPillHostRig Create(OverlayHitTestKind kind = OverlayHitTestKind.Region, bool glass = true)
    {
        var clock = new ManualTimeProvider();
        var bands = new PcFakePillBands();
        var native = new PcFakeBandNative();
        var overlays = new PillOverlays(bands, native, kind, () => clock.GetTimestamp() / (double)clock.TimestampFrequency,
            makeHitTest: k => k == OverlayHitTestKind.StyleFlip ? new StyleFlipHitTest(native, runThread: false) : OverlayHitTests.Create(k, native));
        var map = new MonitorMap(new PcFakeOverlayMonitors { Now = [PcTestMonitors.Main, PcTestMonitors.Right] });
        map.Refresh();
        var surfaces = new PcFakePillSurfaces(shadowWindow: kind != OverlayHitTestKind.StyleFlip, glass: glass);
        var screens = new PillScreens();
        var host = new TactusPillHost(overlays, surfaces, map, screens, clock: clock, driver: Marrow.UI.Port.PcControl.PillDriverKind.RenderThread);
        return new PcPillHostRig(host, overlays, bands, native, surfaces, screens, clock);
    }

    public double Now => Clock.GetTimestamp() / (double)Clock.TimestampFrequency;
}
