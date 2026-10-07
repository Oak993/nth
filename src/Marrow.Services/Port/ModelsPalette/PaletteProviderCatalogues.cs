// PORT-ONLY: AC-3 3A.2 read-only provider input; never a reload or a global lineup member. ED-617.
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public sealed record PaletteCatalogueModel(string Provider,string Id,string Name);
public sealed partial class PaletteEngine
{
    private readonly Dictionary<string,string> observedNames=new(StringComparer.Ordinal);
    public Task ObserveCatalogues(IReadOnlyList<PaletteCatalogueModel> catalogues)=>Isolated(()=>
    {
        PrimeOnActor();var facts=Facts();var changed=false;
        var publicNames=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var row in PublicRows())
        {using var json=System.Text.Json.JsonDocument.Parse(row.PublicFacts);if(json.RootElement.TryGetProperty("name",out var n)&&n.ValueKind==System.Text.Json.JsonValueKind.String)publicNames[row.Id]=n.GetString()!;}
        foreach(var model in catalogues)
        {
            var maker=ResolveMaker(model.Provider,model.Id,facts,model.Name,publicNames);
            var canonical=LineupRanker.Canonical(model.Id);
            var listed=facts.Where(f=>f.Maker==maker.Maker&&LineupRanker.Canonical(f.Id)==canonical).OrderBy(f=>f.Id,StringComparer.Ordinal).FirstOrDefault();
            var id=listed?.Id??(model.Id.Contains('/',StringComparison.Ordinal)?model.Id:maker.Maker+"/"+model.Id);
            changed|=observed.Add(id)||!string.Equals(observedNames.GetValueOrDefault(id),model.Name,StringComparison.Ordinal);observedNames[id]=model.Name;
        }
        if(changed){Publish();QueueCheck();}
    });
}
