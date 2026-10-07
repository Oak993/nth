using System.Runtime.InteropServices;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeLayerOrderTests
{
    private static byte[] Pixels(SKSurface s)
    {
        using var bitmap=new SKBitmap(new SKImageInfo(96,40,SKColorType.Rgba8888,SKAlphaType.Premul)); using var image=s.Snapshot();
        Assert.True(image.ReadPixels(bitmap.Info,bitmap.GetPixels(),bitmap.RowBytes,0,0)); var bytes=new byte[bitmap.ByteCount]; Marshal.Copy(bitmap.GetPixels(),bytes,0,bytes.Length); return bytes;
    }
    [Fact]
    [Trait("Decision","ED-248")]
    public void Burst_and_bolt_screen_groups_equal_successive_screening_in_premultiplied_channels()
    {
        var g=new StrikeGeometry(96,40,80,24); var ink=StrikeInk.Of(StrikeOracleLiquid.From("Claude","dark"));
        var both=new StrikeResolvedFrame {Opacity=1,LightOpacity=0.687}; var bolt=new StrikeResolvedFrame {Opacity=1,LightOpacity=0.687}; var burst=new StrikeResolvedFrame {Opacity=1};
        var zap=new StrikeLight(0,0.63,42,20,22,1,default,0,0); var zapb=new StrikeLight(0,0.55,47,21,12,1,default,0,1);
        both.AddLight(zap); both.AddLight(zapb); bolt.AddLight(zap); burst.AddLight(zapb);
        using var a=new StrikeRaster(new(96,40,SKColorType.Rgba8888,SKAlphaType.Premul)); using var b=new StrikeRaster(new(96,40,SKColorType.Rgba8888,SKAlphaType.Premul)); using var c=new StrikeRaster(new(96,40,SKColorType.Rgba8888,SKAlphaType.Premul));
        a.Paint(g,both,ink,0,1274); b.Paint(g,bolt,ink,0,1274); c.Paint(g,burst,ink,0,1274);
        var actual=Pixels(a.Screen); var s=Pixels(b.Screen); var d=Pixels(c.Screen);
        for(var i=0;i<actual.Length;i++) Assert.InRange(Math.Abs(actual[i]-(s[i]+d[i]-s[i]*d[i]/255d)),0,1);
    }
}
