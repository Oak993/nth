using System.Text.Json.Nodes;
using Marrow.Core.Support;
using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

public sealed class C7PipeQuitCommitTests
{
    // ED-NEW-inst-c7-5: feeds the proposed optional commit marker on the real connected client path.
    // It compiles at the committed F2 head; no production phase/API scaffold is required for its red.
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_last_commit_busy_reply_overrides_the_earlier_idle_ok_on_the_actual_client_path()
    {
        var peer = new RunningNthProcess(4242, @"C:\Apps\nth\current\nth.exe", 2000);
        var initial = new NthControlResponse(NthControlReply.Ok, peer.ProcessId, peer.ExecutablePath, [], 0);
        var document = JsonNode.Parse(initial.ToJson())!.AsObject();
        document["commit"] = true;
        var prepared = initial;
        var busy = prepared with { Reply = NthControlReply.Busy, Work = [new("A late turn", NthWorkKind.Thread, 1)], WorkCount = 1 };
        using var stream = new ReviewDuplexStream([.. NthControl.Frame(document.ToJsonString()),
            .. NthControl.Frame(prepared.ToJson()), .. NthControl.Frame(busy.ToJson())]);

        var answer = await PipeNthControlChannel.SendConnectedAsync(stream, peer, NthControlRequest.QuitIfIdle,
            CancellationToken.None, @"C:\Apps\nth", _ => true);

        Assert.Equal(NthControlReply.Busy, answer.Response!.Reply);
        Assert.Equal("A late turn", Assert.Single(answer.Response.Work).Name);
        Assert.Equal(new byte[] { NthControl.AckByte, NthControl.AckByte, NthControl.AckByte }, stream.Written.ToArray()[^3..]);
    }
}
