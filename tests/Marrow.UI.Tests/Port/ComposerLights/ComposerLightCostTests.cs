using System.Diagnostics;
using System.Globalization;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): "a bounded per-frame cost". Measured here on the CPU: the animator's
// frame (the clock, the eased values, the scene's primitives) and the renderer's frame (uniforms packed, one shader built
// and drawn) on Skia's software rasterizer at 1x and 2x for the 820 x 52 composer. The GPU's own time cannot be measured
// headless; the software raster is its upper bound on this machine. MARROW_COMPOSER_LIGHT_EVIDENCE receives the numbers.
public sealed class ComposerLightCostTests
{
    private static (double AdvanceMicros, double DrawMillis, long DrawBytes) Measure(ComposerLightSettings settings, float scale)
    {
        var light = new ComposerLightAnimator(settings, ComposerLightPhase.Rest, true, 820, 52, false);
        using var renderer = new ComposerLightRenderer();
        var info = new SKImageInfo((int)(900 * scale), (int)(132 * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Scale(scale);
        canvas.Translate(40, 40);
        for (var i = 0; i < 5; i++) { light.Advance(i / 60.0); renderer.Draw(canvas, null, light, scale); }
        var watch = Stopwatch.StartNew();
        const int n = 600;
        for (var i = 5; i < 5 + n; i++) light.Advance(i / 60.0);
        var advance = watch.Elapsed.TotalMilliseconds * 1000 / n;
        long bytes = 0;
        watch.Restart();
        const int m = 20;
        for (var i = 0; i < m; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            renderer.Draw(canvas, null, light, scale);
            surface.Flush();
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        return (advance, watch.Elapsed.TotalMilliseconds / m, bytes / m);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0089: bounded per-frame cost; no per-frame blur, the animator allocates nothing per frame
    public void The_per_frame_cost_is_bounded()
    {
        var lines = new List<string>();
        foreach (var (name, settings) in new (string, ComposerLightSettings)[]
        {
            ("beam-large-fitted (default)", ComposerLightSettings.Default),
            ("beam-line", ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.Line) }),
            ("beam-pulse-outside", ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.PulseOutside) }),
            ("uiverse", ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura }),
        })
        foreach (var scale in new[] { 1f, 2f })
        {
            var (advance, draw, bytes) = Measure(settings, scale);
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0} @{1}x: animator {2:F1} us/frame, software raster {3:F2} ms/frame, renderer managed {4} B/frame", name, scale, advance, draw, bytes));
            Assert.True(advance < 200, $"{name}: the animator's frame takes {advance:F1} us");
            Assert.True(bytes < 4096, $"{name}: the renderer allocates {bytes} B a frame");
        }
        var folder = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_EVIDENCE");
        if (!string.IsNullOrEmpty(folder)) File.WriteAllLines(Path.Combine(folder, "cost.txt"), lines);
        foreach (var l in lines) Console.WriteLine(l);
    }
}
