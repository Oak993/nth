using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests;

// ED-NEW-lean-16 (lean pass L16): a thread event (UpdateThread, the call every runtime status, unread and timestamp change
// goes through) costs the same with 500 threads in the library as with 50. Measured on the main loop after warm-up: the first
// event schedules the save, and every row's child and selection cells exist, as the sidebar makes them.
public sealed class AppModelLeanBudgetTests
{
    private const int Warmup = 20;
    private const int Events = 200;

    private static double BytesPerThreadEvent(int threadCount)
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
                // every tenth thread is a helper of the one before it, so the child cells hold something
                ParentThreadID = index % 10 == 9 ? threads[index - 1].Id : null,
            };
        }
        h.WriteLibrary([project], threads);
        var model = h.MakeModel();
        var target = threads[threadCount / 2].Id;
        double bytes = -1;
        h.Main.OnMain(() =>
        {
            foreach (var thread in threads)
            {
                model.Children(thread.Id);
                model.IsSelected(thread.Id);
            }
            void Event(int k) => model.UpdateThread(target, thread => thread with
            {
                UpdatedAt = start.AddSeconds(k),
                LastStatus = k % 2 == 0 ? TurnStatus.Running : TurnStatus.Completed,
            });
            for (var k = 1; k <= Warmup; k++) Event(k);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var k = Warmup + 1; k <= Warmup + Events; k++) Event(k);
            bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Events;
            Assert.Equal(start.AddSeconds(Warmup + Events), model.Thread(target)!.UpdatedAt);
        });
        return bytes;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // budget: bytes per thread event with 500 threads
    public void A_thread_event_with_500_threads_stays_within_its_byte_budget()
    {
        var bytes = BytesPerThreadEvent(500);
        Assert.True(bytes <= 6_200, $"a thread event with 500 threads allocates {bytes:F0} B (limit 6,200; 251,544 before ED-NEW-lean-16, 4,752 after)");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // budget: bytes per thread event with 50 threads
    public void A_thread_event_with_50_threads_stays_within_its_byte_budget()
    {
        var bytes = BytesPerThreadEvent(50);
        Assert.True(bytes <= 1_500, $"a thread event with 50 threads allocates {bytes:F0} B (limit 1,500; 26,616 before ED-NEW-lean-16, 1,152 after)");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // budget: what one more thread in the library adds to a thread event
    public void A_thread_event_grows_by_no_more_than_the_new_lists_reference_per_thread()
    {
        // The event hands Threads a new list (a Swift array value, AppModel.swift:712), one 8-byte reference per thread;
        // nothing else in it may grow with the library. 499.8 B per thread before ED-NEW-lean-16, 8.0 after.
        var perThread = (BytesPerThreadEvent(500) - BytesPerThreadEvent(50)) / 450;
        Assert.True(perThread <= 10.4, $"each thread in the library adds {perThread:F1} B to a thread event (limit 10.4)");
    }
}
