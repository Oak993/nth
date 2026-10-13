using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 11.3, 12.4; ED-NEW-dextra-as10-18): where a duty becomes a mission. A duty with no project works in
// the owner's one project and says so when there are none or several; one whose project is gone does not run and says why; a
// reserve rule that cannot be read stops the run before it starts rather than starting it unguarded; and what is read from outside
// (a folder, a page) marks the mission it starts as tainted.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-18")]
public sealed class DutyLauncherTests
{
    private static Duty Sample(Guid? project = null, string? reserve = null) =>
        new("d1", "Weekly note", "dextra", new ManualTrigger(), "Write the weekly note.") { ProjectId = project, ReserveText = reserve };

    private static DutyFire Fire(Duty duty, DutyCause cause = DutyCause.Manual) => new(duty, cause, false, null, false);

    private static DutyLauncher Launcher(DutyHostRig rig, params MissionProject[] projects) =>
        new(rig.Controller, new DutyLauncherOptions { Projects = () => projects, OnMain = work => work(), AccountNames = () => ["Claude B", "Codex A"] });

    [Fact]
    public async Task A_duty_with_no_project_works_in_the_owners_one_project()
    {
        using var rig = new DutyHostRig();
        var launch = await Launcher(rig, rig.Project).Launch(Fire(Sample()));
        Assert.Null(launch.Refused);
        var mission = rig.Controller.Runner.Find(launch.MissionId!.Value)!;
        Assert.Equal(rig.Project.Id, mission.ProjectId);
        Assert.Equal(rig.Project.Path, mission.ProjectPath);
        Assert.Equal(("d1", MissionOrigin.Duty), (mission.DutyId, mission.Mission.Origin));
    }

    [Fact]
    public async Task A_duty_with_no_project_among_none_or_several_does_not_run_and_says_which()
    {
        using var rig = new DutyHostRig();
        var none = await Launcher(rig).Launch(Fire(Sample()));
        Assert.Equal("There is no project for this duty to work in.", none.Refused);
        var other = new MissionProject(Guid.NewGuid(), "Other", rig.Project.Path);
        var several = await Launcher(rig, rig.Project, other).Launch(Fire(Sample()));
        Assert.Equal("This duty has no project, and there is more than one. Open the duty and pick one.", several.Refused);
        Assert.Empty(rig.Controller.Runner.Missions);
    }

    [Fact]
    public async Task A_duty_whose_project_is_gone_does_not_run_and_one_that_names_a_project_works_in_that_one()
    {
        using var rig = new DutyHostRig();
        var other = new MissionProject(Guid.NewGuid(), "Other", rig.Project.Path);
        var launcher = Launcher(rig, rig.Project, other);
        var gone = await launcher.Launch(Fire(Sample(Guid.NewGuid())));
        Assert.Equal("This duty's project is gone. Open the duty and pick another.", gone.Refused);
        Assert.Empty(rig.Controller.Runner.Missions);
        var named = await launcher.Launch(Fire(Sample(other.Id)));
        Assert.Equal(other.Id, rig.Controller.Runner.Find(named.MissionId!.Value)!.ProjectId);
    }

    [Fact]
    public async Task A_reserve_rule_that_cannot_be_read_stops_the_run_before_it_starts_and_one_that_can_does_not()
    {
        using var rig = new DutyHostRig();
        var launcher = Launcher(rig, rig.Project);
        var unreadable = await launcher.Launch(Fire(Sample(reserve: "Keep 30% of what's left")));
        Assert.StartsWith("This duty's reserve rule could not be read: ", unreadable.Refused, StringComparison.Ordinal);
        Assert.EndsWith(" It did not run.", unreadable.Refused, StringComparison.Ordinal);
        Assert.Empty(rig.Controller.Runner.Missions);
        var fine = await launcher.Launch(Fire(Sample(reserve: "keep 30% on Claude B")));
        Assert.Null(fine.Refused);
        Assert.Single(rig.Controller.Runner.Missions);
    }

    [Fact]
    public async Task A_start_that_throws_is_a_refusal_that_says_so_and_the_launcher_stays_usable()
    {
        using var rig = new DutyHostRig();
        var launcher = new DutyLauncher(rig.Controller, new DutyLauncherOptions
        {
            Projects = () => throw new InvalidOperationException("the project list is being rewritten"),
            OnMain = work => work(),
        });
        var launch = await launcher.Launch(Fire(Sample()));
        Assert.Equal("nth could not start it: the project list is being rewritten", launch.Refused);
        Assert.Null(launch.MissionId);
    }

    [Fact]
    public async Task A_dry_run_is_titled_as_one_and_flagged()
    {
        using var rig = new DutyHostRig();
        var launch = await Launcher(rig, rig.Project).Launch(new DutyFire(Sample(), DutyCause.Manual, true, null, false));
        var mission = rig.Controller.Runner.Find(launch.MissionId!.Value)!;
        Assert.Equal("Dry run: Weekly note", mission.Mission.Title);
        Assert.True(mission.DryRun);
    }
}
