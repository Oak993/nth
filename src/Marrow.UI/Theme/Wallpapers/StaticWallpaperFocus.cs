using SkiaSharp;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The page's 13-tap horizontal/vertical Gaussian, screen glow, grain and dither.</summary>
public sealed class StaticWallpaperFocus : IDisposable
{
    private SKRuntimeEffect? effect;
    private SKSurface? intermediate;
    private (int W, int H, GRContext? Gpu) size;
    private readonly SKPaint paint = new();
    private const string Source = """
        uniform shader u_src;
        uniform float2 u_px;
        uniform float2 u_dir;
        uniform float u_sig;
        uniform float u_grain;
        uniform float u_last;
        uniform float u_dream;
        float h21(float2 p){ float3 q = fract(float3(p.xyx)*.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y)*q.z); }
        half4 main(float2 coord){
          float3 s = float3(0.); float ws = 0.;
          for (int i = -6; i <= 6; i++) {
            float k = float(i)*.5, w = exp(-.5*k*k);
            s += w*float3(u_src.eval(coord + u_dir*(k*u_sig)).rgb); ws += w;
          }
          float3 c = s/ws;
          if (u_last > .5) {
            c = mix(c, 1. - (1. - c)*(1. - c), u_dream*.16);
            float2 fc = float2(coord.x, u_px.y - coord.y);
            c += (h21(fc*1.13 + 3.7) + h21(fc*.97 + 19.1) - 1.)*u_grain*.034;
            c += (h21(fc + 41.) + h21(fc + 87.3) - 1.)/255.;
          }
          return half4(c, 1.);
        }
        """;

    public void Apply(SKSurface surface, int width, int height, GRContext? gpu, double sigma, double dream, double grain)
    {
        effect ??= SKRuntimeEffect.CreateShader(Source, out var error)
            ?? throw new InvalidOperationException(error);
        if (size != (width, height, gpu)) { intermediate?.Dispose(); intermediate = null; size = (width, height, gpu); }
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        intermediate ??= (gpu is null ? null : SKSurface.Create(gpu, false, info)) ?? SKSurface.Create(info);
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        uniforms["u_px"] = new float[] { width, height }; uniforms["u_sig"] = (float)sigma;
        uniforms["u_dream"] = (float)dream; uniforms["u_grain"] = (float)grain;
        void Pass(SKImage source, SKCanvas target, float x, float y, float last)
        {
            using var sampler = source.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            var children = new SKRuntimeEffectChildren(effect) { ["u_src"] = sampler };
            uniforms["u_dir"] = new float[] { x, y }; uniforms["u_last"] = last;
            using var shader = effect.ToShader(uniforms, children);
            paint.Shader = shader; paint.BlendMode = SKBlendMode.Src;
            target.DrawRect(0, 0, width, height, paint); paint.Shader = null;
        }
        using (var sharp = surface.Snapshot()) Pass(sharp, intermediate.Canvas, 1, 0, 0);
        using (var mid = intermediate.Snapshot()) Pass(mid, surface.Canvas, 0, 1, 1);
    }

    public void Dispose() { intermediate?.Dispose(); effect?.Dispose(); paint.Dispose(); }
}
