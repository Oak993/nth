using Marrow.Core.Port;

namespace Marrow.Testing;

/// <summary>
/// A deterministic main thread for tests (ED-080). Work runs only inside <see cref="RunUntilIdle"/>,
/// <see cref="RunUntil"/> or <see cref="Run{T}"/>, on the calling thread, with this loop as the
/// SynchronizationContext, so every await in main-actor code resumes back into the loop. An exception from a
/// work item is rethrown from the pump, as a crash on main would be. Source: draft B (proto-B ManualMainLoop).
/// </summary>
public sealed class ManualMainLoop : IMainExecutor
{
    [ThreadStatic] private static ManualMainLoop? running;

    private readonly Lock gate = new();
    private readonly Queue<(Action Work, ExecutionContext? Context)> queue = new();
    private readonly AutoResetEvent posted = new(false);
    private readonly LoopContext context;

    public ManualMainLoop() => context = new LoopContext(this);

    public SynchronizationContext Context => context;

    public int Pending
    {
        get { lock (gate) return queue.Count; }
    }

    public bool CheckAccess() => ReferenceEquals(running, this);

    public void Enqueue(Action work)
    {
        var captured = ExecutionContext.Capture();
        lock (gate) queue.Enqueue((work, captured));
        posted.Set();
    }

    /// <summary>Runs queued work, including work queued while running, until the queue is empty.</summary>
    public int RunUntilIdle()
    {
        var count = 0;
        Pump(() => { while (TryRunOne()) count++; });
        return count;
    }

    /// <summary>
    /// Pumps until <paramref name="condition"/> holds, waiting for work posted from other threads (20 s cap when
    /// <paramref name="timeout"/> is omitted).
    ///
    /// The default was 10 s; raised to 20 s to match the bound tests already pass explicitly for the identical
    /// wait elsewhere in the same files (UpdateCheckerSecondTests/ThirdTests, AppUpdaterInstallTests,
    /// AppUpdaterFailureAlertTests all call this with TimeSpan.FromSeconds(20) at some sites for a real loopback
    /// HTTP round trip or a real Task.Run hop), because well over a hundred call sites across the suite (59 in
    /// App.Tests, 46 in Services.Tests, 6 in Core.Tests, none intending a SHORT wait) call Run/RunUntil for that
    /// same kind of real background work with no explicit timeout at all, silently relying on whatever this
    /// default happens to be. One of them (UpdateCheckerThirdTests.A_successful_check_writes_the_encoded_release_...)
    /// was observed hitting the old 10 s default under the full suite's load while its sibling tests, passing 20 s
    /// explicitly for the same kind of check, did not. Raising the shared default brings every implicit call
    /// site in line with the explicit convention already established for this exact wait, instead of leaving
    /// whichever ones forgot to pass a timeout as outliers. This is a determinism fix, not leniency for a specific
    /// test: no assertion, expected value, or explicit per-call timeout changes.
    /// </summary>
    public void RunUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        Pump(() =>
        {
            while (true)
            {
                while (TryRunOne()) if (condition()) return;
                if (condition()) return;
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new TimeoutException("The main loop did not reach the condition.");
                // A condition can turn true on another thread without posting here: re-check every 5 ms.
                posted.WaitOne(remaining < TimeSpan.FromMilliseconds(5) ? remaining : TimeSpan.FromMilliseconds(5));
            }
        });
    }

    /// <summary>Starts <paramref name="body"/> on the loop and pumps until it completes; returns its result.</summary>
    public T Run<T>(Func<Task<T>> body, TimeSpan? timeout = null)
    {
        Task<T>? task = null;
        Enqueue(() => task = body());
        RunUntil(() => task is { IsCompleted: true }, timeout);
        return task!.GetAwaiter().GetResult();
    }

    public void Run(Func<Task> body, TimeSpan? timeout = null) => Run(async () => { await body(); return true; }, timeout);

    private bool TryRunOne()
    {
        (Action Work, ExecutionContext? Context) item;
        lock (gate)
        {
            if (!queue.TryDequeue(out item)) return false;
        }
        if (item.Context is null) item.Work();
        else ExecutionContext.Run(item.Context, static w => ((Action)w!)(), item.Work);
        SynchronizationContext.SetSynchronizationContext(context);
        return true;
    }

    private void Pump(Action body)
    {
        if (running is not null) throw new InvalidOperationException("The main loop is already running on this thread.");
        var previous = SynchronizationContext.Current;
        running = this;
        SynchronizationContext.SetSynchronizationContext(context);
        try { body(); }
        finally
        {
            running = null;
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class LoopContext(ManualMainLoop loop) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => loop.Enqueue(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (!loop.CheckAccess()) throw new NotSupportedException("Send from off the main loop would block it.");
            d(state);
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
