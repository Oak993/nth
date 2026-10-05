using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeConsentBoundaryTests
{
    [Fact]
    public async Task All_accounts_being_out_does_not_hide_the_first_time_choice_or_switch_when_one_recovers()
    {
        await using var rig = new HandshakeRig([new("c1", "claude", "One"), new("c2", "claude", "Two")]);
        var now = rig.Clock.GetUtcNow();
        rig.SetUsed("c1", 100, now.AddHours(2), true);
        rig.SetUsed("c2", 100, now.AddHours(1), true);
        await rig.Machine.LimitHitAsync("c1", now.AddHours(2), rig.Ct);
        rig.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.Empty(rig.Host.Log);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.True(request.FirstTime);
        Assert.Contains(DecisionKind.OnLimit, request.Kinds);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
    }

    [Fact]
    public async Task A_just_this_time_switch_answer_remains_the_same_decision_while_waiting_for_a_destination()
    {
        await using var rig = new HandshakeRig([new("c1", "claude", "One"), new("c2", "claude", "Two")]);
        var now = rig.Clock.GetUtcNow();
        rig.SetUsed("c1", 100, now.AddHours(2), true);
        rig.SetUsed("c2", 100, now.AddHours(1), true);
        await rig.Machine.LimitHitAsync("c1", now.AddHours(2), rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        await rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        rig.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested);
        Assert.Null(rig.DecisionStore.GetSetting(DecisionKind.OnLimit));
        Assert.Null(rig.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.OnLimit));
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("service")]
    [InlineData("earlier")]
    public async Task An_answer_that_omits_the_requested_switch_permission_cannot_commit_or_change_preferences(string permission)
    {
        await using var rig = new HandshakeRig(active: permission == "earlier" ? "c2" : "c1");
        if (permission != "limit") rig.SettleAll();
        if (permission == "service")
            foreach (var id in new[] { "c1", "c2", "c3" }) rig.SetUsed(id, 100, rig.Clock.GetUtcNow().AddHours(2), true);
        if (permission == "earlier")
        {
            rig.DecisionStore.SetSetting(DecisionKind.EarlierAccount, DecisionValues.Ask);
            await rig.Machine.TickAsync(rig.Ct);
        }
        else await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Machine.AnswerAsync([], RememberScope.AllThreads, rig.Ct));
        Assert.Equal(permission == "earlier" ? "c2" : "c1", rig.Host.ActiveAccountId);
        Assert.Empty(rig.Host.Log);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        Assert.Null(rig.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.OnLimit));
        Assert.Equal(permission == "limit" ? null : DecisionValues.Switch, rig.DecisionStore.GetSetting(DecisionKind.OnLimit));
    }

    [Fact]
    public async Task An_existing_standby_does_not_supply_an_unset_mid_reply_choice_when_a_hard_limit_interrupts_work()
    {
        await using var rig = new HandshakeRig();
        rig.DecisionStore.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        rig.DecisionStore.SetSetting(DecisionKind.Bring, DecisionValues.Full);
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        rig.Host.ReplyInFlight = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.MidReply], request.Kinds);
        rig.Host.AtSafePoint = true;
        await rig.Machine.AnswerAsync([new(DecisionKind.MidReply, DecisionValues.Stop)], RememberScope.ThisThread, rig.Ct);
        Assert.Equal("switch:c2:noresume:safe", rig.Host.Log[^1]);
    }

    [Theory]
    [InlineData(DecisionValues.Wait)]
    [InlineData(DecisionValues.Ask)]
    public async Task A_standby_prepared_before_automatic_switching_is_revoked_cannot_transfer_afterward(string choice)
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        rig.DecisionStore.SetSetting(DecisionKind.OnLimit, choice);
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.DoesNotContain(rig.Host.Log, line => line.StartsWith("switch:", StringComparison.Ordinal));
        Assert.Equal(1, rig.Host.StandbysDisposed);
    }
}
