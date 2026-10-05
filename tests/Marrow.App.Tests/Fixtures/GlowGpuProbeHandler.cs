using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.Core.Port.Voice.Glow;
using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// VT-9, native: on the window's render thread, draws glow frames through the real <see cref="VoiceGlowPainter"/> into offscreen surfaces made by the window's GPU context (ANGLE
/// D3D11 on Windows), reads them back, and times runs of frames, so a test can compare them with the reference PNGs of ref-glow.js. A program that did not compile on the GPU draws
/// nothing (Skia reports a GPU compile failure by drawing nothing), which the comparison sees.
/// </summary>
internal sealed class GlowGpuProbeHandler(IReadOnlyList<string> names, string referenceFolder) : CompositionCustomVisualHandler
{
    private bool done;

    /// <summary>Completes with one bitmap per name (null for every name when the window has no GPU context), the GPU backend's name and the lines of timing.</summary>
    public TaskCompletionSource<(SKBitmap?[] Frames, string Gpu, IReadOnlyList<string> Timing, long FrameBytes)> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override void OnMessage(object message) => Invalidate();

    public override Rect GetRenderBounds() => new(0, 0, 1, 1);

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (done) return;
        done = true;
        try
        {
            using var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            var gpu = lease?.GrContext;
            var frames = new SKBitmap?[names.Count];
            if (gpu is null) { Result.TrySetResult((frames, "none", [], 0)); return; }
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(referenceFolder, "manifest.json")));
            var jobs = manifest.RootElement.GetProperty("jobs").EnumerateArray().ToDictionary(j => j.GetProperty("name").GetString()!);
            using var painter = new VoiceGlowPainter();
            var timing = new List<string>();
            long frameBytes = 0;
            for (var i = 0; i < names.Count; i++)
            {
                var job = jobs[names[i]];
                int w = job.GetProperty("width").GetInt32(), h = job.GetProperty("height").GetInt32();
                var dark = job.GetProperty("theme").GetString() == "dark";
                var look = VoiceGlowLook.From(VoiceGlowComposerProps.For(dark, processing: false, reduceMotion: false));
                var driver = new VoiceGlowDriver(look.Config);
                var frame = driver.Frame;
                foreach (var segment in job.GetProperty("script").EnumerateArray())
                {
                    driver.Configure(VoiceGlowResolver.Resolve(VoiceGlowComposerProps.For(dark, segment.GetProperty("processing").GetBoolean(), reduceMotion: false)).Config);
                    for (var k = 0; k < segment.GetProperty("frames").GetInt32(); k++) frame = driver.Step(1.0 / 60, segment.GetProperty("level").GetDouble(), w, h);
                }
                var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(gpu, false, info) ?? throw new InvalidOperationException("the GPU context made no surface");
                void Draw()
                {
                    surface.Canvas.Clear(dark ? new SKColor(0x1c, 0x1c, 0x1c) : new SKColor(0xf4, 0xf4, 0xf4));
                    painter.Draw(surface.Canvas, gpu, look, frame, w, h, 1, 1);
                }

                // ms a frame (draw, flush, and the GPU's finish after 120 of them), of which the CPU's recording of the draw, and the managed bytes a frame
                string Time(VoiceGlowLayers layers, VoiceGlowBandMode mode, bool keepBytes = false)
                {
                    painter.Layers = layers;
                    painter.BandMode = mode;
                    for (var k = 0; k < 10; k++) Draw();
                    gpu.Flush(true, true);
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    long recordTicks = 0;
                    var watch = Stopwatch.StartNew();
                    for (var k = 0; k < 120; k++)
                    {
                        var t0 = Stopwatch.GetTimestamp();
                        Draw();
                        recordTicks += Stopwatch.GetTimestamp() - t0;
                        surface.Flush();
                    }
                    gpu.Flush(true, true);
                    var ms = watch.Elapsed.TotalMilliseconds / 120;
                    var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 120;
                    if (keepBytes) frameBytes = bytes;
                    painter.Layers = VoiceGlowLayers.All;
                    painter.BandMode = VoiceGlowBandMode.Auto;
                    return string.Create(CultureInfo.InvariantCulture, $"{ms:F2} ms (recording {recordTicks * 1000.0 / Stopwatch.Frequency / 120:F2}), {bytes} B");
                }

                Draw();
                surface.Flush();
                var bitmap = new SKBitmap(info);
                if (!surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0)) throw new InvalidOperationException("the GPU surface could not be read back");
                frames[i] = bitmap;
                if (names[i].Contains("level-1.0_720x96_dark", StringComparison.Ordinal))
                {
                    timing.Add("720 x 96, level 1.0, dark, all layers, band analytic (what Auto draws on a GPU): " + Time(VoiceGlowLayers.All, VoiceGlowBandMode.Analytic, keepBytes: true));
                    timing.Add("720 x 96, level 1.0, dark, all layers, band as the canvas strokes it: " + Time(VoiceGlowLayers.All, VoiceGlowBandMode.Strokes));
                    timing.Add("  inner light alone: " + Time(VoiceGlowLayers.Inner, VoiceGlowBandMode.Auto));
                    timing.Add("  stroke ring alone: " + Time(VoiceGlowLayers.Stroke, VoiceGlowBandMode.Auto));
                    timing.Add("  bloom alone: " + Time(VoiceGlowLayers.Bloom, VoiceGlowBandMode.Auto));
                    timing.Add("  band alone, analytic: " + Time(VoiceGlowLayers.Band, VoiceGlowBandMode.Analytic));
                    timing.Add("  band alone, strokes: " + Time(VoiceGlowLayers.Band, VoiceGlowBandMode.Strokes));
                }
            }
            Result.TrySetResult((frames, gpu.Backend.ToString(), timing, frameBytes));
        }
        catch (Exception error) { Result.TrySetException(error); }
    }
}
