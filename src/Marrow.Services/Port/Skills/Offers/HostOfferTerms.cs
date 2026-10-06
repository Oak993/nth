// PORT-ONLY: first-release feature unit SK-14. The offer's terms seam over the host's terms step: one record of acceptance (state.json, with its date and the licence hash), whichever surface took it.
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Offers;

public sealed class HostOfferTerms(TermsStep terms) : ISkillsOfferTerms
{
    public TermsOffer? Offer(string itemId) => terms.Offer(itemId);

    public Task<string> LicenceTextAsync(string itemId, CancellationToken cancellation) => terms.LicenceTextAsync(itemId, cancellation);

    public void Accept(string itemId) => terms.Accept(itemId, readControlChecked: true);
}
