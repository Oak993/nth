using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 6, 7.1; ED-NEW-dextra-as11-8): a duty that nth runs itself, with no mission (the Morning digest), ends inside its
// launch. The scheduler records the run and ends it at once, the way it ends a mission's: a report that ends with the nothing-changed line is a
// quiet run, a failure is a failure, and the duty is not left "running", so the next time it comes it runs again.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-8")]
public sealed class AssistantSchedulerInlineRunTests
{
    [Fact]
    public void A_run_the_launcher_finished_inside_its_launch_is_recorded_and_ended_at_once()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Launcher.FinishAs = DutyRunOutcome.Done;
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(1));
        var duty = rig.Scheduler!.Find("d1")!;
        Assert.Equal(DutyRunOutcome.Done, duty.History[0].Outcome);
        Assert.Equal(rig.Launcher.LastMission, duty.History[0].MissionId);
        Assert.False(rig.Scheduler.Duties[0].Running);
        Assert.Equal(DutyRunOutcome.Done, rig.Store.Saved("dextra").Duties[0].History[0].Outcome);
        Assert.NotNull(duty.LastRun);
    }

    [Fact]
    public void The_next_time_runs_again_because_the_duty_is_not_left_running()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Launcher.FinishAs = DutyRunOutcome.Done;
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(1));
        rig.Advance(TimeSpan.FromDays(1));
        Assert.Equal(2, rig.Launcher.Fires.Count);
        Assert.DoesNotContain(rig.Scheduler!.Find("d1")!.History, run => run.Outcome == DutyRunOutcome.Skipped);
    }

    [Fact]
    public void A_report_ending_with_the_nothing_changed_line_is_a_quiet_run_and_a_failure_is_a_failure()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Launcher.FinishAs = DutyRunOutcome.Done;
        rig.Launcher.FinishReport = "Nothing to say.\nRESULT: nothing_changed";
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(1));
        Assert.Equal(DutyRunOutcome.NothingChanged, rig.Scheduler!.Find("d1")!.History[0].Outcome);
        Assert.Single(rig.Feed, line => line.Kind == DutyFeedKind.Quiet);
        rig.Launcher.FinishAs = DutyRunOutcome.Failed;
        rig.Launcher.FinishReport = "RESULT: nothing_changed";
        rig.Advance(TimeSpan.FromDays(1));
        Assert.Equal(DutyRunOutcome.Failed, rig.Scheduler.Find("d1")!.History[0].Outcome);
    }

    [Fact]
    public void A_dry_run_that_finished_inside_its_launch_ends_too_and_is_not_a_run_of_the_schedule()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Launcher.FinishAs = DutyRunOutcome.Done;
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        Assert.Null(rig.Scheduler!.RunNow("d1", dry: true));
        var duty = rig.Scheduler.Find("d1")!;
        Assert.Equal(DutyRunOutcome.Done, duty.History[0].Outcome);
        Assert.Null(duty.LastRun);
        Assert.False(rig.Scheduler.Duties[0].Running);
    }

    [Fact]
    public void A_duty_that_looks_every_ten_minutes_runs_each_time_because_each_run_is_over_before_the_next()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Launcher.FinishAs = DutyRunOutcome.Done;
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 10));
        rig.Begin();
        rig.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(rig.Launcher.Fires);
        rig.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, rig.Launcher.Fires.Count);
        Assert.False(rig.Scheduler!.Duties[0].Held);
    }
}
