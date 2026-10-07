using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeCompositeBudgetTests
{
    [Theory]
    [InlineData("Claude",4)]
    [InlineData("OpenAI",3)]
    [InlineData("AWS",7)]
    [Trait("Decision","ED-248")]
    public void Mask_composites_and_group_blits_leave_one_of_the_eight_tube_draws_for_glass(string maker,int expectedPeak)
    {
        var count=typeof(StrikeRaster).GetProperty("CompositeDraws"); Assert.NotNull(count);
        var g=new StrikeGeometry(298,34,272,24); var ink=StrikeInk.Of(StrikeOracleLiquid.From(maker)); var storm=new StrikeStorm(1274);
        using var raster=new StrikeRaster(new(298,34,SKColorType.Rgba8888,SKAlphaType.Premul));
        var at=StrikeMix.Timing(36863,1274).At;
        var peak=0;
        for(var n=0;n<120;n++) {var t=at+n/60d; var f=storm.Resolve(g,t,n/60d,1,ink.Amount); raster.Paint(g,f,ink,n/60d,1274); var draws=(int)count.GetValue(raster)!; Assert.InRange(draws,0,7); peak=Math.Max(peak,draws);}
        Assert.Equal(expectedPeak,peak);
    }
}
