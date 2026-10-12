using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7U4HostReviewTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Quit_if_idle_does_not_stop_work_that_started_before_the_acknowledgement()
    {
        using var rig = new NthControlRig("A newly started turn");
        var answer = rig.Ask(NthControlRequest.QuitIfIdle);
        Assert.Equal(NthControlReply.Ok, answer.Response.Reply);

        rig.SetRunning(0, TimeSpan.FromSeconds(1)); // user starts work while the answer/ACK crosses the pipe
        rig.Finish(answer);

        Assert.Empty(rig.Recorder.Calls); // no stop consent was given
    }

}
