using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.UI.Port.ComposerLights;
using SkiaSharp;

namespace Marrow.App.Tests.Fixtures;

// Lead-only GPU oracle: reuse the window's actual context, compare the retained filter with base Draw,
// and record a warm run including GPU completion. This is not a desktop presentation witness.
internal sealed class Smooth2VeilGpuProbe : CompositionCustomVisualHandler
{
    private bool done;
    public TaskCompletionSource<(string Backend, int Comparisons, double Milliseconds)> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override void OnMessage(object message) => Invalidate();
    public override Rect GetRenderBounds() => new(0, 0, 1, 1);
    public override void OnRender(ImmediateDrawingContext context)
    {
        if (done) return;
        done = true;
        try
        {
            using var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            var gpu = lease?.GrContext ?? throw new InvalidOperationException("NATIVE_PRECONDITION: no GPU context.");
            var info = new SKImageInfo(512, 256, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var actual = SKSurface.Create(gpu, false, info) ?? throw new InvalidOperationException("no GPU surface");
            using var expected = SKSurface.Create(gpu, false, info) ?? throw new InvalidOperationException("no oracle GPU surface");
            using var a = new SKBitmap(info); using var e = new SKBitmap(info);
            using var veil = new WallpaperDimVeil();
            var comparisons = 0;
            foreach (var scale in new[] { 1f, 1.25f, 1.5f })
            foreach (var spread in new[] { 24.0, 24, 39, 24 })
            foreach (var width in new[] { 120.0, 160, 180 })
            {
                actual.Canvas.Clear(SKColors.Transparent); expected.Canvas.Clear(SKColors.Transparent);
                actual.Canvas.Save(); expected.Canvas.Save();
                actual.Canvas.Translate(60, 60); expected.Canvas.Translate(60, 60);
                actual.Canvas.Scale(scale); expected.Canvas.Scale(scale);
                veil.Draw(actual.Canvas, width, 52, 22, spread, .35);
                // Frozen geometry/filter/alpha from 4f6db1d72.
                var grow = (float)(spread * .25);
                var radius = (float)Math.Min(22, Math.Min(width, 52) / 2) + grow;
                using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Max(.5, spread / 3));
                using var paint = new SKPaint { IsAntialias = true, MaskFilter = blur,
                    Color = new SKColor(8, 8, 10).WithAlpha((byte)Math.Round(.35 * 255)) };
                expected.Canvas.DrawRoundRect(new SKRect(-grow, -grow, (float)width + grow, 52 + grow), radius, radius, paint);
                actual.Canvas.Restore(); expected.Canvas.Restore();
                actual.Flush(); expected.Flush();
                if (!actual.ReadPixels(info, a.GetPixels(), info.RowBytes, 0, 0) || !expected.ReadPixels(info, e.GetPixels(), info.RowBytes, 0, 0))
                    throw new InvalidOperationException("GPU readback failed");
                if (!a.GetPixelSpan().SequenceEqual(e.GetPixelSpan())) throw new InvalidOperationException($"GPU veil changed: {scale}, {spread}, {width}");
                comparisons++;
            }
            for (var i = 0; i < 20; i++) veil.Draw(actual.Canvas, 160, 52, 22, 24, .35);
            actual.Flush(); gpu.Flush(true, true);
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 120; i++)
            {
                actual.Canvas.Clear(SKColors.Transparent);
                veil.Draw(actual.Canvas, 160 + i % 20, 52, 22, 24, .35);
                actual.Flush();
            }
            gpu.Flush(true, true);
            Completion.TrySetResult((gpu.Backend.ToString(), comparisons, Stopwatch.GetElapsedTime(start).TotalMilliseconds / 120));
        }
        catch (Exception error) { Completion.TrySetException(error); }
    }
}
