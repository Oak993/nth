using SkiaSharp;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The approved animated host's 24-tap Gaussian disc and explicit trilinear mip level.</summary>
internal sealed class AnimatedWallpaperFocus : IDisposable
{
    private SKRuntimeEffect? effect;
    private readonly List<SKSurface> levels = [];
    private readonly SKPaint paint = new();
    private (int W, int H, GRContext? Gpu) size;
    private const string Source = """
        uniform shader u_low;
        uniform shader u_high;
        uniform float2 u_res;
        uniform float2 u_lowScale;
        uniform float2 u_highScale;
        uniform float u_mix;
        uniform float u_sigma;
        float wl_ign(float2 v) { return fract(52.9829189*fract(dot(v,float2(.06711056,.00583715)))); }
        half4 main(float2 coord) {
            float R = 2.2*u_sigma;
            float3 acc = float3(0.); float ws = 0.;
            for (int i = 0; i < 24; i++) {
                float fi = float(i)+.5;
                float r = R*sqrt(fi/24.);
                float th = fi*2.39996323;
                float w = exp(-(r*r)/(2.*u_sigma*u_sigma));
                float2 p = coord+r*float2(cos(th),-sin(th));
                float3 low = float3(u_low.eval(p*u_lowScale).rgb);
                float3 high = float3(u_high.eval(p*u_highScale).rgb);
                acc += w*mix(low,high,u_mix); ws += w;
            }
            float3 c = acc/ws;
            float2 fc = float2(coord.x,u_res.y-coord.y);
            c += (wl_ign(fc)+wl_ign(fc+float2(37.,17.))-1.)/255.;
            return half4(c,1.);
        }
        """;

    public void Apply(SKCanvas target, int width, int height, GRContext? gpu, double sigma, Action<SKCanvas> drawSharp)
    {
        effect ??= SKRuntimeEffect.CreateShader(Source, out var error) ?? throw new InvalidOperationException(error);
        if (size != (width, height, gpu)) { ReleaseLevels(); size = (width, height, gpu); }
        var lod = Math.Log2(Math.Max(sigma * .7, 1));
        var low = (int)Math.Floor(lod); var high = low + 1;
        var maxLevel = (int)Math.Floor(Math.Log2(Math.Max(width, height)));
        low = Math.Min(low, maxLevel); high = Math.Min(high, maxLevel);
        while (levels.Count <= high)
        {
            var i = levels.Count; var w = Math.Max(1, width >> i); var h = Math.Max(1, height >> i);
            var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            levels.Add((gpu is null ? null : SKSurface.Create(gpu, false, info)) ?? SKSurface.Create(info));
        }
        levels[0].Canvas.Clear(SKColors.Transparent); drawSharp(levels[0].Canvas);
        var linear = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        for (var i = 1; i <= high; i++)
        {
            using var source = levels[i - 1].Snapshot();
            var bounds = levels[i].Canvas.DeviceClipBounds;
            levels[i].Canvas.DrawImage(source, new SKRect(0, 0, bounds.Width, bounds.Height), linear);
        }
        using var lowImage = levels[low].Snapshot(); using var highImage = levels[high].Snapshot();
        using var lowShader = lowImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, linear);
        using var highShader = highImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, linear);
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        uniforms["u_res"] = new float[] { width, height }; uniforms["u_sigma"] = (float)sigma;
        uniforms["u_lowScale"] = new float[] { lowImage.Width / (float)width, lowImage.Height / (float)height };
        uniforms["u_highScale"] = new float[] { highImage.Width / (float)width, highImage.Height / (float)height };
        uniforms["u_mix"] = (float)(lod - Math.Floor(lod));
        var children = new SKRuntimeEffectChildren(effect) { ["u_low"] = lowShader, ["u_high"] = highShader };
        using var shader = effect.ToShader(uniforms, children);
        paint.Shader = shader; paint.BlendMode = SKBlendMode.Src;
        target.DrawRect(0, 0, width, height, paint); paint.Shader = null;
    }

    private void ReleaseLevels() { foreach (var level in levels) level.Dispose(); levels.Clear(); }
    public void Dispose() { ReleaseLevels(); effect?.Dispose(); paint.Dispose(); }
}
