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
        var plan = plans(item, record);
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
        var job = downloader.StartSet(files, value => progress.Report(value.BytesTotal == 0 ? null : (double)value.BytesDone / value.BytesTotal));
        using var registration = cancellation.Register(job.Cancel);
        var outcome = await job.Completion.ConfigureAwait(false);
        if (outcome.Status == DownloadStatus.Cancelled) throw new OperationCanceledException(cancellation);
        if (!outcome.Succeeded) throw new IOException(outcome.Failure?.Message ?? throw new InvalidDataException("Download failed without a reason."));
        return plan.WithDownloads(outcome);
    }
}
