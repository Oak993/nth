// PORT-ONLY: AC-3 3A.2 read-only provider input; never a reload or a global lineup member. ED-NEW-models-palette-6.
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public sealed record PaletteCatalogueModel(string Provider,string Id,string Name);
public sealed partial class PaletteEngine
{
    private readonly Dictionary<string,string> observedNames=new(StringComparer.Ordinal);
    public Task ObserveCatalogues(IReadOnlyList<PaletteCatalogueModel> catalogues)=>Isolated(()=>
    {
        var facts=Facts();var changed=false;
        foreach(var model in catalogues)
        {
            var maker=Makers.Resolve(model.Provider,model.Id,facts);
            var canonical=LineupRanker.Canonical(model.Id);
            var listed=facts.Where(f=>f.Maker==maker.Maker&&LineupRanker.Canonical(f.Id)==canonical).OrderBy(f=>f.Id,StringComparer.Ordinal).FirstOrDefault();
            var id=listed?.Id??(model.Id.Contains('/',StringComparison.Ordinal)?model.Id:maker.Maker+"/"+model.Id);
            changed|=observed.Add(id);observedNames[id]=model.Name;
        }
        if(changed){Publish();QueueCheck();}
    });
}
