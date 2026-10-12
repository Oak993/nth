using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Assistant;

// AS-2 (DESIGN.md 5.1 Backpressure): ordering per thread, coalescing, and what is dropped when the queue is over its limit.
// The queue is driven with a time the test passes in, so nothing here waits on a clock.
public sealed class FeedQueueTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    private static readonly DateTimeOffset Far = FeedSamples.Start.AddDays(1);

    private static List<FeedDraft> Take(FeedQueue queue, double seconds)
    {
        var taken = new List<FeedDraft>();
        queue.TakeReady(FeedSamples.Start.AddSeconds(seconds), taken);
        return taken;
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Drafts_of_one_thread_come_out_in_the_order_they_went_in_even_with_folded_kinds_between()
    {
        var queue = new FeedQueue();
        var threads = new[] { A, B, C };
        for (var i = 0; i < 300; i++)
        {
            var thread = threads[i % 3];
            var seconds = i * 0.4;
            queue.Enqueue((i % 7) switch
            {
                0 => FeedSamples.Tokens(thread, seconds, 10),
                1 => FeedSamples.Progress(thread, seconds, $"note {i}"),
                2 => FeedSamples.NeedsYou(thread, seconds, $"ask {i}"),
                3 => FeedSamples.Info(thread, seconds, $"done {i}"),
                _ => FeedSamples.Quiet(thread, seconds, $"tool {i}"),
            });
        }
        queue.ReleaseAll();
        var taken = Take(queue, 86_400);
        Assert.NotEmpty(taken);
        foreach (var thread in threads)
        {
            var times = taken.Where(d => d.ThreadId == thread).Select(d => d.Time).ToList();
            Assert.Equal(times.Order().ToList(), times);
        }
        Assert.Equal(300, taken.Sum(d => d.Count));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Token_drafts_of_one_thread_fold_into_one_when_the_two_second_window_ends()
    {
        var queue = new FeedQueue();
        for (var i = 0; i < 100; i++) queue.Enqueue(FeedSamples.Tokens(A, i * 0.019, 10));

        Assert.Empty(Take(queue, 1.5));
        Assert.Equal(1, queue.HeldCount);
        Assert.Equal(FeedSamples.Start.AddSeconds(2), queue.NextDue);

        var only = Assert.Single(Take(queue, 2));
        Assert.Equal((1_000L, 100, FeedSamples.Start), (only.CostDelta, only.Count, only.Time));
        Assert.Null(queue.NextDue);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void A_token_draft_at_the_end_of_the_window_opens_the_next_one_so_a_thread_speaks_once_in_two_seconds()
    {
        var queue = new FeedQueue();
        var heard = new List<(double At, FeedDraft Draft)>();
        foreach (var seconds in new[] { 0.0, 1.0, 2.0, 3.0, 3.9, 4.0, 6.0 })
        {
            queue.Enqueue(FeedSamples.Tokens(A, seconds, 5));
            heard.AddRange(Take(queue, seconds).Select(d => (seconds, d)));
        }

        Assert.Equal([2.0, 4.0, 6.0], heard.Select(h => h.At));
        Assert.Equal([(10L, 2), (15L, 3), (5L, 1)], heard.Select(h => (h.Draft.CostDelta, h.Draft.Count)));
        Assert.Equal([0.0, 2.0, 4.0], heard.Select(h => (h.Draft.Time - FeedSamples.Start).TotalSeconds));
        Assert.Single(Take(queue, 8));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Any_other_draft_of_the_thread_ends_its_window_and_comes_after_the_folded_one()
    {
        var queue = new FeedQueue();
        queue.Enqueue(FeedSamples.Tokens(A, 0, 10));
        queue.Enqueue(FeedSamples.Tokens(A, 0.2, 10));
        queue.Enqueue(FeedSamples.NeedsYou(A, 0.5, "ask"));

        var taken = Take(queue, 0.5);
        Assert.Equal([FeedEventKind.TokenSpend, FeedEventKind.Approval], taken.Select(d => d.Kind));
        Assert.Equal(20L, taken[0].CostDelta);
        Assert.Equal(0, queue.HeldCount);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void A_draft_of_another_thread_leaves_the_window_open_and_is_not_held_up_by_it()
    {
        var queue = new FeedQueue();
        queue.Enqueue(FeedSamples.Tokens(A, 0, 10));
        queue.Enqueue(FeedSamples.NeedsYou(B, 0.1, "ask"));
        queue.Enqueue(FeedSamples.Tokens(A, 0.3, 10));

        var taken = Take(queue, 0.4);
        Assert.Equal([FeedEventKind.Approval], taken.Select(d => d.Kind));
        Assert.Equal(1, queue.HeldCount);
        var later = Assert.Single(Take(queue, 2));
        Assert.Equal((20L, 2), (later.CostDelta, later.Count));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Progress_keeps_the_newest_note_and_counts_the_ones_it_replaced()
    {
        var queue = new FeedQueue();
        queue.Enqueue(FeedSamples.Progress(A, 0, "reading"));
        queue.Enqueue(FeedSamples.Progress(A, 0.5, "editing"));
        queue.Enqueue(FeedSamples.Progress(A, 1.0, "testing"));

        var only = Assert.Single(Take(queue, 2));
        Assert.Equal(("testing", 3, FeedSamples.Start), (only.Detail, only.Count, only.Time));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Progress_of_two_heads_in_one_thread_is_not_folded_together_and_keeps_its_order()
    {
        var queue = new FeedQueue();
        queue.Enqueue(FeedSamples.Progress(A, 0, "one", "head-1"));
        queue.Enqueue(FeedSamples.Progress(A, 0.1, "two", "head-2"));
        queue.Enqueue(FeedSamples.Progress(A, 0.2, "three", "head-1"));

        queue.ReleaseAll();
        var taken = Take(queue, 0.2);
        Assert.Equal(["one", "two", "three"], taken.Select(d => d.Detail));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Releasing_everything_delivers_the_folded_drafts_at_once()
    {
        var queue = new FeedQueue();
        queue.Enqueue(FeedSamples.Tokens(A, 0, 10));
        queue.Enqueue(FeedSamples.Progress(B, 0, "note"));
        Assert.Empty(Take(queue, 0.1));

        queue.ReleaseAll();
        Assert.Equal(2, Take(queue, 0.1).Count);
        Assert.Null(queue.NextDue);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Over_the_limit_the_oldest_quiet_drafts_go_first()
    {
        var queue = new FeedQueue(100);
        for (var i = 0; i < 150; i++) queue.Enqueue(FeedSamples.Quiet(A, i, $"q{i}"));

        Assert.Equal(100, queue.Count);
        Assert.Equal(50, queue.DroppedCount);
        var taken = Take(queue, 1_000);
        Assert.Equal(Enumerable.Range(50, 100).Select(i => $"q{i}"), taken.Select(d => d.Lead));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Needs_you_and_error_drafts_are_never_dropped_however_many_quiet_ones_follow()
    {
        var queue = new FeedQueue(100);
        for (var i = 0; i < 10; i++) queue.Enqueue(FeedSamples.NeedsYou(A, i, $"ask{i}"));
        queue.Enqueue(FeedSamples.Draft(FeedEventKind.Error, FeedSeverity.Error, 10, "boom", A));
        for (var i = 0; i < 500; i++) queue.Enqueue(FeedSamples.Quiet(B, 11 + i, $"q{i}"));

        Assert.Equal(100, queue.Count);
        var taken = Take(queue, 10_000);
        Assert.Equal(11, taken.Count(d => d.Severity is FeedSeverity.NeedsYou or FeedSeverity.Error));
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"ask{i}"), taken.Where(d => d.Severity == FeedSeverity.NeedsYou).Select(d => d.Lead));
        Assert.Equal(Enumerable.Range(411, 89).Select(i => $"q{i}"), taken.Where(d => d.Severity == FeedSeverity.Quiet).Select(d => d.Lead));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void More_needs_you_drafts_than_the_limit_are_all_kept()
    {
        var queue = new FeedQueue(100);
        for (var i = 0; i < 250; i++) queue.Enqueue(FeedSamples.NeedsYou(A, i, $"ask{i}"));
        for (var i = 0; i < 50; i++) queue.Enqueue(FeedSamples.Quiet(B, 300 + i, $"q{i}"));

        Assert.Equal(250, queue.Count);
        var taken = Take(queue, 10_000);
        Assert.Equal(250, taken.Count);
        Assert.All(taken, d => Assert.Equal(FeedSeverity.NeedsYou, d.Severity));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void Info_drafts_are_dropped_only_after_every_quiet_one_is_gone()
    {
        var queue = new FeedQueue(10);
        for (var i = 0; i < 30; i++) queue.Enqueue(i % 6 == 0 ? FeedSamples.Info(A, i, $"i{i}") : FeedSamples.Quiet(B, i, $"q{i}"));

        var taken = Take(queue, 1_000);
        Assert.Equal(10, taken.Count);
        Assert.Equal(5, taken.Count(d => d.Severity == FeedSeverity.Info));
        for (var i = 0; i < 40; i++) queue.Enqueue(FeedSamples.Info(A, 100 + i, $"j{i}"));
        Assert.Equal(10, queue.Count);
        Assert.Equal(Enumerable.Range(30, 10).Select(i => $"j{i}"), Take(queue, 1_000).Select(d => d.Lead));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public void A_dropped_folded_draft_counts_every_event_it_stood_for_and_leaves_no_window_behind()
    {
        var queue = new FeedQueue(2);
        for (var i = 0; i < 5; i++) queue.Enqueue(FeedSamples.Tokens(A, i * 0.1, 10));
        queue.Enqueue(FeedSamples.Quiet(B, 1, "b1"));
        queue.Enqueue(FeedSamples.Quiet(B, 2, "b2"));
        queue.Enqueue(FeedSamples.Quiet(B, 3, "b3"));

        Assert.Equal(6, queue.DroppedCount);
        Assert.Equal(0, queue.HeldCount);
        queue.Enqueue(FeedSamples.Tokens(A, 4, 10));
        Assert.Equal(1, queue.HeldCount);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void Hiding_a_chat_takes_the_words_out_of_the_drafts_waiting_for_it()
    {
        var queue = new FeedQueue();
        var facts = FeedSamples.Facts("Plans");
        queue.Enqueue(FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 0, "finished", A, "secret plan words", facts));
        queue.Enqueue(FeedSamples.Draft(FeedEventKind.Error, FeedSeverity.Error, 0, "hit an error", B, "other words", facts));
        queue.Enqueue(FeedSamples.Draft(FeedEventKind.AgentProgress, FeedSeverity.Quiet, 0, "helper working", A, "more words", facts, 0, "h"));

        Assert.Equal(2, queue.Hide(A));
        queue.ReleaseAll();
        var taken = Take(queue, 1);
        Assert.All(taken.Where(d => d.ThreadId == A), d =>
        {
            Assert.Null(d.Detail);
            Assert.True(d.Chat!.Hidden);
        });
        Assert.Equal("other words", taken.Single(d => d.ThreadId == B).Detail);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public async Task Several_threads_filling_the_queue_at_once_lose_no_needs_you_draft_and_keep_each_threads_order()
    {
        const int Producers = 4;
        const int Each = 5_000;
        var queue = new FeedQueue(1_000);
        var tasks = Enumerable.Range(0, Producers).Select(p => Task.Run(() =>
        {
            var thread = Guid.NewGuid();
            for (var i = 0; i < Each; i++)
                queue.Enqueue(i % 50 == 0 ? FeedSamples.NeedsYou(thread, i, $"{p}:{i}") : FeedSamples.Quiet(thread, i, $"{p}:{i}"));
        })).ToArray();
        await Task.WhenAll(tasks);

        var taken = Take(queue, 100_000);
        Assert.Equal(Producers * Each / 50, taken.Count(d => d.Severity == FeedSeverity.NeedsYou));
        Assert.Equal(1_000, taken.Count);
        Assert.Equal(Producers * Each - 1_000, queue.DroppedCount);
        foreach (var group in taken.GroupBy(d => d.ThreadId))
        {
            var order = group.Select(d => int.Parse(d.Lead[(d.Lead.IndexOf(':', StringComparison.Ordinal) + 1)..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(order.Order().ToList(), order);
        }
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-5")]
    public async Task A_consumer_running_alongside_the_producers_sees_every_draft_that_was_not_dropped()
    {
        const int Producers = 4;
        const int Each = 20_000;
        var queue = new FeedQueue(2_000);
        var seen = new List<FeedDraft>();
        var producing = new[] { Producers };
        var consumer = Task.Run(() =>
        {
            while (Volatile.Read(ref producing[0]) > 0 || queue.Count > 0)
            {
                queue.TakeReady(Far, seen);
                Thread.Yield();
            }
        });
        var tasks = Enumerable.Range(0, Producers).Select(p => Task.Run(() =>
        {
            var thread = Guid.NewGuid();
            for (var i = 0; i < Each; i++)
                queue.Enqueue(i % 100 == 0 ? FeedSamples.NeedsYou(thread, i, $"{p}:{i}") : FeedSamples.Quiet(thread, i, $"{p}:{i}"));
            Interlocked.Decrement(ref producing[0]);
        })).ToArray();
        await Task.WhenAll(tasks);
        await consumer.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(Producers * Each / 100, seen.Count(d => d.Severity == FeedSeverity.NeedsYou));
        Assert.Equal(Producers * Each, seen.Count + queue.DroppedCount);
        Assert.Equal(seen.Count, seen.Select(d => d.Lead).Distinct().Count());
    }
}
