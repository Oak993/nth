// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): one running download or set.
namespace Marrow.Services.Port.Downloads;

/// <summary>
/// A download in progress: its progress, pause, resume and cancel, and the task that ends with its outcome. Pause and Cancel both stop the
/// transfer at once and keep the partial files; a paused job waits for <see cref="Resume"/> (or <see cref="Cancel"/>) and holds no connection.
/// Thread-agnostic: events come from whatever thread the transfer is on.
/// </summary>
public sealed class DownloadJob
{
    internal DownloadJob() { }

    public DownloadJobState State => throw new NotImplementedException();

    public DownloadProgress Progress => throw new NotImplementedException();

    /// <summary>
    /// Bytes taken in so far, live (not throttled), counting what resumed partials held. It is updated after the progress report for the same
    /// bytes has been decided, so a reader that sees it has also seen that report.
    /// </summary>
    public long BytesReceived => throw new NotImplementedException();

    /// <summary>Requests (attempts, redirects not counted) started so far, over all files.</summary>
    public int AttemptsStarted => throw new NotImplementedException();

    /// <summary>True while an automatic retry waits out its delay (<see cref="DownloadOptions.RetryDelays"/>).</summary>
    public bool IsWaitingToRetry => throw new NotImplementedException();

    /// <summary>Completes with the outcome; never faults.</summary>
    public Task<DownloadOutcome> Completion => throw new NotImplementedException();

    /// <summary>Raised at most once per <see cref="DownloadOptions.ProgressInterval"/>, with the newest numbers, and once more at the end.</summary>
    public event Action<DownloadProgress>? ProgressChanged;

    public void Pause() => throw new NotImplementedException();

    public void Resume() => throw new NotImplementedException();

    public void Cancel() => throw new NotImplementedException();

    private void Unused() => ProgressChanged?.Invoke(default);
}
