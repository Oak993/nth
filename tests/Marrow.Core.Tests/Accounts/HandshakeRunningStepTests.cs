using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeRunningStepTests
{
    [Fact]
    public async Task A_prepared_Handshake_continues_the_running_reply_only_with_the_chosen_finish_policy()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.ReplyInFlight = true;
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("switch:c2:resume:safe", rig.Host.Log[^1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DecisionValues.Stop)]
    public async Task Proactive_priming_does_not_cut_a_reply_when_finishing_it_on_the_next_account_was_not_chosen(string? midReply)
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.DecisionStore.SetSetting(DecisionKind.MidReply, midReply);
        rig.Host.ReplyInFlight = true;
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        Assert.DoesNotContain(rig.Host.Log, line => line.StartsWith("switch:", StringComparison.Ordinal));
        rig.Host.ReplyInFlight = false;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("switch:c2:noresume:safe", rig.Host.Log[^1]);
    }
    [Fact]
    public async Task Cancellation_during_a_safe_switch_releases_the_private_pipeline_and_resets_the_phase()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.ReplyInFlight = true;
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        using var cancellation = new CancellationTokenSource();
        rig.Host.AtSafePoint = true;
        rig.Host.SwitchGate = new TaskCompletionSource().Task;
        var switching = rig.Machine.TickAsync(cancellation.Token);
        Assert.Equal(HandshakePhase.Switch, rig.Machine.Phase);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => switching);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.Equal(1, rig.Host.StandbysDisposed);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.Single(rig.Host.Targets);
    }
}
