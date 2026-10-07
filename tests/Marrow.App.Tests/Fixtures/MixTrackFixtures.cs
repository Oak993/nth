using Avalonia;
using Avalonia.Styling;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Fixtures;

// Production integration oracle: independent state instances, whose maths are separately pinned by A/B vectors and C's compositor.
public static class MixTrackFixtures
{
    public static SKBitmap Draw(TrackLook look,StrikeGeometry g,int seed,int bubbleSeed,double time,double offset,bool still=false,double? inkAmount=null,double? previousFrame=null)
    {
        var ink=StrikeInk.Of(JewelLiquid.Of(look.Brand)); if(inkAmount is {} amount) ink=ink with {Tint=ink.Tint with {Ink=amount}};
        var storm=new StrikeStorm(seed); var clock=new StrikeFastClock(TrackEffect.IsFast(look));
        var normal=Math.Clamp((int)Strike.Round(g.Width/32),4,24); var nuc=(int)Strike.Round(normal/3d);
        var lanes=new StrikeBubbleLanes($"{bubbleSeed},{normal},{nuc},1",g.Width,g.Height); var contacts=new StrikeContacts(seed,lanes,clock);
        if(!still&&previousFrame is {} previous)
        {
            var warm=storm.Resolve(g,previous+offset,previous,TrackEffect.IsFast(look)?1:0,ink.Amount);
            contacts.Process(g,storm.Plans,warm,previous,offset,ink.Amount);
        }
        var strikeTime=still ? StrikeMix.Timing(1,seed).At+0.035 : time+offset;
        var f=storm.Resolve(g,strikeTime,still?0:time,TrackEffect.IsFast(look)?1:0,ink.Amount);
        if(!still) { contacts.Process(g,storm.Plans,f,time,offset,ink.Amount); contacts.ResolveLight(g,f,time); }
        var spec=LiquidSpec.For(look); var size=new Size(g.Width,g.Height); var c=still?0:clock.ClockC(time); var layers=new LiquidTrack.LiquidLayers();
        using var target=SKSurface.Create(new SKImageInfo((int)g.Width,(int)g.Height,SKColorType.Rgba8888,SKAlphaType.Premul));
        try
        {
            var frame=new LiquidTrack.Frame(size,g.Width,still?0:time,LiquidTrack.GradientFor(spec,(int)g.Width,ThemeVariant.Dark),spec,null,false,LiquidTrack.StrandColorsOf(spec,ThemeVariant.Dark),new(c/7,c/11,c/6),null,
                Layers:layers,Mix:f,MixGeometry:g,MixInk:ink,MixLanes:lanes,FastClock:clock,MixContacts:contacts,MixStill:still,MixSeed:seed);
            layers.Draw(target.Canvas,null,1,frame); var bitmap=new SKBitmap((int)g.Width,(int)g.Height); using var image=target.Snapshot();
            if(!image.ReadPixels(bitmap.Info,bitmap.GetPixels(),bitmap.RowBytes,0,0)) throw new InvalidOperationException("Mix oracle pixels unavailable"); return bitmap;
        }
        finally {layers.Dispose();}
    }
    public static double NextStrikePageTime(TrackEffect effect,double pageTime=0)
    {
        var t=pageTime+effect.StrikeOffset; var first=(int)Math.Floor(t/2.4)-1;
        for(var k=first;;k++) { var p=StrikeMix.Timing(k,effect.StrikeSeed); if(p.At>t&&p.Style==0) return p.At+0.01-effect.StrikeOffset; }
    }
}
