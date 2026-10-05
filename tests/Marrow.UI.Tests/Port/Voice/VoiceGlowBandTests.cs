using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9 (voice-SPEC V-13.2, V-19): the band canvas (voice-glow.js Sn) is drawn two ways. The canvas's own: seventeen strokes of the polyline, each blurred on its own (exact, and the
// only way on the CPU raster, where it costs 10 ms). On a GPU one analytic pass: for each pixel the distance to the polyline, every stroke's coverage after its blur as a Gaussian
// edge on each side of its line width, composited in the canvas's order (what Auto draws there: 2.3 ms for the strokes against a fraction of that on the GPU of the native
// test). Both are held to the browser's frames (VoiceGlowFidelityTests); here, to each other and to the Gaussian they stand for.
public sealed class VoiceGlowBandTests
{
    private static VoiceGlowJob Job(string state, string theme = "dark") =>
        VoiceGlowJobs.States.Where(s => s.State == state).Select(s => new VoiceGlowJob(s.State, 720, 96, theme, s.Script)).Single();

    private static SKBitmap BandOnly(VoiceGlowJob job, VoiceGlowBandMode mode)
    {
        using var painter = new VoiceGlowPainter { BandMode = mode, Layers = VoiceGlowLayers.Band };
        return VoiceGlowRig.Render(job, painter: painter);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T60")]
    public void Auto_strokes_the_band_on_the_cpu_raster_and_the_two_modes_draw_the_same_picture()
    {
        var job = Job("level-1.0");
        using var auto = BandOnly(job, VoiceGlowBandMode.Auto);
        using var strokes = BandOnly(job, VoiceGlowBandMode.Strokes);
        using var analytic = BandOnly(job, VoiceGlowBandMode.Analytic);
        Assert.True(auto.Bytes.AsSpan().SequenceEqual(strokes.Bytes), "with no GPU, Auto is the canvas's strokes");
        Assert.False(analytic.Bytes.AsSpan().SequenceEqual(strokes.Bytes));
        var (within8, within16, mean, max) = VoiceGlowRig.Compare(strokes, analytic);
        Assert.True(within16 >= 0.998 && mean <= 2.0, $"the analytic band against the strokes: within 8 {within8:P2}, within 16 {within16:P2}, mean {mean:F2}, largest {max}");
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("level-0.6")]
    [InlineData("proc-1.35")]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T60")]
    public void The_analytic_band_follows_the_strokes_in_every_state(string state)
    {
        var job = Job(state, "light");
        using var strokes = BandOnly(job, VoiceGlowBandMode.Strokes);
        using var analytic = BandOnly(job, VoiceGlowBandMode.Analytic);
        var (_, within16, mean, _) = VoiceGlowRig.Compare(strokes, analytic);
        Assert.True(within16 >= 0.995 && mean <= 3.0, $"{state}: within 16 {within16:P2}, mean {mean:F2}");
    }

    // a straight horizontal line at y = 50.5 (the middle of pixel row 50): one stroke of line width 10 blurred by sigma 3, white at alpha 1, no gradient fade over the strip
    private static SKBitmap Straight(float width, float sigma, float alpha, int offsetIndex = 0, float offsetY = 0)
    {
        using var b = new GlowBandUniforms();
        for (var i = 0; i < b.Ys.Length; i++) b.Ys[i] = 50.5f;
        b.Geo[0] = 0;
        b.Geo[1] = 400f / 56;
        b.Geo[2] = 60;
        b.Offsets[4] = 0;
        b.Offsets[5] = offsetY;
        b.Strokes[0] = offsetIndex;
        b.Strokes[1] = width / 2;
        b.Strokes[2] = sigma;
        b.Strokes[3] = alpha;
        b.Colors[0] = b.Colors[1] = b.Colors[2] = 1;
        b.Ramp[0] = 0;
        b.Ramp[1] = 400;
        b.Ramp[2] = 0.015f;
        var u = b.Uniforms;
        u["ys"] = b.Ys;
        u["geo"] = b.Geo;
        u["offs"] = b.Offsets;
        u["strokes"] = b.Strokes;
        u["colors"] = b.Colors;
        u["ramp"] = b.Ramp;
        u["count"] = 1f;
        var info = new SKImageInfo(400, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        using var shader = VoiceGlowShaders.Band.ToShader(u);
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
        surface.Canvas.DrawRect(0, 0, 400, 100, paint);
        var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        return bitmap;
    }

    // the standard normal CDF by the error function's series (an independent oracle for the shader's logistic form of it)
    private static double Phi(double x)
    {
        // Abramowitz and Stegun 7.1.26
        var t = 1 / (1 + 0.3275911 * Math.Abs(x) / Math.Sqrt(2));
        var y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x / 2);
        return x >= 0 ? 0.5 * (1 + y) : 0.5 * (1 - y);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T60")]
    public void A_straight_stroke_has_the_gaussian_edge_of_its_line_width_and_blur()
    {
        const float width = 10, sigma = 3;
        using var bitmap = Straight(width, sigma, 1);
        // the middle of the strip (x = 200), pixel rows 50 +- d: the pixel centre is d px from the line at y 50.5
        foreach (var d in new[] { 0, 1, 2, 3, 5, 7, 9, 12, 16 })
        {
            var expected = Phi((width / 2 - d) / sigma) - Phi((-width / 2 - d) / sigma);
            var above = bitmap.GetPixel(200, 50 - d).Alpha / 255.0;
            var below = bitmap.GetPixel(200, 50 + d).Alpha / 255.0;
            Assert.True(Math.Abs(above - expected) < 0.012, $"{d} px above the line: {above:F3} against the Gaussian's {expected:F3}");
            Assert.True(Math.Abs(below - expected) < 0.012, $"{d} px below the line: {below:F3} against the Gaussian's {expected:F3}");
        }
        // beyond four sigma of the line's edge nothing is drawn
        Assert.Equal(0, bitmap.GetPixel(200, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(200, 99).Alpha);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T60")]
    public void The_gradient_fades_the_line_in_over_its_first_and_last_fiftieth_and_a_stroke_can_be_shifted()
    {
        using var plain = Straight(10, 3, 0.8f);
        // the canvas gradient: alpha 0 at the first x, the plateau from 1.5 % of the line (6 px of 400) to 98.5 %, 0 at the end
        var edge = plain.GetPixel(3, 50).Alpha / 255.0;                      // t = 3.5 / 400, halfway up the 1.5 % ramp
        var plateau = plain.GetPixel(200, 50).Alpha / 255.0;
        Assert.InRange(edge / plateau, 0.5, 0.65);
        Assert.True(plain.GetPixel(399, 50).Alpha <= 20, "and the other end is faded out as well");
        using var shifted = Straight(10, 3, 0.8f, offsetIndex: 1, offsetY: 20);
        Assert.True(shifted.GetPixel(200, 70).Alpha / 255.0 > 0.7 * plateau, "the shifted stroke's line is 20 px lower");
        Assert.True(shifted.GetPixel(200, 50).Alpha / 255.0 < 0.05, "and not where it was");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T60")]
    public void Strokes_composite_over_each_other_in_the_canvas_order()
    {
        // the same two strokes in the two orders give different pictures: the second is over the first (premultiplied source-over, in floating point)
        using var b = new GlowBandUniforms();
        for (var i = 0; i < b.Ys.Length; i++) b.Ys[i] = 50.5f;
        b.Geo[1] = 400f / 56;
        b.Geo[2] = 60;
        b.Ramp[1] = 400;
        b.Ramp[2] = 0.015f;
        SKColor Centre(float redFirst)
        {
            // stroke 0: red at alpha 0.5, stroke 1: blue at alpha 0.5, both 10 wide, sigma 1
            b.Strokes[0] = 0; b.Strokes[1] = 5; b.Strokes[2] = 1; b.Strokes[3] = 0.5f;
            b.Strokes[4] = 0; b.Strokes[5] = 5; b.Strokes[6] = 1; b.Strokes[7] = 0.5f;
            b.Colors[0] = redFirst; b.Colors[2] = 1 - redFirst; b.Colors[4] = 1 - redFirst; b.Colors[6] = redFirst;
            var u = b.Uniforms;
            u["ys"] = b.Ys; u["geo"] = b.Geo; u["offs"] = b.Offsets; u["strokes"] = b.Strokes; u["colors"] = b.Colors; u["ramp"] = b.Ramp; u["count"] = 2f;
            var info = new SKImageInfo(400, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            using var shader = VoiceGlowShaders.Band.ToShader(u);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
            surface.Canvas.DrawRect(0, 0, 400, 100, paint);
            using var bitmap = new SKBitmap(info);
            surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
            return bitmap.GetPixel(200, 50);
        }
        var redThenBlue = Centre(1);                                          // stroke 0 red, stroke 1 blue
        var blueThenRed = Centre(0);                                          // stroke 0 blue, stroke 1 red
        Assert.True(redThenBlue.Blue > redThenBlue.Red, $"blue is over: {redThenBlue}");
        Assert.True(blueThenRed.Red > blueThenRed.Blue, $"red is over: {blueThenRed}");
        // alpha 1 - (1 - 0.5)^2 = 0.75, and the top colour holds 2/3 of the premultiplied colour
        Assert.InRange(redThenBlue.Alpha, 188, 194);
    }
}
