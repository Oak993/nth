using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Providers;
using System.Reflection;

namespace Marrow.App.Tests.Assistant;

// Regression for the accepted Dextra C6-F5 finding. Offline only.
public sealed class DextraC6F5Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-5")]
    public async Task Crossing_the_coalescing_deadline_during_timeout_creation_keeps_the_pump_alive()
    {
        var start = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        using var bus = new Marrow.App.Assistant.AssistantEventBus(new C6BoundaryTimeProvider(start.AddSeconds(2)));
        var queue = (FeedQueue)typeof(Marrow.App.Assistant.AssistantEventBus).GetField("queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus)!;
        queue.Enqueue(new FeedDraft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, start, "") { ThreadId = Guid.NewGuid() });
        var wait = (Task)typeof(Marrow.App.Assistant.AssistantEventBus).GetMethod("WaitForWorkAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bus, [CancellationToken.None])!;
        await wait.WaitAsync(TimeSpan.FromSeconds(10));
    }
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-5")]
    [Trait("Decision", "ED-NEW-dextra-c6c-4")]
    public async Task A_bad_draft_does_not_strand_the_next_protected_events()
    {
        // Separately pin the overdue clamp. This phase already passed on the base code,
        // but fails when only the fixed zero clamp is removed.
        using (var overdue = new Marrow.App.Assistant.AssistantEventBus(new C6TimerRangeTimeProvider()))
        {
            var queue = Queue(overdue);
            var start = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
            queue.Enqueue(new FeedDraft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, start.AddSeconds(-5), "")
                { ThreadId = Guid.NewGuid() });
            var wait = (Task)typeof(Marrow.App.Assistant.AssistantEventBus)
                .GetMethod("WaitForWorkAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(overdue, [CancellationToken.None])!;
            await wait.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var stamp = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        using var clock = new C6cPumpBoundaryTimeProvider(stamp.AddSeconds(2));
        using var bus = new Marrow.App.Assistant.AssistantEventBus(clock);
        HoldHiddenDraft(bus, stamp);
        using var feed = new FeedCollector(bus);
        try
        {
            await clock.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
            bus.Publish(new FeedDraft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, DateTimeOffset.MaxValue, "bad")
                { ThreadId = Guid.NewGuid() }); // Retain the existing enqueue-fault assertion.
            bus.Publish(FeedDraft.About(FeedEventKind.Approval, FeedSeverity.NeedsYou, stamp, "approve"));
            bus.Publish(FeedDraft.About(FeedEventKind.Error, FeedSeverity.Error, stamp, "error"));
            clock.ReleaseFirstRead();
            await AssertPumpReachedTimer(bus, clock); // Observe the real wait before Flush releases its deadline.
            await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, bus.FaultCount);
            Assert.Equal(new[] { FeedEventKind.Approval, FeedEventKind.Error }, feed.Lines.Select(line => line.Kind));
        }
        finally { clock.ReleaseFirstRead(); }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-5")]
    [Trait("Decision", "ED-NEW-dextra-c6c-4")]
    public async Task A_fault_in_one_build_keeps_the_pump_delivering_this_batch_and_later_batches()
    {
        var stamp = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        using var clock = new C6cPumpBoundaryTimeProvider(stamp.AddSeconds(2));
        using var bus = new Marrow.App.Assistant.AssistantEventBus(clock);
        var queue = Queue(bus);
        queue.Enqueue(FeedDraft.About(FeedEventKind.Error, FeedSeverity.Error, stamp, "bad"));
        // Retain the actual build-fault boundary and its existing fault/sequence assertions.
        var ready = (System.Collections.IEnumerable)typeof(FeedQueue)
            .GetField("ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(queue)!;
        var node = ready.Cast<object>().Single();
        node.GetType().GetField("Draft")!.SetValue(node, null);
        queue.Enqueue(FeedDraft.About(FeedEventKind.Approval, FeedSeverity.NeedsYou, stamp, "approve"));
        HoldHiddenDraft(bus, stamp);
        using var feed = new FeedCollector(bus);
        try
        {
            await clock.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.ReleaseFirstRead();
            await AssertPumpReachedTimer(bus, clock);
            await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, bus.FaultCount);
            Assert.Equal(FeedEventKind.Approval, Assert.Single(feed.Lines).Kind);
            bus.Publish(FeedDraft.About(FeedEventKind.Error, FeedSeverity.Error, stamp, "next"));
            await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new[] { FeedEventKind.Approval, FeedEventKind.Error }, feed.Lines.Select(line => line.Kind));
            Assert.Equal(new long[] { 1, 2 }, feed.Lines.Select(line => line.Sequence));
        }
        finally { clock.ReleaseFirstRead(); }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-5")]
    public async Task A_far_future_coalescing_stamp_cannot_exceed_the_timeout_range()
    {
        var clock = new C6TimerRangeTimeProvider();
        using var bus = new Marrow.App.Assistant.AssistantEventBus(clock);
        var queue = (FeedQueue)typeof(Marrow.App.Assistant.AssistantEventBus)
            .GetField("queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus)!;
        queue.Enqueue(new FeedDraft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, clock.GetUtcNow().AddYears(10), "")
            { ThreadId = Guid.NewGuid() });
        var wait = (Task)typeof(Marrow.App.Assistant.AssistantEventBus)
            .GetMethod("WaitForWorkAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bus, [CancellationToken.None])!;
        await wait.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromMilliseconds(4294967294), clock.LastDelay);
    }

    private static FeedQueue Queue(Marrow.App.Assistant.AssistantEventBus bus) =>
        (FeedQueue)typeof(Marrow.App.Assistant.AssistantEventBus)
            .GetField("queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus)!;

    private static void HoldHiddenDraft(Marrow.App.Assistant.AssistantEventBus bus, DateTimeOffset stamp) =>
        Queue(bus).Enqueue(new FeedDraft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, stamp, "")
        {
            ThreadId = Guid.NewGuid(), CostDelta = 1,
            Chat = new FeedChatFacts("hidden", ProviderKind.Claude, true, false, false),
        });

    private static async Task AssertPumpReachedTimer(Marrow.App.Assistant.AssistantEventBus bus, C6cPumpBoundaryTimeProvider clock)
    {
        var pump = (Task)typeof(Marrow.App.Assistant.AssistantEventBus)
            .GetField("pump", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus)!;
        var winner = await Task.WhenAny(clock.TimerRequested.Task, pump).WaitAsync(TimeSpan.FromSeconds(10));
        if (winner == pump) await pump; // Surface the base negative-timeout fault rather than a timeout.
        await clock.TimerRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromTicks(1), clock.FirstDelay);
    }
}
