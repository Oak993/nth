// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-6). Several apps at once: one ordered stream of work per
// app, and the streams of different apps run side by side. Work for one app waits for the work before it (in arrival
// order); work for another app never waits for it. The keys with work in flight are the set of active apps that the pill
// and the session show. A stream that already holds MaxQueued calls refuses the next one (error busy) instead of growing.
namespace Marrow.Services.Port.PcControl;

/// <summary>Thrown when one app's stream is full.</summary>
public sealed class PcLaneFullException(string key) : Exception("too many Tactus actions are waiting for " + key)
{
    public string Key { get; } = key;
}

public sealed class PcLanes
{
    public const int DefaultMaxQueued = 8;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Lane> lanes = new(StringComparer.Ordinal);
    private long sequence;

    public PcLanes(int maxQueued = DefaultMaxQueued) => MaxQueued = Math.Max(1, maxQueued);

    public int MaxQueued { get; }

    /// <summary>Raised (outside the lock) whenever the set of active keys changes.</summary>
    public event Action<IReadOnlyList<string>>? ActiveChanged;

    /// <summary>The keys that have work running or waiting, in the order their streams started.</summary>
    public IReadOnlyList<string> Active
    {
        get
        {
            lock (gate) return lanes.Values.OrderBy(l => l.Started).Select(l => l.Key).ToList();
        }
    }

    /// <summary>Runs <paramref name="work"/> after the work already queued for <paramref name="key"/>.</summary>
    public Task<T> Run<T>(string key, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(work);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Lane lane;
        Task previous;
        var opened = false;
        lock (gate)
        {
            if (!lanes.TryGetValue(key, out lane!))
            {
                lane = new Lane(key, ++sequence);
                lanes[key] = lane;
                opened = true;
            }
            if (lane.Count >= MaxQueued) throw new PcLaneFullException(key);
            lane.Count++;
            previous = lane.Tail;
            lane.Tail = done.Task;
        }
        if (opened) Raise();
        return RunAfter(lane, previous, done, work, cancellationToken);
    }

    private async Task<T> RunAfter<T>(Lane lane, Task previous, TaskCompletionSource done, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        try
        {
            await previous.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await work(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The next caller waits on this one; when this one was cancelled while waiting, it still finishes only after
            // the work before it, so two calls for one app never overlap.
            if (previous.IsCompleted) done.TrySetResult();
            else _ = previous.ContinueWith(_ => done.TrySetResult(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            bool closed;
            lock (gate)
            {
                lane.Count--;
                closed = lane.Count == 0 && lanes.Remove(lane.Key);
            }
            if (closed) Raise();
        }
    }

    private void Raise() => ActiveChanged?.Invoke(Active);

    private sealed class Lane(string key, long started)
    {
        public string Key { get; } = key;
        public long Started { get; } = started;
        public Task Tail { get; set; } = Task.CompletedTask;
        public int Count { get; set; }
    }
}
