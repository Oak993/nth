using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Port;

// PORT-ONLY: OD-0113 / OD-0108. Fixed-gradient/path GPU programs join the existing Graphics card step.
public static class AuroraLoaderWarmup
{
    private static readonly Lock gate = new();
    private static readonly List<IDisposable> entries = [];
    public static void Register()
    {
        lock (gate)
        {
            if (entries.Count != 0) return;
            entries.Add(GpuWarmup.AddActivity(AuroraLoader.AnyAnimating));
            entries.Add(GpuWarmup.Register(GpuProgram.Draw("aurora-curtain", FirstRunSteps.Graphics, c => Draw(c, AuroraLoaderKind.CurtainFold))));
            entries.Add(GpuWarmup.Register(GpuProgram.Draw("aurora-flow", FirstRunSteps.Graphics, c => Draw(c, AuroraLoaderKind.FlowArc))));
            entries.Add(GpuWarmup.Register(GpuProgram.Draw("aurora-sheen", FirstRunSteps.Graphics, c => Draw(c, AuroraLoaderKind.AuroraSheen))));
        }
    }

    public static void Unregister()
    {
        lock (gate) { foreach (var entry in entries) entry.Dispose(); entries.Clear(); }
    }

    private static void Draw(GpuWarmContext context, AuroraLoaderKind kind)
    {
        var width = kind == AuroraLoaderKind.AuroraSheen ? 180 : kind == AuroraLoaderKind.CurtainFold ? 48 : 40;
        var height = kind == AuroraLoaderKind.AuroraSheen ? 88 : width;
        var info = new SKImageInfo((int)Math.Ceiling(width * context.Scaling), (int)Math.Ceiling(height * context.Scaling), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = context.Gpu is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
        if (surface is null) throw new InvalidOperationException("The Aurora loader warm-up surface was not created.");
        using var painter = new AuroraLoaderPainter();
        surface.Canvas.Scale((float)context.Scaling);
        foreach (var dark in new[] { true, false })
        {
            painter.Draw(surface.Canvas, kind, width, height, .9, dark, false);
            painter.Draw(surface.Canvas, kind, width, height, .9, dark, true);
        }
        surface.Flush();
    }
}
