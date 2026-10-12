using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-1 (DESIGN.md rev 5, 12.6b; ED-NEW-dextracore-5): Pause, Resume, Stop, Revert and Restart for a task and for
// each head, and the rule that sums a task's heads into one state. Every move lands on a legal state.
public sealed class MissionMachineTests
{
    private static Mission Task(MissionState state, params MissionHead[] heads) =>
        new(Guid.NewGuid(), "Next video: Why lenses fog up", "dextra", MissionOrigin.Owner, state)
        {
            Heads = new ValueList<MissionHead>(heads),
            ChangedFiles = 3,
        };

    private static MissionHead Head(string id, HeadState state, params string[] dependsOn) =>
        new(id, id, state) { DependsOn = new ValueList<string>(dependsOn) };

    private static Mission Done(MissionMove move)
    {
        Assert.True(move.Done, move.Refused);
        return move.Mission;
    }

    private static void Refused(MissionMove move, Mission before)
    {
        Assert.False(move.Done);
        Assert.Same(before, move.Mission);
    }

    private static void Legal(Mission before, Mission after)
    {
        if (before.State != after.State) Assert.True(MissionStates.IsLegal(before.State, after.State), $"{before.State} -> {after.State}");
        foreach (var head in after.Heads)
        {
            var old = before.Heads.FirstOrDefault(h => h.Id == head.Id);
            if (old is not null && old.State != head.State)
                Assert.True(MissionStates.IsLegal(old.State, head.State), $"{head.Id}: {old.State} -> {head.State}");
        }
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Pause_waits_for_the_turn_and_Resume_on_Pausing_cancels_it()
    {
        var working = Task(MissionState.Working);
        var pausing = Done(MissionMachine.Pause(working, turnRunning: true));
        Assert.Equal(MissionState.Pausing, pausing.State);
        Assert.Equal(MissionState.Working, pausing.ResumeTo);
        Legal(working, pausing);
        // Resume while pausing cancels the pause: the turn never stopped.
        var cancelled = Done(MissionMachine.Resume(pausing));
        Assert.Equal(MissionState.Working, cancelled.State);
        Assert.Null(cancelled.ResumeTo);
        // The turn ends: Paused, then Resume continues where it paused.
        var paused = Done(MissionMachine.TurnBoundary(pausing));
        Assert.Equal(MissionState.Paused, paused.State);
        Legal(pausing, paused);
        var resumed = Done(MissionMachine.Resume(paused));
        Assert.Equal(MissionState.Working, resumed.State);
        Legal(paused, resumed);
        // With no turn in flight, and on a task that runs no turn, Pause acts at once.
        Assert.Equal(MissionState.Paused, Done(MissionMachine.Pause(working, turnRunning: false)).State);
        Assert.Equal(MissionState.Paused, Done(MissionMachine.Pause(Task(MissionState.NeedsYou), turnRunning: true)).State);
        Assert.Equal(MissionState.Paused, Done(MissionMachine.Pause(Task(MissionState.Blocked), turnRunning: true)).State);
    }

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    [InlineData(MissionState.Failed)]
    [InlineData(MissionState.Done)]
    [InlineData(MissionState.Pausing)]
    [InlineData(MissionState.Paused)]
    [InlineData(MissionState.Stopping)]
    [InlineData(MissionState.Stopped)]
    public void Pause_is_refused_where_it_means_nothing(MissionState state)
    {
        var task = Task(state);
        Refused(MissionMachine.Pause(task, turnRunning: true), task);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Stop_works_from_any_live_state_and_keeps_where_it_came_from()
    {
        foreach (var state in new[] { MissionState.Planning, MissionState.NeedsYou, MissionState.Working, MissionState.Reviewing, MissionState.Blocked, MissionState.Failed })
        {
            var task = Task(state);
            var stopping = Done(MissionMachine.Stop(task, agentsOpen: true));
            Assert.Equal(MissionState.Stopping, stopping.State);
            Assert.Equal(state, stopping.ResumeTo);
            Legal(task, stopping);
            var stopped = Done(MissionMachine.ConfirmStopped(stopping));
            Assert.Equal(MissionState.Stopped, stopped.State);
            Legal(stopping, stopped);
            Assert.Equal(MissionState.Stopped, Done(MissionMachine.Stop(task, agentsOpen: false)).State);
        }
        // Stopping a paused or pausing task keeps the state it paused from.
        var paused = Done(MissionMachine.Pause(Task(MissionState.Reviewing), turnRunning: false));
        var stoppedFromPause = Done(MissionMachine.Stop(paused, agentsOpen: false, StopCause.StopAll));
        Assert.Equal(MissionState.Reviewing, stoppedFromPause.ResumeTo);
        Assert.Equal(StopCause.StopAll, stoppedFromPause.StoppedBy);
        var pausing = Done(MissionMachine.Pause(Task(MissionState.Working), turnRunning: true));
        Assert.Equal(MissionState.Working, Done(MissionMachine.Stop(pausing, agentsOpen: true)).ResumeTo);
        // Not from Stopping, Stopped or Done; and Resume waits for Stopping to end.
        foreach (var state in new[] { MissionState.Stopping, MissionState.Stopped, MissionState.Done })
        {
            var task = Task(state);
            Refused(MissionMachine.Stop(task, agentsOpen: true), task);
        }
        var stoppingTask = Task(MissionState.Stopping);
        Refused(MissionMachine.Resume(stoppingTask), stoppingTask);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void A_stopped_task_offers_Resume_while_its_changes_are_kept_and_Restart_after_a_revert()
    {
        var stopped = Done(MissionMachine.Stop(Task(MissionState.Working), agentsOpen: false));
        Assert.Equal(SlotA.Resume, stopped.Slots.A);
        Refused(MissionMachine.Restart(stopped), stopped);
        Assert.Equal(MissionState.Working, Done(MissionMachine.Resume(stopped)).State);

        var reverted = Done(MissionMachine.Revert(stopped));
        Assert.True(reverted.Reverted);
        Assert.Equal(0, reverted.ChangedFiles);
        Assert.Equal(SlotA.Restart, reverted.Slots.A);
        Assert.False(reverted.Slots.BEnabled);
        Refused(MissionMachine.Resume(reverted), reverted);
        Refused(MissionMachine.Revert(reverted), reverted);
        var restarted = Done(MissionMachine.Restart(reverted));
        Assert.Equal(MissionState.Planning, restarted.State);
        Assert.False(restarted.Reverted);
        Legal(reverted, restarted);

        // Revert only on a stopped task that changed files; Restart from Failed.
        var working = Task(MissionState.Working);
        Refused(MissionMachine.Revert(working), working);
        var clean = Done(MissionMachine.Stop(Task(MissionState.Working) with { ChangedFiles = 0 }, agentsOpen: false));
        Refused(MissionMachine.Revert(clean), clean);
        Assert.Equal(MissionState.Planning, Done(MissionMachine.Restart(Task(MissionState.Failed))).State);
        var paused = Task(MissionState.Paused);
        Refused(MissionMachine.Restart(paused), paused);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void A_reserves_pause_and_a_held_irreversible_card_resume_to_Needs_you()
    {
        var reserve = Done(MissionMachine.Pause(Task(MissionState.Working), turnRunning: false, PauseCause.Reserve));
        Assert.Equal(BoardGroup.NeedsYou, reserve.Group);
        Assert.Equal(MissionState.NeedsYou, Done(MissionMachine.Resume(reserve)).State);
        Assert.False(MissionMachine.ResumedByResumeAll(reserve));

        var owner = Done(MissionMachine.Pause(Task(MissionState.Working), turnRunning: false));
        Assert.Equal(MissionState.NeedsYou, Done(MissionMachine.Resume(owner, heldIrreversibleCard: true)).State);
        Assert.True(MissionMachine.ResumedByResumeAll(owner));

        // Resume all picks up what Stop all stopped, never what the owner stopped by hand, nor a reverted task.
        var byStopAll = Done(MissionMachine.Stop(Task(MissionState.Working), agentsOpen: false, StopCause.StopAll));
        var byHand = Done(MissionMachine.Stop(Task(MissionState.Working), agentsOpen: false));
        Assert.True(MissionMachine.ResumedByResumeAll(byStopAll));
        Assert.False(MissionMachine.ResumedByResumeAll(byHand));
        Assert.False(MissionMachine.ResumedByResumeAll(Done(MissionMachine.Revert(byStopAll))));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void The_task_sums_its_heads_in_the_designs_order()
    {
        // Running while any head works.
        Assert.Equal(TaskSum.Running, TaskRollup.Sum([Head("voice", HeadState.Working), Head("subs", HeadState.Paused)]).Sum);
        // Or a queued head can start because everything it waits on is done.
        Assert.Equal(TaskSum.Running, TaskRollup.Sum([Head("voice", HeadState.Done), Head("subs", HeadState.Queued, "voice")]).Sum);
        // A queued head waiting on a paused head does not count.
        Assert.Equal(TaskSum.Paused, TaskRollup.Sum([Head("voice", HeadState.Paused), Head("subs", HeadState.Queued, "voice")]).Sum);
        // Then: Stopping, Pausing, Paused, Stopped, in that order.
        Assert.Equal(TaskSum.Stopping, TaskRollup.Sum([Head("a", HeadState.Stopping), Head("b", HeadState.Pausing), Head("c", HeadState.Paused)]).Sum);
        Assert.Equal(TaskSum.Pausing, TaskRollup.Sum([Head("a", HeadState.Stopped), Head("b", HeadState.Pausing), Head("c", HeadState.Paused)]).Sum);
        Assert.Equal(TaskSum.Paused, TaskRollup.Sum([Head("a", HeadState.Stopped), Head("b", HeadState.Paused)]).Sum);
        Assert.Equal(TaskSum.Stopped, TaskRollup.Sum([Head("a", HeadState.Stopped), Head("b", HeadState.Queued, "a")]).Sum);
        // Otherwise Blocked.
        Assert.Equal(TaskSum.Blocked, TaskRollup.Sum([Head("a", HeadState.Blocked), Head("b", HeadState.Queued, "a")]).Sum);
        // Finished heads and reserve-held heads are not counted.
        Assert.Equal(TaskSum.Finished, TaskRollup.Sum([Head("a", HeadState.Done)]).Sum);
        Assert.Equal(TaskSum.ReserveHeld, TaskRollup.Sum([Head("a", HeadState.Done), Head("b", HeadState.Paused) with { HeldByReserve = true }]).Sum);
        Assert.Equal(TaskSum.Running, TaskRollup.Sum([Head("a", HeadState.Working), Head("b", HeadState.Stopped) with { HeldByReserve = true }]).Sum);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Stopping_the_last_head_that_can_run_stops_the_task_with_the_designs_line()
    {
        // Voice is done; Subtitles works; Thumbnail waits on Subtitles.
        var task = Task(MissionState.Working,
            Head("Voice", HeadState.Done),
            Head("Subtitles", HeadState.Working, "Voice"),
            Head("Thumbnail", HeadState.Queued, "Subtitles"));
        var stopped = Done(MissionMachine.StopHead(task, "Subtitles", agentOpen: false));
        Assert.Equal(MissionState.Stopped, stopped.State);
        Legal(task, stopped);
        Assert.Equal("1 of 2 stopped \U000000B7 Thumbnail waits on Subtitles", TaskRollup.Sum(stopped.Heads).Line); // U+00B7 MIDDLE DOT
        Assert.True(stopped.Heads.Single(h => h.Id == "Subtitles").StoppedByHand);
        // Resuming only Thumbnail, which waits on Subtitles, cannot run; resuming Subtitles resumes the task.
        Refused(MissionMachine.ResumeHead(stopped, "Thumbnail"), stopped);
        var resumed = Done(MissionMachine.ResumeHead(stopped, "Subtitles"));
        Assert.Equal(MissionState.Working, resumed.State);
        Legal(stopped, resumed);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Pausing_a_head_with_a_turn_counts_it_as_pausing_until_its_boundary()
    {
        var task = Task(MissionState.Working, Head("Voice", HeadState.Working), Head("Writer", HeadState.Working));
        var one = Done(MissionMachine.PauseHead(task, "Voice", turnRunning: true));
        Assert.Equal(MissionState.Working, one.State);
        Assert.Equal("1 of 2 pausing", TaskRollup.Sum(one.Heads).Line);
        var both = Done(MissionMachine.PauseHead(one, "Writer", turnRunning: true));
        Assert.Equal(MissionState.Pausing, both.State);
        Legal(one, both);
        var voiceDone = Done(MissionMachine.TurnBoundary(both, "Voice"));
        Assert.Equal(MissionState.Pausing, voiceDone.State);
        Assert.Equal("1 of 2 pausing \U000000B7 1 of 2 paused", TaskRollup.Sum(voiceDone.Heads).Line); // U+00B7 MIDDLE DOT
        var paused = Done(MissionMachine.TurnBoundary(voiceDone));
        Assert.Equal(MissionState.Paused, paused.State);
        Legal(voiceDone, paused);
        // A head is never counted as paused before it gets there.
        Assert.DoesNotContain("paused", TaskRollup.Sum(one.Heads).Line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void A_task_Pause_and_Resume_carry_each_head_back_to_where_it_was()
    {
        var task = Task(MissionState.Working,
            Head("Voice", HeadState.Working),
            Head("Subtitles", HeadState.Queued, "Voice"),
            Head("Notes", HeadState.Stopped) with { StoppedByHand = true, ResumeTo = HeadState.Working });
        var pausing = Done(MissionMachine.Pause(task, turnRunning: true));
        Assert.Equal(MissionState.Pausing, pausing.State);
        Assert.Equal(HeadState.Pausing, pausing.Heads[0].State);
        Assert.Equal(HeadState.Paused, pausing.Heads[1].State);
        Legal(task, pausing);
        var paused = Done(MissionMachine.TurnBoundary(pausing));
        Assert.Equal(MissionState.Paused, paused.State);
        var resumed = Done(MissionMachine.Resume(paused));
        Assert.Equal(MissionState.Working, resumed.State);
        Assert.Equal(HeadState.Working, resumed.Heads[0].State);
        Assert.Equal(HeadState.Queued, resumed.Heads[1].State);
        // A head stopped on its own stays stopped when nothing needs it.
        Assert.Equal(HeadState.Stopped, resumed.Heads[2].State);
        Legal(paused, resumed);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void A_task_Resume_brings_back_a_hand_stopped_head_the_task_cannot_go_on_without()
    {
        var task = Task(MissionState.Working,
            Head("Subtitles", HeadState.Stopped) with { StoppedByHand = true, ResumeTo = HeadState.Working },
            Head("Thumbnail", HeadState.Queued, "Subtitles"));
        var stopped = Done(MissionMachine.Stop(task, agentsOpen: false, StopCause.StopAll));
        var resumed = Done(MissionMachine.Resume(stopped));
        Assert.Equal(HeadState.Working, resumed.Heads[0].State);
        Assert.Equal(MissionState.Working, resumed.State);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Head_moves_follow_their_rules()
    {
        var task = Task(MissionState.Working, Head("Voice", HeadState.Working), Head("Subs", HeadState.Queued, "Voice"));
        // A queued head never starts before the heads it waits on are done.
        Refused(MissionMachine.StartHead(task, "Subs"), task);
        var voiceDone = Done(MissionMachine.FinishHead(task, "Voice"));
        var started = Done(MissionMachine.StartHead(voiceDone, "Subs"));
        Assert.Equal(HeadState.Working, started.Heads[1].State);
        // A head a reserve holds is resumed by its card, not by Resume.
        var held = task with { Heads = new ValueList<MissionHead>([Head("Voice", HeadState.Paused) with { HeldByReserve = true, ResumeTo = HeadState.Working }]) };
        Refused(MissionMachine.ResumeHead(held, "Voice"), held);
        // Unknown heads, and moves that mean nothing, are refused.
        Refused(MissionMachine.PauseHead(task, "nobody", turnRunning: true), task);
        var stoppingHead = task with { Heads = new ValueList<MissionHead>([Head("Voice", HeadState.Stopping)]) };
        Refused(MissionMachine.ResumeHead(stoppingHead, "Voice"), stoppingHead);
        Refused(MissionMachine.StopHead(stoppingHead, "Voice", agentOpen: true), stoppingHead);
        // A stopping head confirms into stopped.
        var stopping = Done(MissionMachine.StopHead(task, "Voice", agentOpen: true));
        Assert.Equal(HeadState.Stopping, stopping.Heads[0].State);
        var confirmed = Done(MissionMachine.ConfirmStopped(stopping, "Voice"));
        Assert.Equal(HeadState.Stopped, confirmed.Heads[0].State);
        Assert.Equal(MissionState.Stopped, confirmed.State);
        Legal(stopping, confirmed);
    }
}
