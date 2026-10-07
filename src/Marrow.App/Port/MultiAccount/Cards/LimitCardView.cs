// PORT-ONLY: MA-3b-11, page :368-384 / :2925-2942, layout A; OD-0116.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Cards;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Cards;

internal sealed class LimitCardView : Decorator
{
    private readonly LimitCardModel model;
    private readonly Dictionary<DecisionKind, string> choices = [];
    private readonly TextBlock failure = LibraryUi.Text("", 12, secondary: true, wrap: true);
    internal IReadOnlyList<Button> Actions { get; }
    internal CzRadioGroup Remember { get; }
    internal IReadOnlyDictionary<DecisionKind, CzRadioGroup> Options { get; }
    internal event Action? Answered;
    internal LimitCardView(LimitCardModel model, IReadOnlyList<string>? accountNames = null)
    {
        this.model = model; Focusable = true; this.Named(model.Title, "limit-card");
        var stack = new StackPanel { Spacing = 10 };
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*"), ColumnSpacing = 9 };
        var icon = LibraryUi.Glyph("gauge.with.needle", 16, MultiAccountPalette.Hot); icon.Margin = new Thickness(0, 2, 0, 0);
        var words = new CardTitleBlock { Text = model.Title, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Foreground = ChromeTokens.PrimaryText.Brush(this), LineHeight = 19 };
        SwiftFont.System(13.5).Apply(words); CardTitle.Emphasize(words, accountNames ?? []); Grid.SetColumn(words, 1); title.Children.Add(icon); title.Children.Add(words); stack.Children.Add(title);
        if (model.Explanation is { } explanation)
        {
            var text = new CardTitleBlock { Text = explanation, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(25, -4, 0, 0), Foreground = MultiAccountPalette.Secondary.Brush(this) };
            SwiftFont.System(12).Apply(text);
            if (model.Kind == CardKind.MidReply) CardTitle.Emphasize(text, ["Finish now", "Stop"]);
            stack.Children.Add(text);
        }
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(25, 0, 0, 0) };
        Actions = model.Buttons.Select(spec =>
        {
            var button = PlainControls.Button(LibraryUi.Text(spec.Text, 12.5, weight: SwiftFont.Weight.Medium), () => Answer(spec.Value));
            button.Height = 28; button.Padding = new Thickness(13, 0); button.Focusable = true; button.Named(spec.Text, "limit-action-" + spec.Value);
            if (spec.Primary) button.Classes.Add("primary");
            button.Background = spec.Primary ? MultiAccountPalette.Accent.Brush(button) : CardColors.Capsule.Brush(button);
            if (spec.Primary && button.Content is TextBlock ink) ink.Foreground = MultiAccountPalette.AccentInk.Brush(button);
            if (!spec.Primary) { button.BorderBrush = CardColors.Divider.Brush(button); button.BorderThickness = new Thickness(1); }
            AccountButtonFace.Apply(button, 14); actions.Children.Add(button); return button;
        }).ToArray(); stack.Children.Add(actions);
        var options = new StackPanel { Spacing = 6, Margin = new Thickness(25, 0, 0, 0) };
        var groups = new Dictionary<DecisionKind, CzRadioGroup>();
        foreach (var spec in model.Groups)
        {
            var group = Segment(spec.Label, spec.Options.Select(b => (b.Value, b.Text)).ToArray(), spec.Options.FirstOrDefault(b => b.Selected)?.Value);
            // The existing CzButton capsule radius clamps to half its 22px height (11px ends).
        foreach (var button in group.Buttons) button.Named(button.Id, "limit-option-" + spec.Kind + "-" + button.Id);
            if (group.SelectedId.Length > 0) choices[spec.Kind] = group.SelectedId;
            group.SelectionChanged += value => { choices[spec.Kind] = value; model.Select(spec.Kind, value); Refresh(); };
            groups.Add(spec.Kind, group); options.Children.Add(OptionLine(spec.Label, group));
        }
        Options = groups;
        Remember = Segment(model.RememberLabel, [("once", "Just this time"), ("thread", "This thread"), ("all", "All threads")], "once");
        Remember.SelectionChanged += value => model.Scope = value switch { "thread" => RememberScope.ThisThread, "all" => RememberScope.AllThreads, _ => RememberScope.JustThisTime };
        options.Children.Add(OptionLine(model.RememberLabel, Remember));
        stack.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = CardColors.Divider.Brush(this), Padding = new Thickness(0, 9, 0, 0), Child = options });
        failure.IsVisible = false; stack.Children.Add(failure);
        var surface = new Border { Padding = new Thickness(14, 12), Child = stack }.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(14, RoundedCornerStyle.Continuous))); Child = surface;
        model.Changed += Refresh; DetachedFromVisualTree += (_, _) => model.Changed -= Refresh; AttachedToVisualTree += (_, _) => { model.Changed -= Refresh; model.Changed += Refresh; Refresh(); };
        Refresh();
    }
    private static CzRadioGroup Segment(string label, IReadOnlyList<(string Id, string Text)> options, string? selected)
    {
        // CzSegmentedControl requires a pick during Arrange. The shared radio group supports an explicitly empty first-time choice.
        var group = new CzRadioGroup(label, options, selected) { Spacing = 2 };
        foreach (var button in group.Buttons) { button.Height = 22; button.Padding = new Thickness(9, 0); if (button.Child is TextBlock text) text.FontSize = 11.5; }
        group.SelectionChanged += _ => Paint(); Paint(); return group;
        void Paint() { foreach (var button in group.Buttons) { button.Background = button.Picked ? ChromeTokens.Accent.Opacity(.22).Brush(button) : Brushes.Transparent; button.InvalidateVisual(); } }
    }
    private static Control OptionLine(string label, CzRadioGroup group)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        var words = LibraryUi.Text(label, 12, secondary: true); words.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(words);
        var segments = new Border { Padding = new Thickness(3), Background = ChromeTokens.Overlay(.06).Brush(group), Child = group, HorizontalAlignment = HorizontalAlignment.Left }.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        Grid.SetColumn(segments, 1); row.Children.Add(segments); return row;
    }
    private void Refresh()
    {
        for (var i = 0; i < Actions.Count; i++)
        {
            var value = model.Buttons[i].Value; var moving = value is "switch" or "finish" or "goBack" or "continue";
            Actions[i].IsEnabled = !model.Answered && !model.Answering && (!moving || model.Groups.All(g => choices.ContainsKey(g.Kind)));
        }
        Remember.IsEnabled = !model.Answered && !model.Answering;
        foreach (var group in Options.Values) group.IsEnabled = Remember.IsEnabled;
    }
    private async void Answer(string value)
    {
        if (model.Answered || model.Answering) return;
        try { failure.IsVisible = false; await model.AnswerAsync(value); Answered?.Invoke(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { failure.Text = error.Message; failure.IsVisible = true; }
        Refresh();
    }
}
