using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-3")]
public sealed class WallpaperShaderArithmeticTests
{
    [Fact]
    public void The_hash_reduces_three_products_in_the_pages_left_to_right_order()
    {
        // Independent WebGL RGBA32F probe at cell (70,-36): h21 = .0001220703125.
        // Skia's dot builtin sums x+(y+z), returning .999755859375 at this cell.
        var definition = WallpaperCatalogue.Get(WallpaperKind.Animated, "medusa") with
        {
            Source = """
                float hash(float2 p) { float3 q = fract(float3(p.xyx)*.1031); q += dot(q,q.yzx+33.33); return fract((q.x+q.y)*q.z); }
                vec3 wall(vec2 p,float t) { return vec3(hash(vec2(70.,-36.))); }
                """,
        };
        using var effect = WallpaperCatalogue.Compile(definition);
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        uniforms["u_res"] = new float[] { 1, 1 }; uniforms["u_tilt"] = new float[] { 0, 0 };
        uniforms["u_pan"] = 0f; uniforms["u_dither"] = 0f;
        using var shader = effect.ToShader(uniforms);
        using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var paint = new SKPaint { Shader = shader }; surface.Canvas.DrawRect(0, 0, 1, 1, paint);
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        Assert.InRange(bitmap.GetPixel(0, 0).Red, 0, 1);
    }

    [Fact]
    public void The_signed_square_in_the_approved_bell_and_glint_profiles_is_symmetric()
    {
        // Medusa/lensball/equinox use exp(-pow(signed distance, 2.0)). SwiftShader multiplies signed squares.
        var definition = WallpaperCatalogue.Get(WallpaperKind.Animated, "medusa") with
        {
            Source = "vec3 wall(vec2 p, float t) { return vec3(exp(-pow(p.x, 2.0))); }",
        };
        using var effect = WallpaperCatalogue.Compile(definition);
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        uniforms["u_res"] = new float[] { 32, 20 }; uniforms["u_tilt"] = new float[] { 0, 0 };
        uniforms["u_pan"] = 0f; uniforms["u_dither"] = 0f;
        using var shader = effect.ToShader(uniforms);
        using var surface = SKSurface.Create(new SKImageInfo(32, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var paint = new SKPaint { Shader = shader };
        surface.Canvas.DrawRect(0, 0, 32, 20, paint);
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        for (var x = 0; x < 16; x++)
        {
            Assert.InRange(bitmap.GetPixel(x, 10).Red, 10, 255);
            Assert.InRange(Math.Abs(bitmap.GetPixel(x, 10).Red - bitmap.GetPixel(31 - x, 10).Red), 0, 1);
        }
    }
}
