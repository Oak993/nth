// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): model downloads on the shared downloader (V-9.5).
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

/// <summary>
/// V-9.5 on SK-3's downloader (one downloader in the app: no second HTTP client). The shared downloader resumes with Range, restarts on a 200,
/// verifies size and SHA-256 while streaming, moves the file into the store only when both match, keeps the partial on cancel, limits progress to
/// ten a second and follows https redirects; this class adds the store, the manifest's pins and the back-off.
/// </summary>
public sealed class ModelDownloader
{
    private readonly ModelStore store;
    private readonly Downloader downloader;
    private readonly Func<ModelFile, Uri> urlFor;
    private readonly Lock gate = new();
    private readonly Dictionary<string, ModelDownload> active = new(StringComparer.Ordinal);

    /// <summary>The first-URL host list of model downloads (V-3.3: Hugging Face; the CDN it redirects to need not be listed).</summary>
    public static IReadOnlyList<string> AllowedHosts { get; } = ["huggingface.co"];

    /// <summary>V-9.5 item 6: network errors are retried three times, after 2 s, 4 s and 8 s.</summary>
    public static IReadOnlyList<TimeSpan> RetryDelays { get; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    /// <summary>The downloader the models use: SK-3's, with Hugging Face on its list and the back-off above.</summary>
    public static Downloader CreateDownloader(string partFolder, IEnumerable<string>? allowedHosts = null, HttpMessageHandler? handler = null) =>
        new(partFolder, new DownloadPolicy(allowedHosts ?? AllowedHosts), new DownloadOptions { RetryDelays = RetryDelays }, handler);

    /// <param name="urlFor">The address of a file: the manifest's pinned one unless a test points it at the fake server.</param>
    public ModelDownloader(ModelStore store, Downloader downloader, Func<ModelFile, Uri>? urlFor = null)
    {
        this.store = store;
        this.downloader = downloader;
        this.urlFor = urlFor ?? (f => new Uri(f.Url));
    }

    /// <summary>Starts a download, or returns the one already running for this file. Several files may download at once (V-9.5 item 5).</summary>
    public ModelDownload Start(ModelFile file)
    {
        lock (gate)
        {
            if (active.TryGetValue(file.Id, out var running)) return running;
            var request = new DownloadRequest(urlFor(file), store.PathFor(file), file.Bytes, file.Sha256, "huggingface.co");
            var job = downloader.Start(request);
            ModelDownload? download = null;
            download = new ModelDownload(job, () => Finish(file, job, () => download!));
            active[file.Id] = download;
            store.SetDownloading(file, () => download!.Fraction);
            return download;
        }
    }

    public ModelDownload? Active(ModelFile file)
    {
        lock (gate) return active.GetValueOrDefault(file.Id);
    }

    private async Task<ModelDownloadResult> Finish(ModelFile file, DownloadJob job, Func<ModelDownload> self)
    {
        await Task.Yield(); // never run on inside Start (an already ended job would otherwise continue here at once, before Start's bookkeeping)
        var outcome = await job.Completion;
        try
        {
            if (outcome.Succeeded) store.RecordInstalled(file);
        }
        finally
        {
            // Under the gate: Start marks the row Downloading under it, so a download that ended at once cannot be marked after its end
            // (ED-NEW-f-vt-7 (8)); and whatever the store does, the row never stays Downloading.
            lock (gate)
            {
                store.SetDownloading(file, null);
                active.Remove(file.Id);
            }
        }
        return outcome.Status switch
        {
            DownloadStatus.Succeeded => new ModelDownloadResult(ModelDownloadStatus.Installed, null),
            DownloadStatus.Cancelled => new ModelDownloadResult(ModelDownloadStatus.Cancelled, null),
            _ when outcome.Failure?.Kind == DownloadFailureKind.Mismatch => new ModelDownloadResult(ModelDownloadStatus.Corrupt, outcome.Failure), // V-9.5 item 3
            _ => new ModelDownloadResult(ModelDownloadStatus.Failed, outcome.Failure),
        };
    }
}
