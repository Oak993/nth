using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10, 12.6b; ED-NEW-dextra-as19-10): the Teach page in a headless window. The start button and the line under it; the recordings that wait
// with the day each deletes itself; a recording's draft with the steps that ask marked in words, the flag on a step that does not, the dry run, the
// trial with the owner, and Save; Delete that asks twice and Escape that takes the ask back; names for assistive technology; long text and a narrow window.
[Collection("Headless")]
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-10")]
public sealed class TeachViewTests(HeadlessSession headless) : IDisposable
{
    private readonly TeachControllerRig rig = new();

    public void Dispose() => rig.Dispose();

    private async Task<string> Drafted()
    {
        var id = await rig.RecordMail();
        await rig.Controller.Draft(id);
        return id;
    }

    private static Button Named(TeachView view, string name) =>
        view.Buttons.FirstOrDefault(b => AutomationProperties.GetName(b) == name)
        ?? throw new InvalidOperationException($"No button is named \"{name}\". The buttons are: " + string.Join(" | ", view.Buttons.Select(b => AutomationProperties.GetName(b))));

    private static bool Has(TeachView view, string name) => view.Buttons.Any(b => AutomationProperties.GetName(b) == name);

    [Fact]
    public void With_nothing_recorded_the_page_offers_to_start_and_says_nothing_is_waiting()
    {
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Assert.True(view.ShowsEmpty);
            Assert.Contains("Teach Dextra", view.Texts);
            var start = Named(view, "Start recording what you do");
            Assert.True(start.IsEnabled);
            Click(start);
            stage.Layout();
            Assert.NotNull(rig.Controller.Active);
            Assert.False(Named(view, "Start recording what you do").IsEnabled);
        });
    }

    [Fact]
    public void A_start_that_cannot_happen_says_why_under_the_button()
    {
        rig.Indicator.CannotShow = true;
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Click(Named(view, "Start recording what you do"));
            stage.Layout();
            Assert.Equal("nth could not show the recording strip, so it did not record. Nothing was kept.", view.StartStatusText);
        });
    }

    [Fact]
    public async Task A_recording_waits_in_the_list_with_when_it_deletes_itself_and_can_be_drafted()
    {
        var id = await rig.RecordMail();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Assert.False(view.ShowsEmpty);
            var row = Assert.Single(model.Rows);
            Assert.Equal(id, row.Info.Id);
            Assert.Equal("13 Oct, 09:30 · Recorded · 0:11", row.Title);
            Assert.Equal("4 steps · 0 pictures · deletes itself on 27 Oct", row.Detail);
            Assert.Contains(row.Title, view.Texts);
            Assert.True(Has(view, "Draft the recording of " + row.Title));
            Assert.False(Has(view, "Open the draft of " + row.Title));
        });
    }

    [Fact]
    public async Task Opening_a_draft_shows_the_steps_with_the_one_that_sends_marked_in_words()
    {
        var id = await Drafted();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            var title = model.Rows.Single().Title;
            Click(Named(view, "Open the draft of " + title));
            stage.Layout();
            Assert.Equal(id, model.SelectedId);
            Assert.Equal(["Click “New mail” (button) in Outlook", "Type “sam@example.com” into “To” (edit)", "Click “Send” (button) in Outlook"], view.StepBoxes.Select(b => b.Text));
            Assert.Contains(view.Texts, t => t.StartsWith("Asks you · sends: ", StringComparison.Ordinal));
            Assert.Contains("1 of 3 steps ask you first, whoever runs the skill.", view.Texts);
            Assert.Contains(view.Texts, t => t.StartsWith("At 0:05 you typed", StringComparison.Ordinal));
            Assert.Equal(3, view.StepBoxes.Count);
            Assert.Equal("Step 1", AutomationProperties.GetName(view.StepBoxes[0]));
        });
    }

    [Fact]
    public async Task A_step_that_does_not_ask_can_be_flagged_and_one_that_does_cannot()
    {
        await Drafted();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Assert.False(Has(view, "Pause here in the trial, step 3, off"));
            Click(Named(view, "Pause here in the trial, step 1, off"));
            stage.Layout();
            Assert.True(Has(view, "Pause here in the trial, step 1, on"));
            Assert.Contains(view.Texts, t => t.StartsWith("Pauses for you", StringComparison.Ordinal));
            Assert.Contains(view.Texts, t => t == "✓ Pause here in the trial");
            Assert.Equal(2, model.Draft!.Boundary.Count);
            Click(Named(view, "Pause here in the trial, step 1, on"));
            stage.Layout();
            Assert.Single(model.Draft!.Boundary);
        });
    }

    [Fact]
    public async Task A_step_is_reworded_in_its_own_box_and_taken_out_with_its_button_and_a_question_is_answered_with_a_press()
    {
        await Drafted();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            view.StepBoxes[0].Text = "Open a new message";
            view.StepBoxes[0].Focus();
            stage.Press(Key.Enter);
            Assert.Equal("Open a new message", model.Draft!.Steps[0].Text);
            Click(Named(view, "Answer “It changes: make it an input” to: At 0:05 you typed “sam@example.com”. Is it always the same?"));
            stage.Layout();
            Assert.Equal("to", Assert.Single(model.Draft!.Parameters).Name);
            Click(Named(view, "Take step 1 out of the skill"));
            stage.Layout();
            Assert.Equal(2, view.StepBoxes.Count);
        });
    }

    [Fact]
    public async Task The_dry_run_lists_the_would_steps_and_changes_nothing_on_disk()
    {
        await Drafted();
        var before = rig.Snapshot();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Click(Named(view, "List what the skill would do, without doing anything"));
            stage.Layout();
            Assert.Contains(TeachDryRun.Banner, view.Texts);
            Assert.Contains("3. Would click “Send” (button) in Outlook (would ask you)", view.Texts);
            Assert.Contains("1 of 3 steps would ask you.", view.Texts);
        });
        Assert.Equal(before, rig.Snapshot());
    }

    [Fact]
    public async Task The_trial_puts_each_step_to_the_owner_and_the_press_of_a_button_answers_it()
    {
        await Drafted();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Click(Named(view, "Try the skill with you watching"));
        });
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 1);
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Assert.Contains(view.Texts, t => t.StartsWith("Trial, step 1:", StringComparison.Ordinal));
            Click(Named(view, "I did it, step 1"));
        });
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 2);
        rig.Controller.StopTrial();
        await rig.Until(() => rig.Controller.Trial?.Result is not null);
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Assert.Contains("The trial was stopped.", view.Texts);
            Click(Named(view, "Close the trial"));
            stage.Layout();
            Assert.Null(rig.Controller.Trial);
        });
    }

    [Fact]
    public async Task Delete_asks_a_second_time_and_Escape_takes_the_ask_back()
    {
        var id = await rig.RecordMail();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            var title = model.Rows.Single().Title;
            Click(Named(view, "Delete the recording of " + title));
            stage.Layout();
            Assert.Equal(id, model.DeleteAsked);
            Assert.True(Has(view, "Delete for real the recording of " + title));
            Assert.True(Has(view, "Keep the recording of " + title));
            Named(view, "Delete for real the recording of " + title).Focus();
            stage.Press(Key.Escape);
            Assert.Null(model.DeleteAsked);
            Assert.True(Has(view, "Delete the recording of " + title));
            Click(Named(view, "Delete the recording of " + title));
            Click(Named(view, "Delete for real the recording of " + title));
            stage.Layout();
            Assert.True(view.ShowsEmpty);
            Assert.Null(rig.Recordings.Info(id));
        });
    }

    [Fact]
    public async Task Every_button_and_box_on_the_page_has_a_name_for_assistive_technology()
    {
        await Drafted();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Assert.All(view.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
            Assert.All(view.StepBoxes, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
            var names = view.Buttons.Select(b => AutomationProperties.GetName(b)).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        });
    }

    [Fact]
    public async Task Long_step_text_and_a_narrow_window_do_not_overflow_sideways()
    {
        await Drafted();
        rig.Controller.Edit(rig.Controller.List(out _).Single().Id, d => d.Reword(2, new string('w', 600)) with { Name = "a-very-long-name" });
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view, 375, 700);
            var scroll = (ScrollViewer)view.Child!;
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, $"{scroll.Extent.Width} > {scroll.Viewport.Width}");
        });
    }

    [Fact]
    public async Task Save_is_off_until_the_draft_can_be_saved_and_says_what_is_wrong()
    {
        await Drafted();
        headless.OnUiThread(() =>
        {
            using var model = new TeachModel(rig.Controller, TimeZoneInfo.Utc);
            model.Select(model.Rows.Single().Info.Id);
            var view = new TeachView(model);
            using var stage = new ReserveViewStage(view);
            Assert.True(Named(view, "Save the draft as a skill").IsEnabled);
            model.Describe("");
            stage.Layout();
            Assert.False(Named(view, "Save the draft as a skill").IsEnabled);
            Assert.Contains(view.Texts, t => t.StartsWith("Describe the skill in one line", StringComparison.Ordinal));
        });
    }
}
