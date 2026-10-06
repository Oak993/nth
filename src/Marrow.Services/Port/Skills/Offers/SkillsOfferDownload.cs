// PORT-ONLY: SK-12 checked SK-3 -> SK-4 -> routing-publication bridge, ONDEMAND 3.3-3.6 and 5.4.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;
namespace Marrow.Services.Port.Skills.Offers;

public sealed class SkillsOfferDownload(PipelineDownloadSource source, ItemInstaller installer, Func<CatalogueItem, InstallResult, string> publish) : ISkillsOfferDownload
{
    private readonly object gate = new();
    private readonly Dictionary<string, OfferTransfer> transfers = new(StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, string>> Install(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Refuse the whole card before preparing any file or starting any download.
        if (items.Any(i => i.PinState != PinState.Pinned || CatalogueGate.Missing(i).Count != 0))
            throw new InvalidDataException("Download does not match its pinned file.");
        OfferTransfer? created = null;
        List<OfferTransfer> selected;
        lock (gate)
        {
            var missing = items.Where(i => !transfers.ContainsKey(i.Id)).ToArray();
            if (missing.Length != 0)
            {
                created = new(missing);
                foreach (var item in missing) transfers.Add(item.Id, created);
            }
            selected = Select(items);
        }
        // Subscribe before starting: synchronous progress and completion cannot be lost.
        var wait = Wait(selected, items, progress, cancellation);
        if (created is { } transfer) _ = Run(transfer, cancellation);
        return wait;
    }
    public Task<IReadOnlyDictionary<string, string>> Attach(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        lock (gate) return Wait(Select(items), items, progress, cancellation); // Select fails closed without an existing job.
    }
    private List<OfferTransfer> Select(IReadOnlyList<CatalogueItem> items) => items.Select(item =>
    {
        if (!transfers.TryGetValue(item.Id, out var transfer) || !transfer.Items.Contains(item)) throw new InvalidOperationException("No existing download is bound.");
        return transfer;
    }).Distinct().ToList();

    private async Task Run(OfferTransfer transfer, CancellationToken cancellation)
    {
        try
        {
            var prepared = transfer.Items.Select(item => source.Prepare(item, new ItemRecord(item.Id, ItemState.Checking, null, item.Pin, null))).ToArray();
            var plans = await source.DownloadSetAsync(prepared, bytes => transfer.Report(new(OfferStage.Downloading, bytes.BytesDone, bytes.BytesTotal)), cancellation).ConfigureAwait(false);
            var total = prepared.SelectMany(p => p.Files).Sum(f => f.Size);
            transfer.Report(new(OfferStage.Checking, total, total));
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var plan in plans)
            {
                var result = await installer.InstallAsync(plan, cancellation).ConfigureAwait(false);
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    // The host publishes shims/state here, after SK-4's lock and whole-tree verification.
                    var path = publish(plan.Item, result);
                    if (result.Record.State != ItemState.Installed || !Path.IsPathFullyQualified(path)) throw new InvalidDataException("Download does not match its pinned file.");
                    paths.Add(plan.Item.Id, path);
                }
                finally { result.Reservation?.Dispose(); }
            }
            transfer.Result.TrySetResult(paths);
        }
        catch (Exception error)
        {
            lock (gate) foreach (var item in transfer.Items)
                if (transfers.GetValueOrDefault(item.Id) == transfer) transfers.Remove(item.Id);
            transfer.Result.TrySetException(error); // Retry uses SK-3's remaining .part, or its verified local file.
        }
    }
    private static async Task<IReadOnlyDictionary<string, string>> Wait(IReadOnlyList<OfferTransfer> selected, IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation)
    {
        var leave = selected.Select(t => t.Listen(progress)).ToArray();
        try
        {
            var results = await Task.WhenAll(selected.Select(t => t.Result.Task.WaitAsync(cancellation))).ConfigureAwait(false);
            return items.ToDictionary(i => i.Id, i => results.Single(r => r.ContainsKey(i.Id))[i.Id], StringComparer.Ordinal);
        }
        finally { foreach (var remove in leave) remove(); }
    }
}
