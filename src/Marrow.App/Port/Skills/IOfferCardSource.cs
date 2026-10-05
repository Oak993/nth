// PORT-ONLY: first-release feature unit SK-14 (DL-18). What the in-chat offer card reads and asks of an offer; SkillsOfferFlow is the one that answers it.
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.App.Port.Skills;

public interface IOfferCardSource
{
    OfferStage Stage { get; }

    OfferCard? Card { get; }

    OfferProgress? Progress { get; }

    string? Failure { get; }

    /// <summary>Yes (and Retry): downloads what the card names.</summary>
    Task Accept();

    /// <summary>Cancel while the bytes come in: the card returns to asked.</summary>
    void Cancel();

    void Decline();
}
