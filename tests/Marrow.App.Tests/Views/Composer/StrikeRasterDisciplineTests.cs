using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeRasterDisciplineTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Drift_and_model_crossfade_create_no_managed_frame_objects_surfaces_or_filters()
    {
        var g = new StrikeGeometry(298, 34, 272, 24); using var raster = new StrikeRaster(new(298, 34, SKColorType.Rgba8888, SKAlphaType.Premul)); var storm = new StrikeStorm(1274);
        var a = StrikeInk.Of(StrikeOracleLiquid.From("Claude", "dark")); var b = StrikeInk.Of(StrikeOracleLiquid.From("OpenAI", "dark"));
        var t0 = StrikeMix.Timing(36863, 1274).At;
        for (var n = 0; n < 30; n++) { var ink = StrikeInk.Mix(a, b, n / 29d); var f = storm.Resolve(g, t0 + n / 60d, n / 60d, 1, ink.Amount); raster.Paint(g, f, ink, n / 60d, 1274); }
        var deltas = new long[240]; var surfaces = raster.SurfacesMade; var filters = raster.FiltersMade; var before = GC.GetAllocatedBytesForCurrentThread();
        for (var n = 0; n < 240; n++) { var start = GC.GetAllocatedBytesForCurrentThread(); var t = n / 240d; var ink = StrikeInk.Mix(b, a, Strike.Ss(0, 0.6, t)); var f = storm.Resolve(g, t0 + 0.5 + t, t, 1, ink.Amount); raster.Paint(g, f, ink, t, 1274); deltas[n] = GC.GetAllocatedBytesForCurrentThread() - start; }
        try { Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); }
        catch (Xunit.Sdk.XunitException error) { throw new Xunit.Sdk.XunitException(error.Message + " Frames: " + string.Join(", ", Enumerable.Range(0, deltas.Length).Where(i => deltas[i] != 0).Select(i => $"{i}={deltas[i]}"))); }
        Assert.Equal(surfaces, raster.SurfacesMade); Assert.Equal(filters, raster.FiltersMade);
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Fully_pale_liquid_still_screens_a_burst_independently_of_the_empty_bolt_light()
    {
        var g = new StrikeGeometry(298, 34, 272, 24); using var raster = new StrikeRaster(new(298, 34, SKColorType.Rgba8888, SKAlphaType.Premul));
        var ink = StrikeInk.Of(StrikeOracleLiquid.From("OpenAI", "dark")); var f = new StrikeResolvedFrame { Opacity = 1, LightOpacity = 0 };
        f.AddLight(new(0, 0.55, 120.5, 17.25, 8, 1, default, 0, 1)); raster.Paint(g, f, ink, 0, 1274);
        using var pixels = new SKBitmap(298, 34); using var image = raster.Screen.Snapshot(); Assert.True(image.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0));
        Assert.True(pixels.GetPixel(120, 17).Alpha > 100); Assert.Equal(0, raster.LightDraws);
    }
}
