using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (the notes of AS-16; DESIGN.md rev 5, 9.2, 9.4, 16.2; ED-NEW-dextra-as17-5, -6): the Team tab over a real controller and a real team in a throwaway store. The desk makes, edits and
// retires through the controller's own calls; the source joins the controller's missions to their assistants, follows the team, and has no meter without cost controls.
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-6")]
public sealed class TeamBindingTests
{
    private static DextraLive Live(AsturRig rig, FakeMissionJobs? jobs = null) =>
        new(jobs ?? new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls(), rig.Controller);

    [Fact]
    public async Task The_desk_makes_edits_and_retires_through_the_controller_and_answers_with_what_is_wrong()
    {
        using var rig = await AsturRig.OpenAsync();
        var desk = new ControllerAsturDesk(rig.Controller);
        var draft = AsturDrafter.FromTemplate("inbox", rig.Team.Profiles);

        Assert.Empty(await desk.Create(draft));
        await rig.Controller.Settled();
        var inbox = Assert.Single(rig.Team.Active, p => p.Name == "Inbox");
        Assert.NotEmpty(await desk.Create(draft));                                                  // the same name and look again: refused, and told

        Assert.Empty(await desk.Update(inbox.Id, AsturTeamRules.ToDraft(inbox) with { Style = "terse" }));
        Assert.Equal("terse", rig.Team.Get(inbox.Id)!.Style);
        Assert.NotEmpty(await desk.Update(inbox.Id, AsturTeamRules.ToDraft(inbox) with { Name = "" }));

        Assert.Null(await desk.Retire(inbox.Id));
        Assert.DoesNotContain(rig.Team.Active, p => p.Id == inbox.Id);
        Assert.NotNull(await desk.Retire("nobody"));
    }

    [Fact]
    public async Task A_retirement_waits_for_the_Asturs_missions_and_the_desk_tells_the_controllers_sentence()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var desk = new ControllerAsturDesk(rig.Controller);
        rig.Controller.Runner.Start(new MissionRequest("Work", "Do it.", a.Id, rig.Project.Id, rig.Project.Path, ProviderKind.Claude));

        Assert.Equal("It has missions at work. Stop them first.", await desk.Retire(a.Id));
        Assert.Contains(rig.Team.Active, p => p.Id == a.Id);
    }

    [Fact]
    public async Task Without_a_team_in_the_build_the_desk_says_so()
    {
        using var rig = await AsturRig.OpenAsync(withTeam: false);
        var desk = new ControllerAsturDesk(rig.Controller);

        Assert.Contains("not part of this build", Assert.Single(await desk.Create(new AsturDraft())), StringComparison.Ordinal);
        Assert.Contains("not part of this build", (await desk.Retire("x"))!, StringComparison.Ordinal);
        Assert.Empty(TeamBinding.Problems(rig.Controller));
    }

    [Fact]
    public async Task The_source_over_the_controller_shows_a_new_Astur_and_raises_Changed_when_the_team_changes_and_not_after_it_is_let_go()
    {
        using var rig = await AsturRig.OpenAsync();
        using var source = TeamBinding.For(() => rig.Team.Active, Live(rig), rig.Controller);
        var raised = 0;
        source.Changed += () => raised++;
        Assert.Empty(source.Now().Asturs);

        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);

        Assert.True(raised > 0);
        Assert.Equal([a.Id], source.Now().Asturs.Select(m => m.Id));
        Assert.Equal(rig.Dextra.Id, source.Now().Dextra!.Id);

        source.Dispose();
        var after = raised;
        await rig.MakeAsync("Store", AsturShape.Diamond);
        Assert.Equal(after, raised);
    }

    [Fact]
    public async Task A_task_the_runner_knows_belongs_to_its_assistant_and_one_it_does_not_know_is_Dextras()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        var record = rig.Controller.Runner.Start(new MissionRequest("Work", "Do it.", a.Id, rig.Project.Id, rig.Project.Path, ProviderKind.Claude)).Record;
        var jobs = new FakeMissionJobs();
        jobs.Set(new MissionJob(record.Mission.Id, "Work", MissionState.Working), new MissionJob(Guid.NewGuid(), "Unknown", MissionState.Planning));
        using var source = TeamBinding.For(() => rig.Team.Active, Live(rig, jobs), rig.Controller);

        var snap = source.Now();

        Assert.Equal(["Work"], snap.Find(a.Id)!.Missions.Select(m => m.Title));
        Assert.Equal(AsturPresence.Working, snap.Find(a.Id)!.Presence);
        Assert.Equal(["Unknown"], snap.Dextra!.Missions.Select(m => m.Title));
    }

    [Fact]
    public async Task Without_cost_controls_there_is_no_meter_and_the_facts_are_the_ones_the_controller_holds()
    {
        using var rig = await AsturRig.OpenAsync();
        var a = await rig.MakeAsync("Channel A", AsturShape.Squircle);
        using var source = TeamBinding.For(() => rig.Team.Active, Live(rig), rig.Controller, id => id == a.Id ? 2 : 0);

        var snap = source.Now();

        Assert.Null(snap.Find(a.Id)!.Meter);
        Assert.Null(snap.Dextra!.Meter);
        Assert.Equal(new TeamFacts(2, 0, 0, ""), snap.Find(a.Id)!.Facts);                          // two duties from the scheduler, no notes, no grants, no standing rule
        Assert.Equal("", TeamBinding.StandingRules(rig.Controller, a.Id));
    }
}
