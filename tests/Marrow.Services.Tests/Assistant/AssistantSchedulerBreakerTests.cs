using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 13.2 "Failed runs never retry endlessly", 7.1; ED-NEW-dextra-as10-19): the scheduler asks the failure breaker
// (AS-15) before it fires a duty. A duty the breaker paused is not started from a time, an event or a wake: the time is skipped with the reason (one
// line that counts, not a line each), the list says it is paused, and only the owner's Run now starts it. A breaker that cannot answer is read as
// paused. Lifting the pause lets the next time run.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-19")]
public sealed class AssistantSchedulerBreakerTests
{
    private static SchedulerRig Rig(Func<string, bool> paused, params Duty[] duties)
    {
        var rig = new SchedulerRig();
        rig.Store.Put("dextra", duties);
        rig.Begin(options => new AssistantSchedulerOptions
        {
            Store = options.Store,
            Launcher = options.Launcher,
            AssistantIds = options.AssistantIds,
            Time = options.Time,
            MayFire = options.MayFire,
            DesktopLocked = options.DesktopLocked,
            Feed = options.Feed,
            Faulted = options.Faulted,
            BreakerPaused = paused,
        });
        return rig;
    }

    [Fact]
    public void A_paused_duty_is_skipped_at_its_time_once_with_a_count_and_says_why_in_the_list()
    {
        var paused = true;
        using var rig = Rig(_ => paused, SchedulerRig.Every("d1", 10));
        rig.Advance(TimeSpan.FromMinutes(35));
        Assert.Empty(rig.Launcher.Fires);
        var line = Assert.Single(rig.Scheduler!.Find("d1")!.History);
        Assert.Equal((DutyRunOutcome.Skipped, AssistantScheduler.BreakerWhy, 3), (line.Outcome, line.Note, line.Count));
        Assert.Equal(AssistantScheduler.BreakerWhy, rig.Scheduler.Duties[0].Status);
        Assert.Single(rig.Feed, f => f.Kind == DutyFeedKind.Skipped); // told once

        paused = false; // the owner let it go on
        rig.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("d1", Assert.Single(rig.Launcher.Fires).Duty.Id);
        Assert.Null(rig.Scheduler.Duties[0].Status);
    }

    [Fact]
    public void A_paused_duty_is_not_started_by_an_event_or_a_wake_and_Run_now_still_starts_it()
    {
        using var rig = Rig(_ => true, SchedulerRig.Every("d1", 10), new Duty("e1", "On reset", "dextra", new AppEventTrigger(AppEventKind.WindowReset), "React."));
        rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.WindowReset, "Codex A: weekly reset"));
        Assert.Empty(rig.Launcher.Fires);
        Assert.Equal(AssistantScheduler.BreakerWhy, rig.Scheduler.WakeRun("d1"));
        Assert.Empty(rig.Launcher.Fires);
        Assert.Null(rig.Scheduler.RunNow("d1"));
        var fire = Assert.Single(rig.Launcher.Fires);
        Assert.Equal((DutyCause.Manual, "d1"), (fire.Cause, fire.Duty.Id));
    }

    [Fact]
    public void A_breaker_that_cannot_answer_holds_the_duty_and_one_duty_paused_does_not_hold_another()
    {
        using var broken = Rig(_ => throw new InvalidOperationException("the book is unreadable"), SchedulerRig.Every("d1", 10));
        broken.Advance(TimeSpan.FromMinutes(10));
        Assert.Empty(broken.Launcher.Fires);
        Assert.Equal(DutyRunOutcome.Skipped, broken.Scheduler!.Find("d1")!.History[0].Outcome);

        using var one = Rig(id => id == "d1", SchedulerRig.Every("d1", 10), SchedulerRig.Every("d2", 10));
        one.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("d2", Assert.Single(one.Launcher.Fires).Duty.Id);
    }
}
