// PORT-ONLY: first-release feature unit SK-14 (ONDEMAND 5.4: the terms step stays inside asked). What the in-chat offer reads and asks of the terms step.
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Offers;

public interface ISkillsOfferTerms
{
    /// <summary>The terms the item owes now, or null when it is open or already accepted.</summary>
    TermsOffer? Offer(string itemId);

    /// <summary>The item's pinned licence text (read before any other byte of the item is fetched).</summary>
    Task<string> LicenceTextAsync(string itemId, CancellationToken cancellation);

    /// <summary>Stores the acceptance: the separate "I have read these terms" control was used.</summary>
    void Accept(string itemId);
}
