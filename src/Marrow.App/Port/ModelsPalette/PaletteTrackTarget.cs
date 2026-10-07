// PORT-ONLY: actor-prepared C25 handoff after T9's Strike merge; ED-625.
using Avalonia.Media;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using SkiaSharp;
using Marrow.UI.Port;
namespace Marrow.App.Port.ModelsPalette;
public sealed record PaletteTrackTarget(PaletteComposite Composite,LiquidSpec Spec,Color[] StrandColors,SKColor[] Gradient)
{
    public IReadOnlyList<PaletteColourSpan> TitleStops {get;init;}=[];
    public IReadOnlyList<PaletteColourSpan> GlowStops {get;init;}=[];
    public JewelMaterial Jewel {get;init;}=null!;
    public PaletteState State {get;init;}
    public string SnapshotDigest {get;init;}="";
    public string ModelsKey {get;init;}="";
    public PaletteShade TitleShade {get;init;}=null!;
    public PaletteShade GlowShade {get;init;}=null!;
    internal bool SameVisual(PaletteTrackTarget other)
    {var a=Composite.Shade;var b=other.Composite.Shade;return State==other.State&&ModelsKey==other.ModelsKey&&a.Stops.Equals(b.Stops)&&a.GradientRow.Equals(b.GradientRow)&&a.Title==b.Title&&a.Glow==b.Glow&&a.Motion.Count==b.Motion.Count&&a.Motion.All(p=>b.Motion.TryGetValue(p.Key,out var value)&&value==p.Value)&&TitleShade.GradientRow.Equals(other.TitleShade.GradientRow)&&GlowShade.GradientRow.Equals(other.GlowShade.GradientRow);}
    public static PaletteTrackTarget Create(PaletteSnapshot snapshot,IReadOnlyList<PaletteRenderSelection> workers,IReadOnlyList<PaletteRenderSelection> leads,bool light,PaletteComposite composite)
    {
        var selected=workers.Reverse().Select(x=>(Selection:x,Lead:false)).Concat(leads.Reverse().Select(x=>(Selection:x,Lead:true))).Select(x=>(Shade:snapshot.Resolve(x.Selection.Id,x.Selection.Effort,light),x.Lead,x.Selection.Id)).Where(x=>x.Shade is not null).ToArray();
        var segments=selected.Select(x=>new LiquidSegment((selected.Length==1?x.Shade!.Stops.ToArray():[x.Shade!.Stops[1]]).Select(SwiftColor.Hex).ToArray(),x.Lead?8:5,x.Shade.Motion.ContainsKey("oil_sheen_alpha"),SwiftColor.Hex(x.Shade.Title),x.Lead)).ToArray();
        var row=new SKColor[256];for(var n=0;n<256;n++)row[n]=new(composite.Shade.GradientRow[n*4],composite.Shade.GradientRow[n*4+1],composite.Shade.GradientRow[n*4+2]);
        return new(composite,new(segments),selected.Select(x=>Color.Parse(x.Shade!.Title)).ToArray(),row)
        {TitleStops=PaletteVisibleProjection.Spans(snapshot,workers,leads,light,false),GlowStops=PaletteVisibleProjection.Spans(snapshot,workers,leads,light,true),
         Jewel=JewelMaterial.For(JewelLiquid.FromStops("snapshot",composite.Shade.Stops),EffortSlider.ThumbSize),
         State=selected.Any(x=>snapshot.Models[x.Id].State==PaletteState.Pending)?PaletteState.Pending:selected.All(x=>snapshot.Models[x.Id].State==PaletteState.Router)?PaletteState.Router:PaletteState.Ready,
         SnapshotDigest=snapshot.Digest,ModelsKey=string.Join('|',selected.Select(x=>x.Id+":"+x.Lead)),
         TitleShade=Projection(false),GlowShade=Projection(true)};
        PaletteShade Projection(bool glow)
        {
            var inputs=workers.Select(x=>(x,Lead:false)).Concat(leads.Select(x=>(x,Lead:true)));
            var arrangement=PaletteArrangement.Create(inputs.Where(x=>snapshot.Resolve(x.x.Id,x.x.Effort,light) is not null).Select(x=>{var shade=snapshot.Resolve(x.x.Id,x.x.Effort,light)!;var hex=glow?shade.Glow:shade.Title;return new PaletteSelection(x.x.Id,x.Lead,new([hex,hex,hex]));}).ToArray());
            var bytes=new byte[1024];for(var x=0;x<256;x++){Convert.FromHexString(arrangement.Sample((x+.5)/256)[1..]).CopyTo(bytes,x*4);bytes[x*4+3]=255;}
            return composite.Shade with {GradientRow=new(bytes)};
        }
    }
}
// Mutable per-effect buffers: no shared mutable snapshot, effect, timer or wall clock.
public sealed class PaletteLiveSample
{
    internal readonly byte[] Row=new byte[1024];internal double[] Values=[];
    internal readonly byte[] TitleRow=new byte[1024],GlowRow=new byte[1024];
    internal IReadOnlyList<string> Keys=Array.Empty<string>();
    public PaletteGlideFrame Frame {get;internal set;}
    public PaletteMotionSample Motion {get;} 
    public double FastPhase {get;internal set;} public double SlowPhase {get;internal set;} public double CausticPhase {get;internal set;}
    public double BreathePhase {get;internal set;} public double OilPhase {get;internal set;}
    internal LiquidSpec? OilSpec {get;set;}
    internal PaletteInkWorker? Ink {get;set;}
    public bool Water {get;internal set;} public bool Loading {get;internal set;}
    public double SolidMix {get;internal set;} public double LoadingPhase {get;internal set;}
    public double Specular {get;internal set;}=1;
    public PaletteLiveSample()=>Motion=new(this);
}
public sealed class PaletteMotionSample(PaletteLiveSample sample)
{
    public double Get(string key,double fallback){for(var i=0;i<sample.Keys.Count;i++)if(sample.Keys[i]==key)return sample.Values[i];return fallback;}
}
