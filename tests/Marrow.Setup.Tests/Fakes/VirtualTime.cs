using System.Diagnostics;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// Runs a task that waits on a <see cref="ManualTimeProvider"/> to its end by moving the clock from one pending timer to the next, so a
/// 15 s wait costs a few milliseconds of real time and every wait ends at exactly its own time (nth installer PLAN.md 10, unit U4).
/// </summary>
public static class VirtualTime
{
    private static readonly TimeSpan RealLimit = TimeSpan.FromSeconds(30);

    // How many 1 ms looks with no timer at all make a task idle for AdvanceUntilAsync (a task between two waits registers its next timer in far less).
    private const int IdleChecks = 25;

    /// <summary>Moves time until <paramref name="task"/> finishes; fails when it needs more than <paramref name="virtualLimit"/> of the fake clock (10 minutes by default).</summary>
    public static async Task<T> RunAsync<T>(this ManualTimeProvider time, Task<T> task, TimeSpan? virtualLimit = null)
    {
        await time.PumpAsync(task, virtualLimit);
        return await task;
    }

    /// <summary>Like <see cref="RunAsync{T}"/> for a task with no result.</summary>
    public static async Task RunAsync(this ManualTimeProvider time, Task task, TimeSpan? virtualLimit = null)
    {
        await time.PumpAsync(task, virtualLimit);
        await task;
    }

    /// <summary>Moves time until <paramref name="done"/> is true or the fake clock has moved by <paramref name="by"/>; the task keeps running.</summary>
    public static async Task AdvanceUntilAsync(this ManualTimeProvider time, Func<bool> done, TimeSpan by)
    {
        var start = time.Elapsed;
        var real = Stopwatch.StartNew();
        var idle = 0;
        while (!done())
        {
            if (real.Elapsed > RealLimit) throw new TimeoutException("The condition never came true in real time.");
            if (time.Elapsed - start >= by) return;
            if (time.TimerDeadlines is not [var next, ..])
            {
                // Nothing waits on the clock. Either the task is between two waits (a real moment brings the next one) or it has nothing left to wait
                // for, and then the rest of the time passes at once.
                if (++idle > IdleChecks && !done())
                {
                    time.Advance(by - (time.Elapsed - start));
                    return;
                }
                await Task.Delay(1);
                continue;
            }
            idle = 0;
            var step = next - time.Elapsed;
            var limit = by - (time.Elapsed - start);
            time.Advance(step > limit ? limit : step < TimeSpan.Zero ? TimeSpan.Zero : step);
            await Task.Yield();
        }
    }

    private static async Task PumpAsync(this ManualTimeProvider time, Task task, TimeSpan? virtualLimit)
    {
        var limit = virtualLimit ?? TimeSpan.FromMinutes(10);
        var start = time.Elapsed;
        var real = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (real.Elapsed > RealLimit) throw new TimeoutException("The task never finished in real time (it waits on something that is not the fake clock).");
            if (time.TimerDeadlines is not [var next, ..])
            {
                await Task.Delay(1); // the task is between two waits: a real moment, never a spin that fills a core
                continue;
            }
            var step = next - time.Elapsed;
            if (time.Elapsed - start + step > limit) throw new TimeoutException($"The task needs more than {limit} of the fake clock.");
            time.Advance(step < TimeSpan.Zero ? TimeSpan.Zero : step);
            await Task.Yield();
        }
    }
}
