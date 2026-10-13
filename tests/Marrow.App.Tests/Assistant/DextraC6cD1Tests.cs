using System.Diagnostics;
using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

public sealed class DextraC6cD1Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-c6c-1")]
    public async Task External_disposal_joins_the_running_callback_before_returning()
    {
        using var bus = new AssistantEventBus();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var ended = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        var calls = 0;
        var released = false;
        Exception? disposalFault = null;
        using var listener = bus.Subscribe(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            released = release.Wait(TimeSpan.FromSeconds(10));
            ended.Set();
        });
        var disposer = new Thread(() =>
        {
            started.Set();
            try { listener.Dispose(); }
            catch (Exception error) { disposalFault = error; }
            finally { returned.Set(); }
        }) { IsBackground = true };
        var launched = false;
        try
        {
            bus.Publish(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, bus.Now, "first"));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "The callback never entered.");
            disposer.Start();
            launched = true;
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            // A signal immediately before Dispose alone would admit a scheduling gap. Observe the
            // dedicated disposer actually blocked (its only possible wait is the join) or returned.
            var bound = Stopwatch.StartNew();
            while (!returned.IsSet && (disposer.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0
                && bound.Elapsed < TimeSpan.FromSeconds(10)) Thread.Yield();
            Assert.False(returned.IsSet, "Dispose returned while its callback was still executing.");
            Assert.True((disposer.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, "Dispose did not reach its join.");
            Assert.False(ended.IsSet);
            release.Set();
            Assert.True(returned.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(ended.IsSet, "Dispose returned before the callback ended.");
            Assert.True(released);
            Assert.Null(disposalFault);
            bus.Publish(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, bus.Now, "second"));
            await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.Set();
            if (launched) Assert.True(disposer.Join(TimeSpan.FromSeconds(10)));
        }
    }
}
