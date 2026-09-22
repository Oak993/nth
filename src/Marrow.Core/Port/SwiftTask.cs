namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. The current Swift task, as ambient state (ED-066). Swift cancellation is a property of the
/// task a function runs in, not a parameter: <c>Task.isCancelled</c>, <c>Task.checkCancellation()</c> and
/// <c>Task.sleep</c> read it from any function, sync or async, called within that task. The port keeps that
/// shape: every task started through the helpers (MainActor.Spawn, TaskHandle.Detached, Actor.Spawn,
/// TaskGroup children, AsyncLetScope children) runs with its token as the ambient current task, awaits carry
/// it along, and work that is not a Swift task (MainActor.Post, AsyncAfter, SerialQueue.Async) runs outside
/// any task. Ported signatures therefore never grow a CancellationToken parameter the Swift code lacks.
/// </summary>
public static class SwiftTask
{
    // Task.Delay rejects more than uint.MaxValue - 1 milliseconds; Swift has no cap.
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    private static readonly AsyncLocal<TaskState?> current = new();

    private sealed class TaskState(CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
    }

    /// <summary>True inside a Swift task. Diagnostic only; Swift code has no such question.</summary>
    public static bool IsInTask => current.Value is not null;

    /// <summary><c>Task.isCancelled</c>: false outside any task, as in Swift.</summary>
    public static bool IsCancelled => current.Value is { } state && state.Token.IsCancellationRequested;

    /// <summary>The current task's token (None outside a task), for .NET APIs that take one.</summary>
    public static CancellationToken CancellationToken => current.Value?.Token ?? CancellationToken.None;

    /// <summary><c>try Task.checkCancellation()</c>.</summary>
    public static void CheckCancellation()
    {
        if (IsCancelled) throw new CancellationError();
    }

    /// <summary>
    /// <c>try await Task.sleep(for: d)</c> (5 sites): throws <see cref="CancellationError"/> when the task is
    /// cancelled, before or during the sleep. A zero or negative duration still suspends once (Swift enqueues
    /// a job with a past deadline) instead of throwing, as Task.Delay would, or waiting forever at -1 ms.
    /// Waits beyond Task.Delay's 49.7-day cap are chunked. Time comes from <see cref="SwiftClock.Provider"/>.
    /// </summary>
    public static async Task Sleep(TimeSpan duration)
    {
        var token = CancellationToken;
        if (token.IsCancellationRequested) throw new CancellationError();
        if (duration <= TimeSpan.Zero)
        {
            await Task.Yield();
            if (token.IsCancellationRequested) throw new CancellationError();
            return;
        }
        var provider = SwiftClock.Provider.Current;
        try
        {
            while (duration > TimeSpan.Zero)
            {
                var step = duration > MaximumDelay ? MaximumDelay : duration;
                await Task.Delay(step, provider, token);
                duration -= step;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new CancellationError();
        }
    }

    /// <summary>
    /// <c>try? await Task.sleep(for: d)</c> (181 sites): never throws. Returns false when cancelled, which
    /// is <c>nil</c> from <c>try?</c>; the Swift code after the sleep then runs and checks the task itself.
    /// </summary>
    public static async Task<bool> TrySleep(TimeSpan duration)
    {
        try
        {
            await Sleep(duration);
            return true;
        }
        catch (CancellationError)
        {
            return false;
        }
    }

    /// <summary>
    /// <c>.seconds(x)</c> from a Double (a <c>TimeInterval</c>): negative stays negative (Sleep treats it as
    /// zero), values beyond TimeSpan saturate, and NaN or infinity trap as Swift's Duration initializer does.
    /// </summary>
    public static TimeSpan Seconds(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new SwiftTrapException("Duration from a non-finite Double.");
        var ticks = seconds * TimeSpan.TicksPerSecond;
        if (ticks >= long.MaxValue) return TimeSpan.MaxValue;
        if (ticks <= long.MinValue) return TimeSpan.MinValue;
        return TimeSpan.FromTicks((long)Math.Round(ticks, MidpointRounding.ToEven));
    }

    /// <summary>
    /// <c>withTaskCancellationHandler { operation } onCancel: { handler }</c>. The handler runs at once on the
    /// cancelling thread, inside Cancel(), and before the operation when the task is already cancelled. It
    /// runs on an arbitrary thread, so a handler that touches main-actor state hops (MainActor.Spawn).
    /// </summary>
    public static async Task<T> WithCancellationHandler<T>(Func<Task<T>> operation, Action onCancel)
    {
        var registration = CancellationToken.Register(onCancel);
        try { return await operation(); }
        finally { registration.Unregister(); }
    }

    public static Task WithCancellationHandler(Func<Task> operation, Action onCancel) =>
        WithCancellationHandler(async () => { await operation(); return true; }, onCancel);

    /// <summary>Called first inside a task body's async wrapper, so the change is scoped to that task.</summary>
    internal static void Enter(CancellationToken token) => current.Value = new TaskState(token);

    /// <summary>Runs work that is not a Swift task (a GCD block, a timer, a view update) with no current task.</summary>
    internal static void RunOutsideTask(Action work)
    {
        var saved = current.Value;
        if (saved is null)
        {
            work();
            return;
        }
        current.Value = null;
        try { work(); }
        finally { current.Value = saved; }
    }
}

/// <summary>
/// Swift <c>CancellationError</c>: what <c>Task.checkCancellation()</c> and a cancelled <c>Task.sleep</c>
/// throw. A .NET OperationCanceledException from a BCL API is NOT this type: convert it at that boundary to
/// what the Swift API would have thrown (for example URLError.cancelled for URLSession) and record it.
/// </summary>
public sealed class CancellationError : OperationCanceledException, ISwiftErrorIdentity
{
    public CancellationError() : base("CancellationError()") { }
    public string Domain => "Swift.CancellationError";
    public long Code => 1;
}

/// <summary>
/// A stored Swift <c>Task</c> handle (<c>readTask</c>, <c>scrollSettle</c>, <c>renderTask</c>). Cancel is a flag
/// the body polls through <see cref="SwiftTask.IsCancelled"/>; it is idempotent, runs registered
/// cancellation handlers synchronously on the cancelling thread, and never reaches another task. Identity
/// is reference identity (<c>inFlight[key] == task</c>). Source: draft B (proto-B TaskHandle.cs) with the
/// ED-066 ambient task and ED-078 trap semantics.
/// </summary>
public class TaskHandle
{
    private protected TaskHandle(CancellationTokenSource? cancellation) => Cancellation = cancellation ?? new();

    private protected CancellationTokenSource Cancellation { get; }

    /// <summary><c>await task.value</c> for a task with no result. Faults only for a throwing task's error.</summary>
    public Task Completion { get; private protected set; } = Task.CompletedTask;

    /// <summary><c>task.isCancelled</c>.</summary>
    public bool IsCancelled => Cancellation.IsCancellationRequested;

    /// <summary><c>task.cancel()</c>.</summary>
    public void Cancel() => Cancellation.Cancel();

    /// <summary><c>Task.detached { }</c>, and <c>Task { }</c> created in nonisolated code: the pool, no inherited
    /// actor, no inherited cancellation. Swift priorities have no pool equivalent and are dropped (ED-066).</summary>
    public static TaskHandle Detached(Func<Task> body) => Start(ThreadPoolExecutor.Shared, body, throwing: false);

    public static TaskHandle<T> Detached<T>(Func<Task<T>> body) =>
        TaskHandle<T>.Start(ThreadPoolExecutor.Shared, body, throwing: false);

    /// <summary>A throwing <c>Task.detached</c> (<c>Task&lt;T, Error&gt;</c>): a thrown error faults <see cref="TaskHandle{T}.Value"/>.</summary>
    public static TaskHandle<T> DetachedThrowing<T>(Func<Task<T>> body) =>
        TaskHandle<T>.Start(ThreadPoolExecutor.Shared, body, throwing: true);

    internal static TaskHandle Start(IExecutor executor, Func<Task> body, bool throwing, CancellationTokenSource? cancellation = null)
    {
        var handle = new TaskHandle(cancellation);
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handle.Completion = source.Task;
        var token = handle.Cancellation.Token;
        executor.Enqueue(() => Run(token, body, source, throwing));
        return handle;
    }

    // async void on purpose: in a NON-throwing Swift task only a trap (or a port bug) can escape the body, and
    // Swift then ends the process. An exception escaping here is posted to the SynchronizationContext (the
    // main loop, a strand) or thrown on the pool, which does the same. A throwing task's thrown error is
    // delivered through Completion; a trap never is.
    private static async void Run(CancellationToken token, Func<Task> body, TaskCompletionSource source, bool throwing)
    {
        SwiftTask.Enter(token);
        try
        {
            await body();
        }
        catch (Exception error) when (throwing && SwiftTry.IsThrownError(error))
        {
            source.SetException(error);
            return;
        }
        source.SetResult();
    }
}

/// <summary>A stored Swift <c>Task&lt;T, _&gt;</c> handle; <see cref="Value"/> is <c>await task.value</c>.</summary>
public sealed class TaskHandle<T> : TaskHandle
{
    private TaskHandle(CancellationTokenSource? cancellation) : base(cancellation) { }

    public Task<T> Value { get; private set; } = null!;

    internal static TaskHandle<T> Start(IExecutor executor, Func<Task<T>> body, bool throwing, CancellationTokenSource? cancellation = null)
    {
        var handle = new TaskHandle<T>(cancellation);
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        handle.Value = source.Task;
        handle.Completion = source.Task;
        var token = handle.Cancellation.Token;
        executor.Enqueue(() => Run(token, body, source, throwing));
        return handle;
    }

    private static async void Run(CancellationToken token, Func<Task<T>> body, TaskCompletionSource<T> source, bool throwing)
    {
        SwiftTask.Enter(token);
        T value;
        try
        {
            value = await body();
        }
        catch (Exception error) when (throwing && SwiftTry.IsThrownError(error))
        {
            source.SetException(error);
            return;
        }
        source.SetResult(value);
    }
}

/// <summary>
/// <c>withTaskGroup(of: T.self) { group in ... }</c>. Children run on the pool as child tasks: they see the
/// parent task's cancellation and <see cref="CancelAll"/>, and a child that calls a <c>@MainActor</c> member
/// hops with <see cref="MainActor.Invoke{T}"/>, as the Swift child closure hops at its <c>await</c>.
/// <see cref="Next"/> returns results in completion order (<c>group.next()</c>, <c>for await r in group</c>).
/// The scope never returns before every child has finished: children not consumed are awaited, not
/// cancelled, as withTaskGroup does. Source: draft B (proto-B TaskGroup), extended with results and next().
/// </summary>
public sealed class TaskGroup<T> : IAsyncEnumerable<T>
{
    private readonly Lock gate = new();
    private readonly Queue<T> finished = new();
    private readonly CancellationTokenSource cancellation;
    private TaskCompletionSource? signal;
    private int running;
    private int unconsumed;

    private TaskGroup(CancellationToken parent) => cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);

    /// <summary><c>group.isCancelled</c>.</summary>
    public bool IsCancelled => cancellation.IsCancellationRequested;

    /// <summary><c>group.cancelAll()</c>.</summary>
    public void CancelAll() => cancellation.Cancel();

    /// <summary><c>group.addTask { ... }</c>.</summary>
    public void AddTask(Func<Task<T>> body)
    {
        lock (gate)
        {
            running++;
            unconsumed++;
        }
        var token = cancellation.Token;
        ThreadPoolExecutor.Shared.Enqueue(() => RunChild(token, body));
    }

    /// <summary><c>await group.next()</c>: (false, default) once no child is left to return.</summary>
    public async Task<(bool HasValue, T Value)> Next()
    {
        while (true)
        {
            Task wait;
            lock (gate)
            {
                if (finished.TryDequeue(out var value))
                {
                    unconsumed--;
                    return (true, value);
                }
                if (unconsumed == 0) return (false, default!);
                signal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = signal.Task;
            }
            await wait;
        }
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        while (await Next() is (true, var value)) yield return value;
    }

    /// <summary><c>await withTaskGroup(of: T.self) { group in ... }</c>, in the current task.</summary>
    public static async Task<TResult> Run<TResult>(Func<TaskGroup<T>, Task<TResult>> body)
    {
        var group = new TaskGroup<T>(SwiftTask.CancellationToken);
        try
        {
            return await body(group);
        }
        finally
        {
            await group.WaitForChildren();
            group.cancellation.Dispose();
        }
    }

    public static Task Run(Func<TaskGroup<T>, Task> body) => Run(async group => { await body(group); return true; });

    // A withTaskGroup child cannot throw in Swift; an exception escaping here is a trap and ends the process.
    private async void RunChild(CancellationToken token, Func<Task<T>> body)
    {
        SwiftTask.Enter(token);
        var value = await body();
        TaskCompletionSource? wake;
        lock (gate)
        {
            finished.Enqueue(value);
            running--;
            wake = signal;
            signal = null;
        }
        wake?.TrySetResult();
    }

    private async Task WaitForChildren()
    {
        while (true)
        {
            Task wait;
            lock (gate)
            {
                if (running == 0) return;
                signal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = signal.Task;
            }
            await wait;
        }
    }
}

/// <summary><c>withTaskGroup(of: Void.self)</c>: the same group with no results.</summary>
public sealed class TaskGroup
{
    private readonly TaskGroup<bool> inner;

    private TaskGroup(TaskGroup<bool> inner) => this.inner = inner;

    public bool IsCancelled => inner.IsCancelled;

    public void CancelAll() => inner.CancelAll();

    public void AddTask(Func<Task> body) => inner.AddTask(async () => { await body(); return true; });

    /// <summary><c>for await _ in group</c>: true each time a child finishes, false once none is left.</summary>
    public async Task<bool> Next() => (await inner.Next()).HasValue;

    public static Task Run(Func<TaskGroup, Task> body) => TaskGroup<bool>.Run(group => body(new TaskGroup(group)));
}

/// <summary>
/// <c>async let x = f()</c>: each child starts at once on the pool as a child task of the current task
/// (a <c>@MainActor</c> callee hops inside with MainActor.Invoke); <c>await x</c> is awaiting the returned
/// task where Swift reads it. At scope exit (<c>await using</c>) every child is cancelled and awaited, which
/// is what Swift does to an <c>async let</c> that was never awaited; an awaited child has already finished.
/// </summary>
public sealed class AsyncLetScope : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(SwiftTask.CancellationToken);
    private readonly List<Task> children = [];

    public Task<T> Start<T>(Func<Task<T>> body)
    {
        var child = TaskHandle<T>.Start(ThreadPoolExecutor.Shared, body, throwing: true, cancellation);
        children.Add(child.Completion);
        return child.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await cancellation.CancelAsync();
        foreach (var child in children)
            await child.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellation.Dispose();
    }
}
