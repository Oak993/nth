using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "When an earlier account resets": Ask me / Go back to it / Stay until this one runs out; go back is a Handshake up to the highest-ranked available account at the next safe point; a pill can carry its own value.
public sealed class HandshakeMachineEarlierAccountTests
{
    private static HandshakeRig OnSecondAccount()
    {
        var rig = new HandshakeRig(active: "c2");
        rig.SettleAll();
        return rig;
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T64")]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_nothing_chosen_an_earlier_account_coming_back_asks_once()
    {
        await using var rig = OnSecondAccount();
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.EarlierAccount], request.Kinds);
        Assert.Equal(("c2", "c1", true), (request.OutgoingAccountId, request.IncomingAccountId, request.FirstTime));
        Assert.All(rig.Events, e => Assert.Equal(("c2", HandshakeReason.EarlierAccountBack), (e.OutgoingAccountId, e.Reason)));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T65")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Go_back_runs_a_Handshake_up_and_switches_at_the_next_safe_point()
    {
        await using var rig = OnSecondAccount();
        rig.DecisionStore.SetSetting(DecisionKind.EarlierAccount, DecisionValues.GoBack);
        rig.Host.AtSafePoint = false;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        Assert.DoesNotContain(rig.Host.Log, l => l.StartsWith("switch", StringComparison.Ordinal));
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(["standby:c1", "briefing", "receive:readback", "switch:c1:noresume:safe"], rig.Host.Log);
        Assert.All(rig.Events.Where(e => e.Kind == HandshakeEventKind.PhaseChanged), e => Assert.Equal(HandshakeReason.EarlierAccountBack, e.Reason));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T66")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Stay_does_nothing_and_does_not_ask_again_until_the_thread_moves()
    {
        await using var rig = OnSecondAccount();
        rig.DecisionStore.SetSetting(DecisionKind.EarlierAccount, DecisionValues.Stay);
        await rig.Machine.TickAsync(rig.Ct);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Events);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T67")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_card_answer_of_go_back_goes_back_and_stay_stays()
    {
        await using var back = OnSecondAccount();
        await back.Machine.TickAsync(back.Ct);
        await back.Machine.AnswerAsync([new Choice(DecisionKind.EarlierAccount, DecisionValues.GoBack)], RememberScope.ThisThread, back.Ct);
        Assert.Equal("c1", back.Host.ActiveAccountId);
        Assert.Equal(DecisionValues.GoBack, back.DecisionStore.GetThread(HandshakeRig.Thread, DecisionKind.EarlierAccount));

        await using var stay = OnSecondAccount();
        await stay.Machine.TickAsync(stay.Ct);
        await stay.Machine.AnswerAsync([new Choice(DecisionKind.EarlierAccount, DecisionValues.Stay)], RememberScope.JustThisTime, stay.Ct);
        Assert.Equal("c2", stay.Host.ActiveAccountId);
        Assert.Equal(HandshakePhase.Idle, stay.Machine.Phase);
        await stay.Machine.TickAsync(stay.Ct);
        Assert.Equal(HandshakePhase.Idle, stay.Machine.Phase);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T68")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_pills_own_value_beats_Settings_and_the_thread_beats_the_pill()
    {
        await using var rig = OnSecondAccount();
        rig.DecisionStore.SetSetting(DecisionKind.EarlierAccount, DecisionValues.Stay);
        rig.Host.Pill = new Pill("p", [new PillStep("c1"), new PillStep("c2")], EarlierAccount: DecisionValues.GoBack);
        rig.Host.StepIndex = 1;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);

        await using var thread = OnSecondAccount();
        thread.Host.Pill = rig.Host.Pill;
        thread.Host.StepIndex = 1;
        thread.DecisionStore.SetThread(HandshakeRig.Thread, DecisionKind.EarlierAccount, DecisionValues.Stay);
        await thread.Machine.TickAsync(thread.Ct);
        Assert.Equal("c2", thread.Host.ActiveAccountId);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T69")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_thread_pinned_by_hand_is_never_offered_the_earlier_account()
    {
        await using var rig = OnSecondAccount();
        rig.Host.Pinned = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Events);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T70")]
    [Trait("Rule", "R-TEST-4")]
    public async Task An_earlier_account_that_is_still_out_or_off_is_not_offered()
    {
        await using var rig = OnSecondAccount();
        rig.SetUsed("c1", 100, rig.Clock.GetUtcNow().AddHours(1), isOut: true);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Events);
        rig.SetUsed("c1", 5);
        rig.Store.Update("c1", a => a with { Enabled = false });
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Events);
    }
}
