using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// Helper for DroppyCodePrologueTests (R-TEST-3: helpers live here). The launch prologue starts the hang watchdog, a
// process-wide thread that has no stop (HangWatchdog.swift:42-60, PR-LAUNCH-014/-021). It can therefore be started once
// per test process, and the thread has to be started under a clock and a main queue the tests own, or it would ping an
// unbound main actor and, after four real seconds of stall, write a report into the owner's data folder.
// The rig makes that one start: a manual clock and a manual main loop are installed for the calling flow, the prologue
// is run, and the thread (which inherits them) is left sleeping on the manual clock for the life of the process.

/// <summary>The one prologue run of the test process, with the clock and the main loop its watchdog thread runs on.</summary>
public sealed class PrologueWatchdogRig
{
    private static readonly Lazy<PrologueWatchdogRig> Shared = new(() => new PrologueWatchdogRig(), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly TimeSpan RealBound = TimeSpan.FromSeconds(5);

    /// <summary>The rig, made (and the prologue run for the first time) by the first caller.</summary>
    public static PrologueWatchdogRig Instance => Shared.Value;

    private PrologueWatchdogRig()
    {
        Time = new ManualTimeProvider();
        Loop = new ManualMainLoop();
        using (Apply())
        {
            DroppyCodeApp.RunPrologue();
            WatchdogIsSleeping = WaitForSleepers(1);
        }
    }

    /// <summary>The clock the watchdog thread sleeps on; it moves only when a test advances it.</summary>
    public ManualTimeProvider Time { get; }

    /// <summary>The main queue the watchdog thread pings; its work runs only when a test pumps it.</summary>
    public ManualMainLoop Loop { get; }

    /// <summary>Whether, after the first prologue run, a thread was waiting out its 500 ms on the manual clock.</summary>
    public bool WatchdogIsSleeping { get; }

    /// <summary>The clock and the main queue as the calling flow sees them, for a prologue run that must start nothing real.</summary>
    public IDisposable Apply() => new PrologueScope(SwiftClock.Provider.Override(Time), MainActor.Executor.Override(Loop));

    /// <summary>Waits (in real time, bounded) until <paramref name="count"/> threads wait on a timer of the manual clock.</summary>
    public bool WaitForSleepers(int count)
    {
        var deadline = DateTime.UtcNow + RealBound;
        while (Time.ActiveTimers != count)
        {
            if (DateTime.UtcNow > deadline) return false;
            Thread.Sleep(2);
        }
        return true;
    }

    /// <summary>
    /// Moves the clock by <paramref name="by"/> and returns how many blocks the watchdog put on the main queue (they are run
    /// at once, as the main thread does, so the watchdog never sees a stall). A wake is awaited, in real time, only when the
    /// watchdog is due: a block that should not come is looked for during a short real pause.
    /// </summary>
    public int Advance(TimeSpan by, bool expectPing)
    {
        WaitForSleepers(1);
        Time.Advance(by);
        if (expectPing)
        {
            var deadline = DateTime.UtcNow + RealBound;
            while (Loop.Pending == 0 && DateTime.UtcNow < deadline) Thread.Sleep(2);
            WaitForSleepers(1); // the thread went round to its next sleep
        }
        Thread.Sleep(60); // a second, unwanted block would arrive in this time
        return Loop.RunUntilIdle();
    }
}

/// <summary>Two ambient overrides put back together, the later one first.</summary>
public sealed class PrologueScope(IDisposable clock, IDisposable main) : IDisposable
{
    public void Dispose()
    {
        main.Dispose();
        clock.Dispose();
    }
}
