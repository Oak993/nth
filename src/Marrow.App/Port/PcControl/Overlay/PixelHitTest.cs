// PORT-ONLY: Tactus pill v2, the per-pixel hit test, a named fallback (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-22 item 5;
// DESIGN.md 10.6 "Hit testing" 2). NP-15 (np15-pixel) tells whether Avalonia's composited window honours per-pixel alpha
// for hit testing; if it does, this mode needs neither a region nor the hook path.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>
/// The per-pixel hit test (DESIGN.md 10.6 "Hit testing" 2): a layered input window that is not click-through, so Windows
/// gives it input exactly where its pixels are not transparent; no region and no hook path. The shadow is drawn by its own
/// click-through window so a soft shadow pixel never takes a click.
/// </summary>
public sealed class PixelHitTest(IOverlayBandNative native) : IOverlayHitTest
{
    private nint window;

    public OverlayHitTestKind Kind => OverlayHitTestKind.Pixel;

    public uint InputStyles => OverlayStyles.Extended(OverlayRole.Input) | OverlayStyles.Layered;

    public bool SeparateShadow => true;

    public bool GrabsInFlight => false;

    public void Attach(nint handle, OverlayBandGeometry geometry)
    {
        window = handle;
        native.SetClickThrough(window, false);
    }

    public void Moved(OverlayBandGeometry geometry)
    {
    }

    public void Rest(PillShape shape)
    {
    }

    public void Moving(PillShape shape)
    {
    }

    public void Hidden()
    {
    }

    public void ButtonHeldElsewhere(bool held)
    {
    }

    public void Pointer(int x, int y, double seconds)
    {
    }

    public void Dispose()
    {
    }
}
