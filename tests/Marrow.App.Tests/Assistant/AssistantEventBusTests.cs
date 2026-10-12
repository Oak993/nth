using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// AS-2 (DESIGN.md 5.1 The bus, Backpressure): a publish is one O(1) enqueue and runs nothing else; subscribers run off the
// publishing thread, in order per thread; token and progress events fold; a burst of 50,000 events loses no needs-you
// event. The privacy rules (5.4) are applied on the way out. Expected lines are written out here.
public sealed class AssistantEventBusTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static FeedDraft Quiet(Guid thread, DateTimeOffset at, string lead) =>
        FeedBusSamples.Draft(FeedEventKind.ToolStarted, FeedSeverity.Quiet, at, lead, thread);

    private static FeedDraft Ask(Guid thread, DateTimeOffset at, string lead) =>
        FeedBusSamples.Draft(FeedEventKind.Approval, FeedSeverity.NeedsYou, at, lead, thread);

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-6")]
    public void A_publish_with_nobody_listening_queues_nothing()
    {
        using var bus = new AssistantEventBus();
        Assert.False(bus.HasSubscribers);
        for (var i = 0; i < 100; i++) bus.Publish(Ask(A, FeedBusSamples.Start, "ask"));
        Assert.Equal(0, bus.Pending);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-6")]
    public async Task Lines_reach_the_subscribers_on_the_buss_own_task_never_on_the_publishing_thread()
    {
        using var bus = new AssistantEventBus();
        using var collector = new FeedCollector(bus);
        var publisher = Environment.CurrentManagedThreadId;

        bus.Publish(Ask(A, FeedBusSamples.Start, "ask"));
        await collector.UntilAsync(lines => lines.Count == 1);

        Assert.DoesNotContain(publisher, collector.HandlerThreads);
        Assert.Single(collector.HandlerThreads);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-6")]
    public async Task Lines_of_one_thread_arrive_in_the_order_they_were_published_and_are_numbered_in_delivery_order()
    {
        using var bus = new AssistantEventBus();
        using var collector = new FeedCollector(bus);
        for (var i = 0; i < 300; i++)
        {
            bus.Publish(Quiet(i % 2 == 0 ? A : B, FeedBusSamples.Start.AddMilliseconds(i), $"tool {i}"));
            if (i % 50 == 0) bus.Publish(Ask(i % 4 == 0 ? A : B, FeedBusSamples.Start.AddMilliseconds(i), $"ask {i}"));
        }
        await bus.FlushAsync();
        var lines = await collector.UntilAsync(seen => seen.Count == 306);

        foreach (var thread in new[] { A, B })
        {
            var times = lines.Where(line => line.ThreadId == thread).Select(line => line.Time).ToList();
            Assert.Equal(times.Order().ToList(), times);
        }
        Assert.Equal(Enumerable.Range(1, 306).Select(i => (long)i), lines.Select(line => line.Sequence));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-6")]
    public async Task A_subscriber_that_throws_costs_the_others_nothing()
    {
        using var bus = new AssistantEventBus();
        using var bad = bus.Subscribe(_ => throw new InvalidOperationException("a broken subscriber"));
        using var good = new FeedCollector(bus);

        bus.Publish(Ask(A, FeedBusSamples.Start, "ask"));
        await good.UntilAsync(lines => lines.Count == 1);
        bus.Publish(Ask(A, FeedBusSamples.Start.AddSeconds(1), "ask again"));
        await good.UntilAsync(lines => lines.Count == 2);

        Assert.Equal(2, bus.FaultCount);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-6")]
    public async Task A_subscription_that_is_disposed_hears_nothing_more()
    {
        using var bus = new AssistantEventBus();
        var keeper = new FeedCollector(bus);
        var leaver = new FeedCollector(bus);
        bus.Publish(Ask(A, FeedBusSamples.Start, "one"));
        await keeper.UntilAsync(lines => lines.Count == 1);
        await leaver.UntilAsync(lines => lines.Count == 1);

        leaver.Dispose();
        bus.Publish(Ask(A, FeedBusSamples.Start.AddSeconds(1), "two"));
        await keeper.UntilAsync(lines => lines.Count == 2);

        Assert.Single(leaver.Lines);
        keeper.Dispose();
        Assert.False(bus.HasSubscribers);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public async Task Token_events_of_a_thread_fold_into_one_line_when_the_two_second_window_ends()
    {
        var clock = new ManualTimeProvider(FeedBusSamples.Start);
        using var bus = new AssistantEventBus(clock);
        using var collector = new FeedCollector(bus);
        var facts = FeedBusSamples.Facts();
        for (var i = 0; i < 20; i++)
            bus.Publish(FeedBusSamples.Draft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, clock.GetUtcNow(), "", A, null, facts, 100));
        bus.Publish(Ask(B, clock.GetUtcNow(), "a marker from another thread"));
        await collector.UntilAsync(lines => lines.Count == 1);
        Assert.Equal(FeedEventKind.Approval, collector.Lines[0].Kind);

        clock.Advance(TimeSpan.FromSeconds(2));
        var lines = await collector.UntilAsync(seen => seen.Count == 2);

        Assert.Equal("Fix the parser: used 2,000 tokens", lines[1].Summary);
        Assert.Equal((2_000L, 20, FeedBusSamples.Start), (lines[1].CostDelta, lines[1].Count, lines[1].Time));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public async Task A_flush_ends_the_windows_and_completes_when_everything_published_before_it_was_delivered()
    {
        var clock = new ManualTimeProvider(FeedBusSamples.Start);
        using var bus = new AssistantEventBus(clock);
        using var collector = new FeedCollector(bus);
        var facts = FeedBusSamples.Facts();
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, clock.GetUtcNow(), "", A, null, facts, 7));
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.AgentProgress, FeedSeverity.Quiet, clock.GetUtcNow(), "helper working", B, "reading", facts, 0, "h1"));

        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, collector.Lines.Count);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public async Task The_owners_switches_decide_what_words_reach_the_subscribers()
    {
        using var bus = new AssistantEventBus();
        using var collector = new FeedCollector(bus);
        var codexChat = FeedBusSamples.Facts("Fix the parser", ProviderKind.Codex);
        FeedDraft Finished(double seconds) => FeedBusSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, FeedBusSamples.Start.AddSeconds(seconds),
            "finished", A, "All 14 tests pass.", codexChat);

        bus.Publish(Finished(0));
        await bus.FlushAsync();
        bus.Brain = new FeedBrain(ProviderKind.Claude);
        bus.Publish(Finished(1));
        await bus.FlushAsync();
        bus.Brain = new FeedBrain(ProviderKind.Claude, AlsoOtherProviders: true);
        bus.Publish(Finished(2));
        await bus.FlushAsync();
        bus.Brain = new FeedBrain(ProviderKind.Codex);
        bus.Publish(Finished(3));
        await bus.FlushAsync();

        Assert.Equal(["Fix the parser: finished", "Fix the parser: finished", "Fix the parser: finished. All 14 tests pass.", "Fix the parser: finished. All 14 tests pass."],
            collector.Lines.Select(line => line.Summary));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public async Task A_hidden_chat_reaches_the_subscribers_only_as_a_fixed_line_with_no_id()
    {
        using var bus = new AssistantEventBus();
        using var collector = new FeedCollector(bus);
        bus.Brain = new FeedBrain(ProviderKind.Claude, AlsoOtherProviders: true);
        var hidden = FeedBusSamples.Facts("SECRET PLAN", ProviderKind.Claude, hidden: true);
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.ToolStarted, FeedSeverity.Quiet, FeedBusSamples.Start, "running a command", A, "rm -rf plans", hidden));
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, FeedBusSamples.Start, "", A, null, hidden, 500));
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.Approval, FeedSeverity.NeedsYou, FeedBusSamples.Start, "waiting for approval", A, "Delete the SECRET PLAN?", hidden));
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, FeedBusSamples.Start, "finished", A, "SECRET PLAN is done", hidden));
        await bus.FlushAsync();

        var lines = collector.Lines;
        Assert.Equal(["A hidden chat needs you", "A hidden chat finished a turn"], lines.Select(line => line.Summary));
        Assert.All(lines, line => Assert.Null(line.ThreadId));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public async Task A_chat_hidden_while_its_words_were_still_waiting_loses_them()
    {
        var clock = new ManualTimeProvider(FeedBusSamples.Start);
        using var bus = new AssistantEventBus(clock);
        bus.Brain = new FeedBrain(ProviderKind.Claude);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var collector = new FeedCollector(bus)
        {
            Hook = _ =>
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(60));
            },
        };
        bus.Publish(new FeedDraft(FeedEventKind.MissionState, FeedSeverity.Info, clock.GetUtcNow(), "A first line holds the subscriber"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var facts = FeedBusSamples.Facts("Fix the parser");
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.AgentProgress, FeedSeverity.Quiet, clock.GetUtcNow(), "helper working", A, "reading the SECRET PLAN", facts, 0, "h1"));
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, clock.GetUtcNow(), "finished", B, "SECRET PLAN done", facts));
        bus.Publish(FeedBusSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, clock.GetUtcNow(), "finished", Guid.NewGuid(), "a plain chat is done", facts));

        bus.Hide(A);
        bus.Hide(B);
        release.Set();
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["A first line holds the subscriber", "A hidden chat finished a turn", "Fix the parser: finished. a plain chat is done"],
            collector.Lines.Select(line => line.Summary));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public async Task A_burst_of_50000_events_costs_the_publisher_one_enqueue_each_and_loses_no_needs_you_event()
    {
        const int Total = 50_000;
        var threads = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        var facts = FeedBusSamples.Facts();
        var drafts = new FeedDraft[Total];
        var asks = 0;
        for (var i = 0; i < Total; i++)
        {
            var thread = threads[i % threads.Length];
            var at = FeedBusSamples.Start.AddMilliseconds(i);
            if (i % 500 == 0)
            {
                drafts[i] = FeedBusSamples.Draft(FeedEventKind.Approval, FeedSeverity.NeedsYou, at, "waiting for approval", thread, $"ask {i}", facts);
                asks++;
            }
            else if (i % 7 == 0) drafts[i] = FeedBusSamples.Draft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, at, "", thread, null, facts, 10);
            else if (i % 11 == 0) drafts[i] = FeedBusSamples.Draft(FeedEventKind.AgentProgress, FeedSeverity.Quiet, at, "helper working", thread, $"note {i}", facts, 0, "h");
            else drafts[i] = FeedBusSamples.Draft(FeedEventKind.ToolStarted, FeedSeverity.Quiet, at, "running a command", thread, null, facts);
        }

        using var bus = new AssistantEventBus();
        bus.Brain = new FeedBrain(ProviderKind.Claude);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var collector = new FeedCollector(bus)
        {
            Hook = _ =>
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(60));
            },
        };
        bus.Publish(Ask(threads[0], FeedBusSamples.Start.AddMilliseconds(-1), "primer"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The subscriber is stalled. The publishing thread must not wait for it, run it, or build any line.
        var publisher = Environment.CurrentManagedThreadId;
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var draft in drafts) bus.Publish(draft);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(publisher, Environment.CurrentManagedThreadId);
        Assert.True(allocated / Total < 1_024, $"{allocated / Total} bytes allocated per publish");
        Assert.True(bus.Pending is > 0 and <= FeedQueue.DefaultLimit, $"{bus.Pending} drafts pending");
        Assert.Empty(collector.Lines);

        release.Set();
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(60));
        var lines = collector.Lines;

        Assert.DoesNotContain(publisher, collector.HandlerThreads);
        Assert.Equal(asks + 1, lines.Count(line => line.Kind == FeedEventKind.Approval));
        Assert.Equal(Total + 1, lines.Sum(line => (long)line.Count) + bus.DroppedCount);
        Assert.True(bus.DroppedCount > 0);
        foreach (var group in lines.Where(line => line.ThreadId is not null).GroupBy(line => line.ThreadId))
        {
            var times = group.Select(line => line.Time).ToList();
            Assert.Equal(times.Order().ToList(), times);
        }
    }
}
