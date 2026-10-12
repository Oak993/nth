using System.IO.Pipes;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a and 10, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The whole app side in one line: a client on
// a real named pipe, the server nth listens with, the host that decides, and the shell that quits, on a real AppModel whose threads run. The
// client speaks the wire by hand (the setup's own client is tested in Marrow.Setup.Tests against this same server). The test main loop is pumped
// while a request waits for it, as the app's own main loop is. The pipe names are random, so no real nth is touched.
[Collection("MCPHub processes")]
public sealed class InstallerQuitOverPipeTests
{
    private static string NewPipeName() => "nth-test-" + Guid.NewGuid().ToString("N") + "-control";

    private static async Task<NthControlResponse?> SendAsync(string pipeName, NthControlRequest request)
    {
        using var window = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(window.Token);
        await NthControl.WriteFrameAsync(client, request.ToJson(), window.Token);
        var bytes = await NthControl.ReadFrameAsync(client, window.Token);
        if (bytes is null) return null;
        try
        {
            await NthControl.WriteAckAsync(client, window.Token); // what the installer does after it read the reply: nth quits only after it
        }
        catch (IOException)
        {
            // The server needed no acknowledgement and let go.
        }
        return NthControlResponse.Parse(bytes);
    }

    // The request waits for the app's main loop, which only runs when the test lets it.
    private static NthControlResponse Ask(NthControlRig rig, string pipeName, NthControlRequest request)
    {
        var asking = Task.Run(() => SendAsync(pipeName, request));
        rig.H.Main.Loop.RunUntil(() => asking.IsCompleted, AppModelHarness.Bound);
        return asking.GetAwaiter().GetResult() ?? throw new InvalidOperationException("nth closed the pipe without an answer.");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void An_idle_nth_says_ok_over_the_pipe_and_quits_the_way_closing_its_window_does()
    {
        using var rig = new NthControlRig("Idle thread");
        var name = NewPipeName();
        using var server = NthControlServer.Start(name, rig.Host);

        var status = Ask(rig, name, NthControlRequest.Status);
        Assert.Equal(NthControlReply.Status, status.Reply);
        Assert.Equal(4242, status.Pid);
        Assert.Empty(rig.Recorder.Calls);

        var answer = Ask(rig, name, NthControlRequest.QuitIfIdle);
        Assert.Equal(NthControlReply.Ok, answer.Reply);
        rig.H.Main.Loop.RunUntil(() => rig.Recorder.Exits > 0, AppModelHarness.Bound); // after the answer was read, the exit

        Assert.Equal(["exit"], rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_working_nth_says_busy_over_the_pipe_with_its_threads_and_stays_until_it_is_told_to_stop()
    {
        using var rig = new NthControlRig("Refactor the sign-in module", "Write the release notes");
        rig.SetRunning(0, TimeSpan.FromMinutes(4));
        rig.SetRunning(1, TimeSpan.FromMinutes(1));
        var name = NewPipeName();
        using var server = NthControlServer.Start(name, rig.Host);

        var busy = Ask(rig, name, NthControlRequest.QuitIfIdle);
        rig.H.Main.RunUntilIdle();

        Assert.Equal(NthControlReply.Busy, busy.Reply);
        Assert.Equal(["Refactor the sign-in module", "Write the release notes"], busy.Work.Select(item => item.Name));
        Assert.Equal(2, busy.WorkCount);
        Assert.Empty(rig.Recorder.Calls); // nothing was stopped by asking

        var stop = Ask(rig, name, NthControlRequest.QuitAndStop);
        rig.H.Main.Loop.RunUntil(() => rig.Recorder.Exits > 0, AppModelHarness.Bound);

        Assert.Equal(NthControlReply.Ok, stop.Reply);
        Assert.Equal(["exit"], rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_team_merge_is_waited_for_over_the_pipe_and_the_installer_giving_up_calls_the_quit_off()
    {
        using var rig = new NthControlRig("Ana");
        rig.SetMerging(0, true, TimeSpan.FromSeconds(30));
        var name = NewPipeName();
        using var server = NthControlServer.Start(name, rig.Host);

        var stop = Ask(rig, name, NthControlRequest.QuitAndStop);
        Assert.Equal(NthControlReply.Ok, stop.Reply);
        Assert.Equal(NthWorkKind.Merge, stop.Deferred); // the installer learns nth first finishes the merge
        rig.H.Main.RunUntilIdle();
        rig.H.Main.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(rig.Recorder.Calls);

        var during = Ask(rig, name, NthControlRequest.Status);
        Assert.Equal(NthWorkKind.Merge, Assert.Single(during.Work).Kind); // asking again shows it is still merging

        var withdrawn = Ask(rig, name, NthControlRequest.Withdraw);
        Assert.Equal(NthControlReply.Ok, withdrawn.Reply);
        rig.SetMerging(0, false);
        rig.H.Main.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(rig.Recorder.Calls); // the merge ended and nth stayed open: the update was given up
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_request_that_is_not_ours_gets_a_refusal_and_never_reaches_the_app()
    {
        using var rig = new NthControlRig("Idle thread");
        var name = NewPipeName();
        using var server = NthControlServer.Start(name, rig.Host);

        var asking = Task.Run(async () =>
        {
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(window.Token);
            await NthControl.WriteFrameAsync(client, "{\"v\":1,\"cmd\":\"quit\"}", window.Token); // a quit with no mode is not a request
            return await NthControl.ReadFrameAsync(client, window.Token);
        });
        rig.H.Main.Loop.RunUntil(() => asking.IsCompleted, AppModelHarness.Bound);

        var answer = NthControlResponse.Parse((await asking)!);
        Assert.NotNull(answer);
        Assert.Equal(NthControlReply.Refused, answer.Reply);
        Assert.Equal("unsupported", answer.Reason);
        rig.H.Main.RunUntilIdle();
        Assert.Empty(rig.Recorder.Calls);
    }
}
