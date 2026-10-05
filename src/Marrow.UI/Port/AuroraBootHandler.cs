using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;

namespace Marrow.UI.Port;

internal sealed class AuroraBootHandler : CompositionCustomVisualHandler
{
    private readonly AuroraBootPainter painter = new();
    private AuroraBootSnapshot? snapshot;
    private bool stopped;
    public override void OnMessage(object message)
    {
        if (message is AuroraBootSnapshot next) { snapshot = next; Invalidate(); }
        else if (message is false) { stopped = true; painter.Dispose(); }
    }
    public override Rect GetRenderBounds() => snapshot is { } s ? new Rect(s.Size) : default;
    public override void OnRender(ImmediateDrawingContext context)
    {
        if (stopped || snapshot is not { } s) return;
        var begin = TimeProvider.System.GetTimestamp(); var bytes = GC.GetAllocatedBytesForCurrentThread();
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        long drawingBytes;
        using (var lease = feature.Lease())
        {
            var startBytes = GC.GetAllocatedBytesForCurrentThread();
            painter.Draw(lease.SkCanvas, s.Size, s.Window, s.Frame, s.Seconds, s.Dark, s.ReduceMotion, lease.CurrentOpacity, lease.GrContext);
            drawingBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        }
        s.Telemetry?.Record(begin, TimeProvider.System.GetTimestamp(), drawingBytes, GC.GetAllocatedBytesForCurrentThread() - bytes);
    }
}
