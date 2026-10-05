// PORT-ONLY: first-release feature unit SK-16 (DL-19). The in-chat offers' download seam (SK-12's ISkillsOfferDownload) bound to the host's shared downloads.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// <see cref="ISkillsOfferDownload"/> over <see cref="SkillsDownloads"/>. Each item of the card is one shared download (it joins one Settings has running, and the reverse); the card's progress is the sum
/// of the items' pinned bytes done (stage Downloading) until every item's bytes are in (stage Checking, the offer's "checking" state). Returns each item's installed folder, which the offer flow takes as the
/// verified absolute path. A cancel of this card leaves the downloads running for whoever else asked.
/// </summary>
public sealed class SkillsOfferInstalls(SkillsDownloads downloads) : ISkillsOfferDownload
{
    public Task<IReadOnlyDictionary<string, string>> Install(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation) => Run(items, progress, cancellation);

    public Task<IReadOnlyDictionary<string, string>> Attach(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Fails closed without an existing download, as SK-12's own bridge does.
        if (items.Any(item => !downloads.IsRunning(item.Id))) throw new InvalidOperationException("No existing download is bound.");
        return Run(items, progress, cancellation);
    }

    private async Task<IReadOnlyDictionary<string, string>> Run(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var card = new Card(items, progress);
        var paths = await Task.WhenAll(items.Select(item => downloads.Install(item, card.For(item), cancellation))).ConfigureAwait(false);
        return items.Select((item, index) => (item.Id, Path: paths[index])).ToDictionary(pair => pair.Id, pair => pair.Path, StringComparer.Ordinal);
    }

    private sealed class Card(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress)
    {
        private readonly object gate = new();
        private readonly Dictionary<string, double> done = items.ToDictionary(item => item.Id, _ => 0.0, StringComparer.Ordinal);
        private readonly long total = items.Sum(SkillsDownloads.Bytes);

        public IProgress<double?> For(CatalogueItem item) => new Item(this, item);

        private void Report(CatalogueItem item, double? value)
        {
            lock (gate)
            {
                done[item.Id] = Math.Clamp(value ?? 0, 0, 1);
                var bytes = (long)Math.Floor(items.Sum(i => done[i.Id] * SkillsDownloads.Bytes(i)));
                var stage = items.All(i => done[i.Id] >= 1) ? OfferStage.Checking : OfferStage.Downloading;
                progress(new OfferProgress(stage, stage == OfferStage.Checking ? total : Math.Min(bytes, total), total));
            }
        }

        private sealed class Item(Card owner, CatalogueItem item) : IProgress<double?>
        {
            public void Report(double? value) => owner.Report(item, value);
        }
    }
}
