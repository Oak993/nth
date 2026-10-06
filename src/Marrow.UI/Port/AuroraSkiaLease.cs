using Avalonia.Media;
using SkiaSharp;

namespace Marrow.UI.Port;

// Value-type equivalent of Avalonia 12.1.2 DrawingContextImpl.SkiaLeaseFeature.ApiLease.
// It keeps the backend's lease guard and matrix restoration without the two per-frame wrappers.
internal readonly struct AuroraSkiaLease(object owner, SKCanvas canvas, double opacity) : IDisposable
{
    private readonly SKMatrix matrix = canvas.TotalMatrix;
    internal SKCanvas Canvas => canvas;
    internal double Opacity => opacity;
    internal static bool TryBorrow(ImmediateDrawingContext context, out AuroraSkiaLease lease) => AuroraSkiaLeaseAccess.TryBorrow(context, out lease);
    public void Dispose()
    {
        try { canvas.SetMatrix(matrix); }
        finally { AuroraSkiaLeaseAccess.Release(owner); }
    }
}
