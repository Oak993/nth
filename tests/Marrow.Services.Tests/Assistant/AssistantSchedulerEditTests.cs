using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 15.3; ED-NEW-dextra-as10-5, -11): adding, editing, removing and switching duties. What cannot
// be saved says why. A trigger edit starts a fresh schedule. What a file holds that this build does not know survives a save; a
// file from a newer nth, or one that does not parse, is never written over. The first duty is announced as the first.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-5")]
public sealed class AssistantSchedulerEditTests
{
    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void A_new_duty_is_saved_armed_and_announced_and_only_the_first_is_the_first()
    {
        using var rig = new SchedulerRig();
        rig.Begin();
        var (first, problem) = rig.Scheduler!.Add(SchedulerRig.Daily("", 8) with { Id = "", Name = "Morning digest" });
        Assert.Null(problem);
        Assert.Matches(Duty.IdShape, first!.Id);
        Assert.Equal(rig.Now, first.Since);
        Assert.Single(rig.Store.Saved("dextra").Duties);
        rig.Scheduler.Add(SchedulerRig.Daily("d2", 9));
        Assert.Equal([true, false], rig.Added.Select(a => a.First));
        rig.Advance(TimeSpan.FromHours(3));
        Assert.Equal(2, rig.Launcher.Fires.Count);
    }

    [Fact]
    public void A_duty_that_cannot_run_is_refused_with_the_reason_and_nothing_is_saved()
    {
        using var rig = new SchedulerRig();
        rig.Begin();
        var tooOften = rig.Scheduler!.Add(SchedulerRig.Every("d1", 2));
        Assert.Null(tooOften.Duty);
        Assert.Contains("5 or more", tooOften.Problem, StringComparison.Ordinal);
        rig.Scheduler.Add(SchedulerRig.Daily("d2", 8));
        var twice = rig.Scheduler.Add(SchedulerRig.Daily("d2", 9));
        Assert.Equal("A duty with that id exists already.", twice.Problem);
        var wake = rig.Scheduler.Add(SchedulerRig.Daily("d3", 8) with { NeedsDesktop = true, WakePc = true });
        Assert.Contains("needs the desktop", wake.Problem, StringComparison.Ordinal);
        Assert.Single(rig.Store.Saved("dextra").Duties);
        Assert.Single(rig.Added);
    }

    [Fact]
    public void Editing_the_trigger_starts_a_fresh_schedule_and_keeps_the_history()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(1)); // runs at 07:00
        var before = rig.Scheduler!.Find("d1")!;
        rig.Scheduler.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "ok");
        var (edited, problem) = rig.Scheduler.Update(before with { Trigger = new TimeTrigger(TimeSchedule.Daily(new TimeOnly(9, 30)), "UTC"), Name = "Renamed" });
        Assert.Null(problem);
        Assert.Equal(rig.Now, edited!.Since);
        Assert.Null(edited.Handled);
        Assert.Equal("Renamed", edited.Name);
        Assert.Single(edited.History);
        rig.Advance(TimeSpan.FromMinutes(150)); // 09:30
        Assert.Equal(2, rig.Launcher.Fires.Count);
        Assert.Equal(At("2026-10-13T09:30:00+00:00"), rig.Scheduler.Find("d1")!.LastRun);
    }

    [Fact]
    public void Editing_something_other_than_the_trigger_keeps_the_schedule_and_cannot_change_what_it_must_not()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        var before = rig.Scheduler!.Find("d1")!;
        var (edited, _) = rig.Scheduler.Update(before with { BudgetUsd = 3m, AssistantId = "someone-else", Since = At("2000-01-01T00:00:00+00:00"), CreatedBy = "forged" });
        Assert.Equal(3m, edited!.BudgetUsd);
        Assert.Equal("dextra", edited.AssistantId);
        Assert.Equal(before.Since, edited.Since);
        Assert.Equal("owner", edited.CreatedBy);
        Assert.Equal("There is no such duty.", rig.Scheduler.Update(before with { Id = "ghost" }).Problem);
    }

    [Fact]
    public void Removing_a_duty_forgets_it_and_what_its_watcher_saw()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7), SchedulerRig.Daily("d2", 8));
        rig.Begin();
        Assert.Null(rig.Scheduler!.Remove("d1"));
        Assert.Equal(["d2"], rig.Store.Saved("dextra").Duties.Select(d => d.Id));
        Assert.Contains("d1", rig.Store.Deleted);
        Assert.Equal("There is no such duty.", rig.Scheduler.Remove("d1"));
        rig.Advance(TimeSpan.FromHours(3));
        Assert.Equal("d2", Assert.Single(rig.Launcher.Fires).Duty.Id);
    }

    [Fact]
    public void Fields_this_build_does_not_know_survive_a_save()
    {
        using var rig = new SchedulerRig();
        var node = new JsonObject
        {
            ["version"] = 1,
            ["futureSetting"] = new JsonObject { ["x"] = 1 },
            ["duties"] = new JsonArray(DutyJson.ToJson(SchedulerRig.Daily("d1", 7)).WithField("futureField", "kept")),
        };
        rig.Store.PutRaw("dextra", node);
        rig.Begin();
        Assert.Null(rig.Scheduler!.SetEnabled("d1", false));
        var saved = (JsonObject)rig.Store.Raw("dextra")!;
        Assert.Equal(1, saved["futureSetting"]!["x"]!.GetValue<int>());
        Assert.Equal("kept", saved["duties"]![0]!["futureField"]!.GetValue<string>());
        Assert.False(saved["duties"]![0]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void An_entry_that_cannot_be_read_is_reported_and_written_back_as_it_was()
    {
        using var rig = new SchedulerRig();
        var broken = new JsonObject { ["id"] = "broken", ["name"] = "No trigger at all" };
        rig.Store.PutRaw("dextra", new JsonObject { ["version"] = 1, ["duties"] = new JsonArray(DutyJson.ToJson(SchedulerRig.Daily("d1", 7)), broken) });
        rig.Begin();
        Assert.Contains(rig.Scheduler!.Problems, p => p.Contains("could not be read", StringComparison.Ordinal));
        Assert.Null(rig.Scheduler.SetEnabled("d1", false));
        var saved = (JsonArray)rig.Store.Raw("dextra")!["duties"]!;
        Assert.Equal(2, saved.Count);
        Assert.Equal("broken", saved[1]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void A_file_from_a_newer_nth_is_shown_but_never_run_or_written()
    {
        using var rig = new SchedulerRig();
        var node = new JsonObject { ["version"] = 2, ["duties"] = new JsonArray(DutyJson.ToJson(SchedulerRig.Every("d1", 10))) };
        rig.Store.PutRaw("dextra", node);
        var before = node.ToJsonString();
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(2));
        Assert.Empty(rig.Launcher.Fires);
        Assert.Single(rig.Scheduler!.Duties);
        Assert.True(rig.Scheduler.Duties[0].ReadOnly);
        Assert.Contains(rig.Scheduler.Problems, p => p.Contains("newer nth", StringComparison.Ordinal));
        Assert.Contains("newer nth", rig.Scheduler.Add(SchedulerRig.Daily("d2", 8)).Problem, StringComparison.Ordinal);
        Assert.Contains("newer nth", rig.Scheduler.SetEnabled("d1", false), StringComparison.Ordinal);
        Assert.Contains("newer nth", rig.Scheduler.Remove("d1"), StringComparison.Ordinal);
        Assert.Equal(0, rig.Store.Saves);
        Assert.Equal(before, rig.Store.Raw("dextra")!.ToJsonString());
    }

    [Fact]
    public void A_file_that_does_not_parse_is_never_replaced_by_an_empty_one()
    {
        using var rig = new SchedulerRig();
        rig.Store.BreakFile("dextra");
        rig.Begin();
        Assert.Empty(rig.Scheduler!.Duties);
        Assert.NotNull(rig.Scheduler.Add(SchedulerRig.Daily("d1", 8)).Problem);
        Assert.Equal(0, rig.Store.Saves);
        Assert.Contains(rig.Scheduler.Problems, p => p.Contains("could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_assistants_duties_live_in_their_own_file()
    {
        using var rig = new SchedulerRig();
        rig.Assistants = ["dextra", "astur-a"];
        rig.Begin();
        rig.Scheduler!.Add(SchedulerRig.Daily("d1", 8));
        rig.Scheduler.Add(SchedulerRig.Daily("d2", 9) with { AssistantId = "astur-a" });
        Assert.Equal(["d1"], rig.Store.Saved("dextra").Duties.Select(d => d.Id));
        Assert.Equal(["d2"], rig.Store.Saved("astur-a").Duties.Select(d => d.Id));
    }

    [Fact]
    public void A_disk_that_will_not_take_the_save_is_reported_and_the_scheduler_goes_on()
    {
        using var rig = new SchedulerRig();
        rig.Begin();
        rig.Store.SaveFails = new IOException("The disk is full.");
        var (duty, problem) = rig.Scheduler!.Add(SchedulerRig.Every("d1", 10));
        Assert.Null(problem);
        Assert.NotNull(duty);
        Assert.Contains(rig.Faults, f => f.Contains("could not be saved", StringComparison.Ordinal));
        rig.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(rig.Launcher.Fires);
    }

    [Fact]
    public void A_duty_survives_a_restart_with_its_history_and_its_schedule()
    {
        using var rig = new SchedulerRig();
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 8));
        rig.Begin();
        rig.Advance(TimeSpan.FromHours(2));
        rig.Scheduler!.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "ok");
        rig.Scheduler.Dispose();
        rig.Begin(); // the same store, a new scheduler
        var duty = rig.Scheduler!.Find("d1")!;
        Assert.Single(duty.History);
        Assert.Equal(DutyRunOutcome.Done, duty.History[0].Outcome);
        Assert.Single(rig.Launcher.Fires); // nothing was missed, so nothing ran again
        rig.Advance(TimeSpan.FromHours(24));
        Assert.Equal(2, rig.Launcher.Fires.Count);
    }
}
