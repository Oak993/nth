using System.Runtime.ExceptionServices;

namespace Marrow.Testing;

/// <summary>Measures only synchronous application work, on a dedicated thread with no test-runner context.</summary>
public static class AllocationProbe
{
    public static long Measure(Action warmup, Action measured)
    {
        long bytes = -1;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                warmup();
                var before = GC.GetAllocatedBytesForCurrentThread();
                measured();
                bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        }) { IsBackground = true, Name = "Marrow allocation probe" };
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) throw new TimeoutException("The allocation probe did not finish.");
        failure?.Throw();
        return bytes;
    }
}
