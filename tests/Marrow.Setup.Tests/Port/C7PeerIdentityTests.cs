using Marrow.Core.Support;
using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

public sealed class C7PeerIdentityTests
{
    // ED-NEW-inst-c7-4: retained uncertain discovery must not admit an unverifiable handle for forced exit.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unverifiable_retained_process_identity_is_never_admitted_for_forced_exit()
    {
        var original = new RunningNthProcess(4242, @"C:\Apps\nth\current\nth.exe", 1000);

        Assert.Equal(ProcessEndResult.Failed, WindowsRunningNthProcesses.CheckEndIdentity(original, original.ExecutablePath, null));
        Assert.Equal(ProcessEndResult.Failed, WindowsRunningNthProcesses.CheckEndIdentity(original, original.ExecutablePath, 0));
        Assert.Equal(ProcessEndResult.Failed, WindowsRunningNthProcesses.CheckEndIdentity(original, null, 1000));
        Assert.Equal(ProcessEndResult.Failed, WindowsRunningNthProcesses.CheckEndIdentity(original with { StartedAtTicks = 0 }, original.ExecutablePath, 1000));
        Assert.Equal(ProcessEndResult.Failed, WindowsRunningNthProcesses.CheckEndIdentity(original, @"E:\Unrelated\other.exe", 1000));
        Assert.Equal(ProcessEndResult.Gone, WindowsRunningNthProcesses.CheckEndIdentity(original, original.ExecutablePath, 2000));
        Assert.Null(WindowsRunningNthProcesses.CheckEndIdentity(original, original.ExecutablePath, 1000));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_claimed_nth_identity_cannot_override_the_actual_server_process()
    {
        var claimed = new NthControlResponse(NthControlReply.Status, 4242, @"C:\Apps\nth\current\nth.exe", [], 0);
        using var stream = new ReviewDuplexStream(NthControl.Frame(claimed.ToJson()));
        var actual = new RunningNthProcess(9001, @"E:\Unrelated\other.exe", 2000);

        var result = await PipeNthControlChannel.SendConnectedAsync(stream, actual, NthControlRequest.Status, CancellationToken.None, @"C:\Apps\nth", _ => true);

        Assert.Null(result.Response);
        Assert.Empty(stream.Written.ToArray()); // authenticated peer is checked before even sending a command
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_OS_peer_path_is_authoritative_even_when_the_JSON_path_claims_somewhere_else()
    {
        var actual = new RunningNthProcess(4242, @"C:\Apps\nth\current\nth.exe", 2000);
        var claimed = new NthControlResponse(NthControlReply.Status, 4242, @"E:\Unrelated\pretend.exe", [], 0);
        using var stream = new ReviewDuplexStream(NthControl.Frame(claimed.ToJson()));
        var checks = 0;
        var result = await PipeNthControlChannel.SendConnectedAsync(stream, actual, NthControlRequest.Status,
            CancellationToken.None, @"C:\Apps\nth", peer => { checks++; return peer == actual; });
        Assert.Equal(NthControlFailure.None, result.Failure);
        Assert.Equal(actual.ExecutablePath, result.Response!.Exe);
        Assert.Equal(actual.ProcessId, result.Response.Pid);
        Assert.Equal(2, checks);
        Assert.Equal(NthControl.AckByte, stream.Written.ToArray()[^1]);
    }

    [Theory]
    [InlineData(4242, 0, @"C:\Apps\nth\current\nth.exe")]
    [InlineData(4242, 2000, @"C:\Apps\nth-copy\current\nth.exe")]
    [InlineData(4242, 2000, @"C:\Apps\nth\..\foreign\nth.exe")]
    [Trait("Decision", "ED-247")]
    public async Task An_unproved_lifetime_or_path_is_not_an_authenticated_peer(int pid, long begun, string path)
    {
        var actual = new RunningNthProcess(pid, path, begun);
        using var stream = new ReviewDuplexStream([]);
        var result = await PipeNthControlChannel.SendConnectedAsync(stream, actual, NthControlRequest.Status,
            CancellationToken.None, @"C:\Apps\nth", _ => true);
        Assert.Null(result.Response);
        Assert.Empty(stream.Written.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Decision", "ED-247")]
    public async Task A_different_claimed_pid_or_recycled_peer_after_reply_is_not_acknowledged(bool recycled)
    {
        var actual = new RunningNthProcess(4242, @"C:\Apps\nth\current\nth.exe", 2000);
        var claimed = new NthControlResponse(NthControlReply.Status, recycled ? 4242 : 9001, actual.ExecutablePath, [], 0);
        using var stream = new ReviewDuplexStream(NthControl.Frame(claimed.ToJson()));
        var checks = 0;
        var result = await PipeNthControlChannel.SendConnectedAsync(stream, actual, NthControlRequest.Status,
            CancellationToken.None, @"C:\Apps\nth", _ => ++checks == 1);
        Assert.Null(result.Response);
        Assert.Equal(NthControl.Frame(NthControlRequest.Status.ToJson()), stream.Written.ToArray());
    }

}
