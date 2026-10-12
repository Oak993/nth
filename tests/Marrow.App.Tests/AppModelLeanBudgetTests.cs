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
        Assert.True(bytes <= 377_000, $"a thread event with 500 threads allocates {bytes:F0} B (limit 377,000; 251,544 measured on the unchanged code)");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-16")] // budget: bytes per thread event with 50 threads
    public void A_thread_event_with_50_threads_stays_within_its_byte_budget()
    {
        var bytes = BytesPerThreadEvent(50);
        Assert.True(bytes <= 39_900, $"a thread event with 50 threads allocates {bytes:F0} B (limit 39,900; 26,616 measured on the unchanged code)");
    }
}
