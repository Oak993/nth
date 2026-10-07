using SkiaSharp;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The page's triangular dither, fixed in screen space, laid over a picture that was blurred without it (so the blur cannot average it away).</summary>
internal static class WallpaperDither
{
    private const string Source = """
        uniform shader src;
        uniform shader src2;
        uniform float2 u_res;
        uniform float u_mix;
        float wl_ign(float2 v) { return fract(52.9829189 * fract(dot(v, float2(.06711056, .00583715)))); }
        half4 main(float2 coord) {
            float2 fc = float2(coord.x, u_res.y - coord.y);
            half4 c = mix(src.eval(coord), src2.eval(coord), half(u_mix));
            float d = (wl_ign(fc) + wl_ign(fc + float2(37., 17.)) - 1.) / 255.;
            return half4(c.rgb + half(d), 1.);
        }
        """;
    private static readonly Lazy<SKRuntimeEffect> effect = new(() => SKRuntimeEffect.CreateShader(Source, out var error)
        ?? throw new InvalidOperationException($"wallpaper dither: {error}"));

    /// <summary>The picture <paramref name="a"/> mixed toward <paramref name="b"/> by <paramref name="mix"/> (0 is a alone), dithered.</summary>
    public static SKShader Shader(SKImage a, SKImage b, float mix, int width, int height)
    {
        using var uniforms = new SKRuntimeEffectUniforms(effect.Value) { ["u_res"] = new float[] { width, height }, ["u_mix"] = mix };
        using var children = new SKRuntimeEffectChildren(effect.Value) { ["src"] = a.ToShader(), ["src2"] = b.ToShader() };
        return effect.Value.ToShader(uniforms, children);
    }
}
