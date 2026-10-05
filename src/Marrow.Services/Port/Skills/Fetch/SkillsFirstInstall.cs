// PORT-ONLY: first-release feature unit SK-16 (DL-19). Stub for the red run.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.Services.Port.Skills.Fetch;

public sealed class SkillsFirstInstall(SkillsDownloads downloads) : IFirstInstall
{
    public Task InstallAsync(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation) => throw new NotImplementedException();
}
