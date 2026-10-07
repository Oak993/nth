using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-4")]
public sealed class WallpaperBranchParityTests
{
    // Expected branches come from the original highp GLSL in pinned Chrome
    // 151.0.7922.173/SwiftShader, before any production edit (branch-proof-before.json).
    [Theory]
    [InlineData(65, 28, false, 3)]
    [InlineData(152, 67, false, 1)]
    [InlineData(62, 45, true, 3)]
    [InlineData(128, 64, true, 3)]
    [InlineData(129, 44, true, 3)]
    public void Tunnel_selects_the_reference_sheet_at_every_original_branch_failure(int x, int y, bool perspective, int sheet)
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Static, "tunnel");
        var prefix = wall.Source[..wall.Source.IndexOf("void main()", StringComparison.Ordinal)]
            .Replace("col = c;", "col = vec3(k/5.);", StringComparison.Ordinal);
        using var bitmap = Probe(wall with { Source = prefix + ProbeMain("wall(p).x") }, perspective ? new[] { .008775f, .0054f } : new[] { 0f, 0f }, 0);
        Assert.Equal(sheet * 51, bitmap.GetPixel(x, y).Red);
    }

    [Fact]
    public void Stairwell_keeps_the_tilted_reference_pixel_below_the_shadow_threshold()
    {
        var wall = WallpaperCatalogue.Get(WallpaperKind.Static, "stairwell");
        var prefix = wall.Source[..wall.Source.IndexOf("void main()", StringComparison.Ordinal)]
            .Replace("float f = fract(s);", "float f = fract(s); return vec3(step(.06,f));", StringComparison.Ordinal);
        using var bitmap = Probe(wall with { Source = prefix + ProbeMain("wall(p).x") }, new[] { .006825f, .0042f }, 1);
        Assert.Equal(0, bitmap.GetPixel(131, 25).Red);
    }

    private static string ProbeMain(string expression) => """
        void main() {
            vec2 fc = gl_FragCoord.xy;
            vec2 p = (fc/u_res - .5)*2.*vec2(u_res.x/u_res.y, 1.);
            p += u_tilt*u_tg;
        """ + "vec3 c = vec3(" + expression + "); o = vec4(c, 1.); }";

    private static SKBitmap Probe(WallpaperDefinition wall, float[] tilt, float tg)
    {
        using var effect = WallpaperCatalogue.Compile(wall);
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        uniforms["u_res"] = new float[] { 160, 100 };
        uniforms["u_tilt"] = tilt; uniforms["u_tg"] = tg;
        uniforms["u_opt"] = .5f; uniforms["u_dpr"] = 1f;
        using var shader = effect.ToShader(uniforms);
        using var surface = SKSurface.Create(new SKImageInfo(160, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
        surface.Canvas.DrawRect(0, 0, 160, 100, paint);
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }
}
