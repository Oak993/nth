// PORT-ONLY: first-release feature unit SK-14 (DL-18; OD-0099 Offer A: an inline card in the reply). There is no Swift twin.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;

namespace Marrow.App.Port.Skills;

/// <summary>
/// Where the offer card sits in a reply: after the timeline's rows, before its end sentinel (the caller adds the sentinel after this). A chat that has a skills offer flow gets the card and, right after it, the caption line
/// the ended offer leaves; both take no room while there is nothing to say. A chat without a flow gets nothing.
/// </summary>
public static class SkillsOfferRows
{
    /// <summary>Appends the card and the caption of the chat's offer flow to <paramref name="body"/>; false when the chat has none.</summary>
    public static bool Append(Panel body, ThreadRuntime runtime, double above)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(runtime);
        if (runtime.SkillsOffers is not { } offers) return false;
        var card = new OfferCardControl(offers, offers.Catalogue, offers.State) { Margin = new Thickness(0, above, 0, 0) };
        body.Children.Add(card);
        body.Children.Add(new OfferCaptionControl(offers, offers.Catalogue, card));
        return true;
    }
}
