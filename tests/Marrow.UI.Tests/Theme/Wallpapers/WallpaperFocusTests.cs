using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperFocusTests
{
    private static readonly WallpaperDefinition Wall = WallpaperCatalogue.Get(WallpaperKind.Animated, "borealis");

    [Theory]
    [InlineData(800, 0, 0)]
    [InlineData(800, 1, 28)]
    [InlineData(800, 0.5, 10.6100)]
    [InlineData(100, 1, 3.5)]
    [InlineData(1080, 0.2, 3.9713)]
    [Trait("Rule", "R-TEST-1")]
    public void Sigma_is_0_035_times_the_height_times_focus_to_the_1_4(double height, double focus, double expected)
        => Assert.Equal(expected, WallpaperFocus.Sigma(height, focus), 1e-3);

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void A_sharp_wallpaper_costs_no_blur_and_the_blur_starts_above_0_35_px()
    {
        using var sharp = new WallpaperRenderer(new(WallpaperKind.Animated, Wall.Id, Focus: 0));
        WallpaperFrames.Draw(sharp, 160, 100); WallpaperFrames.At(sharp, 3, 160, 100); Assert.Equal(0, sharp.Blurs);
        using var faint = new WallpaperRenderer(new(WallpaperKind.Animated, Wall.Id, Focus: 0.05));
        WallpaperFrames.Draw(faint, 160, 100); Assert.True(WallpaperFocus.Sigma(100, 0.05) < 0.35); Assert.Equal(0, faint.Blurs);
        using var dreamy = new WallpaperRenderer(new(WallpaperKind.Animated, Wall.Id, Focus: 1));
        WallpaperFrames.Draw(dreamy, 160, 100); Assert.Equal(1, dreamy.Blurs);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Focus_blurs_the_picture_with_a_gaussian_of_the_pages_sigma()
    {
        const int w = 160, h = 100; var sigma = WallpaperFocus.Sigma(h, 1);
        using var sharp = new WallpaperRenderer(new(WallpaperKind.Animated, "ribbon", Focus: 0)); using var blurred = new WallpaperRenderer(new(WallpaperKind.Animated, "ribbon", Focus: 1));
        var raw = WallpaperFrames.At(sharp, 4, w, h); var got = WallpaperFrames.At(blurred, 4, w, h);
        var want = Gaussian(raw, w, h, sigma);
        Assert.True(WallpaperFrames.Diff(raw, got) >= 3, "the focus changed the picture");
        // Skia's raster blur is three box blurs, a Gaussian to within a few levels; a sigma 15% off is several times worse
        var error = WallpaperFrames.Diff(want, got);
        Assert.True(error <= 6, $"blur differs from a reference gaussian of sigma {sigma} by {error}/255");
        foreach (var scale in new[] { 0.85, 1.15 })
            Assert.True(WallpaperFrames.Diff(Gaussian(raw, w, h, sigma * scale), got) >= 2 * error, $"the proof tells sigma x{scale} apart from sigma {sigma}");
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Focus_eases_from_the_current_value_and_ends_on_the_exact_frame()
    {
        var pick = new WallpaperSelection(WallpaperKind.Animated, Wall.Id, Focus: 0);
        using var r = new WallpaperRenderer(pick) { ReduceMotion = true };
        var start = WallpaperFrames.Draw(r, 96, 60); var previous = start;
        r.Set(pick with { Focus = 1 }, true); Assert.Equal(start, WallpaperFrames.Draw(r, 96, 60)); Assert.True(r.NeedsFrames);
        for (var i = 0; i < 34; i++) { r.Advance(1.0 / 60); var next = WallpaperFrames.Draw(r, 96, 60); Assert.InRange(WallpaperFrames.Diff(previous, next), 0, 20); previous = next; }
        using var target = new WallpaperRenderer(pick with { Focus = 1 }) { ReduceMotion = true };
        Assert.Equal(WallpaperFrames.Draw(target, 96, 60), WallpaperFrames.Draw(r, 96, 60)); Assert.False(r.NeedsFrames); Assert.Equal(1, r.FocusValue);
    }

    private static byte[] Gaussian(byte[] src, int w, int h, double sigma)
    {
        var radius = (int)Math.Ceiling(4 * sigma); var k = new double[2 * radius + 1];
        for (var i = -radius; i <= radius; i++) k[i + radius] = Math.Exp(-i * i / (2 * sigma * sigma));
        var sum = k.Sum(); for (var i = 0; i < k.Length; i++) k[i] /= sum;
        var tmp = new double[src.Length]; var dst = new byte[src.Length];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) for (var c = 0; c < 3; c++)
        {
            double v = 0; for (var i = -radius; i <= radius; i++) v += k[i + radius] * src[(y * w + Math.Clamp(x + i, 0, w - 1)) * 4 + c];
            tmp[(y * w + x) * 4 + c] = v;
        }
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            for (var c = 0; c < 3; c++)
            {
                double v = 0; for (var i = -radius; i <= radius; i++) v += k[i + radius] * tmp[(Math.Clamp(y + i, 0, h - 1) * w + x) * 4 + c];
                dst[(y * w + x) * 4 + c] = (byte)Math.Round(v);
            }
            dst[(y * w + x) * 4 + 3] = 255;
        }
        return dst;
    }
}
