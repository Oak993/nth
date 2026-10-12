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
    public async Task A_bad_draft_does_not_strand_the_next_protected_events()
    {
        using var bus = new Marrow.App.Assistant.AssistantEventBus();
        using var feed = new FeedCollector(bus);
        bus.Publish(new FeedDraft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, DateTimeOffset.MaxValue, "bad")
            { ThreadId = Guid.NewGuid() }); // The coalescing deadline cannot be represented.
        bus.Publish(FeedDraft.About(FeedEventKind.Approval, FeedSeverity.NeedsYou, bus.Now, "approve"));
        bus.Publish(FeedDraft.About(FeedEventKind.Error, FeedSeverity.Error, bus.Now, "error"));
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, bus.FaultCount);
        Assert.Equal(new[] { FeedEventKind.Approval, FeedEventKind.Error }, feed.Lines.Select(line => line.Kind));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-5")]
    public async Task A_fault_in_one_build_keeps_the_pump_delivering_this_batch_and_later_batches()
    {
        using var bus = new Marrow.App.Assistant.AssistantEventBus();
        var queue = (FeedQueue)typeof(Marrow.App.Assistant.AssistantEventBus)
            .GetField("queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus)!;
        queue.Enqueue(FeedDraft.About(FeedEventKind.Error, FeedSeverity.Error, bus.Now, "bad"));
        // Deliberately corrupt one queued draft to exercise the build-fault boundary, not the enqueue guard.
        var ready = (System.Collections.IEnumerable)typeof(FeedQueue)
            .GetField("ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(queue)!;
        var node = ready.Cast<object>().Single();
        node.GetType().GetField("Draft")!.SetValue(node, null);
        queue.Enqueue(FeedDraft.About(FeedEventKind.Approval, FeedSeverity.NeedsYou, bus.Now, "approve"));
        using var feed = new FeedCollector(bus);
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, bus.FaultCount);
        Assert.Equal(FeedEventKind.Approval, Assert.Single(feed.Lines).Kind);
        bus.Publish(FeedDraft.About(FeedEventKind.Error, FeedSeverity.Error, bus.Now, "next"));
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { FeedEventKind.Approval, FeedEventKind.Error }, feed.Lines.Select(line => line.Kind));
        Assert.Equal(new long[] { 1, 2 }, feed.Lines.Select(line => line.Sequence));
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

}
