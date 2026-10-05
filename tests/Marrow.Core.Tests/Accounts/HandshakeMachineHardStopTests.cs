using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Handshake" (the CLI's own limit message stays the hard-stop fallback), "Q3 answered" (mid-reply: finish the reply on the next account / stop).
public sealed class HandshakeMachineHardStopTests
{
    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T44")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_limit_with_no_warning_switches_at_once_without_a_briefing_from_the_exhausted_account()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.SetUsed("c1", 100, rig.Clock.GetUtcNow().AddHours(3), isOut: true);
        await rig.Machine.LimitHitAsync("c1", rig.Clock.GetUtcNow().AddHours(3), rig.Ct);
        Assert.Equal(["standby:c2", "receive:readback", "switch:c2:noresume:safe"], rig.Host.Log);
        Assert.Null(Assert.Single(rig.Host.Packages).Briefing);
        Assert.All(rig.Events, e => Assert.Equal(HandshakeReason.HardLimit, e.Reason));
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
    }

    [Theory]
    [InlineData(DecisionValues.Finish, true, "switch:c2:resume:safe")]
    [InlineData(DecisionValues.Stop, true, "switch:c2:noresume:safe")]
    [InlineData(DecisionValues.Finish, false, "switch:c2:noresume:safe")]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T45")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_limit_mid_reply_finishes_on_the_next_account_or_stops_as_chosen(string midReply, bool inFlight, string expected)
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(midReply: midReply);
        rig.Host.ReplyInFlight = inFlight;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(expected, rig.Host.Log[^1]);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T46")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_limit_while_the_briefing_is_being_written_cancels_it_and_carries_on_with_the_standby_already_started()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        var gate = new TaskCompletionSource();
        rig.Host.BriefingGate = gate.Task;
        rig.SetUsed("c1", 98);
        var tick = rig.Machine.TickAsync(rig.Ct);
        while (!rig.Host.Log.Contains("briefing")) await Task.Yield();
        Assert.Equal(HandshakePhase.Linking, rig.Machine.Phase);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        await tick;
        Assert.Equal(["standby:c2", "briefing", "receive:readback", "switch:c2:noresume:safe"], rig.Host.Log);
        Assert.Null(Assert.Single(rig.Host.Packages).Briefing);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T47")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_limit_while_in_sync_and_waiting_for_a_safe_point_switches_at_once_on_the_same_standby()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        rig.Host.AtSafePoint = true;
        rig.Host.ReplyInFlight = true;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Single(rig.Host.Log, l => l.StartsWith("standby", StringComparison.Ordinal));
        Assert.Equal("switch:c2:resume:safe", rig.Host.Log[^1]);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T48")]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_the_Handshake_off_a_hard_stop_still_moves_and_hands_over_the_summary_without_a_read_back()
    {
        await using var rig = new HandshakeRig(options: new HandshakeOptions(Enabled: false));
        rig.SettleAll();
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(["standby:c2", "receive", "switch:c2:noresume:safe"], rig.Host.Log);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T49")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_standby_that_cannot_start_after_a_hard_stop_leaves_the_thread_idle_with_the_reason()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.StandbyFails = new InvalidOperationException("cli missing");
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.Contains(rig.Events, e => e.Kind == HandshakeEventKind.Degraded);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T50")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_limit_with_Wait_chosen_waits_for_that_accounts_reset_and_touches_no_session()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(onLimit: DecisionValues.Wait);
        var resets = rig.Clock.GetUtcNow().AddHours(2);
        await rig.Machine.LimitHitAsync("c1", resets, rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        Assert.Equal(resets, rig.Events[^1].ResumesAt);
        Assert.Empty(rig.Host.Log);
    }
}
