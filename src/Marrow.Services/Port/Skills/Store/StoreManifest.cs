// PORT-ONLY: SK-5. The trusted whole-tree inventory, provided from the embedded catalogue or verified SK-4 installation.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreManifest
{
    public StoreManifest(IEnumerable<CatalogueFile> files) => throw new NotImplementedException();
    public IReadOnlyList<CatalogueFile> Files => throw new NotImplementedException();
}
