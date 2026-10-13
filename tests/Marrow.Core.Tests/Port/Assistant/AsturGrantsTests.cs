using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.AsturTeamSample;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.1 "an Astur can never hold a grant its parent lacks", 12.2; ED-NEW-dextra-as16-5): a grant for an Astur is kept only
// while Dextra holds a live grant that already covers it. A child grant is never broader than its parent's: the same class, a target the parent's covers, no
// field the parent lacks, every exclusion kept, a scope that ends no later. The policy engine reads the same rule at every action.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-5")]
public sealed class AsturGrantsTests
{
    private static readonly IReadOnlyList<AssistantProfile> TwoChannels = Channels();
    private const string Site = "https://voice.example";

    private static Grant Make(string assistant, ActionClass actionClass, GrantTarget target, GrantScope scope, params string[] fields) =>
        new(Guid.NewGuid(), assistant, actionClass, target, scope) { Created = Now, Fields = new ValueList<string>(fields) };

    private static string? Refusal(Grant grant, params Grant[] held) => AsturGrants.Refusal(grant, TwoChannels, held, Now);

    [Fact]
    public void A_grant_for_Dextra_has_no_parent_to_be_checked_against()
    {
        Assert.Null(Refusal(Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing)));
    }

    [Fact]
    public void A_grant_for_an_Astur_needs_a_live_grant_of_Dextra_that_covers_it()
    {
        var child = Make("channel-b", ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing, "sign in");
        Assert.Equal("Dextra does not hold this itself, and Channel B never holds more than Dextra.", Refusal(child));
        var parent = Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing, "sign in");
        Assert.Null(Refusal(child, parent));
        // A grant of the sibling does not stand in for Dextra's.
        Assert.NotNull(Refusal(child, Make("channel-a", ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing, "sign in")));
    }

    [Fact]
    public void A_child_grant_is_never_broader_than_its_parents_in_class_target_fields_exclusions_or_time()
    {
        var parent = Make(DextraId, ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), GrantScope.UntilDate(Now.AddDays(7)), "draft") with { Excluded = new ValueList<string>(["send"]) };
        Grant Child(ActionClass actionClass = ActionClass.ConnectorWrite, string connector = "gmail", GrantScope? scope = null, string[]? fields = null, string[]? excluded = null) =>
            Make("channel-a", actionClass, GrantTarget.Connector(connector), scope ?? GrantScope.UntilDate(Now.AddDays(1)), fields ?? ["draft"]) with { Excluded = new ValueList<string>(excluded ?? ["send"]) };

        Assert.Null(Refusal(Child(), parent));
        Assert.NotNull(Refusal(Child(actionClass: ActionClass.Login), parent));                          // another class
        Assert.NotNull(Refusal(Child(connector: "calendar"), parent));                                    // another target
        Assert.NotNull(Refusal(Child(fields: ["draft", "label"]), parent));                               // a field the parent lacks
        Assert.NotNull(Refusal(Child(fields: []), parent));                                               // "every field" is broader than "draft"
        Assert.NotNull(Refusal(Child(scope: GrantScope.Standing), parent));                               // no end, where the parent's ends
        Assert.NotNull(Refusal(Child(scope: GrantScope.UntilDate(Now.AddDays(30))), parent));             // a later end
        Assert.Null(Refusal(Child(scope: GrantScope.Once), parent));                                      // once is inside any scope

        // A parent that allows every field but "send": the child may not drop that exclusion, unless it lists fields and so never reaches "send".
        var allButSend = Make(DextraId, ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), GrantScope.Standing) with { Excluded = new ValueList<string>(["send"]) };
        Assert.Null(Refusal(Child(fields: [], excluded: ["send"], scope: GrantScope.Standing), allButSend));
        Assert.NotNull(Refusal(Child(fields: [], excluded: [], scope: GrantScope.Standing), allButSend));
        Assert.Null(Refusal(Child(fields: ["draft"], excluded: [], scope: GrantScope.Standing), allButSend));
    }

    [Fact]
    public void A_parent_grant_that_is_revoked_spent_or_ended_covers_nothing()
    {
        var child = Make("channel-a", ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing);
        var parent = Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing);
        Assert.Null(Refusal(child, parent));
        Assert.NotNull(Refusal(child, parent.Revoked(Now)));
        var ended = Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.UntilDate(Now.AddMinutes(-1)));
        Assert.NotNull(Refusal(child with { Scope = GrantScope.Once }, ended));
        var spent = Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.Once).UsedByEntry("audit-1");
        Assert.NotNull(Refusal(child with { Scope = GrantScope.Once }, spent));
    }

    [Fact]
    public void A_mission_grant_needs_a_parent_grant_live_in_that_mission()
    {
        var mission = Guid.NewGuid();
        var child = Make("channel-a", ActionClass.Login, GrantTarget.Site(Site), GrantScope.ForMission(mission));
        Assert.Null(Refusal(child, Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing)));
        Assert.Null(Refusal(child, Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.ForMission(mission))));
        Assert.NotNull(Refusal(child, Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.ForMission(Guid.NewGuid()))));
    }

    [Fact]
    public void A_grant_that_can_never_exist_or_belongs_to_nobody_is_refused()
    {
        Assert.NotNull(Refusal(Make(DextraId, ActionClass.Outward, GrantTarget.Recipients("a@b.example"), GrantScope.Standing)));          // irreversible: no grant can cover it
        Assert.Equal("That assistant is not on the team.", Refusal(Make("nobody", ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing)));
        var retired = Team(Astur("Old", AsturShape.Star, "old", retired: true));
        Assert.Equal("That assistant is not on the team.", AsturGrants.Refusal(Make("old", ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing), retired, [], Now));
    }

    [Fact]
    public void The_grant_Dextra_would_need_covers_the_Asturs_and_is_fresh_and_unused()
    {
        var child = Make("channel-a", ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing, "sign in") with { UsedBy = new ValueList<string>(["audit-1"]), RevokedAt = Now };
        var offer = AsturGrants.ParentOffer(child, DextraId, Now.AddMinutes(1));
        Assert.Equal(DextraId, offer.AssistantId);
        Assert.NotEqual(child.Id, offer.Id);
        Assert.Empty(offer.UsedBy);
        Assert.Null(offer.RevokedAt);
        Assert.Equal(Now.AddMinutes(1), offer.Created);
        Assert.Null(Refusal(child with { RevokedAt = null, UsedBy = ValueList<string>.Empty }, offer));
    }

    [Fact]
    public void The_engine_allows_an_Asturs_grant_only_while_Dextras_covers_it_and_asks_again_when_Dextras_is_revoked()
    {
        var astur = Astur("Channel B", AsturShape.Hexagon, "channel-b");
        var chain = new ValueList<AssistantProfile>([astur, Dextra()]);
        var childGrant = Make("channel-b", ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing);
        var parentGrant = Make(DextraId, ActionClass.Login, GrantTarget.Site(Site), GrantScope.Standing);
        var signIn = new Classification(ActionClass.Login, "sign in") { GrantTarget = GrantTarget.Site(Site), Field = "sign in" };
        PolicyDecision Decide(params Grant[] held) => PolicyEngine.Decide(signIn, new PolicyContext(chain, Now) { Grants = new ValueList<Grant>(held) });

        Assert.Equal(Verdict.Ask, Decide(childGrant).Verdict);                                // alone, the Astur's grant covers nothing
        var allowed = Decide(childGrant, parentGrant);
        Assert.Equal(Verdict.Allow, allowed.Verdict);
        Assert.Contains(childGrant.Id, allowed.GrantsUsed);
        Assert.Equal(Verdict.Ask, Decide(childGrant, parentGrant.Revoked(Now)).Verdict);      // revoked at the next action
    }
}
