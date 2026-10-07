// PORT-ONLY: MA-4-2, OD-0108/ED-252. Uses the window's registered offscreen GPU warm-up and program cache.
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.MultiAccount.Handshake;
using SkiaSharp;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal static class HandshakeGpuProgram
{
    private static readonly Lock gate = new();
    private static IDisposable? registration;
    internal static void EnsureRegistered()
    {
        lock (gate) registration ??= GpuWarmup.Register(GpuProgram.Draw("Handshake neural filaments and bloom", FirstRunSteps.Graphics, Warm));
    }
    internal static void Unregister() { lock (gate) { registration?.Dispose(); registration = null; } }
    private static void Warm(GpuWarmContext context)
    {
        var pixels = (int)Math.Ceiling(256 * context.Scaling); var info = new SKImageInfo(pixels, pixels, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = context.Gpu is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
        if (surface is null) throw new InvalidOperationException("Handshake warm-up could not create its offscreen surface.");
        using var painter = new HandshakePainter(256, 144, (240, 105, 90), (58, 214, 168)); surface.Canvas.Scale((float)context.Scaling);
        painter.Draw(surface.Canvas, new HandshakeFrameSnapshot(Marrow.Core.Accounts.HandshakePhase.InSync, 3500, .5, "", false, false, (240, 105, 90), (58, 214, 168)), true); surface.Canvas.Flush(); context.Gpu?.Flush();
    }
}
