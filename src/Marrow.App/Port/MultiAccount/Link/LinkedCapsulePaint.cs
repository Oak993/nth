// PORT-ONLY: Link A's expanded plain outline. Reads the retained capsule's eased usage; the same CapsulePainter and glass material, no copied geometry.
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
    private IFrameSource? frames;
    private Action<TimeSpan>? frame;
    internal LinkedCapsulePaint(AccountCapsule source)
    {
        this.source = source; Height = 22; IsHitTestVisible = false;
        AttachedToVisualTree += (_, _) => Tick(default);
        DetachedFromVisualTree += (_, _) => { if (frame is not null) frames?.CancelFrame(frame); frame = null; };
        LayoutUpdated += (_, _) => InvalidateVisual();
    }
    private void Tick(TimeSpan _)
    {
        frame = null; InvalidateVisual();
        if (!FrameClock.IsBound || CapsuleMotion.Reduce(this)) return;
        frames = FrameClock.Source.Current; frame = Tick; frames.RequestFrame(frame);
    }
    public override void Render(DrawingContext context)
    {
        state.Width = Bounds.Width; state.Arc = source.ArcNow; state.Light = source.LightNow; state.Hot = source.Hot;
        state.Dark = PlainControls.IsDark(ActualThemeVariant); state.KeyboardFocus = source.Face.IsKeyboardFocusWithin;
        CapsulePainter.Paint(context, state);
    }
}
