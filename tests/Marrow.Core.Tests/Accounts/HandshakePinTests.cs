using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakePinTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_pinned_account_never_prepares_or_automatically_moves_on_a_near_or_hard_limit(bool hard)
    {
        await using var rig = new HandshakeRig(options: new(Enabled: true));
        rig.SettleAll();
        rig.Host.Pinned = true;
        rig.SetUsed("c1", hard ? 100 : 99, rig.Clock.GetUtcNow().AddHours(1));
        if (hard) await rig.Machine.LimitHitAsync("c1", rig.Clock.GetUtcNow().AddHours(1), rig.Ct);
        else await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Host.Targets);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.DoesNotContain(rig.Host.Log, item => item.StartsWith("switch:", StringComparison.Ordinal));
        Assert.Equal(hard ? HandshakePhase.Waiting : HandshakePhase.Idle, rig.Machine.Phase);
    }

    [Fact]
    public async Task A_pin_chosen_after_preparation_revokes_the_forward_switch_at_its_safe_point()
    {
        await using var rig = new HandshakeRig(options: new(Enabled: true));
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 99);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        rig.Host.Pinned = true;
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.Equal(1, rig.Host.StandbysDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Back_to_order_after_a_pinned_limit_rechecks_the_existing_choice_without_granting_switch_consent(bool chosen)
    {
        await using var rig = new HandshakeRig(options: new(Enabled: true));
        if (chosen) rig.SettleAll();
        rig.Host.Pinned = true;
        var reset = rig.Clock.GetUtcNow().AddHours(1);
        rig.SetUsed("c1", 100, reset);
        await rig.Machine.LimitHitAsync("c1", reset, rig.Ct);
        Assert.Equal(HandshakePhase.Waiting, rig.Machine.Phase);
        rig.Host.Pinned = false;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(chosen ? "c2" : "c1", rig.Host.ActiveAccountId);
        Assert.Equal(chosen ? HandshakePhase.Idle : HandshakePhase.Asking, rig.Machine.Phase);
        if (!chosen)
        {
            Assert.Empty(rig.Host.Targets);
            Assert.Null(rig.DecisionStore.GetSetting(DecisionKind.OnLimit));
        }
    }

    [Fact]
    public async Task Back_to_order_after_a_pin_canceled_preparation_does_not_quarantine_the_healthy_source()
    {
        await using var rig = new HandshakeRig(options: new(Enabled: true));
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 99);
        await rig.Machine.TickAsync(rig.Ct);
        rig.Host.Pinned = true;
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        rig.Host.Pinned = false;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }

    [Fact]
    public async Task Back_to_order_during_the_pin_cancellation_notification_cannot_recreate_a_failed_source_quarantine()
    {
        await using var rig = new HandshakeRig(options: new(Enabled: true));
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 99);
        await rig.Machine.TickAsync(rig.Ct);
        rig.Machine.Changed += (_, value) =>
        {
            if (value.Kind == HandshakeEventKind.Degraded) rig.Host.Pinned = false;
        };
        rig.Host.Pinned = true;
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.False(rig.Host.Pinned);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }
}
