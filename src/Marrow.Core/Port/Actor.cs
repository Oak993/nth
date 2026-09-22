using System.Runtime.ExceptionServices;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Base for a ported Swift <c>actor</c> (ED-067). Each isolated method wraps its body in
/// <c>Isolated(...)</c> and keeps the Swift body literally, awaits included. The body runs on a private strand
/// that is also its SynchronizationContext, so every await resumes on the strand: one synchronous segment at
/// a time, and the actor is RELEASED at every await (Swift actors are re-entrant). A call from inside the
/// actor runs inline, as a same-actor call does in Swift. Never ConfigureAwait(false) inside Isolated, never
/// block inside it, never hold a lock or SemaphoreSlim across an await in place of isolation.
/// Source: draft B (proto-B Concurrency/Actor.cs), with Spawn on the ED-066 task model.
/// </summary>
public abstract class Actor
{
    private readonly SerialExecutor executor;

    /// <param name="target">Where the strand runs: the pool in production; a ManualMainLoop in a test that
    /// needs a deterministic actor.</param>
    protected Actor(IExecutor? target = null) => executor = new SerialExecutor(target);

    /// <summary>Whether the caller is inside this actor.</summary>
    public bool IsIsolated => executor.IsCurrent;

    protected void AssertIsolated()
    {
        if (!IsIsolated) throw new SwiftTrapException($"Expected to be isolated to {GetType().Name}.");
    }

    /// <summary>A call into the actor. It stays in the caller's task, so the body sees the caller's cancellation.</summary>
    protected Task<T> Isolated<T>(Func<Task<T>> body)
    {
        if (IsIsolated) return body();
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        executor.Enqueue(() => _ = Run(body, source));
        return source.Task;
    }

    protected Task<T> Isolated<T>(Func<T> body) => Isolated(() => Task.FromResult(body()));

    protected Task Isolated(Func<Task> body) => Isolated(async () => { await body(); return true; });

    protected Task Isolated(Action body) => Isolated(() => { body(); return true; });

    /// <summary><c>Task { ... }</c> created inside the actor: a new task that runs on the actor, later.</summary>
    protected TaskHandle Spawn(Func<Task> body) => TaskHandle.Start(executor, body, throwing: false);

    private static async Task Run<T>(Func<Task<T>> body, TaskCompletionSource<T> source)
    {
        try { source.SetResult(await body()); }
        catch (Exception error) { source.SetException(error); }
    }
}

/// <summary>
/// A private serial <c>DispatchQueue(label:)</c> (ED-069): FIFO, one item at a time, on a strand over the
/// pool (or a test loop). <see cref="Async"/> runs outside any Swift task, as a GCD block does.
/// <see cref="Sync(Action)"/> blocks until the item and everything queued before it has run (so
/// <c>queue.sync {}</c> is the barrier <c>Storage.swift:378</c> uses); calling it from the queue itself
/// traps, as GCD does. Call Sync only from the pool or a dedicated thread, never from an actor body, and from
/// main only where the Swift code also blocks main. Source: draft B (proto-B SerialQueue).
/// </summary>
public sealed class SerialQueue(string label, IExecutor? target = null)
{
    private readonly SerialExecutor executor = new(target);

    public string Label { get; } = label;

    public bool IsCurrent => executor.IsCurrent;

    public void Async(Action work) => executor.Enqueue(() => SwiftTask.RunOutsideTask(work));

    /// <summary><c>queue.asyncAfter(deadline: .now() + delay) { }</c> (MCPOAuth.swift:452): the timer comes from SwiftClock.</summary>
    public WorkItem AsyncAfter(TimeSpan delay, Action work) => GlobalQueue.After(delay, work, Async);

    public T Sync<T>(Func<T> work)
    {
        if (executor.IsCurrent) throw new SwiftTrapException($"{Label}: sync called on the queue itself.");
        using var done = new ManualResetEventSlim();
        T result = default!;
        Exception? failure = null;
        executor.Enqueue(() =>
        {
            try { result = work(); }
            catch (Exception error) { failure = error; }
            finally { done.Set(); }
        });
        done.Wait();
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
        return result;
    }

    public void Sync(Action work) => Sync(() => { work(); return true; });
}

/// <summary>
/// <c>DispatchQueue.global(qos:)</c> (ED-069): <c>.async { }</c> (Shell.swift:91) and <c>.asyncAfter(deadline:) { }</c>, the
/// kill ladders of ED-027/ED-047 (Shell.swift:113, StdioProcess.swift:125 and :178, MCPHub.swift:290,
/// MCPProbe.swift:231). The work runs on the pool, outside any Swift task; the QoS has no pool equivalent and is
/// dropped (ED-066); the delay's timer comes from SwiftClock, so tests drive it.
/// </summary>
public static class GlobalQueue
{
    public static void Async(Action work) => ThreadPoolExecutor.Shared.Enqueue(() => SwiftTask.RunOutsideTask(work));

    public static WorkItem AsyncAfter(TimeSpan delay, Action work) => After(delay, work, Async);

    internal static WorkItem After(TimeSpan delay, Action work, Action<Action> enqueue)
    {
        var item = new WorkItem(work);
        ITimer? timer = null;
        timer = SwiftClock.Provider.Current.CreateTimer(_ =>
        {
            timer?.Dispose();
            enqueue(item.Perform);
        }, null, delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
        return item;
    }
}
