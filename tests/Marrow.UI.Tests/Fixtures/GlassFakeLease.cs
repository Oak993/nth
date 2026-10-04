// REVIEW fixture: a software Skia lease over a given surface (no GPU context), to drive GlassMaterial's private Backdrop directly.
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.UI.Tests.Fixtures;

internal sealed class GlassFakeLease(SKSurface surface) : ISkiaSharpApiLease
{
    public SKCanvas SkCanvas => surface.Canvas;
    public GRContext? GrContext => null;
    public SKSurface? SkSurface => surface;
    public double CurrentOpacity => 1;
    public ISkiaSharpPlatformGraphicsApiLease? TryLeasePlatformGraphicsApi() => null;
    public void Dispose() { }
}
