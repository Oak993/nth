using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Pills loop": if no step is available it waits for the earliest reset, shows when, then resumes there by itself.
public sealed class HandshakeMachineWaitTests
{
    private static readonly Pill Loop = new("p", [new PillStep("c1"), new PillStep("c2"), new PillStep("x1")]);

    private static HandshakeRig AllOutInPill()
    {
        var rig = new HandshakeRig(active: "x1");
        rig.SettleAll();
        rig.Host.Pill = Loop;
        rig.Host.StepIndex = 2;
        var now = rig.Clock.GetUtcNow();
        rig.SetUsed("c1", 100, now.AddHours(3), isOut: true);
        rig.SetUsed("c2", 100, now.AddHours(1), isOut: true);
        rig.SetUsed("x1", 100, now.AddHours(2), isOut: true);
        return rig;
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T71")]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_every_step_out_the_pill_waits_and_shows_the_earliest_reset()
    {
        await using var rig = AllOutInPill();
        await rig.Machine.LimitHitAsync("x1", rig.Clock.GetUtcNow().AddHours(2), rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        Assert.Equal(rig.Clock.GetUtcNow().AddHours(1), rig.Events[^1].ResumesAt);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T72")]
    [Trait("Rule", "R-TEST-4")]
    public async Task When_that_step_resets_the_pill_resumes_there_by_itself()
    {
        await using var rig = AllOutInPill();
        await rig.Machine.LimitHitAsync("x1", rig.Clock.GetUtcNow().AddHours(2), rig.Ct);
        rig.Clock.Advance(TimeSpan.FromMinutes(59));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        Assert.Empty(rig.Host.Log);
        rig.Clock.Advance(TimeSpan.FromMinutes(2));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(["standby:c2", "receive:readback", "switch:c2:noresume:safe"], rig.Host.Log);
        Assert.Equal(1, rig.Host.StepIndex);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.All(rig.Events.Where(e => e.To is HandshakePhase.Standby or HandshakePhase.Linking), e => Assert.Equal(HandshakeReason.ResetReached, e.Reason));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T73")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_user_chosen_wait_waits_for_that_account_only_and_ends_without_a_switch()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(onLimit: DecisionValues.Wait);
        var resets = rig.Clock.GetUtcNow().AddHours(2);
        rig.SetUsed("c1", 100, resets, isOut: true);
        await rig.Machine.LimitHitAsync("c1", resets, rig.Ct);
        rig.Clock.Advance(TimeSpan.FromHours(1));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        rig.Clock.Advance(TimeSpan.FromHours(1));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.Equal(HandshakeReason.ResetReached, rig.Events[^1].Reason);
        Assert.Empty(rig.Host.Log);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T74")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_pill_wait_ends_without_a_switch_when_the_current_account_is_the_first_back()
    {
        await using var rig = AllOutInPill();
        rig.SetUsed("c2", 100, rig.Clock.GetUtcNow().AddHours(5), isOut: true);
        await rig.Machine.LimitHitAsync("x1", rig.Clock.GetUtcNow().AddHours(2), rig.Ct);
        rig.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T75")]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_no_enabled_account_the_machine_stays_idle_and_says_so()
    {
        await using var rig = new HandshakeRig([new AccountInfo("c1", "claude", "Claude")]);
        rig.SettleAll();
        rig.Store.Update("c1", a => a with { Enabled = false });
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.Contains(rig.Events, e => e.Kind == HandshakeEventKind.Degraded);
    }
}
