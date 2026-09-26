using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/PendingReplies.swift (B3 52bc7926), :1-63. New at 1.7.0 (R3-13, impact
// #1/#2 of that requirement): extracted from the continuation maps ClaudeSession and PiSession each kept
// privately at B2 (ClaudeSession.swift:37, 306-309, 336-343, 686-690; PiSession.swift:257-276, 304-309, 638-640).

/// <summary>
/// :3-6 Replies waiting on a request id. The id is registered before send runs, so a fast reply can't arrive
/// before anyone is waiting for it. MainActor-isolated (R-ISO-1): every member assumes the caller is already
/// on main and does not re-check per call, as the Swift @MainActor class does not.
/// </summary>
/// <remarks>
/// ENGINEERING DECISION (review, lane L1k): the Swift class is generic over any <c>Value: Sendable</c>, but a
/// bare C# <c>TValue?</c> only carries true Optional semantics (able to tell "no value" from "the default
/// value") when TValue is a reference type; for an unconstrained generic, <c>TValue?</c> erases to plain
/// <c>TValue</c> for a value-type instantiation, so <c>default</c> and an explicitly-supplied zero value become
/// indistinguishable and <c>timeoutValue is { } value</c> is unconditionally true. :24's Swift contract
/// ("without a timeoutValue, a timeout throws CancellationError") only holds under that trap-free condition, so
/// TValue is constrained to <c>class</c> here, matching every real and planned caller
/// (<c>PendingReplies&lt;JSONValue&gt;</c>, a reference type: B3_PLAN.md N1/A18) and the same guard the port's
/// own <see cref="Marrow.Core.Port.SwiftErrors"/> uses (<c>Optional&lt;T&gt; where T : class</c> vs
/// <c>OptionalValue&lt;T&gt; where T : struct</c>) and <c>Ambient&lt;T&gt; where T : class</c> uses elsewhere in
/// this codebase.
/// </remarks>
public sealed class PendingReplies<TValue> where TValue : class
{
    // :7
    private readonly Dictionary<string, CheckedContinuation<TValue>> continuations = [];

    /// <summary>
    /// :9-36. Without a <paramref name="timeoutValue"/>, a timeout throws CancellationError. Swift's
    /// <c>send</c> parameter has no default and trails two defaulted ones (R-NAME-5); it is moved ahead of
    /// timeout/timeoutValue here because C# requires optional parameters to trail, and every call site already
    /// names timeout and timeoutValue.
    /// </summary>
    public Task<TValue> Register(string id, Action send, TimeSpan? timeout = null, TValue? timeoutValue = default)
    {
        // :32-34. A weak reference, exactly like the Swift `[weak self]`: unlike the timeout task below, the
        // cancellation handler must not keep this instance alive past the caller's own lifetime.
        var weak = new WeakReference<PendingReplies<TValue>>(this);
        // :16, :31-35
        return SwiftTask.WithCancellationHandler(async () =>
        {
            // :17
            SwiftTask.CheckCancellation();
            // :18-30
            return await CheckedContinuation<TValue>.With(continuation =>
            {
                // :19-20
                continuations[id] = continuation;
                send();
                // :21
                if (timeout is not { } duration) return;
                // :22-29. Strong self (no [weak self] in the Swift Task {}): the timer keeps this instance
                // alive for its own duration, independent of the caller's task or cancellation.
                MainActor.Spawn(async () =>
                {
                    // :23
                    await SwiftTask.TrySleep(duration);
                    // :24-28
                    if (timeoutValue is { } value) Resolve(id, value);
                    else Fail(id, new CancellationError());
                });
            });
        }, () =>
        {
            // :32-34
            MainActor.Spawn(() =>
            {
                if (weak.TryGetTarget(out var self) && self.continuations.Remove(id, out var continuation))
                {
                    continuation.Resume(new CancellationError());
                }
                return Task.CompletedTask;
            });
        });
    }

    /// <summary>:38-43. Removes and resumes the waiter for <paramref name="id"/>; false when none is waiting.</summary>
    public bool Resolve(string id, TValue value)
    {
        // :40
        if (!continuations.Remove(id, out var continuation)) return false;
        // :41
        continuation.Resume(value);
        // :42
        return true;
    }

    /// <summary>:45-50. Removes and fails the waiter for <paramref name="id"/>; false when none is waiting.</summary>
    public bool Fail(string id, Exception error)
    {
        // :47
        if (!continuations.Remove(id, out var continuation)) return false;
        // :48
        continuation.Resume(error);
        // :49
        return true;
    }

    /// <summary>:52-56. Every waiter is resumed with <paramref name="value"/>; the map is emptied first.</summary>
    public void ResolveAll(TValue value)
    {
        // :53
        var waiting = continuations.Values.ToArray();
        // :54
        continuations.Clear();
        // :55
        foreach (var continuation in waiting) continuation.Resume(value);
    }

    /// <summary>:58-62. Every waiter is failed with <paramref name="error"/>; the map is emptied first.</summary>
    public void FailAll(Exception error)
    {
        // :59
        var waiting = continuations.Values.ToArray();
        // :60
        continuations.Clear();
        // :61
        foreach (var continuation in waiting) continuation.Resume(error);
    }
}
