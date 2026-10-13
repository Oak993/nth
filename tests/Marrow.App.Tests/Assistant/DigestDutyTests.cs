using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using static Marrow.App.Tests.Fixtures.DigestRigs;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 6 "Composed at a set time, 08:00 by default and changeable. It can also be asked for", 7.1, 7.4, canvas 5; ED-NEW-dextra-as11-8): the
// Morning digest is a standing duty of Dextra's own, every day at 08:00, reporting to the digest only. The scheduler fires it with everything a duty has: it is caught up
// once if nth was closed, held under Stop all, changeable like any duty. It starts no mission, and it is made once: a duty the owner deletes is not made again.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-8")]
public sealed class DigestDutyTests
{
    private static DutyHostRig Rig(DigestSetup? setup = null)
    {
        var rig = new DutyHostRig(digest: setup ?? Setup());
        rig.Host.Start();
        return rig;
    }

    [Fact]
    public void Starting_the_host_makes_the_Morning_digest_duty_every_day_at_eight_reporting_to_the_digest_only()
    {
        using var rig = Rig();
        var duty = Assert.Single(rig.Host.Scheduler.Duties).Duty;
        Assert.Equal(DigestDuty.Id, duty.Id);
        Assert.Equal("Morning digest", duty.Name);
        Assert.Equal("dextra", duty.AssistantId);
        Assert.Equal(new TimeTrigger(TimeSchedule.Daily(new TimeOnly(8, 0)), "UTC"), duty.Trigger);
        Assert.Equal(ReportStyle.DigestOnly, duty.Report);
        Assert.Equal(MissedRunPolicy.CatchUpOnce, duty.Missed);
        Assert.Equal("nth", duty.CreatedBy);
        Assert.False(duty.WakePc);
        Assert.False(duty.NeedsDesktop);
        Assert.Equal(DigestDuty.Instructions, duty.Instructions);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero), rig.Host.Scheduler.Duties[0].NextRun);
        Assert.True(rig.Host.Digests!.Book.DutyMade);
    }

    [Fact]
    public void It_is_made_once_and_a_duty_the_owner_deletes_is_not_made_again()
    {
        using var rig = Rig();
        Assert.Null(rig.Host.Digests!.EnsureDuty());
        Assert.Single(rig.Host.Scheduler.Duties);
        Assert.Null(rig.Host.Scheduler.Remove(DigestDuty.Id));
        Assert.Null(rig.Host.Digests.EnsureDuty());
        Assert.Empty(rig.Host.Scheduler.Duties);
    }

    [Fact]
    public void A_host_made_without_a_digest_makes_no_duty_and_has_none()
    {
        using var rig = new DutyHostRig();
        rig.Host.Start();
        Assert.Null(rig.Host.Digests);
        Assert.Empty(rig.Host.Scheduler.Duties);
    }

    [Fact]
    public void At_eight_the_scheduler_composes_the_morning_digest_and_the_run_ends_inside_its_launch()
    {
        using var rig = Rig();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        var digest = Latest(rig);
        Assert.Equal(DigestKind.Scheduled, digest.Kind);
        Assert.Equal("Morning digest · 08:00", digest.Header);
        MissionRig.Until(() => rig.Host.Scheduler.Find(DigestDuty.Id)!.History.Count > 0 && rig.Host.Scheduler.Find(DigestDuty.Id)!.History[0].Outcome == DutyRunOutcome.Done, "the run to end");
        var view = Assert.Single(rig.Host.Scheduler.Duties);
        Assert.False(view.Running);
        Assert.Equal(DutyCause.Time, view.Duty.History[0].Cause);
        Assert.Empty(rig.Controller.Runner.Missions);                      // no mission, no agent, no spend
    }

    [Fact]
    public void It_runs_again_the_next_morning()
    {
        using var rig = Rig();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        MissionRig.Until(() => rig.Host.Digests!.Book.Digests.Count == 1, "the first digest");
        rig.Clock.Advance(TimeSpan.FromDays(1));
        MissionRig.Until(() => rig.Host.Digests!.Book.Digests.Count == 2, "the second digest");
        Assert.Equal(DutyRunOutcome.Done, rig.Host.Scheduler.Find(DigestDuty.Id)!.History[0].Outcome);
    }

    [Fact]
    public void Under_Stop_all_it_is_held_and_after_Resume_all_it_is_caught_up_once()
    {
        using var rig = Rig();
        rig.Controller.Hold.HoldForStop();
        rig.Clock.Advance(TimeSpan.FromHours(5));
        Assert.Null(rig.Host.Digests!.Latest);
        Assert.True(rig.Host.Scheduler.Duties[0].Held);
        rig.Controller.Hold.ReleaseAll();
        var digest = Latest(rig);
        Assert.Equal("Morning digest · 11:00", digest.Header);
        Assert.Equal(DutyCause.CatchUp, rig.Host.Scheduler.Find(DigestDuty.Id)!.History[0].Cause);
    }

    [Fact]
    public void Run_now_is_the_owner_asking_for_a_digest_and_it_does_not_say_morning()
    {
        using var rig = Rig();
        Assert.Null(rig.Host.Scheduler.RunNow(DigestDuty.Id));
        var digest = Latest(rig);
        Assert.Equal(DigestKind.Asked, digest.Kind);
        Assert.Equal("Digest · 06:00", digest.Header);
    }

    [Fact]
    public void A_dry_run_composes_a_preview_for_the_duty_card_and_keeps_and_pins_nothing()
    {
        using var rig = Rig();
        Assert.Null(rig.Host.Scheduler.RunNow(DigestDuty.Id, dry: true));
        MissionRig.Until(() => rig.Host.Digests!.LastPreview is not null, "the preview");
        Assert.Null(rig.Host.Digests!.Latest);
        MissionRig.Until(() => rig.Host.Scheduler.Find(DigestDuty.Id)!.History.Count > 0 && rig.Host.Scheduler.Find(DigestDuty.Id)!.History[0].Outcome == DutyRunOutcome.Done, "the dry run to end");
        var duty = rig.Host.Scheduler.Find(DigestDuty.Id)!;
        Assert.StartsWith("Dry run", duty.History[0].Note, StringComparison.Ordinal);
        Assert.Null(duty.LastRun);                                         // a dry run is not a run of the schedule
        Assert.Empty(rig.Host.Digests.Book.Digests);
    }

    [Fact]
    public void A_digest_that_could_not_be_composed_is_a_failed_run_and_the_next_morning_tries_again()
    {
        var fail = true;
        using var rig = Rig(Setup(habits: () => fail ? throw new InvalidOperationException("the chats could not be read") : []));
        rig.Clock.Advance(TimeSpan.FromHours(2));
        MissionRig.Until(() => rig.Host.Scheduler.Find(DigestDuty.Id)!.History.Count > 0 && rig.Host.Scheduler.Find(DigestDuty.Id)!.History[0].Outcome == DutyRunOutcome.Failed, "the run to fail");
        Assert.Null(rig.Host.Digests!.Latest);
        Assert.False(rig.Host.Scheduler.Duties[0].Running);
        fail = false;
        rig.Clock.Advance(TimeSpan.FromDays(1));
        MissionRig.Until(() => rig.Host.Digests.Latest is not null, "the next morning's digest");
    }

    [Fact]
    public void The_owner_can_move_it_to_another_time_like_any_duty()
    {
        using var rig = Rig();
        var duty = rig.Host.Scheduler.Find(DigestDuty.Id)!;
        var moved = rig.Host.Save(duty with { Trigger = new TimeTrigger(TimeSchedule.Daily(new TimeOnly(7, 30)), "UTC") });
        Assert.Null(moved.Problem);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 7, 30, 0, TimeSpan.Zero), rig.Host.Scheduler.Duties[0].NextRun);
        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal("Morning digest · 07:30", Latest(rig).Header);
    }

    [Fact]
    public async Task Switching_it_off_stops_the_digest_and_the_owner_can_still_ask_for_one()
    {
        using var rig = Rig();
        Assert.Null(rig.Host.Scheduler.SetEnabled(DigestDuty.Id, false));
        rig.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Null(rig.Host.Digests!.Latest);
        Assert.NotNull(await rig.Host.Digests.Ask());
    }

    [Fact]
    public void The_digest_duty_is_not_the_owners_first_duty_it_brings_no_card_and_the_first_he_makes_still_does()
    {
        using var rig = Rig();
        Assert.False(rig.Host.FirstCard.Visible);
        var duty = DutyHostRig.Daily("d1", 9);
        Assert.Null(rig.Host.Save(duty).Problem);
        Assert.True(rig.Host.FirstCard.Visible);
    }

    [Fact]
    public void Another_duty_still_starts_a_mission_through_the_launcher_behind_the_digest()
    {
        using var rig = Rig();
        var duty = rig.Save(DutyHostRig.Daily("d1", 7, "Compare the views."));
        rig.Clock.Advance(TimeSpan.FromHours(2));
        var mission = rig.MissionOf(duty);
        Assert.Equal(MissionOrigin.Duty, mission.Mission.Origin);
        Assert.Contains("Compare the views.", mission.Brief, StringComparison.Ordinal);
    }

    [Fact]
    public void The_scheduler_never_hands_the_digest_to_the_failure_breaker()
    {
        using var rig = Rig(Setup(habits: () => throw new InvalidOperationException("no")));
        for (var day = 1; day <= 4; day++)
        {
            rig.Clock.Advance(TimeSpan.FromDays(1));
            var expected = day;
            MissionRig.Until(() => rig.Host.Scheduler.Find(DigestDuty.Id)!.History.Count(h => h.Outcome == DutyRunOutcome.Failed) >= expected, "a failed morning");
        }
        Assert.False(rig.Controller.Duties!.IsPaused(DigestDuty.Id));
    }

    [Fact]
    public void A_title_the_brain_gives_a_duty_that_matches_the_Morning_digest_changes_it_on_a_card_and_makes_no_second_one()
    {
        using var rig = Rig();
        var tools = rig.Host.Tools;
        var chat = new AssistantChat(Guid.NewGuid(), "dextra", null, AssistantChatRole.Home, Marrow.Core.Models.ProviderKind.Claude, "model-a");
        var call = new AssistantToolCall(chat, AssistantToolCatalog.Find("schedule_duty")!,
            Marrow.Core.Support.JSONValue.Parse("""{"title":"Morning digest","instructions":"Compose the morning digest from the feed, missions, usage and duties.","schedule":"weekdays 07:00"}""")!,
            new Classification(ActionClass.WriteElsewhere, "changes the assistant's standing configuration"),
            new PolicyDecision(Verdict.Ask, PolicyRule.AlwaysConfirmed, "Asks: creating a duty always asks"), AssistantRoute.Mcp);
        var inputs = tools.Describe(call)!.ToDictionary(i => i.Name, i => i.Value);
        Assert.Contains("(was:", inputs["Trigger"], StringComparison.Ordinal);
        Assert.Single(rig.Host.Scheduler.Duties);
    }
}
