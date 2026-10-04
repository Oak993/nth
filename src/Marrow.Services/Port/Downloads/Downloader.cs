// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3 = DL-3 of skills-ONDEMAND-PLAN.md section 7):
// the one downloader of the app (sections 3.2, 3.3, 3.9). Skills, tools and the voice models (VT-7) all fetch through it.
namespace Marrow.Services.Port.Downloads;

public sealed class Downloader
{
    /// <param name="partFolder">Where partial files live: <c>&lt;first 8 hex digits of the pinned hash&gt;.part</c> (section 3.1).</param>
    /// <param name="policy">The hosts a first url may name.</param>
    /// <param name="options">Timeouts, retries, free-space probe; the defaults otherwise.</param>
    /// <param name="handler">The transport; <see cref="DownloaderSeams.Handler"/> when null.</param>
    public Downloader(string partFolder, DownloadPolicy policy, DownloadOptions? options = null, HttpMessageHandler? handler = null) =>
        throw new NotImplementedException();

    /// <summary>Starts one file.</summary>
    public DownloadJob Start(DownloadRequest file, Action<DownloadProgress>? progress = null) => throw new NotImplementedException();

    /// <summary>
    /// Starts a set of files, <see cref="DownloadOptions.MaxInFlight"/> at a time. One file that fails fails the set: the files still in
    /// flight stop (their partial files are kept), none further starts, and the files already finished stay where they are.
    /// </summary>
    public DownloadJob StartSet(IReadOnlyList<DownloadRequest> files, Action<DownloadProgress>? progress = null) => throw new NotImplementedException();

    /// <summary>Deletes partial files older than <paramref name="maxAge"/> (7 days when null) from the part folder (section 3.2); returns how many.</summary>
    public int DeleteStalePartials(TimeSpan? maxAge = null) => throw new NotImplementedException();
}
