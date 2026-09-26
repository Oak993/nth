using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// Source: DroppyCode/Core/Support/PendingReplies.swift (B3 52bc7926), :1-63. New at 1.7.0 (R3-13): extracted
// from the continuation maps ClaudeSession and PiSession each kept privately at B2.
public sealed class PendingRepliesTests
{
    private static CancellationError Faulted(Task task)
    {
        Assert.True(task.IsFaulted, "the task did not fail");
        return Assert.IsType<CancellationError>(task.Exception!.InnerException);
    }

    private static Exception FailedWith(Task task)
    {
        Assert.True(task.IsFaulted, "the task did not fail");
        return task.Exception!.InnerException!;
    }

    private static T Resolved<T>(Task<T> task)
    {
        Assert.True(task.IsCompletedSuccessfully, "the task did not resolve");
        return task.Result;
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-164")] // Core/Support/PendingReplies.swift:17-20
    public void Register_stores_the_waiter_before_send_runs_so_a_reply_made_inside_send_resolves_it()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var handle = MainActor.SpawnThrowing(() => pending.Register("id-1", () => pending.Resolve("id-1", "reply")));
        runtime.RunUntilIdle();
        Assert.Equal("reply", Resolved(handle.Value));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-164")] // Core/Support/PendingReplies.swift:17
    public void Register_throws_CancellationError_and_never_calls_send_when_already_cancelled()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var sendCalled = false;
        var handle = MainActor.SpawnThrowing(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(5));
            return await pending.Register("id-2", () => sendCalled = true);
        });
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.RunUntilIdle();
        Faulted(handle.Value);
        Assert.False(sendCalled);
        Assert.False(pending.Resolve("id-2", "late")); // :40 nothing was ever stored under the id
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-164")] // Core/Support/PendingReplies.swift:21-29
    public void Register_with_no_timeoutValue_fails_with_CancellationError_once_the_timeout_elapses()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var handle = MainActor.SpawnThrowing(() => pending.Register("id-3", () => { }, timeout: TimeSpan.FromSeconds(5)));
        runtime.RunUntilIdle();
        Assert.False(handle.Value.IsCompleted);
        runtime.Advance(TimeSpan.FromSeconds(5));
        Faulted(handle.Value);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-164")] // Core/Support/PendingReplies.swift:21-26
    public void Register_with_a_timeoutValue_resolves_to_it_once_the_timeout_elapses()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var handle = MainActor.SpawnThrowing(() =>
            pending.Register("id-4", () => { }, timeout: TimeSpan.FromSeconds(5), timeoutValue: "fallback"));
        runtime.RunUntilIdle();
        Assert.False(handle.Value.IsCompleted);
        runtime.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("fallback", Resolved(handle.Value));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-164")] // Core/Support/PendingReplies.swift:24-29
    [Trait("Req", "PR-PROVACP-165")] // Core/Support/PendingReplies.swift:40
    public void A_reply_that_arrives_before_the_timeout_wins_and_the_timer_then_finds_nothing()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var handle = MainActor.SpawnThrowing(() =>
            pending.Register("id-5", () => { }, timeout: TimeSpan.FromSeconds(5), timeoutValue: "fallback"));
        runtime.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(2));
        Assert.True(pending.Resolve("id-5", "real"));
        runtime.Advance(TimeSpan.FromSeconds(3)); // the timer fires here; :25's resolve must find nothing
        Assert.Equal("real", Resolved(handle.Value));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-164")] // Core/Support/PendingReplies.swift:31-35
    public void Cancelling_the_caller_while_waiting_removes_the_entry_and_resumes_with_CancellationError()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var handle = MainActor.SpawnThrowing(() => pending.Register("id-6", () => { }));
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.RunUntilIdle();
        Faulted(handle.Value);
        Assert.False(pending.Resolve("id-6", "late")); // the removal already found the entry
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-164")] // Core/Support/PendingReplies.swift:21-29,31-35
    public void The_timeout_task_does_not_inherit_the_callers_cancellation_and_runs_to_the_end_of_its_sleep()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var handle = MainActor.SpawnThrowing(() =>
            pending.Register("id-7", () => { }, timeout: TimeSpan.FromSeconds(5), timeoutValue: "fallback"));
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.RunUntilIdle();
        Faulted(handle.Value); // cancellation wins immediately, without waiting for the 5 s timeout
        // The timer keeps sleeping and fires later; its resolve/fail then finds nothing removed and must not throw
        // or resume anything twice (a second resume on the already-faulted continuation would trap).
        runtime.Advance(TimeSpan.FromSeconds(5));
        Faulted(handle.Value);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-165")] // Core/Support/PendingReplies.swift:39-42
    public void Resolve_resumes_the_waiter_with_the_value_and_reports_true()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var handle = MainActor.SpawnThrowing(() => pending.Register("id-8", () => { }));
        runtime.RunUntilIdle();
        bool reported = false;
        runtime.OnMain(() => reported = pending.Resolve("id-8", "value"));
        Assert.True(reported);
        Assert.Equal("value", Resolved(handle.Value));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-165")] // Core/Support/PendingReplies.swift:40
    public void Resolve_of_an_unknown_id_does_nothing_and_reports_false()
    {
        var pending = new PendingReplies<string>();
        Assert.False(pending.Resolve("nowhere", "value"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-165")] // Core/Support/PendingReplies.swift:46-49
    public void Fail_resumes_the_waiter_with_the_error_and_reports_true()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var handle = MainActor.SpawnThrowing(() => pending.Register("id-9", () => { }));
        runtime.RunUntilIdle();
        var error = new InvalidOperationException("boom");
        bool reported = false;
        runtime.OnMain(() => reported = pending.Fail("id-9", error));
        Assert.True(reported);
        Assert.Same(error, FailedWith(handle.Value));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-165")] // Core/Support/PendingReplies.swift:47
    public void Fail_of_an_unknown_id_does_nothing_and_reports_false()
    {
        var pending = new PendingReplies<string>();
        Assert.False(pending.Fail("nowhere", new InvalidOperationException("boom")));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-165")] // Core/Support/PendingReplies.swift:52-56
    public void ResolveAll_resumes_every_waiter_with_the_value()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var first = MainActor.SpawnThrowing(() => pending.Register("id-a", () => { }));
        var second = MainActor.SpawnThrowing(() => pending.Register("id-b", () => { }));
        runtime.RunUntilIdle();
        runtime.OnMain(() => pending.ResolveAll("done"));
        // :54's dictionary order is unspecified (R-COLL-2): both results equal "done", checked as a set.
        Assert.Equal(["done", "done"], new[] { Resolved(first.Value), Resolved(second.Value) }.OrderBy(v => v));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-165")] // Core/Support/PendingReplies.swift:58-62
    public void FailAll_resumes_every_waiter_with_the_error()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var first = MainActor.SpawnThrowing(() => pending.Register("id-c", () => { }));
        var second = MainActor.SpawnThrowing(() => pending.Register("id-d", () => { }));
        runtime.RunUntilIdle();
        var error = new InvalidOperationException("closed");
        runtime.OnMain(() => pending.FailAll(error));
        Assert.Same(error, FailedWith(first.Value));
        Assert.Same(error, FailedWith(second.Value));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-165")] // Core/Support/PendingReplies.swift:53-54
    public void ResolveAll_empties_the_map_first_so_a_fresh_registration_under_the_same_id_is_not_swept_up()
    {
        using var runtime = new TestRuntime();
        var pending = new PendingReplies<string>();
        var first = MainActor.SpawnThrowing(() => pending.Register("id-e", () => { }));
        runtime.RunUntilIdle();
        runtime.OnMain(() => pending.ResolveAll("first"));
        TaskHandle<string>? second = null;
        runtime.OnMain(() => second = MainActor.SpawnThrowing(() => pending.Register("id-e", () => { })));
        Assert.Equal("first", Resolved(first.Value));
        Assert.False(second!.Value.IsCompleted);
        runtime.OnMain(() => pending.Resolve("id-e", "second"));
        Assert.Equal("second", Resolved(second.Value));
    }
}
