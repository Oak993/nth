// PORT-ONLY: SK-5 seam for SK-3 (not merged). ONDEMAND 3.2/3.5: download the installed catalogue pin, never latest.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Store;

public interface IStoreRepairDownloader
{
    // Skill files use their catalogue-relative paths; tool artifacts use CatalogueArtifact.Name.
    // SK-3 owns HTTPS, resume, size/hash checks and progress. This seam never runs an installer.
    Task DownloadAsync(CatalogueItem pinnedItem, string downloadsFolder, CancellationToken cancellationToken);
}
