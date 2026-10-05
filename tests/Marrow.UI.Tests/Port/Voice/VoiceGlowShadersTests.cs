using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9 (V-13.2): the SkSL of the glow's layers evaluates the CSS gradients the way the browser does (the ellipse of given radii, the stops along the normalised distance in
// premultiplied colour, the first gradient of a background on top) and the layer mask of `ie()` plus the inner light's edge gradients. Each program is drawn on a strip and its
// pixels compared with the formulas of CSS Images Level 3 (the stops) and Masking Level 1 (the mask composite).
public sealed class VoiceGlowShadersTests
{
    private const int W = 100, H = 50;

    private static SKBitmap Draw(SKRuntimeEffect effect, SKRuntimeEffectUniforms uniforms, SKRuntimeEffectChildren? children = null)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        using var shader = children is null ? effect.ToShader(uniforms) : effect.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
        surface.Canvas.DrawRect(0, 0, W, H, paint);
        var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        return bitmap;
    }

    private static void Near(float expected, byte actual, string what, double tolerance = 2.5) =>
        Assert.True(Math.Abs(expected * 255 - actual) <= tolerance, $"{what}: expected {expected * 255:F1}, drawn {actual}");

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_ellipse_gradient_interpolates_premultiplied_stops_along_the_normalised_distance()
    {
        // radial-gradient(ellipse 40px 100px at 50.5px 25.5px, red 0%, rgba(0,255,0,0.5) 25%, rgba(0,0,255,0.2) 50%, transparent 100%), the stops premultiplied; the centre is
        // the centre of pixel (50, 25), so the pixel (50 + d, 25) is d px from it
        using var uniforms = new SKRuntimeEffectUniforms(VoiceGlowShaders.Ellipse);
        uniforms["ell"] = new float[] { 50.5f, 25.5f, 40, 100 };
        uniforms["pos"] = new float[] { 0, 0.25f, 0.5f, 1 };
        uniforms["s0"] = new float[] { 1, 0, 0, 1 };
        uniforms["s1"] = new float[] { 0, 0.5f, 0, 0.5f };
        uniforms["s2"] = new float[] { 0, 0, 0.2f, 0.2f };
        uniforms["s3"] = new float[] { 0, 0, 0, 0 };
        using var bitmap = Draw(VoiceGlowShaders.Ellipse, uniforms);
        const int row = 25;
        var centre = bitmap.GetPixel(50, row);
        Assert.True(centre.Red >= 254 && centre.Alpha >= 254, $"the centre is the first stop, {centre}");
        // d = 5 (t = 0.125): halfway between the first and second stop: premultiplied (0.5, 0.25, 0, 0.75)
        var p = bitmap.GetPixel(55, row);
        Near(0.75f, p.Alpha, "alpha, halfway to the second stop");
        Near(0.5f / 0.75f, p.Red, "red (premultiplied 0.5 over alpha 0.75)");
        Near(0.25f / 0.75f, p.Green, "green");
        // d = 12 (t = 0.3): a fifth of the way from the second stop to the third: premultiplied green 0.5 -> 0, blue 0 -> 0.2, alpha 0.5 -> 0.2
        var q = bitmap.GetPixel(62, row);
        var alpha = 0.5f + (0.2f - 0.5f) * 0.2f;
        Near(alpha, q.Alpha, "alpha at t 0.3");
        Near(0.5f * 0.8f / alpha, q.Green, "green at t 0.3");
        Near(0.2f * 0.2f / alpha, q.Blue, "blue at t 0.3");
        // d = 16 (t = 0.4): 0.6 of the way
        Near(0.5f + (0.2f - 0.5f) * 0.6f, bitmap.GetPixel(66, row).Alpha, "alpha at t 0.4");
        // d = 30 (t = 0.75): between the third stop and the transparent end, halfway
        Near(0.2f * 0.5f, bitmap.GetPixel(80, row).Alpha, "alpha at t 0.75", 3);
        // d = 45 (t above 1): beyond the last stop, the last colour: transparent
        Assert.Equal(0, bitmap.GetPixel(95, row).Alpha);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_ellipse_gradient_with_no_radius_is_its_last_colour_everywhere()
    {
        using var uniforms = new SKRuntimeEffectUniforms(VoiceGlowShaders.Ellipse);
        uniforms["ell"] = new float[] { 50, 25, 0, 20 };
        uniforms["pos"] = new float[] { 0, 0.3f, 0.6f, 1 };
        uniforms["s0"] = new float[] { 1, 1, 1, 1 };
        uniforms["s1"] = new float[] { 1, 1, 1, 1 };
        uniforms["s2"] = new float[] { 1, 1, 1, 1 };
        uniforms["s3"] = new float[] { 0, 0, 0, 0 };
        using var bitmap = Draw(VoiceGlowShaders.Ellipse, uniforms);
        for (var x = 0; x < W; x += 9) Assert.Equal(0, bitmap.GetPixel(x, 25).Alpha);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_lobes_are_seven_gradients_the_first_on_top_each_fading_to_transparent_at_its_fade()
    {
        // lobe 0: red at 0.8 alpha, centred x 40, radii 20 x 20; lobe 1: blue at 1.0, centred x 55, radii 30 x 30; the other five have no height
        using var uniforms = new SKRuntimeEffectUniforms(VoiceGlowShaders.Lobes);
        var a = new float[28];
        var c = new float[28];
        a[0] = 40.5f; a[1] = 25.5f; a[2] = 20; a[3] = 20;
        c[0] = 1; c[1] = 0; c[2] = 0; c[3] = 0.8f;
        a[4] = 55.5f; a[5] = 25.5f; a[6] = 30; a[7] = 30;
        c[4] = 0; c[5] = 0; c[6] = 1; c[7] = 1;
        uniforms["lobeA"] = a;
        uniforms["lobeC"] = c;
        uniforms["fade"] = 0.75f;
        using var bitmap = Draw(VoiceGlowShaders.Lobes, uniforms);
        // at pixel 40 (the first lobe's centre): lobe 0 is at 0.8 alpha, over lobe 1 (15 px from its centre)
        float G(float t, float fade) => 1 - Math.Clamp(t / fade, 0, 1);
        var x = 40;
        var g0 = G(0, 0.75f) * 0.8f;                                       // lobe 0's alpha here
        var g1 = G(15f / 30, 0.75f);                                       // lobe 1's alpha here
        var alpha = g0 + g1 * (1 - g0);                                    // premultiplied source-over, lobe 0 on top
        var p = bitmap.GetPixel(x, 25);
        Near(alpha, p.Alpha, "alpha at the first lobe's centre");
        var redPremul = g0;                                                // premultiplied red = colour x alpha
        var bluePremul = g1 * (1 - g0);
        Near(redPremul / alpha, p.Red, "red share");
        Near(bluePremul / alpha, p.Blue, "blue share");
        // far to the right only lobe 1 reaches: pixel 80 is 25 px from it, t = 0.83 > 0.75: nothing
        Assert.Equal(0, bitmap.GetPixel(80, 25).Alpha);
        // pixel 70: 15 px from lobe 1's centre, t = 0.5, alpha 1 - 0.5 / 0.75 = 0.33; no red there (lobe 0 ends at 40.5 + 15)
        var q = bitmap.GetPixel(70, 25);
        Near(G(15f / 30, 0.75f), q.Alpha, "alpha at x 70");
        Assert.True(q.Red <= 2 && q.Blue >= 250, $"blue only at x 70: {q}");
    }

    private static SKRuntimeEffectChildren LayerChildren(SKShader content, SKShader noise)
    {
        var children = new SKRuntimeEffectChildren(VoiceGlowShaders.Layer);
        children["content"] = content;
        children["noise"] = noise;
        return children;
    }

    private static SKRuntimeEffectUniforms LayerUniforms(float edge, bool edgeOn, float opacity = 1, float dispScale = 0, float rx = 1e6f)
    {
        var u = new SKRuntimeEffectUniforms(VoiceGlowShaders.Layer);
        u["ell"] = new float[] { 50.5f, 25.5f, rx, rx };
        u["pos"] = new float[] { 0, 0.5f, 1, 1 };
        u["ma"] = new float[] { 1, 0.5f, 0, 0 };
        u["edge"] = new float[] { edge, edgeOn ? 1 : 0, W, H };
        u["dispShift"] = new float[] { 0, 0, 0, 0 };
        u["zf"] = 1f;
        u["opacity"] = opacity;
        u["dispScale"] = dispScale;
        return u;
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_inner_lights_mask_is_the_union_of_two_edge_gradients_inside_the_ellipse()
    {
        // mask-composite: intersect, add: ellipse AND (vertical edge OR horizontal edge); edges white at the border, transparent from 10 px in
        using var content = SKShader.CreateColor(SKColors.White);
        using var noise = SKShader.CreateColor(SKColors.Gray);
        using var uniforms = LayerUniforms(edge: 10, edgeOn: true);
        using var children = LayerChildren(content, noise);
        using var bitmap = Draw(VoiceGlowShaders.Layer, uniforms, children);
        // the middle of the strip is inside neither edge band: nothing
        Assert.Equal(0, bitmap.GetPixel(50, 25).Alpha);
        // 5 px from the left edge, in the vertical middle: the horizontal gradient is 1 - 5.5/10 = 0.45 and the vertical one is 0 there: union 0.45
        Near(1 - 5.5f / 10, bitmap.GetPixel(5, 25).Alpha, "5 px in from the left");
        // 1.5 px from the top edge in the horizontal middle
        Near(1 - 1.5f / 10, bitmap.GetPixel(50, 1).Alpha, "1.5 px in from the top");
        // a corner pixel: both gradients act, union = a + b - ab
        var av = 1 - 1.5f / 10;
        Near(av + av - av * av, bitmap.GetPixel(1, 1).Alpha, "the corner");
        // the bottom edge mirrors the top: the gradient rises again over the last 10 px
        Near(1 - 1.5f / 10, bitmap.GetPixel(50, H - 2).Alpha, "1.5 px in from the bottom");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_ellipse_mask_scales_the_picture_and_the_opacity_scales_it_again()
    {
        using var content = SKShader.CreateColor(SKColors.White);
        using var noise = SKShader.CreateColor(SKColors.Gray);
        // an ellipse of radius 40: white at the centre, one half at 50 % (pos 0.5 -> 20 px), nothing from 40 px; opacity 0.5
        using var uniforms = LayerUniforms(edge: 0, edgeOn: false, opacity: 0.5f, rx: 40);
        using var children = LayerChildren(content, noise);
        using var bitmap = Draw(VoiceGlowShaders.Layer, uniforms, children);
        Near(0.5f, bitmap.GetPixel(50, 25).Alpha, "the centre at opacity 0.5", 3);
        Near(0.5f * 0.5f, bitmap.GetPixel(70, 25).Alpha, "20 px out: the half stop");
        Assert.Equal(0, bitmap.GetPixel(95, 25).Alpha);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_displacement_moves_the_picture_sideways_by_scale_times_the_noise_less_a_half()
    {
        // feDisplacementMap with xChannelSelector R and the green channel at 0.5: the pixel at x shows the picture at x + scale * (R - 0.5); a noise of R = 1 and scale 20 -> +10 px
        using var ramp = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(W, 0), [SKColors.Black, SKColors.White], SKShaderTileMode.Clamp);
        using var noise = SKShader.CreateColor(new SKColor(255, 128, 0));
        using var still = LayerUniforms(edge: 0, edgeOn: false);
        using var moved = LayerUniforms(edge: 0, edgeOn: false, dispScale: 20);
        using var children = LayerChildren(ramp, noise);
        using var a = Draw(VoiceGlowShaders.Layer, still, children);
        using var b = Draw(VoiceGlowShaders.Layer, moved, children);
        // the ramp is x / 100 (black to white): the displaced picture at x shows (x + 10) / 100
        Near(55.5f / 100, a.GetPixel(55, 25).Red, "the still ramp");
        Near(65.5f / 100, b.GetPixel(55, 25).Red, "the ramp 10 px on", 3);
        // R = 0 would move it by -scale / 2: a noise of zero red
        using var zero = SKShader.CreateColor(new SKColor(0, 128, 0));
        using var back = new SKRuntimeEffectChildren(VoiceGlowShaders.Layer);
        back["content"] = ramp;
        back["noise"] = zero;
        using var c = Draw(VoiceGlowShaders.Layer, moved, back);
        Near(45.5f / 100, c.GetPixel(55, 25).Red, "the ramp 10 px back", 3);
    }
}
