using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Rule for every choice", "ONE first-time card", "Other provider when all accounts are out": one card for every unresolved kind, one Remember line.
public sealed class HandshakeMachineDecisionTests
{
    private static IReadOnlyList<Choice> Card(string onLimit = DecisionValues.Switch, string bring = DecisionValues.Full) =>
        [new Choice(DecisionKind.OnLimit, onLimit), new Choice(DecisionKind.Bring, bring)];

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T51")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_first_limit_with_nothing_chosen_raises_one_first_time_card_for_everything_unset_and_changes_nothing()
    {
        await using var rig = new HandshakeRig();
        rig.Host.ReplyInFlight = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.OnLimit, DecisionKind.MidReply, DecisionKind.Bring], request.Kinds.Order());
        Assert.True(request.FirstTime);
        Assert.Equal(("c1", "c2"), (request.OutgoingAccountId, request.IncomingAccountId));
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T52")]
    [Trait("Rule", "R-TEST-4")]
    public async Task What_Settings_already_answers_is_never_asked_again()
    {
        await using var rig = new HandshakeRig();
        rig.DecisionStore.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        rig.DecisionStore.SetSetting(DecisionKind.MidReply, DecisionValues.Finish);
        rig.Host.ReplyInFlight = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.Bring], request.Kinds);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T53")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Ask_me_raises_the_card_that_is_not_the_first_time_one()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(onLimit: DecisionValues.Ask);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.OnLimit], request.Kinds);
        Assert.False(request.FirstTime);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T54")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Answering_Switch_for_this_thread_switches_and_the_next_limit_on_the_thread_does_not_ask()
    {
        await using var rig = new HandshakeRig();
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync(Card(bring: DecisionValues.Full), RememberScope.ThisThread, rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.Equal(Bring.Full, Assert.Single(rig.Host.Packages).Bring);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        await rig.Machine.LimitHitAsync("c2", null, rig.Ct);
        Assert.Equal("c3", rig.Host.ActiveAccountId);
        Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested);
        Assert.Null(rig.DecisionStore.GetSetting(DecisionKind.OnLimit));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T55")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Answering_for_all_threads_writes_the_Settings_values()
    {
        await using var rig = new HandshakeRig();
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync(Card(bring: DecisionValues.AiSummary), RememberScope.AllThreads, rig.Ct);
        Assert.Equal(DecisionValues.Switch, rig.DecisionStore.GetSetting(DecisionKind.OnLimit));
        Assert.Equal(DecisionValues.AiSummary, rig.DecisionStore.GetSetting(DecisionKind.Bring));
        Assert.Null(rig.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.OnLimit));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T56")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Just_this_time_acts_once_and_the_next_limit_asks_again()
    {
        await using var rig = new HandshakeRig();
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync(Card(), RememberScope.JustThisTime, rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        await rig.Machine.LimitHitAsync("c2", null, rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        Assert.Equal(2, rig.Events.Count(e => e.Kind == HandshakeEventKind.DecisionRequested));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T57")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Answering_Wait_waits_for_the_reset()
    {
        await using var rig = new HandshakeRig();
        var resets = rig.Clock.GetUtcNow().AddHours(1);
        await rig.Machine.LimitHitAsync("c1", resets, rig.Ct);
        await rig.Machine.AnswerAsync(Card(onLimit: DecisionValues.Wait), RememberScope.JustThisTime, rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        Assert.Equal(resets, rig.Events[^1].ResumesAt);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T58")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_card_answered_without_a_choice_the_switch_needs_never_stalls_on_a_second_card()
    {
        await using var rig = new HandshakeRig();
        rig.Host.ReplyInFlight = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync([new Choice(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal("switch:c2:resume:safe", rig.Host.Log[^1]);
        Assert.Equal(Bring.MarrowSummary, Assert.Single(rig.Host.Packages).Bring);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T59")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Answers_without_a_card_or_for_a_kind_not_asked_are_refused()
    {
        await using var rig = new HandshakeRig();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Machine.AnswerAsync(Card(), RememberScope.JustThisTime, rig.Ct));
        rig.DecisionStore.SetSetting(DecisionKind.OnLimit, DecisionValues.Ask);
        rig.DecisionStore.SetSetting(DecisionKind.Bring, DecisionValues.Full);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Machine.AnswerAsync([new Choice(DecisionKind.EarlierAccount, DecisionValues.Stay)], RememberScope.JustThisTime, rig.Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Machine.AnswerAsync([new Choice(DecisionKind.OnLimit, DecisionValues.Finish)], RememberScope.JustThisTime, rig.Ct));
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
    }
}
