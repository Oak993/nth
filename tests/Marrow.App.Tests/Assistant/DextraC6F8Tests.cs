using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Providers;
using System.Reflection;

namespace Marrow.App.Tests.Assistant;

// Regression for the accepted Dextra C6-F8 finding. Offline only.
public sealed class DextraC6F8Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-8")]
    public async Task Unsubscribing_during_an_earlier_listener_stops_the_later_listener()
    {
        using var bus = new Marrow.App.Assistant.AssistantEventBus();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = bus.Subscribe(_ => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); });
        var calls = 0;
        using var later = bus.Subscribe(_ => Interlocked.Increment(ref calls));
        bus.Publish(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, bus.Now, "primer"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { later.Dispose(); }
        finally { release.Set(); }
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, Volatile.Read(ref calls));
    }
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-8")]
    public async Task A_handler_can_dispose_its_own_subscription_without_blocking_other_listeners()
    {
        using var bus = new Marrow.App.Assistant.AssistantEventBus();
        IDisposable? self = null;
        var calls = 0;
        self = bus.Subscribe(_ => { Interlocked.Increment(ref calls); self!.Dispose(); });
        using var feed = new FeedCollector(bus);
        bus.Publish(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, bus.Now, "first"));
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        bus.Publish(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, bus.Now, "second"));
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, calls);
        Assert.Equal(2, feed.Lines.Count);
        self.Dispose();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-8")]
    public async Task Whole_bus_disposal_retires_later_captured_listeners_without_waiting_on_their_gates()
    {
        using var bus = new Marrow.App.Assistant.AssistantEventBus();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = bus.Subscribe(_ => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); });
        var calls = 0;
        using var later = bus.Subscribe(_ => Interlocked.Increment(ref calls));
        bus.Publish(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, bus.Now, "primer"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Task.Run(bus.Dispose).WaitAsync(TimeSpan.FromSeconds(8));
            release.Set();
            var pump = (Task)typeof(Marrow.App.Assistant.AssistantEventBus)
                .GetField("pump", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus)!;
            await pump.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, Volatile.Read(ref calls));
        }
        finally { release.Set(); }
    }

}
