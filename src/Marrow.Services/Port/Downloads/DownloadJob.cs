// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): one running download or set.
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace Marrow.Services.Port.Downloads;

/// <summary>
/// A download in progress: its progress, pause, resume and cancel, and the task that ends with its outcome. Pause and Cancel both stop the
/// transfer at once and keep the partial files; a paused job waits for <see cref="Resume"/> (or <see cref="Cancel"/>) and holds no connection.
/// Thread-agnostic: events come from whatever thread the transfer is on. Every wait is on a token or on the downloader's clock.
/// </summary>
public sealed partial class DownloadJob
{
    private readonly HttpClient client;
    private readonly string partFolder;
    private readonly DownloadPolicy policy;
    private readonly DownloadOptions options;
    private readonly TimeProvider clock;
    private readonly IReadOnlyList<DownloadRequest> files;
    private readonly Action<DownloadProgress>? callback;
    private readonly Lock gate = new();
    private readonly Lock emitGate = new();
    private readonly CancellationTokenSource lifetime = new(); // the user's Cancel
    private readonly CancellationTokenSource failed = new();   // the set's first failure
    private readonly TaskCompletionSource<DownloadOutcome> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long[] received;
    private readonly DownloadFileResult?[] finished;
    private readonly long totalBytes;

    private DownloadJobState state = DownloadJobState.Running;
    private CancellationTokenSource pause = new();
    private TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DownloadFailure? firstFailure;
    private int filesDone;
    private int attempts;
    private int waitingToRetry;
    private long published;
    private long lastEmitStamp;
    private bool emittedOnce;
    private ITimer? trailing;

    internal DownloadJob(HttpClient client, string partFolder, DownloadPolicy policy, DownloadOptions options, TimeProvider clock,
        IReadOnlyList<DownloadRequest> files, Action<DownloadProgress>? progress)
    {
        this.client = client;
        this.partFolder = partFolder;
        this.policy = policy;
        this.options = options;
        this.clock = clock;
        this.files = files;
        callback = progress;
        received = new long[files.Count];
        finished = new DownloadFileResult?[files.Count];
        totalBytes = files.Sum(f => f.Size);
    }

    public DownloadJobState State
    {
        get { lock (gate) return state; }
    }

    /// <summary>The newest numbers (not throttled): see <see cref="BytesReceived"/> for when they are updated.</summary>
    public DownloadProgress Progress
    {
        get { lock (gate) return new DownloadProgress(Interlocked.Read(ref published), totalBytes, filesDone, files.Count); }
    }

    /// <summary>
    /// Bytes taken in so far, live (not throttled), counting what resumed partials held. It is updated after the progress report for the same
    /// bytes has been decided, so a reader that sees it has also seen that report.
    /// </summary>
    public long BytesReceived => Interlocked.Read(ref published);

    /// <summary>Requests (attempts, redirects not counted) started so far, over all files.</summary>
    public int AttemptsStarted => Volatile.Read(ref attempts);

    /// <summary>True while an automatic retry waits out its delay (<see cref="DownloadOptions.RetryDelays"/>).</summary>
    public bool IsWaitingToRetry => Volatile.Read(ref waitingToRetry) > 0;

    /// <summary>Completes with the outcome; never faults.</summary>
    public Task<DownloadOutcome> Completion => completion.Task;

    /// <summary>Raised at most once per <see cref="DownloadOptions.ProgressInterval"/>, with the newest numbers, and once more at the end.</summary>
    public event Action<DownloadProgress>? ProgressChanged;

    /// <summary>Stops the transfer, keeps the partial files and waits for <see cref="Resume"/>. No effect unless the job is running.</summary>
    public void Pause()
    {
        CancellationTokenSource toCancel;
        lock (gate)
        {
            if (state != DownloadJobState.Running) return;
            state = DownloadJobState.Paused;
            resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            toCancel = pause;
        }
        toCancel.Cancel(); // outside the lock: cancellation callbacks run inline
    }

    /// <summary>Continues a paused job: each file asks for the rest of its partial with Range. No effect unless the job is paused.</summary>
    public void Resume()
    {
        lock (gate)
        {
            if (state != DownloadJobState.Paused) return;
            state = DownloadJobState.Running;
            pause = new CancellationTokenSource();
            resumed.TrySetResult();
        }
    }

    /// <summary>Stops the transfer and ends the job as Cancelled; the partial files are kept (section 3.2). No effect once it has ended.</summary>
    public void Cancel()
    {
        lock (gate)
        {
            if (state is DownloadJobState.Succeeded or DownloadJobState.Failed or DownloadJobState.Cancelled) return;
        }
        lifetime.Cancel();
    }

    internal void Begin() => _ = Task.Run(RunAsync);

    // ---- the job -------------------------------------------------------------------------------------------------------------------------

    private async Task RunAsync()
    {
        DownloadOutcome outcome;
        try
        {
            outcome = await RunCoreAsync();
        }
        catch (Exception error)
        {
            // Nothing above throws; if something unforeseen does, the job still ends, with a failure, so no waiter hangs.
            Downloader.Log.Error("download job failed unexpectedly: " + error.GetType().Name);
            outcome = new DownloadOutcome(DownloadStatus.Failed, FinishedFiles(),
                new DownloadFailure(DownloadFailureKind.DiskError, DownloadMessages.DiskError));
        }
        lock (gate)
        {
            state = outcome.Status switch
            {
                DownloadStatus.Succeeded => DownloadJobState.Succeeded,
                DownloadStatus.Cancelled => DownloadJobState.Cancelled,
                _ => DownloadJobState.Failed,
            };
            trailing?.Dispose();
            trailing = null;
        }
        Emit(final: true);
        completion.TrySetResult(outcome);
    }

    private async Task<DownloadOutcome> RunCoreAsync()
    {
        // 1. Policy first: nothing is created and nothing connects for a url that is refused (section 3.2).
        foreach (var file in files)
            if (Refusal(file) is { } refusal) return new DownloadOutcome(DownloadStatus.Failed, [], refusal);
        // 2. Room: the download plus the unpacked size plus 10% must fit, checked before any connection (section 3.2).
        if (CheckSpace() is { } noRoom) return new DownloadOutcome(DownloadStatus.Failed, [], noRoom);
        if (files.Count == 0) return new DownloadOutcome(DownloadStatus.Succeeded, [], null);
        Directory.CreateDirectory(partFolder);

        var next = -1;
        var workers = Enumerable.Range(0, Math.Max(1, Math.Min(options.MaxInFlight, files.Count))).Select(_ => Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested && !failed.IsCancellationRequested)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= files.Count) return;
                await FetchFileAsync(index);
            }
        })).ToArray();
        await Task.WhenAll(workers);

        if (lifetime.IsCancellationRequested) return new DownloadOutcome(DownloadStatus.Cancelled, FinishedFiles(), null);
        if (firstFailure is { } failure) return new DownloadOutcome(DownloadStatus.Failed, FinishedFiles(), failure);
        return new DownloadOutcome(DownloadStatus.Succeeded, FinishedFiles(), null);
    }

    private List<DownloadFileResult> FinishedFiles() => [.. finished.OfType<DownloadFileResult>()];

    private DownloadFailure? Refusal(DownloadRequest file)
    {
        if (!file.Url.IsAbsoluteUri || file.Url.Scheme != Uri.UriSchemeHttps)
            return Refused(file, DownloadCause.NotHttps, "nth only downloads over https.");
        if (!policy.AllowsFirstUrl(file.Url))
            return Refused(file, DownloadCause.HostNotAllowed, $"nth won't download from {file.Url.Host}.");
        return null;
    }

    private static DownloadFailure Refused(DownloadRequest file, DownloadCause cause, string message) =>
        new(DownloadFailureKind.Refused, message, cause, DestinationPath: file.DestinationPath);

    private DownloadFailure? CheckSpace()
    {
        decimal need = 0;
        foreach (var file in files)
        {
            var part = PartPath(file);
            long have = 0;
            try
            {
                if (File.Exists(part)) have = Math.Min(new FileInfo(part).Length, file.Size);
            }
            catch (IOException)
            {
                have = 0;
            }
            need += (file.Size - have) + file.UnpackedBytes;
        }
        var required = (long)decimal.Ceiling(need * (1m + (decimal)options.SpaceMargin));
        long free;
        try
        {
            free = options.FreeSpace(partFolder);
        }
        catch (Exception)
        {
            return null; // unknown room is not a reason to refuse
        }
        return free >= required
            ? null
            : new DownloadFailure(DownloadFailureKind.DiskFull, DownloadMessages.DiskFull(required, DownloadMessages.DriveLabel(partFolder)));
    }

    private string PartPath(DownloadRequest file) => Path.Combine(partFolder, file.Sha256[..8].ToLowerInvariant() + ".part");

    // ---- one file, with pause, retries and the end of the set ------------------------------------------------------------------------------

    private async Task FetchFileAsync(int index)
    {
        var file = files[index];
        var retry = 0;
        var state = new FileState();
        while (true)
        {
            using var attemptLink = LinkedAttempt();
            try
            {
                DownloadFileResult result;
                // A file with the same pinned hash shares the partial: one transfer writes it at a time (ED-304 (15)). The turn is held per
                // attempt, not across a pause or a retry wait.
                var part = PartPath(file);
                var turn = await PartFileTurns.Take(part, attemptLink.Token);
                try
                {
                    result = await AttemptAsync(index, file, state, attemptLink.Token);
                }
                finally
                {
                    PartFileTurns.Give(part, turn);
                }
                lock (gate)
                {
                    finished[index] = result;
                    filesDone += 1;
                }
                ReportProgress();
                return;
            }
            catch (DownloadFailureException failure) when (failure.Transient && retry < options.RetryDelays.Count)
            {
                var delay = options.RetryDelays[retry++];
                Interlocked.Increment(ref waitingToRetry);
                try
                {
                    await Task.Delay(delay, clock, attemptLink.Token);
                }
                catch (OperationCanceledException)
                {
                    // paused or stopped during the wait: handled below like any interruption
                }
                finally
                {
                    Interlocked.Decrement(ref waitingToRetry);
                }
                if (!await AfterInterruptionAsync()) return;
            }
            catch (DownloadFailureException failure)
            {
                RecordFailure(failure.Failure with { DestinationPath = file.DestinationPath });
                return;
            }
            catch (OperationCanceledException)
            {
                if (!await AfterInterruptionAsync()) return;
            }
        }
    }

    /// <summary>What survives from one request chain to the next of one file.</summary>
    private sealed class FileState
    {
        /// <summary>The one silent restart after a resumed partial ended in the wrong hash (section 3.3) has been spent.</summary>
        internal bool RestartUsed;
    }

    private void RecordFailure(DownloadFailure failure)
    {
        lock (gate)
        {
            if (firstFailure is not null) return;
            firstFailure = failure;
        }
        failed.Cancel(); // the set stops: files in flight are interrupted, none further starts
    }

    private CancellationTokenSource LinkedAttempt()
    {
        CancellationToken pauseToken;
        lock (gate) pauseToken = pause.Token;
        return CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, failed.Token, pauseToken);
    }

    /// <summary>After an interruption: true when the file should try again (it was a pause that ended), false when the job is over for it.</summary>
    private async Task<bool> AfterInterruptionAsync()
    {
        if (lifetime.IsCancellationRequested || failed.IsCancellationRequested) return false;
        Task wait;
        lock (gate)
        {
            if (state != DownloadJobState.Paused) return true; // the pause was already resumed
            wait = resumed.Task;
        }
        try
        {
            await wait.WaitAsync(lifetime.Token).WaitAsync(failed.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        return true;
    }
}
