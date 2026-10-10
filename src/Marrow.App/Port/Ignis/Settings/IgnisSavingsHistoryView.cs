// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-settings; PLAN unit U5). Settings › Ignis "Savings history", drawn as the locked
// design draws it (phoenix-designs.html .sv-tiles, .sv-card, .mpick, .mchart, .sbar, .sv-legend, .days) in the Settings
// pages' own card language (ChromeCard's 16 pt continuous fill at 0.03, the secondary text, the semibold headings): two
// tiles (all-time, the picked month), "Each month" with the month picker over a bar per month (pick a bar or step the
// arrows), "Where {month}'s savings came from" (a four-part bar and its legend), and "Day by day", newest first. Picking
// a month changes the month tile, the sources and the days; the bars ease their colour, the new days fade in (reduce
// motion: at once). No keyboard shortcuts (owner); the arrows and bars are plain buttons with accessible names.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Ignis;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Ignis.Settings;

public sealed class IgnisSavingsHistoryView : Decorator
{
    private const double ChartHeight = 118, BarMaxHeight = 72, BarMinHeight = 3;

    private readonly IgnisSavingsHistory history;
    private readonly Grid chart = new() { Height = ChartHeight, ColumnSpacing = 6 };
    private readonly Grid sourceBar = new() { Height = 6, ColumnSpacing = 2 };
    private readonly Border sourceTrack = new() { Height = 6, ClipToBounds = true };
    private readonly Grid legend = new() { ColumnDefinitions = new ColumnDefinitions("*,18,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto") };
    private readonly StackPanel days = new();
    private readonly List<(Button Button, Border Bar, TextBlock Value, TextBlock Name, AnimatedColor Fill, int Index)> bars = [];
    private int windowStart = -1;

    public IgnisSavingsHistoryView(IgnisSavingsHistory history)
    {
        this.history = history;
        Selected = history.Months.Count - 1;

        // .sv-tiles: two tiles side by side
        AllTimeAmount = Text(history.AllTimeAmount, SwiftFont.System(24, SwiftFont.Weight.Semibold), Chrome.Success);
        SinceText = Text(history.SinceText, SwiftFont.System(11.5), Chrome.SecondaryText.Opacity(0.8), wrap: true);
        MonthTileLabel = Text("", SwiftFont.System(12), Chrome.SecondaryText);
        MonthTileAmount = Text("", SwiftFont.System(24, SwiftFont.Weight.Semibold), Chrome.PrimaryText);
        MonthTileDetail = Text("", SwiftFont.System(11.5), Chrome.SecondaryText.Opacity(0.8), wrap: true);
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        var allTime = Tile(Text(IgnisWords.SavedSinceOn, SwiftFont.System(12), Chrome.SecondaryText), AllTimeAmount, SinceText);
        var month = Tile(MonthTileLabel, MonthTileAmount, MonthTileDetail);
        Grid.SetColumn(month, 2);
        tiles.Children.Add(allTime);
        tiles.Children.Add(month);

        // .sv-card "Each month" with .mpick
        MonthName = Text("", SwiftFont.System(12.5, SwiftFont.Weight.Medium), Chrome.PrimaryText);
        MonthName.MinWidth = 112;
        MonthName.TextAlignment = TextAlignment.Center;
        MonthName.VerticalAlignment = VerticalAlignment.Center;
        PreviousButton = Arrow("chevron.left", IgnisWords.PreviousMonth, () => Select(Selected - 1));
        NextButton = Arrow("chevron.right", IgnisWords.NextMonth, () => Select(Selected + 1));
        var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { PreviousButton, MonthName, NextButton } };
        AutomationProperties.SetName(chart, IgnisWords.MonthChartAccessibleName);
        AutomationProperties.SetLiveSetting(MonthName, AutomationLiveSetting.Polite);
        var monthCard = Card(Head(Text(IgnisWords.EachMonth, SwiftFont.System(13, SwiftFont.Weight.Semibold), Chrome.PrimaryText), picker), chart);

        // .sv-card "Where {month}'s savings came from"
        SourceTitle = Text("", SwiftFont.System(13, SwiftFont.Weight.Semibold), Chrome.PrimaryText, wrap: true);
        sourceTrack.Child = sourceBar;
        sourceTrack.RoundedRectangle(3, RoundedCornerStyle.Continuous);
        var sourceCard = Card(SourceTitle, sourceTrack, legend);

        // .sv-card "Day by day"
        var dayScroll = new ScrollViewer { Content = days, MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DayScroll = dayScroll;
        var daysCard = Card(Head(Text(IgnisWords.DayByDay, SwiftFont.System(13, SwiftFont.Weight.Semibold), Chrome.PrimaryText),
            Text(IgnisWords.NewestFirst, SwiftFont.System(11.5), Chrome.SecondaryText.Opacity(0.8))), dayScroll);

        Child = new StackPanel { Spacing = 10, Children = { tiles, monthCard, sourceCard, daysCard } };
        Show(Selected, animate: false);
    }

    /// <summary>The picked month's index in <see cref="IgnisSavingsHistory.Months"/> (the newest at first).</summary>
    public int Selected { get; private set; }

    public IgnisMonthSavings SelectedMonth => history.Months[Selected];

    public TextBlock AllTimeAmount { get; }
    public TextBlock SinceText { get; }
    public TextBlock MonthTileLabel { get; }
    public TextBlock MonthTileAmount { get; }
    public TextBlock MonthTileDetail { get; }
    public TextBlock MonthName { get; }
    public Button PreviousButton { get; }
    public Button NextButton { get; }
    public TextBlock SourceTitle { get; }
    public ScrollViewer DayScroll { get; }

    /// <summary>The chart's bars as they stand: the month, its accessible name and whether it is the picked one.</summary>
    public IReadOnlyList<(DateOnly Month, string Name, bool Picked)> Bars =>
        bars.Select(bar => (history.Months[bar.Index].Month, AutomationProperties.GetName(bar.Button) ?? "", bar.Index == Selected)).ToList();

    /// <summary>The legend as it reads: name and amount, in legend order.</summary>
    public IReadOnlyList<(string Name, string Amount)> Legend { get; private set; } = [];

    /// <summary>"Day by day" as it reads: label, what Ignis did (or "A quiet day") and the amount.</summary>
    public IReadOnlyList<(string Label, IReadOnlyList<string> Did, string Amount)> DayLines { get; private set; } = [];

    /// <summary>Picks a month (a bar, or the arrows); out of range does nothing.</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= history.Months.Count || index == Selected) return;
        Show(index, animate: true);
    }

    /// <summary>Picks a bar by its month, as a click on it does.</summary>
    public void PickBar(DateOnly month)
    {
        var index = history.Months.ToList().FindIndex(entry => entry.Month == month);
        if (index >= 0) Select(index);
    }

    private void Show(int index, bool animate)
    {
        Selected = index;
        var month = history.Months[index];
        MonthTileLabel.Text = month.TileLabel;
        MonthTileAmount.Text = IgnisWords.Money(month.Total);
        MonthTileDetail.Text = month.TileDetail;
        MonthName.Text = IgnisWords.MonthYear(month.Month);
        SourceTitle.Text = IgnisWords.WhereMonthsSavingsCameFrom(month.Month);
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index < history.Months.Count - 1;
        BuildChart(index, animate);
        BuildSources(month);
        BuildDays(month, animate);
    }

    private void BuildChart(int selected, bool animate)
    {
        var (start, count) = history.ChartWindow(selected);
        if (start != windowStart)
        {
            windowStart = start;
            bars.Clear();
            chart.Children.Clear();
            chart.ColumnDefinitions.Clear();
            var max = Math.Max(0.01m, history.Months.Skip(start).Take(count).Max(entry => entry.Total));
            for (var column = 0; column < count; column++)
            {
                var index = start + column;
                var entry = history.Months[index];
                chart.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                var value = Text(IgnisWords.MonthBarValue(entry.Total), SwiftFont.System(10), Chrome.SecondaryText);
                value.HorizontalAlignment = HorizontalAlignment.Center;
                var bar = new Border { MaxWidth = 30, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(5, 5, 3, 3),
                    Height = Math.Max(BarMinHeight, (double)(entry.Total / max) * BarMaxHeight) };
                var fill = new AnimatedColor(bar);
                bar.Background = fill.Brush;
                var name = Text(IgnisWords.MonthShort(entry.Month), SwiftFont.System(10.5), Chrome.SecondaryText.Opacity(0.8));
                name.HorizontalAlignment = HorizontalAlignment.Center;
                var stack = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Children = { value, bar, name } };
                var face = new Border { Background = Brushes.Transparent, Child = stack, CornerRadius = new CornerRadius(8) };
                var button = PlainControls.Button(face, () => Select(index));
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.VerticalAlignment = VerticalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.VerticalContentAlignment = VerticalAlignment.Bottom;
                AutomationProperties.SetName(button, IgnisWords.MonthName(entry.Month) + " " + IgnisWords.Money(entry.Total));
                var hovering = false;
                button.OnHover(on => { hovering = on; Paint(index, hovering, animate: true); });
                Grid.SetColumn(button, column);
                chart.Children.Add(button);
                bars.Add((button, bar, value, name, fill, index));
            }
        }
        foreach (var bar in bars) Paint(bar.Index, bar.Button.IsPointerOver, animate);
    }

    // .mchart: a bar is the green at 30 %, 55 % under the pointer, full when picked; its value shows when picked or hovered.
    private void Paint(int index, bool hovering, bool animate)
    {
        var bar = bars.FirstOrDefault(entry => entry.Index == index);
        if (bar.Button is null) return;
        var picked = index == Selected;
        bar.Fill.Set(picked ? Chrome.Success : Chrome.Success.Opacity(hovering ? 0.55 : 0.30), animate ? Animation.EaseOut(0.2) : null);
        bar.Value.Opacity = picked || hovering ? 1 : 0;
        bar.Name.FontWeight = picked ? FontWeight.SemiBold : FontWeight.Normal;
        bar.Name.Foreground = (picked ? Chrome.PrimaryText : Chrome.SecondaryText.Opacity(0.8)).Brush(bar.Name);
    }

    private void BuildSources(IgnisMonthSavings month)
    {
        sourceBar.Children.Clear();
        sourceBar.ColumnDefinitions.Clear();
        legend.Children.Clear();
        var total = month.BySource.Values.Sum();
        var lines = new List<(string, string)>();
        for (var i = 0; i < IgnisSavingsHistory.Sources.Count; i++)
        {
            var source = IgnisSavingsHistory.Sources[i];
            var amount = month.BySource[source];
            if (total > 0)
            {
                sourceBar.ColumnDefinitions.Add(new ColumnDefinition((double)amount, GridUnitType.Star));
                var part = new Border { Background = SourceColor(source).Brush(sourceBar) };
                Grid.SetColumn(part, sourceBar.ColumnDefinitions.Count - 1);
                sourceBar.Children.Add(part);
            }
            var name = Text(IgnisWords.SourceName(source), SwiftFont.System(12.5), Chrome.SecondaryText);
            var value = Text(IgnisWords.Money(amount), SwiftFont.System(12.5, SwiftFont.Weight.Semibold), Chrome.PrimaryText);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,7,*,8,Auto"), Children = { Dot(source, 7), name, value } };
            Grid.SetColumn(name, 2);
            Grid.SetColumn(value, 4);
            Grid.SetColumn(row, i % 2 == 0 ? 0 : 2);
            Grid.SetRow(row, i < 2 ? 0 : 2);
            legend.Children.Add(row);
            lines.Add((IgnisWords.SourceName(source), IgnisWords.Money(amount)));
        }
        sourceTrack.Background = total <= 0 ? Chrome.Overlay(0.08).Brush(sourceTrack) : null;
        Legend = lines;
    }

    private void BuildDays(IgnisMonthSavings month, bool animate)
    {
        days.Children.Clear();
        var lines = new List<(string, IReadOnlyList<string>, string)>();
        var rows = month.DayRows(history.Today);
        for (var i = 0; i < rows.Count; i++)
        {
            var day = rows[i];
            var date = Text(day.Label, SwiftFont.System(12.5), Chrome.SecondaryText);
            var did = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 3 };
            if (day.Did.Count == 0) did.Children.Add(Text(IgnisWords.AQuietDay, SwiftFont.System(11.5), Chrome.SecondaryText.Opacity(0.7)));
            foreach (var item in day.Did)
                did.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { Dot(item.Source, 6), Text(item.Text, SwiftFont.System(11.5), Chrome.SecondaryText) } });
            var amount = Text(day.Amount, SwiftFont.System(12.5, SwiftFont.Weight.Semibold), Chrome.Success);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("76,12,*,12,Auto"), Margin = new Thickness(0, 9, 8, 9), Children = { date, did, amount } };
            Grid.SetColumn(did, 2);
            Grid.SetColumn(amount, 4);
            amount.VerticalAlignment = VerticalAlignment.Top;
            date.VerticalAlignment = VerticalAlignment.Top;
            if (i > 0) days.Children.Add(new ChromeRowDivider { Margin = default });
            days.Children.Add(grid);
            lines.Add((day.Label, day.Did.Count == 0 ? [IgnisWords.AQuietDay] : day.Did.Select(item => item.Text).ToList(), day.Amount));
        }
        DayLines = lines;
        DayScroll.Offset = default;
        // .sv-month-in: the new days fade in over 0.3 s (reduce motion: at once).
        if (animate && !Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion)
        {
            var fade = new Marrow.App.Views.Tour.AnimatedOpacity(days, 0);
            fade.Set(1, Animation.EaseOut(0.3));
        }
    }

    internal static SwiftColor SourceColor(IgnisSource source) => source switch
    {
        IgnisSource.Reuse => Chrome.Accent,
        IgnisSource.KeptWarm => SwiftColor.Dynamic(new SwiftColor(red: 1.0, green: 0.416, blue: 0.122), new SwiftColor(red: 1.0, green: 0.416, blue: 0.122)), // --ember-outer
        IgnisSource.TidyUp => Chrome.Success,
        IgnisSource.SmartHelpers => SwiftColor.Dynamic(new SwiftColor(red: 0.482, green: 0.333, blue: 0.878), new SwiftColor(red: 0.663, green: 0.545, blue: 1.0)), // --violet
    };

    private static Control Dot(IgnisSource source, double size)
    {
        var dot = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Center };
        dot.Background = SourceColor(source).Brush(dot);
        AutomationProperties.SetAccessibilityView(dot, AccessibilityView.Raw);
        return dot;
    }

    private static Control Tile(Control label, Control amount, Control detail)
    {
        var tile = new Border { Padding = new Thickness(14, 12), Child = new StackPanel { Spacing = 2, Children = { label, amount, detail } } }
            .RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        tile.Background = Chrome.Overlay(0.03).Brush(tile);
        return tile;
    }

    private static Control Card(params Control[] parts)
    {
        var stack = new StackPanel { Spacing = 12 };
        foreach (var part in parts) stack.Children.Add(part);
        var card = new Border { Padding = new Thickness(14, 12, 14, 14), Child = stack }.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        card.Background = Chrome.Overlay(0.03).Brush(card);
        return card;
    }

    private static Control Head(Control title, Control trailing)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,Auto"), Children = { title, trailing } };
        title.VerticalAlignment = VerticalAlignment.Center;
        trailing.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(trailing, 2);
        return grid;
    }

    private static Button Arrow(string symbol, string name, Action action)
    {
        var glyph = SwiftImage.SystemName(symbol).Font(SwiftFont.System(10, SwiftFont.Weight.Bold)).ForegroundStyle(Chrome.SecondaryText);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Child = glyph };
        var wash = new AnimatedColor(face);
        face.Background = wash.Brush;
        wash.Set(SwiftColor.Clear, null);
        var button = PlainControls.Button(face, action);
        button.OnHover(on => wash.Set(on && button.IsEnabled ? Chrome.Overlay(0.08) : SwiftColor.Clear, Chrome.Hover));
        button.PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty) face.Opacity = button.IsEnabled ? 1 : 0.3; };
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    private static TextBlock Text(string value, SwiftFont font, SwiftColor color, bool wrap = false)
    {
        var text = new TextBlock { Text = value, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        font.Apply(text);
        text.Foreground = color.Brush(text);
        return text;
    }
}
