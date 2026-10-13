// PORT-ONLY: Link A's expanded plain outline. Reads the retained capsule's eased usage; the same CapsulePainter and glass material, no copied geometry.
// ED-NEW-perf-idle-3: it repaints when the capsule it reads repaints (every change of what it reads goes through that repaint), and when its own layout or the
// theme changes; it has no frame loop of its own.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.Core.Port;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Link;
internal sealed class LinkedCapsulePaint : Control
{
    private readonly AccountCapsule source;
    private readonly CapsulePaintState state = new() { Style = "plain", Mono = "" };
    internal LinkedCapsulePaint(AccountCapsule source)
    {
        this.source = source; Height = 22; IsHitTestVisible = false;
        AttachedToVisualTree += (_, _) => { source.Repainted += Redraw; InvalidateVisual(); };
        DetachedFromVisualTree += (_, _) => source.Repainted -= Redraw;
        LayoutUpdated += (_, _) => { if (Bounds.Width != state.Width) InvalidateVisual(); }; // drawn at its width: only a new width draws it again
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    private void Redraw() => InvalidateVisual();
    /// <summary>How many times it has been drawn (tests).</summary>
    internal int Renders { get; private set; }
    public override void Render(DrawingContext context)
    {
        Renders++;
        state.Width = Bounds.Width; state.Arc = source.ArcNow; state.Light = source.LightNow; state.Hot = source.Hot;
        state.Dark = PlainControls.IsDark(ActualThemeVariant); state.KeyboardFocus = source.Face.IsKeyboardFocusWithin;
        CapsulePainter.Paint(context, state);
    }
}
