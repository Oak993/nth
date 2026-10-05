// PORT-ONLY: first-release feature unit SK-14 (DL-18). What the in-chat offer card reads and asks of an offer; SkillsOfferFlow is the one that answers it.
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.App.Port.Skills;

public interface IOfferCardSource
{
    OfferStage Stage { get; }

    OfferCard? Card { get; }

    OfferProgress? Progress { get; }

    /// <summary>Whether what the card names can be fetched at all; false keeps the card's Download and Retry off.</summary>
    bool CanFetch { get; }

    string? Failure { get; }

    /// <summary>Grows when the terms step's answer may have changed (an acceptance): the card reads its state again.</summary>
    int TermsRevision { get; }

    /// <summary>The terms the item owes now, or null (none, or no terms step is bound).</summary>
    Marrow.Services.Port.Skills.Terms.TermsOffer? TermsFor(string itemId);

    Task<string> LicenceText(string itemId);

    /// <summary>The separate "I have read these terms" control was used: the acceptance is stored.</summary>
    void AcceptTerms(string itemId);

    /// <summary>Yes (and Retry): downloads what the card names.</summary>
    Task Accept();

    /// <summary>Cancel while the bytes come in: the card returns to asked.</summary>
    void Cancel();

    void Decline();
}
