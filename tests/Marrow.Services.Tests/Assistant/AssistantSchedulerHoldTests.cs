using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 7.4, 12.6; ED-NEW-dextra-as10-11, -3): what holds a duty. Stop all holds the scheduler until
// Resume all; "Pause duties for 1 hour" holds it for a while; active hours hold a run until they open; a run whose last run still
// goes is skipped on a time. Whatever was held runs once when the hold ends (catch up once) or is skipped (skip). Run now ignores
// hours and the pause but not Stop all.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-11")]
public sealed class AssistantSchedulerHoldTests
{
    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Stop_all_holds_the_scheduler_and_resume_all_runs_what_came_due_once()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 10));
        rig.Begin();
        rig.MayFire = false;
        rig.Advance(TimeSpan.FromHours(1));
        Assert.Empty(rig.Launcher.Fires); // six times passed, none ran
        Assert.True(rig.Scheduler!.Duties[0].Held);
        rig.MayFire = true;
        rig.Scheduler.Reevaluate();
        var fire = Assert.Single(rig.Launcher.Fires); // however many were held, one run
        Assert.Equal(DutyCause.CatchUp, fire.Cause);
        Assert.False(rig.Scheduler.Duties[0].Held);
    }

    [Fact]
    public void A_duty_that_skips_what_it_missed_skips_the_held_run_when_the_hold_ends()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 10) with { Missed = MissedRunPolicy.Skip });
        rig.Begin();
        rig.MayFire = false;
        rig.Advance(TimeSpan.FromMinutes(30));
        rig.MayFire = true;
        rig.Scheduler!.Reevaluate();
        Assert.Empty(rig.Launcher.Fires);
        Assert.Equal(DutyRunOutcome.Skipped, Assert.Single(rig.Scheduler.Find("d1")!.History).Outcome);
        rig.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(rig.Launcher.Fires); // and the next time runs as usual
    }

    [Fact]
    public void Pause_duties_for_an_hour_holds_them_and_the_pause_ending_runs_what_was_held_once()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 20));
        rig.Begin();
        rig.Scheduler!.PauseFor(TimeSpan.FromHours(1));
        Assert.Equal(rig.Now.AddHours(1), rig.Scheduler.PausedUntil);
        rig.Advance(TimeSpan.FromMinutes(59));
        Assert.Empty(rig.Launcher.Fires);
        rig.Advance(TimeSpan.FromMinutes(1)); // the pause ends at 07:00
        Assert.Single(rig.Launcher.Fires);
        Assert.Null(rig.Scheduler.PausedUntil);
    }

    [Fact]
    public void Resuming_the_duties_early_runs_the_held_run_at_once()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 20));
        rig.Begin();
        rig.Scheduler!.PauseFor(TimeSpan.FromHours(1));
        rig.Advance(TimeSpan.FromMinutes(25));
        Assert.Empty(rig.Launcher.Fires);
        rig.Scheduler.ResumeDuties();
        Assert.Single(rig.Launcher.Fires);
    }

    [Fact]
    public void Active_hours_hold_a_run_until_they_open_and_it_runs_once()
    {
        using var rig = new SchedulerRig(At("2026-10-13T00:00:00+00:00"));
        var duty = SchedulerRig.Every("d1", 60) with { Hours = ActiveHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(17, 0)), Since = rig.Now };
        rig.Store.Put("dextra", duty);
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(8)); // 08:00: eight hourly times passed outside the hours
        Assert.Empty(rig.Launcher.Fires);
        rig.Advance(TimeSpan.FromHours(1)); // 09:00 opens: the held run, and the 09:00 time, are one run
        Assert.Single(rig.Launcher.Fires);
        Assert.Equal(At("2026-10-13T09:00:00+00:00"), rig.Scheduler!.Find("d1")!.LastRun);
        rig.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, rig.Launcher.Fires.Count); // inside the hours it runs on its grid
    }

    [Fact]
    public void A_run_on_the_weekend_waits_for_monday_when_the_hours_are_weekdays()
    {
        using var rig = new SchedulerRig(At("2026-10-17T10:00:00+00:00")) { AutoEnd = false }; // Saturday
        var duty = SchedulerRig.Daily("d1", 11) with { Hours = ActiveHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(17, 0)), Since = rig.Now };
        rig.Store.Put("dextra", duty);
        rig.Begin();
        rig.Advance(TimeSpan.FromDays(2) + TimeSpan.FromHours(5)); // to Monday 15:00
        // Saturday's 11:00 and Sunday's were held and are one run, which goes when the hours open at 09:00 on Monday; Monday's own
        // 11:00 finds that run still going and is skipped.
        Assert.Single(rig.Launcher.Fires);
        Assert.Equal(At("2026-10-19T09:00:00+00:00"), rig.Scheduler!.Find("d1")!.LastRun);
        Assert.Equal(DutyCause.CatchUp, rig.Launcher.Fires[0].Cause);
    }

    [Fact]
    public void A_time_that_comes_while_the_last_run_still_goes_is_skipped_and_the_skip_is_not_repeated_in_the_history()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 10));
        rig.Begin();
        rig.Advance(TimeSpan.FromMinutes(10)); // starts; the mission never ends
        rig.Advance(TimeSpan.FromMinutes(30)); // three more times come and are skipped
        Assert.Single(rig.Launcher.Fires);
        var skips = rig.Scheduler!.Find("d1")!.History.Where(h => h.Outcome == DutyRunOutcome.Skipped).ToList();
        Assert.Single(skips);
        Assert.Equal(3, skips[0].Count);
        Assert.Equal(At("2026-10-13T06:10:00+00:00"), rig.Scheduler.Find("d1")!.LastRun); // a skip is not a run
        Assert.Contains("still going", skips[0].Note, StringComparison.Ordinal);
        rig.Scheduler.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "Done.");
        rig.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, rig.Launcher.Fires.Count);
    }

    [Fact]
    public void A_run_whose_mission_never_reports_back_frees_the_duty_after_a_day()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 60));
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(1));
        rig.Advance(TimeSpan.FromHours(25));
        Assert.True(rig.Launcher.Fires.Count >= 2);
        Assert.Contains(rig.Feed, line => line.Kind == DutyFeedKind.Problem && line.Text.Contains("never reported back", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_now_ignores_active_hours_and_the_pause_but_not_stop_all_or_a_run_in_progress()
    {
        using var rig = new SchedulerRig();
        var duty = SchedulerRig.Daily("d1", 23) with { Hours = ActiveHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(17, 0)) };
        rig.Store.Put("dextra", duty);
        rig.Begin();
        rig.Scheduler!.PauseFor(TimeSpan.FromHours(2));
        Assert.Null(rig.Scheduler.RunNow("d1"));
        var fire = Assert.Single(rig.Launcher.Fires);
        Assert.Equal(DutyCause.Manual, fire.Cause);
        Assert.Equal("The last run is still going.", rig.Scheduler.RunNow("d1"));
        rig.Scheduler.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "ok");
        rig.MayFire = false;
        Assert.Equal("Stop all is on. Tap Resume all first.", rig.Scheduler.RunNow("d1"));
        Assert.Equal("There is no such duty.", rig.Scheduler.RunNow("nope"));
    }

    [Fact]
    public void A_held_run_survives_a_restart_as_a_missed_run()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Every("d1", 30));
        rig.Begin();
        rig.MayFire = false;
        rig.Advance(TimeSpan.FromMinutes(45));
        rig.Scheduler!.Dispose();
        // nth quits while Stop all holds the scheduler. The duty was never handled, so the next start still owes it.
        rig.MayFire = true;
        rig.Begin();
        var fire = Assert.Single(rig.Launcher.Fires);
        Assert.Equal(DutyCause.CatchUp, fire.Cause);
    }
}
