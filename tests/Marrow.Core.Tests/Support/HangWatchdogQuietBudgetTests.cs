using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// ED-NEW-lean-6 (b): while no window of the app can be seen the watchdog does not wake main. The watchdog thread is a real
// thread; the manual clock drives its 0.5 s sleep (R-GCD-3) and the test thread plays the main loop, as in HangWatchdogTests.
[Trait("Decision", "ED-NEW-lean-6")]
public sealed class HangWatchdogQuietBudgetTests
{
    // Pings posted to main in 10 simulated seconds while quiet. Measured (Debug): 20 before the fix, so the first limit
    // was 30; 0 after, the limit now.
    private const int QuietPings = 0;

    [Fact]
    public void A_quiet_app_is_not_pinged()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var lines = new ConcurrentQueue<string>();
        using var sink = Logger.Sink.Override((message, channel) => lines.Enqueue(message));
        var quiet = new StrongBox<bool>(true);
        var runner = new HangWatchdog.Runner((window, main) => "", () => scratch.FullPath, key => null, () => Volatile.Read(ref quiet.Value));
        runner.Start();
        for (var tick = 0; tick < 20; tick++) Step(runtime);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.InRange(runtime.Loop.Pending, 0, QuietPings);
        Assert.Empty(lines);

        // Seen again: the pings resume, and main's time to answer counts from now (the quiet spell is no stall).
        Volatile.Write(ref quiet.Value, false);
        var pending = runtime.Loop.Pending;
        for (var tick = 0; tick < 7; tick++) Step(runtime);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.Equal(pending + 7, runtime.Loop.Pending);
        Assert.Empty(lines);
        runtime.RunUntilIdle();
        Assert.Equal(runtime.Time.GetUtcNow(), runner.State.LastPong);
    }

    // A main thread that stops while the app is unseen cannot refresh the presence; Windows' hung-window check ends the quiet
    // instead (WindowPresence.Hung, from the watchdog's thread). From then on main is pinged, and its silence is reported.
    [Fact]
    public void A_hang_that_begins_while_quiet_is_reported_once_the_quiet_ends()
    {
        using var runtime = new TestRuntime();
        using var scratch = new ScratchFolder();
        var lines = new ConcurrentQueue<string>();
        using var sink = Logger.Sink.Override((message, channel) => lines.Enqueue(message));
        var quiet = new StrongBox<bool>(true);
        var runner = new HangWatchdog.Runner((window, main) => "STACKS\n", () => scratch.FullPath, key => null, () => Volatile.Read(ref quiet.Value));
        runner.Start();
        for (var tick = 0; tick < 20; tick++) Step(runtime);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.Empty(lines);

        // Main has stopped (the test never runs its loop again); the hung check ends the quiet.
        Volatile.Write(ref quiet.Value, false);
        for (var tick = 0; tick < 12; tick++) Step(runtime);
        Poll.Until(() => lines.Count == 2);
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        Assert.Equal("Main thread unresponsive for 4.0s, sampling", lines.ToArray()[0]);
        Assert.StartsWith("Hang report written to ", lines.ToArray()[1], StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(HangWatchdog.ReportsFolder(scratch.FullPath), "hang-*.txt"));
    }

    private static void Step(TestRuntime runtime)
    {
        Poll.Until(() => runtime.Time.ActiveTimers == 1);
        runtime.Time.Advance(TimeSpan.FromMilliseconds(500));
    }
}
