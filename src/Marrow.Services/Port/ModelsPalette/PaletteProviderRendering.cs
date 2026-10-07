// PORT-ONLY: canonical provider identities are resolved on the palette actor, from genuine catalogue facts.
// ED-625. Provider input never contributes price/date to global model ranking.
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public readonly record struct PaletteProviderRenderSelection(string Provider,string Id,string Name,string? Effort);
public sealed partial class PaletteEngine
{
    public Task<T> PrepareProviderRender<T>(IReadOnlyList<PaletteProviderRenderSelection> workers,IReadOnlyList<PaletteProviderRenderSelection> leads,bool light,Func<PaletteSnapshot,PaletteRenderSelection[],PaletteRenderSelection[],PaletteComposite?,T> prepare)
    {
        var capturedWorkers=workers.ToArray();var capturedLeads=leads.ToArray();
        return Isolated(()=>
        {
            PrimeOnActor();var facts=Facts();var changed=false;
            PaletteRenderSelection Map(PaletteProviderRenderSelection x)
            {
                var maker=ResolveMaker(x.Provider,x.Id,facts,x.Name);var canonical=LineupRanker.Canonical(x.Id);
                var member=facts.Where(f=>f.Maker==maker.Maker&&LineupRanker.Canonical(f.Id)==canonical).OrderBy(f=>f.Id,StringComparer.Ordinal).FirstOrDefault();
                var id=member?.Id??(x.Id.Contains('/',StringComparison.Ordinal)?x.Id:maker.Maker+"/"+x.Id);
                if(observed.Add(id)){observedNames[id]=x.Name;changed=true;}
                return new(id,x.Effort);
            }
            var w=capturedWorkers.Select(Map).ToArray();var l=capturedLeads.Select(Map).ToArray();
            if(changed){Publish();QueueCheck();}
            return prepare(Current,w,l,PaletteComposition.Create(Current,w,l,light));
        });
    }
}
