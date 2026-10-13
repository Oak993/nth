using System.Text;
using Marrow.Setup.Port;
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
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal("{\"v\":1,\"cmd\":\"status\"}", server.Requests[0]);
        Assert.Matches(@"^\{""v"":1,""cmd"":""quit"",""mode"":""if-idle"",""id"":""[a-f0-9]{32}""\}$", server.Requests[1]);
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
        Assert.Equal([rig.Nth.Main.ProcessId], rig.Nth.Ended);
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
        var executable = Environment.ProcessPath!;
        var root = Path.GetDirectoryName(executable)!;
        var nth = new FakeNth(TimeProvider.System, root, new RunningNthProcess(Environment.ProcessId, executable, 1000));
        var scenario = new C7QuitReentryChannel(nth, false);
        var name = FakeNthPipeServer.NewName();
        FakeNthPipeServer? server = null;
        server = new FakeNthPipeServer(name, async (pipe, token) =>
        {
            var request = NthControlRequest.Parse(Encoding.UTF8.GetBytes(server!.Requests.Last()))!;
            var exchange = await scenario.SendAsync(request, token);
            await NthControl.WriteFrameAsync(pipe, exchange.Response!.ToJson(), token);
        });
        using var ownedServer = server;
        var channel = new PipeNthControlChannel(name, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), root);
        var clock = new HoldableTime();
        var handshake = new RunningNthHandshake(root, channel, nth, clock, PipeRig.Quick);
        Assert.Equal(RunningNthState.NotResponding, (await handshake.AskAsync()).State);
        Assert.True(scenario.EarlierQuitPending);
        var original = Assert.Single(scenario.Requests, request => request.Command == NthControlCommand.Quit);
        Assert.Equal(RunningNthState.Busy, (await handshake.AskAsync()).State);
        var asked = scenario.Requests.Count(request => request.Command == NthControlCommand.Quit);
        // VELOPACK_CHECK.md finding 14 (ED-NEW-inst-fix-9): no 350 ms race with the poll. The wait polls twice over the pipe (each
        // answered busy), and its third pause never ends, so Stop waiting comes while no poll is in flight, every run.
        clock.HoldAfter(2);
        var waiting = handshake.WaitForWorkToFinishAsync();
        await clock.Held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, scenario.Requests.Count(request => request.Command == NthControlCommand.Quit) - asked);

        await handshake.CancelAsync();

        Assert.Equal(RunningNthState.Cancelled, (await waiting).State);
        Assert.True(nth.Running);
        Assert.DoesNotContain(scenario.Requests, request => request.Command == NthControlCommand.Quit && request.Mode == NthQuitMode.Stop);
        var withdrawn = Assert.Single(scenario.Requests, request => request.Command == NthControlCommand.Withdraw);
        Assert.Equal(original.Id, withdrawn.Id);
        Assert.False(scenario.EarlierQuitPending);
        Assert.Empty(nth.Ended);
    }
}
