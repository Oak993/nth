using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Port;

// OD-0108 / OD-0113: renderer's actual image/gradient/blur families join the boot's awaited preparation.
public static class AuroraBootWarmup
{
    private static readonly List<IDisposable> entries = [];
    public static void Register()
    {
        if (entries.Count != 0) return;
        for (var i = 0; i < AuroraMark.WarmupPrograms.Count; i++)
        {
            var variant = i;
            entries.Add(GpuWarmup.Register(GpuProgram.Draw(AuroraMark.WarmupPrograms[i], FirstRunSteps.Graphics, context => Draw(context, variant))));
        }
    }
    public static void Unregister() { foreach (var entry in entries) entry.Dispose(); entries.Clear(); }
    private static void Draw(GpuWarmContext context, int variant)
    {
        // One actual draw per render-thread step. The former 9 x 3 x 3 whole-renderer draws blocked B.
        // All three boot sizes get both an opaque and opacity-modulated dark pose and the light pose.
        var size = (variant / 3) switch { 0 => 440.0, 1 => 792, _ => 540 };
        var pixels = (int)Math.Ceiling(size * context.Scaling);
        var info = new SKImageInfo(pixels, pixels, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = context.Gpu is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
        if (surface is null) throw new InvalidOperationException("Aurora boot warm-up surface was not created.");
        using var mark = new AuroraMark();
        surface.Canvas.Scale((float)context.Scaling);
        if (variant == 0) mark.Draw(surface.Canvas, 32, "dark", AuroraTime.Still, new SKSizeI(pixels, pixels));
        mark.Draw(surface.Canvas, size, variant % 3 == 2 ? "light" : "dark", variant % 3 == 1 ? AuroraTime.Launch(.3) : AuroraTime.Still, new SKSizeI(pixels, pixels));
        surface.Flush();
    }
}
