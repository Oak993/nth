using System.Diagnostics;
using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6 "What Stop all does" and "Afterwards"; ED-NEW-dextra-as6-3): Stop all. Its flags are set in
// the first 50 ms, from any thread, with the app's main thread busy; every process tree is ended through its job; each agent
// shows its confirmed state, and one not confirmed within 5 s shows "Still stopping" with a Force end; open decisions are held
// and the live provider approvals answered Deny; the scheduler is held; the chord stops every scope; Resume all re-raises what
// was held bound to its inputs.
[Trait("Decision", "ED-NEW-dextra-as6-3")]
[Trait("Feature", "AS-6")]
public sealed class StopAllTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private ControlsRig Rig(bool stopKey = true, Func<DecisionItem, ValueList<DecisionInput>?>? currentInputs = null, MissionLimits? limits = null) =>
        new(temp.Folder, stopKey, currentInputs: currentInputs, limits: limits);

    [Fact]
    public void The_flags_are_set_before_anything_else_while_the_main_thread_is_busy_and_the_rest_follows_when_it_is_free()
    {
        var rig = Rig(limits: new MissionLimits { LocalAgents = 1 });
        var voice = new RecordingScope("voice");
        rig.Scopes.Add(voice);
        var running = rig.Started();
        var lead = running.LeadThread!.Value;
        rig.Rig.Jobs.For(lead);
        // A second mission waits at the gate: its queued steps are dropped by the flags.
        var waiting = rig.Runner.Start(rig.Rig.Request("Second mission"));
        Assert.False(waiting.Launched.IsCompleted);
        rig.Agents.Defer = true; // the app's main thread is busy

        var report = rig.Stop.Press(StopSource.Chord, "Ctrl+Alt+Esc");

        // Only flags so far: the scheduler is held, the voice is cut, the queued mission's launch is cancelled, the process tree is
        // ended from the pressing thread. No interrupt, no card and no state has been touched.
        Assert.True(rig.Hold.StopHeld);
        Assert.Equal(1, voice.Stops);
        Assert.True(MissionRig.Within(waiting.Launched, 10));
        Assert.Null(rig.Now(waiting.Record).LeadThread);
        Assert.Equal(1, rig.Rig.JobFactory.Made.Single().Terminations);
        Assert.Equal(0, rig.Agents.Of(lead).Interrupts);
        Assert.Equal(MissionState.Working, rig.Now(running).Mission.State);
        Assert.Equal(0, report.Agents);
        Assert.StartsWith("Stop sent in ", report.Strip, StringComparison.Ordinal);

        rig.Agents.RunPosted();
        Assert.Equal(1, rig.Agents.Of(lead).Interrupts);
        Assert.Equal(MissionState.Stopping, rig.Now(running).Mission.State);
        Assert.Equal(StopCause.StopAll, rig.Now(running).Mission.StoppedBy);
    }

    [Fact]
    public void The_flags_take_under_50_ms_with_a_hundred_missions_and_sixteen_agents()
    {
        // Warm: the first press pays for the code's first run, which is not what the claim is about.
        var warm = Rig();
        warm.Started();
        warm.Stop.Press(StopSource.Header);

        var rig = new ControlsRig(temp.Folder, realStopClock: true);
        for (var i = 0; i < 16; i++) rig.Started(rig.Rig.Request("Mission " + i));
        for (var i = 16; i < 100; i++) rig.Runner.Start(rig.Rig.Request("Mission " + i)); // 84 wait at the gate
        Assert.Equal(16, rig.Rig.Gate.Running);
        rig.Agents.Defer = true;
        foreach (var chat in rig.Agents.Chats) rig.Rig.Jobs.For(chat.Id);

        var report = rig.Stop.Press(StopSource.Chord);

        Assert.True(report.SentMs < 50, $"the flags took {report.SentMs:0.0} ms");
        Assert.True(rig.Hold.StopHeld);
    }

    [Fact]
    public void Process_trees_are_ended_and_each_agent_is_confirmed_one_by_one_with_the_two_times_kept_apart()
    {
        var rig = Rig();
        rig.Rig.JobFactory.EndOnTerminate = false;
        var (record, lead, head) = rig.WithHead("Voice");
        rig.StartHead(head);
        rig.Rig.Jobs.For(lead);
        rig.Rig.Jobs.For(head);
        var changes = new List<StopReport?>();
        rig.Stop.Changed += changes.Add;

        var report = rig.Stop.Press(StopSource.Chord, "Ctrl+Alt+Esc");

        Assert.All(rig.Rig.JobFactory.Made, job => Assert.True(job.Terminations >= 1));
        var now = rig.Stop.Report!;
        Assert.Equal((2, 0), (now.Agents, now.Confirmed));
        Assert.Equal(MissionState.Stopping, rig.Now(record).Mission.State);
        Assert.StartsWith("Stop sent in ", now.Strip, StringComparison.Ordinal);
        Assert.Contains("Confirming agents: 0 of 2 stopped", now.Strip, StringComparison.Ordinal);

        // The head's turn ends and its process is gone: one of two.
        rig.Agents.Finish(head, null);
        rig.Runner.HeadFinished(head, TurnStatus.Interrupted);
        rig.Rig.JobFactory.Made[1].LiveProcesses = 0;
        rig.Time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal((2, 1), (rig.Stop.Report!.Agents, rig.Stop.Report.Confirmed));
        Assert.False(rig.Stop.Report.Done);

        // Then the lead's.
        rig.Time.Advance(TimeSpan.FromMilliseconds(300));
        rig.Agents.Finish(lead, null);
        rig.Runner.OnTurnFinished(lead, TurnStatus.Interrupted, continues: false);
        rig.Rig.JobFactory.Made[0].LiveProcesses = 0;
        rig.Time.Advance(TimeSpan.FromMilliseconds(100));
        var done = rig.Stop.Report!;
        Assert.True(done.Done);
        Assert.Equal(TimeSpan.FromMilliseconds(500), done.ConfirmedIn);
        Assert.Contains("2 of 2 agents confirmed stopped in 0.5 s; process trees closed.", done.Strip, StringComparison.Ordinal);
        Assert.Contains("(Ctrl+Alt+Esc)", done.Strip, StringComparison.Ordinal);
        Assert.Contains("Nothing was undone.", done.Strip, StringComparison.Ordinal);
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
        Assert.NotEmpty(changes);
        Assert.Empty(done.StillStopping);
        Assert.Equal(0, report.Confirmed); // the report Press handed back is the first one: what was sent
    }

    [Fact]
    public void An_agent_not_confirmed_within_five_seconds_shows_Still_stopping_and_Force_end_ends_its_job()
    {
        var rig = Rig();
        rig.Rig.JobFactory.EndOnTerminate = false;
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Rig.Jobs.For(lead);
        rig.Stop.Press(StopSource.Header);
        rig.Agents.Finish(lead, null);
        rig.Runner.OnTurnFinished(lead, TurnStatus.Interrupted, continues: false);

        rig.Time.Advance(TimeSpan.FromSeconds(4.9));
        Assert.Empty(rig.Stop.Report!.StillStopping);
        rig.Time.Advance(TimeSpan.FromSeconds(0.2));
        var slow = Assert.Single(rig.Stop.Report!.StillStopping);
        Assert.Equal(lead, slow.Thread);
        Assert.Equal("Lead", slow.Name);
        Assert.False(slow.Settled);
        Assert.Equal(MissionState.Stopping, rig.Now(record).Mission.State);

        Assert.True(rig.Stop.ForceEnd(lead));
        Assert.Empty(rig.Rig.Jobs.Threads);
        Assert.True(rig.Rig.JobFactory.Made.Single().Disposed);
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
        Assert.True(rig.Stop.Report!.Done);
        Assert.Empty(rig.Stop.Report.StillStopping);
        Assert.False(rig.Stop.ForceEnd(Guid.NewGuid()));
        Assert.Contains(rig.Rig.Audit, e => e.Tool == "force_end");
    }

    [Fact]
    public void Open_decisions_are_held_and_live_approvals_answered_Deny_and_Resume_all_raises_them_bound_to_their_inputs()
    {
        var changed = false;
        var rig = Rig(currentInputs: card => changed && card.Kind == DecisionKind.Limit
            ? new ValueList<DecisionInput>([new("Task", "Write the docs and the changelog")])
            : null, limits: new MissionLimits { Depth = 2 });
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Runner.OnApproval(lead, ControlsRig.Approval("a1", "npm test"));
        Assert.Null(rig.Runner.RequestHelpers(record.Id, "Write the docs", 2, "It is a lot", out _));
        var limit = rig.Queue.Of(record.Id).Single(c => c.Kind == DecisionKind.Limit);

        rig.Stop.Press(StopSource.Chord);

        Assert.Contains((lead, "a1", false), rig.Agents.Resolutions);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Items.Single(c => c.RequestId == "a1").State);
        Assert.Equal(DecisionHold.Stop, rig.Queue.Find(limit.Id)!.HeldBy);
        Assert.Equal("Held by Stop.", rig.Queue.Answer(limit.Id, DecisionAnswer.AllowOnce));
        rig.EndTurn(lead, null, TurnStatus.Interrupted);
        rig.EndProcesses();
        rig.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);

        changed = true; // the card's inputs changed while it was held
        var report = rig.Controls.ResumeAll();

        Assert.Equal(1, report.Result.PickedUp);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Items.Single(c => c.Id == limit.Id).State);
        Assert.Equal("Its inputs changed while it was held.", rig.Queue.Items.Single(c => c.Id == limit.Id).Note);
        var again = rig.Queue.Of(record.Id).Single(c => c.Kind == DecisionKind.Limit);
        Assert.NotEqual(limit.Id, again.Id);
        Assert.Equal("Write the docs and the changelog", again.Inputs.Single().Value);
        Assert.Equal(DecisionState.Open, again.State);
    }

    [Fact]
    public void The_scheduler_is_held_until_Resume_all_and_a_mission_started_meanwhile_waits_paused()
    {
        var rig = Rig();
        rig.Started();
        rig.Stop.Press(StopSource.Tray);
        Assert.False(rig.Hold.DutiesMayFire);

        var meanwhile = rig.Runner.Start(rig.Rig.Request("Weekly stats") with { Origin = MissionOrigin.Duty });
        Assert.True(meanwhile.Launched.IsCompleted);
        var paused = rig.Now(meanwhile.Record);
        Assert.Equal((MissionState.Paused, PauseCause.PauseAll, MissionState.Planning), (paused.Mission.State, paused.Mission.PausedBy, paused.Mission.ResumeTo));
        Assert.Null(paused.LeadThread);
        Assert.Contains(rig.Rig.Audit, e => e.Tool == "pause" && e.Result == "starts paused: Stop all is on");

        var report = rig.Controls.ResumeAll();
        Assert.True(report.Result.AfterStop);
        Assert.True(rig.Hold.DutiesMayFire);
        Assert.False(rig.Hold.StopHeld);
        MissionRig.Until(() => rig.Now(meanwhile.Record).LeadThread is not null, "the held mission's lead to start");
        Assert.Equal(MissionState.Working, rig.Now(meanwhile.Record).Mission.State);
        Assert.Null(rig.Stop.Report);
    }

    [Fact]
    public void The_chord_stops_every_scope_and_the_assistant_at_once_and_a_scope_that_throws_hides_nothing()
    {
        var rig = Rig();
        var tactus = new RecordingScope("Tactus streams") { Throws = new InvalidOperationException("stream stuck") };
        var voice = new RecordingScope("voice");
        rig.Scopes.AddRange([tactus, voice]);
        var faults = new List<string>();
        rig.Runner.Faulted += faults.Add;
        var record = rig.Started();
        Assert.True(rig.Key.TryAcquire("tactus:stream", out var lease));

        rig.Host.Press();

        Assert.Equal((1, 1), (tactus.Stops, voice.Stops));
        Assert.Equal(["Tactus streams could not stop: stream stuck"], faults);
        Assert.Equal(1, rig.Stop.Presses);
        Assert.Equal(MissionState.Stopping, rig.Now(record).Mission.State);
        Assert.StartsWith("Stop sent in ", rig.Stop.Report!.Strip, StringComparison.Ordinal);
        Assert.Contains("(Ctrl+Alt+Esc)", rig.Stop.Report.Strip, StringComparison.Ordinal);
        lease!.Dispose();
    }

    [Fact]
    public void Stop_all_leaves_a_failed_or_done_task_alone_and_stops_a_paused_one_so_Resume_all_picks_it_up()
    {
        var rig = Rig();
        var paused = rig.Started(rig.Rig.Request("Paused one"));
        Assert.Null(rig.Runner.Pause(paused.Id));
        rig.EndTurn(paused.LeadThread!.Value, "Paused here.");
        Assert.Equal(MissionState.Paused, rig.Now(paused).Mission.State);
        var failed = new MissionRecord(new Mission(Guid.NewGuid(), "Failed one", "dextra", MissionOrigin.Owner, MissionState.Failed)) { Brief = "b", ProjectPath = temp.Folder };
        rig.Runner.Load([failed]);

        rig.Stop.Press(StopSource.Header);

        Assert.Equal(MissionState.Failed, rig.Runner.Find(failed.Id)!.Mission.State);
        var stopped = rig.Now(paused).Mission;
        Assert.Equal((MissionState.Stopped, StopCause.StopAll, MissionState.Working), (stopped.State, stopped.StoppedBy, stopped.ResumeTo));
        Assert.True(MissionMachine.ResumedByResumeAll(stopped));
    }
}
