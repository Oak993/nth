// PORT-ONLY: web surfaces W25, ask_user (DESIGN.md 7.3 "a structured question card (single choice, multiple choice, text, number)",
// 7.5 look, 10 accessibility; ED-NEW-web-w25-1). The waiting card: its questions with the controls for each kind, and the footer with
// Skip and Send. Calm tinted fills, the accent only for what is chosen and for focus; Send is on only when every answer can be taken,
// and says what is still to do when it is not. The card never takes focus when it appears (the person may be typing in the composer);
// a screen reader hears "Waiting for your answer" and the first question, and the keyboard reaches every control in reading order.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.AskUser;

internal sealed class AskCardForm : Border
{
    private sealed class Block(AskQuestion question)
    {
        public AskQuestion Question { get; } = question;
        public List<AskChoicePlate> Plates { get; } = [];
        public TextBox? Field { get; set; }
        // The words in the primary colour, for contrast; the danger colour marks the mark beside them, so nothing rests on colour alone.
        public TextBlock Problem { get; } = TimelineRows.ChatText("", SwiftFont.TextStyle.Subheadline, color: ChromeTokens.PrimaryText);
        public StackPanel ProblemRow { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };
        public bool Touched { get; set; }
    }

    private readonly AskCard card;
    private readonly Func<IReadOnlyDictionary<string, IReadOnlyList<string>>, CultureInfo, AskCheck?> answer;
    private readonly Action skip;
    private readonly CultureInfo culture;
    private readonly List<Block> blocks = [];
    private readonly TextBlock hint;
    private Button send = null!;
    private Button skipButton = null!;
    private bool closing;
    private bool built;

    /// <param name="card">The waiting card.</param>
    /// <param name="answer">Takes the answers (the runtime's AnswerAsk); null when the card is no longer waiting.</param>
    /// <param name="skip">Skips the card (the runtime's SkipAsk).</param>
    public AskCardForm(AskCard card, Func<IReadOnlyDictionary<string, IReadOnlyList<string>>, CultureInfo, AskCheck?> answer, Action skip, CultureInfo? culture = null)
    {
        this.card = card;
        this.answer = answer;
        this.skip = skip;
        this.culture = culture ?? CultureInfo.CurrentCulture;
        hint = TimelineRows.ChatText("", SwiftFont.TextStyle.Subheadline, color: ChromeTokens.SecondaryText, singleLine: true);
        hint.VerticalAlignment = VerticalAlignment.Center;

        var root = new StackPanel { Spacing = 14 };
        root.Children.Add(Header());
        foreach (var question in card.Questions)
        {
            var block = new Block(question);
            blocks.Add(block);
            root.Children.Add(Question(block));
        }
        root.Children.Add(Footer());

        Child = root;
        Padding = new Thickness(16, 14);
        HorizontalAlignment = HorizontalAlignment.Left;
        MaxWidth = AskCardRow.CardMaxWidth;
        this.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        Background = ChromeTokens.Overlay(0.06).Brush(this);
        AutomationProperties.SetName(this, AskCardStrings.CardName(card));
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Group);
        built = true;
        Refresh();
    }

    /// <summary>The card this form asks.</summary>
    public AskCard Card => card;

    // The card is as wide as the column allows up to a paragraph's width, so it keeps one width whatever it holds (nothing jumps as answers are typed).
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Math.Min(double.IsFinite(availableSize.Width) ? availableSize.Width : AskCardRow.CardMaxWidth, AskCardRow.CardMaxWidth);
        return new Size(width, base.MeasureOverride(new Size(width, availableSize.Height)).Height);
    }

    /// <summary>The Send button (the default action).</summary>
    internal Button SendButton => send;

    internal Button SkipButton => skipButton;

    internal TextBlock Hint => hint;

    /// <summary>The text field of the question with <paramref name="questionId"/>: the answer, the number, or the "something else" field.</summary>
    internal TextBox? FieldOf(string questionId) => blocks.FirstOrDefault(block => block.Question.Id == questionId)?.Field;

    /// <summary>The plates of the question with <paramref name="questionId"/>, in order.</summary>
    internal IReadOnlyList<AskChoicePlate> PlatesOf(string questionId) => blocks.First(block => block.Question.Id == questionId).Plates;

    internal TextBlock ProblemOf(string questionId) => blocks.First(block => block.Question.Id == questionId).Problem;

    // ---- pieces ----

    private Control Header()
    {
        var mark = SwiftImage.SystemName("questionmark.bubble").Font(SwiftFont.System(15)).ForegroundStyle(ChromeTokens.Accent);
        var title = TimelineRows.ChatText(AskCardStrings.Title(card.Questions.Count), SwiftFont.TextStyle.Callout, SwiftFont.Weight.Semibold);
        title.VerticalAlignment = VerticalAlignment.Center;
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { mark, title } };

        var dot = new Border { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        dot.Capsule(RoundedCornerStyle.Continuous);
        dot.Background = ChromeTokens.Accent.Brush(dot);
        var state = TimelineRows.ChatText(AskCardStrings.Waiting, SwiftFont.TextStyle.Subheadline, SwiftFont.Weight.Medium, ChromeTokens.SecondaryText, singleLine: true);
        state.VerticalAlignment = VerticalAlignment.Center;
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { dot, state } };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(status, AskCardStrings.Announcement(card));

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(status, 1);
        header.Children.Add(left);
        header.Children.Add(status);
        return header;
    }

    private Control Question(Block block)
    {
        var question = block.Question;
        var column = new StackPanel { Spacing = 8 };
        if (question.Header is { Length: > 0 } header)
            column.Children.Add(TimelineRows.ChatText(header.ToUpperInvariant(), SwiftFont.TextStyle.Caption, SwiftFont.Weight.Medium, ChromeTokens.SecondaryText));
        var prompt = TimelineRows.SelectableText(wraps: true);
        prompt.Text = question.Prompt;
        ChatFont.Chat(SwiftFont.TextStyle.Body, SwiftFont.Weight.Semibold).Apply(prompt);
        prompt.Foreground = ChromeTokens.PrimaryText.Brush(prompt);
        column.Children.Add(prompt);
        if (!question.Required)
            column.Children.Add(TimelineRows.ChatText(AskCardStrings.Optional, SwiftFont.TextStyle.Subheadline, color: ChromeTokens.SecondaryText));

        switch (question.Kind)
        {
            case AskKind.Single:
            case AskKind.Multiple:
                column.Children.Add(Choices(block));
                break;
            case AskKind.Text:
                column.Children.Add(TextField(block));
                break;
            default:
                column.Children.Add(NumberField(block));
                break;
        }
        var warning = SwiftImage.SystemName("exclamationmark.triangle").Font(SwiftFont.System(12)).ForegroundStyle(ChromeTokens.Danger);
        warning.VerticalAlignment = VerticalAlignment.Center;
        block.Problem.VerticalAlignment = VerticalAlignment.Center;
        block.Problem.IsVisible = false;
        block.ProblemRow.Children.Add(warning);
        block.ProblemRow.Children.Add(block.Problem);
        column.Children.Add(block.ProblemRow);
        return column;
    }

    private Control Choices(Block block)
    {
        var question = block.Question;
        var column = new StackPanel { Spacing = 6 };
        var group = card.Id + "/" + question.Id;
        foreach (var option in question.Options)
        {
            var plate = new AskChoicePlate(option, question.Kind == AskKind.Multiple, group);
            if (question.Kind == AskKind.Single && string.Equals(question.Default, option.Label, StringComparison.OrdinalIgnoreCase)) plate.IsSelected = true;
            plate.Toggled += () => Chose(block, plate);
            block.Plates.Add(plate);
            column.Children.Add(plate.Button);
        }
        if (question.AllowsOther)
        {
            var field = Field(block, AskCardStrings.SomethingElse, multiline: false);
            OnText(field, () =>
            {
                if (question.Kind == AskKind.Single && !string.IsNullOrEmpty(field.Text))
                    foreach (var plate in block.Plates) plate.IsSelected = false;
            });
            column.Children.Add(field);
        }
        return column;
    }

    private void Chose(Block block, AskChoicePlate plate)
    {
        block.Touched = true;
        if (block.Question.Kind == AskKind.Single && plate.IsSelected && block.Field is { } other && !string.IsNullOrEmpty(other.Text)) other.Text = "";
        Refresh();
    }

    private Control TextField(Block block)
    {
        var question = block.Question;
        var field = Field(block, question.Placeholder ?? AskCardStrings.YourAnswer, question.Multiline);
        if (question.Default is { Length: > 0 } initial) field.Text = initial;
        return field;
    }

    private Control NumberField(Block block)
    {
        var question = block.Question;
        var field = Field(block, question.Placeholder ?? "", multiline: false);
        field.MinWidth = 120;
        field.HorizontalAlignment = HorizontalAlignment.Left;
        field.Width = 140;
        if (AskUserSchema.TryNumber(question.Default, CultureInfo.InvariantCulture, out var start)) field.Text = AskUserWords.Shown(start, culture);
        field.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key is not (Key.Up or Key.Down) || e.KeyModifiers != KeyModifiers.None) return;
            Nudge(block, e.Key == Key.Up ? 1 : -1);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        var down = Stepper("minus", AskCardStrings.Decrease, () => Nudge(block, -1));
        var up = Stepper("plus", AskCardStrings.Increase, () => Nudge(block, 1));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { field, down, up } };
        if (question.Unit is { Length: > 0 } unit)
        {
            var shownUnit = TimelineRows.ChatText(unit, SwiftFont.TextStyle.Callout, color: ChromeTokens.SecondaryText);
            shownUnit.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Insert(1, shownUnit);
        }
        var column = new StackPanel { Spacing = 4, Children = { row } };
        if (AskCardStrings.Range(question, culture) is { Length: > 0 } range)
            column.Children.Add(TimelineRows.ChatText(range, SwiftFont.TextStyle.Subheadline, color: ChromeTokens.SecondaryText));
        return column;
    }

    private Button Stepper(string symbol, string name, Action action)
    {
        var mark = SwiftImage.SystemName(symbol).Font(SwiftFont.System(12, weight: SwiftFont.Weight.Semibold)).ForegroundStyle(ChromeTokens.PrimaryText);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border { Width = 28, Height = 28, Child = mark }.RoundedRectangle(8, RoundedCornerStyle.Continuous);
        face.Background = ChromeTokens.Overlay(0.08).Brush(face);
        var button = PlainControls.Button(face, action);
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    private void Nudge(Block block, int direction)
    {
        var question = block.Question;
        if (block.Field is not { } field) return;
        var step = question.Step is > 0 ? question.Step.Value : 1;
        var value = AskUserSchema.TryNumber(field.Text, culture, out var current) ? current + direction * step : question.Min ?? 0;
        if (question.Min is { } low) value = Math.Max(low, value);
        if (question.Max is { } high) value = Math.Min(high, value);
        value = question.WholeNumber ? Math.Round(value) : Math.Round(value, 10);
        block.Touched = true;
        field.Text = AskUserWords.Shown(value, culture);
        field.CaretIndex = field.Text.Length;
    }

    private TextBox Field(Block block, string placeholder, bool multiline)
    {
        var question = block.Question;
        var field = new TextBox
        {
            PlaceholderText = placeholder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 64 : 0,
            MaxHeight = multiline ? 200 : double.PositiveInfinity,
            MaxLength = AskUserSchema.MaxText,
        };
        Marrow.App.Port.ChatDialogControls.TextEditor(field, style: "RoundedBorder");
        AutomationProperties.SetName(field, question.Prompt);
        OnText(field, () => { block.Touched = true; Refresh(); });
        // Tunnelled, so the field's own handling of Return (a new line) does not get there first.
        field.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key != Key.Enter) return;
            if (multiline && e.KeyModifiers != KeyModifiers.Control) return; // a plain Return writes a new line; Ctrl+Return sends
            e.Handled = true;
            Send();
        }, RoutingStrategies.Tunnel);
        block.Field = field;
        return field;
    }

    // The change of a field's text as it happens (the TextChanged event follows the dispatcher, a beat late).
    private static void OnText(TextBox field, Action changed) =>
        field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) changed(); };

    private Control Footer()
    {
        skipButton = TimelineRows.GlassCommand(AskCardStrings.Skip, Skip, prominent: false);
        ToolTip.SetTip(skipButton, AskCardStrings.SkipHelp);
        AutomationProperties.SetName(skipButton, AskCardStrings.Skip);
        var many = card.Questions.Count > 1;
        send = TimelineRows.GlassCommand(many ? AskCardStrings.SendAnswers : AskCardStrings.SendAnswer, Send, prominent: true);
        send.IsDefault = false; // Return belongs to the field it is typed in; the card never takes the window's default action
        AutomationProperties.SetName(send, many ? AskCardStrings.SendAnswers : AskCardStrings.SendAnswer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { skipButton, send } };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(hint);
        footer.Children.Add(buttons);
        return footer;
    }

    // ---- state ----

    private IReadOnlyDictionary<string, IReadOnlyList<string>> Given()
    {
        var given = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var block in blocks)
        {
            var values = block.Plates.Where(plate => plate.IsSelected).Select(plate => plate.Label).ToList();
            if (block.Field is { Text: { Length: > 0 } typed }) values.Add(typed);
            given[block.Question.Id] = values;
        }
        return given;
    }

    private void Refresh()
    {
        if (!built) return; // a default being put into a field while the card is still being made
        var check = AskUserSchema.Check(card, Given(), culture);
        foreach (var block in blocks)
        {
            var typed = !string.IsNullOrEmpty(block.Field?.Text);
            var problem = check.Problems.TryGetValue(block.Question.Id, out var found) ? found : null;
            // A problem shows once the person has worked on the question and there is something to say about what they gave.
            var shown = problem is not null && block.Touched && (typed || block.Plates.Any(plate => plate.IsSelected));
            block.Problem.Text = shown ? problem! : "";
            block.Problem.IsVisible = shown;
            block.ProblemRow.IsVisible = shown;
        }
        send.IsEnabled = check.IsValid && !closing;
        hint.Text = check.IsValid ? "" : AskCardStrings.StillToAnswer(check.Problems.Count);
        ToolTip.SetTip(send, check.IsValid ? card.Questions.Count > 1 ? AskCardStrings.SendAnswersHelp : AskCardStrings.SendHelp : hint.Text);
    }

    private void Send()
    {
        if (closing) return;
        foreach (var block in blocks) block.Touched = true;
        var check = answer(Given(), culture);
        if (check is { IsValid: true }) { closing = true; send.IsEnabled = false; skipButton.IsEnabled = false; return; }
        Refresh();
    }

    private void Skip()
    {
        if (closing) return;
        closing = true;
        send.IsEnabled = false;
        skipButton.IsEnabled = false;
        skip();
    }
}
