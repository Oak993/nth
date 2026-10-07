using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeTransferConsentTests
{
    [Theory]
    [InlineData("permission")]
    [InlineData("off")]
    [InlineData("pin")]
    [InlineData("source")]
    public async Task Revocation_during_the_outgoing_briefing_prevents_any_incoming_context_transfer(string change)
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        var briefing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Host.BriefingGate = briefing.Task;
        rig.SetUsed("c1", 98);
        var preparation = rig.Machine.TickAsync(rig.Ct);
        while (!rig.Host.Log.Contains("briefing")) await Task.Yield();
        switch (change)
        {
            case "permission": rig.DecisionStore.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait); break;
            case "off": rig.Store.Update("c2", account => account with { Enabled = false }); break;
            case "pin": rig.Host.Pinned = true; break;
            case "source": rig.Host.ActiveAccountId = "c3"; break;
        }
        briefing.SetResult();
        await preparation;
        Assert.Empty(rig.Host.Packages);
        Assert.Equal(1, rig.Host.StandbysDisposed);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
    }
}
