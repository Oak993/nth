// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-6: the list of lines. Only the lines on screen (and a few above and below) exist as controls; the
// scroll area is as tall as the whole list, so a log of 250,000 lines scrolls as smoothly as one of 20.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>
/// The Activity view's list: the log (newest first under a header for each day) or the lineage (mission, agent, action with
/// its grant). Every line is <see cref="RowHeight"/> high, so where a line is, and which lines are on screen, is arithmetic.
/// A line is picked with the pointer or the keyboard (Up, Down, Home, End, Page Up, Page Down; Left and Right or Enter fold a
/// header of the lineage); a picked action fills the detail panel; a grant on an action filters the log to that grant.
/// </summary>
internal sealed class ActivityRowList : Decorator
{
    public const double RowHeight = 34;
    public const int Buffer = 4;

    /// <summary>Below this width the class and the grant columns are left out.</summary>
    public const double CompactWidth = 620;

    private readonly ActivityLogModel model;
    private readonly TimeZoneInfo zone;
    private readonly ScrollViewer scroll;
    private readonly RowSurface surface;
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360, IsHitTestVisible = false };
    private readonly Dictionary<int, ActivityRowCell> live = [];
    private readonly Stack<ActivityRowCell> spare = new();
    private IReadOnlyList<ActivityListItem> items = [];
    private int cursor = -1;
    private bool compact;

    public ActivityRowList(ActivityLogModel model, TimeZoneInfo? zone = null)
    {
        this.model = model;
        this.zone = zone ?? TimeZoneInfo.Local;
        surface = new RowSurface(this);
        scroll = new ScrollViewer
        {
            Content = surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        SoftScroll.SetIsEnabled(scroll, true);
        scroll.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => UpdateWindow());
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(message);
        message.Foreground = Chrome.SecondaryText.Brush(message);
        Child = new Panel { Children = { scroll, message } };
        Focusable = true;
        IsTabStop = true;
        AutomationProperties.SetName(this, "Activity log lines");
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.List);
        SizeChanged += (_, e) => SetCompact(e.NewSize.Width < CompactWidth);
        Refresh();
    }

    /// <summary>The lines shown now.</summary>
    public IReadOnlyList<ActivityListItem> Items => items;

    /// <summary>The cells that exist now, by the line they show (a test reads what is on screen through these).</summary>
    internal IReadOnlyDictionary<int, ActivityRowCell> Cells => live;

    internal ScrollViewer Scroll => scroll;

    /// <summary>The line the keyboard is on, or -1.</summary>
    public int KeyboardRow => cursor;

    public bool IsCompact => compact;

    /// <summary>The words shown over the list when it has no line to show, or an empty text.</summary>
    public string Message => message.IsVisible ? message.Text ?? "" : "";

    /// <summary>Reads the model again (its lines, its selection) and draws the lines on screen.</summary>
    public void Refresh()
    {
        items = model.Items;
        if (cursor >= items.Count) cursor = items.Count - 1;
        var phase = model.Phase;
        var text = phase is ActivityLoadPhase.Ready && items.Count == 0
            ? model.Snapshot!.Rows.Count == 0 ? ActivityWords.EmptyLog : ActivityWords.NothingMatches
            : "";
        message.Text = text;
        message.IsVisible = text.Length > 0;
        surface.InvalidateMeasure();
        foreach (var (_, cell) in live) Recycle(cell);
        live.Clear();
        UpdateWindow();
    }

    /// <summary>Redraws the cells for a new selection, without moving the list.</summary>
    public void RefreshSelection()
    {
        foreach (var (index, cell) in live) cell.Bind(items[index], index);
    }

    /// <summary>Scrolls so the line is in view, about a third from the top, easing there unless the owner reduces motion.</summary>
    public void ScrollTo(int index)
    {
        if (index < 0 || index >= items.Count) return;
        var top = index * RowHeight;
        var viewport = scroll.Viewport.Height > 0 ? scroll.Viewport.Height : 480;
        var target = Math.Max(0, Math.Min(top - viewport / 3, items.Count * RowHeight - viewport));
        if (top >= scroll.Offset.Y && top + RowHeight <= scroll.Offset.Y + viewport) return; // already in view
        var animation = Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion ? null : Animation.Smooth(0.35);
        SoftScroll.ScrollTo(scroll, new Vector(0, target), animation);
        UpdateWindow();
    }

    internal bool IsSelected(ActivityListItem item) => item.Row is not null && ReferenceEquals(item.Row, model.Selected);

    internal bool HasCursor(int index) => index == cursor && IsKeyboardFocusWithin;

    internal ActivityLabels Labels => model.Labels;

    internal TimeZoneInfo Zone => zone;

    internal bool Compact => compact;

    internal DateOnly Today => model.Today;

    // ---- the window -------------------------------------------------------------------------------------------------

    private void UpdateWindow()
    {
        if (items.Count == 0)
        {
            foreach (var (_, cell) in live) Recycle(cell);
            live.Clear();
            return;
        }
        var viewport = scroll.Viewport.Height > 0 ? scroll.Viewport.Height : 480;
        var first = Math.Max(0, (int)Math.Floor(scroll.Offset.Y / RowHeight) - Buffer);
        var last = Math.Min(items.Count - 1, (int)Math.Ceiling((scroll.Offset.Y + viewport) / RowHeight) + Buffer);
        foreach (var index in live.Keys.Where(i => i < first || i > last).ToList())
        {
            Recycle(live[index]);
            live.Remove(index);
        }
        for (var index = first; index <= last; index++)
        {
            if (live.TryGetValue(index, out var existing))
            {
                existing.Bind(items[index], index);
                continue;
            }
            var cell = spare.Count > 0 ? spare.Pop() : NewCell();
            cell.IsVisible = true;
            live[index] = cell;
            cell.Bind(items[index], index);
        }
        surface.InvalidateArrange();
    }

    private ActivityRowCell NewCell()
    {
        var cell = new ActivityRowCell(this);
        surface.Children.Add(cell);
        return cell;
    }

    private void Recycle(ActivityRowCell cell)
    {
        cell.IsVisible = false;
        cell.Release();
        spare.Push(cell);
    }

    private void SetCompact(bool value)
    {
        if (compact == value) return;
        compact = value;
        foreach (var (index, cell) in live) cell.Bind(items[index], index);
    }

    // ---- picking ----------------------------------------------------------------------------------------------------

    /// <summary>A press on a line, or Enter on it: an action is selected; a header folds.</summary>
    internal void Pick(int index)
    {
        if (index < 0 || index >= items.Count) return;
        cursor = index;
        var item = items[index];
        if (item.Row is { } row) model.Select(row);
        else if (item.Kind != ActivityListItemKind.DayHeader) _ = model.ToggleFoldAsync(item.Key);
        RefreshSelection();
    }

    internal void FilterToGrant(string grant) => _ = model.UpdateFilterAsync(f => f with { Grant = grant });

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (items.Count == 0) return;
        var page = Math.Max(1, (int)(scroll.Viewport.Height / RowHeight) - 1);
        var from = cursor < 0 ? (model.Selected is { } selected ? model.IndexOf(selected) : -1) : cursor;
        int? to = e.Key switch
        {
            Key.Down => Math.Min(items.Count - 1, from + 1),
            Key.Up => Math.Max(0, from < 0 ? 0 : from - 1),
            Key.Home => 0,
            Key.End => items.Count - 1,
            Key.PageDown => Math.Min(items.Count - 1, Math.Max(0, from) + page),
            Key.PageUp => Math.Max(0, Math.Max(0, from) - page),
            _ => null,
        };
        if (to is { } target)
        {
            // Down, Up and the page keys pass over the day headers; the lineage's headers are stops of their own, to fold.
            target = Stop(target, e.Key is Key.Down or Key.PageDown or Key.Home ? 1 : -1);
            cursor = target;
            if (items[target].Row is { } row) model.Select(row);
            ScrollTo(target);
            RefreshSelection();
            e.Handled = true;
            return;
        }
        if (cursor < 0 || cursor >= items.Count) return;
        var item = items[cursor];
        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                Pick(cursor);
                e.Handled = true;
                break;
            case Key.Left when item.Kind is ActivityListItemKind.MissionHeader or ActivityListItemKind.AgentHeader && item.Expanded:
            case Key.Right when item.Kind is ActivityListItemKind.MissionHeader or ActivityListItemKind.AgentHeader && !item.Expanded:
                _ = model.ToggleFoldAsync(item.Key);
                e.Handled = true;
                break;
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (cursor < 0 && items.Count > 0) cursor = model.Selected is { } row && model.IndexOf(row) is >= 0 and var at ? at : FirstStop();
        RefreshSelection();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        RefreshSelection();
    }

    /// <summary>The nearest line from <paramref name="target"/> that is not a day header, looking first in <paramref name="direction"/>, then the other way.</summary>
    private int Stop(int target, int direction)
    {
        for (var i = target; i >= 0 && i < items.Count; i += direction)
            if (items[i].Kind != ActivityListItemKind.DayHeader) return i;
        for (var i = target; i >= 0 && i < items.Count; i -= direction)
            if (items[i].Kind != ActivityListItemKind.DayHeader) return i;
        return target;
    }

    private int FirstStop()
    {
        for (var i = 0; i < items.Count; i++)
            if (items[i].Kind != ActivityListItemKind.DayHeader) return i;
        return 0;
    }

    // ---- the surface ------------------------------------------------------------------------------------------------

    /// <summary>As tall as the whole list; arranges only the cells that exist, each at its line's place.</summary>
    private sealed class RowSurface(ActivityRowList owner) : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
            foreach (var child in Children)
                if (child.IsVisible) child.Measure(new Size(width, RowHeight));
            return new Size(width, owner.items.Count * RowHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var (index, cell) in owner.live)
                cell.Arrange(new Rect(0, index * RowHeight, finalSize.Width, RowHeight));
            return finalSize;
        }
    }
}

/// <summary>One line of the list. It is made once and shows whichever line the list gives it (cells are reused as the list scrolls).</summary>
internal sealed class ActivityRowCell : Border
{
    private readonly ActivityRowList list;
    private readonly AnimatedColor fill;
    private readonly Grid actionPart = new();
    private readonly Grid headerPart = new();
    private readonly TextBlock time = new();
    private readonly TextBlock assistant = new();
    private readonly TextBlock what = new();
    private readonly Run tool = new();
    private readonly Run target = new();
    private readonly TextBlock actionClass = new();
    private readonly Border classChip = new();
    private readonly TextBlock grant = new();
    private readonly Button grantButton;
    private readonly Border trust = new() { Width = 16, Height = 16 };
    private readonly SwiftImage chevron;
    private readonly TextBlock headline = new();
    private readonly Run title = new();
    private readonly Run subtitle = new();
    private readonly TextBlock stats = new();
    private readonly IBrush danger;
    private string titleValue = "";
    private string subtitleValue = "";
    private readonly Border indent = new();
    private int index = -1;
    private ActivityListItem? item;
    private string? grantId;
    private string? trustSymbol;

    public ActivityRowCell(ActivityRowList list)
    {
        this.list = list;
        Height = ActivityRowList.RowHeight;
        fill = new AnimatedColor(this);
        Background = fill.Brush;
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(6, 0);
        Padding = new Thickness(8, 0);
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.ListItem);

        var mono = SwiftFont.MonospacedSystemFont(11.5, SwiftFont.Weight.Regular);
        mono.Apply(time);
        time.Foreground = Chrome.SecondaryText.Brush(time);
        SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(assistant);
        assistant.Foreground = Chrome.PrimaryText.Brush(assistant);
        SwiftFont.System(12.5).Apply(what);
        what.Foreground = Chrome.PrimaryText.Brush(what);
        what.Inlines!.Add(tool);
        what.Inlines.Add(target);
        target.Foreground = Chrome.SecondaryText.Brush(what);
        SwiftFont.System(11, SwiftFont.Weight.Medium).Apply(actionClass);
        actionClass.Foreground = Chrome.SecondaryText.Brush(actionClass);
        classChip.Child = actionClass;
        classChip.Padding = new Thickness(7, 2);
        classChip.Background = Chrome.Overlay(0.07).Brush(classChip);
        classChip.RoundedRectangle(6, RoundedCornerStyle.Continuous);
        classChip.VerticalAlignment = VerticalAlignment.Center;
        classChip.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var text in new[] { time, assistant, what, actionClass, grant, headline, stats })
        {
            text.MaxLines = 1;
            text.TextWrapping = TextWrapping.NoWrap;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.VerticalAlignment = VerticalAlignment.Center;
        }
        SwiftFont.MonospacedSystemFont(11, SwiftFont.Weight.Regular).Apply(grant);
        grant.Foreground = Chrome.SecondaryText.Brush(grant);
        grantButton = PlainControls.Button(grant, () => { if (grantId is { } id) list.FilterToGrant(id); });
        grantButton.VerticalAlignment = VerticalAlignment.Center;
        grantButton.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAccessibilityView(trust, AccessibilityView.Raw);

        actionPart.ColumnDefinitions = new ColumnDefinitions("Auto,64,112,*,Auto,128,16");
        Place(actionPart, indent, 0);
        Place(actionPart, time, 1);
        Place(actionPart, assistant, 2);
        Place(actionPart, what, 3);
        Place(actionPart, classChip, 4);
        Place(actionPart, grantButton, 5);
        Place(actionPart, trust, 6);
        what.Margin = new Thickness(0, 0, 10, 0);
        classChip.Margin = new Thickness(0, 0, 10, 0);

        chevron = SwiftImage.SystemName("chevron.right").Font(SwiftFont.System(10, SwiftFont.Weight.Bold)).ForegroundStyle(Chrome.SecondaryText);
        chevron.Width = 14;
        chevron.Height = 14;
        danger = Chrome.Danger.Brush(what);
        chevron.RenderTransformOrigin = RelativePoint.Center;
        AutomationProperties.SetAccessibilityView(chevron, AccessibilityView.Raw);
        SwiftFont.System(12.5).Apply(headline);
        headline.Foreground = Chrome.SecondaryText.Brush(headline);
        title.FontWeight = FontWeight.SemiBold;
        title.Foreground = Chrome.PrimaryText.Brush(headline);
        headline.Inlines!.Add(title);
        headline.Inlines.Add(subtitle);
        SwiftFont.System(11.5).Apply(stats);
        stats.Foreground = Chrome.SecondaryText.Brush(stats);
        stats.HorizontalAlignment = HorizontalAlignment.Right;
        stats.Margin = new Thickness(12, 0, 0, 0);
        headerPart.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
        Place(headerPart, chevron, 0);
        Place(headerPart, headline, 1);
        Place(headerPart, stats, 2);
        headerPart.IsVisible = false;

        Child = new Panel { Children = { actionPart, headerPart } };
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && index >= 0)
            {
                list.Pick(index);
                list.Focus();
            }
        };
        PointerEntered += (_, _) => { hover = true; Paint(Chrome.Hover); };
        PointerExited += (_, _) => { hover = false; Paint(Chrome.Hover); };
    }

    private bool hover;

    /// <summary>The line this cell shows now, or -1.</summary>
    public int Index => index;

    public ActivityListItem? Item => item;

    public string TimeText => time.Text ?? "";

    public string AssistantText => assistant.Text ?? "";

    public string WhatText => (tool.Text ?? "") + (target.Text ?? "");

    public string ClassText => actionClass.Text ?? "";

    public string GrantText => grant.Text ?? "";

    public string TitleText => titleValue;

    public string SubtitleText => subtitleValue;

    public string StatsText => stats.Text ?? "";

    public Button GrantButton => grantButton;

    public bool ShowsTrustMark => trustSymbol is not null;

    public string? TrustSymbol => trustSymbol;

    public bool IsHeader => headerPart.IsVisible;

    public bool ClassVisible => classChip.IsVisible;

    public double ChevronAngle => (chevron.RenderTransform as RotateTransform)?.Angle ?? 0;

    private static void Place(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    public void Release()
    {
        index = -1;
        item = null;
    }

    public void Bind(ActivityListItem value, int at)
    {
        item = value;
        index = at;
        var row = value.Row;
        var header = row is null;
        headerPart.IsVisible = header;
        actionPart.IsVisible = !header;
        Margin = new Thickness(6 + (header ? value.Depth * 14 : 0), 0, 6, 0);
        if (row is null) BindHeader(value);
        else BindAction(row, value);
        Paint(null);
    }

    private void BindHeader(ActivityListItem value)
    {
        var labels = list.Labels;
        chevron.IsVisible = value.Kind != ActivityListItemKind.DayHeader;
        chevron.RenderTransform = new RotateTransform(value.Expanded ? 90 : 0);
        switch (value.Kind)
        {
            case ActivityListItemKind.DayHeader:
                Headline(ActivityWords.DayTitle(value.Day, list.Today), ActivityWords.Lines(value.Count));
                stats.Text = "";
                AutomationProperties.SetName(this, titleValue + ", " + subtitleValue);
                break;
            case ActivityListItemKind.MissionHeader:
                Headline(value.Id.Length == 0 ? ActivityWords.NoMission : labels.ForMission(value.Id), value.Id.Length == 0 ? "" : labels.ForAssistant(value.Assistant));
                stats.Text = MissionStats(value);
                AutomationProperties.SetName(this, titleValue + (subtitleValue.Length > 0 ? ", " + subtitleValue : "") + ", " + stats.Text + (value.Expanded ? ", expanded" : ", collapsed"));
                break;
            default:
                Headline(value.Id.Length == 0 ? ActivityWords.NoAgent : labels.ForAgent(value.Id), "");
                stats.Text = ActivityWords.Count(value.Count, "action", "actions");
                AutomationProperties.SetName(this, "Agent " + titleValue + ", " + stats.Text + (value.Expanded ? ", expanded" : ", collapsed"));
                break;
        }
    }

    private void Headline(string first, string second)
    {
        titleValue = first;
        subtitleValue = second;
        title.Text = first;
        subtitle.Text = second.Length == 0 ? "" : "  " + second;
    }

    private static string MissionStats(ActivityListItem value)
    {
        var parts = new List<string> { ActivityWords.Count(value.Count, "line", "lines") };
        if (value.CostUsd > 0) parts.Add(ActivityWords.Money(value.CostUsd));
        if (value.Tokens > 0) parts.Add(ActivityWords.Count(value.Tokens, "token", "tokens"));
        if (value.Grants.Count > 0) parts.Add(ActivityWords.Count(value.Grants.Count, "grant or decision", "grants and decisions"));
        return string.Join(" · ", parts);
    }

    private void BindAction(ActivityRow row, ActivityListItem value)
    {
        var labels = list.Labels;
        indent.Width = value.Depth >= 2 ? 14 * 2 : 0;
        time.Text = row.Kind == ActivityRowKind.Unreadable ? "line " + row.At.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) : ActivityWords.Clock(row.Time, list.Zone);
        assistant.Text = row.Kind == ActivityRowKind.Unreadable ? "" : labels.ForAssistant(row.Assistant);
        var action = ActivityWords.What(row);
        if (row.Kind == ActivityRowKind.Action && !string.IsNullOrEmpty(row.Target))
        {
            tool.Text = row.Tool;
            target.Text = " · " + ActivityText.OneLine(row.Target, 160);
        }
        else
        {
            tool.Text = action;
            target.Text = "";
        }
        var showsClass = row.Kind == ActivityRowKind.Action && !list.Compact;
        actionClass.Text = row.Class;
        classChip.IsVisible = showsClass && row.Class.Length > 0;
        grantId = string.IsNullOrEmpty(row.AllowedBy) ? null : row.AllowedBy;
        grant.Text = grantId ?? "";
        grantButton.IsVisible = grantId is not null && !list.Compact;
        grantButton.IsEnabled = grantId is not null;
        AutomationProperties.SetName(grantButton, grantId is null ? "" : "Filter to " + grantId);
        actionPart.ColumnDefinitions[4].Width = classChip.IsVisible ? GridLength.Auto : new GridLength(0);
        actionPart.ColumnDefinitions[5].Width = list.Compact ? new GridLength(0) : new GridLength(128);
        // A line the check does not vouch for is marked: the failing line and every line after it.
        SetTrustMark(row.Trust == ActivityTrust.NotVerified ? "xmark.octagon.fill" : null);
        tool.Foreground = row.Kind == ActivityRowKind.Unreadable ? danger : null;
        AutomationProperties.SetName(this, ActivityWords.Sentence(row, labels, list.Zone));
    }

    private void SetTrustMark(string? symbol)
    {
        if (trustSymbol == symbol) return;
        trustSymbol = symbol;
        trust.Child = symbol is null ? null : SwiftImage.SystemName(symbol).Font(SwiftFont.System(12, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.Danger);
    }

    /// <summary>The fill: the selected line in the accent, the line the keyboard is on and the one under the pointer lighter, and a line the check does not vouch for faintly red.</summary>
    private void Paint(Animation? animation)
    {
        if (item is null) return;
        var selected = list.IsSelected(item);
        SwiftColor color;
        if (selected) color = Chrome.Accent.Opacity(0.20);
        else if (list.HasCursor(index)) color = Chrome.PrimaryText.Opacity(0.10);
        else if (hover) color = Chrome.PrimaryText.Opacity(0.06);
        else if (item.Row?.Trust == ActivityTrust.NotVerified) color = Chrome.Danger.Opacity(0.07);
        else color = SwiftColor.Clear;
        fill.Set(color, animation);
    }
}
