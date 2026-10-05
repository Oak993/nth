// PORT-ONLY: first-release feature unit SK-14 (DL-18, DL-19). There is no Swift twin. Where an offer for a chat is made over the app's one skills host.
using Marrow.App.Runtime;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.App.Port.Skills;

/// <summary>
/// One offer for one chat, over the host: the catalogue it was opened with, the pipeline's state (installed items, accepted terms, the chats' declined offers), the host's shared downloads
/// (<see cref="SkillsHost.OfferDownloads"/>: an offer joins a download Settings has running, and the reverse) and the pipeline's writer of declined offers (<see cref="Marrow.Services.Port.Skills.Pipelines.SkillsPipeline.SetDeclined"/>:
/// a decline is written before the provider is answered). The detection snapshot also includes the host's live downloads, so an offer attaches to an existing transfer without persisting transient state
/// (ENGINEERING DECISION ED-NEW-w3-sk14-fix). AppModel creates these flows per chat and their cards are mounted inline in the reply (OD-0099 Offer A).
/// </summary>
public static class SkillsOfferFlows
{
    public static SkillsOfferFlow Create(ThreadRuntime runtime, SkillsHost host)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(host);
        return new SkillsOfferFlow(runtime, host.Catalogue, () => host.Downloads.StateForDetection, host.Pipeline.SetDeclined, host.OfferDownloads, host.OfferTerms);
    }
}
