using Avalonia;
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using Marrow.UI.Port.Nth;
using SkiaSharp;

namespace Marrow.App.Port;

// PORT-ONLY: OD-0136 amendment 2 (ED-NEW-nth-motion), the AuroraLoaderWarmup pattern (OD-0113 / OD-0108). The nth motion pack
// uses no runtime shader source; its GPU programs are Skia's own for the linear gradients, the blurred shadow, the round-capped
// strokes, the skewed light and the DstIn layer of the text light. Each painter draws once per theme, moving and calm, on the
// Graphics card step, so the first spinner, bar or send press never compiles a program on screen. The idle warm-up also waits
// while any nth primitive on screen is moving.
public static class NthMotionWarmup
{
    private static readonly Lock gate = new();
    private static readonly List<IDisposable> entries = [];

    public static void Register()
    {
        lock (gate)
        {
            if (entries.Count != 0) return;
            entries.Add(GpuWarmup.AddActivity(NthVisualHost.AnyAnimating));
            entries.Add(GpuWarmup.Register(GpuProgram.Draw("nth-motion", FirstRunSteps.Graphics, Draw)));
        }
    }

    public static void Unregister()
    {
        lock (gate) { foreach (var entry in entries) entry.Dispose(); entries.Clear(); }
    }

    private static void Draw(GpuWarmContext context)
    {
        const int width = 320, height = 200;
        var info = new SKImageInfo((int)Math.Ceiling(width * context.Scaling), (int)Math.Ceiling(height * context.Scaling), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = context.Gpu is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
        if (surface is null) throw new InvalidOperationException("The nth motion warm-up surface was not created.");
        var canvas = surface.Canvas;
        canvas.Scale((float)context.Scaling);
        var clock = TimeProvider.System;
        NthSkiaPainter[] painters =
        [
            new NthSpinnerPainter(), new NthBarPainter(), new NthBarPainter(), new NthRingPainter(), new NthStatusPainter(),
            new NthSkeletonPainter(), new NthSkeletonPainter(), new NthSkeletonPainter(), new NthSkeletonPainter(),
            new NthSendPainter(), new NthSendPainter(), new NthTubePainter(), new NthLinkPainter(), new NthThinkingGlyphPainter(),
            new NthFlickPainter(), new NthFlickPainter(),
        ];
        (Size Size, object? State)[] scenes =
        [
            (new Size(16, 16), NthSpinnerInk.Mark),
            (new Size(200, 4), new NthBarState(NthSpring.At(.5), false)),
            (new Size(200, 3), new NthBarState(NthSpring.At(0), true)),
            (new Size(18, 18), new NthRingState(NthSpring.At(1), 0, false)),
            (new Size(16, 16), new NthStatusState(NthStatus.Done, NthStatus.Working, 0, 0, NthDoneMark.Tick, 14)),
            (new Size(300, 200), new NthSkeletonState(NthSkeletonKind.List, 5, null, null)),
            (new Size(300, 160), new NthSkeletonState(NthSkeletonKind.Diff, 8, null, null)),
            (new Size(80, 80), new NthSkeletonState(NthSkeletonKind.Plate, 1, null, null)),
            (new Size(300, 80), new NthSkeletonState(NthSkeletonKind.Bars, 3, null, null)),
            (new Size(32, 32), new NthSendState(false, double.NegativeInfinity, false, double.NegativeInfinity, .5)),
            (new Size(32, 32), new NthSendState(true, 0, true, 0, 1)),
            (new Size(168, 12), new NthTubeState(null, null)),
            (new Size(120, 16), new NthLinkState(null)),
            (new Size(14, 14), null),
            (new Size(48, 48), new NthFlickState(null)),
            (new Size(48, 48), new NthFlickState(null)),
        ];
        try
        {
            foreach (var dark in new[] { true, false })
                foreach (var calm in new[] { false, true })
                    for (var i = 0; i < painters.Length; i++)
                    {
                        canvas.Clear(SKColors.Transparent);
                        // .3 s in: the spinner, bar light, ring pop and send glyphs are all mid-motion
                        // the two flick scenes 0.7 s and 1.6 s into their loop (after the 0.4 s blank and the 0.3 s start): the tip in
                        // flight, then the light's SrcATop band with the bead keeping it
                        var now = painters[i] is NthFlickPainter ? (i == painters.Length - 1 ? 2.3 : 1.4) : .3;
                        painters[i].Draw(canvas, new NthFrame(scenes[i].Size, dark, calm, clock, 0, scenes[i].State), now);
                    }
            // the text light: a layer kept only through a horizontal ramp (DstIn), as NthShinePainter composites it
            using var ramp = SKShader.CreateLinearGradient(new SKPoint(-30, 0), new SKPoint(30, 0), [SKColors.Transparent, SKColors.White, SKColors.Transparent], [0, .5f, 1], SKShaderTileMode.Clamp);
            using var mask = new SKPaint { BlendMode = SKBlendMode.DstIn, Shader = ramp };
            using var ink = new SKPaint { IsAntialias = true, Color = SKColors.Black };
            canvas.SaveLayer(new SKRect(0, 0, 60, 20), null);
            canvas.DrawRect(new SKRect(4, 4, 56, 16), ink);
            canvas.Translate(30, 0);
            canvas.DrawPaint(mask);
            canvas.Restore();
            surface.Flush();
        }
        finally { foreach (var painter in painters) painter.Dispose(); }
    }
}
