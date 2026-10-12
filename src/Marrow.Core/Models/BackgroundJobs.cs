using Marrow.Core.Port;

// PORT-ONLY: web surfaces W5, the aggregate behind the Background tasks pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.10
// "New BackgroundJobs model ... aggregating Hydra heads, helpers and subagents, Tactus streams, browser model sessions,
// terminal sessions, downloads and workflows"; ED-NEW-web-w5-1). No Swift counterpart. Sources describe what they run as
// immutable BackgroundJob snapshots and carry out Stop, Pause and Resume; this class merges them, orders them, overlays the
// "stopping" state, hides what was cleared, lets a finished job go after ten minutes, and ticks four times a second while
// something looks at it. It touches no pixel and no AppModel, so the pane, the assistant's mission board and the tests all
// read the same jobs. Everything here runs on the main actor (the tick posts itself there); only Attach and its lease are
// callable from anywhere.

namespace Marrow.Core.Models;

/// <summary>Something that runs jobs and can say what they are. Implemented by the app for each of its six sources.</summary>
public interface IBackgroundJobSource
{
    /// <summary>The source's name, for logs and tests ("Hydra heads", "Downloads").</summary>
    string Name { get; }

    /// <summary>The source's jobs now. Called on the main actor, at most 4 times a second while the pane is open. Must be cheap and must not throw.</summary>
    IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now);

    /// <summary>Stops the job; true when the request was taken (the job may need a moment to end).</summary>
    bool Stop(string jobId);

    /// <summary>Pauses the job; true when taken. A source whose jobs cannot pause returns false.</summary>
    bool Pause(string jobId);

    /// <summary>Resumes a paused job; true when taken.</summary>
    bool Resume(string jobId);

    /// <summary>The person cleared a finished job from the list: a source that keeps its own list (the Hydra panel) lets it go too.</summary>
    void Dismiss(string jobId);
}

/// <summary>A published job's handles: what the owner of a job lets the pane do to it. Any can be null (the action is then refused).</summary>
public sealed record BackgroundJobControl(Func<bool>? Stop = null, Func<bool>? Pause = null, Func<bool>? Resume = null, Action? Dismiss = null);

/// <summary>
/// A source anyone can publish into: Tactus streams, Visus agent tabs, workflows, and the assistant's missions call
/// <see cref="Publish"/> as their job changes and <see cref="Retire"/> when it is forgotten. Thread-safe: publishers
/// run on whatever thread their work is on; the aggregate reads on the main actor.
/// </summary>
public sealed class PublishedJobSource(string name) : IBackgroundJobSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, (BackgroundJob Job, BackgroundJobControl? Control)> jobs = [];

    public string Name { get; } = name;

    /// <summary>Adds the job or replaces the one with the same id (a progress update is a new snapshot).</summary>
    public void Publish(BackgroundJob job, BackgroundJobControl? control = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (gate)
        {
            if (control is null && jobs.TryGetValue(job.Id, out var existing)) control = existing.Control;
            jobs[job.Id] = (job, control);
        }
    }

    /// <summary>Forgets the job (it vanishes from the list; to show it as ended, publish it as Done, Failed or Stopped instead).</summary>
    public bool Retire(string jobId)
    {
        lock (gate) return jobs.Remove(jobId);
    }

    public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now)
    {
        lock (gate) return jobs.Values.Select(entry => entry.Job).ToArray();
    }

    private BackgroundJobControl? ControlOf(string id)
    {
        lock (gate) return jobs.TryGetValue(id, out var entry) ? entry.Control : null;
    }

    public bool Stop(string jobId) => ControlOf(jobId)?.Stop?.Invoke() ?? false;

    public bool Pause(string jobId) => ControlOf(jobId)?.Pause?.Invoke() ?? false;

    public bool Resume(string jobId) => ControlOf(jobId)?.Resume?.Invoke() ?? false;

    public void Dismiss(string jobId)
    {
        var control = ControlOf(jobId);
        control?.Dismiss?.Invoke();
        // A finished published job the person cleared goes from the feed too, so it cannot come back on the next tick.
        lock (gate)
        {
            if (jobs.TryGetValue(jobId, out var entry) && entry.Job.IsFinished) jobs.Remove(jobId);
        }
    }
}

/// <summary>The ordering and filtering rules of the pane, pure so any list of jobs can be put through them.</summary>
public static class BackgroundJobOrder
{
    /// <summary>
    /// Live jobs first, the oldest start first (a new job joins the end, so nothing already shown moves down), then the finished
    /// ones, the most recently ended first. Ties break on the id, so the same jobs always give the same order.
    /// </summary>
    public static List<BackgroundJob> Sort(IEnumerable<BackgroundJob> jobs)
    {
        var list = jobs.ToList();
        list.Sort(Compare);
        return list;
    }

    public static int Compare(BackgroundJob a, BackgroundJob b)
    {
        if (a.IsFinished != b.IsFinished) return a.IsFinished ? 1 : -1;
        int order;
        if (a.IsFinished)
        {
            order = Nullable.Compare(b.FinishedAt, a.FinishedAt);
            if (order != 0) return order;
            order = b.StartedAt.CompareTo(a.StartedAt);
        }
        else order = a.StartedAt.CompareTo(b.StartedAt);
        return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
    }

    /// <summary>The jobs the pane lists for a thread: This thread keeps the thread's own and the app-wide ones; All threads keeps everything.</summary>
    public static IEnumerable<BackgroundJob> Filter(IEnumerable<BackgroundJob> jobs, Guid? thread, BackgroundJobScope scope) =>
        scope == BackgroundJobScope.AllThreads ? jobs : jobs.Where(job => job.ThreadId is null || job.ThreadId == thread);
}

/// <summary>The aggregate of every source's jobs (see the file header).</summary>
public sealed class BackgroundJobs
{
    /// <summary>How long a "stop" the source accepted may keep showing Stopping while the job still looks alive (a refused stop returns to Running).</summary>
    public static readonly TimeSpan StoppingGrace = TimeSpan.FromSeconds(15);

    private readonly List<IBackgroundJobSource> sources = [];
    private readonly Dictionary<string, IBackgroundJobSource> owners = [];
    private readonly Dictionary<string, DateTimeOffset> stopping = [];
    private readonly Dictionary<string, DateTimeOffset> firstSeenFinished = [];
    private readonly HashSet<string> dismissed = [];
    private readonly Lock leaseGate = new();
    private readonly TimeProvider clock;
    private readonly Action<Action> post;
    private List<BackgroundJob> jobs = [];
    private int leases;
    private ITimer? timer;
    private int tickQueued;
    private long version;
    private DateTimeOffset refreshedAt;

    public BackgroundJobs(TimeProvider? clock = null, Action<Action>? post = null)
    {
        this.clock = clock ?? SwiftClock.Provider.Current;
        this.post = post ?? MainActor.Post;
        refreshedAt = this.clock.GetUtcNow();
    }

    /// <summary>The ordered jobs of the last refresh: live ones, then finished ones within <see cref="BackgroundJobText.DoneLifetime"/>.</summary>
    public IReadOnlyList<BackgroundJob> Jobs => jobs;

    /// <summary>Rises by one whenever the list the pane draws changes (a job came, went, ended, or changed state or order), not when only live values change.</summary>
    public long Version => version;

    /// <summary>The clock time of the last refresh; cards compute their elapsed time from it so every number of one tick shares a moment.</summary>
    public DateTimeOffset RefreshedAt => refreshedAt;

    /// <summary>How many views hold a lease (the ticker runs while it is above zero).</summary>
    public int Leases
    {
        get { lock (leaseGate) return leases; }
    }

    /// <summary>Raised after a refresh that changed the list the pane draws (see <see cref="Version"/>).</summary>
    public event Action? Changed;

    /// <summary>Raised after every refresh: the live values (elapsed time, tokens, tool uses, activity, progress) are new.</summary>
    public event Action? Ticked;

    /// <summary>Adds a source (idempotent). Its jobs join the list at the next refresh.</summary>
    public void AddSource(IBackgroundJobSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!sources.Contains(source)) sources.Add(source);
    }

    public bool RemoveSource(IBackgroundJobSource source) => sources.Remove(source);

    public IReadOnlyList<IBackgroundJobSource> Sources => sources;

    /// <summary>The job with <paramref name="id"/> as of the last refresh.</summary>
    public BackgroundJob? Find(string id) => jobs.FirstOrDefault(job => job.Id == id);

    /// <summary>The jobs to list for <paramref name="thread"/> under <paramref name="scope"/>, in order.</summary>
    public List<BackgroundJob> For(Guid? thread, BackgroundJobScope scope) => BackgroundJobOrder.Filter(jobs, thread, scope).ToList();

    /// <summary>
    /// Holds the ticker open while a view looks at the list: the first lease starts the 4 Hz timer and refreshes at once, the
    /// last one disposed stops it. Dispose is idempotent.
    /// </summary>
    public IDisposable Attach()
    {
        var first = false;
        lock (leaseGate)
        {
            leases++;
            if (leases == 1)
            {
                first = true;
                timer = clock.CreateTimer(_ => QueueTick(), null, BackgroundJobText.TickInterval, BackgroundJobText.TickInterval);
            }
        }
        if (first) Refresh();
        return new Lease(this);
    }

    private void Detach()
    {
        ITimer? stopped = null;
        lock (leaseGate)
        {
            if (leases == 0) return;
            leases--;
            if (leases == 0)
            {
                stopped = timer;
                timer = null;
            }
        }
        stopped?.Dispose();
    }

    private sealed class Lease(BackgroundJobs owner) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Detach();
        }
    }

    // The timer fires on a pool thread; the refresh needs the main actor (the sources read the app's models). A tick that finds
    // the last one still queued is dropped, so a busy main thread never builds a backlog.
    private void QueueTick()
    {
        if (Interlocked.Exchange(ref tickQueued, 1) == 1) return;
        post(() =>
        {
            Volatile.Write(ref tickQueued, 0);
            if (Leases > 0) Refresh();
        });
    }

    /// <summary>Asks every source for its jobs now and publishes the merged list. The ticker calls it four times a second; tests and the pane's first draw call it directly.</summary>
    public void Refresh()
    {
        var now = clock.GetUtcNow();
        var fresh = new List<BackgroundJob>();
        var seen = new Dictionary<string, IBackgroundJobSource>();
        foreach (var source in sources.ToArray())
        {
            IReadOnlyList<BackgroundJob> snapshot;
            try { snapshot = source.Snapshot(now); }
            catch (Exception) { continue; } // one broken source never takes the others' jobs with it
            foreach (var job in snapshot)
            {
                if (job is null || string.IsNullOrEmpty(job.Id) || !seen.TryAdd(job.Id, source)) continue;
                fresh.Add(job);
            }
        }
        var shown = new List<BackgroundJob>(fresh.Count);
        foreach (var raw in fresh)
        {
            var job = raw;
            if (job.IsFinished)
            {
                if (dismissed.Contains(job.Id)) continue;
                var finished = job.FinishedAt;
                if (finished is null)
                {
                    if (!firstSeenFinished.TryGetValue(job.Id, out var first)) firstSeenFinished[job.Id] = first = now;
                    finished = first;
                    job = job with { FinishedAt = finished };
                }
                if (now - finished.Value >= BackgroundJobText.DoneLifetime) continue;
                stopping.Remove(job.Id);
            }
            else
            {
                firstSeenFinished.Remove(job.Id);
                dismissed.Remove(job.Id);
                if (stopping.TryGetValue(job.Id, out var asked))
                {
                    if (now - asked >= StoppingGrace) stopping.Remove(job.Id);
                    else job = job with { State = BackgroundJobState.Stopping, CanStop = false, CanPause = false };
                }
            }
            shown.Add(job);
        }
        // Forget the bookkeeping of jobs the sources no longer list.
        foreach (var id in stopping.Keys.Where(id => !seen.ContainsKey(id)).ToList()) stopping.Remove(id);
        foreach (var id in firstSeenFinished.Keys.Where(id => !seen.ContainsKey(id)).ToList()) firstSeenFinished.Remove(id);
        dismissed.RemoveWhere(id => !seen.ContainsKey(id));

        owners.Clear();
        foreach (var (id, source) in seen) owners[id] = source;

        var ordered = BackgroundJobOrder.Sort(shown);
        var changed = ordered.Count != jobs.Count;
        if (!changed)
            for (var i = 0; i < ordered.Count; i++)
                if (!ordered[i].SameShape(jobs[i])) { changed = true; break; }
        jobs = ordered;
        refreshedAt = now;
        if (changed) version++;
        if (changed) Changed?.Invoke();
        Ticked?.Invoke();
    }

    /// <summary>Asks the job's source to stop it. The job shows Stopping from now until its source reports it over (or 15 seconds pass).</summary>
    public bool Stop(string jobId)
    {
        if (!owners.TryGetValue(jobId, out var source) || Find(jobId) is not { IsLive: true, CanStop: true } job) return false;
        bool taken;
        try { taken = source.Stop(jobId); }
        catch (Exception) { taken = false; }
        if (!taken) return false;
        stopping[job.Id] = clock.GetUtcNow();
        Refresh();
        return true;
    }

    /// <summary>Asks the job's source to pause it.</summary>
    public bool Pause(string jobId)
    {
        if (!owners.TryGetValue(jobId, out var source) || Find(jobId) is not { State: BackgroundJobState.Running, CanPause: true }) return false;
        bool taken;
        try { taken = source.Pause(jobId); }
        catch (Exception) { taken = false; }
        if (taken) Refresh();
        return taken;
    }

    /// <summary>Asks the job's source to resume it.</summary>
    public bool Resume(string jobId)
    {
        if (!owners.TryGetValue(jobId, out var source) || Find(jobId) is not { State: BackgroundJobState.Paused or BackgroundJobState.Pausing }) return false;
        bool taken;
        try { taken = source.Resume(jobId); }
        catch (Exception) { taken = false; }
        if (taken) Refresh();
        return taken;
    }

    /// <summary>Clears the finished jobs the list shows for <paramref name="thread"/> under <paramref name="scope"/> ("Clear done"); returns how many.</summary>
    public int ClearDone(Guid? thread, BackgroundJobScope scope)
    {
        var cleared = BackgroundJobOrder.Filter(jobs, thread, scope).Where(job => job.IsFinished).ToList();
        foreach (var job in cleared)
        {
            dismissed.Add(job.Id);
            if (owners.TryGetValue(job.Id, out var source))
            {
                try { source.Dismiss(job.Id); }
                catch (Exception) { /* the card is gone either way */ }
            }
        }
        if (cleared.Count > 0) Refresh();
        return cleared.Count;
    }
}
