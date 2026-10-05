using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Port.Skills.Pipelines;

// PORT-ONLY: explicit SK-3 -> SK-4 bridge. SK-2/SK-19 supplies installer parameters and unpacked sizes; never guessed here.
public sealed class PipelineDownloadSource(Downloader downloader,
    Func<CatalogueItem, ItemRecord, InstallPlan> plans,
    Func<InstallPlan, IReadOnlyList<DownloadRequest>> requests)
{
    public async Task<InstallPlan> DownloadAsync(CatalogueItem item, ItemRecord record, IProgress<double?> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var prepared = Prepare(item, record);
        var plans = await DownloadSetAsync([prepared], value => progress.Report(value.BytesTotal == 0 ? null : (double)value.BytesDone / value.BytesTotal), cancellation).ConfigureAwait(false);
        return plans[0];
    }

    // SK-12 prepares the whole dependency-ordered card first, so SK-3 reports its actual combined bytes and percentage.
    internal (InstallPlan Plan, IReadOnlyList<DownloadRequest> Files) Prepare(CatalogueItem item, ItemRecord record)
    {
        var plan = plans(item, record);
        if (plan.Item != item || plan.Record != record) throw new InvalidDataException("Download plan does not match its item and record.");
        var files = requests(plan);
        if (files.Count == 0) throw new InvalidDataException("An update requires pinned download inputs.");
        foreach (var file in files)
        {
            var key = plan.LocalFiles.Single(p => Path.GetFullPath(p.Value) == Path.GetFullPath(file.DestinationPath)).Key;
            var artifact = item.Artifacts.SingleOrDefault(a => a.Name == key);
            var pinned = item.Files.SingleOrDefault(f => f.Path == key);
            if (artifact is not null)
            {
                if (artifact.Url != file.Url.AbsoluteUri || artifact.Size != file.Size || !string.Equals(artifact.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Download does not match its pinned artifact.");
            }
            else if (pinned is null || pinned.Size != file.Size || !pinned.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Download does not match its pinned file.");
        }
        return (plan, files);
    }

    internal async Task<IReadOnlyList<InstallPlan>> DownloadSetAsync(IReadOnlyList<(InstallPlan Plan, IReadOnlyList<DownloadRequest> Files)> prepared, Action<DownloadProgress> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var files = prepared.SelectMany(p => p.Files).ToArray();
        // Internal callers supply the pinned plans prepared by this source.
        foreach (var input in prepared)
            if (input.Files.Any(f => !input.Plan.LocalFiles.Values.Any(p => Path.GetFullPath(p) == Path.GetFullPath(f.DestinationPath))))
                throw new InvalidDataException("Download does not match its pinned file.");
        var job = downloader.StartSet(files, progress);
        using var registration = cancellation.Register(job.Cancel);
        var outcome = await job.Completion.ConfigureAwait(false);
        if (outcome.Status == DownloadStatus.Cancelled) throw new OperationCanceledException(cancellation);
        if (!outcome.Succeeded) throw new IOException(outcome.Failure?.Message ?? throw new InvalidDataException("Download failed without a reason."));
        return prepared.Select(p => p.Plan.WithDownloads(outcome)).ToArray();
    }
}
