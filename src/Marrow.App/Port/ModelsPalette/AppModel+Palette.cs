// PORT-ONLY: OD-0127 shared RefreshNow authority; ED-618.
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Core.Models;
using Marrow.Services.Port.ModelsPalette;
using Marrow.UI.Port.ModelsPalette;
using System.ComponentModel;
namespace Marrow.App;
public sealed partial class AppModel
{
    private PaletteEngine? paletteEngine;
    private Port.ModelsPalette.ModelShades? paletteShades;
    internal Port.ModelsPalette.ModelShades PaletteShades=>paletteShades??=new(PaletteEngine);
    // ED-NEW-T44 / T45 c1ce62b66 / R-TEST-5: observe ownership only; production never substitutes the
    // engine. A disposable fixture owns every model created in its ambient roots,
    // including models constructed directly instead of through MakeModel.
    internal static Ambient<Action<AppModel>> PaletteLifetimeForTesting { get; } = new(_ => { });
    internal PaletteEngine PaletteEngine
    {
        get
        {
            PaletteLifetimeForTesting.Current(this);
            return paletteEngine??=new PaletteEngine(Storage.Root,URLSession.Transport.Current,new SkiaLogoDecoder(),time:SwiftClock.Provider.Current,releasePack:Port.ModelsPalette.PaletteStartup.Pack);
        }
    }
    private void InitializePalette()
    {
        // Only the desktop startup supplies the prepared pack; isolated source-twin tests do not start extra services.
        if(Port.ModelsPalette.PaletteStartup.Pack is null)return;
        _ =PalettePrimeBudget.BeforeWindow(PaletteEngine.Prime()).GetAwaiter().GetResult();Providers.PropertyChanged+=PaletteCatalogChanged;
    }
    private void PaletteCatalogChanged(object? sender,PropertyChangedEventArgs e)
    {if(e.PropertyName==nameof(Providers.Catalogs))_ =CollectPaletteCatalogues();}
    private Task CollectPaletteCatalogues()=>PaletteEngine.ObserveCatalogues(Providers.Catalogs.SelectMany(p=>p.Value.Select(m=>new PaletteCatalogueModel(p.Key.Id,m.Id,m.Name))).ToArray());
    private void StartPaletteChecks()
    {if(paletteEngine is null)return;_ =CollectPaletteCatalogues();paletteEngine.Start();}
    internal void ResumePaletteChecks()=>paletteEngine?.NotifyResume();
    internal void StopPaletteChecks()
    {Providers.PropertyChanged-=PaletteCatalogChanged;paletteShades?.Dispose();paletteShades=null;paletteEngine?.Dispose();paletteEngine=null;}
    internal Port.ModelsPalette.PaletteColoursController CreatePaletteColoursController()=>new(PaletteEngine,()=>Providers.AvailableProviders.Count(p=>p!=ProviderKind.OpenRouter),sharedFacade:PaletteShades,collectCatalogues:()=>PaletteEngine.ObserveCatalogues(Providers.Catalogs.SelectMany(p=>p.Value.Select(m=>new PaletteCatalogueModel(p.Key.Id,m.Id,m.Name))).ToArray()));
}
