// PORT-ONLY: SK-5 boundary for SK-4. Downloaded archives need an installer before the installed-tree check.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Store;

public interface IStoreRepairInstaller
{
    Task InstallAsync(CatalogueItem pinnedItem, string downloadsFolder, string preparedFolder, CancellationToken cancellationToken);
}
