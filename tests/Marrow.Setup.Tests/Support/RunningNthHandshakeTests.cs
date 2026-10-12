using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a and 10, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The running-nth handshake the screens
// drive, one test per case the plan names, on a pretend nth and a fake clock: nth closed; nth open and agrees; busy then wait; busy then stop;
// agrees but hangs (the setup asks before it ends it, and ends it only on yes); refuses. Every wait of 15 s costs no real time.
public sealed class RunningNthHandshakeTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Closed_nth_means_go_ahead_and_nothing_is_asked_of_anything()
    {
        var rig = new HandshakeRig(running: false);

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Clear, status.State);
        Assert.True(status.CanProceed);
        Assert.Empty(rig.Nth.Sent);
        Assert.Empty(rig.Nth.Ended);
        Assert.Equal([RunningNthState.Asking, RunningNthState.Clear], rig.States);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_update_screen_can_tell_whether_nth_is_open_without_asking_it_anything()
    {
        var open = new HandshakeRig();
        var closed = new HandshakeRig(running: false);

        Assert.True(await open.Handshake.IsNthOpenAsync());
        Assert.False(await closed.Handshake.IsNthOpenAsync());
        Assert.Empty(open.Nth.Sent);
        Assert.Equal(RunningNthState.NotAsked, open.Handshake.Status.State);
        Assert.Empty(open.Seen);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Open_nth_that_is_idle_agrees_saves_and_is_gone_so_the_install_goes_ahead()
    {
        var rig = new HandshakeRig();

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.True(status.CanProceed);
        Assert.False(rig.Nth.Running);
        Assert.Equal(["status", "quit:if-idle"], rig.Nth.Sent); // it is looked at before it is told anything
        Assert.Empty(rig.Nth.Ended);
        Assert.Equal([RunningNthState.Asking, RunningNthState.Closing, RunningNthState.Closed], rig.States);
        Assert.True(rig.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Busy_nth_is_not_asked_to_quit_and_the_work_is_listed_with_nothing_changed()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Busy, status.State);
        Assert.False(status.CanProceed);
        Assert.Equal(2, status.WorkCount);
        Assert.Equal(["Refactor the sign-in module", "Write the release notes"], status.Work.Select(item => item.Name));
        Assert.Equal([240, 60], status.Work.Select(item => item.Seconds));
        Assert.True(rig.Nth.Running);
        Assert.False(rig.Nth.Quitting);
        Assert.Equal(["status", "quit:if-idle"], rig.Nth.Sent);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Busy_then_wait_updates_when_the_work_ends_and_nth_quits_by_itself_the_moment_it_is_idle()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = HandshakeRig.Dwindling;
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);

        var status = await rig.Time.RunAsync(rig.Handshake.WaitForWorkToFinishAsync());

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.False(rig.Nth.Running);
        Assert.Empty(rig.Nth.Ended);
        Assert.Equal(0, rig.Nth.Count("quit:stop")); // waiting never stops anyone's work
        // The list shrinks on the screen as the work ends: two threads, then one, then nth closes.
        var afterBusy = rig.Seen.SkipWhile(item => item.State != RunningNthState.Busy).Skip(1).ToList();
        Assert.Equal([RunningNthState.Waiting, RunningNthState.Waiting, RunningNthState.Closing, RunningNthState.Closed], afterBusy.Select(item => item.State));
        Assert.Equal([2, 1], afterBusy.Where(item => item.State == RunningNthState.Waiting).Select(item => item.WorkCount));
        Assert.InRange(rig.Elapsed, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(13)); // idle at 9 s, noticed at the next ask, gone 0.3 s later
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Busy_then_stop_tells_nth_to_stop_what_runs_save_and_quit_and_waits_for_it_to_be_gone()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads; // the work never ends by itself
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);

        var status = await rig.Time.RunAsync(rig.Handshake.StopWorkAndQuitAsync());

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.False(rig.Nth.Running);
        Assert.Equal(["status", "quit:if-idle", "quit:stop"], rig.Nth.Sent);
        Assert.Empty(rig.Nth.Ended); // stopping the work is nth's own doing, not TerminateProcess
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Agrees_but_hangs_asks_after_fifteen_seconds_and_ends_nothing_without_a_yes()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null; // agrees, then never leaves

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.NotResponding, status.State);
        Assert.Equal("did-not-exit", status.Reason);
        Assert.InRange(rig.Elapsed, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15.2));
        Assert.True(rig.Nth.Running);
        Assert.Empty(rig.Nth.Ended); // it asks first: nothing is forced
        Assert.False(status.CanProceed);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Agrees_but_hangs_then_a_yes_ends_it_and_the_install_goes_ahead()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        Assert.Equal(RunningNthState.NotResponding, (await rig.AskAsync()).State);

        var status = await rig.Time.RunAsync(rig.Handshake.EndItAsync());

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Equal([4242], rig.Nth.Ended);
        Assert.False(rig.Nth.Running);
        Assert.Contains(RunningNthState.Ending, rig.States);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Agrees_but_hangs_then_a_no_waits_another_fifteen_seconds_and_still_forces_nothing()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        Assert.Equal(RunningNthState.NotResponding, (await rig.AskAsync()).State);
        var asked = rig.Elapsed;

        var status = await rig.Time.RunAsync(rig.Handshake.KeepWaitingAsync());

        Assert.Equal(RunningNthState.NotResponding, status.State);
        Assert.InRange(rig.Elapsed - asked, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15.2));
        Assert.Empty(rig.Nth.Ended);
        Assert.Equal(1, rig.Nth.Count("quit:if-idle")); // it was told once; waiting does not tell it again
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_hung_nth_that_leaves_while_the_person_is_deciding_is_simply_closed()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        Assert.Equal(RunningNthState.NotResponding, (await rig.AskAsync()).State);
        rig.Nth.Close(); // it finished saving after all

        var status = await rig.Time.RunAsync(rig.Handshake.KeepWaitingAsync());

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Refuses_means_stop_here_and_nothing_is_closed_or_ended()
    {
        var rig = new HandshakeRig();
        rig.Nth.RefuseWith = "not-allowed";

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Refused, status.State);
        Assert.Equal("not-allowed", status.Reason);
        Assert.False(status.CanProceed);
        Assert.True(rig.Nth.Running);
        Assert.Empty(rig.Nth.Ended);
        Assert.Equal(["status", "quit:if-idle"], rig.Nth.Sent);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_refusal_can_be_asked_again_so_Retry_works_after_the_person_changed_something()
    {
        var rig = new HandshakeRig();
        rig.Nth.RefuseWith = "starting";
        Assert.Equal(RunningNthState.Refused, (await rig.AskAsync()).State);
        rig.Nth.RefuseWith = null;

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Closed, status.State);
    }
}
