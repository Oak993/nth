// REVIEW fixture: a software Skia lease over a given surface (no GPU context), to drive GlassMaterial's private Backdrop directly.
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.UI.Tests.Fixtures;

internal sealed class GlassFakeLease(SKSurface surface, double opacity = 1) : ISkiaSharpApiLease
{
    public SKCanvas SkCanvas => surface.Canvas;
    public GRContext? GrContext => null;
    public SKSurface? SkSurface => surface;
    public double CurrentOpacity => opacity;
    public ISkiaSharpPlatformGraphicsApiLease? TryLeasePlatformGraphicsApi() => null;
    public void Dispose() { }
}
