// PORT-ONLY: DL-13 injected verified download/install boundary; no implicit network or CLI.
using Marrow.Core.Port.Skills.Catalogue;
namespace Marrow.Services.Port.Skills.Offers;
public interface ISkillsOfferDownload
{
    /// <summary>SK-14 review: whether the item can be fetched at all (the seams are bound and its pin is complete). The card keeps its Download off while it cannot; a scripted bridge keeps the default.</summary>
    bool CanFetch(CatalogueItem item) => true;

    Task<IReadOnlyDictionary<string, string>> Install(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation);
    Task<IReadOnlyDictionary<string, string>> Attach(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation) => throw new InvalidOperationException("No existing download is bound.");
}
