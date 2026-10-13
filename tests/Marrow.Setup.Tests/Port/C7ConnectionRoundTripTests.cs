using Marrow.Core.Support;
using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

public sealed class C7ConnectionRoundTripTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_real_connected_client_waits_for_server_commit_before_it_closes_the_transport()
    {
        var pair = DuplexMemoryPipe.Create();
        using var client = pair.Client; using var server = pair.Server;
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = new RunningNthProcess(4242, @"C:\Apps\nth\current\nth.exe", 2000);
        var request = NthControlRequest.QuitAndStop with { Id = Guid.NewGuid().ToString("N") };
        var host = new ScriptedNthHost(_ => new NthControlAnswer(
            new NthControlResponse(NthControlReply.Ok, peer.ProcessId, peer.ExecutablePath, [], 0, Id: request.Id),
            Commit: async token => { entered.SetResult(); await release.Task.WaitAsync(token); return null; }));
        var serving = NthControlConnection.ServeAsync(server, host, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), bound.Token, default);
        var asking = PipeNthControlChannel.SendConnectedAsync(client, peer, request, bound.Token, @"C:\Apps\nth", _ => true);
        try
        {
            await entered.Task.WaitAsync(bound.Token);
            Assert.False(asking.IsCompleted);
            release.SetResult();
            await serving.WaitAsync(bound.Token);
            var answer = await asking.WaitAsync(bound.Token);
            Assert.Equal(NthControlReply.Ok, answer.Response!.Reply);
            Assert.Equal(request.Id, answer.Response.Id);
        }
        finally { release.TrySetResult(); bound.Cancel(); await serving; }
    }
}
