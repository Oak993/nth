using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeSwitchNowTests
{
    [Fact]
    public async Task Switch_now_leaves_the_wait_and_asks_for_unresolved_transfer_choices_without_changing_preferences()
    {
        await using var rig = new HandshakeRig(options: new(Enabled: false));
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.ThisThread, rig.Ct);
        await rig.Machine.SwitchNowAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        var request = rig.Events.Last(e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.Bring], request.Kinds);
        Assert.Empty(rig.Host.Log);
        Assert.Equal(DecisionValues.Wait, rig.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.OnLimit));
        await rig.Machine.AnswerAsync([new(DecisionKind.Bring, DecisionValues.Full)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.Equal(Bring.Full, Assert.Single(rig.Host.Packages).Bring);
        Assert.Equal(DecisionValues.Wait, rig.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.OnLimit));
    }
}
