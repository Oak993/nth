using Marrow.Core.Support;
using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Support;

// ED-NEW-inst-c7-3: fake processes and clock only; every discovered process must consent or leave.
public sealed class C7U4ReviewTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_portable_pipe_answer_does_not_clear_the_busy_nth_still_in_the_install_root()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;
        rig.Nth.AnswerPid = 9001;
        rig.Nth.AnswerExe = @"E:\Portable\nth.exe";

        var result = await rig.AskAsync();

        Assert.True(rig.Nth.Running);
        Assert.Equal(["status"], rig.Nth.Sent);
        Assert.Empty(rig.Nth.Ended);
        Assert.False(result.CanProceed); // existing inside-root nth has neither agreed nor exited
        Assert.Equal(RunningNthState.NotResponding, result.State);
    }

    [Theory]
    [InlineData(9001, true)]
    [InlineData(4242, false)]
    [Trait("Decision", "ED-247")]
    public async Task Neither_another_in_root_peer_nor_an_outside_claim_using_the_discovered_pid_can_clear_it(int pid, bool inside)
    {
        var rig = new HandshakeRig();
        rig.Nth.AnswerPid = pid;
        rig.Nth.AnswerExe = inside ? Path.Combine(HandshakeRig.Root, "current", "nth.exe") : @"E:\Portable\nth.exe";
        var result = await rig.AskAsync();
        Assert.False(result.CanProceed);
        Assert.True(rig.Nth.Running);
        Assert.Equal(["status"], rig.Nth.Sent);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task An_outside_answer_leaves_the_guard_until_the_discovered_process_exits_itself()
    {
        var rig = new HandshakeRig();
        rig.Nth.AnswerPid = 9001;
        rig.Nth.AnswerExe = @"E:\Portable\nth.exe";
        rig.Nth.CloseAfter(TimeSpan.FromSeconds(1));
        var result = await rig.AskAsync();
        Assert.Equal(RunningNthState.Closed, result.State);
        Assert.True(result.CanProceed);
        Assert.False(rig.Nth.Running);
        Assert.Equal(["status"], rig.Nth.Sent);
        Assert.Empty(rig.Nth.Ended);
    }
}
