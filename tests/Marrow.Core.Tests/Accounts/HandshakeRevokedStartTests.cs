using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeRevokedStartTests
{
    [Fact]
    public async Task Turning_off_proactive_handshaking_invalidates_a_prepared_near_limit_switch_before_the_safe_point()
    {
        var enabled = true;
        await using var rig = new HandshakeRig(optionsSource: () => new(Enabled: enabled));
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 99);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        enabled = false;
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.DoesNotContain(rig.Events, value => value.To == HandshakePhase.Switch);
    }
}
