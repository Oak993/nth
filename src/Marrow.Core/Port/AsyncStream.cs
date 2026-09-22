namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Swift <c>AsyncStream&lt;Element&gt;</c> with the default <c>.unbounded</c> buffering and a single
/// consumer (ED-069). Kept semantics: values buffer before the consumer starts; a yield after finish is
/// dropped and reports Terminated; cancelling the consuming task ENDS a <c>for await</c> quietly (no
/// exception), terminates the stream and calls OnTermination(Cancelled) before the consumer sees the end;
/// values already buffered are still delivered first; OnTermination runs exactly once; Finish is
/// idempotent; a second concurrent consumer traps. <c>for await x in s</c> is <c>await foreach (var x in s)</c>,
/// which observes the current task's cancellation (ED-066). A bare Channel's ReadAllAsync would throw instead.
/// Source: draft B (proto-B Concurrency/AsyncStream.cs), with the ambient task token and the trap type.
/// </summary>
public sealed class AsyncStream<T> : IAsyncEnumerable<T>
{
    public enum Termination { Finished, Cancelled }

    public enum YieldResult { Enqueued, Terminated }

    private readonly Lock gate = new();
    private readonly Queue<T> buffer = new();
    private TaskCompletionSource<bool>? waiter;
    private bool terminal;
    private Action<Termination>? onTermination;

    private AsyncStream() { }

    /// <summary><c>AsyncStream.makeStream(of:)</c>.</summary>
    public static (AsyncStream<T> Stream, Continuation Continuation) MakeStream()
    {
        var stream = new AsyncStream<T>();
        return (stream, new Continuation(stream));
    }

    /// <summary><c>AsyncStream { continuation in ... }</c>: the closure runs synchronously, now.</summary>
    public static AsyncStream<T> Create(Action<Continuation> build)
    {
        var (stream, continuation) = MakeStream();
        build(continuation);
        return stream;
    }

    public sealed class Continuation
    {
        private readonly AsyncStream<T> stream;
        internal Continuation(AsyncStream<T> stream) => this.stream = stream;

        public YieldResult Yield(T value) => stream.Yield(value);

        public void Finish() => stream.Terminate(Termination.Finished);

        /// <summary><c>continuation.onTermination = { reason in ... }</c>: called at most once.</summary>
        public Action<Termination>? OnTermination
        {
            set { lock (stream.gate) stream.onTermination = stream.terminal ? null : value; }
        }
    }

    private YieldResult Yield(T value)
    {
        TaskCompletionSource<bool>? wake;
        lock (gate)
        {
            if (terminal) return YieldResult.Terminated;
            buffer.Enqueue(value);
            wake = waiter;
            waiter = null;
        }
        wake?.TrySetResult(true);
        return YieldResult.Enqueued;
    }

    private void Terminate(Termination reason)
    {
        Action<Termination>? handler;
        TaskCompletionSource<bool>? wake;
        lock (gate)
        {
            if (terminal) return;
            terminal = true;
            handler = onTermination;
            onTermination = null;
            wake = waiter;
            waiter = null;
        }
        handler?.Invoke(reason); // before the consumer is woken to see the end
        wake?.TrySetResult(false);
    }

    /// <summary>
    /// The enumerator observes <paramref name="cancellationToken"/> when one is given (WithCancellation), and
    /// otherwise the token of the task that starts the iteration.
    /// </summary>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new Enumerator(this, cancellationToken.CanBeCanceled ? cancellationToken : SwiftTask.CancellationToken);

    private sealed class Enumerator(AsyncStream<T> stream, CancellationToken token) : IAsyncEnumerator<T>
    {
        public T Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            var registration = token.UnsafeRegister(static s => ((AsyncStream<T>)s!).Terminate(Termination.Cancelled), stream);
            try
            {
                return await Next();
            }
            finally
            {
                registration.Unregister();
            }
        }

        private async Task<bool> Next()
        {
            while (true)
            {
                Task<bool> wait;
                lock (stream.gate)
                {
                    if (stream.buffer.TryDequeue(out var value))
                    {
                        Current = value;
                        return true;
                    }
                    if (stream.terminal) return false;
                    if (stream.waiter is not null)
                        throw new SwiftTrapException("AsyncStream has more than one consumer.");
                    stream.waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    wait = stream.waiter.Task;
                }
                await wait;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
