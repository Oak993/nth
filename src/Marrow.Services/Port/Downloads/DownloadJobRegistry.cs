// PORT-ONLY: web surfaces W5 (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.10 "Background tasks ... downloads (DownloadJob)";
// ED-NEW-web-w5-1). The downloader of the app starts many jobs from many places (skills, tools, voice models); the Background
// tasks pane has to list them all, so each job is noted here when it starts. The registry only watches: it never pauses,
// resumes or cancels a job on its own, and it holds no job longer than the pane would show it.

namespace Marrow.Services.Port.Downloads;

/// <summary>A download job as the registry noted it.</summary>
public sealed class DownloadJobEntry(DownloadJob job, string title, DateTimeOffset startedAt)
{
    /// <summary>The entry's identity for the life of the process (the job has none of its own).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public DownloadJob Job { get; } = job;

    /// <summary>The file being fetched ("Qwen3-8B-Q4.gguf"), or the first of a set and how many more.</summary>
    public string Title { get; } = title;

    public DateTimeOffset StartedAt { get; } = startedAt;

    private readonly Lock gate = new();
    private DateTimeOffset? finishedAt;

    /// <summary>When the job ended, once it has. Set from the thread the job ended on, read on the main actor: a 24-byte value, so under a lock.</summary>
    public DateTimeOffset? FinishedAt
    {
        get { lock (gate) return finishedAt; }
        internal set { lock (gate) finishedAt = value; }
    }
}

/// <summary>Every download job the app started lately, running or ended less than <see cref="Keep"/> ago.</summary>
public sealed class DownloadJobRegistry
{
    /// <summary>How long an ended job stays noted (the pane shows it for ten minutes).</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(11);

    private const int Cap = 64;

    /// <summary>The app's registry, which <see cref="Downloader"/> writes to.</summary>
    public static DownloadJobRegistry Shared { get; } = new();

    private readonly Lock gate = new();
    private readonly List<DownloadJobEntry> entries = [];

    /// <summary>Notes a job that has just been made. Safe from any thread.</summary>
    public DownloadJobEntry Track(DownloadJob job, IReadOnlyList<DownloadRequest> files, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var entry = new DownloadJobEntry(job, TitleOf(files), now);
        lock (gate)
        {
            entries.Add(entry);
            Trim(now);
        }
        _ = job.Completion.ContinueWith(_ =>
        {
            lock (gate) entry.FinishedAt = clock.GetUtcNow();
        }, TaskScheduler.Default);
        return entry;
    }

    /// <summary>The noted jobs, oldest first, ended ones past <see cref="Keep"/> dropped.</summary>
    public IReadOnlyList<DownloadJobEntry> Snapshot(DateTimeOffset now)
    {
        lock (gate)
        {
            Trim(now);
            return entries.ToArray();
        }
    }

    private void Trim(DateTimeOffset now)
    {
        entries.RemoveAll(entry => entry.FinishedAt is { } finished && now - finished >= Keep);
        while (entries.Count > Cap)
        {
            var oldestEnded = entries.FindIndex(entry => entry.FinishedAt is not null);
            entries.RemoveAt(oldestEnded >= 0 ? oldestEnded : 0);
        }
    }

    internal static string TitleOf(IReadOnlyList<DownloadRequest> files)
    {
        if (files.Count == 0) return "Download";
        var first = Path.GetFileName(files[0].DestinationPath);
        if (string.IsNullOrEmpty(first)) first = files[0].SourceName ?? files[0].Url.Host;
        return files.Count == 1 ? first : first + " and " + (files.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more";
    }
}
