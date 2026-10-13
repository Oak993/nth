using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 steps 1, 5 and 6, 15.3; ED-NEW-dextra-as19-1, -7): one assistant's recordings over the store. What the
// owner did is appended and read back in order, an unreadable line is skipped and counted, a crash leaves a recording Interrupted with its
// counts worked out again, a saved recording that was not kept is deleted the rest of the way, and one nobody saved is deleted after its
// retention, with the date in the manifest. A recording a newer nth wrote is read and never written.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-7")]
public sealed class TeachRecordingsTests : IDisposable
{
    private readonly TeachRig rig = new();

    public void Dispose() => rig.Dispose();

    private TeachRecordingInfo Made() => rig.Recordings.Create(rig.Clock.GetUtcNow(), TeachSource.Direct);

    [Fact]
    public void A_new_recording_has_a_folder_and_a_manifest_in_the_recording_state()
    {
        var info = Made();
        Assert.True(AssistantStore.IsId(info.Id));
        Assert.Equal(TeachRecordingState.Recording, info.State);
        Assert.Equal(info, rig.Recordings.Info(info.Id));
        Assert.Equal([info], rig.Recordings.List(out var unreadable));
        Assert.Empty(unreadable);
    }

    [Fact]
    public void Events_narration_and_frames_are_appended_and_read_back_in_order()
    {
        var info = Made();
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(2, 8));
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(1, 3));
        rig.Recordings.AppendNarration(info.Id, new TeachNarrationLine(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "later"));
        rig.Recordings.AppendNarration(info.Id, new TeachNarrationLine(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "first"));
        var name = rig.Recordings.WriteFrame(info.Id, 7, TimeSpan.FromSeconds(3), TeachRig.Jpeg(100));
        Assert.Equal("frames/0007.jpg", name);
        Assert.Equal([1, 2], rig.Recordings.Events(info.Id, out var skipped).Select(e => e.Seq));
        Assert.Equal(0, skipped);
        Assert.Equal(["first", "later"], rig.Recordings.Narration(info.Id).Select(n => n.Text));
        Assert.Equal(TeachRig.Jpeg(100), rig.Recordings.Frame(info.Id, name));
        Assert.Null(rig.Recordings.Frame(info.Id, "recording.json"));
    }

    [Fact]
    public void A_line_that_does_not_read_is_skipped_and_counted()
    {
        var info = Made();
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(1, 3));
        rig.Store.AppendTeachLine(TeachRig.Assistant, info.Id, AssistantStore.TeachEventsFile, "this is not json");
        rig.Store.AppendTeachLine(TeachRig.Assistant, info.Id, AssistantStore.TeachEventsFile, "{\"seq\":9}");
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(2, 5));
        Assert.Equal([1, 2], rig.Recordings.Events(info.Id, out var skipped).Select(e => e.Seq));
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void A_draft_is_saved_and_read_back_and_a_draft_from_a_newer_build_is_not_written_over()
    {
        var info = Made();
        var draft = TeachDraftBuilder.Skeleton(info, [TeachRig.Click(1, 3)], []);
        rig.Recordings.SaveDraft(info.Id, draft);
        Assert.Equal(draft with { RecordingId = info.Id }, rig.Recordings.Draft(info.Id));
        Assert.Throws<InvalidOperationException>(() => rig.Recordings.SaveDraft(info.Id, draft with { Version = TeachDraft.CurrentVersion + 1 }));
    }

    [Fact]
    public void A_manifest_from_a_newer_build_is_read_and_never_written()
    {
        var info = Made();
        Assert.Throws<InvalidOperationException>(() => rig.Recordings.SaveInfo(info with { Version = TeachRecordingInfo.CurrentVersion + 1 }));
    }

    [Fact]
    public void A_manifest_that_does_not_read_is_listed_as_a_problem_and_left_where_it_is()
    {
        var good = Made();
        rig.Store.WriteTeachFile(TeachRig.Assistant, "t000000000009", AssistantStore.TeachManifestFile, "{ nope"u8.ToArray());
        Assert.Equal([good.Id], rig.Recordings.List(out var unreadable).Select(i => i.Id));
        Assert.Equal(["t000000000009"], unreadable);
        Assert.Null(rig.Recordings.Info("t000000000009"));
        Assert.True(rig.Store.TeachRecordingExists(TeachRig.Assistant, "t000000000009"));
    }

    [Fact]
    public void Recounting_works_the_counts_out_again_from_the_files()
    {
        var info = Made();
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(1, 3));
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(2, 90));
        rig.Recordings.WriteFrame(info.Id, 1, TimeSpan.FromSeconds(3), TeachRig.Jpeg(50));
        rig.Recordings.WriteFrame(info.Id, 2, TimeSpan.FromSeconds(90), TeachRig.Jpeg(70));
        rig.Recordings.AppendNarration(info.Id, new TeachNarrationLine(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(95), "a long thing"));
        var counted = rig.Recordings.Recount(info);
        Assert.Equal((2, 2, 1, 120L), (counted.Events, counted.Frames, counted.Narration, counted.FrameBytes));
        Assert.Equal(TimeSpan.FromSeconds(95), counted.Elapsed);
    }

    [Fact]
    public void A_recording_a_crash_left_running_becomes_interrupted_with_what_was_written_kept()
    {
        var info = Made();
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(1, 3));
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(2, 20));
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        var sweep = rig.Recordings.Sweep(rig.Clock.GetUtcNow());
        Assert.Equal([info.Id], sweep.Interrupted);
        var after = rig.Recordings.Info(info.Id)!;
        Assert.Equal((TeachRecordingState.Interrupted, TeachStopReason.Closed, 2), (after.State, after.Ended!.Value, after.Events));
        Assert.Equal(TimeSpan.FromSeconds(20), after.Elapsed);
        Assert.Equal(rig.Clock.GetUtcNow(), after.Changed);
        Assert.Equal(2, rig.Recordings.Events(info.Id, out _).Count);
    }

    [Fact]
    public void The_recording_that_is_live_in_this_process_is_not_touched_by_a_sweep()
    {
        var info = Made();
        var sweep = rig.Recordings.Sweep(rig.Clock.GetUtcNow(), live: info.Id);
        Assert.Empty(sweep.Interrupted);
        Assert.Equal(TeachRecordingState.Recording, rig.Recordings.Info(info.Id)!.State);
    }

    [Fact]
    public void A_recording_nobody_saved_is_deleted_after_its_retention_and_not_a_day_before()
    {
        var info = Made();
        rig.Recordings.SaveInfo(info with { State = TeachRecordingState.Recorded });
        Assert.Equal(info.Started + TeachLimits.Retention, rig.Recordings.Info(info.Id)!.ExpiresAt);
        rig.Clock.Advance(TeachLimits.Retention - TimeSpan.FromDays(1));
        Assert.Empty(rig.Recordings.Sweep(rig.Clock.GetUtcNow()).Deleted);
        Assert.NotNull(rig.Recordings.Info(info.Id));
        rig.Clock.Advance(TimeSpan.FromDays(1));
        var sweep = rig.Recordings.Sweep(rig.Clock.GetUtcNow());
        Assert.Equal([(info.Id, "expired")], sweep.Deleted);
        Assert.Null(rig.Recordings.Info(info.Id));
        Assert.False(rig.Store.TeachRecordingExists(TeachRig.Assistant, info.Id));
    }

    [Fact]
    public void A_change_to_a_recording_moves_its_retention_on()
    {
        var info = Made();
        rig.Recordings.SaveInfo(info with { State = TeachRecordingState.Drafted });
        rig.Clock.Advance(TimeSpan.FromDays(10));
        rig.Recordings.SaveInfo(rig.Recordings.Info(info.Id)! with { Changed = rig.Clock.GetUtcNow() });
        rig.Clock.Advance(TimeSpan.FromDays(10));
        Assert.Empty(rig.Recordings.Sweep(rig.Clock.GetUtcNow()).Deleted);
    }

    [Fact]
    public void A_saved_recording_that_was_not_kept_is_deleted_the_rest_of_the_way_and_a_kept_one_stays_for_good()
    {
        var gone = Made();
        var kept = Made();
        rig.Recordings.AppendEvent(gone.Id, TeachRig.Click(1, 3));
        rig.Recordings.SaveInfo(gone with { State = TeachRecordingState.Saved, SkillName = "x" });
        rig.Recordings.SaveInfo(kept with { State = TeachRecordingState.Saved, Keep = true });
        rig.Clock.Advance(TimeSpan.FromDays(400));
        var sweep = rig.Recordings.Sweep(rig.Clock.GetUtcNow());
        Assert.Equal([(gone.Id, "saved")], sweep.Deleted);
        Assert.False(rig.Store.TeachRecordingExists(TeachRig.Assistant, gone.Id));
        Assert.Null(rig.Recordings.Info(kept.Id)!.ExpiresAt);
        Assert.True(rig.Store.TeachRecordingExists(TeachRig.Assistant, kept.Id));
    }

    [Fact]
    public void A_recording_being_saved_and_one_from_a_newer_build_are_never_deleted_by_a_sweep()
    {
        var saving = Made();
        rig.Recordings.SaveInfo(saving with { State = TeachRecordingState.Saving, SkillName = "x" });
        var newer = Made();
        var document = TeachJson.InfoDocument(newer with { State = TeachRecordingState.Recorded });
        document["version"] = TeachRecordingInfo.CurrentVersion + 1;
        rig.Store.WriteTeachFile(TeachRig.Assistant, newer.Id, AssistantStore.TeachManifestFile, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        rig.Clock.Advance(TimeSpan.FromDays(400));
        var sweep = rig.Recordings.Sweep(rig.Clock.GetUtcNow());
        Assert.Empty(sweep.Deleted);
        Assert.Empty(sweep.Interrupted);
        Assert.NotNull(rig.Recordings.Info(saving.Id));
        Assert.True(rig.Recordings.Info(newer.Id)!.Newer);
    }

    [Fact]
    public void Deleting_a_recording_removes_every_file_of_it()
    {
        var info = Made();
        rig.Recordings.AppendEvent(info.Id, TeachRig.Click(1, 3));
        rig.Recordings.WriteFrame(info.Id, 1, TimeSpan.FromSeconds(3), TeachRig.Jpeg());
        rig.Recordings.SaveDraft(info.Id, TeachDraftBuilder.Skeleton(info, [TeachRig.Click(1, 3)], []));
        Assert.True(rig.Recordings.Delete(info.Id));
        Assert.False(Directory.Exists(rig.RecordingFolder(info.Id)));
        Assert.Empty(rig.Recordings.List(out _));
        Assert.False(rig.Recordings.Delete(info.Id));
    }
}
