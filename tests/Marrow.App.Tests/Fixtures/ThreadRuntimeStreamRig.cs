using Marrow.App.Runtime;
using Marrow.App.Tests.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// Helper for Runtime/ThreadRuntimeStreamBudgetTests.cs (R-TEST-2: a test class holds no nested type). Not port code.
// ED-NEW-lean-5: one streamed row of a ThreadRuntime, its QueueDelta, PendingFlushLength and FlushDeltas bound once.
internal sealed class ThreadRuntimeStreamRig
{
    private readonly Action<string, ThreadRuntime.DeltaKind, string> queue;
    private readonly Func<long> flushLength;
    private readonly Action<bool> flush;

    public ThreadRuntimeStreamRig(TimelineItem.Content content)
    {
        var runtime = ThreadRuntimeStreamBudgetTests.CreateRuntime();
        Entry = ThreadRuntimeStreamBudgetTests.AddEntry(runtime, "row", content);
        ThreadRuntimeStreamBudgetTests.SetField(runtime, "lastFlushAt", SwiftClock.Now + TimeSpan.FromSeconds(1000)); // every delta waits for its window
        queue = ThreadRuntimeStreamBudgetTests.Method("QueueDelta").CreateDelegate<Action<string, ThreadRuntime.DeltaKind, string>>(runtime);
        flushLength = ThreadRuntimeStreamBudgetTests.Method("PendingFlushLength").CreateDelegate<Func<long>>(runtime);
        flush = ThreadRuntimeStreamBudgetTests.Method("FlushDeltas").CreateDelegate<Action<bool>>(runtime);
    }

    public TimelineEntry Entry { get; }

    public string ReplyText => Assert.IsType<TimelineItem.Content.Assistant>(Entry.Item.TimelineItemContent).Value.Text;

    // The first delta of a window schedules its flush; three more join it; the flush task reads the length tier
    // and the flush appends the window's text to the row.
    public (long Deltas, long Flush) ReplyWindow()
    {
        queue("row", ThreadRuntime.DeltaKind.Message, ThreadRuntimeStreamBudgetTests.Delta);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 3; i++) queue("row", ThreadRuntime.DeltaKind.Message, ThreadRuntimeStreamBudgetTests.Delta);
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
