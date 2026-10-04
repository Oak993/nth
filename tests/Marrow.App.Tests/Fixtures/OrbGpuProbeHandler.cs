using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.UI.Port.Voice.Orb;
using SkiaSharp;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// VT-8, V-T54 native: on the window's render thread, draws voice orb frames through the real <see cref="OrbRenderer"/> into offscreen surfaces made by the window's
/// GPU context (ANGLE D3D11 on Windows) and reads them back, so a test can compare them with the reference PNGs of ref-orbs.js. A frame whose program did not compile
/// on the GPU draws nothing (Skia reports a GPU compile failure by drawing nothing), which the comparison sees.
/// </summary>
internal sealed class OrbGpuProbeHandler : CompositionCustomVisualHandler
{
    /// <summary>The jobs: (name, orb, mode, hovered, bands or null, t, ball, dark), as ref-orbs.js names them.</summary>
    public static readonly (string Name, VoiceOrbKind Kind, OrbMotionMode Mode, bool Hovered, OrbBands? Bands, double T, int Ball, bool Dark)[] Jobs =
    [
        ("ref_siri-wave_rest-1.3_32_dark", VoiceOrbKind.Siri, OrbMotionMode.Rest, false, null, 1.3, 32, true),
        ("ref_voice-membrane_rest-1.3_32_dark", VoiceOrbKind.Membrane, OrbMotionMode.Rest, false, null, 1.3, 32, true),
        ("ref_siri-wave_listen-speech_26_light", VoiceOrbKind.Siri, OrbMotionMode.Listening, false, new OrbBands(0.5, 0.6, 0.35, 0.5), 2.0, 26, false),
        ("ref_voice-membrane_listen-loud_32_dark", VoiceOrbKind.Membrane, OrbMotionMode.Listening, false, new OrbBands(0.95, 0.9, 0.8, 0.9), 2.0, 32, true),
        ("ref_siri-wave_processing_32_light", VoiceOrbKind.Siri, OrbMotionMode.Processing, false, null, 2.0, 32, false),
        ("ref_voice-membrane_blocked_26_dark", VoiceOrbKind.Membrane, OrbMotionMode.Blocked, false, null, 2.0, 26, true),
        ("ref_voice-membrane_hover_26_light", VoiceOrbKind.Membrane, OrbMotionMode.Rest, true, null, 2.0, 26, false),
    ];

    private bool done;

    /// <summary>Completes with one bitmap per job (null for every job when the window has no GPU context) and the GPU renderer's name.</summary>
    public TaskCompletionSource<(SKBitmap?[] Frames, string Gpu)> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            var frames = new SKBitmap?[Jobs.Length];
            if (gpu is null) { Result.TrySetResult((frames, "none")); return; }
            using var renderer = new OrbRenderer();
            for (var i = 0; i < Jobs.Length; i++)
            {
                var job = Jobs[i];
                var targets = OrbMotion.TargetsFor(job.Mode, job.Hovered, false, job.Bands?.All ?? 0);
                var phase = job.T * targets.Speed * OrbSeeds.IdleSpeed(job.Kind);
                var runtime = new VoiceOrbRuntime(job.Kind, new VoiceOrbRuntimeOptions(OrbScene.ViewPx, job.Ball, 1, job.Dark, Phase: phase));
                runtime.Frame(job.T, new OrbFrameState(Act: targets.Act, Bands: job.Bands, Speed: targets.Speed, Scale: targets.Scale, Glow: targets.Glow, Lift: targets.Lift, Desat: targets.Desat));
                var info = new SKImageInfo(runtime.DevicePx, runtime.DevicePx, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(gpu, false, info) ?? throw new InvalidOperationException("the GPU context made no surface");
                surface.Canvas.Clear(job.Dark ? new SKColor(0x1c, 0x1c, 0x1c) : new SKColor(0xf4, 0xf4, 0xf4));
                renderer.Draw(surface.Canvas, gpu, runtime.Values, runtime.Mrw, runtime.DevicePx, runtime.RenderPx, (float)OrbScene.ViewPx);
                surface.Flush();
                var bitmap = new SKBitmap(info);
                if (!surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0)) throw new InvalidOperationException("the GPU surface could not be read back");
                frames[i] = bitmap;
            }
            Result.TrySetResult((frames, gpu.Backend.ToString()));
        }
        catch (Exception error) { Result.TrySetException(error); }
    }
}
