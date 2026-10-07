using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-4")]
public sealed class StaticWallpaperFocusTests
{
    [Fact]
    public void Both_gaussian_passes_bilinear_edges_and_dream_glow_match_independent_cpu_oracle()
    {
        var oracle = StaticWallpaperPage.Golden.GetProperty("blur");
        var width = oracle.GetProperty("W").GetInt32(); var height = oracle.GetProperty("H").GetInt32();
        var input = oracle.GetProperty("input").EnumerateArray().Select(x => x.GetByte()).ToArray();
        var expected = oracle.GetProperty("expected").EnumerateArray().Select(x => x.GetByte()).ToArray();
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        System.Runtime.InteropServices.Marshal.Copy(input, 0, bitmap.GetPixels(), input.Length);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.DrawBitmap(bitmap, 0, 0);
        using var focus = new StaticWallpaperFocus();
        focus.Apply(surface, width, height, null, oracle.GetProperty("sigma").GetDouble(), oracle.GetProperty("dream").GetDouble(), 0);
        using var image = surface.Snapshot();
        using var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.True(image.ReadPixels(result.Info, result.GetPixels(), result.RowBytes, 0, 0));
        Assert.Equal(expected.Length, result.Bytes.Length);
        // 1 byte of source dithering, plus 1 byte of intermediate CPU/SkSL rounding. Fixed before the first run.
        Assert.All(expected.Zip(result.Bytes), p => Assert.InRange(Math.Abs(p.First - p.Second), 0, 2));
    }

    [Fact]
    public void Intermediate_focus_and_threshold_match_actual_page_viewer_state()
    {
        foreach (var row in StaticWallpaperPage.Golden.GetProperty("focus").EnumerateArray())
        {
            using var r = new WallpaperRenderer(new(WallpaperKind.Static, row.GetProperty("id").GetString(), Options:
                new Dictionary<string, double> { ["focus"] = row.GetProperty("f").GetDouble() }));
            using var surface = SKSurface.Create(new SKImageInfo(32, 20)); r.Draw(surface.Canvas, 32, 20);
            var s = r.StaticState!;
            Assert.Equal(row.GetProperty("focus").GetDouble(), s.Focus, 9);
            Assert.Equal(row.GetProperty("blur").GetDouble(), s.Sigma, 9);
            Assert.Equal(row.GetProperty("dream").GetDouble(), s.Dream, 9);
        }
    }

    [Fact]
    public void Quantized_boundary_red_is_allowed_but_brown_ground_is_rejected()
    {
        var barchan = StaticWallpaperPage.Snapshot.GetProperty("wallpapers").EnumerateArray().Single(w => w.GetProperty("id").GetString() == "barchan");
        var mars = barchan.GetProperty("packs").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Mars");
        Assert.Equal("#230708", mars.GetProperty("dark")[5].GetString());
        Assert.False(StaticWallpaperPage.IsUnambiguouslyWarmGround("#230708"));
        Assert.True(StaticWallpaperPage.IsUnambiguouslyWarmGround("#35230c"));
    }
}
