// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.3, 11.5, 11.7; the approved canvas "Usage reserves"; unit AS-13; ED-NEW-dextra-as13-10). No Swift twin.
// Settings > Reserves: the owner's standing rules with a line to say a new one in ("always keep 25% on Codex A"), the one question a
// rule that did not name its accounts gets, the two switches ("Hold reserves until the reset", "Always ask before starting"), and a
// bar per account window showing what is used, the floor that binds now and, when it is easing toward a reset, where it began. The
// page is its own class so the Settings that is moving into the main window can mount it as it is.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class ReservesView : Decorator
{
    public const double NarrowWidth = 900;
    public const double Gutter = 16;
    public const string Title = "Usage reserves";
    public const string RulesTitle = "Standing rules";
    public const string AccountsTitle = "Accounts";
    public const string FieldPrompt = "Say a rule, e.g. always keep 25% on Codex A";
    public const string AddTitle = "Add";
    public const string Hint = "A task's “keep 30%” means 30% of what is left now. A standing rule means 30% of the whole window. The plan always says which it used.";
    public const string EmptyRules = "No standing rules yet. Say one above.";
    public const string EmptyAccounts = "No account has reported its usage yet. Its bar appears after the first poll.";
    public const string NewerVersion = "These rules were saved by a newer version of nth. They show here and nothing changes them.";
    public const string HoldTitle = "Hold reserves until the reset";
    public const string HoldDetail = "Standing floors keep their level near a reset instead of easing away.";
    public const string AskTitle = "Always ask before starting";
    public const string AskDetail = "Every plan waits for your Start.";

    private readonly ReservesModel model;
    private readonly ReserveGuard? guard;
    private readonly TextBlock subtitle = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel rules = new() { Orientation = Orientation.Vertical, Spacing = 6 };
    private readonly TextBlock emptyRules = new() { Text = EmptyRules, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock newer = new() { Text = NewerVersion, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel question = new() { Orientation = Orientation.Vertical, Spacing = 6 };
    private readonly LiveStatusText problem = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel accountRows = new() { Orientation = Orientation.Vertical, Spacing = 10 };
    private readonly TextBlock emptyAccounts = new() { Text = EmptyAccounts, TextWrapping = TextWrapping.Wrap };
    private readonly Grid body = new();
    private readonly Border rulesCard;
    private readonly Border accountsCard;
    private readonly Dictionary<string, ReserveBarRow> rows = new(StringComparer.Ordinal);
    private readonly TextBox ruleBox;
    private bool stacked;
    private bool refreshing;

    public ReservesView(ReservesModel model, ReserveGuard? guard = null)
    {
        this.model = model;
        this.guard = guard;
        ruleBox = PlainControls.TextField(FieldPrompt, new Binding<string>(() => model.Draft, value => { if (!refreshing) model.Draft = value; }));
        AutomationProperties.SetName(ruleBox, "New reserve rule");
        // Tunnelled, so the box's own handling of Enter does not swallow it.
        ruleBox.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key == Key.Enter) { model.Submit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { model.Cancel(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var heading = Text(Title, 20, SwiftFont.Weight.Semibold, primary: true);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(subtitle);
        subtitle.Foreground = Chrome.SecondaryText.Brush(subtitle);
        subtitle.Text = "Keep part of an account's allowance for yourself. nth plans inside the rest, shows a one-line plan, tracks live and pauses before it would touch a reserve. The allowance is a ceiling, not a target.";
        Secondary(emptyRules);
        Secondary(emptyAccounts);
        Secondary(newer);
        newer.Foreground = Chrome.Warning.Brush(newer);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(problem);
        problem.Foreground = Chrome.Danger.Brush(problem);
        AutomationProperties.SetLiveSetting(problem, AutomationLiveSetting.Polite);

        rulesCard = Card(RulesPanel());
        accountsCard = Card(new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Children = { Text(AccountsTitle, 15, SwiftFont.Weight.Semibold, primary: true), accountRows, emptyAccounts } });
        body.Children.Add(rulesCard);
        body.Children.Add(accountsCard);
        LayOut(stackedLayout: false);

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Margin = new Thickness(Gutter), Children = { heading, subtitle, body } };
        Child = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        SizeChanged += (_, e) => LayOut(e.NewSize.Width < NarrowWidth);
        AttachedToVisualTree += (_, _) =>
        {
            model.Changed -= OnChanged;
            model.Changed += OnChanged;
            if (guard is not null)
            {
                guard.Changed -= OnChanged;
                guard.Changed += OnChanged;
            }
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            model.Changed -= OnChanged;
            if (guard is not null) guard.Changed -= OnChanged;
        };
        Refresh();
    }

    public ReservesModel Model => model;

    public bool IsStacked => stacked;

    public TextBox Field => ruleBox;

    internal Button? AddButton { get; private set; }

    internal Button? EveryButton { get; private set; }

    internal Button? OnlyButton { get; private set; }

    internal SettingsSwitch HoldSwitch { get; private set; } = null!;

    internal SettingsSwitch AskSwitch { get; private set; } = null!;

    /// <summary>The standing rules' titles as shown, in order.</summary>
    public IReadOnlyList<string> RuleTitles => [.. rules.Children.OfType<Border>().Select(b => AutomationProperties.GetName(b) ?? "")];

    public IReadOnlyList<Button> RemoveButtons => [.. rules.Children.OfType<Border>().Select(b => (Button)b.Tag!)];

    public IReadOnlyList<ReserveBarRow> AccountBars => [.. accountRows.Children.OfType<ReserveBarRow>()];

    public string ProblemText => problem.Text ?? "";

    public string QuestionText => (question.Children.FirstOrDefault() as TextBlock)?.Text ?? "";

    public bool ShowsQuestion => question.IsVisible;

    public bool ShowsEmptyRules => emptyRules.IsVisible;

    public bool ShowsEmptyAccounts => emptyAccounts.IsVisible;

    public bool ShowsNewerVersion => newer.IsVisible;

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    private Control RulesPanel()
    {
        var add = PlanCard.Capsule(AddTitle, model.Submit, "Add the rule");
        AddButton = add.Button;
        var inputBox = new Border { Padding = new Thickness(10, 6), Child = ruleBox, MinHeight = Chrome.CapsuleContentHeight, VerticalAlignment = VerticalAlignment.Center };
        inputBox.Capsule(RoundedCornerStyle.Continuous);
        inputBox.Background = Chrome.Overlay(0.06).Brush(inputBox);
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(add.Shown, 1);
        form.Children.Add(inputBox);
        form.Children.Add(add.Shown);

        var hint = Text(Hint, SwiftFont.TextStyle.Footnote, secondary: true);
        var hold = SwitchRow(HoldTitle, HoldDetail, () => model.HoldReserves, value => model.SetHoldReserves(value), out var holdSwitch);
        var ask = SwitchRow(AskTitle, AskDetail, () => model.AlwaysAskBeforeStarting, value => model.SetAlwaysAskBeforeStarting(value), out var askSwitch);
        HoldSwitch = holdSwitch;
        AskSwitch = askSwitch;
        question.IsVisible = false;
        AutomationProperties.SetName(rules, RulesTitle);
        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Children = { Text(RulesTitle, 15, SwiftFont.Weight.Semibold, primary: true), newer, rules, emptyRules, form, question, problem, hint, Legend(), hold, ask },
        };
    }

    // ---- drawing the model --------------------------------------------------------------------------------------------

    private void Refresh()
    {
        refreshing = true;
        try
        {
            if (!string.Equals(ruleBox.Text ?? "", model.Draft, StringComparison.Ordinal)) ruleBox.Text = model.Draft;
        }
        finally { refreshing = false; }
        var readOnly = model.ReadOnly;
        newer.IsVisible = readOnly;
        ruleBox.IsEnabled = !readOnly;
        if (AddButton is not null) AddButton.IsEnabled = !readOnly;

        rules.Children.Clear();
        foreach (var rule in model.Rules) rules.Children.Add(RuleRow(rule, readOnly));
        emptyRules.IsVisible = model.Rules.Count == 0;

        question.Children.Clear();
        question.IsVisible = model.Question is not null;
        EveryButton = null;
        OnlyButton = null;
        if (model.Question is { } ask)
        {
            var text = Text(ask.Text, 13, SwiftFont.Weight.Medium, primary: true);
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            var every = PlanCard.Capsule("Every account", () => model.AnswerQuestion(everywhere: true), "Keep it on every account");
            var only = PlanCard.Capsule("Only " + ask.Only, () => model.AnswerQuestion(everywhere: false), "Keep it only on " + ask.Only);
            EveryButton = every.Button;
            OnlyButton = only.Button;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { every.Shown, only.Shown } };
            buttons.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                model.Cancel();
                // The question's buttons are gone and the focus they held with them: the owner is put back in the field once
                // this key has been dealt with.
                Dispatcher.UIThread.Post(() => ruleBox.Focus(), DispatcherPriority.Input);
                e.Handled = true;
            };
            question.Children.Add(text);
            question.Children.Add(buttons);
        }
        problem.Text = model.Problem ?? "";
        problem.IsVisible = model.Problem is not null;

        RefreshAccounts();
    }

    private void RefreshAccounts()
    {
        var bars = model.Bars;
        var keys = bars.SelectMany(b => b.Windows.Select(w => b.Key + "|" + w.Id)).ToList();
        if (!keys.SequenceEqual(rows.Keys))
        {
            rows.Clear();
            accountRows.Children.Clear();
            foreach (var key in keys)
            {
                var row = new ReserveBarRow();
                rows[key] = row;
                accountRows.Children.Add(row);
            }
        }
        foreach (var bar in bars)
        {
            var first = true;
            foreach (var window in bar.Windows)
            {
                string? extra = null;
                if (window.Eased && window.Unadjusted is { } before) extra = $"easing toward the reset, was {before.ToString("0.0", CultureInfo.InvariantCulture)}%";
                if (first && bar.OutsidePoints > 0) extra = (extra is null ? "" : extra + " · ") + $"{ReserveMath.Percent(bar.OutsidePoints)} used outside nth in the last 24 hours";
                first = false;
                rows[bar.Key + "|" + window.Id].Set(bar.Name, ReserveBarRow.Line(window.Used, null, window.Floor, window.Title, bar.Rough, extra),
                    window.Used, 0, window.Floor, window.Unadjusted);
            }
        }
        emptyAccounts.IsVisible = rows.Count == 0;
    }

    private Control RuleRow(ReserveRule rule, bool readOnly)
    {
        var title = ReserveWords.Title(rule);
        var icon = SwiftImage.SystemName("shield").Font(SwiftFont.System(13, SwiftFont.Weight.Medium)).ForegroundStyle(Chrome.SecondaryText);
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(title, 13, SwiftFont.Weight.Medium, primary: true));
        text.Children.Add(Text(ReserveWords.Detail(rule), SwiftFont.TextStyle.Footnote, secondary: true));
        var glyph = SwiftImage.SystemName("xmark").Font(SwiftFont.System(11, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.PrimaryText);
        var face = new Border { Width = Chrome.CapsuleContentHeight, Height = Chrome.CapsuleContentHeight, Child = glyph, Background = Brushes.Transparent };
        var remove = PlainControls.Button(face, () => model.Remove(rule.Id));
        AutomationProperties.SetName(remove, "Remove the rule: " + title);
        remove.IsEnabled = !readOnly;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        var shown = remove.ChromeGlassCircle();
        Grid.SetColumn(text, 1);
        Grid.SetColumn(shown, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(shown);
        var row = new Border { Child = grid, Padding = new Thickness(10, 8), Tag = remove };
        row.RoundedRectangle(12, RoundedCornerStyle.Continuous);
        row.Background = Chrome.Overlay(0.04).Brush(row);
        AutomationProperties.SetName(row, title);
        return row;
    }

    // ---- pieces -------------------------------------------------------------------------------------------------------

    private static Control Legend()
    {
        Control Item(Control swatch, string label)
        {
            var text = Text(label, SwiftFont.TextStyle.Caption, secondary: true);
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { swatch, text } };
        }
        Border Swatch(Func<Border, IBrush> brush, double width = 14, double height = 8)
        {
            var swatch = new Border { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center };
            swatch.RoundedRectangle(2, RoundedCornerStyle.Continuous);
            swatch.Background = brush(swatch);
            AutomationProperties.SetAccessibilityView(swatch, AccessibilityView.Raw);
            return swatch;
        }
        var used = Swatch(b => Chrome.Accent.Brush(b));
        var planned = Swatch(b => Chrome.Accent.Opacity(0.45).Brush(b));
        var reserve = Swatch(b => Chrome.Overlay(0.12).Brush(b));
        var floor = Swatch(b => Chrome.PrimaryText.Brush(b), width: 2, height: 12);
        return new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 4, Children = { Item(used, "Used"), Item(planned, "Planned"), Item(reserve, "Reserve"), Item(floor, "Floor") } };
    }

    private static Control SwitchRow(string title, string detail, Func<bool> get, Action<bool> set, out SettingsSwitch toggle)
    {
        toggle = new SettingsSwitch(new Binding<bool>(get, set));
        Control inner = toggle;
        while (inner is Decorator { Child: { } next }) inner = next;
        AutomationProperties.SetName(inner, title);
        AutomationProperties.SetHelpText(inner, detail);
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(title, 13, SwiftFont.Weight.Medium, primary: true));
        text.Children.Add(Text(detail, SwiftFont.TextStyle.Footnote, secondary: true));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(text);
        grid.Children.Add(toggle);
        return grid;
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

    private void LayOut(bool stackedLayout)
    {
        stacked = stackedLayout;
        body.ColumnDefinitions.Clear();
        body.RowDefinitions.Clear();
        if (stackedLayout)
        {
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowSpacing = 12;
            body.ColumnSpacing = 0;
            Grid.SetRow(rulesCard, 0);
            Grid.SetColumn(rulesCard, 0);
            Grid.SetRow(accountsCard, 1);
            Grid.SetColumn(accountsCard, 0);
        }
        else
        {
            body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            body.RowSpacing = 0;
            body.ColumnSpacing = 12;
            Grid.SetRow(rulesCard, 0);
            Grid.SetColumn(rulesCard, 0);
            Grid.SetRow(accountsCard, 0);
            Grid.SetColumn(accountsCard, 1);
        }
    }
}
