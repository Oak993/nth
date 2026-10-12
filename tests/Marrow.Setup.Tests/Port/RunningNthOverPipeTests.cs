using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The plan's "running nth" cases once more, with the
// handshake talking to a fake pipe server over a real named pipe on the real clock (short waits): closed, open and agrees, busy then wait,
// busy then stop, agrees but hangs (asked before it is ended), refuses. The same cases run on a fake clock in RunningNthHandshakeTests; here
// the bytes really cross a pipe. WINDOWS-ONLY.
public sealed class RunningNthOverPipeTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Closed_nth_means_nothing_crosses_the_pipe()
    {
        var rig = PipeRig.Create(running: false);
        using var server = rig.Server;

        var status = await rig.Handshake.AskAsync();

        Assert.Equal(RunningNthState.Clear, status.State);
        Assert.Empty(server.Requests);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Open_nth_agrees_over_the_pipe_and_is_gone()
    {
        var rig = PipeRig.Create();
        using var server = rig.Server;

        var status = await rig.Handshake.AskAsync();

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Equal(["{\"v\":1,\"cmd\":\"status\"}", "{\"v\":1,\"cmd\":\"quit\",\"mode\":\"if-idle\"}"], server.Requests);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Busy_then_wait_over_the_pipe_ends_when_the_work_does()
    {
        var rig = PipeRig.Create();
        using var server = rig.Server;
        rig.Nth.Work = at => at < TimeSpan.FromMilliseconds(600) ? HandshakeRig.TwoThreads : [];
        var busy = await rig.Handshake.AskAsync();
        Assert.Equal(RunningNthState.Busy, busy.State);
        Assert.Equal(["Refactor the sign-in module", "Write the release notes"], busy.Work.Select(item => item.Name));

        var status = await rig.Handshake.WaitForWorkToFinishAsync();

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Equal(0, rig.Nth.Count("quit:stop"));
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Busy_then_stop_over_the_pipe_sends_stop_and_nth_leaves()
    {
        var rig = PipeRig.Create();
        using var server = rig.Server;
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;
        Assert.Equal(RunningNthState.Busy, (await rig.Handshake.AskAsync()).State);

        var status = await rig.Handshake.StopWorkAndQuitAsync();

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Equal(["status", "quit:if-idle", "quit:stop"], rig.Nth.Sent);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Agrees_but_hangs_over_the_pipe_is_asked_about_and_ended_only_on_yes()
    {
        var rig = PipeRig.Create();
        using var server = rig.Server;
        rig.Nth.ExitDelay = null;

        var status = await rig.Handshake.AskAsync();

        Assert.Equal(RunningNthState.NotResponding, status.State);
        Assert.Empty(rig.Nth.Ended);
        Assert.True(rig.Nth.Running);

        var closed = await rig.Handshake.EndItAsync();

        Assert.Equal(RunningNthState.Closed, closed.State);
        Assert.Equal([4242], rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Refuses_over_the_pipe_stops_the_install_and_ends_nothing()
    {
        var rig = PipeRig.Create();
        using var server = rig.Server;
        rig.Nth.RefuseWith = "not-allowed";

        var status = await rig.Handshake.AskAsync();

        Assert.Equal(RunningNthState.Refused, status.State);
        Assert.Equal("not-allowed", status.Reason);
        Assert.True(rig.Nth.Running);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Stop_waiting_over_the_pipe_calls_off_the_wait_and_withdraws_the_request()
    {
        var rig = PipeRig.Create();
        using var server = rig.Server;
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;
        Assert.Equal(RunningNthState.Busy, (await rig.Handshake.AskAsync()).State);
        var waiting = rig.Handshake.WaitForWorkToFinishAsync();
        await Task.Delay(350);

        await rig.Handshake.CancelAsync();

        Assert.Equal(RunningNthState.Cancelled, (await waiting).State);
        Assert.True(rig.Nth.Running);
        Assert.Equal(0, rig.Nth.Count("quit:stop"));
        Assert.Contains("withdraw", rig.Nth.Sent);
    }
}
