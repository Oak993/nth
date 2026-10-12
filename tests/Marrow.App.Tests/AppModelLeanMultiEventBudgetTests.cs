using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests;

// ED-NEW-lean-16 (follow-up): the two thread events that change several threads at once, MarkRead(ids) and the activity reorder
// (MoveInActivity, through UpdateThreads), grow with the library by no more than the new list's one 8-byte reference per thread,
// as UpdateThread does (AppModelLeanBudgetTests). Measured on the main loop after warm-up, every row's child and selection cells
// made, as the sidebar makes them; only the measured call's bytes are counted (MarkRead's unread mark is set between calls).
public sealed class AppModelLeanMultiEventBudgetTests
{
    private const int Warmup = 20;
    private const int Events = 200;

    private static double BytesPerEvent(int threadCount, bool markRead)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("lean")) with { IsExpanded = true };
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var threads = new ChatThread[threadCount];
        for (var index = 0; index < threadCount; index++)
        {
            threads[index] = AppModelSamples.Thread(project) with
            {
                Title = "Thread " + index,
                CreatedAt = start.AddMinutes(index),
                UpdatedAt = start.AddMinutes(index),
                ParentThreadID = index % 10 == 9 ? threads[index - 1].Id : null,
            };
        }
        h.WriteLibrary([project], threads);
        var model = h.MakeModel();
        var first = threads[threadCount / 2].Id;
        var second = threads[threadCount / 2 + 1].Id;
        IReadOnlyList<Guid> read = [first, second];
        double bytes = -1;
        h.Main.OnMain(() =>
        {
            foreach (var thread in threads)
            {
                model.Children(thread.Id);
                model.IsSelected(thread.Id);
            }
            long Event(int k)
            {
                if (markRead)
                {
                    model.UpdateThread(first, thread => thread with { HasUnread = true });
                    model.UpdateThread(second, thread => thread with { HasUnread = true });
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    model.MarkRead(read);
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    Assert.False(model.Thread(first)!.HasUnread);
                    return allocated;
                }
                var at = GC.GetAllocatedBytesForCurrentThread();
                // first after second, then second after first: each event reorders the two
                var moved = k % 2 == 0 ? model.MoveInActivity(first, second, true, model.Thread(first)!.ActivityOrder < model.Thread(second)!.ActivityOrder ? [first, second] : [second, first])
                    : model.MoveInActivity(second, first, true, model.Thread(first)!.ActivityOrder < model.Thread(second)!.ActivityOrder ? [first, second] : [second, first]);
                var spent = GC.GetAllocatedBytesForCurrentThread() - at;
                Assert.True(moved);
                return spent;
            }
            for (var k = 1; k <= Warmup; k++) Event(k);
            long total = 0;
            for (var k = Warmup + 1; k <= Warmup + Events; k++) total += Event(k);
            bytes = total / (double)Events;
        });
        return bytes;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // budget: what one more thread in the library adds to MarkRead(ids)
    public void Marking_threads_read_grows_by_no_more_than_the_new_lists_reference_per_thread()
    {
        var perThread = (BytesPerEvent(500, markRead: true) - BytesPerEvent(50, markRead: true)) / 450;
        Assert.True(perThread <= MarkReadLimit, $"each thread in the library adds {perThread:F1} B to MarkRead (limit {MarkReadLimit})");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // budget: what one more thread in the library adds to an activity reorder
    public void An_activity_reorder_grows_by_no_more_than_the_new_lists_reference_per_thread()
    {
        var perThread = (BytesPerEvent(500, markRead: false) - BytesPerEvent(50, markRead: false)) / 450;
        Assert.True(perThread <= ReorderLimit, $"each thread in the library adds {perThread:F1} B to an activity reorder (limit {ReorderLimit})");
    }

    // Measured first on the unchanged events (claude/lean-r1 d723dd5f8): 16.0 B per thread each (a list copy, then the new list's
    // array); after ED-NEW-lean-16 (follow-up) 8.0 B each, the new list's one reference per thread (limits 1.3x, as UpdateThread's).
    private const double MarkReadLimit = 10.4; // before 16.0, after 8.0
    private const double ReorderLimit = 10.4; // before 16.0, after 8.0
}
