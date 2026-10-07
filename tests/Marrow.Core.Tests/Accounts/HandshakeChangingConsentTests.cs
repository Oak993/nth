using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeChangingConsentTests
{
    [Fact]
    public async Task A_destination_change_after_the_card_requires_the_new_service_permission_before_any_switch()
    {
        await using var rig = new HandshakeRig();
        rig.SetUsed("c1", 100, isOut: true);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal("c2", Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).IncomingAccountId);
        rig.SetUsed("c2", 100, isOut: true);
        rig.SetUsed("c3", 100, isOut: true);
        await rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.DoesNotContain(rig.Host.Log, line => line.StartsWith("switch:", StringComparison.Ordinal));
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        var second = rig.Events.Last(e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.OtherService], second.Kinds);
        await rig.Machine.AnswerAsync([new(DecisionKind.OtherService, DecisionValues.Switch)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal("x1", rig.Host.ActiveAccountId);
        Assert.Null(rig.DecisionStore.GetSetting(DecisionKind.OtherService));
        Assert.Null(rig.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.OnLimit));
    }

    [Fact]
    public async Task Duplicate_limit_signals_share_the_once_answer_until_adoption_and_a_later_limit_asks_again()
    {
        await using var rig = new HandshakeRig();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 100, isOut: true);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime, rig.Ct);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested);
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        rig.SetUsed("c2", 100, isOut: true);
        await rig.Machine.LimitHitAsync("c2", null, rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        Assert.Equal(2, rig.Events.Count(e => e.Kind == HandshakeEventKind.DecisionRequested));
        Assert.Null(rig.DecisionStore.GetSetting(DecisionKind.OnLimit));
    }

    [Fact]
    public async Task Changing_bring_discards_the_old_private_context_and_primes_a_fresh_session_with_the_new_choice()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(bring: DecisionValues.Full);
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(Bring.Full, Assert.Single(rig.Host.Packages).Bring);
        rig.DecisionStore.SetSetting(DecisionKind.Bring, DecisionValues.MarrowSummary);
        rig.Host.AtSafePoint = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.Equal(Bring.MarrowSummary, rig.Host.Packages[^1].Bring);
        Assert.Equal(2, rig.Host.Targets.Count);
        Assert.Equal(1, rig.Host.StandbysDisposed);
    }

    [Theory]
    [InlineData(DecisionValues.Wait)]
    [InlineData(DecisionValues.Ask)]
    public async Task A_second_limit_signal_cannot_launder_revoked_other_service_permission(string changed)
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.DecisionStore.SetSetting(DecisionKind.OtherService, DecisionValues.Switch);
        rig.Host.AtSafePoint = false;
        foreach (var id in new[] { "c1", "c2", "c3" }) rig.SetUsed(id, 100, isOut: true);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        rig.DecisionStore.SetSetting(DecisionKind.OtherService, changed);
        rig.Host.AtSafePoint = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.DoesNotContain(rig.Host.Log, line => line.StartsWith("switch:", StringComparison.Ordinal));
        Assert.Equal(changed == DecisionValues.Wait ? HandshakePhase.Waiting : HandshakePhase.Asking, rig.Machine.Phase);
    }

    [Fact]
    public async Task A_wait_notices_another_service_recovering_and_asks_only_for_the_new_permission()
    {
        await using var rig = new HandshakeRig();
        var now = rig.Clock.GetUtcNow();
        foreach (var account in rig.Store.Accounts) rig.SetUsed(account.Id, 100, now.AddHours(2), true);
        await rig.Machine.LimitHitAsync("c1", now.AddHours(2), rig.Ct);
        await rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        rig.SetUsed("x1", 10);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        var request = rig.Events.Last(e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.OtherService], request.Kinds);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        await rig.Machine.AnswerAsync([new(DecisionKind.OtherService, DecisionValues.Switch)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal("x1", rig.Host.ActiveAccountId);
        Assert.Null(rig.DecisionStore.GetSetting(DecisionKind.OtherService));
    }

    [Fact]
    public async Task An_unknown_usage_report_does_not_end_a_wait_or_discard_the_once_choice_before_the_known_reset()
    {
        await using var rig = new HandshakeRig([new("c1", "claude", "One"), new("c2", "claude", "Two")]);
        var reset = rig.Clock.GetUtcNow().AddHours(2);
        rig.SetUsed("c1", 100, reset, true);
        rig.SetUsed("c2", 100, reset, true);
        await rig.Machine.LimitHitAsync("c1", reset, rig.Ct);
        await rig.Machine.AnswerAsync([new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime, rig.Ct);
        rig.Limits.SetUsage("c1", null!); // The reader's missing/failed report is explicitly unknown.
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        Assert.DoesNotContain(rig.Events, e => e.Reason == HandshakeReason.ResetReached);
        rig.SetUsed("c1", 100, reset, true);
        rig.SetUsed("c2", 10);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested);
    }
}
