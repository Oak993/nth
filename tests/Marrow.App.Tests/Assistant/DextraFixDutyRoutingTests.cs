using Marrow.App.Assistant;
using Marrow.Services.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-4")]
public sealed class DextraFixDutyRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duties_use_the_router_and_its_account_or_the_configured_brain(bool route)
    {
        var account = Guid.Parse("11111111-0000-4000-8000-000000000001");
        var brainAccount = Guid.Parse("22222222-0000-4000-8000-000000000002");
        var router = route ? new RouterSetup
        {
            Accounts = () => [new RouterAccount(account, new ReserveAccountRef("codex:test", "Fake Codex", "codex"), ProviderKind.Codex, true, 0)],
            Models = _ => [new RouteModel("routed-model", ModelTier.Strong)],
        } : null;
        using var rig = new DutyHostRig(provider: ProviderKind.Codex, model: "brain-model", router: router, accountId: brainAccount);
        var fire = new DutyFire(new Duty("d1", "Weekly", "dextra", new ManualTrigger(), "Write a note."), DutyCause.Manual, false, null, false);
        var result = await rig.Host.Launcher.Launch(fire);
        Assert.NotNull(result.MissionId);
        var mission = rig.Controller.Runner.Find(result.MissionId.Value)!;
        Assert.Equal(ProviderKind.Codex, mission.Provider);
        Assert.Equal(route ? "routed-model" : "brain-model", mission.Model);
        Assert.Equal(route ? account : brainAccount, mission.AccountId);
    }
}
