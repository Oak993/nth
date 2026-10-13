using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Xunit;

namespace Marrow.Services.Tests.Port.MultiAccount;

// PORT-ONLY (ED-NEW-perf-ui-thread-5): the handoff journal's disk work (the append, the rotation, the compression) is on the writer's own queue, not on the thread that hands the lines over.
// The sink the writer calls is held, failed and recorded by the tests; real files under a scratch folder for the order and the reader.
public sealed class HandoffJournalWriterTests
{
    private static string[] Lines(string prefix, int count) => Enumerable.Range(0, count).Select(i => "{\"Event\":\"" + prefix + i + "\"}").ToArray();

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-5
    public void Handing_lines_over_does_not_wait_for_the_disk_and_the_batches_arrive_in_order_on_another_thread()
    {
        var folder = "queue-order-" + Guid.NewGuid().ToString("N");
        var release = new ManualResetEventSlim();
        var entered = new ManualResetEventSlim();
        var calls = new List<(int Thread, string[] Lines)>();
        using var sink = HandoffJournalWriter.Sink.Override((where, lines) =>
        {
            if (where != folder) return;
            lock (calls) calls.Add((Environment.CurrentManagedThreadId, [.. lines]));
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });

        var first = Lines("a", 3);
        var second = Lines("b", 2);
        var list = new List<string>(first);
        HandoffJournalWriter.Append(folder, list);
        list.Clear(); // the caller reuses its list at once: the writer took a copy
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "the writer never took the first batch");
        HandoffJournalWriter.Append(folder, second);   // returns while the first is still held

        lock (calls) Assert.Single(calls);
        Assert.NotEqual(Environment.CurrentManagedThreadId, calls[0].Thread);
        release.Set();
        HandoffJournalWriter.WaitForQueuedWrites();

        lock (calls)
        {
            Assert.Equal(2, calls.Count);
            Assert.Equal(first, calls[0].Lines);
            Assert.Equal(second, calls[1].Lines);
            Assert.Equal(calls[0].Thread, calls[1].Thread);
        }
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Reading_waits_for_the_batches_handed_over_and_returns_every_line_in_order()
    {
        using var scratch = new StorageScratchFolder();
        var folder = Path.Combine(scratch.Root, "thread");
        var expected = new List<string>();
        for (var batch = 0; batch < 20; batch++)
        {
            var lines = Lines("batch" + batch + "-", 5);
            expected.AddRange(lines);
            HandoffJournalWriter.Append(folder, lines);
        }

        Assert.Equal(expected, HandoffJournal.Read(folder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-5: a disk that said no loses nothing and changes nothing in the order
    public void A_batch_the_disk_refuses_is_kept_written_first_with_the_next_batch_and_read_after_the_lines_on_disk()
    {
        var folder = "queue-refused-" + Guid.NewGuid().ToString("N");
        var seen = new List<string[]>();
        var failing = true;
        using var sink = HandoffJournalWriter.Sink.Override((where, lines) =>
        {
            if (where != folder) return;
            if (failing) throw new IOException("the disk said no");
            lock (seen) seen.Add([.. lines]);
        });

        var first = Lines("a", 2);
        var second = Lines("b", 2);
        HandoffJournalWriter.Append(folder, first);
        HandoffJournalWriter.WaitForQueuedWrites();
        Assert.Equal(first, HandoffJournalWriter.RefusedFor(folder));
        Assert.Equal(first, HandoffJournal.Read(folder)); // a reader still gets them

        failing = false;
        HandoffJournalWriter.Append(folder, second);
        HandoffJournalWriter.WaitForQueuedWrites();
        lock (seen) Assert.Equal([[.. first, .. second]], seen);
        Assert.Empty(HandoffJournalWriter.RefusedFor(folder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-5: a failure that is not an IO failure does not take the worker thread, and with it the app, down
    public void A_sink_that_throws_anything_is_survived_and_the_lines_are_kept()
    {
        var folder = "queue-throws-" + Guid.NewGuid().ToString("N");
        using var sink = HandoffJournalWriter.Sink.Override((where, lines) => { if (where == folder) throw new InvalidOperationException("not an IO failure"); });

        HandoffJournalWriter.Append(folder, Lines("a", 1));
        HandoffJournalWriter.WaitForQueuedWrites();

        Assert.Single(HandoffJournalWriter.RefusedFor(folder));
        HandoffJournalWriter.Append(folder, []);                  // nothing to hand over: nothing happens
        HandoffJournalWriter.WaitForQueuedWrites();
        Assert.Single(HandoffJournalWriter.RefusedFor(folder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-5: the one-pass encoding is the bytes the two-pass encoding made
    public void The_event_line_is_what_the_two_pass_encoding_wrote()
    {
        var options = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        static string TwoPass(ProviderEvent value, System.Text.Json.JsonSerializerOptions options) =>
            System.Text.Json.JsonSerializer.Serialize(new { Event = value.GetType().Name, Payload = System.Text.Json.JsonSerializer.SerializeToElement(value, value.GetType(), options) }, options);
        var nasty = "em — dash, \"quotes\", back\\slash, tab\t, newline\n, <tag> & 'apos', café, 日本語, emoji 😀, \u0001 control, big " + new string('x', 70_000);
        ProviderEvent[] events =
        [
            new ProviderEvent.MessageDelta("m1", nasty), new ProviderEvent.MessageCompleted("m1", nasty), new ProviderEvent.ReasoningDelta("r", nasty),
            new ProviderEvent.ReasoningCompleted("r", ""), new ProviderEvent.PlanDelta("p", nasty), new ProviderEvent.PlanCompleted("p", nasty),
            new ProviderEvent.ToolStarted("t", new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = nasty, Output = nasty }),
            new ProviderEvent.ToolOutput("t", nasty), new ProviderEvent.ToolUpdated("t", new ToolUpdate { Output = nasty, Status = ToolCall.Status.Completed }),
            new ProviderEvent.RequestResolved("q"), new ProviderEvent.Diff(nasty), new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = nasty }),
            new ProviderEvent.TurnCompleted(TurnStatus.Failed, nasty), new ProviderEvent.TurnCompleted(TurnStatus.Completed, null), new ProviderEvent.Exited(null), new ProviderEvent.Exited(nasty),
        ];

        foreach (var value in events)
        {
            Assert.Equal(TwoPass(value, options), HandoffJournal.Encode(value));
        }
        // An event the journal does not keep is still not kept.
        Assert.Null(HandoffJournal.Encode(new ProviderEvent.SessionReady("s")));
    }
}
