using SkiaSharp;

namespace Marrow.App.Tests.Wallpapers;

public sealed class WallpaperPerceptualComparisonTests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Metric_has_known_colour_endpoints_and_symmetric_distance()
    {
        Assert.Equal(0, WallpaperPerceptualComparison.Delta(SKColors.White, SKColors.White));
        Assert.InRange(WallpaperPerceptualComparison.Delta(SKColors.Black, SKColors.White), 0.999999, 1.000001);
        Assert.InRange(WallpaperPerceptualComparison.Delta(SKColors.Red, SKColors.Blue), 0.53708, 0.53710);
        Assert.Equal(WallpaperPerceptualComparison.Delta(SKColors.Red, SKColors.Blue), WallpaperPerceptualComparison.Delta(SKColors.Blue, SKColors.Red));
        Assert.False(WallpaperPerceptualComparison.Within(double.NaN, 0));
        Assert.False(WallpaperPerceptualComparison.Within(0, double.PositiveInfinity));
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Sparse_and_global_regressions_are_caught_by_independent_bounds()
    {
        using var expected = new SKBitmap(160, 100);
        using var sparse = new SKBitmap(160, 100);
        expected.Erase(SKColors.Black); sparse.Erase(SKColors.Black);
        sparse.SetPixel(3, 7, SKColors.White);
        var pixel = WallpaperPerceptualComparison.Measure(expected, sparse);
        Assert.True(pixel.Mean < WallpaperPerceptualComparison.MeanLimit);
        Assert.True(pixel.Max > WallpaperPerceptualComparison.PixelLimit);
        Assert.False(WallpaperPerceptualComparison.Within(pixel.Mean, pixel.Max));
        Assert.Equal((3, 7), (pixel.X, pixel.Y));
        using var global = new SKBitmap(160, 100);
        global.Erase(new SKColor(30, 30, 30));
        expected.Erase(new SKColor(20, 20, 20));
        var field = WallpaperPerceptualComparison.Measure(expected, global);
        Assert.True(field.Mean > WallpaperPerceptualComparison.MeanLimit);
        Assert.True(field.Max < WallpaperPerceptualComparison.PixelLimit);
        Assert.False(WallpaperPerceptualComparison.Within(field.Mean, field.Max));
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Missing_frame_alpha_or_wrong_size_never_compares_as_a_pass()
    {
        using var expected = new SKBitmap(160, 100);
        using var actual = new SKBitmap(160, 100);
        expected.Erase(SKColors.White); actual.Erase(SKColors.Transparent);
        Assert.Throws<InvalidDataException>(() => WallpaperPerceptualComparison.Measure(expected, actual));
        using var wrong = new SKBitmap(100, 160);
        Assert.Throws<InvalidDataException>(() => WallpaperPerceptualComparison.Measure(expected, wrong));
    }
}
