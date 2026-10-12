using System.Globalization;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-8). Waits (in real time, bounded) for a condition another task makes true,
// for tests of calls that block on a held confirmation or approval, and a clock whose delays end only when a test moves it.
public static class PcTestWait
{
    public static async Task Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("the condition did not come true in " + timeoutMs.ToString(CultureInfo.InvariantCulture) + " ms");
            await Task.Delay(5);
        }
    }

    /// <summary>Gives other tasks time to run, then reports whether the task is still waiting.</summary>
    public static async Task<bool> StillWaiting(Task task, int ms = 150)
    {
        await Task.Delay(ms);
        return !task.IsCompleted;
    }
}

/// <summary>
/// A clock whose delays finish only when a test advances it (for timeouts that must not fire by themselves). Inline: a due
/// delay's continuation runs inside <see cref="Advance"/>, so the code under test has reached its next wait (or ended) when
/// Advance returns, and a test that advances again at once never races it (ED-NEW-pcctl-35).
/// </summary>
public sealed class PcManualClock(bool inline = false) : IPcClock
{
    private readonly Lock gate = new();
    private readonly List<(long Due, TaskCompletionSource Done)> waiting = [];
    private long now;

    public long NowMs
    {
        get
        {
            lock (gate) return now;
        }
    }

    /// <summary>The delays not yet due (a test waits until the code under test is waiting).</summary>
    public int Pending
    {
        get
        {
            lock (gate) return waiting.Count(w => !w.Done.Task.IsCompleted);
        }
    }

    public Task Delay(int milliseconds, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(inline ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate) waiting.Add((now + milliseconds, done));
        cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        return done.Task;
    }

    public void Advance(int milliseconds)
    {
        List<TaskCompletionSource> due;
        lock (gate)
        {
            now += milliseconds;
            due = waiting.Where(w => w.Due <= now).Select(w => w.Done).ToList();
            waiting.RemoveAll(w => w.Due <= now);
        }
        foreach (var done in due) done.TrySetResult();
    }
}
