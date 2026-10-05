// PORT-ONLY: first-release feature unit SK-16 (DL-19). The Settings pages' first-install seam (SK-15's IFirstInstall) bound to the host's shared downloads.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// <see cref="IFirstInstall"/> over <see cref="SkillsDownloads"/>: the item is fetched, verified and installed by the pipeline, or joins the download an in-chat offer (or the other page) already has
/// running for it. Returns when the item is installed and throws when it is not: the exception's message is what the row shows.
/// </summary>
public sealed class SkillsFirstInstall(SkillsDownloads downloads) : IFirstInstall
{
    public bool IsAvailable => downloads.Available;

    public async Task InstallAsync(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation) => await downloads.Install(item, progress, cancellation).ConfigureAwait(false);
}
