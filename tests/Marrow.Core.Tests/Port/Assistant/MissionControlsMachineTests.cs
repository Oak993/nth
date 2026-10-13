using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6b "Resume or Restart a stopped task", r4 B6, r4 G6; ED-NEW-dextra-as6-8): the moves AS-6 added to
// AS-1's machine. Resume all leaves a head the owner stopped on its own stopped, even one a waiting head needs; a task that
// was stopped after it failed offers Restart and Revert, never Resume, and Resume all skips it.
public sealed class MissionControlsMachineTests
{
    private static MissionHead Head(string id, HeadState state, params string[] dependsOn) =>
        new(id, id, state) { DependsOn = new ValueList<string>(dependsOn) };

    private static Mission Stopped(MissionState from, bool failed = false, params MissionHead[] heads) =>
        new(Guid.NewGuid(), "Next video", "dextra", MissionOrigin.Owner, MissionState.Stopped)
        {
            ResumeTo = failed ? MissionState.Failed : from,
            StoppedBy = StopCause.Owner,
            ChangedFiles = 3,
            Heads = new ValueList<MissionHead>(heads),
        };

    [Fact]
    [Trait("Feature", "AS-6")]
    [Trait("Decision", "ED-NEW-dextra-as6-8")]
    public void A_task_Resume_brings_back_a_hand_stopped_head_the_task_cannot_go_on_without_but_Resume_all_never_does()
    {
        var subtitles = Head("Subtitles", HeadState.Stopped) with { StoppedByHand = true, ResumeTo = HeadState.Working };
        var thumbnail = Head("Thumbnail", HeadState.Queued, "Subtitles");
        var task = new Mission(Guid.NewGuid(), "Next video", "dextra", MissionOrigin.Owner, MissionState.Stopped)
        {
            ResumeTo = MissionState.Working,
            StoppedBy = StopCause.Owner,
            Heads = new ValueList<MissionHead>([subtitles, thumbnail]),
        };
        var own = MissionMachine.Resume(task);
        Assert.True(own.Done);
        Assert.Equal(HeadState.Working, own.Mission.Heads.Single(h => h.Id == "Subtitles").State);
        Assert.Equal(MissionState.Working, own.Mission.State);

        var all = MissionMachine.Resume(task, resumeNeededHandStopped: false);
        Assert.True(all.Done);
        Assert.Equal(HeadState.Stopped, all.Mission.Heads.Single(h => h.Id == "Subtitles").State);
        Assert.True(all.Mission.Heads.Single(h => h.Id == "Subtitles").StoppedByHand);
        // Nothing can run, so the task is stopped again, with the head named.
        Assert.Equal(MissionState.Stopped, all.Mission.State);
        Assert.Equal("1 of 2 stopped · Thumbnail waits on Subtitles", TaskRollup.Sum(all.Mission.Heads).Line);
    }

    [Fact]
    [Trait("Feature", "AS-6")]
    [Trait("Decision", "ED-NEW-dextra-as6-8")]
    public void A_task_Resume_brings_back_every_hand_stopped_head_when_nothing_else_could_go_on_but_Resume_all_does_not()
    {
        var task = new Mission(Guid.NewGuid(), "Next video", "dextra", MissionOrigin.Owner, MissionState.Stopped)
        {
            ResumeTo = MissionState.Working,
            StoppedBy = StopCause.Owner,
            Heads = new ValueList<MissionHead>([
                Head("Writer", HeadState.Stopped) with { StoppedByHand = true, ResumeTo = HeadState.Working },
                Head("Voice", HeadState.Stopped) with { StoppedByHand = true, ResumeTo = HeadState.Working },
            ]),
        };
        var own = MissionMachine.Resume(task);
        Assert.True(own.Done);
        Assert.Equal([HeadState.Working, HeadState.Working], own.Mission.Heads.Select(h => h.State));
        Assert.Equal(MissionState.Working, own.Mission.State);
        var all = MissionMachine.Resume(task, resumeNeededHandStopped: false);
        Assert.Equal([HeadState.Stopped, HeadState.Stopped], all.Mission.Heads.Select(h => h.State));
        Assert.Equal(MissionState.Stopped, all.Mission.State);
    }

    [Fact]
    [Trait("Feature", "AS-6")]
    [Trait("Decision", "ED-NEW-dextra-as6-8")]
    public void A_task_stopped_after_it_failed_offers_Restart_and_Revert_never_Resume()
    {
        var stopped = Stopped(MissionState.Working, failed: true);
        Assert.True(stopped.StoppedAfterFailure);
        var slots = stopped.Slots;
        Assert.Equal((SlotA.Restart, true, SlotB.RevertChanges, true), (slots.A, slots.AEnabled, slots.B, slots.BEnabled));
        var resume = MissionMachine.Resume(stopped);
        Assert.False(resume.Done);
        Assert.Equal("It failed: Restart begins again from the brief.", resume.Refused);
        Assert.True(MissionMachine.Restart(stopped).Done);
        Assert.False(MissionMachine.ResumedByResumeAll(stopped with { StoppedBy = StopCause.StopAll }));
        // An ordinary stopped task still offers Resume while its changes are kept.
        var plain = Stopped(MissionState.Working);
        Assert.False(plain.StoppedAfterFailure);
        Assert.Equal((SlotA.Resume, SlotB.RevertChanges), (plain.Slots.A, plain.Slots.B));
        Assert.False(MissionMachine.Restart(plain).Done);
        // A failed task offers Restart and Stop (r4 G6), and Stop makes it stopped without any agent to wait for.
        var failed = new Mission(Guid.NewGuid(), "Next video", "dextra", MissionOrigin.Owner, MissionState.Failed);
        var cut = MissionMachine.Stop(failed, agentsOpen: false);
        Assert.True(cut.Done);
        Assert.Equal(MissionState.Stopped, cut.Mission.State);
        Assert.True(cut.Mission.StoppedAfterFailure);
    }
}
