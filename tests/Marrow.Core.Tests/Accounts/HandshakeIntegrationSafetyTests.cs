using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeIntegrationSafetyTests
{
    [Fact]
    public async Task Disabling_the_prepared_account_cancels_it_before_adoption()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        rig.Store.Update("c2", a => a with { Enabled = false });
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.DoesNotContain(rig.Host.Log, l => l.StartsWith("switch:c2", StringComparison.Ordinal));
        Assert.Contains(rig.Events, e => e.Kind == HandshakeEventKind.Degraded);
    }

    [Fact]
    public async Task A_limit_does_not_authorize_adoption_while_a_tool_is_still_running()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        Assert.DoesNotContain(rig.Host.Log, l => l.StartsWith("switch:", StringComparison.Ordinal));
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }
}
