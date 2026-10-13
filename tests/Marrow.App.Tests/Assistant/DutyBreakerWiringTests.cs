using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 13.2 "Failed runs never retry endlessly", 7.1; ED-NEW-dextra-as10-19, AS-15's ED-NEW-dextra-as15-5): the scheduler's half of
// the failure breaker. Every run's end is reported to the watch (a finished run, even a quiet one, is a success; a failed one a failure; one the
// owner stopped and a dry run are neither); a duty the breaker paused does not fire from its time, and says so in its list and its history, while the
// owner's Run now still starts it; turning the duty off and on again lets it go on; and when nth starts again the card of a duty that is still paused
// is raised again.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-19")]
public sealed class DutyBreakerWiringTests
{
    private static MissionRecord Run(DutyHostRig rig, string duty, int number)
    {
        MissionRig.Until(() => rig.Controller.Runner.Missions.Count(m => m.DutyId == duty) >= number, "run " + number);
        return rig.Controller.Runner.Missions.Where(m => m.DutyId == duty).OrderBy(m => m.Created).ElementAt(number - 1);
    }

    private static void Fail(DutyHostRig rig, string duty, int number)
    {
        rig.Controller.OnTurnFinished(rig.LeadOf(Run(rig, duty, number)), TurnStatus.Failed, false);
        MissionRig.Until(() => !rig.Host.Scheduler.Find(duty)!.History.Any(h => h.Outcome == DutyRunOutcome.Started), "the failed run to be counted");
    }

    private static void FailThrice(DutyHostRig rig, string duty)
    {
        for (var number = 1; number <= 3; number++)
        {
            rig.Clock.Advance(TimeSpan.FromMinutes(10));
            Fail(rig, duty, number);
        }
    }

    [Fact]
    public void Three_failed_runs_in_a_row_pause_the_duty_with_one_card_and_the_next_time_is_skipped_with_the_reason()
    {
        using var rig = new DutyHostRig();
        rig.Save(DutyHostRig.Every("d1", 10));
        rig.Host.Start();
        FailThrice(rig, "d1");
        Assert.True(rig.Controller.Duties!.IsPaused("d1"));
        var card = Assert.Single(rig.Controller.Queue.Waiting, c => c.Kind == DecisionKind.Duty);
        Assert.Equal("Duty d1 was paused after 3 failures in a row", card.Title);

        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        var view = Assert.Single(rig.Host.Scheduler.Duties);
        Assert.Equal(AssistantScheduler.BreakerWhy, view.Status);
        var line = rig.Host.Scheduler.Find("d1")!.History[0];
        Assert.Equal((DutyRunOutcome.Skipped, AssistantScheduler.BreakerWhy), (line.Outcome, line.Note));
        Assert.Equal(3, rig.Controller.Runner.Missions.Count(m => m.DutyId == "d1")); // no fourth run was started
    }

    [Fact]
    public void The_owners_Run_now_still_starts_a_duty_the_breaker_paused_and_a_success_does_not_hide_the_pause()
    {
        using var rig = new DutyHostRig();
        rig.Save(DutyHostRig.Every("d1", 10));
        rig.Host.Start();
        FailThrice(rig, "d1");
        rig.Clock.Advance(TimeSpan.FromSeconds(1)); // the run is told apart from the third by when it began
        Assert.Null(rig.Host.Scheduler.RunNow("d1"));
        var manual = Run(rig, "d1", 4);
        rig.Complete(manual, "RESULT: nothing_changed");
        MissionRig.Until(() => !rig.Host.Scheduler.Duties[0].Running, "the manual run to end");
        Assert.True(rig.Controller.Duties!.IsPaused("d1")); // a run the owner started by hand does not lift the pause
    }

    [Fact]
    public void A_run_that_found_nothing_a_stopped_run_and_a_dry_run_are_not_failures()
    {
        using var rig = new DutyHostRig();
        rig.Save(DutyHostRig.Every("d1", 10));
        rig.Host.Start();
        for (var number = 1; number <= 2; number++)
        {
            rig.Clock.Advance(TimeSpan.FromMinutes(10));
            Fail(rig, "d1", number);
        }
        Assert.Equal(2, rig.Controller.Duties!.HealthOf("d1").ConsecutiveFailures);

        // A dry run that fails is not counted.
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(rig.Host.Scheduler.RunNow("d1", dry: true));
        var dry = Run(rig, "d1", 3);
        Assert.True(dry.DryRun);
        rig.Controller.OnTurnFinished(rig.LeadOf(dry), TurnStatus.Failed, false);
        MissionRig.Until(() => !rig.Host.Scheduler.Duties[0].Running, "the dry run to end");
        Assert.Equal(2, rig.Controller.Duties.HealthOf("d1").ConsecutiveFailures);

        // A run the owner stopped is not counted.
        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        var stopped = Run(rig, "d1", 4);
        var lead = rig.LeadOf(stopped);
        Assert.Null(rig.Controller.Runner.Stop(stopped.Id));
        rig.Agents.Finish(lead, null);
        rig.Controller.OnTurnFinished(lead, TurnStatus.Interrupted, false);
        MissionRig.Until(() => rig.Host.Scheduler.Find("d1")!.History[0].Outcome == DutyRunOutcome.Stopped, "the stop");
        Assert.Equal(2, rig.Controller.Duties.HealthOf("d1").ConsecutiveFailures);

        // A run that ends quietly is a success: the count starts again.
        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        rig.Complete(Run(rig, "d1", 5), "RESULT: nothing_changed");
        MissionRig.Until(() => !rig.Host.Scheduler.Duties[0].Running, "the quiet run to end");
        Assert.Equal(0, rig.Controller.Duties.HealthOf("d1").ConsecutiveFailures);
    }

    [Fact]
    public void Turning_the_duty_off_and_on_again_lets_it_go_on_and_the_next_time_runs()
    {
        using var rig = new DutyHostRig();
        var duty = rig.Save(DutyHostRig.Every("d1", 10));
        rig.Host.Start();
        FailThrice(rig, "d1");
        var card = Assert.Single(rig.Controller.Queue.Waiting, c => c.Kind == DecisionKind.Duty);
        Assert.Null(rig.Host.Save(duty with { Enabled = false }).Problem);
        Assert.True(rig.Controller.Duties!.IsPaused("d1")); // off is not on
        Assert.Null(rig.Host.Save(duty with { Enabled = true }).Problem);
        Assert.False(rig.Controller.Duties.IsPaused("d1"));
        Assert.DoesNotContain(rig.Controller.Queue.Waiting, c => c.Id == card.Id); // its card was withdrawn
        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(MissionOrigin.Duty, Run(rig, "d1", 4).Mission.Origin);
    }

    [Fact]
    public void When_nth_starts_again_the_card_of_a_duty_that_is_still_paused_is_raised_again_and_the_duty_does_not_run()
    {
        var book = new DutyHealthBook();
        using (var first = new DutyHostRig(book: book))
        {
            first.Save(DutyHostRig.Every("d1", 10));
            first.Host.Start();
            FailThrice(first, "d1");
            Assert.True(book.Of("d1").Paused);
        }

        using var second = new DutyHostRig(book: book);
        second.Save(DutyHostRig.Every("d1", 10));
        Assert.Empty(second.Controller.Queue.Waiting);
        second.Host.Start();
        var card = Assert.Single(second.Controller.Queue.Waiting, c => c.Kind == DecisionKind.Duty);
        Assert.Equal("Duty d1 was paused after 3 failures in a row", card.Title);
        second.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.DoesNotContain(second.Controller.Runner.Missions, m => m.DutyId == "d1");
    }
}
