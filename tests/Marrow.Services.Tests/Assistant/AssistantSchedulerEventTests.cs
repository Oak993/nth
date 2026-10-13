using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.2 "App event"; ED-NEW-dextra-as10-11): a duty that waits for an app event runs when it happens.
// A short cooldown stops a burst becoming a run each; a mission's own events never start a duty (it would feed itself); a usage
// share fires when it is crossed, not at every reading above it; an event that comes while the last run goes waits for it.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-11")]
public sealed class AssistantSchedulerEventTests
{
    private static Duty OnEvent(string id, AppEventTrigger trigger) =>
        new(id, "Duty " + id, "dextra", trigger, "React.") { Since = SchedulerRig.Start };

    [Fact]
    public void A_chat_finishing_runs_the_duty_that_waits_for_it_with_the_events_words()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", OnEvent("d1", new AppEventTrigger(AppEventKind.ChatFinished)));
        rig.Begin();
        rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.ChatFinished, "Claude finished a turn in “Fix the login”"));
        var fire = Assert.Single(rig.Launcher.Fires);
        Assert.Equal(DutyCause.AppEvent, fire.Cause);
        Assert.Equal("Claude finished a turn in “Fix the login”", fire.Detail);
    }

    [Fact]
    public void Another_kind_of_event_and_a_switched_off_duty_do_nothing()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", OnEvent("d1", new AppEventTrigger(AppEventKind.HeadFailed)), OnEvent("d2", new AppEventTrigger(AppEventKind.ChatFinished)) with { Enabled = false });
        rig.Begin();
        rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.ChatFinished, "x"));
        Assert.Empty(rig.Launcher.Fires);
    }

    [Fact]
    public void A_burst_of_events_is_one_run_per_cooldown()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", OnEvent("d1", new AppEventTrigger(AppEventKind.ChatFinished)));
        rig.Begin();
        for (var i = 0; i < 5; i++) rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.ChatFinished, "chat " + i));
        Assert.Single(rig.Launcher.Fires);
        rig.Scheduler!.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "ok");
        rig.Advance(TimeSpan.FromSeconds(31));
        rig.Scheduler.OnEvent(new DutyEvent(AppEventKind.ChatFinished, "later"));
        Assert.Equal(2, rig.Launcher.Fires.Count);
    }

    [Fact]
    public void A_mission_chats_events_and_a_duty_missions_own_end_never_start_a_duty()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", OnEvent("chat", new AppEventTrigger(AppEventKind.ChatFinished)), OnEvent("end", new AppEventTrigger(AppEventKind.MissionFinished)));
        rig.Begin();
        rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.ChatFinished, "a mission's lead finished") { Mission = Guid.NewGuid() });
        Assert.Empty(rig.Launcher.Fires);
        rig.Scheduler.OnEvent(new DutyEvent(AppEventKind.MissionFinished, "A mission finished") { Mission = Guid.NewGuid() });
        var started = Assert.Single(rig.Launcher.Fires);
        Assert.Equal("end", started.Duty.Id);
        // The mission that run made ends: that is its own event, so it must not run the duty again.
        rig.Scheduler.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "ok");
        rig.Advance(TimeSpan.FromMinutes(5));
        rig.Scheduler.OnEvent(new DutyEvent(AppEventKind.MissionFinished, "own") { Mission = rig.Launcher.LastMission });
        Assert.Single(rig.Launcher.Fires);
    }

    [Fact]
    public void A_usage_share_fires_when_it_is_crossed_and_again_after_it_fell_below()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", OnEvent("d1", new AppEventTrigger(AppEventKind.AccountAbove) { AbovePercent = 80, Account = "Claude B" }));
        rig.Begin();
        void Reading(double percent, string account = "Claude B")
        {
            rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.AccountAbove, $"{account}: weekly at {percent}%") { Account = account, Window = "weekly", Percent = percent });
            rig.Advance(TimeSpan.FromMinutes(5));
            if (rig.Launcher.LastMission is { } mission && rig.Scheduler.Duties[0].Running) rig.Scheduler.MissionEnded(mission, DutyRunOutcome.Done, "ok");
        }
        Reading(70);
        Assert.Empty(rig.Launcher.Fires);
        Reading(85);
        Assert.Single(rig.Launcher.Fires);
        Reading(90); // still above: no second run
        Assert.Single(rig.Launcher.Fires);
        Reading(95, "Codex A"); // another account
        Assert.Single(rig.Launcher.Fires);
        Reading(60); // fell below
        Reading(82);
        Assert.Equal(2, rig.Launcher.Fires.Count);
    }

    [Fact]
    public void A_window_reset_can_be_narrowed_to_an_account_and_a_window()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", OnEvent("d1", new AppEventTrigger(AppEventKind.WindowReset) { Account = "Codex A", Window = "weekly" }));
        rig.Begin();
        rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.WindowReset, "x") { Account = "Claude B", Window = "weekly" });
        rig.Scheduler.OnEvent(new DutyEvent(AppEventKind.WindowReset, "x") { Account = "Codex A", Window = "5-hour" });
        Assert.Empty(rig.Launcher.Fires);
        rig.Scheduler.OnEvent(new DutyEvent(AppEventKind.WindowReset, "Codex A: weekly reset") { Account = "Codex A", Window = "weekly" });
        Assert.Single(rig.Launcher.Fires);
    }

    [Fact]
    public void An_event_that_comes_while_the_last_run_goes_waits_for_it_and_runs_once()
    {
        using var rig = new SchedulerRig { AutoEnd = false };
        rig.Store.Put("dextra", OnEvent("d1", new AppEventTrigger(AppEventKind.HeadFailed)));
        rig.Begin();
        rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.HeadFailed, "Subtitles failed"));
        var first = rig.Launcher.LastMission!.Value;
        rig.Advance(TimeSpan.FromMinutes(2));
        rig.Scheduler.OnEvent(new DutyEvent(AppEventKind.HeadFailed, "Thumbnail failed"));
        rig.Advance(TimeSpan.FromMinutes(2));
        rig.Scheduler.OnEvent(new DutyEvent(AppEventKind.HeadFailed, "Voice failed"));
        Assert.Single(rig.Launcher.Fires);
        Assert.True(rig.Scheduler.Duties[0].Held);
        rig.Scheduler.MissionEnded(first, DutyRunOutcome.Done, "ok");
        var second = Assert.Single(rig.Launcher.Fires.Skip(1));
        Assert.Equal(DutyCause.AppEvent, second.Cause);
        Assert.Contains("Thumbnail failed", second.Detail, StringComparison.Ordinal);
        Assert.Contains("Voice failed", second.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Events_wait_under_stop_all_and_run_once_after_resume_all()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", OnEvent("d1", new AppEventTrigger(AppEventKind.ChatFinished)));
        rig.Begin();
        rig.MayFire = false;
        rig.Scheduler!.OnEvent(new DutyEvent(AppEventKind.ChatFinished, "x"));
        Assert.Empty(rig.Launcher.Fires);
        rig.MayFire = true;
        rig.Scheduler.Reevaluate();
        Assert.Equal(DutyCause.CatchUp, Assert.Single(rig.Launcher.Fires).Cause);
    }
}
