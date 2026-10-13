// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2, 8.3, 8.4; the home menu "Memory" and Settings > Assistant > Memory; unit AS-12; ED-NEW-dextra-as12-20).
// No Swift twin. The Memory view: notes by assistant and topic with search; the notes that wait for the owner's Keep, with Keep and
// Delete; Edit, Delete (once more before a used note goes) and Source on every note; a line to write a note himself; "Forget
// everything about ..." that shows the notes it will remove first; and a button that brings in the notes other agents keep. The page
// is its own class so the home and the Settings that is moving into the main window can both mount it as it is.
// Nothing moves on its own: a row grows only inside itself (its edit, its question or its source), a list changes only when a note
// does, and with Reduce motion nothing here animates at all (there is no animation in the view).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class MemoryView : Decorator
{
    public const double Gutter = 16;
    public const double MaxContentWidth = 860;

    private readonly MemoryModel model;
    private readonly TextBlock countLine = new() { TextWrapping = TextWrapping.Wrap };
    private readonly LiveStatusText status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel problems = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    private readonly StackPanel waitingList = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    private readonly Border waitingCard;
    private readonly StackPanel groups = new() { Orientation = Orientation.Vertical, Spacing = 14 };
    private readonly TextBlock emptyText = new() { Text = MemoryViewWords.Empty, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock noMatchText = new() { Text = MemoryViewWords.NoMatch, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel forgetPreview = new() { Orientation = Orientation.Vertical, Spacing = 6 };
    private readonly LiveStatusText addProblem = new() { TextWrapping = TextWrapping.Wrap };
    private readonly LiveStatusText importLine = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox searchBox;
    private readonly TextBox addBox;
    private readonly TextBox forgetBox;
    private readonly List<MemoryNoteRow> rows = [];
    private readonly Dictionary<NoteKind, Button> kindButtons = [];
    private readonly Dictionary<NoteKind, TextBlock> kindLabels = [];
    private bool refreshing;
    private (string Id, MemoryRowButton Button)? focusRequest;

    public MemoryView(MemoryModel model)
    {
        this.model = model;
        searchBox = Field(MemoryViewWords.SearchPrompt, () => model.Query, value => model.Query = value, "Search the notes");
        searchBox.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key != Key.Escape || string.IsNullOrEmpty(model.Query)) return;
            model.Query = "";
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        addBox = Field(MemoryViewWords.WritePrompt, () => model.AddText, value => model.AddText = value, "Write a note yourself");
        addBox.MaxLength = MemoryRules.MaxNoteChars;
        addBox.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key == Key.Enter) { _ = model.Add(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        forgetBox = Field(MemoryViewWords.ForgetPrompt, () => model.ForgetText, value => model.ForgetText = value, "Forget everything about a topic");
        forgetBox.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key == Key.Enter) { model.PreviewForget(); e.Handled = true; }
            else if (e.Key == Key.Escape && model.ForgetPreview is not null) { model.CancelForget(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var title = Text(MemoryViewWords.Title, 20, SwiftFont.Weight.Semibold, primary: true);
        var subtitle = Text(MemoryViewWords.Subtitle, SwiftFont.TextStyle.Footnote, secondary: true);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(countLine);
        countLine.Foreground = Chrome.SecondaryText.Brush(countLine);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(status);
        status.Foreground = Chrome.SecondaryText.Brush(status);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(addProblem);
        addProblem.Foreground = Chrome.Danger.Brush(addProblem);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(importLine);
        importLine.Foreground = Chrome.SecondaryText.Brush(importLine);
        Secondary(emptyText);
        Secondary(noMatchText);

        var heading = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Children = { title, subtitle, countLine } };
        waitingCard = Card(new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Children =
            {
                Text(MemoryViewWords.WaitingTitle, 15, SwiftFont.Weight.Semibold, primary: true),
                Text(MemoryViewWords.WaitingDetail, SwiftFont.TextStyle.Footnote, secondary: true),
                waitingList,
                KeepAllRow(),
            },
        });
        AutomationProperties.SetName(waitingList, MemoryViewWords.WaitingTitle);
        AutomationProperties.SetName(groups, "Notes");

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            Margin = new Thickness(Gutter),
            MaxWidth = MaxContentWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { heading, ImportCard(), problems, Boxed(searchBox), status, waitingCard, groups, emptyText, noMatchText, ToolsCard() },
        };
        Child = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        AttachedToVisualTree += (_, _) =>
        {
            model.Changed -= OnChanged;
            model.Changed += OnChanged;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => model.Changed -= OnChanged;
        Refresh();
    }

    public MemoryModel Model => model;

    public TextBox SearchBox => searchBox;

    public TextBox AddBox => addBox;

    public TextBox ForgetBox => forgetBox;

    internal Button? AddButton { get; private set; }

    internal Button? PreviewButton { get; private set; }

    internal Button? ConfirmForgetButton { get; private set; }

    internal Button? CancelForgetButton { get; private set; }

    internal Button? ImportButton { get; private set; }

    internal Button? KeepAllButton { get; private set; }

    internal IReadOnlyList<MemoryNoteRow> NoteRows => rows;

    internal IReadOnlyList<MemoryNoteRow> WaitingRows => [.. waitingList.Children.OfType<MemoryNoteRow>()];

    internal IReadOnlyDictionary<NoteKind, Button> KindButtons => kindButtons;

    public string CountText => countLine.Text ?? "";

    public string StatusText => status.Text ?? "";

    public string AddProblemText => addProblem.Text ?? "";

    public string ImportText => importLine.Text ?? "";

    public bool ShowsEmpty => emptyText.IsVisible;

    public bool ShowsNoMatch => noMatchText.IsVisible;

    public bool ShowsWaiting => waitingCard.IsVisible;

    public IReadOnlyList<string> ProblemTexts => [.. problems.Children.OfType<TextBlock>().Select(t => t.Text ?? "")];

    /// <summary>The headings of the lists as shown, in order: each assistant, each topic, then its notes' texts.</summary>
    public IReadOnlyList<string> Outline
    {
        get
        {
            var lines = new List<string>();
            foreach (var child in groups.Children)
            {
                if (child is not StackPanel group) continue;
                foreach (var part in group.Children)
                {
                    switch (part)
                    {
                        case TextBlock text: lines.Add(text.Text ?? ""); break;
                        case MemoryNoteRow row: lines.Add("  - " + row.Note.Text); break;
                        case StackPanel topic:
                            foreach (var inner in topic.Children)
                            {
                                if (inner is TextBlock heading) lines.Add(" " + heading.Text);
                                else if (inner is MemoryNoteRow row) lines.Add("  - " + row.Note.Text);
                            }
                            break;
                    }
                }
            }
            return lines;
        }
    }

    public IReadOnlyList<string> ForgetPreviewTexts => [.. forgetPreview.Children.OfType<TextBlock>().Select(t => t.Text ?? "")];

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    // ---- drawing the model ---------------------------------------------------------------------------------------------------

    private void Refresh()
    {
        var focusedEdit = rows.FirstOrDefault(r => r.EditBox is { IsKeyboardFocusWithin: true });
        if (focusedEdit is not null) focusRequest = (focusedEdit.Note.Id, MemoryRowButton.EditBox);
        refreshing = true;
        try
        {
            Sync(searchBox, model.Query);
            Sync(addBox, model.AddText);
            Sync(forgetBox, model.ForgetText);
        }
        finally { refreshing = false; }

        countLine.Text = MemoryViewWords.Count(model.Counts);
        status.Text = model.Status;
        status.IsVisible = model.Status.Length > 0;
        problems.Children.Clear();
        foreach (var line in model.Problems)
        {
            var text = Text(line, SwiftFont.TextStyle.Footnote, secondary: true);
            text.Foreground = Chrome.Warning.Brush(text);
            problems.Children.Add(text);
        }
        addProblem.Text = model.AddProblem ?? "";
        addProblem.IsVisible = model.AddProblem is not null;
        importLine.Text = model.ImportSummary;
        importLine.IsVisible = model.ImportSummary.Length > 0;
        if (ImportButton is not null) ImportButton.IsEnabled = !model.Importing;

        rows.Clear();
        waitingList.Children.Clear();
        foreach (var note in model.Waiting)
        {
            var row = new MemoryNoteRow(model, note, FocusLater);
            waitingList.Children.Add(row);
            rows.Add(row);
        }
        waitingCard.IsVisible = model.Waiting.Count > 0;

        groups.Children.Clear();
        foreach (var scope in model.Groups)
        {
            var group = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            var heading = Text(scope.Title + " · " + scope.Count, 15, SwiftFont.Weight.Semibold, primary: true);
            AutomationProperties.SetHeadingLevel(heading, 2);
            group.Children.Add(heading);
            if (scope.Detail.Length > 0) group.Children.Add(Text(scope.Detail, SwiftFont.TextStyle.Footnote, secondary: true));
            foreach (var topic in scope.Topics)
            {
                var section = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
                var topicHeading = Text(topic.Title, 13, SwiftFont.Weight.Medium, primary: false);
                AutomationProperties.SetHeadingLevel(topicHeading, 3);
                section.Children.Add(topicHeading);
                foreach (var note in topic.Notes)
                {
                    var row = new MemoryNoteRow(model, note, FocusLater);
                    section.Children.Add(row);
                    rows.Add(row);
                }
                group.Children.Add(section);
            }
            groups.Children.Add(group);
        }
        emptyText.IsVisible = model.IsEmpty;
        noMatchText.IsVisible = model.NoMatch;

        forgetPreview.Children.Clear();
        forgetPreview.IsVisible = model.ForgetPreview is { Count: > 0 };
        ConfirmForgetButton = null;
        CancelForgetButton = null;
        if (model.ForgetPreview is { Count: > 0 } preview)
        {
            var line = Text(preview.Count == 1 ? "This removes 1 note:" : $"This removes {preview.Count} notes:", SwiftFont.TextStyle.Footnote, secondary: false);
            AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
            forgetPreview.Children.Add(line);
            foreach (var note in preview.Take(50)) forgetPreview.Children.Add(Text("• " + (note.Quarantined ? "(waiting) " : "") + note.Text, SwiftFont.TextStyle.Footnote, secondary: true));
            if (preview.Count > 50) forgetPreview.Children.Add(Text($"… and {preview.Count - 50} more.", SwiftFont.TextStyle.Footnote, secondary: true));
            var remove = PlanCard.Capsule(preview.Count == 1 ? "Remove 1 note" : $"Remove {preview.Count} notes", () => _ = model.ConfirmForget(), "Remove these notes for real");
            var cancel = PlanCard.Capsule(MemoryViewWords.CancelTitle, () => { model.CancelForget(); forgetBox.Focus(); }, "Do not remove these notes");
            ConfirmForgetButton = remove.Button;
            CancelForgetButton = cancel.Button;
            forgetPreview.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Children = { remove.Shown, cancel.Shown } });
        }
        RefreshKinds();
        RestoreFocus();
    }

    // The row that held the focus is about to be redrawn: the request waits for it, then lands on the same note's button.
    private void FocusLater(string id, MemoryRowButton button) => focusRequest = (id, button);

    private void RestoreFocus()
    {
        if (focusRequest is not { } request) return;
        focusRequest = null;
        Dispatcher.UIThread.Post(() =>
        {
            var row = rows.FirstOrDefault(r => r.Note.Id == request.Id);
            Control? target = request.Button switch
            {
                MemoryRowButton.Edit => (Control?)(row?.EditButton ?? row?.KeepButton ?? row?.DeleteButton),
                MemoryRowButton.Delete => row?.DeleteButton,
                MemoryRowButton.Source => row?.SourceButton,
                MemoryRowButton.Keep => row?.KeepButton,
                MemoryRowButton.EditBox => row?.EditBox ?? (Control?)row?.EditButton ?? row?.KeepButton ?? row?.DeleteButton,
                _ => null,
            };
            target?.Focus();
            if (target is TextBox box) box.CaretIndex = box.Text?.Length ?? 0;
        }, DispatcherPriority.Input);
    }

    private void Sync(TextBox box, string value)
    {
        if (!string.Equals(box.Text ?? "", value, StringComparison.Ordinal)) box.Text = value;
    }

    // ---- pieces ------------------------------------------------------------------------------------------------------------------

    private TextBox Field(string prompt, Func<string> get, Action<string> set, string name)
    {
        var box = PlainControls.TextField(prompt, new Binding<string>(get, value => { if (!refreshing) set(value); }));
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static Border Boxed(TextBox box)
    {
        var field = new Border { Padding = new Thickness(10, 6), Child = box, MinHeight = Chrome.CapsuleContentHeight };
        field.Capsule(RoundedCornerStyle.Continuous);
        field.Background = Chrome.Overlay(0.06).Brush(field);
        return field;
    }

    private Control KeepAllRow()
    {
        var all = PlanCard.Capsule(MemoryViewWords.KeepAllTitle, () => _ = model.KeepAll(), "Keep every note that waits and use it");
        KeepAllButton = all.Button;
        return new WrapPanel { Orientation = Orientation.Horizontal, Children = { all.Shown } };
    }

    private Control ImportCard()
    {
        var button = PlanCard.Capsule(MemoryViewWords.ImportTitle, () => _ = model.ImportAsync(), "Bring in the notes other agents keep");
        ImportButton = button.Button;
        var card = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Children = { Text(MemoryViewWords.ImportDetail, SwiftFont.TextStyle.Footnote, secondary: true), new WrapPanel { Children = { button.Shown } }, importLine },
        };
        var shown = Card(card);
        shown.IsVisible = model.CanImport;
        return shown;
    }

    private Control ToolsCard()
    {
        var add = PlanCard.Capsule(MemoryViewWords.AddTitle, () => _ = model.Add(), "Add the note");
        AddButton = add.Button;
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(add.Shown, 1);
        addRow.Children.Add(Boxed(addBox));
        addRow.Children.Add(add.Shown);

        var kinds = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var kind in new[] { NoteKind.Fact, NoteKind.Preference, NoteKind.Procedure, NoteKind.Project })
        {
            var chip = Chip(kind);
            kindButtons[kind] = chip.Button;
            kindLabels[kind] = chip.Label;
            kinds.Children.Add(chip.Shown);
        }

        var preview = PlanCard.Capsule(MemoryViewWords.ShowTitle, model.PreviewForget, "Show the notes that would be removed");
        PreviewButton = preview.Button;
        var forgetRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(preview.Shown, 1);
        forgetRow.Children.Add(Boxed(forgetBox));
        forgetRow.Children.Add(preview.Shown);
        forgetPreview.IsVisible = false;

        return Card(new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Children =
            {
                Text(MemoryViewWords.WriteTitle, 15, SwiftFont.Weight.Semibold, primary: true),
                kinds,
                addRow,
                addProblem,
                Text(MemoryViewWords.ForgetTitle, 15, SwiftFont.Weight.Semibold, primary: true),
                forgetRow,
                forgetPreview,
            },
        });
    }

    // The kind the next note is written as: a tick before its word, so the choice never rests on colour alone.
    private void RefreshKinds()
    {
        foreach (var (kind, button) in kindButtons)
        {
            var on = model.AddKind == kind;
            kindLabels[kind].Text = (on ? "✓ " : "") + MemoryRules.KindWord(kind);
            AutomationProperties.SetName(button, $"Kind of note: {MemoryRules.KindWord(kind)}" + (on ? ", chosen" : ""));
        }
    }

    private (Button Button, Control Shown, TextBlock Label) Chip(NoteKind kind)
    {
        var label = new TextBlock { Text = MemoryRules.KindWord(kind), MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = Chrome.PrimaryText.Brush(label);
        var face = new Border { Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), Height = Chrome.CapsuleContentHeight, Child = label, Background = Brushes.Transparent };
        face.Capsule(RoundedCornerStyle.Continuous);
        var button = PlainControls.Button(face, () => { model.AddKind = kind; RefreshKinds(); });
        AutomationProperties.SetName(button, "Kind of note: " + MemoryRules.KindWord(kind));
        return (button, button.ChromeGlassCapsule(), label);
    }

    private static Border Card(Control child)
    {
        var card = new Border { Child = child, Padding = new Thickness(14) };
        card.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        card.Background = Chrome.Overlay(0.03).Brush(card);
        return card;
    }

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

    private static void Secondary(TextBlock block)
    {
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
    }
}
