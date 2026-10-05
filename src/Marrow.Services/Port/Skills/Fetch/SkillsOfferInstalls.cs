// PORT-ONLY: first-release feature unit SK-16 (DL-19). Stub for the red run.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.Services.Port.Skills.Fetch;

public sealed class SkillsOfferInstalls(SkillsDownloads downloads) : ISkillsOfferDownload
{
    public Task<IReadOnlyDictionary<string, string>> Install(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation) => throw new NotImplementedException();
    public Task<IReadOnlyDictionary<string, string>> Attach(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation) => throw new NotImplementedException();
}
