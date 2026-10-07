using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeEarlierAccountThrashTests
{
    [Fact]
    public async Task An_account_left_because_it_was_nearly_out_is_not_an_earlier_account_that_is_back()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.DecisionStore.SetSetting(DecisionKind.EarlierAccount, DecisionValues.GoBack);
        rig.SetUsed("c1", 99, rig.Clock.GetUtcNow().AddHours(1));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        for (var beat = 0; beat < 4; beat++) await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.Single(rig.Host.Log, entry => entry.StartsWith("switch", StringComparison.Ordinal));
    }
}
