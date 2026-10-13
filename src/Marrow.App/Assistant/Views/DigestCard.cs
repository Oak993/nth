// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.2, 5.2, 6, 13.1; the approved canvas "Morning digest"; unit AS-11; ED-NEW-dextra-as11-11). No Swift twin.
// The morning digest as a card: a header (the sparkle, "Morning digest · 08:00", Read aloud once a voice is bound), the sections as a numbered list in the order of 6
// (each item a link to its source), the Suggestions lane with Accept and Dismiss, the seal of the last day's activity log, and the line it ends on. The card draws what
// the model says and nothing else: it sends no toast, raises no card, and has no heartbeat. A changed lane is redrawn alone, and the focus moves to the next open
// suggestion; an answered row fades into the line that took its place (no fade with Reduce Motion). Text is the primary and secondary text of the chrome (4.5:1);
// nothing is told by colour alone.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class DigestCard : Border
{
    public const double CardPadding = 14;
    public const double GutterWidth = 20;

    /// <summary>Under this width the lane's buttons sit under their line instead of beside it.</summary>
    public const double NarrowWidth = 380;

    public const string ReadAloudTitle = "Read aloud";
    public const string ReadAloudName = "Read the digest aloud";

    private readonly DigestModel model;
    private readonly TextBlock title = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border readAloudSlot = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel list = new() { Orientation = Orientation.Vertical, Spacing = 4 };
    private readonly StackPanel lane = new() { Orientation = Orientation.Vertical, Spacing = 6 };
    private readonly TextBlock seal = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock end = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<Button> links = [];
    private readonly List<LaneRowView> rows = [];
    private Digest? shown;
    private string? focusAfter;
    private bool focusQueued;
    private bool narrow;

    public DigestCard(DigestModel model)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        SwiftFont.System(13, SwiftFont.Weight.Semibold).Apply(title);
        title.Foreground = Chrome.PrimaryText.Brush(title);
        var icon = SwiftImage.SystemName("sparkles").Font(SwiftFont.System(13, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.PrimaryText);
        icon.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(readAloudSlot, 2);
        header.Children.Add(icon);
        header.Children.Add(title);
        header.Children.Add(readAloudSlot);
        AutomationProperties.SetHeadingLevel(title, 2);

        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(seal);
        seal.Foreground = Chrome.SecondaryText.Brush(seal);
        SwiftFont.System(12.5, SwiftFont.Weight.Regular).Apply(end);
        end.Foreground = Chrome.SecondaryText.Brush(end);
        end.Text = Digest.EndLine;

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { header, list, lane, seal, end } };
        Child = column;
        Padding = new Thickness(CardPadding);
        this.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        Background = Chrome.Overlay(0.04).Brush(this);
        Focusable = false;
        AttachedToVisualTree += (_, _) =>
        {
            model.Changed -= Refresh;
            model.Changed += Refresh;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => model.Changed -= Refresh;
        SizeChanged += (_, e) =>
        {
            var tight = e.NewSize.Width < NarrowWidth;
            if (tight == narrow) return;
            narrow = tight;
            foreach (var row in rows) row.Arrange(narrow);
        };
        Refresh();
    }

    // ---- what the tests and the screen reader read ----

    /// <summary>The header as shown: "Morning digest · 08:00".</summary>
    public string Header => title.Text ?? "";

    /// <summary>The sections as shown, in order, each as its plain text.</summary>
    public IReadOnlyList<string> SectionTexts => [.. list.Children.OfType<Grid>().Select(g => AutomationProperties.GetName(g) ?? "")];

    /// <summary>Every item that is a link, in reading order.</summary>
    public IReadOnlyList<Button> Links => links;

    public Button? ReadAloudButton { get; private set; }

    public string EndLine => end.Text ?? "";

    public string Seal => seal.Text ?? "";

    public string LaneTitle { get; private set; } = "";

    /// <summary>The lane's rows as shown.</summary>
    public IReadOnlyList<LaneRowView> LaneRows => rows;

    // ---- drawing ----

    /// <summary>Reads the model again and shows what it says now: the digest when it changed, the lane always.</summary>
    public void Refresh()
    {
        var digest = model.Digest;
        IsVisible = digest is not null;
        if (digest is null)
        {
            shown = null;
            return;
        }
        if (!ReferenceEquals(digest, shown))
        {
            shown = digest;
            Rebuild(digest);
        }
        RebuildLane(digest);
        RebuildReadAloud();
    }

    private void Rebuild(Digest digest)
    {
        title.Text = digest.Header;
        AutomationProperties.SetName(this, digest.Header);
        list.Children.Clear();
        links.Clear();
        var number = 1;
        foreach (var section in digest.Sections) list.Children.Add(Row(section, number++));
        seal.Text = digest.SealLine ?? "";
        seal.IsVisible = digest.SealLine is not null;
    }

    private Grid Row(DigestSection section, int number)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{GutterWidth},*") };
        var numeral = new TextBlock { Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", VerticalAlignment = VerticalAlignment.Top };
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(numeral);
        numeral.Foreground = Chrome.SecondaryText.Brush(numeral);
        AutomationProperties.SetAccessibilityView(numeral, AccessibilityView.Raw);
        Grid.SetColumn(numeral, 0);
        var body = Body(section);
        Grid.SetColumn(body, 1);
        grid.Children.Add(numeral);
        grid.Children.Add(body);
        AutomationProperties.SetName(grid, section.Text);
        return grid;
    }

    // A section of one thing, or one that runs its items on, is a single wrapping line: the label, then the items. Several sentences are listed under the label.
    private Control Body(DigestSection section)
    {
        var single = section.Inline || section.Items.Count <= 1;
        var label = Text(section.Label + (section.Lead.Length > 0 ? " " + section.Lead : ""), semibold: true, primary: true);
        if (single)
        {
            var line = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4 };
            line.Children.Add(label);
            for (var i = 0; i < section.Items.Count; i++)
            {
                var item = section.Items[i];
                var last = i == section.Items.Count - 1;
                line.Children.Add(Item(item, last || !section.Inline ? "" : ";"));
                if (item.Note.Length > 0) line.Children.Add(Text(item.Note + (last || !section.Inline ? "" : ";"), semibold: false, primary: false));
            }
            return line;
        }
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        stack.Children.Add(label);
        foreach (var item in section.Items) stack.Children.Add(Item(item, ""));
        return stack;
    }

    // A linked item is a plain button with underlined text, named for what it opens; with nothing to open it, plain text. The mark that runs it on (";") is part of
    // the same wrapping text but is not underlined, so it does not look like part of the link.
    private Control Item(DigestItem item, string trail)
    {
        if (item.Link.Kind == DigestLinkKind.None || model.OpenLink is null) return Text(item.Text + trail, semibold: false, primary: false);
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(13, SwiftFont.Weight.Regular).Apply(label);
        label.Foreground = Chrome.PrimaryText.Brush(label);
        var inlines = new InlineCollection { new Run(item.Text) { TextDecorations = TextDecorations.Underline } };
        if (trail.Length > 0) inlines.Add(new Run(trail));
        label.Inlines = inlines;
        var button = PlainControls.Button(label, () => model.Open(item.Link)).KeyboardFocus(4);
        AutomationProperties.SetName(button, "Open: " + item.Text);
        links.Add(button);
        return button;
    }

    private static TextBlock Text(string text, bool semibold, bool primary)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(13, semibold ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Regular).Apply(block);
        block.Foreground = (primary ? Chrome.PrimaryText : Chrome.SecondaryText).Brush(block);
        return block;
    }

    private void RebuildReadAloud()
    {
        if (!model.CanReadAloud)
        {
            ReadAloudButton = null;
            readAloudSlot.Child = null;
            return;
        }
        if (ReadAloudButton is not null) return;
        var (button, shownButton) = PlanCard.Capsule(ReadAloudTitle, model.Read, ReadAloudName);
        ReadAloudButton = button;
        readAloudSlot.Child = shownButton;
    }

    // ---- the lane ----

    private void RebuildLane(Digest digest)
    {
        _ = digest;
        var rowsNow = model.Lane;
        lane.Children.Clear();
        rows.Clear();
        LaneTitle = model.LaneTitle;
        if (rowsNow.Count == 0) return;
        lane.Children.Add(PlainControls.Divider());
        var heading = Text(LaneTitle, semibold: true, primary: true);
        SwiftFont.System(12.5, SwiftFont.Weight.Semibold).Apply(heading);
        lane.Children.Add(heading);
        foreach (var row in rowsNow)
        {
            var view = new LaneRowView(model, row, this);
            view.Arrange(narrow);
            rows.Add(view);
            lane.Children.Add(view);
        }
        if (focusAfter is { } key && !focusQueued)
        {
            // The row that was answered is gone from the buttons: the focus goes to the next suggestion that is still open, else to the first link. It is
            // found when the focus is given, not now: the lane may be drawn again before then, and the rows of this drawing would be gone.
            focusQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                focusQueued = false;
                focusAfter = null;
                var next = rows.SkipWhile(r => r.Key != key).Skip(1).FirstOrDefault(r => r.AcceptButton is not null) ?? rows.FirstOrDefault(r => r.AcceptButton is not null);
                (next?.AcceptButton ?? links.FirstOrDefault())?.Focus();
            }, DispatcherPriority.Input);
        }
    }

    internal void RememberFocus(string key) => focusAfter = key;

    /// <summary>
    /// Brings a suggestion's row into view and gives its Accept the focus (a chip on the home leads here: it never accepts for the owner, who reads the line and its cost
    /// first). False when that suggestion is no longer open in the lane.
    /// </summary>
    public bool Reveal(string key)
    {
        if (rows.FirstOrDefault(r => r.Key == key) is not { AcceptButton: { } accept } row) return false;
        row.BringIntoView();
        accept.Focus(NavigationMethod.Tab);
        return true;
    }

    internal static void FadeIn(Control control)
    {
        if (Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion) return;
        control.Opacity = 0;
        control.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new CubicEaseOut() }];
        Dispatcher.UIThread.Post(() => control.Opacity = 1);
    }
}

/// <summary>One suggestion of the lane: its line and cost with Accept and Dismiss, or the line that took its place.</summary>
public sealed class LaneRowView : Grid
{
    private readonly TextBlock line = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Control? buttons;
    private readonly TextBlock? status;

    internal LaneRowView(DigestModel model, SuggestionRow row, DigestCard card)
    {
        Key = row.Suggestion.Key;
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        ColumnSpacing = 8;
        RowDefinitions = new RowDefinitions("Auto,Auto");
        RowSpacing = 6;
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        if (row.Answered is { } said)
        {
            status = new LiveStatusText { Text = said, TextWrapping = TextWrapping.Wrap };
            SwiftFont.System(12.5, SwiftFont.Weight.Regular).Apply(status);
            status.Foreground = Chrome.SecondaryText.Brush(status);
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            text.Children.Add(status);
            DigestCard.FadeIn(status);
        }
        else
        {
            line.Text = row.Suggestion.Line;
            SwiftFont.System(12.5, SwiftFont.Weight.Regular).Apply(line);
            line.Foreground = Chrome.SecondaryText.Brush(line);
            text.Children.Add(line);
            if (row.Suggestion.Cost.Length > 0)
            {
                var cost = new TextBlock { Text = row.Suggestion.Cost, TextWrapping = TextWrapping.Wrap };
                SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(cost);
                cost.Foreground = Chrome.SecondaryText.Brush(cost);
                text.Children.Add(cost);
            }
            if (row.Problem is { } problem)
            {
                Problem = new LiveStatusText { Text = problem, TextWrapping = TextWrapping.Wrap };
                SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(Problem);
                Problem.Foreground = Chrome.Danger.Brush(Problem);
                AutomationProperties.SetLiveSetting(Problem, AutomationLiveSetting.Polite);
                text.Children.Add(Problem);
            }
            // Under a past day's digest the lane is the record of what it offered: nothing to tap.
            if (row.Suggestion.State == SuggestionState.Open && model.IsToday)
            {
                var accept = PlanCard.Capsule(SuggestionWords.AcceptTitle, () => { card.RememberFocus(Key); model.Accept(Key); }, "Accept: " + row.Suggestion.Line);
                var dismiss = PlanCard.Capsule(SuggestionWords.DismissTitle, () => { card.RememberFocus(Key); model.Dismiss(Key); }, "Dismiss: " + row.Suggestion.Line);
                AcceptButton = accept.Button;
                DismissButton = dismiss.Button;
                buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Children = { accept.Shown, dismiss.Shown } };
            }
        }
        AutomationProperties.SetName(this, row.Answered ?? row.Suggestion.Line);
        SetColumn(text, 0);
        SetRow(text, 0);
        Children.Add(text);
        if (buttons is not null) Children.Add(buttons);
    }

    public string Key { get; }

    public Button? AcceptButton { get; private set; }

    public Button? DismissButton { get; private set; }

    /// <summary>The panel Accept and Dismiss sit in, or null when the row has none (answered, or a past day's).</summary>
    public Control? Buttons => buttons;

    public TextBlock? Problem { get; private set; }

    /// <summary>The line (or the line that replaced it) as shown.</summary>
    public string Text => status?.Text ?? line.Text ?? "";

    /// <summary>Beside the line on a wide card; under it on a narrow one.</summary>
    internal void Arrange(bool narrow)
    {
        if (buttons is null) return;
        SetRow(buttons, narrow ? 1 : 0);
        SetColumn(buttons, narrow ? 0 : 1);
        SetColumnSpan(buttons, narrow ? 2 : 1);
        buttons.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        buttons.VerticalAlignment = VerticalAlignment.Top;
    }
}
