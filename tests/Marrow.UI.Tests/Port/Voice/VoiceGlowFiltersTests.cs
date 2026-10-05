using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9 (V-13.2): the CSS filter functions the recipe uses (hue-rotate, brightness, saturate: Filter Effects Module Level 1) as Skia colour filters, composed in the order the
// stylesheet writes them, each step clamped as a chain of CSS filter functions is.
public sealed class VoiceGlowFiltersTests
{
    // one pixel of `color` drawn through the filter
    private static SKColor Apply(SKColorFilter filter, SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var paint = new SKPaint { Color = color, ColorFilter = filter, BlendMode = SKBlendMode.Src };
        surface.Canvas.DrawRect(0, 0, 1, 1, paint);
        using var bitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap.GetPixel(0, 0);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_identity_values_change_nothing()
    {
        using var filter = VoiceGlowFilters.Chain(0, 1, 1);
        foreach (var c in new[] { new SKColor(10, 20, 30), new SKColor(255, 70, 120), new SKColor(0, 0, 0), new SKColor(255, 255, 255) })
        {
            var r = Apply(filter, c);
            Assert.InRange(r.Red, c.Red - 1, c.Red + 1);
            Assert.InRange(r.Green, c.Green - 1, c.Green + 1);
            Assert.InRange(r.Blue, c.Blue - 1, c.Blue + 1);
        }
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Brightness_scales_the_colour_and_saturate_zero_takes_the_luminance()
    {
        using var bright = VoiceGlowFilters.Chain(0, 1.15, 1);
        var b = Apply(bright, new SKColor(100, 100, 100));
        Assert.InRange(b.Red, 114, 116);
        using var grey = VoiceGlowFilters.Chain(0, 1, 0);
        var g = Apply(grey, new SKColor(255, 0, 0));
        // Filter Effects: saturate(0) puts every channel at 0.213 R + 0.715 G + 0.072 B
        Assert.InRange(g.Red, 53, 55);
        Assert.InRange(g.Green, 53, 55);
        Assert.InRange(g.Blue, 53, 55);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Hue_rotate_a_half_turn_and_a_full_turn()
    {
        using var full = VoiceGlowFilters.Chain(360, 1, 1);
        var f = Apply(full, new SKColor(255, 70, 120));
        Assert.InRange(f.Red, 254, 255);
        Assert.InRange(f.Green, 69, 71);
        Assert.InRange(f.Blue, 119, 121);
        // hue-rotate(180deg) of white and of grey stays on the grey axis (the matrix preserves luminance), and a pure red goes to a cyan-leaning colour
        using var half = VoiceGlowFilters.Chain(180, 1, 1);
        var grey = Apply(half, new SKColor(128, 128, 128));
        Assert.InRange(grey.Red, 127, 129);
        Assert.InRange(grey.Green, 127, 129);
        Assert.InRange(grey.Blue, 127, 129);
        var red = Apply(half, new SKColor(255, 0, 0));
        Assert.True(red.Red < 20 && red.Blue > 100 && red.Green > 100, $"hue-rotate(180deg) of red: {red}");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void Each_function_clamps_before_the_next_one_as_a_css_filter_chain_does()
    {
        // brightness(2) turns (200, 100, 50) into (255 clamped, 200, 100); saturate(0) of THAT is its luminance 0.213 * 255 + 0.715 * 200 + 0.072 * 100 = 204.5.
        // Without the clamp between them the luminance would be 0.213 * 400 + 0.715 * 200 + 0.072 * 100 = 235.4.
        using var chain = VoiceGlowFilters.Chain(0, 2, 0);
        var c = Apply(chain, new SKColor(200, 100, 50));
        Assert.InRange(c.Green, 203, 206);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_matrices_are_the_specifications()
    {
        Span<float> m = stackalloc float[20];
        VoiceGlowFilters.Saturate(m, 0.5);
        Assert.Equal(0.213 + 0.787 * 0.5, m[0], 6);
        Assert.Equal(0.715 - 0.715 * 0.5, m[1], 6);
        Assert.Equal(0.213 - 0.213 * 0.5, m[5], 6);
        Assert.Equal(0.715 + 0.285 * 0.5, m[6], 6);
        Assert.Equal(0.072 + 0.928 * 0.5, m[12], 6);
        Assert.Equal(1, m[18]);
        VoiceGlowFilters.Brightness(m, 1.35);
        Assert.Equal(1.35, m[0], 5);
        Assert.Equal(0, m[1]);
        Assert.Equal(1.35, m[6], 5);
        Assert.Equal(1.35, m[12], 5);
        VoiceGlowFilters.HueRotate(m, 90);
        Assert.Equal(0.213 - 0.213, m[0], 6);                 // cos 90 = 0, sin 90 = 1
        Assert.Equal(0.715 - 0.715, m[1], 6);
        Assert.Equal(0.072 + 0.928, m[2], 6);
        Assert.Equal(0.213 + 0.143, m[5], 6);
        Assert.Equal(0.715 + 0.140, m[6], 6);
        Assert.Equal(0.072 - 0.283, m[7], 6);
    }
}
