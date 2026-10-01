using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/QuestionTab.swift (B2 1fbf81e), :1-332.

/// <summary>The agent's questions as a badge at the end of the conversation.</summary>
public sealed class QuestionBadgeRow : Decorator
{
    // QuestionBadgeRow.init and body:5-20.
    public QuestionBadgeRow(QuestionRequest request, ThreadRuntime runtime)
    {
        var isShowingQuestions = false;
        Child = new ChatBadge(
            request.Questions.Count == 1
                ? "1 question"
                : request.Questions.Count.ToString(CultureInfo.InvariantCulture) + " questions",
            new Binding<bool>(() => isShowingQuestions, value => isShowingQuestions = value),
            () => SwiftImage.SystemName("questionmark.bubble").ForegroundStyle(Chrome.Accent),
            () => new QuestionSheet(request, runtime),
            caption: "to answer",
            needsAttention: true);
    }
}

/// <summary>The questions, one per page, in the badge's popover.</summary>
public sealed class QuestionSheet : Decorator
{
    // QuestionSheet dimensions:30-33.
    private const double QuestionSheetMaxHeight = 360; // Swift: maxHeight
    private const double QuestionSheetWidth = 460; // Swift: width

    // QuestionSheet inputs, state and measured page height:35-47.
    private readonly QuestionRequest request;
    private readonly ThreadRuntime runtime;
    private ValueMap<string, ValueList<string>> selections = ValueMap<string, ValueList<string>>.Empty;
    private ValueMap<string, string> written = ValueMap<string, string>.Empty;
    private readonly List<Border> dots = [];
    private readonly List<Button> dotButtons = [];
    private readonly TextBlock counter = new();
    private readonly Border footer = new();
    private readonly StackPanel pageStrip = new();
    private readonly Border pageViewport = new();
    private readonly double pageHeight;
    private int page;

    // QuestionSheet.init and body:49-170.
    public QuestionSheet(QuestionRequest request, ThreadRuntime runtime)
    {
        this.request = request;
        this.runtime = runtime;

        foreach (var question in request.Questions)
        {
            var recommended = Recommended(question);
            if (recommended.Count != 0)
                selections = SetMapItem(selections, question.Id, recommended);
        }

        var largest = 0.0;
        foreach (var question in request.Questions)
        {
            // :58 selections and written are .constant([:]): every choice is measured unselected, in the regular font.
            var probe = new QuestionPage(question,
                new Binding<ValueMap<string, ValueList<string>>>(() => ValueMap<string, ValueList<string>>.Empty, _ => { }),
                new Binding<ValueMap<string, string>>(() => ValueMap<string, string>.Empty, _ => { }),
                () => Advance(), () => { });
            probe.Measure(new Size(QuestionSheetWidth, double.PositiveInfinity));
            largest = Math.Max(largest, probe.DesiredSize.Height);
        }

        pageHeight = Math.Min(Math.Max(largest, 120), QuestionSheetMaxHeight);

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        if (request.Questions.Count > 1)
        {
            var dotRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            for (var index = 0; index < request.Questions.Count; index++)
            {
                var dot = new Border
                {
                    Width = 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = Chrome.Overlay(0.18).Brush(this),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var capturedIndex = index;
                var button = new Button
                {
                    Content = dot,
                    Width = 6,
                    Height = 6,
                    MinWidth = 0,
                    MinHeight = 0,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                };
                button.Click += (_, _) => SetPage(capturedIndex);
                dots.Add(dot);
                dotButtons.Add(button);
                dotRow.Children.Add(button);
            }

            var counterFont = SwiftFont.System(SwiftFont.TextStyle.Subheadline).MonospacedDigit();
            counterFont.Apply(counter);
            counter.FontFeatures = counterFont.Resolve().FontFeatures;
            counter.Foreground = Chrome.SecondaryText.Brush(counter);

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(dotRow, 0);
            Grid.SetColumn(counter, 1);
            header.Children.Add(dotRow);
            header.Children.Add(counter);
            root.Children.Add(new Border { Padding = new Thickness(16, 12, 16, 6), Child = header });
        }

        pageStrip.Orientation = Orientation.Horizontal;
        for (var index = 0; index < request.Questions.Count; index++)
        {
            var question = request.Questions[index];
            var capturedIndex = index;
            var questionPage = new QuestionPage(
                question,
                BindingForSelections(),
                BindingForWritten(),
                () => Advance(),
                () =>
                {
                    if (!question.AllowsMultiple && capturedIndex < request.Questions.Count - 1)
                        Advance(0.18);
                    else
                        RefreshState();
                });
            pageStrip.Children.Add(new ScrollViewer
            {
                Content = questionPage,
                Width = QuestionSheetWidth,
                Height = pageHeight,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            });
        }

        pageViewport.Width = QuestionSheetWidth;
        pageViewport.Height = pageHeight;
        pageViewport.ClipToBounds = true;
        pageViewport.Child = pageStrip;
        root.Children.Add(pageViewport);
        root.Children.Add(new Border { Height = 1, Background = Chrome.Overlay(0.18).Brush(this) });
        footer.Child = BuildFooter();
        root.Children.Add(footer);
        Child = root;
        Width = QuestionSheetWidth;
        RefreshState();
    }

    // QuestionSheet's two bindings:41-43, :201-202, :226-229.
    private Binding<ValueMap<string, ValueList<string>>> BindingForSelections() =>
        new(() => selections, value =>
        {
            selections = value;
            RefreshState();
        });

    // QuestionSheet's written-answer binding:42, :202, :226-229.
    private Binding<ValueMap<string, string>> BindingForWritten() =>
        new(() => written, value =>
        {
            written = value;
            RefreshState();
        });

    // QuestionSheet's recommended selections:52-57, :68-74.
    private static ValueList<string> Recommended(QuestionRequest.Question question)
    {
        var matches = ValueList<string>.Empty;
        foreach (var choice in question.Choices)
            if (SwiftString.LocalizedCaseInsensitiveContains(choice.Label, "recommended"))
                matches = matches.Appending(choice.Label);

        if (matches.Count == 0 || question.AllowsMultiple)
            return matches;
        return ValueList<string>.Empty.Appending(matches[0]);
    }

    // Immutable map update used by the sheet's state:52-57.
    private static ValueMap<string, TValue> SetMapItem<TValue>(ValueMap<string, TValue> map, string key, TValue value)
    {
        var items = new Dictionary<string, TValue>(SwiftStringComparer.Instance);
        foreach (var pair in map)
            items[pair.Key] = pair.Value;
        items[key] = value;
        return new ValueMap<string, TValue>(items, SwiftStringComparer.Instance);
    }

    // The collected answer map:78-80.
    private ValueMap<string, ValueList<string>> Answers() =>
        QuestionAnswers.Collect(request, selections, written);

    // Page dots, counter and page offset:76-80, :82-103, :114-117.
    private void RefreshState()
    {
        var answers = Answers();
        if (request.Questions.Count > 1)
        {
            counter.Text = (page + 1).ToString(CultureInfo.InvariantCulture)
                + " of "
                + request.Questions.Count.ToString(CultureInfo.InvariantCulture);

            for (var index = 0; index < dots.Count; index++)
            {
                var answered = answers.TryGetValue(request.Questions[index].Id, out var values) && values.Count != 0;
                var width = index == page ? 18 : 6;
                dots[index].Width = width;
                dotButtons[index].Width = width;
                dots[index].Background = (index == page
                    ? Chrome.Accent
                    : answered ? Chrome.Accent.Opacity(0.35) : Chrome.Overlay(0.18)).Brush(dots[index]);
            }
        }

        pageStrip.RenderTransform = new TranslateTransform(-page * QuestionSheetWidth, 0);
        RefreshFooter();
    }

    // Footer refresh:132-167.
    private void RefreshFooter() => footer.Child = BuildFooter();

    // Footer controls and their exact strings:121-167.
    private Control BuildFooter()
    {
        var answers = Answers();
        var unanswered = QuestionAnswers.UnansweredCount(request, answers);
        var isComplete = unanswered == 0;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto") };

        if (request.Questions.Count > 1)
        {
            // :124-129 .buttonStyle(.bordered) (SystemButtons: the Windows standard button, ED-229 proposal (4))
            var previous = SystemButtons.Bordered(SwiftImage.SystemName("chevron.left"), () => SetPage(Math.Max(0, page - 1)));
            previous.IsEnabled = page != 0;
            previous.Margin = new Thickness(0, 0, 8, 0);
            ToolTip.SetTip(previous, "Previous question");
            Grid.SetColumn(previous, 0);
            row.Children.Add(previous);
        }

        if (unanswered > 0)
        {
            var label = new TextBlock
            {
                Text = unanswered == 1
                    ? "1 question still to answer"
                    : unanswered.ToString(CultureInfo.InvariantCulture) + " questions still to answer",
                MaxLines = 1,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(label);
            label.Foreground = Chrome.SecondaryText.Brush(label);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
        }

        var skip = SystemButtons.Bordered("Skip", () => Send(ValueMap<string, ValueList<string>>.Empty)); // :139-140 .bordered
        skip.Margin = new Thickness(0, 0, 8, 0);
        ToolTip.SetTip(skip, "Sends no answers and lets the agent carry on");
        Grid.SetColumn(skip, 3);
        row.Children.Add(skip);

        if (page < request.Questions.Count - 1 && !isComplete)
        {
            var next = SystemButtons.BorderedProminent("Next", () => Advance()); // :145-146 .borderedProminent
            next.IsDefault = true; // :147 .keyboardShortcut(.defaultAction); the last child of the HStack: spacing 8 only lies between neighbours
            ToolTip.SetTip(next, "Goes to the next question");
            Grid.SetColumn(next, 4);
            row.Children.Add(next);
        }
        else
        {
            if (page < request.Questions.Count - 1)
            {
                var next = SystemButtons.Bordered("Next", () => Advance()); // :152-153 .bordered
                next.Margin = new Thickness(0, 0, 8, 0);
                ToolTip.SetTip(next, "Goes to the next question");
                Grid.SetColumn(next, 4);
                row.Children.Add(next);
            }

            var send = SystemButtons.BorderedProminent("Send", () => Send(answers)); // :156-157 .borderedProminent
            send.IsEnabled = isComplete; // :159 .disabled(!isComplete)
            send.IsDefault = true; // :158 .keyboardShortcut(.defaultAction)
            ToolTip.SetTip(send, isComplete
                ? "Sends your answers"
                : request.Questions.Count == 1
                    ? "Answer the question to send"
                    : "Answer all " + request.Questions.Count.ToString(CultureInfo.InvariantCulture) + " questions to send");
            Grid.SetColumn(send, 5);
            row.Children.Add(send);
        }

        return new Border { Padding = new Thickness(16, 10), Child = row };
    }

    // Dot and Previous navigation:86-88, :123-131.
    private void SetPage(int index) =>
        Transaction.WithAnimation(Chrome.PanelSlide, () =>
        {
            page = index;
            RefreshState();
        });

    // Return and single-choice advance behavior:108, :172-187.
    private void Advance(double delay = 0)
    {
        if (page >= request.Questions.Count - 1)
        {
            var answers = Answers();
            if (QuestionAnswers.UnansweredCount(request, answers) == 0)
                Send(answers);
            return;
        }

        if (delay > 0)
        {
            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(delay));
                Transaction.WithAnimation(Chrome.PanelSlide, () =>
                {
                    page++;
                    RefreshState();
                });
            });
            return;
        }

        Transaction.WithAnimation(Chrome.PanelSlide, () =>
        {
            page++;
            RefreshState();
        });
    }

    // Close the badge popover before answering the runtime request:189-194.
    private void Send(ValueMap<string, ValueList<string>> answers)
    {
        Popovers.GetClosePopover(this)?.Invoke();
        runtime.Answer(request, answers);
    }
}

/// <summary>One question page: header, prompt, choices and an optional written-answer field.</summary>
internal sealed class QuestionPage : Decorator
{
    // QuestionPage inputs and callbacks:199-206.
    private readonly QuestionRequest.Question question;
    private readonly Binding<ValueMap<string, ValueList<string>>> selections;
    private readonly Binding<ValueMap<string, string>> written;
    private readonly Action onSubmit;
    private readonly Action onPick;
    private readonly List<Action> updateChoiceRows = [];

    // Question page layout and free-answer field:208-245.
    public QuestionPage(
        QuestionRequest.Question question,
        Binding<ValueMap<string, ValueList<string>>> selections,
        Binding<ValueMap<string, string>> written,
        Action onSubmit,
        Action onPick)
    {
        this.question = question;
        this.selections = selections;
        this.written = written;
        this.onSubmit = onSubmit;
        this.onPick = onPick;

        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        if (SwiftString.Count(question.Header) != 0)
        {
            var header = new TextBlock { Text = SwiftString.Uppercased(question.Header), TextWrapping = TextWrapping.Wrap }; // a Text wraps at the width it is offered
            SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium).Apply(header);
            header.Foreground = Chrome.SecondaryText.Brush(header);
            content.Children.Add(header);
        }

        var prompt = new SelectableTextBlock { Text = question.Prompt, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(prompt);
        prompt.Foreground = Chrome.PrimaryText.Brush(prompt);
        content.Children.Add(prompt);

        foreach (var choice in question.Choices)
            content.Children.Add(BuildChoiceRow(choice));

        if (question.AllowsOther || question.Choices.Count == 0)
        {
            var field = new TextBox
            {
                PlaceholderText = question.IsSecret || question.Choices.Count == 0
                    ? "Your answer"
                    : "Something else",
                PasswordChar = question.IsSecret ? '\U00002022' : '\0', // U+2022 BULLET
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            // :236 .textFieldStyle(.roundedBorder), as ChatView's .roundedBorder fields (ChatView.cs, STOP #5 bezel metrics).
            // The app carries no theme package: without a template the field drew nothing and could not be typed into.
            Marrow.App.Port.ChatDialogControls.TextEditor(field, style: "RoundedBorder");
            if (written.Value.TryGetValue(question.Id, out var currentText))
                field.Text = currentText;
            field.TextChanged += (_, _) =>
                written.Value = SetWrittenItem(written.Value, question.Id, field.Text ?? "");
            field.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                onSubmit();
                e.Handled = true;
            };
            content.Children.Add(field);
        }

        Child = new Border
        {
            Padding = new Thickness(16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content,
        };
        Width = 460;
    }

    // Update a written answer without mutating the value map:226-229.
    private static ValueMap<string, string> SetWrittenItem(ValueMap<string, string> map, string key, string value)
    {
        var items = new Dictionary<string, string>(SwiftStringComparer.Instance);
        foreach (var pair in map)
            items[pair.Key] = pair.Value;
        items[key] = value;
        return new ValueMap<string, string>(items, SwiftStringComparer.Instance);
    }

    // A choice row and its toggle behavior:256-303.
    private Control BuildChoiceRow(QuestionRequest.Choice choice)
    {
        var selected = IsSelected(choice.Label);
        var shown = BareLabel(choice.Label);
        var symbolSlot = new StackPanel { Orientation = Orientation.Horizontal, Width = 18 };
        var symbol = SwiftImage.SystemName(ChoiceSymbol(selected, question.AllowsMultiple))
            .Font(SwiftFont.System(14))
            .ForegroundStyle(selected ? Chrome.Accent : Chrome.SecondaryText);
        symbol.Width = 14;
        symbol.Margin = new Thickness(0, 1, 0, 0);
        symbolSlot.Children.Add(symbol);

        var label = new TextBlock { Text = shown.Label, TextWrapping = TextWrapping.Wrap };
        ApplyLabelFont(label, selected);
        label.Foreground = Chrome.PrimaryText.Brush(label);

        var choiceLabel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(label, 0);
        choiceLabel.Children.Add(label);
        if (shown.IsRecommended)
        {
            var pillText = new TextBlock { Text = "Recommended" };
            SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Medium).Apply(pillText);
            pillText.Foreground = Chrome.Accent.Brush(pillText);
            var pill = new Border { Padding = new Thickness(6, 2), Child = pillText }.Capsule(RoundedCornerStyle.Continuous);
            pill.Background = Chrome.Accent.Opacity(0.14).Brush(pill);
            Grid.SetColumn(pill, 1);
            choiceLabel.Children.Add(pill);
        }

        var textColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        textColumn.Children.Add(choiceLabel);
        if (choice.Detail is { Length: > 0 } detail)
        {
            var detailText = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap };
            SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(detailText);
            detailText.Foreground = Chrome.SecondaryText.Brush(detailText);
            textColumn.Children.Add(detailText);
        }

        var rowContent = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*"), ColumnSpacing = 10 };
        Grid.SetColumn(symbolSlot, 0);
        Grid.SetColumn(textColumn, 1);
        rowContent.Children.Add(symbolSlot);
        rowContent.Children.Add(textColumn);

        var plate = new Border { Padding = new Thickness(12, 9), Child = rowContent }
            .RoundedRectangle(10, RoundedCornerStyle.Continuous);
        plate.Background = (selected ? Chrome.Accent.Opacity(0.12) : Chrome.Overlay(0.06)).Brush(plate);

        void UpdateRow()
        {
            var isSelected = IsSelected(choice.Label);
            symbolSlot.Children.Clear();
            var updatedSymbol = SwiftImage.SystemName(ChoiceSymbol(isSelected, question.AllowsMultiple))
                .Font(SwiftFont.System(14))
                .ForegroundStyle(isSelected ? Chrome.Accent : Chrome.SecondaryText);
            updatedSymbol.Width = 14;
            updatedSymbol.Margin = new Thickness(0, 1, 0, 0);
            symbolSlot.Children.Add(updatedSymbol);
            ApplyLabelFont(label, isSelected);
            plate.Background = (isSelected ? Chrome.Accent.Opacity(0.12) : Chrome.Overlay(0.06)).Brush(plate);
        }

        var button = new Button
        {
            Content = plate,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) =>
        {
            var current = selections.Value.TryGetValue(question.Id, out var values)
                ? values
                : ValueList<string>.Empty;
            var wasSelected = ContainsLabel(current, choice.Label);
            ValueList<string> updated;
            if (question.AllowsMultiple)
            {
                updated = ValueList<string>.Empty;
                foreach (var value in current)
                    if (!SwiftString.Equal(value, choice.Label))
                        updated = updated.Appending(value);
                if (!wasSelected)
                    updated = updated.Appending(choice.Label);
            }
            else
            {
                updated = wasSelected ? ValueList<string>.Empty : ValueList<string>.Empty.Appending(choice.Label);
            }

            selections.Value = SetSelectionItem(selections.Value, question.Id, updated);
            Transaction.WithAnimation(Chrome.Hover, () =>
            {
                foreach (var update in updateChoiceRows)
                    update();
            });
            if (ContainsLabel(updated, choice.Label))
                onPick();
        };

        updateChoiceRows.Add(UpdateRow);
        return button;
    }

    // Choice-label font style:277-279.
    private static void ApplyLabelFont(TextBlock label, bool selected) =>
        SwiftFont.System(
            SwiftFont.TextStyle.Body,
            weight: selected ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Regular).Apply(label);

    // Update selected labels immutably:256-267.
    private static ValueMap<string, ValueList<string>> SetSelectionItem(
        ValueMap<string, ValueList<string>> map,
        string key,
        ValueList<string> value)
    {
        var items = new Dictionary<string, ValueList<string>>(SwiftStringComparer.Instance);
        foreach (var pair in map)
            items[pair.Key] = pair.Value;
        items[key] = value;
        return new ValueMap<string, ValueList<string>>(items, SwiftStringComparer.Instance);
    }

    // Selection lookup and label equality:257, :260-267.
    private bool IsSelected(string label) =>
        selections.Value.TryGetValue(question.Id, out var selected) && ContainsLabel(selected, label);

    private static bool ContainsLabel(ValueList<string> values, string label)
    {
        foreach (var value in values)
            if (SwiftString.Equal(value, label))
                return true;
        return false;
    }

    // Radio/checkbox symbols:305-312.
    private static string ChoiceSymbol(bool selected, bool multiple) =>
        (selected, multiple) switch
        {
            (true, true) => "checkmark.square.fill",
            (false, true) => "square",
            (true, false) => "checkmark.circle.fill",
            (false, false) => "circle",
        };

    // Strip only the trailing recommendation suffix; otherwise keep the label:247-254.
    private static (string Label, bool IsRecommended) BareLabel(string label)
    {
        const string suffix = " (Recommended)";
        if (SwiftString.Count(label) >= SwiftString.Count(suffix) &&
            CultureInfo.CurrentCulture.CompareInfo.Compare(
                SwiftString.Suffix(label, SwiftString.Count(suffix)),
                suffix,
                CompareOptions.IgnoreCase) == 0)
            return (SwiftString.DropLast(label, SwiftString.Count(suffix)), true);

        return (label, SwiftString.LocalizedCaseInsensitiveContains(label, "recommended"));
    }
}

/// <summary>Per-question answer collection and unanswered count.</summary>
internal static class QuestionAnswers
{
    // Collect picked labels, then trimmed written text:316-327.
    public static ValueMap<string, ValueList<string>> Collect(
        QuestionRequest request,
        IReadOnlyDictionary<string, ValueList<string>> selections,
        IReadOnlyDictionary<string, string> written)
    {
        var result = new Dictionary<string, ValueList<string>>(SwiftStringComparer.Instance);
        foreach (var question in request.Questions)
        {
            var values = selections.TryGetValue(question.Id, out var selected)
                ? selected
                : ValueList<string>.Empty;
            if (written.TryGetValue(question.Id, out var text))
            {
                var trimmed = FoundationText.Trim(text, includeNewlines: true);
                if (SwiftString.Count(trimmed) != 0)
                    values = values.Appending(trimmed);
            }
            result[question.Id] = values;
        }
        return new ValueMap<string, ValueList<string>>(result, SwiftStringComparer.Instance);
    }

    // Count questions whose composed answers are empty:329-331.
    public static long UnansweredCount(
        QuestionRequest request,
        IReadOnlyDictionary<string, ValueList<string>> answers)
    {
        long unanswered = 0;
        foreach (var question in request.Questions)
            if (!answers.TryGetValue(question.Id, out var values) || values.Count == 0)
                unanswered++;
        return unanswered;
    }
}
