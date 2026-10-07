using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeJumpPixelTests
{
    [Fact]
    [Trait("Decision","ED-248")]
    public void Every_locked_jump_has_empty_strike_pixels_and_preserves_the_page_wash_and_lane_state()
    {
        using var doc=StrikeVectors.Read("random-entry.json"); var index=0;
        var g=new StrikeGeometry(298,34,272,24); using var raster=new StrikeRaster(new(298,34,SKColorType.Rgba8888,SKAlphaType.Premul));
        foreach(var row in doc.RootElement.GetProperty("waitExamples").EnumerateArray())
        {
            var p=StrikeMix.Timing(row.GetProperty("k").GetInt32(),1274);
            var phase=(index++%8) switch {0=>-p.Lead-p.Charge-0.4,1=>-p.Lead-p.Charge+0.01,2=>-0.05,3=>0,4=>0.5,5=>1,6=>1.85,_=>1.95};
            var t=row.GetProperty("framesWaited").GetInt32()/60d; var offset=p.At+phase;
            var entry=new StrikeReentry(1274,new ScriptedStrikeDraws(row.GetProperty("draws").EnumerateArray().Select(x=>x.GetDouble()).ToArray()),offset); entry.Request();
            var lanes=new StrikeBubbleLanes("53,9,3,1",298,34); var clock=new StrikeFastClock(); var contacts=new StrikeContacts(1274,lanes,clock); var storm=new StrikeStorm(1274);
            for(var n=0;n<row.GetProperty("framesWaited").GetInt32();n++) { var at=n/60d; var f=storm.Resolve(g,at+offset,at,1,0); contacts.Process(g,storm.Plans,f,at,offset,0); }
            var laneBefore=Enumerable.Range(0,lanes.Count).Select(i=>lanes.Now(i,clock.ClockB(t),1)).ToArray();
            var deadBefore=laneBefore.Select((b,i)=>contacts.IsDead(i,b.K)).ToArray();
            Assert.True(entry.BeforeFrame(t));
            var old=new StrikeStorm(1274).Resolve(g,t+offset,t,1,0); var fresh=new StrikeStorm(1274).Resolve(g,t+entry.Offset,t,1,0);
            Assert.Equal(1,old.LightCount); Assert.Equal(old.Light[0],fresh.Light[0]);
            for(var i=0;i<lanes.Count;i++) { Assert.Equal(laneBefore[i],lanes.Now(i,clock.ClockB(t),1)); Assert.Equal(deadBefore[i],contacts.IsDead(i,laneBefore[i].K)); }
            foreach(var ink in new[]{StrikeInk.Of(StrikeOracleLiquid.From("Claude","dark")),StrikeInk.Of(StrikeOracleLiquid.From("OpenAI","dark"))})
                foreach(var frame in new[]{old,fresh})
                {
                    // The idle wash is independently equal above; isolate the strike canvases for B-08's empty-pixel gate.
                    frame.LightCount=0; frame.LightOpacity=(1-ink.Amount); raster.Paint(g,frame,ink,t,1274);
                    foreach(var surface in new[]{raster.Over,raster.Screen}) { using var bitmap=new SKBitmap(298,34); using var image=surface.Snapshot(); Assert.True(image.ReadPixels(bitmap.Info,bitmap.GetPixels(),bitmap.RowBytes,0,0)); Assert.All(bitmap.Pixels,c=>Assert.Equal(0,c.Alpha)); }
                }
        }
    }
}
