// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3 = DL-3 of skills-ONDEMAND-PLAN.md section 7):
// the one downloader of the app (sections 3.2, 3.3, 3.9). Skills, tools and the voice models (VT-7) all fetch through it.
using Marrow.Core.Port;

namespace Marrow.Services.Port.Downloads;

public sealed class Downloader
{
    internal static readonly Logger Log = new("downloads", "fetch");

    private readonly string partFolder;
    private readonly DownloadPolicy policy;
    private readonly DownloadOptions options;
    private readonly HttpClient client;
    private readonly TimeProvider clock;

    /// <param name="partFolder">Where partial files live: <c>&lt;first 8 hex digits of the pinned hash&gt;.part</c> (section 3.1).</param>
    /// <param name="policy">The hosts a first url may name.</param>
    /// <param name="options">Timeouts, retries, free-space probe; the defaults otherwise.</param>
    /// <param name="handler">The transport; <see cref="DownloaderSeams.Handler"/> when null.</param>
    public Downloader(string partFolder, DownloadPolicy policy, DownloadOptions? options = null, HttpMessageHandler? handler = null)
    {
        this.partFolder = partFolder;
        this.policy = policy;
        this.options = options ?? new DownloadOptions();
        // R-NET-1: no HttpClient timeout of its own (a timeout there reads as a cancellation); the downloader's timeouts are its own, on the clock.
        client = new HttpClient(handler ?? DownloaderSeams.Handler.Current, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        clock = SwiftClock.Provider.Current;
    }

    /// <summary>Starts one file.</summary>
    public DownloadJob Start(DownloadRequest file, Action<DownloadProgress>? progress = null) => StartSet([file], progress);

    /// <summary>
    /// Starts a set of files, <see cref="DownloadOptions.MaxInFlight"/> at a time. One file that fails fails the set: the files still in
    /// flight stop (their partial files are kept), none further starts, and the files already finished stay where they are.
    /// </summary>
    public DownloadJob StartSet(IReadOnlyList<DownloadRequest> files, Action<DownloadProgress>? progress = null)
    {
        var job = new DownloadJob(client, partFolder, policy, options, clock, files, progress);
        DownloadJobRegistry.Shared.Track(job, files, clock); // PORT-ONLY (web W5): the Background tasks pane lists every download
        job.Begin();
        return job;
    }

    /// <summary>Deletes partial files older than <paramref name="maxAge"/> (7 days when null) from the part folder (section 3.2); returns how many.</summary>
    public int DeleteStalePartials(TimeSpan? maxAge = null)
    {
        if (!Directory.Exists(partFolder)) return 0;
        var limit = maxAge ?? TimeSpan.FromDays(7);
        var now = clock.GetUtcNow().UtcDateTime;
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(partFolder, "*.part"))
        {
            if (now - File.GetLastWriteTimeUtc(path) <= limit) continue;
            try
            {
                File.Delete(path);
                deleted += 1;
            }
            catch (IOException)
            {
                // In use by a download that started meanwhile: it is not stale.
            }
        }
        return deleted;
    }
}
