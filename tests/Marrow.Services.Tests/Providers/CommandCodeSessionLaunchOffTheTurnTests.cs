#if DEBUG
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-perf-ui-thread-7): CommandCodeSession.Send made the `cmd` child of every turn with CreateProcessW on the caller's turn, which is the UI thread's. It now awaits
// StdioProcess.StartAsync. The held launch (HeldLaunch) shows the turn wait for the child with nothing blocked, written to or announced; a Stop that comes inside that window ends the child once it
// exists and the session announces nothing; an Interrupt there still closes the turn as interrupted; a launch that fails keeps the "Command Code could not start: " message (held by
// CommandCodeSessionLaunchTests.A_launch_that_cannot_start_clears_the_turn_and_reports_the_wrapped_message, which now runs through the awaited launch). The children are the compiled Command Code
// stand-in (R-TEST-8, R-TEST-10), never a real CLI. Like the other rig users the class joins the non-parallel collection of the redirected data root (R-TEST-5).
[Collection("Dev data root env")]
public sealed class CommandCodeSessionLaunchOffTheTurnTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("Decision", "ED-127")] // the launcher it keeps off the turn (ProcessLauncher.Run -> CreateProcessW); ED-NEW-perf-ui-thread-7
    public async Task The_turn_waits_for_the_launch_instead_of_making_it_and_goes_on_when_the_child_exists()
    {
        using var rig = new CommandCodeSessionRig("launch-held");
        using var held = new HeldLaunch(rig.Runtime.Loop);
        await rig.Session.Start();
        Task? send = null;

        rig.Runtime.OnMain(() => { send = rig.Session.Send(CommandCodeSessionRig.Turn()); });
        Assert.Equal(1, held.Requested);         // the turn asked for the launch
        Assert.Equal(0, held.Launched);          // and did not make it itself
        Assert.False(send!.IsCompleted);         // it waits for the child with the loop idle: nothing is blocked
        Assert.Null(rig.Field("readTask"));      // nothing reads from a child that does not exist yet
        Assert.Empty(rig.Events);                // and nothing was announced

        held.Release();
        rig.Runtime.Loop.Run(() => send, Bound);
        Assert.Equal(1, held.Launched);
        Assert.Equal(0, held.LaunchedOnTheLoop);
        Assert.IsType<ProviderEvent.TurnStarted>(rig.Events[0]);
        rig.WaitForTurns(1);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public async Task A_stop_that_comes_while_the_child_is_being_created_ends_it_once_it_exists_and_the_session_announces_nothing()
    {
        using var rig = new CommandCodeSessionRig("launch-stop");
        using var held = new HeldLaunch(rig.Runtime.Loop);
        rig.Hold(); // the stand-in would keep its child for 60 s after its output: only a Terminate ends it sooner
        await rig.Session.Start();
        Task? send = null;

        rig.Runtime.OnMain(() => { send = rig.Session.Send(CommandCodeSessionRig.Turn()); });
        var child = held.Child();
        rig.Runtime.OnMain(rig.Session.Stop);    // the child does not exist yet: this Stop ends nothing
        Assert.False(child.IsRunning);
        Assert.Equal(0, held.Launched);

        held.Release();
        rig.Runtime.Loop.Run(() => send!, Bound); // the send ends without an error
        Assert.Equal(1, held.Launched);
        Assert.True(child.ProcessIdentifier > 0);
        HeldLaunch.AssertEndedByTerminate(rig.Runtime, child); // the held stand-in would stay up for 60 s: it is the session's Terminate that ends it
        Assert.Empty(rig.Events);                // no TurnStarted and no TurnCompleted after a Stop
        Assert.False(rig.Session.IsRunning);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public async Task An_interrupt_that_comes_while_the_child_is_being_created_still_closes_the_turn_as_interrupted()
    {
        using var rig = new CommandCodeSessionRig("launch-interrupt");
        using var held = new HeldLaunch(rig.Runtime.Loop);
        rig.Hold(); // the child would otherwise stay up for 60 s: the turn only closes because the session ends it
        await rig.Session.Start();
        Task? send = null;

        rig.Runtime.OnMain(() => { send = rig.Session.Send(CommandCodeSessionRig.Turn()); });
        rig.Runtime.OnMain(() => { _ = rig.Session.Interrupt(); }); // nothing is running yet to end
        Assert.Empty(rig.Events);

        held.Release();
        rig.Runtime.Loop.Run(() => send!, Bound);
        rig.WaitFor(() => rig.Events.OfType<ProviderEvent.TurnCompleted>().Any(), "the interrupted turn to close", seconds: 15);

        Assert.IsType<ProviderEvent.TurnStarted>(rig.Events[0]);
        var end = Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]);
        Assert.Equal(TurnStatus.Interrupted, end.Status);
    }
}
#endif
