// PORT-ONLY: first-release feature unit SK-16 (DL-19, DL-15: the licence text is shown before any other byte of the item is fetched). There is no Swift twin.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// Reads the item's pinned licence file through the one downloader: the file at <c>LicenceUrl</c>, held to the length and hash the catalogue pins for <c>LicenceFile</c> (the terms step checks them again),
/// and nothing else of the item. The fetched copy is removed once it is read. ED-NEW-dl-13: of a plugin item whose licences are recorded per plugin it reads each named licence file, at the item's pin
/// (<see cref="CatalogueInstallPlans.Url"/>, <paramref name="rawBase"/> as the plans use it), and no other file.
/// </summary>
public sealed class DownloaderLicenceText(Downloader downloader, string dataRoot, Uri? rawBase = null) : ILicenceText
{
    public async Task<byte[]> ReadAsync(CatalogueItem item, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(item);
        var pinned = item.Files.SingleOrDefault(file => file.Path == item.LicenceFile) ?? throw new InvalidOperationException("The licence file has no pin.");
        var url = new Uri(item.LicenceUrl ?? throw new InvalidOperationException("The licence has no address."));
        return await FetchAsync(item, url, pinned, cancellation).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadFileAsync(CatalogueItem item, string path, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.LicenceFile is not null && path == item.LicenceFile) return await ReadAsync(item, cancellation).ConfigureAwait(false);
        if (!PluginLicences.PerPlugin(item) || !PluginLicences.Files(item).Contains(path, StringComparer.Ordinal)) throw new InvalidOperationException("That file is not one of the item's licence files.");
        var pinned = item.Files.SingleOrDefault(file => file.Path == path) ?? throw new InvalidOperationException("The licence file has no pin.");
        return await FetchAsync(item, new CatalogueInstallPlans(dataRoot, rawBase).Url(item, path), pinned, cancellation).ConfigureAwait(false);
    }

    private async Task<byte[]> FetchAsync(CatalogueItem item, Uri url, CatalogueFile pinned, CancellationToken cancellation)
    {
        var folder = Path.Combine(Path.GetFullPath(dataRoot), "addons", "downloads", "licence", item.Id + "-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(folder, "LICENSE");
        try
        {
            var job = downloader.Start(new DownloadRequest(url, destination, pinned.Size, pinned.Sha256, url.Host, pinned.Size));
            using var registration = cancellation.Register(job.Cancel);
            var outcome = await job.Completion.ConfigureAwait(false);
            if (outcome.Status == DownloadStatus.Cancelled) throw new OperationCanceledException(cancellation);
            if (!outcome.Succeeded) throw new IOException(outcome.Failure?.Message ?? "The licence file was not downloaded.");
            return await File.ReadAllBytesAsync(destination, cancellation).ConfigureAwait(false);
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* removed by the next reset */ }
        }
    }
}
