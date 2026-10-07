using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Other provider when all accounts are out": always asked, with the Remember choices; "inside a pill no permission is asked, the user built it".
public sealed class HandshakeMachineOtherServiceTests
{
    private static void ExhaustClaude(HandshakeRig rig)
    {
        var resets = rig.Clock.GetUtcNow().AddHours(2);
        foreach (var id in new[] { "c1", "c2", "c3" }) rig.SetUsed(id, 100, resets, isOut: true);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T60")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Outside_a_pill_another_service_is_asked_even_when_switching_is_the_setting()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        ExhaustClaude(rig);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(HandshakePhase.Asking, rig.Machine.Phase);
        var request = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal([DecisionKind.OtherService], request.Kinds);
        Assert.Equal("x1", request.IncomingAccountId);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T61")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Answering_Switch_moves_to_the_other_service_and_a_remembered_choice_is_not_asked_again()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        ExhaustClaude(rig);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync([new Choice(DecisionKind.OtherService, DecisionValues.Switch)], RememberScope.AllThreads, rig.Ct);
        Assert.Equal("x1", rig.Host.ActiveAccountId);
        Assert.Equal(DecisionValues.Switch, rig.DecisionStore.GetSetting(DecisionKind.OtherService));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T62")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Answering_Wait_waits_for_the_earliest_reset_of_the_service()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        ExhaustClaude(rig);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await rig.Machine.AnswerAsync([new Choice(DecisionKind.OtherService, DecisionValues.Wait)], RememberScope.JustThisTime, rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        Assert.Equal(rig.Clock.GetUtcNow().AddHours(2), rig.Events[^1].ResumesAt);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T63")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Inside_a_pill_a_hop_to_another_service_is_never_asked()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.Pill = new Pill("p", [new PillStep("c1"), new PillStep("x1")]);
        rig.Host.StepIndex = 0;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.DoesNotContain(rig.Events, e => e.Kind == HandshakeEventKind.DecisionRequested);
        Assert.Equal("x1", rig.Host.ActiveAccountId);
    }
}
