using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeDisabledSafePointTests
{
    [Fact]
    public async Task A_switch_without_proactive_handshaking_still_waits_for_and_uses_the_next_safe_point()
    {
        await using var rig = new HandshakeRig(options: new(Enabled: false));
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 100, rig.Clock.GetUtcNow().AddHours(1), true);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }
}
