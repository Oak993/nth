using System.Text;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 steps 5 and 6, 15.3; ED-NEW-dextra-as19-7): the end of teach mode. An approved draft becomes an own skill, and the raw
// recording (events, frames, narration, draft, manifest) is deleted unless the owner keeps it. A save records its intent before the skill and its
// outcome after it, so a save nth was cut off in is settled at the next start: the skill's recording is deleted the rest of the way, or the
// draft comes back to be approved again. A save that fails leaves the recording exactly as it was.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-7")]
public sealed class TeachControllerSaveTests : IDisposable
{
    private readonly TeachControllerRig rig = new();

    public void Dispose() => rig.Dispose();

    private async Task<string> Drafted(string? name = null)
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        rig.Controller.Edit(id, d => d with { Name = name ?? "send-the-report" });
        return id;
    }

    private IReadOnlyList<string> Stagings() =>
        Directory.Exists(Path.Combine(rig.TeachRoot, TeachRig.Assistant)) ? [.. Directory.EnumerateDirectories(Path.Combine(rig.TeachRoot, TeachRig.Assistant), ".stage-*")] : [];

    [Fact]
    public async Task Approving_the_draft_adds_the_skill_and_deletes_the_raw_recording()
    {
        var id = await Drafted();
        var saved = await rig.Controller.Save(id, keep: false);
        Assert.True(saved.Ok, saved.Problem);
        var item = Assert.Single(rig.Skills.Sources.List(CustomAddKind.Skills));
        Assert.Equal((saved.ItemId, "send-the-report", true), (item.Id, item.Name, item.Enabled));
        var text = Encoding.UTF8.GetString(rig.Skills.Installed[item.Id]["payload/SKILL.md"]);
        Assert.Contains("3. **Asks you (sends).** Click “Send” (button) in Outlook\n", text, StringComparison.Ordinal);
        Assert.Null(rig.Recordings.Info(id));
        Assert.False(Directory.Exists(Path.Combine(rig.TeachRoot, TeachRig.Assistant, id)));
        Assert.Empty(rig.Controller.List(out _));
        Assert.Empty(Stagings());
        Assert.Equal(1, rig.SkillsChanges);
        Assert.Empty(rig.Queue.Waiting);
    }

    [Fact]
    public async Task The_activity_log_gets_the_skill_and_the_ids_and_never_what_was_recorded()
    {
        var id = await Drafted();
        var saved = await rig.Controller.Save(id, keep: false);
        var line = rig.Audited.Single(a => a.Tool == "nth-dextra/teach-save");
        Assert.Equal($"recording {id}: skill send-the-report ({saved.ItemId}), recording deleted", line.Result);
        Assert.All(rig.Audited, a => Assert.DoesNotContain("sam@example.com", a.Result, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Keeping_the_recording_keeps_everything_and_marks_it_saved_for_good()
    {
        var id = await Drafted();
        Assert.True(rig.Controller.SetKeep(id, true));
        var saved = await rig.Controller.Save(id, keep: true);
        Assert.True(saved.Ok);
        var info = rig.Recordings.Info(id)!;
        Assert.Equal((TeachRecordingState.Saved, true, saved.ItemId, "send-the-report"), (info.State, info.Keep, info.SkillId, info.SkillName));
        Assert.Null(info.ExpiresAt);
        Assert.NotEmpty(rig.Recordings.Events(id, out _));
        Assert.Contains("recording kept", rig.Audited.Single(a => a.Tool == "nth-dextra/teach-save").Result, StringComparison.Ordinal);
        rig.Clock.Advance(TimeSpan.FromDays(400));
        rig.Controller.Settle();
        Assert.NotNull(rig.Recordings.Info(id));
        Assert.Empty(Stagings());
        Assert.False(rig.Controller.SetKeep(id, false));
    }

    [Fact]
    public async Task A_draft_with_a_problem_is_not_saved_and_the_recording_is_untouched()
    {
        var id = await Drafted("Bad Name");
        var saved = await rig.Controller.Save(id, keep: false);
        Assert.False(saved.Ok);
        Assert.StartsWith("Name the skill with lowercase letters", saved.Problem, StringComparison.Ordinal);
        Assert.Equal(TeachRecordingState.Drafted, rig.Recordings.Info(id)!.State);
        Assert.Empty(rig.Skills.Sources.List(CustomAddKind.Skills));
    }

    [Fact]
    public async Task A_name_another_skill_has_leaves_the_recording_a_draft_with_the_reason()
    {
        var first = await Drafted();
        Assert.True((await rig.Controller.Save(first, keep: false)).Ok);
        var second = await Drafted();
        var saved = await rig.Controller.Save(second, keep: true);
        Assert.False(saved.Ok);
        Assert.True(saved.NameTaken);
        var info = rig.Recordings.Info(second)!;
        Assert.Equal(TeachRecordingState.Drafted, info.State);
        Assert.Null(info.SkillName);
        Assert.Single(rig.Skills.Sources.List(CustomAddKind.Skills));
        Assert.Empty(Stagings());
        Assert.NotNull(rig.Recordings.Draft(second));
    }

    [Fact]
    public async Task A_store_that_cannot_commit_leaves_the_recording_a_draft()
    {
        var id = await Drafted();
        rig.Skills.FailCommit = true;
        var saved = await rig.Controller.Save(id, keep: false);
        Assert.False(saved.Ok);
        Assert.StartsWith("nth could not add the skill", saved.Problem, StringComparison.Ordinal);
        Assert.Equal(TeachRecordingState.Drafted, rig.Recordings.Info(id)!.State);
        Assert.Empty(rig.Skills.Installed);
        Assert.Equal(0, rig.SkillsChanges);
    }

    [Fact]
    public async Task A_cancelled_save_leaves_the_recording_a_draft()
    {
        var id = await Drafted();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Controller.Save(id, keep: false, cancel.Token));
        Assert.Equal(TeachRecordingState.Drafted, rig.Recordings.Info(id)!.State);
        Assert.Empty(Stagings());
    }

    [Fact]
    public async Task Saving_needs_a_draft_a_recording_that_reads_and_the_own_skill_service()
    {
        var id = await rig.RecordMail();
        Assert.Equal("Draft it first.", (await rig.Controller.Save(id, keep: false)).Problem);
        Assert.Equal("That recording is gone or cannot be read.", (await rig.Controller.Save("t0000000000ff", keep: false)).Problem);
        using var without = new TeachControllerRig(skills: false);
        var other = await without.RecordMail();
        await without.Controller.Draft(other);
        Assert.Equal("Saving skills is not available right now.", (await without.Controller.Save(other, keep: false)).Problem);
    }

    [Fact]
    public async Task A_recording_is_saved_once_a_second_save_finds_it_gone()
    {
        var id = await Drafted();
        Assert.True((await rig.Controller.Save(id, keep: true)).Ok);
        Assert.Equal("It was already turned into a skill.", (await rig.Controller.Save(id, keep: true)).Problem);
        Assert.Single(rig.Skills.Sources.List(CustomAddKind.Skills));
    }

    [Fact]
    public async Task A_save_nth_was_cut_off_in_after_the_skill_was_added_is_finished_at_the_next_start()
    {
        var id = await Drafted();
        var draft = rig.Recordings.Draft(id)!;
        // What the cut-off save did: wrote "Saving" with the name, added the skill from the recording's staging folder... and nothing more.
        rig.Recordings.SaveInfo(rig.Recordings.Info(id)! with { State = TeachRecordingState.Saving, SkillName = draft.Name });
        var stage = rig.Store.NewTeachStaging(TeachRig.Assistant, id);
        Assert.True((await new TeachSkillWriter(rig.Skills.Sources, rig.Clock).Save(draft, stage, CancellationToken.None)).Ok);
        rig.Store.DeleteTeachStaging(TeachRig.Assistant, stage);
        rig.Controller.Settle();
        Assert.Null(rig.Recordings.Info(id));
        Assert.Single(rig.Skills.Sources.List(CustomAddKind.Skills));
        Assert.Contains(rig.Audited, a => a.Tool == "nth-dextra/teach-save");
        Assert.Equal(1, rig.SkillsChanges);
    }

    [Fact]
    public async Task A_save_cut_off_before_the_skill_was_added_brings_the_draft_back_to_be_approved_again()
    {
        var id = await Drafted();
        rig.Recordings.SaveInfo(rig.Recordings.Info(id)! with { State = TeachRecordingState.Saving, SkillName = "send-the-report" });
        rig.Store.NewTeachStaging(TeachRig.Assistant, id);
        rig.Controller.Settle();
        var info = rig.Recordings.Info(id)!;
        Assert.Equal(TeachRecordingState.Drafted, info.State);
        Assert.Null(info.SkillName);
        Assert.Empty(Stagings());
        Assert.Empty(rig.Skills.Sources.List(CustomAddKind.Skills));
        Assert.True((await rig.Controller.Save(id, keep: false)).Ok);
    }

    [Fact]
    public async Task A_skill_of_the_same_name_that_is_not_this_recordings_does_not_settle_the_save()
    {
        var other = await Drafted();
        Assert.True((await rig.Controller.Save(other, keep: false)).Ok);
        var id = await Drafted();
        rig.Recordings.SaveInfo(rig.Recordings.Info(id)! with { State = TeachRecordingState.Saving, SkillName = "send-the-report" });
        rig.Controller.Settle();
        Assert.Equal(TeachRecordingState.Drafted, rig.Recordings.Info(id)!.State);
        Assert.Single(rig.Skills.Sources.List(CustomAddKind.Skills));
    }

    [Fact]
    public async Task A_recording_being_saved_cannot_be_discarded()
    {
        var id = await Drafted();
        rig.Recordings.SaveInfo(rig.Recordings.Info(id)! with { State = TeachRecordingState.Saving, SkillName = "x" });
        Assert.False(rig.Controller.Discard(id));
        Assert.NotNull(rig.Recordings.Info(id));
    }

    [Fact]
    public async Task A_crash_between_the_skill_and_the_delete_is_finished_by_the_next_sweep()
    {
        var id = await Drafted();
        rig.Recordings.SaveInfo(rig.Recordings.Info(id)! with { State = TeachRecordingState.Saved, SkillId = "abc", SkillName = "send-the-report", Keep = false });
        rig.Controller.Settle();
        Assert.Null(rig.Recordings.Info(id));
        Assert.False(Directory.Exists(Path.Combine(rig.TeachRoot, TeachRig.Assistant, id)));
    }
}
