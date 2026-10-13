// PORT-ONLY: Dextra (DESIGN.md rev 5, 10, 12.6b; the home menu "Teach"; unit AS-19; ED-NEW-dextra-as19-10). No Swift twin.
// The Teach page: a button that starts a recording, the recordings that wait (with the day each deletes itself), and for the one the owner
// opens its draft: the name, the steps with the ones that ask marked in words, the inputs, reasons and obstacles, the questions, the dry run, the
// trial with him, and Save. The page is its own class so the home and the Settings that is moving into the main window can both mount it. Nothing
// moves on its own: a row grows only inside itself, a list changes only when the model does, a choice is shown with a tick and its words,
// never by colour alone, and there is no animation at all, so Reduce motion has nothing to change.
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

public sealed class TeachView : Decorator
{
    public const double Gutter = 16;
    public const double MaxContentWidth = 860;

    private readonly TeachModel model;
    private readonly LiveStatusText startStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly LiveStatusText draftStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly LiveStatusText saveStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel recordings = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    private readonly StackPanel draftPanel = new() { Orientation = Orientation.Vertical, Spacing = 12 };
    private readonly TextBlock noRecordings = new() { Text = TeachViewWords.NoRecordings, TextWrapping = TextWrapping.Wrap };
    private readonly List<TextBox> stepBoxes = [];
    private bool refreshing;

    public TeachView(TeachModel model)
    {
        this.model = model;
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(startStatus);
        startStatus.Foreground = Chrome.SecondaryText.Brush(startStatus);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(draftStatus);
        draftStatus.Foreground = Chrome.SecondaryText.Brush(draftStatus);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(saveStatus);
        saveStatus.Foreground = Chrome.SecondaryText.Brush(saveStatus);
        Secondary(noRecordings);

        var heading = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Children = { Text(TeachViewWords.Title, 20, SwiftFont.Weight.Semibold, primary: true), Text(TeachViewWords.Subtitle, SwiftFont.TextStyle.Footnote, secondary: true) },
        };
        AutomationProperties.SetHeadingLevel(heading.Children[0], 1);
        StartButton = PlanCard.Capsule(TeachViewWords.StartTitle, model.Start, "Start recording what you do");
        var startCard = Card(new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Children =
            {
                new WrapPanel { Orientation = Orientation.Horizontal, Children = { StartButton.Value.Shown } },
                startStatus,
                Text(TeachViewWords.StartDetail, SwiftFont.TextStyle.Footnote, secondary: true),
            },
        });
        var listHeading = Text(TeachViewWords.RecordingsTitle, 15, SwiftFont.Weight.Semibold, primary: true);
        AutomationProperties.SetHeadingLevel(listHeading, 2);
        var listCard = Card(new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { listHeading, recordings, noRecordings } });
        AutomationProperties.SetName(recordings, TeachViewWords.RecordingsTitle);

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            Margin = new Thickness(Gutter),
            MaxWidth = MaxContentWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { heading, startCard, listCard, draftPanel },
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

    public TeachModel Model => model;

    internal (Button Button, Control Shown)? StartButton { get; }

    internal IReadOnlyList<TextBox> StepBoxes => stepBoxes;

    /// <summary>Every button on the page by its accessible name, for tests and for focus.</summary>
    internal IEnumerable<Button> Buttons => Descendants<Button>(this);

    public string StartStatusText => startStatus.Text ?? "";

    public string DraftStatusText => draftStatus.Text ?? "";

    public string SaveStatusText => saveStatus.Text ?? "";

    public bool ShowsEmpty => noRecordings.IsVisible;

    /// <summary>Every line of text on the page, top to bottom.</summary>
    public IReadOnlyList<string> Texts => [.. Descendants<TextBlock>(this).Select(t => t.Text ?? "").Where(t => t.Length > 0)];

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    // ---- drawing the model ---------------------------------------------------------------------------------------------------

    private void Refresh()
    {
        refreshing = true;
        try
        {
            startStatus.Text = model.StartStatus;
            startStatus.IsVisible = model.StartStatus.Length > 0;
            if (StartButton is { } start) start.Button.IsEnabled = model.CanStart;

            recordings.Children.Clear();
            foreach (var row in model.Rows) recordings.Children.Add(Row(row));
            foreach (var id in model.Unreadable) recordings.Children.Add(Unreadable(id));
            noRecordings.IsVisible = model.Rows.Count == 0 && model.Unreadable.Count == 0;

            stepBoxes.Clear();
            draftPanel.Children.Clear();
            if (model.SelectedId is not null) DrawDraft();
        }
        finally
        {
            refreshing = false;
        }
    }

    private Control Row(TeachRow row)
    {
        var title = Text(row.Title, 13, SwiftFont.Weight.Medium, primary: true);
        var detail = Text(row.Detail, SwiftFont.TextStyle.Footnote, secondary: true);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        var what = row.Title;
        if (row.CanDraft) buttons.Children.Add(PlanCard.Capsule(TeachViewWords.DraftTitle, () => _ = model.DraftAsync(row.Info.Id), "Draft the recording of " + what).Shown);
        if (row.CanOpen) buttons.Children.Add(PlanCard.Capsule(TeachViewWords.OpenTitle, () => model.Select(row.Info.Id), "Open the draft of " + what).Shown);
        if (row.CanDelete)
        {
            var asked = model.DeleteAsked == row.Info.Id;
            var delete = PlanCard.Capsule(asked ? TeachViewWords.DeleteAskTitle : TeachViewWords.DeleteTitle, () => model.Delete(row.Info.Id),
                (asked ? "Delete for real the recording of " : "Delete the recording of ") + what);
            buttons.Children.Add(delete.Shown);
            if (asked)
            {
                var keep = PlanCard.Capsule(TeachViewWords.KeepAskTitle, model.CancelDelete, "Keep the recording of " + what);
                buttons.Children.Add(keep.Shown);
            }
        }
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Children = { title, detail, buttons } };
        if (model.SelectedId == row.Info.Id) column.Children.Insert(0, Text("Open below", SwiftFont.TextStyle.Footnote, secondary: true));
        var border = new Border { Child = column, Padding = new Thickness(10) };
        border.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        border.Background = Chrome.Overlay(0.04).Brush(border);
        border.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key != Key.Escape || model.DeleteAsked is null) return;
            model.CancelDelete();
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return border;
    }

    private static Control Unreadable(string id)
    {
        var text = Text("A recording could not be read (" + id + "). It is left as it is.", SwiftFont.TextStyle.Footnote, secondary: true);
        text.Foreground = Chrome.Warning.Brush(text);
        return text;
    }

    // ---- the draft ----

    private void DrawDraft()
    {
        if (model.Draft is not { } draft)
        {
            draftStatus.Text = model.DraftStatus;
            draftStatus.IsVisible = model.DraftStatus.Length > 0;
            if (draftStatus.IsVisible) draftPanel.Children.Add(draftStatus);
            return;
        }
        var name = Field("lowercase-name", draft.Name, model.Rename, TeachViewWords.NameTitle);
        var description = Field("One line on what it does", draft.Description, model.Describe, TeachViewWords.DescriptionTitle);
        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Children =
            {
                Text(TeachViewWords.NameTitle, SwiftFont.TextStyle.Footnote, secondary: true), Boxed(name),
                Text(TeachViewWords.DescriptionTitle, SwiftFont.TextStyle.Footnote, secondary: true), Boxed(description),
            },
        };
        foreach (var note in draft.Notes) header.Children.Add(Text(note, SwiftFont.TextStyle.Footnote, secondary: true));
        if (draft.ModelWritten) header.Children.Add(Text(TeachViewWords.ModelNote, SwiftFont.TextStyle.Footnote, secondary: true));
        draftPanel.Children.Add(Card(header));

        var steps = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        var stepsHeading = Text(TeachViewWords.StepsTitle + " · " + draft.Steps.Count, 15, SwiftFont.Weight.Semibold, primary: true);
        AutomationProperties.SetHeadingLevel(stepsHeading, 2);
        steps.Children.Add(stepsHeading);
        steps.Children.Add(Text(draft.Boundary.Count == 0 ? "No step asks you." : $"{draft.Boundary.Count} of {draft.Steps.Count} steps ask you first, whoever runs the skill.", SwiftFont.TextStyle.Footnote, secondary: true));
        foreach (var step in draft.Steps) steps.Children.Add(StepRow(step));
        draftPanel.Children.Add(Card(steps));

        if (draft.Parameters.Count + draft.Decisions.Count + draft.Obstacles.Count > 0)
        {
            var notes = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            if (draft.Parameters.Count > 0)
            {
                notes.Children.Add(Heading(TeachViewWords.InputsTitle));
                foreach (var p in draft.Parameters) notes.Children.Add(Text($"{{{p.Name}}}: for example “{p.Example}”" + (p.Where.Length > 0 ? ", " + p.Where : ""), SwiftFont.TextStyle.Footnote, secondary: false));
            }
            if (draft.Decisions.Count > 0)
            {
                notes.Children.Add(Heading(TeachViewWords.DecisionsTitle));
                foreach (var d in draft.Decisions) notes.Children.Add(Text(d.Text + (d.Reason.Length > 0 ? " (" + d.Reason + ")" : ""), SwiftFont.TextStyle.Footnote, secondary: false));
            }
            if (draft.Obstacles.Count > 0)
            {
                notes.Children.Add(Heading(TeachViewWords.ObstaclesTitle));
                foreach (var o in draft.Obstacles) notes.Children.Add(Text(o.Text + (o.Fix.Length > 0 ? ". What worked: " + o.Fix : ""), SwiftFont.TextStyle.Footnote, secondary: false));
            }
            draftPanel.Children.Add(Card(notes));
        }

        if (draft.OpenQuestions.Count > 0)
        {
            var questions = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { Heading(TeachViewWords.QuestionsTitle) } };
            foreach (var q in draft.OpenQuestions)
            {
                var options = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
                foreach (var option in q.Options)
                {
                    var id = q.Id;
                    var chosen = option;
                    options.Children.Add(PlanCard.Capsule(option, () => model.Answer(id, chosen), "Answer “" + option + "” to: " + q.Text).Shown);
                }
                questions.Children.Add(new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Children = { Text(q.Text, 13, SwiftFont.Weight.Medium, primary: true), options } });
            }
            draftPanel.Children.Add(Card(questions));
        }

        draftStatus.Text = model.DraftStatus;
        draftStatus.IsVisible = model.DraftStatus.Length > 0;
        saveStatus.Text = model.SaveStatus;
        saveStatus.IsVisible = model.SaveStatus.Length > 0;
        draftPanel.Children.Add(Actions(draft));
        if (model.DryRunText is { } dry)
        {
            var lines = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Children = { Heading(TeachViewWords.DryRunTitle) } };
            foreach (var line in dry.Split('\n')) lines.Children.Add(Text(line, SwiftFont.TextStyle.Footnote, secondary: false));
            draftPanel.Children.Add(Card(lines));
        }
        if (model.Trial is { } trial && trial.RecordingId == model.SelectedId) draftPanel.Children.Add(Card(TrialPanel(trial)));
        if (draftStatus.IsVisible) draftPanel.Children.Add(draftStatus);
        if (saveStatus.IsVisible) draftPanel.Children.Add(saveStatus);
    }

    private Control StepRow(TeachStep step)
    {
        var number = step.Number;
        var box = Field("The step", step.Text, text => model.Reword(number, text), $"Step {number}");
        stepBoxes.Add(box);
        var mark = TeachViewWords.Mark(step);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        column.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            Children = { NumberLabel(number), Placed(Boxed(box), 1) },
        });
        if (mark.Length > 0)
        {
            var asks = Text(mark + (step.Guard is { OwnerDoesIt: false } g ? ": " + g.Reason : step.Guard is { } own ? ": " + own.Reason : ""), SwiftFont.TextStyle.Footnote, secondary: false);
            column.Children.Add(asks);
        }
        if (!string.IsNullOrWhiteSpace(step.Said)) column.Children.Add(Text("You said: “" + step.Said + "”", SwiftFont.TextStyle.Footnote, secondary: true));
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        if (step.Guard is null)
        {
            var on = step.Flagged;
            var flag = PlanCard.Capsule((on ? "✓ " : "") + TeachViewWords.PauseHereTitle, () => model.Flag(number, !on), $"{TeachViewWords.PauseHereTitle}, step {number}, " + (on ? "on" : "off"));
            buttons.Children.Add(flag.Shown);
        }
        buttons.Children.Add(PlanCard.Capsule(TeachViewWords.RemoveTitle, () => model.RemoveStep(number), $"Take step {number} out of the skill").Shown);
        column.Children.Add(buttons);
        return column;
    }

    private Control Actions(TeachDraft draft)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        row.Children.Add(PlanCard.Capsule(TeachViewWords.DryRunTitle, model.DryRun, "List what the skill would do, without doing anything").Shown);
        row.Children.Add(PlanCard.Capsule(TeachViewWords.TrialTitle, model.StartTrial, "Try the skill with you watching").Shown);
        row.Children.Add(PlanCard.Capsule(TeachViewWords.RedraftTitle, () => _ = model.RedraftAsync(), "Draft it again from the recording and throw away your edits").Shown);
        var keep = model.Keep;
        row.Children.Add(PlanCard.Capsule((keep ? "✓ " : "") + TeachViewWords.KeepTitle, () => model.SetKeep(!keep), TeachViewWords.KeepTitle + ", " + (keep ? "on" : "off")).Shown);
        var save = PlanCard.Capsule(TeachViewWords.SaveTitle, () => _ = model.SaveAsync(), "Save the draft as a skill");
        save.Button.IsEnabled = model.CanSave && draft.Problem() is null;
        row.Children.Add(save.Shown);
        var problem = draft.Problem();
        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Children = { row, problem is null ? new Border() : Text(problem, SwiftFont.TextStyle.Footnote, secondary: true) },
        };
    }

    private Control TrialPanel(TrialView trial)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Children = { Heading(TeachViewWords.TrialTitle) } };
        if (trial.Result is { } result)
        {
            panel.Children.Add(Text(result.Outcome switch
            {
                TrialOutcome.Finished => "The trial is done: every step was gone through.",
                TrialOutcome.Stopped => result.Reason ?? "The trial was stopped.",
                _ => "The trial failed: " + result.Reason,
            }, SwiftFont.TextStyle.Footnote, secondary: false));
            foreach (var line in result.Log) panel.Children.Add(Text(line, SwiftFont.TextStyle.Footnote, secondary: true));
            panel.Children.Add(new WrapPanel { Children = { PlanCard.Capsule(TeachViewWords.CloseTitle, model.CloseTrial, "Close the trial").Shown } });
            return panel;
        }
        if (trial.Pending is { } pending)
        {
            panel.Children.Add(Text(pending.Question, 13, SwiftFont.Weight.Medium, primary: true));
            var options = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
            var answers = new[] { TrialAnswer.Continue, TrialAnswer.Skip, TrialAnswer.Stop };
            for (var i = 0; i < pending.Options.Count && i < answers.Length; i++)
            {
                var answer = answers[i];
                options.Children.Add(PlanCard.Capsule(pending.Options[i], () => model.AnswerTrial(answer), pending.Options[i] + ", step " + pending.Step.Number).Shown);
            }
            panel.Children.Add(options);
        }
        else if (trial.Progress is { } progress)
        {
            panel.Children.Add(Text($"Step {progress.Step} of {progress.Of}: {progress.Text}", SwiftFont.TextStyle.Footnote, secondary: false));
        }
        var controls = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        controls.Children.Add(trial.Paused
            ? PlanCard.Capsule(TeachViewWords.ResumeTitle, model.ResumeTrial, "Resume the trial").Shown
            : PlanCard.Capsule(TeachViewWords.PauseTitle, model.PauseTrial, "Pause the trial before its next step").Shown);
        controls.Children.Add(PlanCard.Capsule(TeachViewWords.StopTitle, model.StopTrial, "Stop the trial").Shown);
        panel.Children.Add(controls);
        return panel;
    }

    // ---- pieces ------------------------------------------------------------------------------------------------------------------

    private TextBox Field(string prompt, string value, Action<string> commit, string name)
    {
        var box = PlainControls.TextField(prompt, new Binding<string>(() => value, text => { if (!refreshing) commit(text); }));
        box.Text = value;
        AutomationProperties.SetName(box, name);
        box.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key != Key.Enter) return;
            commit(box.Text ?? "");
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) =>
        {
            if (!refreshing && !string.Equals(box.Text ?? "", value, StringComparison.Ordinal)) commit(box.Text ?? "");
        };
        return box;
    }

    private static Border Boxed(TextBox box)
    {
        var field = new Border { Padding = new Thickness(10, 6), Child = box, MinHeight = Chrome.CapsuleContentHeight };
        field.Capsule(RoundedCornerStyle.Continuous);
        field.Background = Chrome.Overlay(0.06).Brush(field);
        return field;
    }

    private static Control NumberLabel(int number)
    {
        var label = Text(number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", 13, SwiftFont.Weight.Medium, primary: true);
        label.MinWidth = 26;
        label.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
        return label;
    }

    private static Control Placed(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static TextBlock Heading(string text)
    {
        var heading = Text(text, 15, SwiftFont.Weight.Semibold, primary: true);
        AutomationProperties.SetHeadingLevel(heading, 2);
        return heading;
    }

    // Every control of the page of a kind, in the order they are laid out (the logical tree: what the page was built from).
    private static IEnumerable<T> Descendants<T>(Avalonia.LogicalTree.ILogical root) where T : class
    {
        var stack = new Stack<Avalonia.LogicalTree.ILogical>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is T found) yield return found;
            foreach (var child in node.LogicalChildren.Reverse()) stack.Push(child);
        }
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
