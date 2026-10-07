// PORT-ONLY: C25 composes selected models on the engine actor; rendering reads precomputed RGB only.
using System.Collections.Frozen;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public sealed record PaletteComposite(PaletteArrangement Arrangement,PaletteShade Shade);
public static class PaletteComposition
{
    public static PaletteComposite? Create(PaletteSnapshot snapshot,IReadOnlyList<PaletteRenderSelection> workers,IReadOnlyList<PaletteRenderSelection> leads,bool light)
    {
        var chosen=workers.Select(x=>(Selection:x,Lead:false)).Concat(leads.Select(x=>(Selection:x,Lead:true))).Select(x=>(Shade:snapshot.Resolve(x.Selection.Id,x.Selection.Effort,light),x.Selection.Id,x.Lead)).Where(x=>x.Shade is not null).ToArray();
        if(chosen.Length==0)return null;
        var arrangement=PaletteArrangement.Create(chosen.Select(x=>new PaletteSelection(x.Id,x.Lead,x.Shade!.Stops)).ToArray());
        if(chosen.Length==1)return new(arrangement,chosen[0].Shade!);
        var row=new byte[1024];for(var x=0;x<256;x++){Convert.FromHexString(arrangement.Sample((x+.5)/256)[1..]).CopyTo(row,x*4);row[x*4+3]=255;}
        var total=chosen.Sum(x=>x.Lead?8:5);
        var motion=chosen.SelectMany(x=>x.Shade!.Motion.Keys).Distinct(StringComparer.Ordinal).ToFrozenDictionary(k=>k,k=>chosen.Sum(x=>x.Shade!.Motion.GetValueOrDefault(k)*(x.Lead?8:5))/total,StringComparer.Ordinal);
        var titles=PaletteArrangement.Create(chosen.Select(x=>new PaletteSelection(x.Id,x.Lead,new([x.Shade!.Title,x.Shade.Title,x.Shade.Title]))).ToArray());
        return new(arrangement,new(new([arrangement.Sample(0),arrangement.Sample(.5),arrangement.Sample(1)]),titles.Sample(.5),arrangement.Sample(.5),motion,new(row)));
    }
}
public sealed partial class PaletteEngine
{
    // The app adapter is invoked on the same actor as composition: no colour conversion on the UI thread.
    public Task<T> PrepareRender<T>(IReadOnlyList<PaletteRenderSelection> workers,IReadOnlyList<PaletteRenderSelection> leads,bool light,Func<PaletteSnapshot,PaletteComposite?,T> prepare)
    {
        var capturedWorkers=workers.ToArray();var capturedLeads=leads.ToArray();
        return Isolated(()=>{PrimeOnActor();return prepare(Current,PaletteComposition.Create(Current,capturedWorkers,capturedLeads,light));});
    }
    public Task<PaletteComposite?> Compose(IReadOnlyList<PaletteRenderSelection> workers,IReadOnlyList<PaletteRenderSelection> leads,bool light)
    {
        var capturedWorkers=workers.ToArray();var capturedLeads=leads.ToArray();
        return Isolated(()=>{PrimeOnActor();return PaletteComposition.Create(Current,capturedWorkers,capturedLeads,light);});
    }
}
