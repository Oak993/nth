using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-ASYNC-2 (IMPLEMENTING.md 3.5). Source: draft B (proto-B ContinuationTests), on the ED-066 task model.
public sealed class ContinuationTests
{
    private static readonly object Gate = new();

    private static async Task<(bool InsideLock, int Thread)> AwaitAndObserve(Task<int> task)
    {
        await task;
        return (Monitor.IsEntered(Gate), Environment.CurrentManagedThreadId);
    }

    private static Task<(bool InsideLock, int Thread)> StartWaiter(Task<int> task)
    {
        Task<(bool, int)> waiter = null!;
        var thread = new Thread(() => waiter = AwaitAndObserve(task));
        thread.Start();
        thread.Join();
        return waiter;
    }

    // Resumes on a thread with no SynchronizationContext, as a process callback or pool thread does. (.NET does
    // not inline a continuation onto a thread with a non-default context, such as xUnit's test thread.)
    private static int ResumeUnderLockOnPlainThread(Action resume)
    {
        var resumer = 0;
        var thread = new Thread(() =>
        {
            lock (Gate)
            {
                resumer = Environment.CurrentManagedThreadId;
                resume();
            }
        });
        thread.Start();
        thread.Join();
        return resumer;
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-2")]
    public async Task Without_RunContinuationsAsynchronously_the_awaiter_runs_inside_the_lock()
    {
        // The pitfall Shell.swift:220-221 avoids: a resume runs the awaiting code, here inside the resumer's lock.
        var inline = new TaskCompletionSource<int>();
        var waiter = StartWaiter(inline.Task);
        var resumer = ResumeUnderLockOnPlainThread(() => inline.SetResult(1));
        var (insideLock, ranOn) = await waiter;
        Assert.True(insideLock);
        Assert.Equal(resumer, ranOn);
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-2")]
    [Trait("Decision", "ED-069")]
    public async Task CheckedContinuation_resumes_the_awaiter_outside_the_lock()
    {
        var continuation = new CheckedContinuation<int>();
        var waiter = StartWaiter(continuation.Task);
        var resumer = ResumeUnderLockOnPlainThread(() => continuation.Resume(1));
        var (insideLock, ranOn) = await waiter;
        Assert.False(insideLock);
        Assert.NotEqual(resumer, ranOn);
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-2")]
    public void CheckedContinuation_resumed_twice_traps()
    {
        var continuation = new CheckedContinuation<int>();
        continuation.Resume(1);
        Assert.Throws<SwiftTrapException>(() => continuation.Resume(2));
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-2")]
    public async Task The_continuation_closure_runs_before_the_caller_suspends()
    {
        var ran = false;
        var task = CheckedContinuation<int>.With(c => { ran = true; c.Resume(7); });
        Assert.True(ran);
        Assert.Equal(7, await task);
    }

    [Fact]
    [Trait("Rule", "R-TASK-4")]
    [Trait("Rule", "R-ASYNC-2")]
    public void Rpc_request_cancelled_while_pending_throws_cancellation_on_main()
    {
        // JSONRPC.swift:70-87: the onCancel handler hops to main, removes the pending continuation and resumes it
        // with CancellationError.
        using var runtime = new TestRuntime();
        var pending = new Dictionary<int, CheckedContinuation<string>>();
        var request = MainActor.SpawnThrowing(() =>
        {
            const int id = 1;
            return SwiftTask.WithCancellationHandler(() =>
            {
                SwiftTask.CheckCancellation();
                return CheckedContinuation<string>.With(c => pending[id] = c);
            }, () => MainActor.Spawn(() =>
            {
                if (pending.Remove(id, out var c)) c.Resume(new CancellationError());
                return Task.CompletedTask;
            }));
        });
        runtime.RunUntilIdle();
        Assert.Single(pending);
        request.Cancel();
        runtime.RunUntilIdle();
        Assert.Empty(pending);
        Assert.True(request.Value.IsFaulted);
        Assert.IsType<CancellationError>(request.Value.Exception!.InnerException);
    }
}
