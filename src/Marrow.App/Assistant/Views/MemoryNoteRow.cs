// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.3; unit AS-12; ED-NEW-dextra-as12-20). No Swift twin.
// One note in the Memory view: its words, a line saying what it is and where it came from, and Edit, Delete and Source (Keep and
// Delete for a note that waits). The edit, the second question before a delete and the source open inline in the row, so a row
// never moves anything else; Escape closes whichever is open and puts the focus back on the button that opened it.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>Which button of a row the focus belongs on.</summary>
internal enum MemoryRowButton { None, Edit, Delete, Source, Keep, EditBox }

internal sealed class MemoryNoteRow : Border
{
    private readonly MemoryModel model;
    private readonly Action<string, MemoryRowButton> focusAfter;

    internal MemoryNoteRow(MemoryModel model, MemoryNote note, Action<string, MemoryRowButton> focusAfter)
    {
        this.model = model;
        this.focusAfter = focusAfter;
        Note = note;
        var excerpt = Excerpt(note.Text);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        column.Children.Add(Text(note.Text, 13, SwiftFont.Weight.Medium, primary: true));
        column.Children.Add(Text(MemoryViewWords.Detail(note, model.Zone), SwiftFont.TextStyle.Footnote, secondary: true));
        if (note.Quarantine is not null) column.Children.Add(Text(MemoryWords.WhyWaiting(note), SwiftFont.TextStyle.Footnote, secondary: true));
        if (model.EditingId == note.Id) column.Children.Add(EditPanel(note));
        if (model.ConfirmDeleteId == note.Id) column.Children.Add(ConfirmPanel(note));
        if (model.SourceOpenId == note.Id) column.Children.Add(SourcePanel(note));

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        if (note.Quarantined)
        {
            var keep = Button(MemoryViewWords.KeepTitle, () => model.Keep(note.Id), "Keep the note and use it: " + excerpt);
            KeepButton = keep.Button;
            buttons.Children.Add(keep.Shown);
        }
        else
        {
            var edit = Button(MemoryViewWords.EditTitle, () => model.BeginEdit(note.Id), "Edit the note: " + excerpt);
            EditButton = edit.Button;
            buttons.Children.Add(edit.Shown);
        }
        var delete = Button(MemoryViewWords.DeleteTitle, () => model.Delete(note.Id), "Delete the note for real: " + excerpt);
        DeleteButton = delete.Button;
        buttons.Children.Add(delete.Shown);
        var source = Button(MemoryViewWords.SourceTitle, () => model.ToggleSource(note.Id), "Show why you know the note: " + excerpt);
        SourceButton = source.Button;
        buttons.Children.Add(source.Shown);
        column.Children.Add(buttons);

        Child = column;
        Padding = new Thickness(12, 10);
        this.RoundedRectangle(12, RoundedCornerStyle.Continuous);
        Background = Chrome.Overlay(note.Quarantined ? 0.07 : 0.04).Brush(this);
        AutomationProperties.SetName(this, note.Text);
        KeyDown += OnKeyDown;
    }

    internal MemoryNote Note { get; }

    internal Button? EditButton { get; private set; }

    internal Button? KeepButton { get; private set; }

    internal Button DeleteButton { get; private set; } = null!;

    internal Button SourceButton { get; private set; } = null!;

    internal TextBox? EditBox { get; private set; }

    internal Button? SaveButton { get; private set; }

    internal Button? ConfirmDeleteButton { get; private set; }

    internal Button? KeepNoteButton { get; private set; }

    internal TextBlock? EditProblem { get; private set; }

    internal IReadOnlyList<string> SourceLines { get; private set; } = [];

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        // The focus is asked for first: closing the row redraws it at once, and the focus lands on the redrawn button.
        if (model.EditingId == Note.Id) { focusAfter(Note.Id, MemoryRowButton.Edit); model.CancelEdit(); e.Handled = true; }
        else if (model.ConfirmDeleteId == Note.Id) { focusAfter(Note.Id, MemoryRowButton.Delete); model.CancelDelete(); e.Handled = true; }
        else if (model.SourceOpenId == Note.Id) { focusAfter(Note.Id, MemoryRowButton.Source); model.ToggleSource(Note.Id); e.Handled = true; }
    }

    private Control EditPanel(MemoryNote note)
    {
        var box = PlainControls.TextField("The note", new Binding<string>(() => model.EditText, value => model.EditText = value));
        box.TextWrapping = TextWrapping.Wrap;
        box.AcceptsReturn = false;
        box.MaxLength = MemoryRules.MaxNoteChars;
        AutomationProperties.SetName(box, "Edit the note: " + Excerpt(note.Text));
        box.AddHandler(KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key != Key.Enter) return;
            focusAfter(note.Id, MemoryRowButton.EditBox);
            model.SaveEdit();
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        EditBox = box;
        var field = new Border { Padding = new Thickness(10, 6), Child = box, MinHeight = Chrome.CapsuleContentHeight };
        field.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        field.Background = Chrome.Overlay(0.06).Brush(field);

        var problem = new LiveStatusText { Text = model.EditProblem ?? "", IsVisible = model.EditProblem is not null, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(problem);
        problem.Foreground = Chrome.Danger.Brush(problem);
        EditProblem = problem;

        var save = Button(MemoryViewWords.SaveTitle, () => { focusAfter(note.Id, MemoryRowButton.EditBox); model.SaveEdit(); }, "Save the note: " + Excerpt(note.Text));
        SaveButton = save.Button;
        var cancel = Button(MemoryViewWords.CancelTitle, () => { focusAfter(note.Id, MemoryRowButton.Edit); model.CancelEdit(); }, "Cancel the edit");
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Children = { save.Shown, cancel.Shown } };
        return new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Children = { field, problem, row } };
    }

    private Control ConfirmPanel(MemoryNote note)
    {
        var question = Text("Delete this note for real? It is removed from the files, the index and the search, and nth will not write it again by itself.", SwiftFont.TextStyle.Footnote, secondary: false);
        AutomationProperties.SetLiveSetting(question, AutomationLiveSetting.Polite);
        var confirm = Button("Delete it", () => model.Delete(note.Id), "Yes, delete the note for real: " + Excerpt(note.Text));
        ConfirmDeleteButton = confirm.Button;
        var keep = Button("Keep the note", () => { focusAfter(note.Id, MemoryRowButton.Delete); model.CancelDelete(); }, "No, keep the note");
        KeepNoteButton = keep.Button;
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Children = { confirm.Shown, keep.Shown } };
        return new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Children = { question, row } };
    }

    private Control SourcePanel(MemoryNote note)
    {
        var lines = MemoryWords.SourceLines(note);
        SourceLines = lines;
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        panel.Children.Add(Text(MemoryViewWords.WhyHeading, SwiftFont.TextStyle.Footnote, secondary: false));
        foreach (var line in lines) panel.Children.Add(Text("• " + char.ToUpperInvariant(line[0]) + line[1..], SwiftFont.TextStyle.Footnote, secondary: true));
        panel.Children.Add(Text("Written by: " + MemoryWords.Author(note), SwiftFont.TextStyle.Footnote, secondary: true));
        if (note.LastUsed is { } used)
            panel.Children.Add(Text("Last used: " + TimeZoneInfo.ConvertTime(used, model.Zone).ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture), SwiftFont.TextStyle.Footnote, secondary: true));
        AutomationProperties.SetName(panel, MemoryViewWords.WhyHeading);
        return panel;
    }

    internal static string Excerpt(string text) => text.Length > 48 ? text[..48] + "…" : text;

    private static (Button Button, Control Shown) Button(string title, Action action, string name) => PlanCard.Capsule(title, action, name);

    private static TextBlock Text(string text, double size, SwiftFont.Weight weight, bool primary)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(size, weight).Apply(block);
        block.Foreground = (primary ? Chrome.PrimaryText : Chrome.SecondaryText).Brush(block);
        return block;
    }

    private static TextBlock Text(string text, SwiftFont.TextStyle style, bool secondary)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(style).Apply(block);
        block.Foreground = (secondary ? Chrome.SecondaryText : Chrome.PrimaryText).Brush(block);
        return block;
    }
}
