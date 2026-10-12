// PORT-ONLY: Tactus (ED-NEW-pcctl-23). Opens fake bands with made-up handles and records where each was placed.
using Avalonia;
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakePillBands : IPillBands
{
    private nint next = 0x1000;

    public List<(OverlayMonitor Monitor, PcFakePillBand Band, PixelRect Rect, OverlayHitTestKind Kind)> Opened { get; } = [];

    public IPillBand Open(OverlayMonitor monitor, IOverlayHitTest hitTest, PixelRect rect)
    {
        var band = new PcFakePillBand(next, hitTest.SeparateShadow ? next + 1 : 0);
        next += 0x10;
        Opened.Add((monitor, band, rect, hitTest.Kind));
        return band;
    }
}
