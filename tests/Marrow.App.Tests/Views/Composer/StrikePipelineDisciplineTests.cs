using Avalonia;
using Avalonia.Styling;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikePipelineDisciplineTests
{
    [Fact]
    [Trait("Decision","ED-248")]
    public void The_full_liquid_mix_bubble_ring_and_glass_pipeline_allocates_zero_warmed_frame_bytes()
    {
        var size=new Size(298,34); var g=new StrikeGeometry(298,34,272,24); var spec=LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast,EffortBrand.Silver));
        var row=LiquidTrack.GradientFor(spec,298,ThemeVariant.Dark); var old=LiquidTrack.GradientFor(LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast,EffortBrand.Claude)),298,ThemeVariant.Dark);
        var strands=LiquidTrack.StrandColorsOf(spec,ThemeVariant.Dark); var a=StrikeInk.Of(JewelLiquid.Of(EffortBrand.Claude)); var b=StrikeInk.Of(JewelLiquid.Of(EffortBrand.Silver));
        var storm=new StrikeStorm(1274); var clock=new StrikeFastClock(); var lanes=new StrikeBubbleLanes("53,9,3,1",298,34); var contacts=new StrikeContacts(1274,lanes,clock);
        using var target=SKSurface.Create(new SKImageInfo(298,34,SKColorType.Rgba8888,SKAlphaType.Premul)); var layers=new LiquidTrack.LiquidLayers();
        var picked=new[]{36862,36863,36875,36887,36898,36918};
        void Draw(int n,bool measured)
        {
            var section=n/480; var page=n/240d; var t=StrikeMix.Timing(picked[section],1274).At+(n%480)/240d;
            if(n==700) clock.SetFast(false,page); if(n==900) clock.SetFast(true,page);
            var ex=n is >=1100 and <1300 ? 272-(n-1100)*0.25 : n>=1300 ? 222 : 272; var geometry=g with {Knob=ex};
            var u=Strike.Ss(0,0.6,(n%480)/240d); var ink=StrikeInk.Mix(a,b,u); var resolved=storm.Resolve(geometry,t,page,clock.FastAt(page),ink.Amount);
            contacts.Process(geometry,storm.Plans,resolved,page,t-page,ink.Amount); contacts.ResolveLight(geometry,resolved,page);
            var flow=clock.ClockC(page); var frame=new LiquidTrack.Frame(size,298,page,row,spec,null,false,strands,new(flow/7,flow/11,flow/6),null,Layers:layers,PreviousGradient:old,GradientMix:u,
                Mix:resolved,MixGeometry:geometry,MixInk:ink,MixLanes:lanes,FastClock:clock,MixContacts:contacts,MixSeed:1274);
            layers.Draw(target.Canvas,null,1,frame);
        }
        try
        {
            for(var n=0;n<2880;n++) Draw(n,false);
            var surfaces=layers.SurfacesMade; var masks=layers.StrikeSurfacesMade; var gradients=layers.GradientsMade;
            var before=GC.GetAllocatedBytesForCurrentThread(); for(var n=0;n<2880;n++) Draw(n,true); var bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.Equal(0,bytes); Assert.Equal(surfaces,layers.SurfacesMade); Assert.Equal(masks,layers.StrikeSurfacesMade); Assert.Equal(gradients,layers.GradientsMade);
        }
        finally { layers.Dispose(); }
    }
}
