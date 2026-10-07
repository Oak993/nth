// PORT-ONLY: MA-4-2. Input-transparent canvas; the capsule remains the click/focus target below it.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port.MultiAccount.Handshake;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal sealed class HandshakeCanvas : Control
{
    private readonly Lock gate = new();
    private readonly Queue<HandshakeDrawOperation> pool = new();
    private readonly Action<HandshakeDrawOperation> returnOperation;
    private HandshakeDrawResources? resources;
    private HandshakeFrameSnapshot? frame;
    private bool dark;
    internal HandshakeCanvas()
    {
        IsHitTestVisible = false; ClipToBounds = false; returnOperation = Return;
        for (var i = 0; i < 3; i++) pool.Enqueue(new HandshakeDrawOperation(returnOperation));
        DetachedFromVisualTree += (_, _) => { resources?.Release(); resources = null; };
    }
    internal void Apply(HandshakeMotionPlan next, bool isDark)
        => Apply(new HandshakeFrameSnapshot(next.Phase, next.VirtualMilliseconds, next.Progress, next.Status, next.ShowTick, next.UseCrossfade, next.OutgoingColor, next.IncomingColor), isDark);
    internal void Apply(HandshakeFrameSnapshot next, bool isDark)
    {
        frame = next; dark = isDark; InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); if (frame is not { } value || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (resources is null || !resources.Matches(Bounds.Width, Bounds.Height, value)) { resources?.Release(); resources = new HandshakeDrawResources(Bounds.Width, Bounds.Height, value); }
        HandshakeDrawOperation operation; lock (gate) operation = pool.Count == 0 ? new HandshakeDrawOperation(returnOperation) : pool.Dequeue();
        operation.Reuse(new Rect(Bounds.Size), resources, value, dark); context.Custom(operation);
    }
    private void Return(HandshakeDrawOperation operation) { lock (gate) pool.Enqueue(operation); }
}
