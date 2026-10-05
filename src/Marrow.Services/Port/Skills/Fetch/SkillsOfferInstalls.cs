// PORT-ONLY: first-release feature unit SK-16 (DL-19). The in-chat offers' download seam (SK-12's ISkillsOfferDownload) bound to the host's shared downloads.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// <see cref="ISkillsOfferDownload"/> over <see cref="SkillsDownloads"/>. Each item of the card is one shared download (it joins one Settings has running, and the reverse). ENGINEERING DECISIONS
/// (ED-NEW-cloud-sk-16, review): (1) the card's bytes are those of the DISTINCT items it fetches (the items and the needs they share, each once) and an item that is here already counts as done, so a shared dependency
/// is not counted twice and the card reaches Checking when every byte is in whether or not an item was installed before; (2) the first failure (or a cancel) of any item ends the card at once, without
/// waiting for the others: the card's own interest in the rest is withdrawn, which stops a transfer only if no one else asked for it (a Settings page that did keeps it); (3) the caller's callback is never
/// called while a lock is held. Returns each item's installed folder, which the offer flow takes as the verified absolute path.
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
        var card = new Card(downloads, items, progress);
        using var detach = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var tasks = items.Select(item => downloads.Install(item, Ignore.Instance, detach.Token, card.Part)).ToList();
        try
        {
            var pending = new List<Task<string>>(tasks);
            while (pending.Count > 0)
            {
                var done = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(done);
                await done.ConfigureAwait(false); // the first failure or cancel ends the card here
            }
        }
        catch
        {
            await detach.CancelAsync().ConfigureAwait(false);
            _ = Task.WhenAll(tasks).ContinueWith(finished => _ = finished.Exception, TaskScheduler.Default); // the others end cancelled or failed: nobody awaits them
            throw;
        }
        return items.Select((item, index) => (item.Id, Path: tasks[index].Result)).ToDictionary(pair => pair.Id, pair => pair.Path, StringComparer.Ordinal);
    }

    private sealed class Card
    {
        private readonly object gate = new();
        private readonly object delivery = new();
        private readonly Action<OfferProgress> progress;
        private readonly Dictionary<string, long> sizes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> done = new(StringComparer.Ordinal);
        private readonly long total;
        private long seq;
        private long delivered;

        public Card(SkillsDownloads downloads, IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress)
        {
            this.progress = progress;
            foreach (var item in items)
            {
                var pending = downloads.Pending(item);
                if (pending.Count == 0) { sizes[item.Id] = SkillsDownloads.Bytes(item); done[item.Id] = 1; } // here already: its bytes are in
                foreach (var member in pending) { sizes.TryAdd(member.Id, SkillsDownloads.Bytes(member)); done.TryAdd(member.Id, 0); }
            }
            total = sizes.Values.Sum();
        }

        public void Part(string id, double fraction)
        {
            OfferProgress? next = null;
            long number;
            lock (gate)
            {
                if (!sizes.ContainsKey(id)) return;
                done[id] = Math.Max(done[id], Math.Clamp(fraction, 0, 1));
                var bytes = (long)Math.Floor(sizes.Sum(pair => done[pair.Key] * pair.Value));
                var stage = done.Values.All(value => value >= 1) ? OfferStage.Checking : OfferStage.Downloading;
                next = new OfferProgress(stage, stage == OfferStage.Checking ? total : Math.Min(bytes, total), total);
                number = ++seq;
            }
            lock (delivery)
            {
                if (number <= delivered) return;
                delivered = number;
                progress(next);
            }
        }
    }

    /// <summary>The card follows each item's own part, not the chain-wide fraction (which counts a shared need once for each item that needs it).</summary>
    private sealed class Ignore : IProgress<double?>
    {
        public static Ignore Instance { get; } = new();

        public void Report(double? value) { }
    }
}
