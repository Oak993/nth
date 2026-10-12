using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Providers;
using System.Reflection;

namespace Marrow.App.Tests.Assistant;

// Regression for the accepted Dextra C6-F9 finding. Offline only.
public sealed class DextraC6F9Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-9")]
    public async Task Dispose_completes_a_flush_already_captured_by_the_pump()
    {
        var clock = new C6FlushTimeProvider();
        using var bus = new Marrow.App.Assistant.AssistantEventBus(clock);
        var stamp = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        var queue = (FeedQueue)typeof(Marrow.App.Assistant.AssistantEventBus).GetField("queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus)!;
        queue.Enqueue(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, stamp, "first"));
        clock.AfterFlushCaptured = () => bus.Publish(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, stamp, "second"));
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        var enteredFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var subscription = bus.Subscribe(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1) { enteredFirst.TrySetResult(); releaseFirst.Wait(TimeSpan.FromSeconds(10)); }
            else { enteredSecond.TrySetResult(); releaseSecond.Wait(TimeSpan.FromSeconds(10)); }
        });
        await enteredFirst.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pending = Task.CompletedTask;
        var pendingLater = Task.CompletedTask;
        try
        {
            pending = bus.FlushAsync();
            releaseFirst.Set();
            await enteredSecond.Task.WaitAsync(TimeSpan.FromSeconds(10));
            pendingLater = bus.FlushAsync();
            bus.Dispose();
            Assert.True(pendingLater.IsCompleted, "Dispose left the newly registered flush waiting.");
            Assert.True(pending.IsCompleted, "Dispose returned while a captured flush task was still waiting.");
        }
        finally
        {
            releaseFirst.Set();
            releaseSecond.Set();
            await Task.WhenAll(pending, pendingLater).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
