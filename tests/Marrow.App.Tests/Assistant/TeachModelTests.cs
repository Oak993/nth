using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10; ED-NEW-dextra-as19-10): what the Teach page does, apart from how it draws. Drafting and saving are awaited and their outcome
// lands in the model; edits go to the saved draft; Delete asks twice; Start says why it did not start; a trial in front of the owner holds Start off.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-10")]
public sealed class TeachModelTests : IDisposable
{
    private readonly TeachControllerRig rig = new();

    public void Dispose() => rig.Dispose();

    private TeachModel Model() => new(rig.Controller, TimeZoneInfo.Utc, new InlineTeachWork());

    [Fact]
    public async Task Drafting_a_recording_opens_its_draft_and_the_row_changes_from_draft_it_to_open_it()
    {
        var id = await rig.RecordMail();
        using var model = Model();
        var row = Assert.Single(model.Rows);
        Assert.Equal((true, false, true), (row.CanDraft, row.CanOpen, row.CanDelete));
        await model.DraftAsync(id);
        Assert.Equal(id, model.SelectedId);
        Assert.Equal(3, model.Draft!.Steps.Count);
        Assert.Equal("", model.DraftStatus);
        row = Assert.Single(model.Rows);
        Assert.Equal((false, true, true), (row.CanDraft, row.CanOpen, row.CanDelete));
        Assert.False(model.Busy);
    }

    [Fact]
    public async Task A_recording_with_nothing_in_it_says_so_and_has_no_draft()
    {
        Assert.True(rig.Controller.Start().Started);
        var id = rig.Controller.Active!.Id!;
        await rig.Controller.Stop();
        using var model = Model();
        await model.DraftAsync(id);
        Assert.Null(model.Draft);
        Assert.Equal("Nothing was recorded, so there is nothing to draft. Delete it and teach again.", model.DraftStatus);
    }

    [Fact]
    public async Task Edits_go_to_the_saved_draft_and_the_name_is_made_a_valid_skill_name()
    {
        var id = await rig.RecordMail();
        using var model = Model();
        await model.DraftAsync(id);
        model.Rename("Send The Weekly Report!");
        model.Describe("  Sends\nthe report. ");
        model.Reword(2, "Address it");
        model.Flag(1, true);
        model.RemoveStep(3);
        var saved = rig.Recordings.Draft(id)!;
        Assert.Equal(("send-the-weekly-report", "Sends the report.", "Address it", true, 2), (saved.Name, saved.Description, saved.Steps[1].Text, saved.Steps[0].Flagged, saved.Steps.Count));
        Assert.Equal(saved, model.Draft);
    }

    // Lets the model's work run and waits for the page to be told it landed.
    private static async Task Landed(TeachModel model, Action release)
    {
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Heard() => told.TrySetResult();
        model.Changed += Heard;
        try
        {
            release();
            await told.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            model.Changed -= Heard;
        }
    }

    [Fact]
    public async Task File_work_waits_its_turn_and_lands_in_the_order_it_was_asked()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        var gate = new GatedTeachWork();
        using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc, gate);
        await gate.WaitFor(1);
        await Landed(model, () => gate.ReleaseNext()); // the first read of the list
        model.Select(id);
        await gate.WaitFor(1);
        await Landed(model, () => gate.ReleaseNext()); // the draft is opened
        var original = model.Draft!;
        model.Rename("first-name");
        model.Describe("Second words.");
        await gate.WaitFor(1);
        Assert.Equal(original, model.Draft);
        await Landed(model, () => gate.ReleaseNext());
        Assert.Equal(("first-name", original.Description), (model.Draft!.Name, model.Draft.Description));
        await gate.WaitFor(1);
        await Landed(model, () => gate.ReleaseNext());
        Assert.Equal(("first-name", "Second words."), (model.Draft!.Name, model.Draft.Description));
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_or_written_says_so_and_changes_nothing()
    {
        var id = await rig.RecordMail();
        using var model = Model();
        await model.DraftAsync(id);
        var before = model.Draft;
        rig.Store.Dispose();
        model.Rename("another-name");
        Assert.Equal(before, model.Draft);
        Assert.StartsWith("nth could not read or write the recording (ObjectDisposedException)", model.DraftStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answering_a_question_changes_the_draft_and_choosing_to_keep_is_remembered()
    {
        var id = await rig.RecordMail();
        using var model = Model();
        await model.DraftAsync(id);
        model.Answer("typed-2", "It changes: make it an input");
        Assert.Equal("to", Assert.Single(model.Draft!.Parameters).Name);
        Assert.False(model.Keep);
        model.SetKeep(true);
        Assert.True(model.Keep);
    }

    [Fact]
    public async Task Save_adds_the_skill_deletes_the_recording_and_says_so()
    {
        var id = await rig.RecordMail();
        using var model = Model();
        await model.DraftAsync(id);
        model.Rename("send-the-report");
        await model.SaveAsync();
        Assert.Equal("Saved as the skill “send-the-report”. The recording was deleted.", model.SaveStatus);
        Assert.Null(model.SelectedId);
        Assert.Null(model.Draft);
        Assert.Empty(model.Rows);
        Assert.Single(rig.Skills.Sources.List(CustomAddKind.Skills));
    }

    [Fact]
    public async Task Save_with_keep_leaves_the_recording_open_and_says_it_is_kept()
    {
        var id = await rig.RecordMail();
        using var model = Model();
        await model.DraftAsync(id);
        model.Rename("send-the-report");
        model.SetKeep(true);
        await model.SaveAsync();
        Assert.Equal("Saved as the skill “send-the-report”. The recording is kept.", model.SaveStatus);
        Assert.Equal(id, model.SelectedId);
        Assert.Contains("kept", Assert.Single(model.Rows).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_that_fails_says_why_and_keeps_the_draft()
    {
        var id = await rig.RecordMail();
        using var model = Model();
        await model.DraftAsync(id);
        model.Rename("send-the-report");
        rig.Skills.FailCommit = true;
        await model.SaveAsync();
        Assert.StartsWith("nth could not add the skill", model.SaveStatus, StringComparison.Ordinal);
        Assert.NotNull(model.Draft);
        Assert.Equal(id, model.SelectedId);
        Assert.False(model.Busy);
    }

    [Fact]
    public async Task Delete_asks_first_and_deletes_the_second_time_and_another_row_or_cancel_takes_the_ask_back()
    {
        var id = await rig.RecordMail();
        using var model = Model();
        model.Delete(id);
        Assert.Equal(id, model.DeleteAsked);
        Assert.NotEmpty(model.Rows);
        model.CancelDelete();
        Assert.Null(model.DeleteAsked);
        model.Delete(id);
        model.Delete(id);
        Assert.Empty(model.Rows);
        Assert.Null(rig.Recordings.Info(id));
    }

    [Fact]
    public async Task Start_says_why_it_did_not_start_and_is_off_while_recording_or_while_a_trial_is_open()
    {
        using var model = Model();
        Assert.True(model.CanStart);
        rig.Host.Taken.Add(Marrow.Core.Port.PcControl.Pill.PcHotkey.StopDefault);
        model.Start();
        Assert.StartsWith("Stop key unavailable", model.StartStatus, StringComparison.Ordinal);
        rig.Host.Taken.Clear();
        model.Start();
        Assert.Equal("", model.StartStatus);
        Assert.True(model.Recording);
        Assert.False(model.CanStart);
        await rig.Controller.Stop();
        Assert.True(model.CanStart);
        var id = await rig.RecordMail();
        await model.DraftAsync(id);
        model.StartTrial();
        await rig.Until(() => model.Trial?.Pending is not null);
        Assert.False(model.CanStart);
        Assert.False(model.CanSave);
        model.StopTrial();
        await rig.Until(() => model.Trial?.Result is not null);
        model.CloseTrial();
        Assert.True(model.CanStart);
    }

    [Fact]
    public async Task Changes_to_the_recordings_reach_the_model_and_a_recording_that_goes_away_closes_the_draft()
    {
        using var model = Model();
        var raised = 0;
        model.Changed += () => raised++;
        var id = await rig.RecordMail();
        Assert.True(raised > 0);
        await model.DraftAsync(id);
        Assert.NotNull(model.Draft);
        rig.Controller.Discard(id);
        Assert.Null(model.SelectedId);
        Assert.Null(model.Draft);
    }

    [Fact]
    public async Task The_words_for_a_recording_say_when_it_started_how_it_stands_and_when_it_goes()
    {
        var id = await rig.RecordMail();
        var info = rig.Recordings.Info(id)!;
        Assert.Equal("13 Oct, 09:30 · Recorded · 0:11", TeachViewWords.Row(info, TimeZoneInfo.Utc));
        Assert.Equal("4 steps · 0 pictures · deletes itself on 27 Oct", TeachViewWords.Detail(info, TimeZoneInfo.Utc));
        Assert.Equal("4 steps · 0 pictures · kept", TeachViewWords.Detail(info with { State = TeachRecordingState.Saved, Keep = true }, TimeZoneInfo.Utc));
        Assert.Equal("4 steps · 0 pictures · made by a newer nth", TeachViewWords.Detail(info with { Version = 9 }, TimeZoneInfo.Utc));
        Assert.Equal("Asks you · sends", TeachViewWords.Mark(new TeachStep(1, "x") { Guard = new TeachGuardFinding(ActionClass.Outward, "r") }));
        Assert.Equal("You do it yourself", TeachViewWords.Mark(new TeachStep(1, "x") { Guard = new TeachGuardFinding(ActionClass.HardDeny, "r") }));
        Assert.Equal("Pauses for you", TeachViewWords.Mark(new TeachStep(1, "x") { Flagged = true }));
        Assert.Equal("", TeachViewWords.Mark(new TeachStep(1, "x")));
    }
}
