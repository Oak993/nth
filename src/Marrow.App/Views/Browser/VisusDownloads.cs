using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W8b, Visus's downloads list (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.4 "Downloads appear in
// nth's existing downloads list (DownloadJob, DownloadsPopover.cs:176) with Show in folder", "Files are never opened
// automatically"; 3.10 the Background tasks pane; the canvas's toolbar Downloads button (data-act="dls"); ED-NEW-web-w8b-2).
// No Swift counterpart. A saved file lands in the Downloads folder, so the composer's recent-downloads list (DownloadsPopover,
// which reads that folder) shows it once it is there; this list is the transfers of this run: progress, pause, resume, cancel
// and Show in folder, in the toolbar's Downloads popover and as Background tasks jobs. Nothing here opens a file.

namespace Marrow.App.Views.Browser;

/// <summary>Where a Visus download is.</summary>
public enum VisusDownloadState
{
    Running,
    Paused,
    Done,
    Failed,
    Cancelled,
}

/// <summary>One file a Visus tab saved (or is saving).</summary>
public sealed class VisusDownload
{
    private readonly IWebDownloadOperation operation;

    internal VisusDownload(string fileName, string source, VisusTabKind tabKind, DateTimeOffset startedAt, IWebDownloadOperation operation, TimeProvider time)
    {
        Id = Guid.NewGuid().ToString("N");
        FileName = fileName;
        Source = source;
        TabKind = tabKind;
        StartedAt = startedAt;
        this.operation = operation;
        Read(time);
        operation.Changed += (_, _) =>
        {
            var before = State;
            Read(time);
            Changed?.Invoke(this);
            if (before != State && State is VisusDownloadState.Done) Finished?.Invoke(this);
        };
    }

    public string Id { get; }

    /// <summary>The name it is saved under.</summary>
    public string FileName { get; }

    /// <summary>The full path of the file (where Show in folder points).</summary>
    public string Path => operation.ResultFilePath;

    /// <summary>The site it came from, as the trust strip names it.</summary>
    public string Source { get; }

    /// <summary>The kind of tab that saved it (a private tab's downloads stay out of Background tasks).</summary>
    public VisusTabKind TabKind { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public VisusDownloadState State { get; private set; }

    public long BytesReceived { get; private set; }

    public long? TotalBytes { get; private set; }

    /// <summary>The engine's reason a transfer stopped (null while it runs or after it finished).</summary>
    public string? FailureReason { get; private set; }

    /// <summary>A failed transfer the engine can pick up again.</summary>
    public bool CanResume { get; private set; }

    /// <summary>0 to 1 when the size is known; null while it is not.</summary>
    public double? Progress => State == VisusDownloadState.Done ? 1 : TotalBytes is > 0 and var total ? Math.Clamp((double)BytesReceived / total, 0, 1) : null;

    public bool IsLive => State is VisusDownloadState.Running or VisusDownloadState.Paused;

    /// <summary>The row's second line: "3.1 MB of 84 MB", "Paused · 3.1 MB of 84 MB", "12 KB · from localhost:5173".</summary>
    public string Status => State switch
    {
        VisusDownloadState.Running => Amount(),
        VisusDownloadState.Paused => UiText.Format("visus.downloads.status_paused", Amount()),
        VisusDownloadState.Done => UiText.Format("visus.downloads.status_done", DownloadGate.SizeText(TotalBytes ?? BytesReceived), Source),
        VisusDownloadState.Cancelled => UiText.Get("visus.downloads.status_cancelled"),
        _ => CanResume ? UiText.Get("visus.downloads.status_stopped") : UiText.Get("visus.downloads.status_failed"),
    };

    /// <summary>Raised on the UI thread whenever the transfer moved.</summary>
    public event Action<VisusDownload>? Changed;

    /// <summary>Raised once when the file is complete.</summary>
    public event Action<VisusDownload>? Finished;

    public void Pause()
    {
        if (State == VisusDownloadState.Running) operation.Pause();
    }

    public void Resume()
    {
        if (State == VisusDownloadState.Paused || State == VisusDownloadState.Failed && CanResume) operation.Resume();
    }

    public void Cancel()
    {
        if (IsLive || State == VisusDownloadState.Failed && CanResume) operation.Cancel();
    }

    private string Amount() => TotalBytes is > 0 and var total
        ? UiText.Format("visus.downloads.amount_of", DownloadGate.SizeText(BytesReceived), DownloadGate.SizeText(total))
        : DownloadGate.SizeText(BytesReceived);

    private void Read(TimeProvider time)
    {
        BytesReceived = operation.BytesReceived;
        TotalBytes = operation.TotalBytes;
        CanResume = operation.CanResume;
        var state = operation.State switch
        {
            WebDownloadState.InProgress => VisusDownloadState.Running,
            WebDownloadState.Paused => VisusDownloadState.Paused,
            WebDownloadState.Completed => VisusDownloadState.Done,
            _ => string.Equals(operation.InterruptReason, "UserCanceled", StringComparison.OrdinalIgnoreCase) ? VisusDownloadState.Cancelled : VisusDownloadState.Failed,
        };
        FailureReason = state is VisusDownloadState.Failed ? operation.InterruptReason : null;
        if (state != State || FinishedAt is null)
        {
            var live = state is VisusDownloadState.Running or VisusDownloadState.Paused;
            FinishedAt = live ? null : FinishedAt ?? time.GetUtcNow();
        }
        State = state;
    }
}

/// <summary>Visus's downloads of this run, newest first.</summary>
public sealed class VisusDownloads
{
    /// <summary>How many finished downloads the list keeps (live ones always stay).</summary>
    public const int Limit = 30;

    private readonly List<VisusDownload> items = [];

    public IReadOnlyList<VisusDownload> Items => items;

    /// <summary>A download was added, moved, finished or cleared.</summary>
    public event Action? Changed;

    /// <summary>A download finished (the tab that saved it says "Saved to Downloads · name").</summary>
    public event Action<VisusDownload>? Finished;

    public VisusDownload? Find(string id) => items.FirstOrDefault(item => item.Id == id);

    /// <summary>Running or paused transfers: the toolbar button shows their progress.</summary>
    public int LiveCount => items.Count(item => item.IsLive);

    /// <summary>The overall progress of the live transfers with a known size (null when none has one).</summary>
    public double? LiveProgress
    {
        get
        {
            var sized = items.Where(item => item.IsLive && item.TotalBytes is > 0).ToList();
            if (sized.Count == 0) return null;
            var total = sized.Sum(item => item.TotalBytes!.Value);
            return Math.Clamp((double)sized.Sum(item => item.BytesReceived) / total, 0, 1);
        }
    }

    internal VisusDownload Add(string fileName, string source, VisusTabKind tabKind, IWebDownloadOperation operation, TimeProvider time)
    {
        var download = new VisusDownload(fileName, source, tabKind, time.GetUtcNow(), operation, time);
        download.Changed += _ => Changed?.Invoke();
        download.Finished += item => Finished?.Invoke(item);
        items.Insert(0, download);
        Trim();
        Changed?.Invoke();
        if (download.State == VisusDownloadState.Done) Finished?.Invoke(download);
        return download;
    }

    /// <summary>Takes a finished download off the list (the file stays where it is).</summary>
    public bool Dismiss(string id)
    {
        var item = Find(id);
        if (item is null || item.IsLive) return false;
        items.Remove(item);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Clears every finished download from the list.</summary>
    public void ClearFinished()
    {
        if (items.RemoveAll(item => !item.IsLive) > 0) Changed?.Invoke();
    }

    private void Trim()
    {
        var finished = items.Where(item => !item.IsLive).ToList();
        foreach (var old in finished.Skip(Limit)) items.Remove(old);
    }
}

/// <summary>3.10: Visus's downloads as Background tasks jobs (private tabs' downloads stay out: the model cannot see them).</summary>
public sealed class VisusDownloadJobSource(Func<VisusDownloads> downloads) : IBackgroundJobSource
{
    public const string Prefix = "visus-download:";

    public string Name => "Visus downloads";

    public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now)
    {
        var jobs = new List<BackgroundJob>();
        foreach (var item in downloads().Items)
        {
            if (item.TabKind == VisusTabKind.Private) continue;
            jobs.Add(new BackgroundJob
            {
                Id = Prefix + item.Id,
                Kind = BackgroundJobKind.Download,
                Title = item.FileName,
                KindLabel = UiText.Get("visus.downloads.job_kind"),
                Subject = item.State == VisusDownloadState.Done
                    ? DownloadGate.SizeText(item.TotalBytes ?? item.BytesReceived)
                    : BackgroundJobText.BytesOf(item.BytesReceived, item.TotalBytes ?? 0),
                State = item.State switch
                {
                    VisusDownloadState.Running => BackgroundJobState.Running,
                    VisusDownloadState.Paused => BackgroundJobState.Paused,
                    VisusDownloadState.Done => BackgroundJobState.Done,
                    VisusDownloadState.Failed => BackgroundJobState.Failed,
                    _ => BackgroundJobState.Stopped,
                },
                StartedAt = item.StartedAt,
                FinishedAt = item.IsLive ? null : item.FinishedAt,
                Progress = item.Progress ?? 0,
                Activity = UiText.Format("visus.downloads.job_from", item.Source),
                CanPause = item.IsLive,
                CanStop = item.IsLive,
            });
        }
        return jobs;
    }

    public bool Stop(string jobId)
    {
        if (Find(jobId) is not { IsLive: true } item) return false;
        item.Cancel();
        return true;
    }

    public bool Pause(string jobId)
    {
        if (Find(jobId) is not { State: VisusDownloadState.Running } item) return false;
        item.Pause();
        return true;
    }

    public bool Resume(string jobId)
    {
        if (Find(jobId) is not { State: VisusDownloadState.Paused } item) return false;
        item.Resume();
        return true;
    }

    public void Dismiss(string jobId)
    {
        if (jobId.StartsWith(Prefix, StringComparison.Ordinal)) downloads().Dismiss(jobId[Prefix.Length..]);
    }

    private VisusDownload? Find(string jobId) =>
        jobId.StartsWith(Prefix, StringComparison.Ordinal) ? downloads().Find(jobId[Prefix.Length..]) : null;
}
