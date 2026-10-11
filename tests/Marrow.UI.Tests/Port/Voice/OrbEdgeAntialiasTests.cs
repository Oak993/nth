using Marrow.Core.Port.Voice.Orb;
using Marrow.UI.Port.Voice.Orb;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

/// <summary>
/// ED-NEW-voicefix-ui-5 (owner 2026-10-11, 125%: "a strange low-anti-aliasing white boundary around the orb"). The light ring at the resting orb's edge is the
/// exported shader's own rim highlight (the design's optical rim and its key and fill lobes at the silhouette), which the port keeps (the orbs are exact exports).
/// What the port owns is how that edge is sampled onto the device pixels. This pins it: at every scale the orb is supersampled (V-T51 as ED-NEW-voicefix-ui-7
/// has it: 2x at every scale; at 200% the design's straight draw was 5.2 to 5.6 levels from the ideal on average and up to 30), on a whole-pixel and on a
/// fractional placement, in both themes, the device pixels of the edge annulus stay within a few levels of the same frame rendered at 8x and box-filtered down
/// (an anti-aliased edge by construction), and no pure-white 1 px ring appears that the ideal does not have.
/// </summary>
[Trait("Decision", "ED-NEW-voicefix-ui-5")]
[Trait("Decision", "ED-NEW-voicefix-ui-7")]
public sealed class OrbEdgeAntialiasTests
{
    private const int Ideal = 8;

    private static SKBitmap Draw(VoiceOrbRuntime runtime, float[] values, int devicePx, int renderPx, int size, float phase, int scale, SKColor background)
    {
        var info = new SKImageInfo(size * scale, size * scale, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(background);
        surface.Canvas.Translate(phase * scale, phase * scale);
        surface.Canvas.Scale((float)(devicePx / OrbScene.ViewPx));
        using var renderer = new OrbRenderer();
        renderer.Draw(surface.Canvas, null, values, runtime.Mrw, devicePx, renderPx, (float)OrbScene.ViewPx);
        var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        return bitmap;
    }

    private static SKColor[,] BoxDown(SKBitmap big, int size, int k)
    {
        var down = new SKColor[size, size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            int r = 0, g = 0, b = 0;
            for (var dy = 0; dy < k; dy++)
            for (var dx = 0; dx < k; dx++)
            {
                var p = big.GetPixel(x * k + dx, y * k + dy);
                r += p.Red; g += p.Green; b += p.Blue;
            }
            var n = k * k;
            down[x, y] = new SKColor((byte)Math.Round(r / (double)n), (byte)Math.Round(g / (double)n), (byte)Math.Round(b / (double)n));
        }
        return down;
    }

    private static bool White(SKColor c) => Math.Min(c.Red, Math.Min(c.Green, c.Blue)) >= 250;

    [Theory]
    [InlineData("siri-wave", "dark", 1.25, 0f)]
    [InlineData("siri-wave", "dark", 1.25, 0.46f)]
    [InlineData("siri-wave", "light", 1.25, 0f)]
    [InlineData("siri-wave", "light", 1.25, 0.46f)]
    [InlineData("voice-membrane", "dark", 1.25, 0.46f)]
    [InlineData("voice-membrane", "light", 1.25, 0.46f)]
    [InlineData("siri-wave", "dark", 1.0, 0.46f)]
    [InlineData("siri-wave", "light", 1.5, 0.46f)]
    [InlineData("siri-wave", "dark", 2.0, 0f)]
    [InlineData("siri-wave", "dark", 2.0, 0.46f)]
    [InlineData("siri-wave", "light", 2.0, 0.46f)]
    [InlineData("voice-membrane", "dark", 2.0, 0.46f)]
    [InlineData("voice-membrane", "light", 2.0, 0.46f)]
    [InlineData("siri-wave", "dark", 3.0, 0.46f)]
    public void The_resting_orbs_edge_is_as_smooth_as_an_eight_times_supersampled_render(string orb, string theme, double dpr, float phase)
    {
        var job = new OrbJob(orb, "rest-1.3", 32, theme);
        var runtime = OrbRig.Runtime(job, dpr);
        runtime.Frame(job.T, job.FrameState);
        Assert.Equal(2, runtime.RenderScale); // supersampled at every scale (V-T51, ED-NEW-voicefix-ui-7)
        var background = job.Dark ? OrbRig.DarkCard : OrbRig.LightCard;
        var size = runtime.DevicePx + 2;
        using var actual = Draw(runtime, runtime.Values.ToArray(), runtime.DevicePx, runtime.RenderPx, size, phase, 1, background);
        var values = runtime.Values.ToArray();
        var idealPx = runtime.DevicePx * Ideal;
        values[0] = idealPx;
        values[1] = idealPx;
        SKColor[,] ideal;
        using (var big = Draw(runtime, values, idealPx, idealPx, size, phase, Ideal, background)) ideal = BoxDown(big, size, Ideal);

        var centre = runtime.DevicePx / 2.0 + phase;
        var radius = job.Ball / 2.0 * dpr;
        double sum = 0;
        var count = 0;
        var worst = 0;
        int actualWhite = 0, idealWhite = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var d = Math.Sqrt((x + .5 - centre) * (x + .5 - centre) + (y + .5 - centre) * (y + .5 - centre));
            if (Math.Abs(d - radius) > 2) continue;
            SKColor p = actual.GetPixel(x, y), q = ideal[x, y];
            var diff = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
            sum += diff;
            count++;
            worst = Math.Max(worst, diff);
            if (White(p)) actualWhite++;
            if (White(q)) idealWhite++;
        }
        var mean = sum / count;
        Assert.True(mean <= 2.5 && worst <= 20, $"{orb} {theme} at {dpr} (+{phase}): the edge is {mean:F2} levels from the 8x render on average, {worst} at most, over {count} pixels");
        Assert.True(actualWhite <= idealWhite, $"{orb} {theme} at {dpr} (+{phase}): {actualWhite} pure-white edge pixels where the 8x render has {idealWhite}");
    }
}
