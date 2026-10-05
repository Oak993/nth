// PORT-ONLY: first-release feature unit SK-14 (DL-18). There is no Swift twin.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Services.Port.Skills.Offers;

/// <summary>
/// The offer's states (5.4) as what the card shows. ENGINEERING DECISIONS (ED-NEW-cloud-sk-14): (1) Cancel is offered while the bytes come in and not while they are checked, as the Settings row does
/// (the design draws a cancel on the running bar only); (2) Return's primary action is Download while asked and Retry after a failure; (3) a decline is available while asked or failed (the flow's own
/// rule) and never while a download runs; (4) an item whose terms are owed keeps the card asked with the download off (5.4: the terms step stays inside asked); (5) the percent is the bytes done over the
/// total, rounded down, and none without a total.
/// </summary>
public static class OfferCardProjector
{
    public static OfferCardView? Project(SkillsCatalogue catalogue, AddonsState state, OfferCard? card, OfferStage stage, OfferProgress? progress, string? failure, bool canFetch = true)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(state);
        if (card is null) return null;
        var items = card.Items.Select(id => catalogue.Items.Single(i => i.Id == id)).ToList();
        var hosts = items.Select(i => i.SourceUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var owedItems = stage == OfferStage.Asked ? items.Where(i => ItemTerms.Required(i, state)).Select(i => i.Id).ToList() : [];
        var owed = owedItems.Count > 0;
        var running = stage is OfferStage.Downloading or OfferStage.Checking && progress is not null;
        int? percent = running && progress!.BytesTotal is > 0 and { } total ? (int)Math.Min(100, progress.BytesDone * 100 / total) : null;
        var primary = stage switch
        {
            OfferStage.Asked => LibraryWords.Download(card.ApproxSizeMb),
            OfferStage.Failed => LibraryWords.Retry,
            _ => null,
        };
        return new OfferCardView(stage, items.Select(i => i.Title).ToList(), card.ApproxSizeMb, hosts, percent, running ? progress!.BytesDone : null, running ? progress!.BytesTotal : null,
            primary, primary is not null && !owed && canFetch, stage == OfferStage.Downloading, stage is OfferStage.Asked or OfferStage.Failed,
            stage switch { OfferStage.Checking => LibraryWords.Checking, OfferStage.Failed => LibraryWords.DidNotDownload, _ => null },
            stage == OfferStage.Failed ? failure : null, owed, primary is not null && !canFetch ? LibraryWords.DownloadsUnavailable : null, owedItems);
    }
}
