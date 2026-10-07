// PORT-ONLY: OD-0127 shared RefreshNow authority; ED-NEW-models-palette-7.
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Core.Models;
using Marrow.Services.Port.ModelsPalette;
using Marrow.UI.Port.ModelsPalette;
namespace Marrow.App;
public sealed partial class AppModel
{
    private PaletteEngine? paletteEngine;
    internal PaletteEngine PaletteEngine=>paletteEngine??=new PaletteEngine(Storage.Root,URLSession.Transport.Current,new SkiaLogoDecoder());
    internal Port.ModelsPalette.PaletteColoursController CreatePaletteColoursController()=>new(PaletteEngine,()=>Providers.AvailableProviders.Count(p=>p!=ProviderKind.OpenRouter),collectCatalogues:()=>PaletteEngine.ObserveCatalogues(Providers.Catalogs.SelectMany(p=>p.Value.Select(m=>new PaletteCatalogueModel(p.Key.Id,m.Id,m.Name))).ToArray()));
}
