using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;

namespace Marrow.UI.Port;

internal sealed class AuroraBootHandler : CompositionCustomVisualHandler
{
    private readonly AuroraBootPainter painter = new();
    private AuroraBootSnapshot? snapshot;
    private bool stopped, requested, drawn = true;
    private AuroraBootSequence? sequence;
    public override void OnMessage(object message)
    {
        if (message is AuroraBootSnapshot next)
        {
            if (snapshot is not { } old || old.Window != next.Window || old.ReduceMotion != next.ReduceMotion)
                sequence = new AuroraBootSequence(next.Window, next.ReduceMotion);
            snapshot = next; Invalidate(); Request();
        }
        else if (message is false) { stopped = true; painter.Dispose(); }
    }
    public override Rect GetRenderBounds() => snapshot is { } s ? new Rect(s.Size) : default;
    public override void OnAnimationFrameUpdate()
    {
        requested = false;
        if (stopped || snapshot is null) return;
        Invalidate();
        if (drawn) Request();
    }
    private void Request()
    {
        if (requested || stopped) return;
        requested = true; drawn = false; RegisterForNextAnimationFrameUpdate();
    }
    public override void OnRender(ImmediateDrawingContext context)
    {
        if (stopped || snapshot is not { } s) return;
        drawn = true;
        var elapsed = s.Clock.GetElapsedTime(s.Started).TotalSeconds;
        var frame = sequence!.At(elapsed, s.ReadyAt);
        using var work = AuroraBootTrace.Begin("Boot: compositor draw");
        var begin = TimeProvider.System.GetTimestamp(); var bytes = GC.GetAllocatedBytesForCurrentThread();
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        long drawingBytes;
        using (var lease = feature.Lease())
        {
            var startBytes = GC.GetAllocatedBytesForCurrentThread();
            painter.Draw(lease.SkCanvas, s.Size, s.Window, frame, elapsed, s.Dark, s.ReduceMotion, lease.CurrentOpacity, lease.GrContext);
            drawingBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        }
        if (!frame.StageClosed) Request();
        s.Telemetry?.Record(begin, TimeProvider.System.GetTimestamp(), drawingBytes, GC.GetAllocatedBytesForCurrentThread() - bytes);
    }
}
