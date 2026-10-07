using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;

namespace Marrow.UI.Port;

// PORT-ONLY: OD-0113. The compositor drives the continuous motion without UI-thread frame callbacks or snapshots.
internal sealed class AuroraLoaderHandler : CompositionCustomVisualHandler
{
    private AuroraLoaderSnapshot? snapshot;
    private readonly AuroraLoaderPainter painter = new();
    private bool requested, stopped, drawn = true;

    public override void OnMessage(object message)
    {
        if (message is AuroraLoaderSnapshot next) { snapshot = next; Invalidate(); Request(); }
        else if (message is false) { stopped = true; painter.Dispose(); }
    }

    public override Rect GetRenderBounds() => snapshot is { } value ? new Rect(value.Size) : default;

    public override void OnAnimationFrameUpdate()
    {
        requested = false;
        if (stopped || snapshot is null) return;
        Invalidate();
        if (drawn && Moving(snapshot)) Request();
    }

    private static bool Moving(AuroraLoaderSnapshot s) => s.Finished is not { } end || s.Clock.GetElapsedTime(end).TotalSeconds < .15;
    private void Request()
    {
        if (requested || stopped) return;
        requested = true; drawn = false;
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (stopped || snapshot is not { } s) return;
        drawn = true;
        var elapsed = s.Clock.GetElapsedTime(s.Started).TotalSeconds;
        var fade = s.Finished is { } end ? 1 - AuroraLoaderMotion.Smooth(s.Clock.GetElapsedTime(end).TotalSeconds / .15) : 1;
        var alpha = AuroraLoaderMotion.Visibility(elapsed) * fade;
        var begin = s.Telemetry is null ? 0 : TimeProvider.System.GetTimestamp();
        var handlerBytes = s.Telemetry is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
        long painterBytes = 0;
        var painted = false;
        if (alpha > 0 && AuroraSkiaLease.TryBorrow(context, out var direct))
        {
            using (direct)
            {
                var bytes = s.Telemetry is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
                painter.Draw(direct.Canvas, s.Kind, s.Size.Width, s.Size.Height, elapsed, s.Dark, s.ReduceMotion, alpha * direct.Opacity);
                if (s.Telemetry is not null) painterBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                painted = true;
            }
        }
        else if (alpha > 0 && context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is { } feature)
        {
            using var lease = feature.Lease();
            var bytes = s.Telemetry is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
            painter.Draw(lease.SkCanvas, s.Kind, s.Size.Width, s.Size.Height, elapsed, s.Dark, s.ReduceMotion, alpha * lease.CurrentOpacity);
            if (s.Telemetry is not null) painterBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            painted = true;
        }
        if (Moving(s)) Request();
        if (painted) s.Telemetry?.Record(begin, TimeProvider.System.GetTimestamp(), painterBytes, GC.GetAllocatedBytesForCurrentThread() - handlerBytes);
    }
}
