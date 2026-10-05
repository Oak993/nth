// PORT-ONLY: first-release feature unit SK-14 (DL-18, DL-19). There is no Swift twin. Where an offer for a chat is made over the app's one skills host.
using Marrow.App.Runtime;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.App.Port.Skills;

/// <summary>
/// One offer for one chat, over the host: the catalogue it was opened with, the pipeline's state (installed items, accepted terms, the chats' declined offers), the host's shared downloads
/// (<see cref="SkillsHost.OfferDownloads"/>: an offer joins a download Settings has running, and the reverse) and the pipeline's writer of declined offers (<see cref="Marrow.Services.Port.Skills.Pipelines.SkillsPipeline.SetDeclined"/>:
/// a decline is written before the provider is answered). ENGINEERING DECISION (ED-NEW-cloud-sk-14): nothing in the app calls this yet. A made offer holds a provider's command and answers a model's
/// <c>request_tool</c> only when someone presses its card, and the card's place in a reply (OD-0099 Offer A) is not in the repository's design sources, so no thread gets one: the first one made without a card
/// to answer it would hang the model's request.
/// </summary>
public static class SkillsOfferFlows
{
    public static SkillsOfferFlow Create(ThreadRuntime runtime, SkillsHost host)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(host);
        return new SkillsOfferFlow(runtime, host.Catalogue, () => host.Pipeline.State, host.Pipeline.SetDeclined, host.OfferDownloads);
    }
}
