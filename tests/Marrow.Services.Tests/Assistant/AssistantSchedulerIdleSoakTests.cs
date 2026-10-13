using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4, 15.1, 16 row AS-10; ED-NEW-dextra-as10-17): the idle soak gate, simulated and bounded. A scheduler
// holding thirty duties of every kind sits through eight hours with nothing due: it keeps one timer (never more), launches nothing,
// writes nothing, adds no feed line and no history, and its answer about what is next does not change. The real eight hours, with the
// process's memory and CPU measured, is IdleSoakNativeTests.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-17")]
public sealed class AssistantSchedulerIdleSoakTests
{
    private const int Hours = 8;

    private static IEnumerable<Duty> Thirty()
    {
        for (var i = 0; i < 10; i++) yield return SchedulerRig.Daily("late-" + i, 20 + i % 3, i * 5); // 20:00 to 22:45, after the soak
        for (var i = 0; i < 5; i++)
            yield return new Duty("event-" + i, "Event " + i, "dextra", new AppEventTrigger(AppEventKind.WindowReset), "React.");
        for (var i = 0; i < 5; i++) yield return new Duty("manual-" + i, "Manual " + i, "dextra", new ManualTrigger(), "Only when asked.");
        for (var i = 0; i < 5; i++) yield return SchedulerRig.Daily("off-" + i, 7, 0) with { Enabled = false };
        for (var i = 0; i < 5; i++) yield return SchedulerRig.Every("far-" + i, 24 * 60); // once a day, from the soak's start: next at 06:00 tomorrow
    }

    [Fact]
    public void Thirty_duties_sit_through_eight_idle_hours_on_one_timer_launching_and_writing_nothing()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", Thirty().ToArray());
        rig.Begin();
        var scheduler = rig.Scheduler!;
        var savesAtStart = rig.Store.Saves;
        var nextAtStart = scheduler.Duties.Select(v => (v.Duty.Id, v.NextRun)).ToList();
        Assert.Equal(30, nextAtStart.Count);

        for (var minute = 1; minute <= Hours * 60; minute++)
        {
            rig.Advance(TimeSpan.FromMinutes(1));
            Assert.True(rig.Clock.ActiveTimers <= 1, $"minute {minute}: {rig.Clock.ActiveTimers} timers");
        }

        Assert.Equal(DateTimeOffset.Parse("2026-10-13T14:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture), rig.Now);
        Assert.Empty(rig.Launcher.Fires);
        Assert.Equal(savesAtStart, rig.Store.Saves);
        Assert.Empty(rig.Feed);
        Assert.Empty(rig.Faults);
        Assert.All(scheduler.Duties, view => Assert.Empty(view.Duty.History));
        Assert.Equal(nextAtStart, scheduler.Duties.Select(v => (v.Duty.Id, v.NextRun)).ToList()); // nothing moved: nothing was due
    }

    [Fact]
    public void An_empty_scheduler_holds_no_timer_at_all_and_a_scheduler_with_only_events_and_manual_duties_holds_none_either()
    {
        using var rig = new SchedulerRig();
        rig.Begin();
        Assert.Equal(0, rig.Clock.ActiveTimers);
        rig.Advance(TimeSpan.FromHours(Hours));
        Assert.Equal(0, rig.Clock.ActiveTimers);

        using var quiet = new SchedulerRig();
        quiet.Store.Put("dextra", new Duty("m", "Manual", "dextra", new ManualTrigger(), "x"), new Duty("e", "Event", "dextra", new AppEventTrigger(AppEventKind.WindowReset), "y"));
        quiet.Begin();
        Assert.Equal(0, quiet.Clock.ActiveTimers);
        quiet.Advance(TimeSpan.FromHours(Hours));
        Assert.Equal(0, quiet.Clock.ActiveTimers);
        Assert.Empty(quiet.Launcher.Fires);
    }
}
