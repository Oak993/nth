namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. <c>withChecked(Throwing)Continuation { c in ... }</c> (ED-069). The source is created with
/// RunContinuationsAsynchronously, so a resume never runs the awaiting code inline on the resumer's thread
/// or inside its lock (Shell.swift:218-221 states the same rule). A second resume traps, as in Swift. The
/// closure runs synchronously before the caller suspends. Source: draft B (proto-B Continuations.cs).
/// </summary>
public sealed class CheckedContinuation<T>
{
    private readonly TaskCompletionSource<T> source = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<T> Task => source.Task;

    public void Resume(T value)
    {
        if (!source.TrySetResult(value)) throw new SwiftTrapException("SWIFT TASK CONTINUATION MISUSE: resumed more than once.");
    }

    public void Resume(Exception error)
    {
        if (!source.TrySetException(error)) throw new SwiftTrapException("SWIFT TASK CONTINUATION MISUSE: resumed more than once.");
    }

    /// <summary><c>await withCheckedThrowingContinuation { continuation in ... }</c>.</summary>
    public static Task<T> With(Action<CheckedContinuation<T>> body)
    {
        var continuation = new CheckedContinuation<T>();
        body(continuation);
        return continuation.Task;
    }
}

// Shell.swift:218-243 OnceContinuation is a Swift type, so it is NOT here: its twin is ported in
// Support/Shell.cs from CheckedContinuation and Mutex, exactly as the Swift class wraps them.

/// <summary>
/// Swift <c>Mutex&lt;Value&gt;</c> (Synchronization) and <c>OSAllocatedUnfairLock(initialState:)</c> (ED-069): owns its
/// value, reachable only inside <see cref="WithLock{TResult}"/>, and NOT re-entrant. System.Threading.Lock is
/// re-entrant, so re-entry is detected and trapped here instead of silently succeeding. Keep the source's
/// "take under the lock, act outside it" structure; no await can occur inside (the body is synchronous).
/// Source: drafts A, B and C (identical), with the trap type.
/// </summary>
public sealed class Mutex<T>(T initialValue)
{
    public delegate TResult Body<TResult>(ref T value);
    public delegate void Body(ref T value);

    private readonly Lock gate = new();
    private T value = initialValue;
    private int owner;

    public TResult WithLock<TResult>(Body<TResult> body)
    {
        var me = Environment.CurrentManagedThreadId;
        if (Volatile.Read(ref owner) == me) throw new SwiftTrapException("Mutex is not re-entrant.");
        lock (gate)
        {
            owner = me;
            try { return body(ref value); }
            finally { owner = 0; }
        }
    }

    public void WithLock(Body body) => WithLock((ref T v) => { body(ref v); return true; });
}
