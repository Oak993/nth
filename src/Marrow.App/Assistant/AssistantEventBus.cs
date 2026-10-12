// PORT-ONLY: Dextra, nth's always-on assistant (DESIGN.md rev 5, 5.1; unit AS-2; ED-NEW-dextra-as2-6). No Swift twin.
// The read-only, in-process publisher of the live feed. A publish is one O(1) enqueue onto a bounded queue and runs no
// other code. Everything else (the privacy filter, the shield, the words of each line, the subscribers) runs on one
// background task, never on the thread that published. With nobody subscribed a publish is not even made.
using System.Threading.Channels;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>The feed bus: taps publish drafts, subscribers get batches of finished lines off the UI thread.</summary>
public sealed class AssistantEventBus : IDisposable
{
    /// <summary>The most lines handed to the subscribers at once.</summary>
    private const int BatchSize = 512;

    private readonly FeedQueue queue;
    private readonly TimeProvider clock;
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });
    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private readonly List<TaskCompletionSource> flushWaiters = [];
    private Subscription[] subscribers = [];
    private volatile FeedBrain brain = FeedBrain.Unknown;
    private Task? pump;
    private long sequence;
    private long faults;
    private bool disposed;

    public AssistantEventBus(TimeProvider? clock = null, int limit = FeedQueue.DefaultLimit)
    {
        this.clock = clock ?? TimeProvider.System;
        queue = new FeedQueue(limit);
    }

    /// <summary>The time the taps stamp their drafts with.</summary>
    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Whether anything listens. A tap checks this first and builds nothing when it is false.</summary>
    public bool HasSubscribers => Volatile.Read(ref subscribers).Length > 0;

    /// <summary>
    /// The brain's side of the privacy rules (5.4): its provider, whether it runs on this PC and the owner's two
    /// switches. It starts as <see cref="FeedBrain.Unknown"/>, which passes no chat's words on; the Assistant sets it
    /// when it has a brain and the owner's choices. Safe to set from any thread.
    /// </summary>
    public FeedBrain Brain
    {
        get => brain;
        set => brain = value ?? FeedBrain.Unknown;
    }

    /// <summary>Drafts dropped for being over the queue's limit (quiet ones first, never a needs-you or an error).</summary>
    public long DroppedCount => queue.DroppedCount;

    /// <summary>Subscriber calls that threw. A throwing subscriber never stops the bus or the other subscribers.</summary>
    public long FaultCount => Interlocked.Read(ref faults);

    /// <summary>Drafts waiting for the background task.</summary>
    public int Pending => queue.Count;

    /// <summary>
    /// Listens for batches of finished lines. The handler runs on the bus's own task, in order, never on the UI thread;
    /// it should return quickly (hand work on rather than do it). The first subscriber starts the task.
    /// </summary>
    public IDisposable Subscribe(Action<IReadOnlyList<FeedEvent>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, handler);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            subscribers = [.. subscribers, subscription];
            pump ??= Task.Run(() => PumpAsync(stop.Token));
        }
        return subscription;
    }

    /// <summary>
    /// Adds a draft. O(1): one enqueue and a nudge to the background task. Never throws, never blocks on a subscriber,
    /// never runs the privacy filter or builds a line. Callable from any thread.
    /// </summary>
    public void Publish(FeedDraft draft)
    {
        try
        {
            if (!HasSubscribers) return;
            queue.Enqueue(draft);
            wake.Writer.TryWrite(true);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref faults); // a tap must never break the app
        }
    }

    /// <summary>The owner hid a chat: drafts of it that are still waiting lose their words (5.4).</summary>
    public void Hide(Guid thread)
    {
        queue.Hide(thread);
    }

    /// <summary>
    /// Ends every coalescing window and completes when every draft published before the call has been handed to the
    /// subscribers (or dropped). For shutdown and for tests.
    /// </summary>
    public Task FlushAsync()
    {
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed || pump is null)
            {
                waiter.SetResult();
                return waiter.Task;
            }
            queue.ReleaseAll();
            flushWaiters.Add(waiter);
        }
        wake.Writer.TryWrite(true);
        return waiter.Task;
    }

    public void Dispose()
    {
        Task? running;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            running = pump;
            foreach (var listener in subscribers) listener.Retire();
            subscribers = [];
        }
        stop.Cancel();
        try { running?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        lock (gate)
        {
            foreach (var waiter in flushWaiters) waiter.TrySetResult();
            flushWaiters.Clear();
        }
        stop.Dispose();
    }

    private async Task PumpAsync(CancellationToken token)
    {
        var batch = new List<FeedDraft>(BatchSize);
        try
        {
            while (!token.IsCancellationRequested)
            {
                wake.Reader.TryRead(out _);
                var waiters = TakeWaiters();
                while (true)
                {
                    batch.Clear();
                    queue.TakeReady(clock.GetUtcNow(), batch, BatchSize);
                    if (batch.Count == 0) break;
                    Deliver(batch);
                }
                foreach (var waiter in waiters) waiter.TrySetResult();

                await WaitForWorkAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Sleeps until a draft arrives or the next coalescing window ends, whichever is first.
    private async Task WaitForWorkAsync(CancellationToken token)
    {
        var nextDue = queue.NextDue;
        var delay = TimeSpan.Zero;
        if (nextDue is { } due)
        {
            var now = clock.GetUtcNow(); // One sample: a deadline crossing cannot make the subtraction negative.
            delay = due > now ? due - now : TimeSpan.Zero;
        }
        // A draft with a far-future stamp must not exceed the timer's supported range and fault the only pump.
        var maxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        using var timeout = nextDue is not null
            ? new CancellationTokenSource(delay > maxDelay ? maxDelay : delay, clock)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            await wake.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
        }
    }

    private List<TaskCompletionSource> TakeWaiters()
    {
        lock (gate)
        {
            if (flushWaiters.Count == 0) return [];
            var taken = flushWaiters.ToList();
            flushWaiters.Clear();
            return taken;
        }
    }

    private void Deliver(List<FeedDraft> drafts)
    {
        var listeners = Volatile.Read(ref subscribers);
        if (listeners.Length == 0) return;
        var current = brain;
        var lines = new List<FeedEvent>(drafts.Count);
        foreach (var draft in drafts)
        {
            FeedEvent? line;
            try
            {
                line = FeedBuilder.Build(draft, current, sequence + 1);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref faults);
                continue;
            }
            if (line is null) continue;
            sequence++;
            lines.Add(line);
        }
        if (lines.Count == 0) return;
        IReadOnlyList<FeedEvent> shared = lines.ToArray();
        foreach (var listener in listeners)
        {
            try
            {
                listener.Invoke(shared);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref faults);
            }
        }
    }

    private void Remove(Subscription subscription)
    {
        lock (gate)
        {
            subscribers = subscribers.Where(item => !ReferenceEquals(item, subscription)).ToArray();
        }
    }

    private sealed class Subscription(AssistantEventBus bus, Action<IReadOnlyList<FeedEvent>> handler) : IDisposable
    {
        private readonly object invocationGate = new();
        private int retired;

        public void Invoke(IReadOnlyList<FeedEvent> batch)
        {
            // Dispatch and normal subscription disposal share a reentrant gate. Dispose cannot return
            // between the last active check and handler entry; a handler may dispose itself.
            lock (invocationGate)
            {
                if (Volatile.Read(ref retired) != 0) return;
                handler(batch);
            }
        }

        // Whole-bus shutdown must remain bounded even when an already dispatched callback is stalled.
        public void Retire() => Interlocked.Exchange(ref retired, 1);

        public void Dispose()
        {
            Retire();
            lock (invocationGate) { } // Join any callback already dispatched; no publisher ever takes this gate.
            bus.Remove(this);
        }
    }
}
