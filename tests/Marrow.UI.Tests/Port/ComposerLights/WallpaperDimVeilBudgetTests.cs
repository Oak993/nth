using Marrow.Testing;
using Marrow.UI.Port.ComposerLights;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.ComposerLights;

// ED-NEW-smooth2-1: keep OD-0150's veil exactly, without making its blur again every frame.
public sealed class WallpaperDimVeilBudgetTests
{
    [Fact]
    [Trait("Decision", "OD-0150")]
    public void A_warm_veil_allocates_nothing_as_its_size_and_opacity_move()
    {
        using var veil = new WallpaperDimVeil();
        using var bitmap = new SKBitmap(256, 128);
        using var canvas = new SKCanvas(bitmap);
        void Frames()
        {
            for (var i = 0; i < 120; i++)
                veil.Draw(canvas, 160 + i % 20, 52 + i % 4, 22, 24, .2 + i % 8 * .05);
        }
        var bytes = AllocationProbe.Measure(Frames, Frames);
        Assert.True(bytes == 0, $"The warm veil allocated {bytes} bytes for 120 frames ({bytes / 120.0} B/frame).");
    }

    [Fact]
    [Trait("Decision", "OD-0150")]
    public void A_kept_blur_draws_the_original_pixels_through_spread_size_and_alpha_changes()
    {
        using var veil = new WallpaperDimVeil();
        using var actual = new SKBitmap(256, 128);
        using var expected = new SKBitmap(256, 128);
        using var a = new SKCanvas(actual);
        using var e = new SKCanvas(expected);
        foreach (var spread in new[] { 24.0, 24, 0, 1, 39, 39, 24 })
        foreach (var alpha in new[] { 0.0, .002, .003, .25, 1.0 })
        foreach (var width in new[] { 0.0, 80, 160 })
        {
            a.Clear(SKColors.Transparent); e.Clear(SKColors.Transparent);
            a.Save(); e.Save(); a.Translate(32, 32); e.Translate(32, 32);
            veil.Draw(a, width, 52, 22, spread, alpha);
            // Frozen Draw from the release base 4f6db1d72, independent of the cache implementation.
            if (alpha > .002 && width > 0)
            {
                var grow = (float)(spread * .25);
                var radius = (float)Math.Min(22, Math.Min(width, 52) / 2) + grow;
                using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Max(.5, spread / 3));
                using var paint = new SKPaint { IsAntialias = true, MaskFilter = blur,
                    Color = new SKColor(8, 8, 10).WithAlpha((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255)) };
                e.DrawRoundRect(new SKRect(-grow, -grow, (float)width + grow, 52 + grow), radius, radius, paint);
            }
            a.Restore(); e.Restore();
            Assert.True(actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
                $"Pixels changed at spread {spread}, alpha {alpha}, width {width}.");
        }
        veil.Dispose();
        a.Clear(SKColors.Transparent);
        veil.Draw(a, 160, 52, 22, 24, 1);
        Assert.True(actual.GetPixelSpan().IndexOfAnyExcept((byte)0) < 0);
    }
}
