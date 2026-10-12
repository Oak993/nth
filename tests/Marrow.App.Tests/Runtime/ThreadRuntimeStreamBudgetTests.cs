using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Xunit.Abstractions;

namespace Marrow.App.Tests.Runtime;

// ED-NEW-lean-5 budgets (Debug build, headless, manual clock and main loop; bytes from
// GC.GetAllocatedBytesForCurrentThread on the main-loop thread, so only the runtime's own synchronous work counts).
// A streamed reply goes through QueueDelta (each delta) and, once per flush window, PendingFlushLength (the flush
// delay tier) and FlushDeltas. Tool output queued inside one window builds up the same way.
public sealed class ThreadRuntimeStreamBudgetTests(ITestOutputHelper output)
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Measured on the unchanged code, then after the fix (ED-NEW-lean-5). Before: 282.7 B per delta at 10k and 100k;
    // 349,088 B (10k) and 2,482,912 B (100k) per flush beyond the flat copy; 288,557,112 B for the tool window
    // (limits were 424; 524,000; 3,725,000; 433,000,000). After: 173.3 B; 592.0 B and 609.9 B; 8,485,592 B.
    private const double DeltaBytesLimit = 225;            // bytes per queued delta, at 10k and at 100k characters
    private const double FlushOverheadLimit10k = 770;      // bytes per flush beyond the one flat copy of the reply
    private const double FlushOverheadLimit100k = 795;
    private const long ToolWindowBytesLimit = 11_040_000;  // 1 MB of tool output in 4 KB deltas within one window

    private const string Phrase = "The quick brown fox’s café jumps 🙂 over the lazy dog.\n";
    private const string Delta = "and the reply goes on, ";

    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void Streaming_a_reply_costs_the_same_per_delta_and_per_flush_at_any_length(int length)
    {
        using var test = new TestRuntime();
        var (deltaBytes, flushOverhead) = test.OnMain(() => MeasureReply(length));
        output.WriteLine($"{length} characters: {deltaBytes:F1} B per delta, {flushOverhead:F1} B per flush beyond the flat copy");
        Assert.True(deltaBytes <= DeltaBytesLimit, $"a queued delta costs {deltaBytes:F1} B at {length} characters (a flush {flushOverhead:F1} B beyond the flat copy)");
        var flushLimit = length >= 100_000 ? FlushOverheadLimit100k : FlushOverheadLimit10k;
        Assert.True(flushOverhead <= flushLimit, $"a flush costs {flushOverhead:F1} B beyond the flat copy at {length} characters");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void Per_flush_cost_beyond_the_flat_copy_stays_within_twice_from_10k_to_100k_characters()
    {
        using var test = new TestRuntime();
        var small = test.OnMain(() => MeasureReply(10_000));
        var large = test.OnMain(() => MeasureReply(100_000));
        output.WriteLine($"10k: {small.DeltaBytes:F1} / {small.FlushOverhead:F1} B; 100k: {large.DeltaBytes:F1} / {large.FlushOverhead:F1} B");
        Assert.True(large.DeltaBytes <= 2 * Math.Max(small.DeltaBytes, 64), $"per delta {small.DeltaBytes:F1} B -> {large.DeltaBytes:F1} B");
        Assert.True(large.FlushOverhead <= 2 * Math.Max(small.FlushOverhead, 256), $"per flush {small.FlushOverhead:F1} B -> {large.FlushOverhead:F1} B");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void A_megabyte_of_tool_output_queued_in_one_window_is_copied_a_bounded_number_of_times()
    {
        using var test = new TestRuntime();
        var chunk = ToolChunk();
        long bytes = 0;
        test.OnMain(() =>
        {
            var rig = new Rig(new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run" }));
            rig.ToolWindow(chunk); // warm
            var before = GC.GetAllocatedBytesForCurrentThread();
            rig.ToolWindow(chunk);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var tool = Assert.IsType<TimelineItem.Content.Tool>(rig.Entry.Item.TimelineItemContent).Value;
            Assert.StartsWith("…", tool.Output, StringComparison.Ordinal);
        });
        output.WriteLine($"1 MB of tool output in 4 KB deltas, one window: {bytes:N0} B");
        Assert.True(bytes <= ToolWindowBytesLimit, $"one window of 1 MB tool output allocates {bytes:N0} B");
    }

    private static (double DeltaBytes, double FlushOverhead) MeasureReply(int length)
    {
        var rig = new Rig(new TimelineItem.Content.Assistant(new AssistantMessage { Text = Reply(length), IsStreaming = true }));
        for (var window = 0; window < 20; window++) rig.ReplyWindow(); // warm
        long deltaBytes = 0, flushBytes = 0, flatBytes = 0;
        const int windows = 60;
        for (var window = 0; window < windows; window++)
        {
            var (deltas, flush) = rig.ReplyWindow();
            deltaBytes += deltas;
            flushBytes += flush;
            flatBytes += FlatStringBytes(rig.ReplyText.Length);
        }
        return (deltaBytes / (windows * 3.0), (flushBytes - flatBytes) / (double)windows);
    }

    // One string of n UTF-16 units on x64: 8 (header) + 8 (method table) + 4 (length) + 2n + 2 (terminator), rounded to 8.
    private static long FlatStringBytes(int length) => (22 + 2L * length + 7) / 8 * 8;

    private static string Reply(int length)
    {
        var builder = new StringBuilder(length + Phrase.Length);
        while (builder.Length < length) builder.Append(Phrase);
        return builder.ToString(0, length);
    }

    private static string ToolChunk()
    {
        var builder = new StringBuilder(4_096);
        var line = 0;
        while (builder.Length < 4_096) builder.Append("line ").Append(line++).Append(": build output — ok ✓\n");
        return builder.ToString(0, 4_096);
    }

    private sealed class Rig
    {
        private readonly Action<string, ThreadRuntime.DeltaKind, string> queue;
        private readonly Func<long> flushLength;
        private readonly Action<bool> flush;

        public Rig(TimelineItem.Content content)
        {
            var runtime = CreateRuntime();
            Entry = AddEntry(runtime, "row", content);
            SetField(runtime, "lastFlushAt", SwiftClock.Now + TimeSpan.FromSeconds(1000)); // every delta waits for its window
            queue = Method("QueueDelta").CreateDelegate<Action<string, ThreadRuntime.DeltaKind, string>>(runtime);
            flushLength = Method("PendingFlushLength").CreateDelegate<Func<long>>(runtime);
            flush = Method("FlushDeltas").CreateDelegate<Action<bool>>(runtime);
        }

        public TimelineEntry Entry { get; }

        public string ReplyText => Assert.IsType<TimelineItem.Content.Assistant>(Entry.Item.TimelineItemContent).Value.Text;

        // The first delta of a window schedules its flush; three more join it; the flush task reads the length tier
        // and the flush appends the window's text to the row.
        public (long Deltas, long Flush) ReplyWindow()
        {
            queue("row", ThreadRuntime.DeltaKind.Message, Delta);
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 3; i++) queue("row", ThreadRuntime.DeltaKind.Message, Delta);
            var queued = GC.GetAllocatedBytesForCurrentThread();
            flushLength();
            flush(false);
            var flushed = GC.GetAllocatedBytesForCurrentThread();
            return (queued - start, flushed - queued);
        }

        public void ToolWindow(string chunk)
        {
            for (var i = 0; i < 256; i++) queue("row", ThreadRuntime.DeltaKind.ToolOutput, chunk);
            flushLength();
            flush(false);
        }
    }

    private static MethodInfo Method(string name)
    {
        var method = typeof(ThreadRuntime).GetMethod(name, InstanceMembers);
        Assert.NotNull(method);
        return method!;
    }

    private static ThreadRuntime CreateRuntime()
    {
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        SetField(runtime, "entries", ValueList<TimelineEntry>.Empty);
        SetField(runtime, "turns", ValueList<TurnRecord>.Empty);
        SetField(runtime, "followUps", ValueList<FollowUpPrompt>.Empty);
        SetField(runtime, "skillsFollowUps", new HashSet<Guid>());
        SetField(runtime, "approvals", ValueList<ApprovalRequest>.Empty);
        SetField(runtime, "questions", ValueList<QuestionRequest>.Empty);
        SetField(runtime, "entryIndex", new Dictionary<string, TimelineEntry>(SwiftStringComparer.Instance));
        SetField(runtime, "phase", RuntimePhase.Idle);
        SetField(runtime, "saveRevision", 0L);
        SetField(runtime, "persistenceEnabled", false);
        SetField(runtime, "currentTurnID", null);
        var pendingDeltaType = typeof(ThreadRuntime).GetNestedType("PendingDelta", BindingFlags.NonPublic);
        Assert.NotNull(pendingDeltaType);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), pendingDeltaType!);
        SetField(runtime, "pendingDeltas", Activator.CreateInstance(dictionaryType)!);
        return runtime;
    }

    private static TimelineEntry AddEntry(ThreadRuntime runtime, string id, TimelineItem.Content content)
    {
        var item = new TimelineItem(Guid.NewGuid(), content, id, new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
        Method("Append").Invoke(runtime, [item]);
        return Assert.Single(runtime.Entries, entry => entry.Id == id);
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, InstanceMembers);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
