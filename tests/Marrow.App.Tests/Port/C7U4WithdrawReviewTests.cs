using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7U4WithdrawReviewTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Withdraw_before_the_acknowledgement_calls_off_the_deferred_installer_quit()
    {
        using var rig = new NthControlRig("A merge in progress");
        rig.SetMerging(0, true);
        var answer = rig.Ask(NthControlRequest.QuitAndStop);
        Assert.Equal(NthWorkKind.Merge, answer.Response.Deferred);

        rig.Ask(NthControlRequest.Withdraw);
        rig.Finish(answer);
        rig.SetMerging(0, false);
        rig.H.Main.AdvanceMilliseconds(250);

        Assert.Empty(rig.Recorder.Calls);
    }
}
