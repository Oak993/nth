using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6 "Afterwards", 12.6b "Global controls", r4 B6, B11, B12; ED-NEW-dextra-as6-4): Pause all, Resume
// all and Stop all. Pause all is a latch (a duty that fires while it is on starts paused); Resume all picks up every paused task
// and every task Stop all stopped, re-raises held decisions, never answers an irreversible card, skips reverted tasks, and leaves
// a task or head the owner stopped by hand stopped and names it; the toggle flips to Resume all without waiting for any turn.
[Trait("Decision", "ED-NEW-dextra-as6-4")]
[Trait("Feature", "AS-6")]
public sealed class MissionControlsAllTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private ControlsRig Rig() => new(temp.Folder);

    private static MissionRequest Duty(ControlsRig rig, string title) => rig.Rig.Request(title) with { Origin = MissionOrigin.Duty };

    /// <summary>A mission the owner stopped, settled.</summary>
    private static MissionRecord StoppedByHand(ControlsRig rig, string title)
    {
        var record = rig.Started(rig.Rig.Request(title));
        Assert.Null(rig.Controls.Stop(record.Id));
        rig.EndTurn(record.LeadThread!.Value, null, TurnStatus.Interrupted);
        rig.EndProcesses();
        rig.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
        Assert.Equal(StopCause.Owner, rig.Now(record).Mission.StoppedBy);
        return record;
    }

    [Fact]
    public void Pause_all_pauses_every_task_that_can_pause_and_is_a_latch_for_duties_that_fire_meanwhile()
    {
        var rig = Rig();
        var running = rig.Started(rig.Rig.Request("Running one"));
        var needs = rig.Started(rig.Rig.Request("Needs one"));
        rig.Runner.OnApproval(needs.LeadThread!.Value, ControlsRig.Approval("a1", "npm test"));
        Assert.Equal(GlobalToggleKind.PauseAll, rig.Controls.Toggle.Kind);

        var result = rig.Controls.PauseAll("Pause all button");

        Assert.Equal(2, result.Tasks);
        Assert.Equal("Pause all (Pause all button): 2 tasks pause at their next turn boundary; nothing is cut; new duties start paused", result.Line);
        Assert.Equal(MissionState.Pausing, rig.Now(running).Mission.State); // its turn runs: nothing is cut
        Assert.Equal(PauseCause.PauseAll, rig.Now(running).Mission.PausedBy);
        Assert.Equal(0, rig.Agents.Of(running.LeadThread!.Value).Interrupts);
        Assert.Equal(MissionState.Paused, rig.Now(needs).Mission.State); // needs you runs no turn
        // The toggle is Resume all at once, before any turn has ended, so it never looks dead.
        var toggle = rig.Controls.Toggle;
        Assert.Equal((GlobalToggleKind.ResumeAll, false, true), (toggle.Kind, toggle.CanPauseAll, toggle.CanResumeAll));
        Assert.True(rig.Hold.PauseLatched);
        Assert.False(rig.Hold.StopHeld);
        Assert.True(rig.Hold.DutiesMayFire);

        // A duty fires while it is on: it starts paused, from the brief, and never launches its lead.
        var duty = rig.Runner.Start(Duty(rig, "Price watch"));
        Assert.True(duty.Launched.IsCompleted);
        var held = rig.Now(duty.Record);
        Assert.Equal((MissionState.Paused, PauseCause.PauseAll, MissionState.Planning), (held.Mission.State, held.Mission.PausedBy, held.Mission.ResumeTo));
        Assert.Null(held.LeadThread);
        Assert.Contains(rig.Rig.Audit, e => e.Tool == "pause" && e.Result == "starts paused: Pause all is on");
        // What the owner starts himself still runs.
        var own = rig.Runner.Start(rig.Rig.Request("Mine"));
        Assert.True(MissionRig.Within(own.Launched, 10));
        Assert.Equal(MissionState.Working, rig.Now(own.Record).Mission.State);
        Assert.Equal(GlobalToggleKind.PauseAll, rig.Controls.Toggle.Kind); // a task that can pause is running again
    }

    [Fact]
    public void Resume_all_picks_up_the_paused_tasks_and_names_what_it_leaves_alone()
    {
        var rig = Rig();
        // Paused by the owner, its turn ended.
        var paused = rig.Started(rig.Rig.Request("Weekly stats"));
        Assert.Null(rig.Controls.Pause(paused.Id));
        rig.EndTurn(paused.LeadThread!.Value, "Paused here.");
        // A send card waits and the task is paused: Resume all never answers it.
        var send = rig.Started(rig.Rig.Request("Newsletter"));
        rig.Runner.OnApproval(send.LeadThread!.Value, ControlsRig.Approval("a1", "git push origin main"));
        Assert.Null(rig.Controls.Pause(send.Id));
        var card = rig.Queue.Of(send.Id).Single();
        Assert.Equal(DecisionHold.Pause, card.HeldBy);
        // Stopped by hand: it stays stopped and is named.
        var hand = StoppedByHand(rig, "Price watch");
        // A team with a head the owner stopped on its own, then paused: the head stays stopped and is named.
        var team = rig.Started(rig.Rig.Request("Team job", team: true));
        var teamLead = team.LeadThread!.Value;
        var reader = rig.AddHead(team, "Reader");
        var writer = rig.AddHead(team, "Writer");
        rig.StartHead(reader);
        rig.StartHead(writer);
        Assert.Null(rig.Controls.StopHead(team.Id, writer));
        rig.Agents.Finish(writer, null);
        rig.Runner.HeadFinished(writer, TurnStatus.Interrupted);
        rig.Runner.CheckStopping();
        Assert.Null(rig.Controls.Pause(team.Id));
        rig.Agents.Finish(reader, "Reader piece.");
        Assert.True(rig.Runner.HeadFinished(reader, TurnStatus.Completed));
        rig.EndTurn(teamLead, "Waiting for the heads.");
        Assert.Equal(MissionState.Paused, rig.Now(team).Mission.State);

        var report = rig.Controls.ResumeAll();

        Assert.Equal(3, report.Result.PickedUp);
        Assert.Equal(["send"], report.Result.BackToNeedsYou);
        Assert.Equal(["Price watch"], report.Result.StoppedByHand);
        Assert.Equal([$"Writer in “Team job”"], report.Result.HeadsStoppedByHand);
        Assert.Equal(
            "Resumed all: 3 tasks picked up where they left off. 1 went back to Needs you: a send card never resumes on its own. "
            + "“Price watch” stays stopped, because you stopped it. Writer in “Team job” stays stopped, because you stopped it on your own.",
            report.Line);
        Assert.NotEqual(MissionState.Paused, rig.Now(paused).Mission.State);
        // The send card is back for a new tap; Resume all did not answer it.
        Assert.Equal(MissionState.NeedsYou, rig.Now(send).Mission.State);
        Assert.Equal(DecisionState.Open, rig.Queue.Find(card.Id)!.State);
        Assert.Empty(rig.Agents.Resolutions);
        // The stopped-by-hand task and head are untouched.
        Assert.Equal(MissionState.Stopped, rig.Now(hand).Mission.State);
        var back = rig.Now(team).Mission;
        Assert.Equal(HeadState.Stopped, back.Heads.Single(h => h.Name == "Writer").State);
        Assert.Equal(HeadState.Working, back.Heads.Single(h => h.Name == "Reader").State);
        Assert.False(rig.Hold.PauseLatched);
    }

    [Fact]
    public void Resume_all_after_Stop_all_picks_up_what_Stop_all_stopped_and_skips_reverted_reserve_and_hand_stopped_tasks()
    {
        var rig = Rig();
        var one = rig.Started(rig.Rig.Request("Channel A stats"));
        var two = rig.Started(rig.Rig.Request("Channel B stats"));
        var hand = StoppedByHand(rig, "Price watch");
        // Reverted: Journaled-style record.
        var revertedId = Guid.NewGuid();
        var reverted = new MissionRecord(new Mission(revertedId, "Old job", "dextra", MissionOrigin.Owner, MissionState.Stopped)
        {
            ResumeTo = MissionState.Working,
            StoppedBy = StopCause.StopAll,
            Reverted = true,
        })
        { Brief = "b", ProjectId = MissionRig.Project, ProjectPath = temp.Folder, Created = DateTimeOffset.UnixEpoch };
        // A reserve holds this one.
        var held = rig.Started(rig.Rig.Request("Reserve job"));
        Assert.Null(rig.Runner.Pause(held.Id, PauseCause.Reserve));
        rig.EndTurn(held.LeadThread!.Value, "At the floor.");
        rig.Runner.Load([reverted]);

        rig.Controls.StopAll(StopSource.Header);
        foreach (var stoppedTask in new[] { one, two, held })
        {
            rig.Agents.Finish(stoppedTask.LeadThread!.Value, null);
            rig.Runner.OnTurnFinished(stoppedTask.LeadThread.Value, TurnStatus.Interrupted, continues: false);
        }
        rig.EndProcesses();
        rig.Runner.CheckStopping();
        Assert.Equal(StopCause.StopAll, rig.Now(one).Mission.StoppedBy);
        // Stop all stopped the reserve's task too (its state kept), but the floor still holds it.
        Assert.Equal((MissionState.Stopped, PauseCause.Reserve), (rig.Now(held).Mission.State, rig.Now(held).Mission.PausedBy));

        var report = rig.Controls.ResumeAll();

        Assert.True(report.Result.AfterStop);
        Assert.Equal(2, report.Result.PickedUp);
        Assert.Equal(["Reserve job"], report.Result.HeldByReserve);
        Assert.Equal(MissionState.Stopped, rig.Now(held).Mission.State);
        Assert.NotEqual(MissionState.Stopped, rig.Now(one).Mission.State);
        Assert.NotEqual(MissionState.Stopped, rig.Now(two).Mission.State);
        Assert.Equal(MissionState.Stopped, rig.Now(hand).Mission.State);
        Assert.Equal(1, report.Result.Reverted);
        Assert.Equal(MissionState.Stopped, rig.Runner.Find(revertedId)!.Mission.State);
        Assert.Contains("“Price watch” stays stopped, because you stopped it.", report.Line, StringComparison.Ordinal);
        Assert.Contains("1 reverted task waits for Restart.", report.Line, StringComparison.Ordinal);
        Assert.StartsWith("Resumed all after the stop: ", report.Line, StringComparison.Ordinal);
        Assert.Null(rig.Stop.Report);
        Assert.True(rig.Hold.DutiesMayFire);
    }

    [Fact]
    public void Resume_all_leaves_a_task_a_reserve_still_holds_and_names_it()
    {
        var rig = Rig();
        var held = rig.Started(rig.Rig.Request("Reserve job"));
        Assert.Null(rig.Runner.Pause(held.Id, PauseCause.Reserve));
        rig.EndTurn(held.LeadThread!.Value, "At the floor.");
        var report = rig.Controls.ResumeAll();
        Assert.Equal(0, report.Result.PickedUp);
        Assert.Equal(["Reserve job"], report.Result.HeldByReserve);
        Assert.Equal(MissionState.Paused, rig.Now(held).Mission.State);
        Assert.Equal(PauseCause.Reserve, rig.Now(held).Mission.PausedBy);
        Assert.Contains("“Reserve job” is held by your reserve; its card resumes it.", report.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_all_while_agents_are_still_stopping_names_the_slow_task_and_leaves_it_for_a_second_try()
    {
        var rig = Rig();
        rig.Rig.JobFactory.EndOnTerminate = false;
        var record = rig.Started(rig.Rig.Request("Slow one"));
        rig.Rig.Jobs.For(record.LeadThread!.Value);
        rig.Controls.StopAll(StopSource.Tray);
        Assert.Equal(MissionState.Stopping, rig.Now(record).Mission.State);

        var first = rig.Controls.ResumeAll();
        Assert.Equal(0, first.Result.PickedUp);
        Assert.Equal(["Slow one"], first.Result.StillStopping);
        Assert.Contains("“Slow one” is still stopping; Resume all again once it has stopped.", first.Line, StringComparison.Ordinal);
        Assert.Equal(MissionState.Stopping, rig.Now(record).Mission.State);
    }

    [Fact]
    public void The_tray_items_flip_with_the_toggle_and_Stop_Assistant_runs_Stop_all()
    {
        var rig = Rig();
        var record = rig.Started();
        var items = rig.Controls.TrayItems();
        Assert.Equal(["Stop Assistant", "Pause all"], items.Select(i => i.Label));
        Assert.Equal(["assistant.stop", "assistant.pause"], items.Select(i => i.Id));

        items[1].Run();
        Assert.Equal(MissionState.Pausing, rig.Now(record).Mission.State);
        var flipped = rig.Controls.TrayItems();
        Assert.Equal(["Stop Assistant", "Resume all"], flipped.Select(i => i.Label));
        flipped[1].Run();
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);
        Assert.Equal("Pause all", rig.Controls.TrayItems()[1].Label);

        rig.Controls.TrayItems()[0].Run();
        Assert.Equal(1, rig.Stop.Presses);
        Assert.Equal(MissionState.Stopping, rig.Now(record).Mission.State);
        Assert.Contains("(Tray: Stop Assistant)", rig.Stop.Report!.Strip, StringComparison.Ordinal);
        Assert.True(rig.Hold.StopHeld);
        Assert.Equal("Resume all", rig.Controls.TrayItems()[1].Label);
    }
}
