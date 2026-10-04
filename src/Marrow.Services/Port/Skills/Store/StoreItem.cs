// PORT-ONLY: SK-5. Immutable installed-pin descriptor. No disk manifest is a trust root.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreItem
{
    public StoreItem(CatalogueItem catalogue, string folder, StoreManifest manifest) => throw new NotImplementedException();
    public CatalogueItem Catalogue => throw new NotImplementedException();
    public string Folder => throw new NotImplementedException();
    public StoreManifest Manifest => throw new NotImplementedException();
}
