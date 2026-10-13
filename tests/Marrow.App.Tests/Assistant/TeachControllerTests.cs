using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10, 12.5, 12.6, 15.3; ED-NEW-dextra-as19-1, -4, -11): teach mode as the app holds it, from the recording to the draft.
// A recording starts and ends and is kept; Stop all ends it; the draft is made, its questions are cards on the queue and an answer changes the
// draft; the draft is edited and made again; the dry run writes nothing; the activity log gets ids and counts and never what was recorded.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-4")]
public sealed class TeachControllerTests : IDisposable
{
    private readonly TeachControllerRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    public async Task A_recording_starts_runs_and_ends_with_what_was_recorded_kept_and_two_lines_in_the_activity_log()
    {
        var id = await rig.RecordMail();
        var info = rig.Recordings.Info(id)!;
        Assert.Equal((TeachRecordingState.Recorded, TeachStopReason.Owner, 4), (info.State, info.Ended!.Value, info.Events));
        Assert.Null(rig.Controller.Active);
        Assert.Equal(["nth-dextra/teach-start", "nth-dextra/teach-stop"], rig.Audited.Select(a => a.Tool));
        Assert.All(rig.Audited, a => Assert.DoesNotContain("sam@example.com", a.Result, StringComparison.Ordinal));
        Assert.StartsWith($"recording {id}: Owner, 4 events", rig.Audited[1].Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_strip_goes_up_before_anything_is_watched_and_comes_down_when_the_recording_ends()
    {
        var armedAtShow = true;
        var hookedAtShow = true;
        rig.Indicator.OnShow = () =>
        {
            armedAtShow = rig.Stop.Armed;
            hookedAtShow = rig.Input?.Installed == true;
        };
        Assert.True(rig.Controller.Start().Started);
        Assert.False(armedAtShow);
        Assert.False(hookedAtShow);
        Assert.Equal((1, 0, true), (rig.Indicator.Shown, rig.Indicator.Hidden, rig.Indicator.Up));
        Assert.Same(rig.Controller, rig.Indicator.For);
        await rig.Controller.Stop();
        Assert.Equal((1, 1, false), (rig.Indicator.Shown, rig.Indicator.Hidden, rig.Indicator.Up));
    }

    [Fact]
    public async Task A_recording_that_cannot_show_its_strip_does_not_start_and_nothing_is_watched_or_kept()
    {
        rig.Indicator.CannotShow = true;
        var start = rig.Controller.Start();
        Assert.False(start.Started);
        Assert.Equal("nth could not show the recording strip, so it did not record. Nothing was kept.", start.Refusal);
        Assert.False(rig.Input?.Installed ?? false);
        Assert.Equal(0, rig.Input?.Starts ?? 0);
        Assert.False(rig.Stop.Armed);
        Assert.Empty(rig.Recordings.List(out _));
        Assert.Null(rig.Controller.Active);
        rig.Indicator.CannotShow = false;
        Assert.True(rig.Controller.Start().Started);
        await rig.Controller.Stop();
    }

    [Fact]
    public async Task A_recording_that_does_not_start_takes_the_strip_down_again()
    {
        rig.Host.Taken.Add(Marrow.Core.Port.PcControl.Pill.PcHotkey.StopDefault);
        Assert.False(rig.Controller.Start().Started);
        Assert.Equal((1, 1, false), (rig.Indicator.Shown, rig.Indicator.Hidden, rig.Indicator.Up));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Only_one_recording_runs_at_a_time_and_a_chord_another_app_holds_stops_one_from_starting()
    {
        Assert.True(rig.Controller.Start().Started);
        var second = rig.Controller.Start();
        Assert.False(second.Started);
        Assert.Equal("A recording is already running. Stop it first.", second.Refusal);
        await rig.Controller.Stop();
        rig.Host.Taken.Add(Marrow.Core.Port.PcControl.Pill.PcHotkey.StopDefault);
        var taken = rig.Controller.Start();
        Assert.False(taken.Started);
        Assert.StartsWith("Stop key unavailable", taken.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stop_all_ends_a_recording_through_the_assistants_stop_scope()
    {
        Assert.True(rig.Controller.Start().Started);
        var scope = (IStopScope)rig.Controller;
        Assert.Equal("teach", scope.Name);
        var recorder = rig.Controller.Active!;
        scope.Stop();
        var ended = await recorder.Stop();
        Assert.Equal(TeachStopReason.StopAll, ended.Ended);
        Assert.Null(rig.Controller.Active);
    }

    [Fact]
    public async Task Drafting_makes_the_draft_marks_the_send_step_keeps_the_recording_and_raises_the_question_as_a_card()
    {
        var id = await rig.RecordMail();
        var result = await rig.Controller.Draft(id);
        Assert.True(result.Ok, result.Problem);
        var draft = result.Draft!;
        Assert.Equal(["Click “New mail” (button) in Outlook", "Type “sam@example.com” into “To” (edit)", "Click “Send” (button) in Outlook"], draft.Steps.Select(s => s.Text));
        Assert.Equal(ActionClass.Outward, Assert.Single(draft.Guarded).Guard!.Class);
        Assert.Equal(TeachRecordingState.Drafted, rig.Recordings.Info(id)!.State);
        Assert.Equal(draft, rig.Recordings.Draft(id));
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Equal((DecisionKind.Question, DecisionWeight.Question), (card.Kind, card.Weight));
        Assert.StartsWith("At 0:05 you typed “sam@example.com”", card.Title, StringComparison.Ordinal);
        Assert.Equal(["It is always the same", "It changes: make it an input"], card.Options);
        Assert.Equal("Asks first: teaching", card.Rule);
        Assert.Contains("nth-dextra/teach-draft", rig.Audited.Select(a => a.Tool));
        Assert.All(rig.Audited, a => Assert.DoesNotContain("sam@example.com", a.Result, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Drafting_twice_opens_the_saved_draft_and_does_not_ask_the_model_again()
    {
        var model = new FakeDraftModel(_ => null);
        using var withModel = new TeachControllerRig(model);
        var id = await withModel.RecordMail();
        await withModel.Controller.Draft(id);
        await withModel.Controller.Draft(id);
        Assert.Single(model.Prompts);
        Assert.Single(withModel.Queue.Waiting);
    }

    [Fact]
    public async Task A_recording_with_nothing_in_it_a_running_one_and_one_already_a_skill_are_not_drafted()
    {
        Assert.True(rig.Controller.Start().Started);
        var running = rig.Controller.Active!.Id!;
        Assert.Equal("It is still recording. Stop it first.", (await rig.Controller.Draft(running)).Problem);
        await rig.Controller.Stop();
        Assert.Equal("Nothing was recorded, so there is nothing to draft. Delete it and teach again.", (await rig.Controller.Draft(running)).Problem);
        Assert.Equal("That recording is gone or cannot be read.", (await rig.Controller.Draft("t0000000000ff")).Problem);
        var saving = rig.Recordings.Create(rig.Clock.GetUtcNow(), TeachSource.Direct);
        rig.Recordings.SaveInfo(saving with { State = TeachRecordingState.Saved });
        Assert.Equal("It was already turned into a skill.", (await rig.Controller.Draft(saving.Id)).Problem);
    }

    [Fact]
    public async Task A_recording_from_a_newer_nth_is_not_drafted_edited_or_changed()
    {
        var id = await rig.RecordMail();
        var document = TeachJson.InfoDocument(rig.Recordings.Info(id)!);
        document["version"] = TeachRecordingInfo.CurrentVersion + 1;
        rig.Store.WriteTeachFile(TeachRig.Assistant, id, AssistantStore.TeachManifestFile, System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Equal("This recording was made by a newer nth. Open it there.", (await rig.Controller.Draft(id)).Problem);
        Assert.False(rig.Controller.SetKeep(id, true));
        Assert.Null(rig.Controller.Edit(id, d => d));
    }

    [Fact]
    public async Task Answering_a_card_changes_the_draft_and_the_card_is_done()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.Pick("It changes: make it an input")));
        var draft = rig.Recordings.Draft(id)!;
        Assert.Equal(new TeachParameter("to", "sam@example.com", "step 2"), Assert.Single(draft.Parameters));
        Assert.Equal("Type “{to}” into “To” (edit)", draft.Steps[1].Text);
        Assert.Empty(rig.Queue.Waiting);
        Assert.Empty(draft.OpenQuestions);
    }

    [Fact]
    public async Task Answering_in_the_drafts_own_view_withdraws_the_card()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        var card = Assert.Single(rig.Queue.Waiting);
        var updated = rig.Controller.Answer(id, "typed-2", "It is always the same");
        Assert.Empty(updated!.OpenQuestions);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(card.Id)!.State);
        Assert.Empty(rig.Queue.Waiting);
    }

    [Fact]
    public async Task Opening_a_draft_after_a_restart_raises_its_open_questions_again_once()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        var first = Assert.Single(rig.Queue.Waiting);
        rig.Controller.Open(id);
        Assert.Equal(first.Id, Assert.Single(rig.Queue.Waiting).Id);
        rig.Queue.Withdraw(first.Id, "gone");
        rig.Controller.Open(id);
        Assert.NotEqual(first.Id, Assert.Single(rig.Queue.Waiting).Id);
    }

    [Fact]
    public async Task Edits_are_saved_and_a_draft_made_again_throws_them_away_and_takes_back_the_old_cards()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        var edited = rig.Controller.Edit(id, d => d.Reword(3, "Press send").Flag(1, true) with { Name = "my-mail" })!;
        Assert.Equal(("Press send", true, "my-mail"), (edited.Steps[2].Text, edited.Steps[0].Flagged, edited.Name));
        Assert.Equal(edited, rig.Recordings.Draft(id));
        Assert.Equal(ActionClass.Outward, edited.Steps[2].Guard!.Class);
        var old = Assert.Single(rig.Queue.Waiting);
        var again = await rig.Controller.Redraft(id);
        Assert.Equal("Click “Send” (button) in Outlook", again.Draft!.Steps[2].Text);
        Assert.False(again.Draft.Steps[0].Flagged);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(old.Id)!.State);
        Assert.NotEqual(old.Id, Assert.Single(rig.Queue.Waiting).Id);
    }

    [Fact]
    public async Task A_password_typed_while_teaching_is_a_step_he_does_himself_and_is_nowhere_in_the_draft_or_the_log()
    {
        var id = await rig.RecordMail(password: true);
        var draft = (await rig.Controller.Draft(id)).Draft!;
        var step = draft.Steps.Single(s => s.Guard is { OwnerDoesIt: true });
        Assert.Contains("yourself", step.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", rig.Snapshot() + string.Join("", rig.Audited.Select(a => a.Result)) + step.Text, StringComparison.Ordinal);
        var raw = string.Concat(Directory.EnumerateFiles(rig.TeachRoot, "*", SearchOption.AllDirectories).Select(p => System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(p))));
        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_dry_run_lists_the_steps_in_would_words_and_writes_nothing_and_asks_nothing()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        var before = (rig.Snapshot(), rig.Audited.Count, rig.Queue.Items.Count, rig.Executor);
        var steps = rig.Controller.DryRun(id)!;
        var report = rig.Controller.DryRunReport(id)!;
        Assert.Equal((rig.Snapshot(), rig.Audited.Count, rig.Queue.Items.Count, rig.Executor), before);
        Assert.Equal(["Would click “New mail” (button) in Outlook", "Would type “sam@example.com” into “To” (edit)", "Would click “Send” (button) in Outlook"], steps.Select(s => s.Text));
        Assert.Equal(DryVerdict.WouldAskYou, steps[2].Verdict);
        Assert.StartsWith(TeachDryRun.Banner, report, StringComparison.Ordinal);
        Assert.EndsWith("1 of 3 steps would ask you.", report, StringComparison.Ordinal);
        Assert.Null(rig.Controller.DryRun("t0000000000ff"));
    }

    [Fact]
    public async Task Discarding_deletes_a_recording_and_takes_back_its_cards_and_refuses_one_that_is_running()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.True(rig.Controller.Discard(id));
        Assert.Null(rig.Recordings.Info(id));
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(card.Id)!.State);
        Assert.Contains("nth-dextra/teach-delete", rig.Audited.Select(a => a.Tool));
        Assert.True(rig.Controller.Start().Started);
        Assert.False(rig.Controller.Discard(rig.Controller.Active!.Id!));
        await rig.Controller.Stop();
    }

    [Fact]
    public async Task What_a_crash_left_running_is_settled_at_start_and_a_recording_nobody_saved_goes_after_two_weeks()
    {
        var crashed = rig.Recordings.Create(rig.Clock.GetUtcNow(), TeachSource.Direct);
        rig.Recordings.AppendEvent(crashed.Id, TeachRig.Click(1, 3));
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        var sweep = rig.Controller.Settle();
        Assert.Equal([crashed.Id], sweep.Interrupted);
        Assert.Equal(TeachRecordingState.Interrupted, rig.Recordings.Info(crashed.Id)!.State);
        Assert.Equal(1, rig.Recordings.Info(crashed.Id)!.Events);
        rig.Clock.Advance(TeachLimits.Retention + TimeSpan.FromDays(1));
        // nth was running: the daily look let both go; a start after that finds nothing left to do.
        Assert.Empty(rig.Controller.List(out _));
        Assert.Equal(2, rig.Audited.Count(a => a.Tool == "nth-dextra/teach-delete" && a.Result!.EndsWith(": expired", StringComparison.Ordinal)));
        Assert.Empty(rig.Controller.Settle().Deleted);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_running_nth_lets_go_of_recordings_past_their_two_weeks_once_a_day_and_not_before()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        rig.Clock.Advance(TeachLimits.Retention - TimeSpan.FromDays(1));
        Assert.NotNull(rig.Recordings.Info(id));
        rig.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Null(rig.Recordings.Info(id));
        Assert.Contains(rig.Audited, a => a.Tool == "nth-dextra/teach-delete" && a.Result == $"recording {id}: expired");
    }

    [Fact]
    public void The_daily_look_stops_when_the_controller_is_let_go()
    {
        var before = rig.Clock.ActiveTimers;
        rig.Controller.Dispose();
        Assert.Equal(before - 1, rig.Clock.ActiveTimers);
    }

    [Fact]
    public async Task Ending_the_app_while_recording_ends_the_recording_and_keeps_it()
    {
        Assert.True(rig.Controller.Start().Started);
        var id = rig.Controller.Active!.Id!;
        rig.Controller.Dispose();
        Assert.Equal(TeachStopReason.Closed, rig.Recordings.Info(id)!.Ended);
        Assert.Equal(TeachRecordingState.Recorded, rig.Recordings.Info(id)!.State);
        await Task.CompletedTask;
    }
}
