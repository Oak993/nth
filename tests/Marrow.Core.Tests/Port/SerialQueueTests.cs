using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-GCD-1, R-LOCK-1 (IMPLEMENTING.md 3.6). Source: drafts A and B.
public sealed class SerialQueueTests
{
    [Fact]
    [Trait("Rule", "R-GCD-1")]
    [Trait("Decision", "ED-069")]
    public void Serial_queue_is_fifo_and_sync_on_itself_traps()
    {
        var queue = new SerialQueue("droppycode.test");
        var order = new List<int>();
        for (var i = 0; i < 100; i++)
        {
            var n = i;
            queue.Async(() => order.Add(n));
        }
        queue.Sync(() => { }); // waitForQueuedWrites()
        Assert.Equal(Enumerable.Range(0, 100), order);
        var error = queue.Sync(() => Record.Exception(() => queue.Sync(() => { })));
        Assert.IsType<SwiftTrapException>(error);
    }

    [Fact]
    [Trait("Rule", "R-GCD-1")]
    public void Disk_writer_coalesces_a_backlog_to_the_newest_snapshot()
    {
        var writer = new DiskWriterSample();
        var unblock = new TaskCompletionSource();
        writer.Block(unblock.Task);
        writer.EncodeAndWrite("v1", "library.json");
        writer.EncodeAndWrite("v2", "library.json");
        writer.EncodeAndWrite("v3", "library.json");
        unblock.SetResult();
        writer.WaitForQueuedWrites();
        Assert.Equal(["v3"], writer.Written);
    }

    [Fact]
    [Trait("Rule", "R-GCD-2")]
    public async Task Global_asyncAfter_runs_on_the_pool_after_the_delay_outside_any_task()
    {
        using var runtime = new TestRuntime();
        var ran = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.OnMain(() => MainActor.Spawn(() =>
        {
            GlobalQueue.AsyncAfter(TimeSpan.FromSeconds(1), () => ran.SetResult(SwiftTask.IsInTask || MainActor.IsIsolated));
            return Task.CompletedTask;
        }));
        Assert.False(ran.Task.IsCompleted);
        runtime.Advance(TimeSpan.FromSeconds(1));
        Assert.False(await ran.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    [Trait("Rule", "R-GCD-2")]
    public void A_serial_queue_asyncAfter_item_cancelled_before_it_fires_never_runs()
    {
        using var runtime = new TestRuntime();
        var queue = new SerialQueue("MCPOAuth.callback");
        var ran = 0;
        var item = queue.AsyncAfter(TimeSpan.FromSeconds(120), () => ran++);
        item.Cancel();
        runtime.Advance(TimeSpan.FromSeconds(120));
        queue.Sync(() => { });
        Assert.Equal(0, ran);
        queue.AsyncAfter(TimeSpan.FromSeconds(1), () => ran++);
        runtime.Advance(TimeSpan.FromSeconds(1));
        queue.Sync(() => { });
        Assert.Equal(1, ran);
    }

    [Fact]
    [Trait("Rule", "R-LOCK-1")]
    [Trait("Decision", "ED-069")]
    public void Mutex_reentry_traps_where_a_plain_Lock_would_succeed()
    {
        var plain = new Lock();
        var reentered = false;
        lock (plain) lock (plain) reentered = true;
        Assert.True(reentered); // the pitfall: .NET locks are re-entrant

        var mutex = new Mutex<int>(0);
        Assert.Throws<SwiftTrapException>(() => mutex.WithLock((ref int outer) => mutex.WithLock((ref int inner) => inner++)));
        Assert.Equal(1, mutex.WithLock((ref int v) => ++v));
    }
}
