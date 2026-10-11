using Avalonia;
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Port;

// OD-0108 / OD-0113: renderer's actual image/gradient/blur families join the boot's awaited preparation.
// OD-0136: the boot shows the nth mark B, so its programs are the boot painter's own, drawn by the real AuroraBootPainter at the
// boot's own sizes, plus the empty chat's 44 px tile (NthMark's icon tile and its soft sweep). OD-0142: the pop (the faded tile's
// layer, its turned and dithered gradient, the lifting shadow's scaled blur), the n's trim with its clipped round head and the
// drop-shadow layer, the bead's spring, the settled tile, the grow's cached tile, clip and glow; there is no sweep any more.
public static class AuroraBootWarmup
{
    /// <summary>The registered programs, in order: one render-thread step each.</summary>
    public static IReadOnlyList<string> Programs { get; } = Array.AsReadOnly(new[]
    {
        "nth-boot-pop", "nth-boot-draw", "nth-boot-bead", "nth-boot-hold", "nth-boot-grow", "nth-boot-glow", "nth-boot-still", "nth-hero",
    });

    private static readonly List<IDisposable> entries = [];
    public static void Register()
    {
        if (entries.Count != 0) return;
        foreach (var program in Programs)
        {
            var name = program;
            entries.Add(GpuWarmup.Register(GpuProgram.Draw(name, FirstRunSteps.Graphics, context => Draw(context, name))));
        }
    }
    public static void Unregister() { foreach (var entry in entries) entry.Dispose(); entries.Clear(); }

    // One phase per render-thread step, at every boot size: the 1320 x 860 and 900 x 700 windows (tiles drawn at .6 of their width,
    // 792 and 540) and the reduced-motion still (440). Each pose is the real painter's, with readiness where the phase needs it.
    private static void Draw(GpuWarmContext context, string program)
    {
        if (program == "nth-hero") { Hero(context); return; }
        (double Seconds, double? Ready, double Opacity)[] poses = program switch
        {
            "nth-boot-pop" => [(.1, null, 1), (.3, null, 1)],
            "nth-boot-draw" => [(.7, null, 1)],
            "nth-boot-bead" => [(1.1, null, 1)],
            "nth-boot-hold" => [(1.6, null, 1), (4.6, null, 1)],
            "nth-boot-grow" => [(1.85, 1, 1), (2.2, 1, 1)], // the grow's first frames as vectors (the still is never shrunk), then the still
            "nth-boot-glow" => [(2.6, 1, 1), (3.3, 1, 1)],
            _ => [(.15, null, 1), (.5, null, .6), (1.4, 1, 1)], // the reduced-motion still, opaque and opacity-modulated, and its cross-fade
        };
        var reduce = program == "nth-boot-still";
        foreach (var (width, height) in reduce ? new[] { (900.0, 700.0) } : [(1320.0, 860.0), (900.0, 700.0)])
        {
            var window = new Rect(0, 0, width, height);
            var side = reduce ? AuroraBootSequence.MarkSize : Math.Round(width * .6);
            var pixels = (int)Math.Ceiling(side * context.Scaling);
            var info = new SKImageInfo(pixels, pixels, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = context.Gpu is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
            if (surface is null) throw new InvalidOperationException("nth boot warm-up surface was not created.");
            using var painter = new AuroraBootPainter();
            surface.Canvas.Scale((float)context.Scaling);
            surface.Canvas.Translate((float)(side / 2 - window.Center.X), (float)(side / 2 - window.Center.Y)); // the tile at the surface's centre
            var sequence = new AuroraBootSequence(window, reduce);
            foreach (var (seconds, ready, opacity) in poses)
                painter.Draw(surface.Canvas, window.Size, window, sequence.At(seconds, ready), seconds, true, reduce, opacity, context.Gpu);
            surface.Flush();
        }
    }

    private static void Hero(GpuWarmContext context)
    {
        var pixels = (int)Math.Ceiling(NthTile.Side * context.Scaling);
        var info = new SKImageInfo(pixels, pixels, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = context.Gpu is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
        if (surface is null) throw new InvalidOperationException("nth hero warm-up surface was not created.");
        using var mark = new NthMark();
        surface.Canvas.Scale((float)context.Scaling);
        var box = new SKRect(0, 0, (float)NthTile.Side, (float)NthTile.Side);
        mark.Draw(surface.Canvas, box, NthTileStyle.Icon, NthGlyph.Regular, NthMotion.Hero(NthMotion.HeroSweepDelay + NthMotion.HeroSweepSeconds / 2, false));
        mark.Draw(surface.Canvas, box, NthTileStyle.Icon, NthGlyph.Regular, NthPose.Still);
        surface.Flush();
    }
}
