// PORT-ONLY: one process pack prepared off the UI thread before any model/window; AC-3 Prime/lifetime.
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.App.Port.ModelsPalette;
internal static class PaletteStartup
{
    private static readonly PaletteStartupWork Work = new(PaletteReleasePack.ReadEmbedded);
    internal static PaletteReleasePack? Pack {get;private set;}
    internal static void Begin()=>Work.Begin();
    internal static void Prepare()=>Pack=Work.Finish();
    internal static async Task PrepareAsync()=>Pack=await Work.FinishAsync();
}
