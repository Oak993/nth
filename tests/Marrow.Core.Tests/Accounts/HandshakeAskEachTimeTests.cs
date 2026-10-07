using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// OD-0116/0119, checklist section 7: Ask me each time is a first-time choice, never consent to switch.
[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeAskEachTimeTests
{
    [Theory]
    [InlineData(RememberScope.JustThisTime)]
    [InlineData(RememberScope.ThisThread)]
    [InlineData(RememberScope.AllThreads)]
    public async Task Ask_each_time_records_only_the_selected_scope_and_opens_the_normal_limit_card(RememberScope scope)
    {
        await using var rig = new HandshakeRig(options: new(Enabled: false));
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Ask)], scope, rig.Ct);
        var request = rig.Events.Last(e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.False(request.FirstTime);
        Assert.Contains(DecisionKind.OnLimit, request.Kinds);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.Empty(rig.Host.Log);
        Assert.Equal(scope == RememberScope.ThisThread ? DecisionValues.Ask : null, rig.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.OnLimit));
        Assert.Equal(scope == RememberScope.AllThreads ? DecisionValues.Ask : null, rig.DecisionStore.GetSetting(DecisionKind.OnLimit));
        await rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    public async Task Ask_is_still_refused_as_an_answer_to_a_normal_switch_or_wait_card()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(onLimit: DecisionValues.Ask);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Ask)], RememberScope.AllThreads, rig.Ct));
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        Assert.Empty(rig.Host.Log);
    }
}
