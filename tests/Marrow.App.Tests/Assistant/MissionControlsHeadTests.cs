using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6b "Heads", r4 B3, B4, G11; ED-NEW-dextra-as6-4): Pause, Resume and Stop for each Hydra head
// of a task. Pause finishes the head's turn first and holds its finish; one head pauses or stops while the others work; a task's
// state sums up its heads; a queued head never starts before the heads it waits on are done; line counts never call a pausing head
// paused or a stopping head stopped; Resume returns each head to the state it held.
[Trait("Decision", "ED-NEW-dextra-as6-4")]
[Trait("Feature", "AS-6")]
public sealed class MissionControlsHeadTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private ControlsRig Rig() => new(temp.Folder);

    private static string HeadId(Guid head) => head.ToString("D");

    private static MissionHead HeadOf(MissionRecord record, Guid head) => record.Mission.Heads.Single(h => h.Id == HeadId(head));

    private static string Line(MissionRecord record) => TaskRollup.Sum(record.Mission.Heads).Line;

    /// <summary>A team mission with two heads that are working.</summary>
    private (ControlsRig Rig, MissionRecord Record, Guid Lead, Guid A, Guid B) Team(string a = "Writer", string b = "Voice")
    {
        var rig = Rig();
        var record = rig.Started(rig.Rig.Request(team: true));
        var headA = rig.AddHead(record, a);
        var headB = rig.AddHead(record, b);
        rig.StartHead(headA);
        rig.StartHead(headB);
        return (rig, rig.Now(record), record.LeadThread!.Value, headA, headB);
    }

    [Fact]
    public void Pausing_a_head_sends_no_interrupt_waits_for_its_turn_and_the_others_keep_working()
    {
        var (rig, record, lead, a, b) = Team();

        Assert.Null(rig.Controls.PauseHead(record.Id, a));

        var pausing = rig.Now(record);
        Assert.Equal((HeadState.Pausing, HeadState.Working), (HeadOf(pausing, a).State, HeadOf(pausing, b).State));
        Assert.Equal(MissionState.Working, pausing.Mission.State); // Voice keeps working, so the task does
        Assert.Equal(0, rig.Agents.Of(a).Interrupts);
        Assert.Empty(rig.Agents.HeadStopped);
        Assert.Equal("1 of 2 pausing", Line(pausing)); // never called paused before it gets there
        Assert.Equal((SlotA.Resume, SlotB.Stop), (MissionStates.Slots(HeadState.Pausing).A, MissionStates.Slots(HeadState.Pausing).B));

        // Its turn ends: the finish is held, so Hydra does not finish the head or tell the lead; it is Paused, its slot kept.
        rig.Agents.Finish(a, "Writer finished its piece.");
        Assert.True(rig.Runner.HeadFinished(a, TurnStatus.Completed));
        var paused = rig.Now(record);
        Assert.Equal(HeadState.Paused, HeadOf(paused, a).State);
        Assert.Equal("1 of 2 paused", Line(paused));
        Assert.Equal(MissionState.Working, paused.Mission.State);
        Assert.True(rig.Rig.Gate.Holds(a));
        Assert.Empty(rig.Agents.HeldFinished);
        Assert.True(rig.Agents.TurnRunning(lead));
    }

    [Fact]
    public void Resume_on_a_head_whose_turn_ended_while_paused_gives_Hydra_its_finish_and_it_counts_done()
    {
        var (rig, record, _, a, _) = Team();
        Assert.Null(rig.Controls.PauseHead(record.Id, a));
        rig.Agents.Finish(a, "Writer finished its piece.");
        Assert.True(rig.Runner.HeadFinished(a, TurnStatus.Completed));

        Assert.Null(rig.Controls.ResumeHead(record.Id, a));

        Assert.Equal([(a, TurnStatus.Completed)], rig.Agents.HeldFinished);
        Assert.Equal(HeadState.Working, HeadOf(rig.Now(record), a).State);
        // Hydra runs the tail of the head's turn again; the head is no longer held, so it finishes.
        Assert.False(rig.Runner.HeadFinished(a, TurnStatus.Completed));
        Assert.Equal(HeadState.Done, HeadOf(rig.Now(record), a).State);
        Assert.False(rig.Rig.Gate.Holds(a));
        Assert.Empty(rig.Agents.HeadsRestarted);
    }

    [Fact]
    public void Resume_on_a_pausing_head_cancels_the_pause_and_its_turn_goes_on()
    {
        var (rig, record, _, a, _) = Team();
        Assert.Null(rig.Controls.PauseHead(record.Id, a));
        Assert.Null(rig.Controls.ResumeHead(record.Id, a));
        Assert.Equal(HeadState.Working, HeadOf(rig.Now(record), a).State);
        Assert.True(rig.Agents.TurnRunning(a));
        Assert.Empty(rig.Agents.HeldFinished);
        Assert.Equal("Writer has nothing to resume.", rig.Controls.ResumeHead(record.Id, a));
        rig.Agents.Finish(a, "Done.");
        Assert.False(rig.Runner.HeadFinished(a, TurnStatus.Completed));
        Assert.Equal(HeadState.Done, HeadOf(rig.Now(record), a).State);
    }

    [Fact]
    public void Stopping_a_head_cuts_only_that_head_denies_its_pending_approval_and_holds_its_other_cards()
    {
        var (rig, record, lead, a, b) = Team();
        rig.Rig.Jobs.For(a);
        rig.Rig.Jobs.For(b);
        rig.Runner.OnApproval(a, ControlsRig.Approval("a1", "npm install"));
        rig.Runner.OnApproval(b, ControlsRig.Approval("b1", "npm test"));
        var forB = rig.Queue.Of(record.Id).Single(c => c.ThreadId == b);

        Assert.Null(rig.Controls.StopHead(record.Id, a));

        var stopping = rig.Now(record);
        Assert.Equal(HeadState.Stopping, HeadOf(stopping, a).State);
        Assert.True(HeadOf(stopping, a).StoppedByHand);
        Assert.Equal("1 of 2 stopping", Line(stopping)); // never called stopped before it gets there
        Assert.Equal(1, rig.Agents.Of(a).Interrupts);
        Assert.Equal([a], rig.Agents.HeadStopped);
        Assert.Equal(0, rig.Agents.Of(b).Interrupts);
        Assert.Equal(0, rig.Agents.Of(lead).Interrupts);
        Assert.Contains((a, "a1", false), rig.Agents.Resolutions);
        Assert.DoesNotContain(rig.Agents.Resolutions, r => r.Thread == b);
        Assert.Equal(DecisionState.Open, rig.Queue.Find(forB.Id)!.State);
        Assert.Equal(1, rig.Rig.JobFactory.Made[0].Terminations);
        Assert.Equal(0, rig.Rig.JobFactory.Made[1].Terminations);
        Assert.False(rig.Rig.Gate.Holds(a));
        Assert.True(rig.Rig.Gate.Holds(b));

        // The head's turn ends and its process is gone: only then is it Stopped, and the task still works.
        rig.Agents.Finish(a, null);
        rig.Runner.HeadFinished(a, TurnStatus.Interrupted);
        rig.Runner.CheckStopping();
        var stopped = rig.Now(record);
        Assert.Equal(HeadState.Stopped, HeadOf(stopped, a).State);
        Assert.Equal("1 of 2 stopped", Line(stopped));
        Assert.Equal(MissionState.NeedsYou, stopped.Mission.State); // Voice's card still waits; the task did not stop
        Assert.Equal((SlotA.Resume, SlotB.Stop), (MissionStates.Slots(HeadState.Stopped).A, MissionStates.Slots(HeadState.Stopped).B));
    }

    [Fact]
    public void A_queued_head_never_starts_before_the_heads_it_waits_on_and_stopping_the_one_it_waits_on_stops_the_task()
    {
        var rig = Rig();
        var record = rig.Started(rig.Rig.Request(team: true));
        var subtitles = rig.AddHead(record, "Subtitles");
        var thumbnail = rig.AddHead(record, "Thumbnail", true, subtitles);
        rig.StartHead(subtitles);
        var thumbnailStart = rig.Runner.AdmitHead(thumbnail, ProviderKind.Claude);
        Assert.False(thumbnailStart.IsCompleted);
        Assert.Equal(HeadState.Queued, HeadOf(rig.Now(record), thumbnail).State);
        Assert.Equal([HeadId(subtitles)], HeadOf(rig.Now(record), thumbnail).DependsOn);

        // Stopping Subtitles while Thumbnail waits stops the task, never Working with nothing running.
        Assert.Null(rig.Controls.StopHead(record.Id, subtitles));
        rig.Agents.Finish(subtitles, null);
        rig.Runner.HeadFinished(subtitles, TurnStatus.Interrupted);
        rig.Runner.CheckStopping();
        var stopped = rig.Now(record);
        Assert.Equal(MissionState.Stopped, stopped.Mission.State);
        Assert.Equal("1 of 2 stopped · Thumbnail waits on Subtitles", Line(stopped));
        Assert.False(thumbnailStart.IsCompleted);

        // Resuming Subtitles resumes the task; Thumbnail still waits until Subtitles is done.
        Assert.Null(rig.Controls.ResumeHead(record.Id, subtitles));
        var back = rig.Now(record);
        Assert.Equal(MissionState.Working, back.Mission.State);
        Assert.Equal(HeadState.Working, HeadOf(back, subtitles).State);
        MissionRig.Until(() => rig.Agents.HeadsRestarted.Count == 1, "Subtitles to start again");
        Assert.False(thumbnailStart.IsCompleted);
        Assert.Equal(HeadState.Queued, HeadOf(rig.Now(record), thumbnail).State);

        rig.Agents.Finish(subtitles, "Subtitles done.");
        Assert.False(rig.Runner.HeadFinished(subtitles, TurnStatus.Completed));
        Assert.True(MissionRig.Within(thumbnailStart, 10));
        Assert.Equal(HeadState.Working, HeadOf(rig.Now(record), thumbnail).State);
    }

    [Fact]
    public void Resuming_only_the_head_that_waits_on_a_paused_head_leaves_the_task_paused()
    {
        var rig = Rig();
        var record = rig.Started(rig.Rig.Request(team: true));
        var lead = record.LeadThread!.Value;
        var subtitles = rig.AddHead(record, "Subtitles");
        var thumbnail = rig.AddHead(record, "Thumbnail", true, subtitles);
        rig.StartHead(subtitles);
        _ = rig.Runner.AdmitHead(thumbnail, ProviderKind.Claude);

        Assert.Null(rig.Controls.Pause(record.Id));
        rig.Agents.Finish(subtitles, "Subtitles piece.");
        Assert.True(rig.Runner.HeadFinished(subtitles, TurnStatus.Completed));
        rig.EndTurn(lead, "Waiting for the heads.");
        var paused = rig.Now(record);
        Assert.Equal(MissionState.Paused, paused.Mission.State);
        Assert.Equal((HeadState.Paused, HeadState.Paused), (HeadOf(paused, subtitles).State, HeadOf(paused, thumbnail).State));

        Assert.Null(rig.Controls.ResumeHead(record.Id, thumbnail));
        var still = rig.Now(record);
        Assert.Equal(MissionState.Paused, still.Mission.State);
        Assert.Equal(HeadState.Queued, HeadOf(still, thumbnail).State);
        Assert.Equal("1 of 2 paused · Thumbnail waits on Subtitles", Line(still));
        Assert.Empty(rig.Agents.HeldFinished);
    }

    [Fact]
    public void A_tasks_Resume_returns_each_head_to_the_state_it_held_and_gives_a_held_finish_back_to_Hydra()
    {
        var rig = Rig();
        var record = rig.Started(rig.Rig.Request(team: true));
        var lead = record.LeadThread!.Value;
        var working = rig.AddHead(record, "Writer");
        var queued = rig.AddHead(record, "Voice");
        rig.StartHead(working);

        Assert.Null(rig.Controls.Pause(record.Id));
        var pausing = rig.Now(record);
        Assert.Equal((HeadState.Pausing, HeadState.Paused), (HeadOf(pausing, working).State, HeadOf(pausing, queued).State));
        Assert.Equal("1 of 2 pausing · 1 of 2 paused", Line(pausing));
        rig.Agents.Finish(working, "Writer finished its piece.");
        Assert.True(rig.Runner.HeadFinished(working, TurnStatus.Completed));
        rig.EndTurn(lead, "Waiting for the heads.");
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);

        Assert.Null(rig.Controls.Resume(record.Id));
        var back = rig.Now(record);
        Assert.Equal((HeadState.Working, HeadState.Queued), (HeadOf(back, working).State, HeadOf(back, queued).State));
        Assert.Equal([(working, TurnStatus.Completed)], rig.Agents.HeldFinished);
    }

    [Fact]
    public void A_head_added_while_the_task_is_paused_or_stopped_is_paused_or_stopped_with_it_and_starts_on_Resume()
    {
        var rig = Rig();
        var record = rig.Started(rig.Rig.Request(team: true));
        var lead = record.LeadThread!.Value;
        Assert.Null(rig.Controls.Pause(record.Id));
        rig.EndTurn(lead, "Waiting for the heads.");
        var late = rig.AddHead(record, "Late");
        var start = rig.Runner.AdmitHead(late, ProviderKind.Claude);
        Assert.Equal(HeadState.Paused, HeadOf(rig.Now(record), late).State);
        Assert.False(start.IsCompleted);

        Assert.Null(rig.Controls.Resume(record.Id));
        Assert.True(MissionRig.Within(start, 10)); // it starts only now (it may already be through the gate by the next line)
        Assert.Equal(HeadState.Working, HeadOf(rig.Now(record), late).State);
    }

    [Fact]
    public void A_task_Resume_keeps_a_head_stopped_by_hand_stopped_unless_the_task_cannot_go_on_without_it()
    {
        var (rig, record, lead, a, b) = Team("Writer", "Voice");
        Assert.Null(rig.Controls.StopHead(record.Id, a));
        rig.Agents.Finish(a, null);
        rig.Runner.HeadFinished(a, TurnStatus.Interrupted);
        rig.Runner.CheckStopping();
        Assert.Equal(HeadState.Stopped, HeadOf(rig.Now(record), a).State);

        // The whole task is stopped and resumed: Voice returns, Writer stays stopped, because the owner stopped it.
        Assert.Null(rig.Controls.Stop(record.Id));
        rig.EndTurn(lead, null, TurnStatus.Interrupted);
        rig.Agents.Finish(b, null);
        rig.Runner.HeadFinished(b, TurnStatus.Interrupted);
        rig.EndProcesses();
        rig.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
        Assert.Null(rig.Controls.Resume(record.Id));
        var back = rig.Now(record);
        Assert.Equal(HeadState.Stopped, HeadOf(back, a).State);
        Assert.True(HeadOf(back, a).StoppedByHand);
        Assert.Equal(HeadState.Working, HeadOf(back, b).State);
        Assert.Equal(MissionState.Working, back.Mission.State);

        // A head's own Resume brings it back.
        Assert.Null(rig.Controls.ResumeHead(record.Id, a));
        Assert.Equal(HeadState.Working, HeadOf(rig.Now(record), a).State);
        Assert.False(HeadOf(rig.Now(record), a).StoppedByHand);
    }

    [Fact]
    public void A_task_that_summed_to_Stopped_from_its_heads_while_its_lead_runs_is_cut_by_its_own_Stop_and_by_Stop_all()
    {
        foreach (var byStopAll in new[] { false, true })
        {
            var (rig, record, lead, a, b) = Team();
            foreach (var head in new[] { a, b })
            {
                Assert.Null(rig.Controls.StopHead(record.Id, head));
                rig.Agents.Finish(head, null);
                rig.Runner.HeadFinished(head, TurnStatus.Interrupted);
                rig.Runner.CheckStopping();
            }
            // Every head the task could run is stopped: it reads Stopped, though its lead has not been told anything.
            Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
            Assert.Equal(0, rig.Agents.Of(lead).Interrupts);
            Assert.True(rig.Agents.TurnRunning(lead));

            if (byStopAll) rig.Controls.StopAll(Marrow.App.Safety.StopSource.Header);
            else Assert.Null(rig.Controls.Stop(record.Id));

            Assert.Equal(1, rig.Agents.Of(lead).Interrupts);
            Assert.Equal("The task was stopped as a whole; resume the task.", rig.Controls.ResumeHead(record.Id, a));
            rig.EndTurn(lead, null, TurnStatus.Interrupted);
            rig.EndProcesses();
            rig.Runner.CheckStopping();
            Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
            // Now it is stopped as a whole: Stop has nothing more to do, and the task's Resume brings it back.
            Assert.Equal("The task is already stopping or stopped.", rig.Controls.Stop(record.Id));
            // Every head was stopped on its own and nothing else could go on, so the task's Resume brings them back with it.
            Assert.Null(rig.Controls.Resume(record.Id));
            var back = rig.Now(record).Mission;
            Assert.Equal(MissionState.Working, back.State);
            Assert.All(back.Heads, h => Assert.Equal(HeadState.Working, h.State));
        }
    }

    [Fact]
    public void A_task_that_has_been_pausing_for_five_seconds_names_the_head_it_waits_for_and_what_it_does()
    {
        var (rig, record, lead, a, b) = Team("Voice", "Subtitles");
        rig.Agents.Doing[a] = "generating the voiceover (3:42 of script)";
        Assert.Null(rig.Controls.Pause(record.Id));
        Assert.Equal(MissionState.Pausing, rig.Now(record).Mission.State);

        Assert.Null(rig.Controls.StillPausing(record.Id)); // not yet
        rig.Time.Advance(TimeSpan.FromSeconds(4.9));
        Assert.Null(rig.Controls.StillPausing(record.Id));
        rig.Time.Advance(TimeSpan.FromSeconds(0.2));
        var note = rig.Controls.StillPausing(record.Id)!;
        Assert.Equal("Still pausing. Waiting for Voice and Subtitles to finish: generating the voiceover (3:42 of script). "
            + "Pause never cuts a turn; Stop cuts it now.", note.Text);
        Assert.Equal(["Voice", "Subtitles"], note.Waiting);
        Assert.Equal("still finishing: generating the voiceover (3:42 of script)", ControlWords.HeadStillFinishing(rig.Agents.Doing[a]));
        Assert.Equal("still finishing its turn", ControlWords.HeadStillFinishing(null));

        // Voice finishes its turn: the note follows what is still pending, and goes with the pause.
        rig.Agents.Finish(a, "Voice done.");
        Assert.True(rig.Runner.HeadFinished(a, TurnStatus.Completed));
        Assert.Equal(["Subtitles"], rig.Controls.StillPausing(record.Id)!.Waiting);
        Assert.StartsWith("Still pausing. Waiting for Subtitles to finish. ", rig.Controls.StillPausing(record.Id)!.Text, StringComparison.Ordinal);
        rig.Agents.Finish(b, "Subtitles done.");
        Assert.True(rig.Runner.HeadFinished(b, TurnStatus.Completed));
        rig.EndTurn(lead, "Waiting for the heads.");
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);
        Assert.Null(rig.Controls.StillPausing(record.Id));
    }

    [Fact]
    public void A_head_others_wait_on_says_so_so_its_Stop_can_ask()
    {
        var rig = Rig();
        var record = rig.Started(rig.Rig.Request(team: true));
        var subtitles = rig.AddHead(record, "Subtitles");
        var thumbnail = rig.AddHead(record, "Thumbnail", true, subtitles);
        var voice = rig.AddHead(record, "Voice");
        Assert.Equal(["Thumbnail"], rig.Controls.HeadsWaitingOn(record.Id, subtitles));
        Assert.Empty(rig.Controls.HeadsWaitingOn(record.Id, voice));
        Assert.Empty(rig.Controls.HeadsWaitingOn(record.Id, thumbnail));
        Assert.Empty(rig.Controls.HeadsWaitingOn(Guid.NewGuid(), subtitles));
    }

    [Fact]
    public void A_head_of_a_task_that_was_stopped_as_a_whole_is_resumed_through_the_task()
    {
        var (rig, record, lead, a, b) = Team();
        Assert.Null(rig.Controls.Stop(record.Id));
        rig.EndTurn(lead, null, TurnStatus.Interrupted);
        rig.Agents.Finish(a, null);
        rig.Agents.Finish(b, null);
        rig.Runner.HeadFinished(a, TurnStatus.Interrupted);
        rig.Runner.HeadFinished(b, TurnStatus.Interrupted);
        rig.EndProcesses();
        rig.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
        Assert.Equal("The task was stopped as a whole; resume the task.", rig.Controls.ResumeHead(record.Id, a));
        Assert.Equal("No head 00000000-0000-0000-0000-000000000000 in this task.", rig.Controls.PauseHead(record.Id, Guid.Empty));
        Assert.Equal("There is no such mission.", rig.Controls.StopHead(Guid.NewGuid(), a));
    }
}
