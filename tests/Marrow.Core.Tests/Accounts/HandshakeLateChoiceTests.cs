using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeLateChoiceTests
{
    [Fact]
    public async Task A_user_chosen_source_wait_reaches_its_reported_deadline_even_if_the_next_quota_read_is_unknown()
    {
        await using var rig = new HandshakeRig();
        var reset = rig.Clock.GetUtcNow().AddHours(2);
        rig.SetUsed("c1", 100, reset, true);
        rig.DecisionStore.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
        await rig.Machine.LimitHitAsync("c1", reset, rig.Ct);
        rig.Limits.SetUsage("c1", null!);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        rig.Clock.Advance(TimeSpan.FromHours(2));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.Contains(rig.Events, e => e.Reason == HandshakeReason.ResetReached && e.To == HandshakePhase.Idle);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    public async Task A_bring_choice_cleared_after_an_earlier_account_card_is_asked_before_the_return()
    {
        await using var rig = new HandshakeRig(active: "c2");
        rig.DecisionStore.SetSetting(DecisionKind.EarlierAccount, DecisionValues.Ask);
        rig.DecisionStore.SetSetting(DecisionKind.Bring, DecisionValues.Full);
        await rig.Machine.TickAsync(rig.Ct);
        var first = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.EarlierAccount], first.Kinds);
        rig.DecisionStore.SetSetting(DecisionKind.Bring, null);
        await rig.Machine.AnswerAsync([new(DecisionKind.EarlierAccount, DecisionValues.GoBack)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        Assert.Equal([DecisionKind.Bring], rig.Events.Last(e => e.Kind == HandshakeEventKind.DecisionRequested).Request!.Kinds);
        await rig.Machine.AnswerAsync([new(DecisionKind.Bring, DecisionValues.Full)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.Equal(Bring.Full, Assert.Single(rig.Host.Packages).Bring);
        Assert.Null(rig.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.EarlierAccount));
    }
}
