using System.Threading.Channels;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-ASYNC-1 (IMPLEMENTING.md 3.5). Source: draft B (proto-B AsyncStreamTests), on the ED-066 task model.
public sealed class AsyncStreamTests
{
    [Fact]
    [Trait("Rule", "R-ASYNC-1")]
    [Trait("Decision", "ED-069")]
    public async Task Buffers_until_consumed_and_drops_yields_after_finish()
    {
        var (stream, continuation) = AsyncStream<int>.MakeStream();
        Assert.Equal(AsyncStream<int>.YieldResult.Enqueued, continuation.Yield(1));
        continuation.Yield(2);
        continuation.Finish();
        Assert.Equal(AsyncStream<int>.YieldResult.Terminated, continuation.Yield(3));
        var seen = new List<int>();
        await foreach (var value in stream) seen.Add(value);
        Assert.Equal([1, 2], seen);
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-1")]
    public void Cancelling_the_consuming_task_ends_for_await_quietly_and_terminates_the_stream()
    {
        using var runtime = new TestRuntime();
        var (stream, continuation) = AsyncStream<string>.MakeStream();
        var terminations = new List<AsyncStream<string>.Termination>();
        continuation.OnTermination = terminations.Add;
        var seen = new List<string>();
        var reader = MainActor.Spawn(async () =>
        {
            await foreach (var message in stream) seen.Add(message); // for await: the task's own cancellation
            seen.Add("loop ended");
        });
        runtime.RunUntilIdle();
        continuation.Yield("a");
        runtime.RunUntilIdle();
        reader.Cancel();
        runtime.RunUntilIdle();
        Assert.Equal(["a", "loop ended"], seen);
        Assert.True(reader.Completion.IsCompletedSuccessfully);
        Assert.Equal([AsyncStream<string>.Termination.Cancelled], terminations);
        Assert.Equal(AsyncStream<string>.YieldResult.Terminated, continuation.Yield("late"));
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-1")]
    public async Task Channel_ReadAllAsync_throws_on_cancellation()
    {
        // The pitfall: a bare Channel throws where for await returns.
        var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
        using var cancel = new CancellationTokenSource();
        var reading = Task.Run(async () => { await foreach (var _ in channel.Reader.ReadAllAsync(cancel.Token)) { } });
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-1")]
    [Trait("Oracle", "O-11")]
    public async Task Buffered_elements_drain_after_cancellation()
    {
        var (stream, continuation) = AsyncStream<int>.MakeStream();
        continuation.Yield(1);
        continuation.Yield(2);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var seen = new List<int>();
        await foreach (var value in stream.WithCancellation(cancel.Token)) seen.Add(value);
        Assert.Equal([1, 2], seen);
        Assert.Equal(AsyncStream<int>.YieldResult.Terminated, continuation.Yield(3));
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-1")]
    public void The_closure_form_runs_its_build_closure_synchronously()
    {
        var built = false;
        _ = AsyncStream<int>.Create(continuation =>
        {
            built = true;
            continuation.Finish();
        });
        Assert.True(built);
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-1")]
    public void OnTermination_runs_once_before_the_waiting_consumer_wakes()
    {
        using var runtime = new TestRuntime();
        var log = new List<string>();
        var (stream, continuation) = AsyncStream<int>.MakeStream();
        continuation.OnTermination = reason => log.Add("terminated " + reason);
        var reader = MainActor.Spawn(async () =>
        {
            await foreach (var _ in stream) { }
            log.Add("consumer saw end");
        });
        runtime.RunUntilIdle();
        continuation.Finish();
        continuation.Finish();
        reader.Cancel();
        runtime.RunUntilIdle();
        Assert.Equal(["terminated Finished", "consumer saw end"], log);
    }

    [Fact]
    [Trait("Rule", "R-ASYNC-1")]
    public async Task A_second_concurrent_consumer_traps()
    {
        var (stream, _) = AsyncStream<int>.MakeStream();
        var first = stream.GetAsyncEnumerator();
        var pending = first.MoveNextAsync();
        var second = stream.GetAsyncEnumerator();
        await Assert.ThrowsAsync<SwiftTrapException>(async () => await second.MoveNextAsync());
        Assert.False(pending.IsCompleted);
    }
}
