using Marrow.App.Port;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port;

// PORT-ONLY unit under test: src/Marrow.App/Port/ReopenSignal.cs, the second-launch signal of ED-002: the first
// instance keeps a named event beside its single-instance lock; a second launch that finds the lock taken sets the
// event and exits; the first instance hears it and runs the reopen path (AppDelegate.swift:36-39) on main. Each test
// uses its own event name, never the app's (R-TEST-9).
public sealed class ReopenSignalTests
{
    private static string SignalName() => "Local\\Marrow.Tests.Reopen." + Guid.NewGuid().ToString("N");

    [Fact]
    [Trait("Decision", "ED-002")] // engineering_decisions.md ED-002
    [Trait("Req", "PR-LAUNCH-072")] // App/AppDelegate.swift:36-39 (windows_path: the second launch runs the reopen path)
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-WIN-072: the second launch reaches the running instance (no second process keeps running; the lock is DroppyCodeAppTests)
    [Trait("Req", "PR-WIN-072")] // App/AppDelegate.swift:36-39; engineering_decisions.md ED-002
    public void A_second_launch_reaches_the_running_instance_on_main_once_per_launch()
    {
        using var runtime = new TestRuntime();
        var name = SignalName();
        using var signal = ReopenSignal.Create(name);
        var heard = 0;
        var onMain = true;
        signal.Listen(() =>
        {
            onMain &= runtime.Loop.CheckAccess();
            heard++;
        });

        Assert.True(ReopenSignal.Send(name));
        runtime.Loop.RunUntil(() => heard == 1);
        Assert.True(ReopenSignal.Send(name));
        runtime.Loop.RunUntil(() => heard == 2);

        Assert.True(onMain);
    }

    [Fact]
    [Trait("Decision", "ED-002")] // engineering_decisions.md ED-002
    public void A_launch_made_while_the_running_instance_is_still_starting_is_heard_once_it_listens()
    {
        using var runtime = new TestRuntime();
        var name = SignalName();
        using var signal = ReopenSignal.Create(name);

        Assert.True(ReopenSignal.Send(name)); // before Listen: the event stays set
        var heard = 0;
        signal.Listen(() => heard++);

        runtime.Loop.RunUntil(() => heard == 1);
    }

    [Fact]
    [Trait("Decision", "ED-002")] // engineering_decisions.md ED-002
    public void With_no_running_instance_there_is_nobody_to_signal()
    {
        Assert.False(ReopenSignal.Send(SignalName()));
    }
}
