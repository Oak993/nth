using System.Text;
using Marrow.Core.Support;
using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

public sealed class C7QuitIdWireTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Quit_and_withdraw_ids_round_trip_and_malformed_ids_are_rejected()
    {
        var id = Guid.NewGuid().ToString("N");
        foreach (var request in new[] { NthControlRequest.QuitIfIdle, NthControlRequest.QuitAndStop, NthControlRequest.Withdraw })
        {
            var tagged = request with { Id = id };
            Assert.Equal(tagged, NthControlRequest.Parse(Encoding.UTF8.GetBytes(tagged.ToJson())));
        }
        Assert.Null(NthControlRequest.Parse(Encoding.UTF8.GetBytes("{\"v\":1,\"cmd\":\"withdraw\",\"id\":\"other\"}")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_response_for_a_different_quit_id_is_not_acknowledged()
    {
        var peer = new RunningNthProcess(4242, @"C:\Apps\nth\current\nth.exe", 2000);
        var request = NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") };
        var response = new NthControlResponse(NthControlReply.Ok, peer.ProcessId, peer.ExecutablePath, [], 0, Id: Guid.NewGuid().ToString("N"));
        using var stream = new ReviewDuplexStream(NthControl.Frame(response.ToJson()));
        var answer = await PipeNthControlChannel.SendConnectedAsync(stream, peer, request, default, @"C:\Apps\nth", _ => true);
        Assert.Null(answer.Response);
        Assert.Equal(NthControlFailure.BadAnswer, answer.Failure);
        Assert.Equal(NthControl.Frame(request.ToJson()), stream.Written.ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_final_busy_outcome_for_another_id_is_rejected_before_the_third_ack()
    {
        var peer = new RunningNthProcess(4242, @"C:\Apps\nth\current\nth.exe", 2000);
        var request = NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") };
        var initial = new NthControlResponse(NthControlReply.Ok, peer.ProcessId, peer.ExecutablePath, [], 0, CommitRequired: true, Id: request.Id);
        var prepared = initial with { CommitRequired = false };
        var busy = prepared with { Reply = NthControlReply.Busy, Id = Guid.NewGuid().ToString("N") };
        using var stream = new ReviewDuplexStream([.. NthControl.Frame(initial.ToJson()), .. NthControl.Frame(prepared.ToJson()), .. NthControl.Frame(busy.ToJson())]);
        var answer = await PipeNthControlChannel.SendConnectedAsync(stream, peer, request, default, @"C:\Apps\nth", _ => true);
        Assert.Null(answer.Response);
        Assert.Equal(NthControlFailure.BadAnswer, answer.Failure);
        Assert.Equal(new byte[] { NthControl.AckByte, NthControl.AckByte }, stream.Written.ToArray()[^2..]);
        Assert.Equal(NthControl.Frame(request.ToJson()).Length + 2, stream.Written.Length);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Each_setup_quit_has_a_fresh_id_and_cancellation_withdraws_the_last_exact_id()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;
        rig.Nth.ExitDelay = null;
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);
        Assert.Equal(RunningNthState.NotResponding, (await rig.Time.RunAsync(rig.Handshake.StopWorkAndQuitAsync())).State);
        await rig.Handshake.CancelAsync();
        var quits = rig.Nth.Requests.Where(request => request.Command == NthControlCommand.Quit).ToArray();
        Assert.Equal(2, quits.Length);
        Assert.All(quits, request => Assert.True(Guid.TryParseExact(request.Id, "N", out _)));
        Assert.NotEqual(quits[0].Id, quits[1].Id);
        Assert.Equal(quits[1].Id, rig.Nth.Requests.Last().Id);
        Assert.Equal(NthControlCommand.Withdraw, rig.Nth.Requests.Last().Command);
    }
}
