// PORT-ONLY: Tactus pill v2, the region hit test, the default (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-22 item 5; DESIGN.md
// 10.6 "Hit testing" 1). The input window is not layered and its window region is the resting pill's or card's shape
// inflated by 2 DIP, as a round rect in that monitor's physical pixels, so Windows hit-tests it natively for mouse, pen and
// touch and outside it the band does not exist for input. The region changes only at rest and at a state change, with one
// SetWindowRgn, never per frame; while the shape moves the region is empty (a region that lags the drawn shape would take
// clicks meant for the app beneath) and a mouse press on the moving shape is the hook's grab in flight. Nothing changes
// while a mouse button is held in another app; the newest change waits for its release. A change equal to the region in
// force makes no call.
using Avalonia;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Port.PcControl.Overlay;

public sealed class RegionHitTest(IOverlayBandNative native) : IOverlayHitTest
{
    private nint window;
    private OverlayBandGeometry band;
    private bool held;
    private (PixelRect? Rect, int Corner)? pending;
    private (PixelRect? Rect, int Corner)? applied;
    private PillShape? resting;

    public OverlayHitTestKind Kind => OverlayHitTestKind.Region;

    public uint InputStyles => OverlayStyles.Extended(OverlayRole.Input);

    public bool SeparateShadow => true;

    public bool GrabsInFlight => true;

    /// <summary>The region in force (client pixels; null = empty), for the tests.</summary>
    public PixelRect? Region => applied?.Rect;

    public void Attach(nint handle, OverlayBandGeometry geometry)
    {
        window = handle;
        band = geometry;
        applied = null;
        Set(null, 0);
    }

    public void Moved(OverlayBandGeometry geometry)
    {
        band = geometry;
        if (resting is { } shape) Rest(shape);
    }

    public void Rest(PillShape shape)
    {
        resting = shape;
        var rect = shape.Final.Inflate(PillShape.HitInflate);
        var corner = (int)Math.Round(2 * (PillShape.Radius + PillShape.HitInflate) * band.Scale);
        Set(band.ToClient(rect), corner);
    }

    public void Moving(PillShape shape)
    {
        resting = null;
        Set(null, 0);
    }

    public void Hidden()
    {
        resting = null;
        Set(null, 0);
    }

    public void ButtonHeldElsewhere(bool isHeld)
    {
        held = isHeld;
        if (held || pending is not { } change) return;
        pending = null;
        Apply(change.Rect, change.Corner);
    }

    public void Pointer(int x, int y, double seconds)
    {
    }

    public void Dispose()
    {
    }

    private void Set(PixelRect? rect, int corner)
    {
        if (held)
        {
            pending = (rect, corner);
            return;
        }
        pending = null;
        Apply(rect, corner);
    }

    private void Apply(PixelRect? rect, int corner)
    {
        if (window == 0 || applied == (rect, corner)) return;
        applied = (rect, corner);
        native.SetRegion(window, rect, corner);
    }
}
