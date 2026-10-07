// PORT-ONLY: one process pack prepared off the UI thread before any model/window; AC-3 Prime/lifetime.
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.App.Port.ModelsPalette;
internal static class PaletteStartup
{
    internal static PaletteReleasePack? Pack {get;private set;}
    internal static void Prepare()=>Pack=Task.Run(PaletteReleasePack.ReadEmbedded).GetAwaiter().GetResult();
}
