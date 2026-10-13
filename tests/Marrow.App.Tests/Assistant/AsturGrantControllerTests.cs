using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.1, 12.2; ED-NEW-dextra-as16-5): a grant the owner makes from an Astur's card is kept only inside one Dextra already holds. A child
// grant is never broader than its parent: it is refused, logged, and the chat is told it was kept for this once only. The policy context carries the grants of the
// whole chain, never a sibling's, so the engine can hold an Astur to its parent and one Astur's sign-in is not another's.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-5")]
public sealed class AsturGrantControllerTests
{
    private const string Site = "https://voice.example";

    private static DecisionItem CardOf(AssistantProfile who, Guid? thread = null, Guid? mission = null) =>
        new(Guid.NewGuid(), DecisionKind.Approval, DecisionWeight.Reversible, "Sign in to the voice site")
        {
            AssistantId = who.Id,
            ThreadId = thread,
            MissionId = mission,
            Class = ActionClass.Login,
            GrantTarget = GrantTarget.Site(Site),
        };

    private static Grant Standing(AssistantProfile who, params string[] fields) =>
        new(Guid.NewGuid(), who.Id, ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing) { Fields = new ValueList<string>(fields) };

    [Fact]
    public async Task An_Astur_grant_Dextra_does_not_cover_is_not_kept_and_the_owner_is_told_why()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var asturHome = rig.AddHomeChat(a);

        rig.Controller.Granted(CardOf(a, asturHome.Id), DecisionAnswer.Always(Standing(a)));

        Assert.Empty(rig.Controller.Grants);
        Assert.Contains(rig.SentTo(asturHome.Id), t => t == "The owner's choice was kept for this once only, not as a grant: Dextra does not hold this itself, and Channel A never holds more than Dextra.");
        Assert.Contains(rig.Logged(), e => e.Tool == "grant" && e.Assistant == a.Id && e.Result == "not kept: Dextra does not hold this itself, and Channel A never holds more than Dextra.");
    }

    [Fact]
    public async Task Inside_a_grant_Dextra_holds_the_Astur_grant_is_kept_and_a_broader_one_is_not()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);

        rig.Controller.Granted(CardOf(rig.Dextra), DecisionAnswer.Always(Standing(rig.Dextra, "sign in")));          // Dextra's own: no parent to be inside
        Assert.Single(rig.Controller.Grants);

        rig.Controller.Granted(CardOf(a), DecisionAnswer.Always(Standing(a, "sign in")));                           // inside it
        Assert.Equal(2, rig.Controller.Grants.Count);

        rig.Controller.Granted(CardOf(a), DecisionAnswer.Always(Standing(a)));                                      // every field: broader than "sign in"
        rig.Controller.Granted(CardOf(a), DecisionAnswer.Always(Standing(a, "sign in", "upload")));                 // a field Dextra lacks
        Assert.Equal(2, rig.Controller.Grants.Count);
        Assert.Equal(2, rig.Logged().Count(e => e.Tool == "grant" && e.Result!.StartsWith("not kept", StringComparison.Ordinal)));
        Assert.Equal(2, rig.Logged().Count(e => e.Tool == "grant" && e.Result!.StartsWith("granted by the owner", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task For_this_mission_on_an_Astur_card_is_a_grant_too_and_needs_Dextras_cover()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var mission = Guid.NewGuid();

        rig.Controller.Granted(CardOf(a, mission: mission), DecisionAnswer.ForThisMission);
        Assert.Empty(rig.Controller.Grants);

        rig.Controller.Granted(CardOf(rig.Dextra), DecisionAnswer.Always(Standing(rig.Dextra)));
        rig.Controller.Granted(CardOf(a, mission: mission), DecisionAnswer.ForThisMission);
        var kept = Assert.Single(rig.Controller.Grants, g => g.AssistantId == a.Id);
        Assert.Equal(GrantScope.ForMission(mission), kept.Scope);
    }

    [Fact]
    public async Task The_context_carries_the_grants_of_the_chain_so_the_engine_allows_an_Astur_only_inside_Dextras_and_never_on_a_siblings()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var b = await rig.MakeAsync("Channel B", AsturShape.Hexagon);
        var leadOfA = new AssistantChat(Guid.NewGuid(), a.Id, null, AssistantChatRole.Home, Marrow.Core.Models.ProviderKind.Claude, "model-a");
        var signIn = new Classification(ActionClass.Login, "sign in") { GrantTarget = GrantTarget.Site(Site), Field = "sign in" };

        rig.Controller.Granted(CardOf(rig.Dextra), DecisionAnswer.Always(Standing(rig.Dextra, "sign in")));
        rig.Controller.Granted(CardOf(a), DecisionAnswer.Always(Standing(a, "sign in")));
        rig.Controller.Granted(CardOf(b), DecisionAnswer.Always(Standing(b, "sign in")));
        Assert.Equal(3, rig.Controller.Grants.Count);

        var context = rig.Controller.PolicyContext(leadOfA);
        Assert.Equal(new[] { rig.Dextra.Id, a.Id }.Order(StringComparer.Ordinal), context.Grants.Select(g => g.AssistantId).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(context.Grants, g => g.AssistantId == b.Id);                                          // a sibling's grants are not in the context
        var decision = PolicyEngine.Decide(signIn, context);
        Assert.Equal((Verdict.Allow, PolicyRule.Grant), (decision.Verdict, decision.Rule));

        // Dextra's grant revoked: the Astur's is held to it at the next action.
        var revoked = new PolicyContext(context.Chain, context.Now)
        {
            Grants = new ValueList<Grant>(context.Grants.Select(g => g.AssistantId == rig.Dextra.Id ? g.Revoked(context.Now) : g)),
        };
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(signIn, revoked).Verdict);
    }
}
