using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// OD-0116 (deferred delivery), OD-0122 (an injected history shows the same in-sync tick), ED-439.
[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeInjectedStandbyTests
{
    [Fact]
    public async Task An_injected_destination_loads_the_context_without_a_read_back_and_shows_the_in_sync_tick()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.InjectedAccounts.Add("c2");
        rig.Summaries.KeyFacts = ["file.cs"];
        rig.SetUsed("c1", 100, rig.Clock.GetUtcNow().AddHours(1), true);
        await rig.Machine.LimitHitAsync("c1", rig.Clock.GetUtcNow().AddHours(1), rig.Ct);
        Assert.Equal(["standby:c2", "receive", "switch:c2:noresume:safe"], rig.Host.Log);
        Assert.Empty(rig.Host.GapsSent);
        var ready = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.InSyncReady);
        Assert.Equal("in sync", ready.Detail);
        Assert.Contains(HandshakePhase.InSync, rig.Phases());
    }

    [Fact]
    public async Task An_injected_destination_may_continue_a_reply_when_Finish_was_chosen()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(midReply: DecisionValues.Finish);
        rig.Host.InjectedAccounts.Add("c2");
        rig.Host.ReplyInFlight = true;
        rig.SetUsed("c1", 100, rig.Clock.GetUtcNow().AddHours(1), true);
        await rig.Machine.LimitHitAsync("c1", rig.Clock.GetUtcNow().AddHours(1), rig.Ct);
        Assert.Equal(["standby:c2", "receive", "switch:c2:resume:safe"], rig.Host.Log);
    }

    [Fact]
    public async Task A_loaded_history_that_was_discarded_for_safety_is_a_deferred_delivery_and_never_shows_the_tick()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(midReply: DecisionValues.Finish);
        rig.Host.InjectedAccounts.Add("c2");
        rig.Host.InjectedDiscardsOnReceive = true;
        rig.Host.ReplyInFlight = true;
        rig.SetUsed("c1", 100, rig.Clock.GetUtcNow().AddHours(1), true);
        await rig.Machine.LimitHitAsync("c1", rig.Clock.GetUtcNow().AddHours(1), rig.Ct);
        Assert.Equal(["standby:c2", "receive", "switch:c2:noresume:safe"], rig.Host.Log);
        Assert.DoesNotContain(rig.Events, e => e.Kind == HandshakeEventKind.InSyncReady);
    }

    [Fact]
    public async Task The_mid_reply_card_is_not_asked_when_the_destination_is_deferred_and_would_ignore_the_answer()
    {
        await using var rig = new HandshakeRig();
        rig.DecisionStore.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        rig.Host.DeferredAccounts.Add("c2");
        rig.Host.ReplyInFlight = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.Bring], request.Kinds);
    }

    [Fact]
    public async Task The_mid_reply_card_is_not_asked_either_when_the_deferral_only_shows_after_the_run_time_check()
    {
        await using var rig = new HandshakeRig();
        rig.DecisionStore.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        rig.Host.LateDeferredAccounts.Add("c2");
        rig.Host.ReplyInFlight = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.Bring], request.Kinds);
    }

    [Fact]
    public async Task The_mid_reply_card_is_still_asked_of_a_destination_that_can_continue_the_reply()
    {
        await using var rig = new HandshakeRig();
        rig.DecisionStore.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        rig.Host.InjectedAccounts.Add("c2");
        rig.Host.ReplyInFlight = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.MidReply, DecisionKind.Bring], request.Kinds.Order());
    }
}
