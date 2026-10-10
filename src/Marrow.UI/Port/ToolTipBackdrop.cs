using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace Marrow.UI.Port;

// Apple UI Kit help-tag material: native compositor backdrop, never screen capture.
// Its clip follows only the card; the original border still owns fill/text/shadows.
internal sealed class ToolTipBackdrop : Control
{
    private readonly Border card;
    private PopoverGlass? glass;
    private PopupRoot? root;
    private bool light;
    private double attachedScale;
    private bool pumping;
    public double Sigma => 45;
    public double Saturation => ActualThemeVariant == ThemeVariant.Light ? 0.8 : 1;
    internal string? Failure => glass?.Failure;
    internal bool Attached => glass?.Attached == true;
    internal ToolTipBackdrop(Border card)
    {
        this.card = card; IsHitTestVisible = false;
        LayoutUpdated += (_, _) => Synchronize();
        ActualThemeVariantChanged += (_, _) => { Release(); Synchronize(); };
        DetachedFromVisualTree += (_, _) => Release();
    }
    private void Release()
    {
        if (root is not null) root.ScalingChanged -= OnScaling;
        glass?.Dispose(); glass = null; root = null;
    }
    private void OnScaling(object? sender, EventArgs args) => Synchronize();
    private void Synchronize()
    {
        if (TopLevel.GetTopLevel(this) is not PopupRoot popup || card.Bounds.Width <= 0 || card.Bounds.Height <= 0) return;
        var isLight = ActualThemeVariant == ThemeVariant.Light;
        if (root != popup || light != isLight || attachedScale != popup.RenderScaling)
        {
            Release(); root = popup; light = isLight; attachedScale = popup.RenderScaling;
            root.ScalingChanged += OnScaling;
        }
        glass ??= PopoverGlass.ForToolTip(popup, Sigma * popup.RenderScaling, Saturation);
        if (card.TranslatePoint(default, popup) is not { } origin) return;
        var scale = popup.RenderScaling;
        glass.Update(new(new Rect(origin.X * scale, origin.Y * scale, card.Bounds.Width * scale, card.Bounds.Height * scale), 0, default, 0, 0, 1));
        if (!glass.Attached && glass.Failure is null && !pumping)
        {
            pumping = true;
            popup.RequestAnimationFrame(_ => { pumping = false; if (root == popup) Synchronize(); });
        }
    }
}
