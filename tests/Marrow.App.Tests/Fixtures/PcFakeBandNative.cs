// PORT-ONLY: Tactus (ED-NEW-pcctl-23). A fake of a band's Win32 calls (region, click-through flip, bounds) that records
// them; nothing touches a real window.
using Avalonia;
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakeBandNative : IOverlayBandNative
{
    public List<string> Calls { get; } = [];

    public Dictionary<nint, bool> ClickThrough { get; } = [];

    public Dictionary<nint, PixelRect?> Regions { get; } = [];

    public Dictionary<nint, PixelRect> Bounds { get; } = [];

    public void SetRegion(nint window, PixelRect? roundRect, int corner)
    {
        Regions[window] = roundRect;
        Calls.Add(roundRect is { } r ? $"region {window:x} {r.X},{r.Y},{r.Width},{r.Height} r{corner}" : $"region {window:x} empty");
    }

    public void SetClickThrough(nint window, bool clickThrough)
    {
        lock (Calls)
        {
            ClickThrough[window] = clickThrough;
            Calls.Add($"through {window:x} {clickThrough}");
        }
    }

    public void SetBounds(nint window, PixelRect rect)
    {
        Bounds[window] = rect;
        Calls.Add($"bounds {window:x} {rect.X},{rect.Y},{rect.Width},{rect.Height}");
    }
}
