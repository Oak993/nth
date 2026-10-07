// PORT-ONLY: actor-prepared C25 handoff after T9's Strike merge; ED-NEW-models-palette-14.
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
    public static PaletteTrackTarget Create(PaletteSnapshot snapshot,IReadOnlyList<PaletteRenderSelection> workers,IReadOnlyList<PaletteRenderSelection> leads,bool light,PaletteComposite composite)
    {
        var selected=workers.Reverse().Select(x=>(Selection:x,Lead:false)).Concat(leads.Reverse().Select(x=>(Selection:x,Lead:true))).Select(x=>(Shade:snapshot.Resolve(x.Selection.Id,x.Selection.Effort,light),x.Lead,x.Selection.Id)).Where(x=>x.Shade is not null).ToArray();
        var segments=selected.Select(x=>new LiquidSegment((selected.Length==1?x.Shade!.Stops.ToArray():[x.Shade!.Stops[1]]).Select(SwiftColor.Hex).ToArray(),x.Lead?8:5,x.Shade.Motion.ContainsKey("oil_sheen_alpha"),SwiftColor.Hex(x.Shade.Title),x.Lead)).ToArray();
        var row=new SKColor[256];for(var n=0;n<256;n++)row[n]=new(composite.Shade.GradientRow[n*4],composite.Shade.GradientRow[n*4+1],composite.Shade.GradientRow[n*4+2]);
        return new(composite,new(segments),selected.Select(x=>Color.Parse(x.Shade!.Title)).ToArray(),row);
    }
}
// Mutable per-effect buffers: no shared mutable snapshot, effect, timer or wall clock.
public sealed class PaletteLiveSample
{
    internal readonly byte[] Row=new byte[1024];internal double[] Values=[];
    internal IReadOnlyList<string> Keys=Array.Empty<string>();
    public PaletteGlideFrame Frame {get;internal set;}
    public PaletteMotionSample Motion {get;} 
    public double FastPhase {get;internal set;} public double SlowPhase {get;internal set;} public double CausticPhase {get;internal set;}
    public double BreathePhase {get;internal set;} public double OilPhase {get;internal set;}
    public PaletteLiveSample()=>Motion=new(this);
}
public sealed class PaletteMotionSample(PaletteLiveSample sample)
{
    public double Get(string key,double fallback){for(var i=0;i<sample.Keys.Count;i++)if(sample.Keys[i]==key)return sample.Values[i];return fallback;}
}
