namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Swift <c>@MainActor</c>, expressed without a UI framework (ADR-0002 constraint 11; Core may
/// not reference Avalonia). Marrow.App binds it once at start-up to <c>AvaloniaMainExecutor</c>; tests
/// override it per async flow with a ManualMainLoop. Every member keeps Swift's timing: Post, AsyncAfter and
/// Spawn enqueue (never inline, even from main); Run, Invoke and AssumeIsolated run inline when already on
/// main. ED-065. Source: draft B (proto-B MainActor.cs) with draft C's Ambient seam and the ED-066 task model.
/// </summary>
public static class MainActor
{
    public static Ambient<IMainExecutor> Executor { get; } = new(Unbound.Instance);

    /// <summary>Marrow.App start-up (and a headless UI session): binds the process-wide main executor.</summary>
    public static void Bind(IMainExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        var current = Executor.Current;
        if (!ReferenceEquals(current, Unbound.Instance) && !ReferenceEquals(current, executor))
            throw new SwiftTrapException("MainActor is already bound to another executor.");
        Executor.SetProcessDefault(executor);
    }

    /// <summary>Whether the caller is on the main actor.</summary>
    public static bool IsIsolated => Executor.Current.CheckAccess();

    /// <summary><c>MainActor.preconditionIsolated()</c>: traps (throws, never caught) off main.</summary>
    public static void AssumeIsolated()
    {
        if (!IsIsolated) throw new SwiftTrapException("Incorrect actor executor assumption; expected the main actor.");
    }

    /// <summary><c>MainActor.assumeIsolated { }</c>: runs NOW, synchronously. Never replace with Post.</summary>
    public static void AssumeIsolated(Action body)
    {
        AssumeIsolated();
        body();
    }

    public static T AssumeIsolated<T>(Func<T> body)
    {
        AssumeIsolated();
        return body();
    }

    /// <summary>
    /// <c>DispatchQueue.main.async { }</c>: a later turn of the main loop, even from main. The work runs
    /// outside any Swift task (<c>Task.isCancelled</c> is false inside it), as a GCD block does.
    /// </summary>
    public static void Post(Action work)
    {
        var executor = Executor.Current;
        var context = ExecutionContext.Capture();
        executor.Enqueue(() => RunUnder(context, () => SwiftTask.RunOutsideTask(work)));
    }

    /// <summary>
    /// <c>DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: item)</c>. The timer comes from
    /// <see cref="SwiftClock.Provider"/>, so tests control it. The item can be cancelled until it starts,
    /// which is <c>DispatchWorkItem.cancel()</c>, even after its timer fired.
    /// </summary>
    public static WorkItem AsyncAfter(TimeSpan delay, Action work)
    {
        var executor = Executor.Current;
        var item = new WorkItem(work);
        ITimer? timer = null;
        timer = SwiftClock.Provider.Current.CreateTimer(_ =>
        {
            timer?.Dispose();
            executor.Enqueue(() => SwiftTask.RunOutsideTask(item.Perform));
        }, null, delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
        return item;
    }

    /// <summary>
    /// <c>Task { @MainActor in ... }</c>, and <c>Task { }</c> created in main-actor code: enqueued on the main
    /// loop (never run inline), a new task with its own cancellation that the creator's does not reach.
    /// </summary>
    public static TaskHandle Spawn(Func<Task> body) => TaskHandle.Start(Executor.Current, body, throwing: false);

    public static TaskHandle<T> Spawn<T>(Func<Task<T>> body) => TaskHandle<T>.Start(Executor.Current, body, throwing: false);

    /// <summary>A throwing <c>Task { @MainActor in try ... }</c> (<c>Task&lt;T, Error&gt;</c>): a thrown error faults
    /// <see cref="TaskHandle{T}.Value"/>; a trap still ends the process.</summary>
    public static TaskHandle<T> SpawnThrowing<T>(Func<Task<T>> body) => TaskHandle<T>.Start(Executor.Current, body, throwing: true);

    /// <summary>
    /// <c>await MainActor.run { }</c>: inline, with no suspension, when already on main; otherwise one hop.
    /// The body stays in the caller's task. The caller resumes on its own context afterwards.
    /// </summary>
    public static Task<T> Run<T>(Func<T> body)
    {
        var executor = Executor.Current;
        if (executor.CheckAccess())
        {
            try { return Task.FromResult(body()); }
            catch (Exception error) { return Task.FromException<T>(error); }
        }
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = ExecutionContext.Capture();
        executor.Enqueue(() => RunUnder(context, () =>
        {
            try { source.SetResult(body()); }
            catch (Exception error) { source.SetException(error); }
        }));
        return source.Task;
    }

    public static Task Run(Action body) => Run(() => { body(); return true; });

    /// <summary>
    /// A call to a <c>@MainActor</c> async member from off main (an actor, a detached task, a task-group
    /// child, a pool callback): Swift hops implicitly at the <c>await</c>. Inline when already on main;
    /// otherwise the whole body runs on main, its awaits resume there, and it stays in the caller's task.
    /// </summary>
    public static Task<T> Invoke<T>(Func<Task<T>> body)
    {
        var executor = Executor.Current;
        if (executor.CheckAccess()) return body();
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = ExecutionContext.Capture();
        executor.Enqueue(() => RunUnder(context, () => _ = Complete(body, source)));
        return source.Task;

        static async Task Complete(Func<Task<T>> body, TaskCompletionSource<T> source)
        {
            try { source.SetResult(await body()); }
            catch (Exception error) { source.SetException(error); }
        }
    }

    public static Task Invoke(Func<Task> body) => Invoke(async () => { await body(); return true; });

    private static void RunUnder(ExecutionContext? context, Action work)
    {
        if (context is null) work();
        else ExecutionContext.Run(context, static w => ((Action)w!)(), work);
    }

    private sealed class Unbound : IMainExecutor
    {
        public static readonly Unbound Instance = new();
        public bool CheckAccess() => false;
        public void Enqueue(Action work) =>
            throw new SwiftTrapException("MainActor is not bound: Marrow.App calls MainActor.Bind at start-up; tests use TestRuntime.");
    }
}

/// <summary><c>DispatchWorkItem</c>: cancellable until it starts.</summary>
public sealed class WorkItem(Action work)
{
    private int state; // 0 pending, 1 cancelled, 2 started

    public bool IsCancelled => Volatile.Read(ref state) == 1;

    public void Cancel() => Interlocked.CompareExchange(ref state, 1, 0);

    internal void Perform()
    {
        if (Interlocked.CompareExchange(ref state, 2, 0) != 0) return;
        work();
    }
}
