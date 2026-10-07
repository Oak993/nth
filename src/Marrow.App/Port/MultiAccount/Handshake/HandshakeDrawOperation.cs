// PORT-ONLY: MA-4-2, reusable composition-thread operation, same pool lifetime as JewelKnobOperation.
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.MultiAccount.Handshake;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal sealed class HandshakeDrawOperation(Action<HandshakeDrawOperation> giveBack) : ICustomDrawOperation
{
    private HandshakeDrawResources? resources;
    private HandshakeFrameSnapshot? frame;
    private bool dark;
    private int given;
    public Rect Bounds { get; private set; }
    internal void Reuse(Rect bounds, HandshakeDrawResources next, HandshakeFrameSnapshot plan, bool isDark)
    {
        Bounds = bounds; resources = next; next.Acquire(); frame = plan; dark = isDark; Volatile.Write(ref given, 0);
    }
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { if (Interlocked.Exchange(ref given, 1) != 0) return; resources?.Release(); resources = null; frame = null; giveBack(this); }
    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>() ?? throw new InvalidOperationException("Handshake requires the Skia renderer.");
        using var lease = feature.Lease(); if (resources is null || frame is not { } value) return;
        if (lease.GrContext is not null) GpuProgramCache.ForContext(lease.GrContext);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        resources.Painter.Draw(lease.SkCanvas, value, dark, lease.CurrentOpacity);
        if (lease.GrContext is null) return;
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (Interlocked.Increment(ref gpuFrames) == 1) Volatile.Write(ref firstGpuMilliseconds, ms);
        if (ms > maxGpuMilliseconds) Volatile.Write(ref maxGpuMilliseconds, ms);
    }
    private static long gpuFrames;
    private static double firstGpuMilliseconds, maxGpuMilliseconds;
    internal static long GpuFrames => Interlocked.Read(ref gpuFrames);
    internal static double FirstGpuMilliseconds => Volatile.Read(ref firstGpuMilliseconds);
    internal static double MaxGpuMilliseconds => Volatile.Read(ref maxGpuMilliseconds);
}
