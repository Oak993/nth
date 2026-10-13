using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.1 "default models", 15.6, 9.3 "Escalation"): an Astur's missions try the provider, model and account its profile prefers, where the
// router and the reserve leave room, and fall back to the router's usual pick; what an Astur's agent asks the owner goes to the one queue with its own name on the
// card.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-13")]
public sealed class AsturModelTests
{
    private const string Hand = """{"astur":"Channel A","brief":"Get last week's numbers."}""";

    [Fact]
    public async Task An_Astur_that_prefers_a_model_and_an_account_has_its_missions_started_there()
    {
        using var rig = await AsturRig.OpenAsync(routing: true);
        await rig.MakeAsync("Channel A", AsturShape.Squircle, d => d with { Model = new AsturModel("codex", "astra", AsturRig.CodexAccount) });
        var result = await rig.Call(rig.Runner(rig.DextraHome()), "delegate_to", Hand);
        Assert.False(result.IsError, result.Text);

        var mission = Assert.Single(rig.Missions);
        Assert.Equal((ProviderKind.Codex, "astra", AsturRig.CodexAccount), (mission.Provider, mission.Model, mission.AccountId));
        Assert.False(mission.ModelPinned);                                                         // a default, not a pin: the reserve may still move it
    }

    [Fact]
    public async Task A_preference_nothing_can_meet_falls_back_to_the_routers_usual_pick()
    {
        using var rig = await AsturRig.OpenAsync(routing: true);
        await rig.MakeAsync("Channel A", AsturShape.Squircle, d => d with { Model = new AsturModel("codex", "no-such-model") });
        await rig.Call(rig.Runner(rig.DextraHome()), "delegate_to", Hand);
        var mission = Assert.Single(rig.Missions);
        Assert.Equal(ProviderKind.Claude, mission.Provider);                                       // the router's first account
        Assert.NotEqual("no-such-model", mission.Model);
    }

    [Fact]
    public async Task An_Astur_with_no_preference_and_a_build_with_no_router_start_on_the_controllers_default()
    {
        using var routed = await AsturRig.OpenAsync(routing: true);
        await routed.MakeAsync("Channel A", AsturShape.Squircle);
        await routed.Call(routed.Runner(routed.DextraHome()), "delegate_to", Hand);
        Assert.Equal(ProviderKind.Claude, Assert.Single(routed.Missions).Provider);

        using var plain = await AsturRig.OpenAsync();
        await plain.MakeAsync("Channel A", AsturShape.Squircle, d => d with { Model = new AsturModel("codex", "astra") });
        await plain.Call(plain.Runner(plain.DextraHome()), "delegate_to", Hand);
        var mission = Assert.Single(plain.Missions);
        Assert.Equal((ProviderKind.Claude, "model-a"), (mission.Provider, mission.Model));         // without a router nothing picks: the default, as before
    }

    [Fact]
    public async Task What_an_Asturs_agent_asks_goes_to_the_one_queue_with_the_Asturs_name_on_the_card()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        await rig.Call(rig.Runner(rig.DextraHome()), "delegate_to", Hand);
        var started = rig.Started(Assert.Single(rig.Missions));
        var lead = started.LeadThread!.Value;

        Assert.True(rig.Controller.OnApproval(lead, MissionRig.Approval("p1", "git push origin main")));

        var card = Assert.Single(rig.Controller.Queue.Of(started.Id));
        Assert.Equal(a.Id, card.AssistantId);                                                      // the card says which assistant asked
        Assert.Equal(started.Id, card.MissionId);
        Assert.Equal(DecisionWeight.Irreversible, card.Weight);                                    // and an Astur's send is as irreversible as Dextra's
        Assert.Single(rig.Controller.Queue.Waiting);
    }

    [Fact]
    public async Task An_Asturs_rules_are_never_looser_than_Dextras_when_it_decides_an_approval()
    {
        using var rig = await AsturRig.OpenAsync();
        await rig.MakeAsync("Channel A", AsturShape.Squircle, d => d with { ClassRules = new ValueMap<ActionClass, ClassDefault>([KeyValuePair.Create(ActionClass.Outward, ClassDefault.Never)]) });
        await rig.Call(rig.Runner(rig.DextraHome()), "delegate_to", Hand);
        var started = rig.Started(Assert.Single(rig.Missions));
        var lead = started.LeadThread!.Value;

        rig.Controller.OnApproval(lead, MissionRig.Approval("p1", "git push origin main"));

        // The Astur's own Never wins over the default "always confirmed": the push is refused outright and never reaches the owner.
        Assert.Contains((lead, "p1", false), rig.Agents.Resolutions);
        Assert.Empty(rig.Controller.Queue.Of(started.Id));
    }
}
