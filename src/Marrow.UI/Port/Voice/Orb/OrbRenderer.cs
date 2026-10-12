// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-8, voice-SPEC V-11): draws one orb frame from the export's uniform block (V-11.3).
using Marrow.Core.Port.Voice.Orb;
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>
/// Runs the orb shader over a square and puts it on a canvas. When the block's render size is twice the slot's the shader runs into ONE reused offscreen surface at 2x
/// and is filtered down (voice-orbs.js:493-497 does so below 128 device pixels; the view does so at every scale, ED-NEW-voicefix-ui-7); at 1x it draws straight onto the canvas. The surface is made once and only remade when the
/// render size or the GPU context changes. A frame allocates nothing the port owns: the uniform array, the paint and the surface are reused. What a frame still
/// makes is SkiaSharp's: it copies the uniform array it is handed (one set a frame), and wraps the frame's shader and the surface's snapshot.
/// </summary>
public sealed class OrbRenderer : IDisposable
{
    private readonly SKRuntimeEffectUniforms uniforms = new(OrbShaders.Effect);
    private readonly float[] frame = new float[OrbUniformLayout.FrameFloats];
    private readonly SKPaint paint = new() { IsAntialias = false };
    private readonly SKSamplingOptions linear = new(SKFilterMode.Linear, SKMipmapMode.None);
    private SKSurface? surface;
    private int surfacePx;
    private GRContext? surfaceGpu;
    private bool disposed;

    /// <summary>How many offscreen surfaces have been made (tests: one per size).</summary>
    public int SurfaceCreations { get; private set; }

    /// <summary>The edge of the surface in hand in pixels (0: none made).</summary>
    public int SurfacePx => surface is null ? 0 : surfacePx;

    /// <summary>
    /// Draws the orb with its top left at the canvas's origin, <paramref name="viewPx"/> canvas units wide. <paramref name="values"/> is the block after
    /// <see cref="VoiceOrbRuntime.Frame"/> (its size members are <paramref name="renderPx"/>); <paramref name="devicePx"/> is the slot in device pixels and
    /// <paramref name="renderPx"/> is twice that below 128 (<see cref="VoiceOrbRuntime.RenderPx"/>).
    /// </summary>
    public void Draw(SKCanvas canvas, GRContext? gpu, ReadOnlySpan<float> values, OrbMrw mrw, int devicePx, int renderPx, float viewPx, float opacity = 1)
    {
        if (disposed || opacity <= 0) return;
        OrbUniformLayout.Write(uniforms, frame, values, (float)mrw.Lift, (float)mrw.Desat, (float)mrw.Dark);
        using var shader = OrbShaders.Effect.ToShader(uniforms);
        if (renderPx == devicePx)
        {
            paint.Shader = shader;
            paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255));
            paint.BlendMode = SKBlendMode.SrcOver;
            canvas.Save();
            canvas.Scale(viewPx / devicePx);
            canvas.DrawRect(0, 0, devicePx, devicePx, paint);
            canvas.Restore();
            paint.Shader = null;
            return;
        }
        var target = Surface(gpu, renderPx);
        var inner = target.Canvas;
        inner.Clear(SKColors.Transparent);
        paint.Shader = shader;
        paint.Color = SKColors.White;
        paint.BlendMode = SKBlendMode.Src;
        inner.DrawRect(0, 0, renderPx, renderPx, paint);
        paint.Shader = null;
        paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255));
        paint.BlendMode = SKBlendMode.SrcOver;
        canvas.Save();
        canvas.Scale(viewPx / renderPx);
        using (var image = target.Snapshot()) canvas.DrawImage(image, 0, 0, linear, paint);
        canvas.Restore();
    }

    private SKSurface Surface(GRContext? gpu, int px)
    {
        if (surface is not null && surfacePx == px && ReferenceEquals(surfaceGpu, gpu)) return surface;
        surface?.Dispose();
        var info = new SKImageInfo(px, px, SKColorType.Rgba8888, SKAlphaType.Premul);
        surface = (gpu is not null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
        surfacePx = px;
        surfaceGpu = gpu;
        SurfaceCreations += 1;
        return surface;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        surface?.Dispose();
        surface = null;
        uniforms.Dispose();
        paint.Dispose();
    }
}
