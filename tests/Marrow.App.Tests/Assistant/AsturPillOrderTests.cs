using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-4")]
public sealed class AsturPillOrderTests
{
    [Fact]
    public void Own_order_changes_equal_options_on_the_next_decision_but_never_makes_an_out_account_eligible()
    {
        using var temp = new AssistantTempRoot();
        using var rig = new RouterRig(temp.Folder);
        var order = new ValueList<string>([RouterRig.CodexId.ToString("D"), RouterRig.ClaudeId.ToString("D")]);
        var router = new AssistantRouter(new RouterSetup { Accounts = () => rig.Accounts, Models = _ => [], Rank = (_, a) => AsturPillRank.Of(order, a) }, rig.Clock, rig.Missions.Runner, rig.Missions.Agents);
        Assert.Equal(RouterRig.CodexId, router.Pick(rig.Request(StepWork.Read))!.AccountId);
        order = new ValueList<string>([RouterRig.ClaudeId.ToString("D"), RouterRig.CodexId.ToString("D")]);
        Assert.Equal(RouterRig.ClaudeId, router.Pick(rig.Request(StepWork.Read))!.AccountId);
        rig.Set(RouterRig.ClaudeId, a => a with { Usable = false });
        Assert.Equal(RouterRig.CodexId, router.Pick(rig.Request(StepWork.Read))!.AccountId);
    }
}
