using Marrow.Core.Support;
using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a and 10, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The setup's end of nth's control pipe on
// a real named pipe: a fake pipe server that answers, stays silent, closes at once or sends rubbish, and the app's own server for the full round
// trip. Pipe names are random, so no test reaches a real nth. WINDOWS-ONLY (named pipes with CurrentUserOnly).
public sealed class PipeNthControlChannelTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(10);

    private static string PeerRoot => Path.GetDirectoryName(Environment.ProcessPath!)!;
    private static NthControlResponse OkAnswer() => new(NthControlReply.Ok, Environment.ProcessId, Environment.ProcessPath!, [], 0);

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Every_request_goes_out_as_the_wire_says_and_the_answer_is_read_back()
    {
        var name = FakeNthPipeServer.NewName();
        var busy = new NthControlResponse(NthControlReply.Busy, Environment.ProcessId, Environment.ProcessPath!, [new NthWorkItem("Refactor the sign-in module", NthWorkKind.Thread, 240)], 1);
        using var server = new FakeNthPipeServer(name, FakeNthPipeServer.Answer(busy));
        var channel = new PipeNthControlChannel(name, Patient, Patient, PeerRoot);

        var first = await channel.SendAsync(NthControlRequest.QuitIfIdle, CancellationToken.None);
        await channel.SendAsync(NthControlRequest.Status, CancellationToken.None);
        await channel.SendAsync(NthControlRequest.QuitAndStop, CancellationToken.None);
        await channel.SendAsync(NthControlRequest.Withdraw, CancellationToken.None);

        Assert.Equal(NthControlFailure.None, first.Failure);
        Assert.Equal(NthControlReply.Busy, first.Response!.Reply);
        Assert.Equal("Refactor the sign-in module", Assert.Single(first.Response.Work).Name);
        Assert.Equal(
            ["{\"v\":1,\"cmd\":\"quit\",\"mode\":\"if-idle\"}", "{\"v\":1,\"cmd\":\"status\"}", "{\"v\":1,\"cmd\":\"quit\",\"mode\":\"stop\"}", "{\"v\":1,\"cmd\":\"withdraw\"}"],
            server.Requests);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task No_pipe_is_reported_after_the_connect_time_so_an_older_nth_is_not_waited_for_long()
    {
        var channel = new PipeNthControlChannel(FakeNthPipeServer.NewName(), Short, Patient);
        var started = DateTime.UtcNow;

        var result = await channel.SendAsync(NthControlRequest.Status, CancellationToken.None);

        Assert.Equal(NthControlFailure.NoPipe, result.Failure);
        Assert.Null(result.Response);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_pipe_that_stays_silent_is_no_answer_after_the_reply_time_and_one_that_closes_at_once_is_no_answer_now()
    {
        var silent = FakeNthPipeServer.NewName();
        using var hung = new FakeNthPipeServer(silent, FakeNthPipeServer.Silent());
        var closing = FakeNthPipeServer.NewName();
        using var gone = new FakeNthPipeServer(closing, FakeNthPipeServer.CloseAtOnce());

        var waited = await new PipeNthControlChannel(silent, Patient, Short, PeerRoot).SendAsync(NthControlRequest.QuitIfIdle, CancellationToken.None);
        var closed = await new PipeNthControlChannel(closing, Patient, Patient, PeerRoot).SendAsync(NthControlRequest.QuitIfIdle, CancellationToken.None);

        Assert.Equal(NthControlFailure.NoAnswer, waited.Failure);
        Assert.Equal(NthControlFailure.NoAnswer, closed.Failure);
        Assert.Equal(["{\"v\":1,\"cmd\":\"quit\",\"mode\":\"if-idle\"}"], hung.Requests); // the request did arrive: nth is hung, not absent
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task What_is_not_a_version_1_answer_is_a_bad_answer_and_what_cannot_be_framed_is_no_answer()
    {
        var rubbish = FakeNthPipeServer.NewName();
        using var first = new FakeNthPipeServer(rubbish, (pipe, token) => NthControl.WriteFrameAsync(pipe, "this is not json", token));
        var newer = FakeNthPipeServer.NewName();
        using var second = new FakeNthPipeServer(newer, (pipe, token) => NthControl.WriteFrameAsync(pipe, "{\"v\":2,\"reply\":\"ok\"}", token));
        var huge = FakeNthPipeServer.NewName();
        using var third = new FakeNthPipeServer(huge, FakeNthPipeServer.SendBytes([0x01, 0x00, 0x01, 0x00, 1, 2, 3])); // claims 65,537 bytes

        Assert.Equal(NthControlFailure.BadAnswer, (await new PipeNthControlChannel(rubbish, Patient, Patient, PeerRoot).SendAsync(NthControlRequest.Status, CancellationToken.None)).Failure);
        Assert.Equal(NthControlFailure.BadAnswer, (await new PipeNthControlChannel(newer, Patient, Patient, PeerRoot).SendAsync(NthControlRequest.Status, CancellationToken.None)).Failure);
        Assert.Equal(NthControlFailure.NoAnswer, (await new PipeNthControlChannel(huge, Patient, Patient, PeerRoot).SendAsync(NthControlRequest.Status, CancellationToken.None)).Failure);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Cancelling_ends_a_wait_for_an_answer_at_once_and_is_the_only_thing_that_throws()
    {
        var name = FakeNthPipeServer.NewName();
        using var server = new FakeNthPipeServer(name, FakeNthPipeServer.Silent());
        using var cancel = new CancellationTokenSource();
        var pending = new PipeNthControlChannel(name, Patient, Patient, PeerRoot).SendAsync(NthControlRequest.Status, cancel.Token);
        await Task.Delay(200);
        Assert.False(pending.IsCompleted);

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Patient));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task The_setups_client_and_the_apps_server_understand_each_other_end_to_end()
    {
        var name = FakeNthPipeServer.NewName();
        var busy = new NthControlResponse(NthControlReply.Busy, Environment.ProcessId, Environment.ProcessPath!, [new NthWorkItem("Write the release notes", NthWorkKind.Thread, 61)], 1);
        var afterRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new ScriptedNthHost(request => request.Command switch
        {
            NthControlCommand.Status => new NthControlAnswer(OkAnswer() with { Reply = NthControlReply.Status }),
            NthControlCommand.Quit when request.Mode == NthQuitMode.IfIdle => new NthControlAnswer(busy),
            _ => new NthControlAnswer(OkAnswer(), () => afterRan.TrySetResult()),
        });
        using var server = NthControlServer.Start(name, host);
        var channel = new PipeNthControlChannel(name, Patient, Patient, PeerRoot);

        var status = await channel.SendAsync(NthControlRequest.Status, CancellationToken.None);
        var idle = await channel.SendAsync(NthControlRequest.QuitIfIdle, CancellationToken.None);
        Assert.False(afterRan.Task.IsCompleted); // nothing quits on a busy answer
        var stop = await channel.SendAsync(NthControlRequest.QuitAndStop, CancellationToken.None);
        await afterRan.Task.WaitAsync(Patient); // and a stop quits once its answer was read

        Assert.Equal(NthControlReply.Status, status.Response!.Reply);
        Assert.Equal(Environment.ProcessId, status.Response.Pid);
        Assert.Equal(Environment.ProcessPath, status.Response.Exe);
        Assert.Equal(NthControlReply.Busy, idle.Response!.Reply);
        Assert.Equal(61, Assert.Single(idle.Response.Work).Seconds);
        Assert.Equal(NthControlReply.Ok, stop.Response!.Reply);
        Assert.Equal([NthControlRequest.Status, NthControlRequest.QuitIfIdle, NthControlRequest.QuitAndStop], host.Requests);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void The_channel_for_the_signed_in_user_exists_and_a_development_build_has_its_own()
    {
        Assert.NotNull(PipeNthControlChannel.ForCurrentUser());
        Assert.NotNull(PipeNthControlChannel.ForCurrentUser(development: true));
        Assert.Equal(TimeSpan.FromSeconds(2), PipeNthControlChannel.DefaultConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), PipeNthControlChannel.DefaultReplyTimeout);
    }
}
