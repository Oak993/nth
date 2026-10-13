using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// The launch of a StdioProcess child, held by the test (ED-NEW-perf-ui-thread-7). While it is on, every StdioProcess.StartAsync of the test's flow asks this runner for the launch
/// instead of the pool: the child does not exist until <see cref="Release"/>, so a test sees a session wait for it with nothing blocked, and can place a Stop or an Interrupt inside the
/// window in which a Stop finds no child. The launch itself still runs on a pool thread, as in production, and is counted when it ran on the main loop's thread instead.
/// </summary>
public sealed class HeldLaunch : IDisposable
{
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualMainLoop loop;
    private readonly IDisposable scope;
    private readonly List<StdioProcess> children = [];
    private int requested;
    private int ran;
    private int ranOnTheLoop;

    public HeldLaunch(ManualMainLoop loop, bool hold = true)
    {
        this.loop = loop;
        if (!hold) released.SetResult();
        scope = StdioProcess.LaunchRunner.Override(Run);
    }

    /// <summary>How many launches were asked for.</summary>
    public int Requested => Volatile.Read(ref requested);

    /// <summary>How many launches ran to their end (a launch that failed counts).</summary>
    public int Launched => Volatile.Read(ref ran);

    /// <summary>How many of those ran on the main loop's thread: none, when the session hands the launch off.</summary>
    public int LaunchedOnTheLoop => Volatile.Read(ref ranOnTheLoop);

    /// <summary>The processes whose launch was asked for, in order.</summary>
    public StdioProcess Child(int index = 0)
    {
        lock (children) return children[index];
    }

    /// <summary>Lets the held launches go: the children now exist.</summary>
    public void Release() => released.TrySetResult();

    private Task Run(StdioProcess process, Action launch)
    {
        lock (children) children.Add(process);
        Interlocked.Increment(ref requested);
        return Task.Run(async () =>
        {
            await released.Task;
            if (loop.CheckAccess()) Interlocked.Increment(ref ranOnTheLoop);
            try { launch(); }
            finally { Interlocked.Increment(ref ran); }
        });
    }

    /// <summary>
    /// For a child that outlives its stdin (the stand-ins' ignore-eof, a held Command Code stand-in): it ends only because the session Terminated it once it existed. A Terminate of a running child
    /// arms the forced kill 3 s later on the test clock (a Stop before the launch arms nothing, the child not running), so this waits for that timer, lets the 3 s pass and expects the child gone.
    /// </summary>
    public static void AssertEndedByTerminate(TestRuntime runtime, StdioProcess child)
    {
        var due = runtime.Time.Elapsed + TimeSpan.FromSeconds(3);
        runtime.Loop.RunUntil(() => runtime.Time.TimerDeadlines.Contains(due), TimeSpan.FromSeconds(60));
        runtime.Advance(TimeSpan.FromSeconds(3));
        runtime.Loop.RunUntil(() => !child.IsRunning, TimeSpan.FromSeconds(15));
        Assert.False(child.IsRunning);
    }

    public void Dispose()
    {
        released.TrySetResult();
        scope.Dispose();
    }
}
