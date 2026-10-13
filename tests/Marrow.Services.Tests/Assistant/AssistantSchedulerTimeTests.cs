using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 7.2; ED-NEW-dextra-as10-11): the scheduler on a manual clock. A daily duty fires at its time
// and not before; weekdays skip the weekend; a duty in a zone with clock changes keeps its wall-clock time; a run owed while nth
// was closed is caught up once or skipped by the duty's own policy; an Every schedule fires on its grid; a Once duty fires once
// and switches itself off; the timer never sleeps longer than a minute and sleeps not at all with nothing to do.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-11")]
public sealed class AssistantSchedulerTimeTests
{
    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void A_daily_duty_fires_at_its_time_and_again_the_next_day_and_not_before()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 8));
        rig.Begin();
        rig.Advance(TimeSpan.FromMinutes(119));
        Assert.Empty(rig.Launcher.Fires);
        rig.Advance(TimeSpan.FromMinutes(1));
        var fire = Assert.Single(rig.Launcher.Fires);
        Assert.Equal(DutyCause.Time, fire.Cause);
        Assert.False(fire.Dry);
        Assert.Equal("d1", fire.Duty.Id);
        rig.Advance(TimeSpan.FromHours(24));
        Assert.Equal(2, rig.Launcher.Fires.Count);
        Assert.Equal(At("2026-10-14T08:00:00+00:00"), rig.Scheduler!.Find("d1")!.LastRun);
    }

    [Fact]
    public void The_run_is_recorded_with_its_mission_and_the_schedule_moves_on()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 8));
        rig.Begin();
        rig.AdvanceTo(At("2026-10-13T08:00:00+00:00"));
        var duty = rig.Scheduler!.Find("d1")!;
        var run = Assert.Single(duty.History);
        Assert.Equal(DutyRunOutcome.Started, run.Outcome);
        Assert.Equal(rig.Launcher.LastMission, run.MissionId);
        Assert.Equal(At("2026-10-13T08:00:00+00:00"), duty.Handled);
        var view = Assert.Single(rig.Scheduler.Duties);
        Assert.True(view.Running);
        Assert.Equal(At("2026-10-14T08:00:00+00:00"), view.NextRun);
        Assert.Single(rig.Store.Saved("dextra").Duties[0].History);
    }

    [Fact]
    public void Weekdays_fire_monday_to_friday_and_not_on_the_weekend()
    {
        using var rig = new SchedulerRig(At("2026-10-15T00:00:00+00:00")); // Thursday
        var duty = SchedulerRig.Daily("d1", 9) with { Trigger = new TimeTrigger(TimeSchedule.OnWeekdays(new TimeOnly(9, 0)), "UTC"), Since = rig.Now };
        rig.Store.Put("dextra", duty);
        rig.Begin();
        rig.Advance(TimeSpan.FromDays(5)); // through Tuesday 10/20 00:00
        Assert.Equal(3, rig.Launcher.Fires.Count); // Thursday, Friday and Monday: nothing on Saturday or Sunday
        Assert.Equal(At("2026-10-19T09:00:00+00:00"), rig.Scheduler!.Find("d1")!.LastRun);
    }

    [Fact]
    public void A_duty_in_a_zone_with_clock_changes_keeps_its_wall_clock_time_through_spring_forward()
    {
        using var rig = new SchedulerRig(At("2026-03-07T12:00:00+00:00"));
        var duty = SchedulerRig.Daily("d1", 9, zone: "Eastern Standard Time") with { Since = rig.Now };
        rig.Store.Put("dextra", duty);
        rig.Begin();
        rig.Advance(TimeSpan.FromDays(3));
        var runs = rig.Scheduler!.Find("d1")!.History.Select(r => r.At).OrderBy(x => x).ToList();
        // 09:00 EST on the 7th (14:00Z), then 09:00 EDT on the 8th and 9th (13:00Z): the offset changed, the wall clock did not.
        Assert.Equal([At("2026-03-07T14:00:00+00:00"), At("2026-03-08T13:00:00+00:00"), At("2026-03-09T13:00:00+00:00")], runs);
    }

    [Fact]
    public void A_time_the_clocks_skip_runs_once_that_day()
    {
        using var rig = new SchedulerRig(At("2026-03-07T12:00:00+00:00"));
        var duty = SchedulerRig.Daily("d1", 2, 30, zone: "Eastern Standard Time") with { Since = rig.Now };
        rig.Store.Put("dextra", duty);
        rig.Begin();
        rig.Advance(TimeSpan.FromDays(2));
        var runs = rig.Scheduler!.Find("d1")!.History.Select(r => r.At).OrderBy(x => x).ToList();
        Assert.Equal([At("2026-03-08T07:30:00+00:00"), At("2026-03-09T06:30:00+00:00")], runs); // 03:30 EDT on the skipped day, then 02:30 EDT
    }

    [Fact]
    public void Days_missed_while_nth_was_closed_are_caught_up_once()
    {
        using var rig = new SchedulerRig(At("2026-10-13T09:00:00+00:00"));
        var duty = SchedulerRig.Daily("d1", 8) with { Since = At("2026-10-01T00:00:00+00:00"), Handled = At("2026-10-09T08:00:05+00:00") };
        rig.Store.Put("dextra", duty);
        rig.Begin();
        var fire = Assert.Single(rig.Launcher.Fires); // four 08:00s passed: one run is owed
        Assert.Equal(DutyCause.CatchUp, fire.Cause);
        rig.Advance(TimeSpan.FromHours(30));
        Assert.Equal(2, rig.Launcher.Fires.Count); // and then the schedule goes on at 08:00 the next day
    }

    [Fact]
    public void A_duty_that_skips_missed_runs_records_the_skip_and_runs_at_its_next_time()
    {
        using var rig = new SchedulerRig(At("2026-10-13T09:00:00+00:00"));
        var duty = SchedulerRig.Daily("d1", 8) with { Since = At("2026-10-01T00:00:00+00:00"), Handled = At("2026-10-09T08:00:05+00:00"), Missed = MissedRunPolicy.Skip };
        rig.Store.Put("dextra", duty);
        rig.Begin();
        Assert.Empty(rig.Launcher.Fires);
        var run = Assert.Single(rig.Scheduler!.Find("d1")!.History);
        Assert.Equal(DutyRunOutcome.Skipped, run.Outcome);
        Assert.Equal(DutyCause.CatchUp, run.Cause);
        Assert.Contains(rig.Feed, line => line.Kind == DutyFeedKind.Skipped && line.DutyId == "d1");
        rig.Advance(TimeSpan.FromHours(23));
        Assert.Single(rig.Launcher.Fires);
        Assert.Equal(DutyCause.Time, rig.Launcher.Fires[0].Cause);
    }

    [Fact]
    public void A_run_a_minute_late_is_on_time_and_one_an_hour_late_is_a_catch_up()
    {
        using var rig = new SchedulerRig(At("2026-10-13T08:01:00+00:00"));
        var recent = SchedulerRig.Daily("recent", 8) with { Since = At("2026-10-01T00:00:00+00:00"), Handled = At("2026-10-12T08:00:05+00:00"), Missed = MissedRunPolicy.Skip };
        rig.Store.Put("dextra", recent);
        rig.Begin();
        Assert.Equal(DutyCause.Time, Assert.Single(rig.Launcher.Fires).Cause); // Skip does not skip a run that is only a minute late
    }

    [Fact]
    public void An_every_schedule_fires_on_its_grid()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 10));
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(1));
        Assert.Equal(6, rig.Launcher.Fires.Count);
        Assert.Equal(At("2026-10-13T07:00:00+00:00"), rig.Scheduler!.Find("d1")!.LastRun);
    }

    [Fact]
    public void A_once_duty_fires_once_and_switches_itself_off()
    {
        using var rig = new SchedulerRig();
        var once = SchedulerRig.Daily("d1", 8) with { Trigger = new TimeTrigger(TimeSchedule.Once(new DateTime(2026, 10, 13, 7, 0, 0)), "UTC") };
        rig.Store.Put("dextra", once);
        rig.Begin();
        rig.Advance(TimeSpan.FromDays(2));
        Assert.Single(rig.Launcher.Fires);
        var duty = rig.Scheduler!.Find("d1")!;
        Assert.False(duty.Enabled);
        Assert.Single(duty.History);
        Assert.False(rig.Store.Saved("dextra").Duties[0].Enabled);
    }

    [Fact]
    public void A_duty_switched_off_does_not_fire_and_switching_it_on_does_not_catch_up_the_days_it_was_off()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 8) with { Enabled = false });
        rig.Begin();
        rig.Advance(TimeSpan.FromDays(3));
        Assert.Empty(rig.Launcher.Fires);
        Assert.Null(rig.Scheduler!.SetEnabled("d1", true));
        Assert.Empty(rig.Launcher.Fires); // no catch-up for the three days it was off
        rig.Advance(TimeSpan.FromHours(24));
        Assert.Single(rig.Launcher.Fires);
    }

    [Fact]
    public void The_timer_sleeps_at_most_a_minute_and_not_at_all_with_nothing_to_do()
    {
        using var rig = new SchedulerRig();
        rig.Begin();
        Assert.Equal(0, rig.Clock.ActiveTimers);
        rig.Scheduler!.Add(SchedulerRig.Daily("d1", 23));
        Assert.Equal(1, rig.Clock.ActiveTimers);
        Assert.True(rig.Clock.TimerDeadlines[0] <= TimeSpan.FromSeconds(60));
        rig.Scheduler.Remove("d1");
        Assert.Equal(0, rig.Clock.ActiveTimers);
    }

    [Fact]
    public void A_refused_launch_is_recorded_and_the_next_time_still_comes()
    {
        using var rig = new SchedulerRig();
        rig.Launcher.Refuse = "No project is set for this duty.";
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 30));
        rig.Begin();
        rig.Advance(TimeSpan.FromMinutes(30));
        var run = Assert.Single(rig.Scheduler!.Find("d1")!.History);
        Assert.Equal(DutyRunOutcome.Refused, run.Outcome);
        Assert.Equal("No project is set for this duty.", run.Note);
        Assert.Contains(rig.Feed, line => line.Kind == DutyFeedKind.Problem);
        rig.Launcher.Refuse = null;
        rig.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(2, rig.Launcher.Fires.Count);
    }

    [Fact]
    public void A_launcher_that_throws_is_a_refusal_and_never_stops_the_scheduler()
    {
        using var rig = new SchedulerRig();
        rig.Launcher.Throw = new InvalidOperationException("boom");
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 30));
        rig.Begin();
        rig.Advance(TimeSpan.FromMinutes(60));
        Assert.Equal(2, rig.Launcher.Fires.Count);
        Assert.All(rig.Scheduler!.Find("d1")!.History, run => Assert.Equal(DutyRunOutcome.Refused, run.Outcome));
    }
}
