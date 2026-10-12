// PORT-ONLY: Tactus pill v2, the pill's shadow band on one monitor (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-20; DESIGN.md
// 10.6 "Hit testing" 1). With the region and per-pixel hit tests the input window takes input only on the pill's shape,
// and a region also clips its drawing, so a second band, never taking input, draws the rest. Never shown by tests.
using Avalonia.Automation;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>
/// The click-through band beside the input window (DESIGN.md 10.6 "Hit testing" 1): same place and size, layered and
/// WS_EX_TRANSPARENT, so it never takes input; it draws the pill's shadow, the rubber-band room and, while the shape moves
/// (the input window's region is empty then), the moving pill. Hidden from UI Automation: the input window is the pane.
/// </summary>
public sealed class ShadowOverlayWindow : TactusOverlayWindow
{
    public ShadowOverlayWindow(IOverlayNative? native = null)
        : base(OverlayRole.Shadow, native)
    {
        Title = "Tactus shadow";
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }
}
