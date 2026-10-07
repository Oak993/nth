using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeRasterStorageTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void All_six_masks_are_alpha_only_retained_surfaces()
    {
        using var raster = new StrikeRaster(new(96, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        var masks = Assert.IsType<SKSurface[]>(typeof(StrikeRaster).GetField("masks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(raster));
        Assert.Equal(6, masks.Length);
        foreach (var surface in masks) { using var image = surface.Snapshot(); Assert.Equal(SKColorType.Alpha8, image.ColorType); }
        Assert.Equal(9, raster.SurfacesMade);
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Cropping_the_filled_part_keeps_the_full_tubes_gradient_coordinates()
    {
        using var whole = new StrikeRaster(new(192, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var filled = new StrikeRaster(new(96, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        var f = new StrikeResolvedFrame { Opacity = 1 };
        f.Masks.Add(new(1, 1, new(new([0,20,192,20],0,2,1,false,3,0),0,1,0,0), 6, 1));
        var ink = StrikeInk.Of(StrikeOracleLiquid.From("OpenAI", "dark")); var g = new StrikeGeometry(192,40,80,24);
        whole.Paint(g,f,ink,17.3,1274); filled.Paint(g,f,ink,17.3,1274);
        using var a = new SKBitmap(192,40); using var b = new SKBitmap(96,40); using var ai = whole.Over.Snapshot(); using var bi = filled.Over.Snapshot();
        Assert.True(ai.ReadPixels(a.Info,a.GetPixels(),a.RowBytes,0,0)); Assert.True(bi.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0));
        for(var x=0;x<96;x++) Assert.Equal(a.GetPixel(x,20),b.GetPixel(x,20));
    }
}
