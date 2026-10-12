using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// Lean pass L14 (ED-NEW-lean-14): what the timeline allocates for one streamed reasoning delta, and for one entry appended to the running turn, must not grow
// with the length of the thread. Measured on the headless UI thread with GC.GetAllocatedBytesForCurrentThread (the rows, the working line and the block cache
// all run there; AllocationProbe's own thread cannot host the window). The numbers are a Debug build's (the test project is built Debug).
[Collection("Headless")]
public sealed class ThreadTimelineLeanBudgetTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("8d0c6a51-3c1e-4f43-9a52-2f1d6b7e0c14");
    private const int Warmup = 20;
    private const int Measured = 40;

    private static TimelineEntry Reasoning(string id, string text, bool streaming) =>
        Entry(id, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = text, IsStreaming = streaming }), Turn);

    // A thread of `total` entries: finished turns (three entries each), then the running turn's prompt and its thinking, the last step still streaming.
    private static List<TimelineEntry> Thread(int total)
    {
        var running = new List<TimelineEntry>
        {
            User("prompt", Turn, "Please make the build faster"),
            Reasoning("r1", "First I look at the workflow.", streaming: false),
            Reasoning("r2", "Then the cache.", streaming: false),
            Reasoning("r3", "Now the", streaming: true),
        };
        var finished = (total - running.Count) / 3;
        var entries = Turns(finished, "old-", sentences: 2).ToList();
        for (var i = entries.Count + running.Count; i < total; i++) entries.Add(Entry("note-" + i, new TimelineItem.Content.Notice(new Marrow.Core.Models.Notice { NoticeLevel = Marrow.Core.Models.Notice.Level.Info, Message = "a note" })));
        entries.AddRange(running);
        Assert.Equal(total, entries.Count);
        return entries;
    }

    private static void Open(TimelineMount mount)
    {
        mount.Model.Settings.ShowReasoning = true;
        mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var line = LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single();
        LogicalQuery.SelfAndDescendants<Button>(line).First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
    }

    // One reasoning delta as the runtime lands it (ThreadRuntime.AppendStreamed): the streaming entry's item is replaced with the longer text, in the same entry.
    private static void Delta(TimelineMount mount, TimelineEntry streaming, int step)
    {
        var reasoning = ((TimelineItem.Content.Reasoning)streaming.Item.TimelineItemContent).Value;
        streaming.Item = streaming.Item with { TimelineItemContent = new TimelineItem.Content.Reasoning(reasoning with { Text = reasoning.Text + " w" + (step % 10) }) };
        mount.Advance(16);
    }

    internal static long BytesPerReasoningDelta(HeadlessSession headless, int total)
    {
        long bytes = 0;
        ThreadTimelineRig.Run(headless, mount =>
        {
            var entries = Thread(total); mount.SetEntries(entries); Open(mount);
            var streaming = entries[^1];
            for (var i = 0; i < Warmup; i++) Delta(mount, streaming, i);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Measured; i++) Delta(mount, streaming, Warmup + i);
            bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / Measured;
            // The streamed thinking is on screen, whole, in the opened line.
            Assert.Contains(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(mount.View), view => view.Text == ((TimelineItem.Content.Reasoning)streaming.Item.TimelineItemContent).Value.Text);
        });
        return bytes;
    }

    // One tool entry appended to the running turn, as ThreadRuntime.Append lands it (a new list with the entry at its end).
    internal static long BytesPerAppendedEntry(HeadlessSession headless, int total, bool minimap = true)
    {
        long bytes = 0;
        ThreadTimelineRig.Run(headless, mount =>
        {
            var entries = Thread(total); mount.SetEntries(entries); Open(mount);
            var lists = new List<ValueList<TimelineEntry>>();
            var grown = new List<TimelineEntry>(entries);
            for (var i = 0; i < Warmup + Measured; i++) { grown.Add(ToolEntry("tool-" + i, Turn)); lists.Add(new ValueList<TimelineEntry>(grown)); }
            var property = typeof(ThreadRuntime).GetProperty("Entries")!;
            void Append(int i) { property.SetValue(mount.Runtime, lists[i]); mount.Advance(16); }
            for (var i = 0; i < Warmup; i++) Append(i);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = Warmup; i < Warmup + Measured; i++) Append(i);
            bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / Measured;
        }, showsMinimap: minimap);
        return bytes;
    }

    // Debug build. Measured on the base (claude/release-integration 0a946c558): 1,234,227 bytes per delta at 200 entries, 1,190,866 at 2,000. Limit about 1.5x.
    private const long DeltaLimit = 1_800_000;
    // Debug build, the rail off. Measured on the base: 754,368 bytes per appended entry at 200 entries, 849,309 at 2,000. Limit about 1.5x.
    private const long AppendLimit = 1_275_000;
    // Debug build, the rail on (the default). Measured on the base: 3,945,024 bytes at 200 entries, 37,413,216 at 2,000; the growth is the minimap's
    // (TimelineMinimap.Entries remakes every tick once the thread has more user messages than its 400-entry cache holds), outside this file. Limit about 1.5x.
    private const long RailAppendLimit = 56_000_000;

    private static void Log(string line)
    {
        if (Environment.GetEnvironmentVariable("MARROW_LEAN_LOG") is { Length: > 0 } path) System.IO.File.AppendAllText(path, line + Environment.NewLine);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-14")]
    public void A_reasoning_delta_costs_the_same_in_a_thread_of_two_thousand_entries_as_in_one_of_two_hundred()
    {
        var small = BytesPerReasoningDelta(headless, 200);
        var large = BytesPerReasoningDelta(headless, 2000);
        Log($"delta small={small} large={large}");
        Assert.True(large <= 2 * small, $"a delta with 2,000 entries costs {large} bytes, with 200 {small}");
        Assert.True(large <= DeltaLimit, $"a delta with 2,000 entries costs {large} bytes (limit {DeltaLimit})");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-14")]
    public void An_appended_entry_costs_the_same_in_a_thread_of_two_thousand_entries_as_in_one_of_two_hundred()
    {
        var small = BytesPerAppendedEntry(headless, 200, minimap: false);
        var large = BytesPerAppendedEntry(headless, 2000, minimap: false);
        Log($"append small={small} large={large}");
        Assert.True(large <= 2 * small, $"an appended entry with 2,000 entries costs {large} bytes, with 200 {small}");
        Assert.True(large <= AppendLimit, $"an appended entry with 2,000 entries costs {large} bytes (limit {AppendLimit})");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-14")]
    public void An_appended_entry_with_the_minimap_on_stays_within_its_budget_at_two_thousand_entries()
    {
        var large = BytesPerAppendedEntry(headless, 2000);
        Log($"rail-append large={large}");
        Assert.True(large <= RailAppendLimit, $"an appended entry with 2,000 entries and the rail costs {large} bytes (limit {RailAppendLimit})");
    }
}
