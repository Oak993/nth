using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.3, 8.4; ED-NEW-dextra-as12-20): the Memory view in a headless window. Notes by assistant and topic; the notes that
// wait with Keep; Edit, Delete (asked once more) and Source inside the row, Escape closing each and putting the focus back; a note the
// owner writes; "forget everything about" with the notes shown first; names for assistive technology; long text, a narrow window and
// right-to-left text without overflow.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-as12-20")]
[Trait("Feature", "AS-12")]
public sealed class MemoryViewTests(HeadlessSession headless) : IDisposable
{
    private readonly MemoryRig rig = new();

    public void Dispose() => rig.Dispose();

    private MemoryModel Model() => new(rig.Memory, id => id switch { MemoryRig.Main => "Dextra", MemoryRig.Astur => "Shop", _ => id }, () => MemoryRig.Main, new MemoryImport(rig.Memory, () => MemoryRig.Main), () => [], TimeZoneInfo.Utc);

    private string Add(string text, NoteKind kind = NoteKind.Fact, string scope = MemoryRig.Main, string? topic = null) =>
        rig.Memory.Propose(MemoryRig.Clean(text, kind, scope, topic)).Note!.Id;

    private void Fill()
    {
        Add("The owner is based in Pune", NoteKind.Fact, MemoryScope.Shared, "About you");
        Add("The owner prefers short answers", NoteKind.Preference, MemoryScope.Shared, "Writing style");
        Add("Releases go out on Fridays", NoteKind.Procedure, MemoryRig.Main, "Releases");
        Add("The shop price list is in the red binder", NoteKind.Fact, MemoryRig.Astur, "Prices");
        rig.Memory.Propose(MemoryRig.FromWeb("Page says the vault is in the red folder"));
    }

    private static void Press(ReserveViewStage stage, Key key) => stage.Press(key);

    [Fact]
    public void The_view_lists_notes_by_assistant_and_topic_and_the_waiting_ones_apart_with_Keep()
    {
        headless.OnUiThread(() =>
        {
            Fill();
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            Assert.Equal("4 notes · 1 waiting for you", view.CountText);
            Assert.Equal([
                "About you · 2", "Every assistant reads these.", "About you", "  - The owner is based in Pune", "Writing style", "  - The owner prefers short answers",
                "Dextra · 1", "Releases", "  - Releases go out on Fridays",
                "Shop · 1", "Prices", "  - The shop price list is in the red binder",
            ], Outline(view));
            Assert.True(view.ShowsWaiting);
            var waiting = Assert.Single(view.WaitingRows);
            Assert.Equal("Page says the vault is in the red folder", waiting.Note.Text);
            Assert.NotNull(waiting.KeepButton);
            Assert.Null(waiting.EditButton);
            Assert.Contains("Drawn from a web page", string.Join('\n', Texts(waiting)), StringComparison.Ordinal);
            Assert.False(view.ShowsEmpty);
            Click(view.KeepAllButton!);
            stage.Layout();
            Assert.False(view.ShowsWaiting);
            Assert.Equal("5 notes", view.CountText);
            Assert.Equal("Kept 1 note. It is used from now on.", view.StatusText);
        });
    }

    private static string[] Outline(MemoryView view) => [.. view.Outline.Select(line => line.StartsWith("  - ", StringComparison.Ordinal) ? line : line.TrimStart(' '))];

    [Fact]
    public void An_empty_memory_says_so()
    {
        headless.OnUiThread(() =>
        {
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            Assert.True(view.ShowsEmpty);
            Assert.Equal("0 notes", view.CountText);
            Assert.False(view.ShowsWaiting);
            Assert.Empty(view.NoteRows);
            Assert.Contains("Nothing is remembered yet", Texts(view).Single(t => t.StartsWith("Nothing is remembered", StringComparison.Ordinal)), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Edit_opens_inside_the_row_saves_on_Save_and_Escape_closes_it_and_puts_the_focus_back_on_Edit()
    {
        headless.OnUiThread(() =>
        {
            var id = Add("The owner lives in Pune");
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            var row = Assert.Single(view.NoteRows);
            Click(row.EditButton!);
            stage.Layout();
            var edit = Assert.Single(view.NoteRows);
            Assert.NotNull(edit.EditBox);
            Assert.Equal("The owner lives in Pune", edit.EditBox!.Text);
            Assert.Equal(MemoryRules.MaxNoteChars, edit.EditBox.MaxLength);
            Assert.Equal("Edit the note: The owner lives in Pune", AutomationProperties.GetName(edit.EditBox));

            // Escape in the row closes the edit and the focus goes to the Edit button of the same note.
            edit.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            stage.Layout();
            Assert.Null(model.EditingId);
            Assert.True(Assert.Single(view.NoteRows).EditButton!.IsFocused);

            Click(view.NoteRows[0].EditButton!);
            stage.Layout();
            view.NoteRows[0].EditBox!.Text = "The owner lives in Mumbai";
            Click(view.NoteRows[0].SaveButton!);
            stage.Layout();
            Assert.Equal("The owner lives in Mumbai", rig.Memory.Find(id)!.Text);
            Assert.Equal(["The owner lives in Mumbai"], view.NoteRows.Select(r => r.Note.Text));
            Assert.Equal("Saved.", view.StatusText);
            Assert.True(view.NoteRows[0].EditButton!.IsFocused);
        });
    }

    [Fact]
    public void A_refused_edit_stays_open_and_says_why_in_a_live_region()
    {
        headless.OnUiThread(() =>
        {
            Add("The owner lives in Pune");
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            Click(view.NoteRows[0].EditButton!);
            stage.Layout();
            view.NoteRows[0].EditBox!.Text = "The password is hunter2horse";
            Click(view.NoteRows[0].SaveButton!);
            stage.Layout();
            var row = view.NoteRows[0];
            Assert.NotNull(row.EditBox);
            Assert.True(row.EditProblem!.IsVisible);
            Assert.Contains("Secrets are never stored", row.EditProblem.Text, StringComparison.Ordinal);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(row.EditProblem));
            Assert.Equal("The owner lives in Pune", rig.Memory.Notes(null).Single().Text);
        });
    }

    [Fact]
    public void Delete_asks_inside_the_row_Escape_cancels_and_the_second_press_deletes_for_real()
    {
        headless.OnUiThread(() =>
        {
            var id = Add("The zebrafish tank needs its filter cleaned");
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            Click(view.NoteRows[0].DeleteButton);
            stage.Layout();
            var row = Assert.Single(view.NoteRows);
            Assert.NotNull(row.ConfirmDeleteButton);
            Assert.Contains(Texts(row), t => t.StartsWith("Delete this note for real?", StringComparison.Ordinal));
            Assert.Equal("Yes, delete the note for real: The zebrafish tank needs its filter cleaned", AutomationProperties.GetName(row.ConfirmDeleteButton!));
            Assert.NotNull(rig.Memory.Find(id));
            row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            stage.Layout();
            Assert.Null(model.ConfirmDeleteId);
            Assert.True(view.NoteRows[0].DeleteButton.IsFocused);
            Assert.NotNull(rig.Memory.Find(id));

            Click(view.NoteRows[0].DeleteButton);
            stage.Layout();
            Click(view.NoteRows[0].KeepNoteButton!);                       // "Keep the note"
            stage.Layout();
            Assert.NotNull(rig.Memory.Find(id));
            Click(view.NoteRows[0].DeleteButton);
            stage.Layout();
            Click(view.NoteRows[0].ConfirmDeleteButton!);
            stage.Layout();
            Assert.Null(rig.Memory.Find(id));
            Assert.Empty(view.NoteRows);
            Assert.True(view.ShowsEmpty);
            Assert.Equal("Deleted. The note is gone from the files, the index and the search.", view.StatusText);
            Assert.DoesNotContain("zebrafish", rig.AllText(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Source_answers_why_do_you_know_this_and_Escape_closes_it()
    {
        headless.OnUiThread(() =>
        {
            Add("Releases go out on Fridays");
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            Click(view.NoteRows[0].SourceButton);
            stage.Layout();
            var row = view.NoteRows[0];
            Assert.Equal(["From your chat “Planning”"], row.SourceLines.Select(l => char.ToUpperInvariant(l[0]) + l[1..]));
            var texts = Texts(row);
            Assert.Contains("Why do you know this?", texts);
            Assert.Contains("• From your chat “Planning”", texts);
            Assert.Contains("Written by: An assistant", texts);
            row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            stage.Layout();
            Assert.Null(model.SourceOpenId);
            Assert.True(view.NoteRows[0].SourceButton.IsFocused);
            Assert.DoesNotContain("Why do you know this?", Texts(view.NoteRows[0]));
        });
    }

    [Fact]
    public void Every_control_has_a_name_that_names_its_note_and_the_lists_are_headed()
    {
        headless.OnUiThread(() =>
        {
            Fill();
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            var row = view.NoteRows.Single(r => r.Note.Text == "Releases go out on Fridays");
            Assert.Equal("Releases go out on Fridays", AutomationProperties.GetName(row));
            Assert.Equal("Edit the note: Releases go out on Fridays", AutomationProperties.GetName(row.EditButton!));
            Assert.Equal("Delete the note for real: Releases go out on Fridays", AutomationProperties.GetName(row.DeleteButton));
            Assert.Equal("Show why you know the note: Releases go out on Fridays", AutomationProperties.GetName(row.SourceButton));
            var waiting = view.WaitingRows.Single();
            Assert.Equal("Keep the note and use it: Page says the vault is in the red folder", AutomationProperties.GetName(waiting.KeepButton!));
            Assert.Equal("Search the notes", AutomationProperties.GetName(view.SearchBox));
            Assert.Equal("Write a note yourself", AutomationProperties.GetName(view.AddBox));
            Assert.Equal("Forget everything about a topic", AutomationProperties.GetName(view.ForgetBox));
            Assert.Equal("Add the note", AutomationProperties.GetName(view.AddButton!));
            Assert.Equal("Bring in the notes other agents keep", AutomationProperties.GetName(view.ImportButton!));
            var headings = Nodes<TextBlock>(view).Where(t => AutomationProperties.GetHeadingLevel(t) > 0).Select(t => (t.Text, AutomationProperties.GetHeadingLevel(t))).ToArray();
            Assert.Contains(("About you · 2", 2), headings);
            Assert.Contains(("Releases", 3), headings);
            // Every button has some name, and none is a bare glyph.
            Assert.All(Nodes<Button>(view).Where(b => b is not RepeatButton), b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b)), "a button has no name"));
        });
    }

    [Fact]
    public void Search_filters_as_the_owner_types_and_Escape_clears_it()
    {
        headless.OnUiThread(() =>
        {
            Fill();
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            view.SearchBox.Text = "pune";
            stage.Layout();
            Assert.Equal(["The owner is based in Pune"], view.NoteRows.Select(r => r.Note.Text));
            Assert.False(view.ShowsWaiting);
            view.SearchBox.Text = "zebrafish";
            stage.Layout();
            Assert.True(view.ShowsNoMatch);
            view.SearchBox.Focus();
            view.SearchBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            stage.Layout();
            Assert.Equal("", view.SearchBox.Text);
            Assert.Equal(4, view.NoteRows.Count - view.WaitingRows.Count);
        });
    }

    [Fact]
    public void A_note_is_written_by_the_owner_with_a_kind_marked_by_a_tick_and_a_refusal_says_why()
    {
        headless.OnUiThread(() =>
        {
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            Assert.Equal("✓ Fact", LabelOf(view.KindButtons[NoteKind.Fact]));
            Assert.Equal("Procedure", LabelOf(view.KindButtons[NoteKind.Procedure]));
            Click(view.KindButtons[NoteKind.Procedure]);
            Assert.Equal("✓ Procedure", LabelOf(view.KindButtons[NoteKind.Procedure]));
            Assert.Equal("Fact", LabelOf(view.KindButtons[NoteKind.Fact]));
            Assert.Equal("Kind of note: Procedure, chosen", AutomationProperties.GetName(view.KindButtons[NoteKind.Procedure]));
            view.AddBox.Text = "Back up the laptop on Sundays";
            Click(view.AddButton!);
            stage.Layout();
            Assert.Equal("", view.AddBox.Text);
            Assert.Equal(["Back up the laptop on Sundays"], view.NoteRows.Select(r => r.Note.Text));
            view.AddBox.Text = "The wifi password is hunter2horse";
            Click(view.AddButton!);
            stage.Layout();
            Assert.Contains("Secrets are never stored", view.AddProblemText, StringComparison.Ordinal);
            Assert.Equal("The wifi password is hunter2horse", view.AddBox.Text);
            Assert.DoesNotContain("hunter2horse", rig.AllText(), StringComparison.Ordinal);
        });
    }

    private static string LabelOf(Button button) => Nodes<TextBlock>(button).Single().Text ?? "";

    [Fact]
    public void Forget_shows_the_notes_it_will_remove_and_removes_only_on_the_second_press()
    {
        headless.OnUiThread(() =>
        {
            Add("Dinner is at eight and the owner is vegetarian", topic: "Diet");
            Add("The owner avoids peanuts", topic: "Diet");
            Add("Releases go out on Fridays");
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            view.ForgetBox.Text = "Forget everything about diet";
            Click(view.PreviewButton!);
            stage.Layout();
            Assert.Equal(["This removes 2 notes:", "• Dinner is at eight and the owner is vegetarian", "• The owner avoids peanuts"], view.ForgetPreviewTexts);
            Assert.Equal(3, rig.Memory.Notes(null).Count);
            Assert.Equal("Remove these notes for real", AutomationProperties.GetName(view.ConfirmForgetButton!));
            Click(view.CancelForgetButton!);
            stage.Layout();
            Assert.Empty(view.ForgetPreviewTexts);
            Assert.Equal(3, rig.Memory.Notes(null).Count);
            Click(view.PreviewButton!);
            stage.Layout();
            Click(view.ConfirmForgetButton!);
            stage.Layout();
            Assert.Equal(["Releases go out on Fridays"], view.NoteRows.Select(r => r.Note.Text));
            Assert.Equal("Removed 2 notes, for real.", view.StatusText);
            Assert.DoesNotContain("vegetarian", rig.AllText(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_change_from_elsewhere_redraws_the_list_and_keeps_an_open_edit_and_its_focus()
    {
        headless.OnUiThread(() =>
        {
            Add("The owner lives in Pune");
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view);
            Click(view.NoteRows[0].EditButton!);
            stage.Layout();
            view.NoteRows[0].EditBox!.Focus();
            view.NoteRows[0].EditBox!.Text = "The owner lives in Mumb";
            rig.Memory.Propose(MemoryRig.Clean("Releases go out on Fridays"));          // a capture lands while the owner types
            stage.Layout();
            Assert.Equal(2, view.NoteRows.Count);
            var editing = view.NoteRows.Single(r => r.EditBox is not null);
            Assert.Equal("The owner lives in Mumb", editing.EditBox!.Text);
            Assert.True(editing.EditBox.IsKeyboardFocusWithin);
        });
    }

    [Fact]
    public void Long_text_a_narrow_window_and_right_to_left_text_wrap_without_overflow()
    {
        headless.OnUiThread(() =>
        {
            var longText = string.Join(' ', Enumerable.Repeat("a very long remembered sentence that goes on and on", 12));
            Add(longText[..MemoryRules.MaxNoteChars].TrimEnd());
            Add("مجھے مختصر جواب پسند ہیں اور ہمیشہ انگریزی میں جواب دیں", NoteKind.Preference, MemoryScope.Shared, "Urdu");
            rig.Memory.Propose(MemoryRig.FromWeb("Page says the vault is in the red folder and the long page title goes on for a while"));
            using var model = Model();
            var view = new MemoryView(model);
            using var stage = new ReserveViewStage(view, width: 375, height: 800, flow: FlowDirection.RightToLeft);
            Click(view.NoteRows[0].SourceButton);
            Click(view.NoteRows[1].DeleteButton);
            stage.Layout();
            Assert.All(view.NoteRows, row =>
            {
                var area = new Rect(row.Bounds.Size).TransformToAABB(row.TransformToVisual(stage.Window)!.Value);
                Assert.True(area.Left >= -0.5 && area.Right <= 375.5, $"a row runs past the window: {area.Left} to {area.Right}");
            });
            Assert.True(view.NoteRows.Max(r => r.Bounds.Height) > 60);                       // the long note wrapped onto more lines
            var scroll = Nodes<ScrollViewer>(view).First();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, "the page scrolls sideways");
            Assert.All(Nodes<Button>(view).Where(b => b is not RepeatButton && b.IsVisible), b => Assert.True(b.Bounds.Height >= 28, "a button is shorter than 28 px"));
        });
    }
}
