using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 7.3, 7.4; ED-NEW-dextra-as10-10, -11): how a run ends and what a run is. A report that ends with
// the nothing-changed line is quiet; a run that failed or was stopped never is. A dry run is recorded but is not a run of the
// schedule. A wake from Task Scheduler runs the duty even when it skips missed runs, once. The desktop's lock reaches the run.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-10")]
public sealed class AssistantSchedulerRunTests
{
    private static (SchedulerRig Rig, Guid Mission) Ran()
    {
        var rig = new SchedulerRig { AutoEnd = false };
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(1));
        return (rig, rig.Launcher.LastMission!.Value);
    }

    [Fact]
    public void A_report_that_ends_with_the_nothing_changed_line_is_a_quiet_run_with_one_quiet_feed_line()
    {
        var (rig, mission) = Ran();
        using (rig)
        {
            rig.Scheduler!.MissionEnded(mission, DutyRunOutcome.Done, "Looked at the page.\nRESULT: nothing_changed");
            Assert.Equal(DutyRunOutcome.NothingChanged, rig.Scheduler.Find("d1")!.History[0].Outcome);
            var quiet = Assert.Single(rig.Feed, line => line.Kind == DutyFeedKind.Quiet);
            Assert.Equal(mission, quiet.Mission);
            Assert.False(rig.Scheduler.Duties[0].Running);
        }
    }

    [Theory]
    [InlineData(DutyRunOutcome.Done, "The price fell to $640.", DutyRunOutcome.Done)]
    [InlineData(DutyRunOutcome.Failed, "RESULT: nothing_changed", DutyRunOutcome.Failed)]
    [InlineData(DutyRunOutcome.Stopped, "RESULT: nothing_changed", DutyRunOutcome.Stopped)]
    public void A_run_that_did_something_or_failed_or_was_stopped_is_never_quiet(DutyRunOutcome ended, string report, DutyRunOutcome recorded)
    {
        var (rig, mission) = Ran();
        using (rig)
        {
            rig.Scheduler!.MissionEnded(mission, ended, report);
            Assert.Equal(recorded, rig.Scheduler.Find("d1")!.History[0].Outcome);
            Assert.DoesNotContain(rig.Feed, line => line.Kind == DutyFeedKind.Quiet);
            Assert.Equal(recorded, rig.Store.Saved("dextra").Duties[0].History[0].Outcome);
        }
    }

    [Fact]
    public void A_mission_that_is_not_a_duty_runs_changes_nothing()
    {
        var (rig, _) = Ran();
        using (rig)
        {
            var before = rig.Store.Saves;
            rig.Scheduler!.MissionEnded(Guid.NewGuid(), DutyRunOutcome.Done, "x");
            Assert.Equal(before, rig.Store.Saves);
        }
    }

    [Fact]
    public void The_history_holds_fifty_runs()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 5));
        rig.Begin();
        for (var i = 0; i < 60; i++)
        {
            rig.Advance(TimeSpan.FromMinutes(5));
            rig.Scheduler!.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "ok");
        }
        Assert.Equal(60, rig.Launcher.Fires.Count);
        Assert.Equal(Duty.HistoryLimit, rig.Scheduler!.Find("d1")!.History.Count);
        Assert.Equal(Duty.HistoryLimit, rig.Store.Saved("dextra").Duties[0].History.Count);
    }

    [Fact]
    public void A_dry_run_is_recorded_as_one_but_is_not_a_run_of_the_schedule()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        Assert.Null(rig.Scheduler!.RunNow("d1", dry: true));
        var fire = Assert.Single(rig.Launcher.Fires);
        Assert.True(fire.Dry);
        var duty = rig.Scheduler.Find("d1")!;
        Assert.Equal("Dry run", Assert.Single(duty.History).Note);
        Assert.Null(duty.LastRun);
        Assert.Null(duty.Handled);
        rig.Scheduler.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "steps");
        rig.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, rig.Launcher.Fires.Count); // 07:00 still runs for real
        Assert.False(rig.Launcher.Fires[1].Dry);
    }

    [Fact]
    public void A_wake_runs_the_duty_even_when_it_skips_missed_runs_and_only_once()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7) with { Missed = MissedRunPolicy.Skip, WakePc = true });
        rig.Begin();
        Assert.Null(rig.Scheduler!.WakeRun("d1"));
        var fire = Assert.Single(rig.Launcher.Fires);
        Assert.Equal(DutyCause.Wake, fire.Cause);
        Assert.Equal("It is running already.", rig.Scheduler.WakeRun("d1"));
        rig.Scheduler.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "ok");
        Assert.Equal("It already ran a moment ago.", rig.Scheduler.WakeRun("d1"));
        rig.Advance(AssistantScheduler.WakeDuplicate + TimeSpan.FromMinutes(1));
        Assert.Null(rig.Scheduler.WakeRun("d1"));
        Assert.Equal("There is no such duty.", rig.Scheduler.WakeRun("ghost"));
    }

    [Fact]
    public void A_wake_does_not_run_a_duty_that_is_off_or_held_by_stop_all()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7) with { Enabled = false }, SchedulerRig.Daily("d2", 7));
        rig.Begin();
        Assert.Equal("The duty is off.", rig.Scheduler!.WakeRun("d1"));
        rig.MayFire = false;
        Assert.Equal("Stop all is on.", rig.Scheduler.WakeRun("d2"));
        Assert.Empty(rig.Launcher.Fires);
    }

    [Fact]
    public void A_run_on_a_locked_pc_is_told_so()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7) with { NeedsDesktop = true });
        rig.Begin();
        rig.Locked = true;
        rig.Advance(TimeSpan.FromHours(1));
        Assert.True(Assert.Single(rig.Launcher.Fires).DesktopLocked);
        Assert.True(rig.Launcher.Fires[0].Duty.NeedsDesktop);
    }

    [Fact]
    public void A_stopped_scheduler_fires_nothing_more()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 10));
        rig.Begin();
        rig.Advance(TimeSpan.FromMinutes(10));
        rig.Scheduler!.Dispose();
        Assert.Equal(0, rig.Clock.ActiveTimers);
        rig.Advance(TimeSpan.FromHours(2));
        Assert.Single(rig.Launcher.Fires);
        Assert.Contains("stopped", rig.Scheduler.Add(SchedulerRig.Daily("d2", 9)).Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Eight_hours_with_three_duties_firing_leaves_one_timer_and_a_flat_heap()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("a", 5), SchedulerRig.Every("b", 15), SchedulerRig.Daily("c", 8, 30));
        rig.Begin();
        void Hours(int minutes)
        {
            for (var minute = 0; minute < minutes; minute++)
            {
                rig.Advance(TimeSpan.FromMinutes(1));
                foreach (var view in rig.Scheduler!.Duties.Where(v => v.Running).ToList())
                    rig.Scheduler.MissionEnded(view.Duty.History[0].MissionId!.Value, DutyRunOutcome.Done, "RESULT: nothing_changed");
            }
        }
        Hours(30); // warms up every path once
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var fired = rig.Launcher.Fires.Count;
        Hours(8 * 60);
        var after = GC.GetTotalMemory(forceFullCollection: true);
        Assert.True(rig.Launcher.Fires.Count - fired > 100, "the three duties kept firing");
        Assert.Equal(1, rig.Clock.ActiveTimers);
        Assert.All(rig.Scheduler!.Duties, v => Assert.True(v.Duty.History.Count <= Duty.HistoryLimit));
        // The retained heap does not grow with the hours: far under what 8 hours of leaked runs would be.
        Assert.True(after - before < 2 * 1024 * 1024, $"the heap grew by {(after - before) / 1024} KB over 8 simulated hours");
    }
}
