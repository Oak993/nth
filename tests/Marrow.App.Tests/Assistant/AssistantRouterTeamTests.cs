using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using static Marrow.App.Tests.Fixtures.RouterRig;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 3.4, 15.4; ED-NEW-dextra-as14-7): the router fills a Hydra team's head profiles and chooses the reviewer.
// A team runs on its lead's provider and account (a head has no account of its own), so what is chosen for it is the model: the one its
// plain heads edit with, and a "Reading" profile on a cheaper one for heads that only search and read. The reviewer is a model other than
// the workers', on the account of the mission's provider with the most room.
[Trait("Decision", "ED-NEW-dextra-as14-7")]
[Trait("Feature", "AS-14")]
public sealed class AssistantRouterTeamTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private RouterRig Rig(bool claudeB = false) => new(temp.Folder, claudeB);

    private static MissionRecord Team(RouterRig rig, string model = "opus") =>
        rig.Missions.Started(rig.Missions.Request(team: true) with { Model = model });

    // ---- a team's profiles ----

    [Fact]
    public void A_team_gets_the_strongest_model_for_its_heads_and_a_reading_profile_on_the_light_one()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 30);
        var mission = Team(rig);
        var pair = rig.Router.TeamPair(mission)!;
        Assert.Equal(ProviderKind.Claude, pair.Provider);
        Assert.Equal("opus", pair.OrchestratorModel);
        Assert.Equal("opus", pair.WorkerModel);
        var reading = Assert.Single(pair.HeadProfiles);
        Assert.Equal(("Reading", "haiku"), (reading.Name, reading.Model));
        Assert.Null(reading.Provider);                                 // on the lead's provider
        Assert.Null(reading.Effort);
        Assert.Equal("reading", reading.AgentSlug);
        Assert.Equal("Reading (haiku)", reading.BriefLabel);
    }

    [Fact]
    public void A_profile_is_the_same_profile_every_time_it_is_asked_for_and_the_team_is_worked_out_again_after_five_seconds()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 30);
        var mission = Team(rig);
        var first = rig.Router.TeamPair(mission)!;
        Assert.Same(first, rig.Router.TeamPair(mission));
        rig.Clock.Advance(TimeSpan.FromSeconds(4.9));
        Assert.Same(first, rig.Router.TeamPair(mission));
        rig.Clock.Advance(TimeSpan.FromSeconds(0.1));
        var later = rig.Router.TeamPair(mission)!;
        Assert.NotSame(first, later);
        Assert.Equal(first, later);                                    // the same ids, the same models

        // A different mission has its own ids.
        var other = Team(rig);
        Assert.NotEqual(first.Id, rig.Router.TeamPair(other)!.Id);
        Assert.NotEqual(first.HeadProfiles[0].Id, rig.Router.TeamPair(other)!.HeadProfiles[0].Id);
    }

    [Fact]
    public void The_team_follows_the_room_the_accounts_have_when_it_is_worked_out()
    {
        using var rig = Rig();
        rig.Reserve.Always(30);
        rig.Read(ReserveRig.Claude20, 30);
        var mission = Team(rig);
        Assert.Equal("opus", rig.Router.TeamPair(mission)!.WorkerModel);
        // The account has no room left for any model: there is nothing to fill the team with.
        rig.Read(ReserveRig.Claude20, 69);
        rig.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(rig.Router.TeamPair(mission));
    }

    [Fact]
    public void A_provider_with_one_model_needs_no_profile_and_a_pinned_or_unsealable_mission_gets_no_pair()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 30);
        rig.ModelsOf[ProviderKind.Claude] = [new RouteModel("only", ModelTier.Strong)];
        var mission = Team(rig, "only");
        var pair = rig.Router.TeamPair(mission)!;
        Assert.Equal("only", pair.WorkerModel);
        Assert.Empty(pair.HeadProfiles);

        // The owner pinned the mission's model: its heads follow the lead, so there is no pair.
        Assert.Null(rig.Router.TeamPair(mission with { ModelPinned = true }));
        rig.Refused[ProviderKind.Claude] = "Claude cannot run mission work.";
        rig.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(rig.Router.TeamPair(mission));
    }

    [Fact]
    public void A_team_is_chosen_among_the_models_of_its_leads_account_only()
    {
        using var rig = Rig(claudeB: true);
        rig.Reserve.Always(30, "Claude B");
        rig.Read(ReserveRig.Claude20, 30);
        rig.Read(ClaudeB, 69);                                         // no room for a turn under its floor of 70
        var onClaudeB = Team(rig) with { AccountId = RouterRig.ClaudeBId };
        Assert.Null(rig.Router.TeamPair(onClaudeB));
        var onClaude20 = Team(rig) with { AccountId = RouterRig.ClaudeId };
        Assert.NotNull(rig.Router.TeamPair(onClaude20));
    }

    [Fact]
    public void The_pairs_kept_are_capped_and_the_oldest_go_first()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 30);
        var mission = Team(rig);
        var records = Enumerable.Range(0, AssistantRouter.TeamsKept + 10).Select(_ => mission with { Mission = mission.Mission with { Id = Guid.NewGuid() } }).ToList();
        var first = rig.Router.TeamPair(records[0])!;
        Assert.Same(first, rig.Router.TeamPair(records[0]));
        foreach (var other in records.Skip(1))
        {
            rig.Clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.NotNull(rig.Router.TeamPair(other));
        }
        // The first one was pushed out by the later ones (well within its five seconds): asking again works it out afresh.
        Assert.NotSame(first, rig.Router.TeamPair(records[0]));
        var last = rig.Router.TeamPair(records[^1])!;
        Assert.Same(last, rig.Router.TeamPair(records[^1]));
    }

    // ---- the reviewer ----

    [Fact]
    public void The_reviewer_runs_on_a_model_other_than_the_workers_on_the_account_of_their_provider_with_the_most_room()
    {
        using var rig = Rig(claudeB: true);
        rig.Read(ReserveRig.Claude20, 50);
        rig.Read(ClaudeB, 5);                                          // plenty of room, but its rate is the conservative default
        var mission = rig.Missions.Started(rig.Missions.Request() with { Model = "sonnet" });
        var lead = rig.Missions.Agents.Lead(mission).Id;
        rig.Missions.Agents.Finish(lead, "Done.");
        rig.Missions.Runner.OnTurnFinished(lead, TurnStatus.Completed, continues: false);
        MissionRig.Until(() => rig.Missions.Now(mission).ReviewerThread is not null, "the reviewer to start");
        var launch = rig.Missions.Agents.Of(rig.Missions.Now(mission).ReviewerThread!.Value).Launch;
        Assert.True(launch.Reviewer);
        Assert.Equal("opus", launch.Model);                            // not sonnet, which the workers used; haiku cannot judge
        Assert.Equal(RouterRig.ClaudeId, launch.AccountId);
        Assert.Equal(ProviderKind.Claude, launch.Provider);
    }

    [Fact]
    public void The_reviewer_goes_to_another_account_of_the_provider_when_the_workers_account_has_no_room_left()
    {
        using var rig = Rig(claudeB: true);
        rig.Reserve.Always(30);
        rig.Read(ReserveRig.Claude20, 40);
        rig.Read(ClaudeB, 20);
        var mission = rig.Missions.Started(rig.Missions.Request() with { Model = "sonnet" });
        rig.Read(ReserveRig.Claude20, 68);
        var pick = rig.Router.Pick(rig.Missions.Now(mission), ["haiku", "sonnet", "opus"])!;
        Assert.Equal(("opus", RouterRig.ClaudeBId, ClaudeB), (pick.Model, pick.AccountId, pick.Account));
        // With no room anywhere there is no better answer than the provider's first account: the runner then uses its own pick.
        rig.Read(ClaudeB, 99);
        rig.Reserve.Always(30, "Claude B");
        Assert.Null(rig.Router.Pick(rig.Missions.Now(mission), ["haiku", "sonnet", "opus"]));
    }
}
