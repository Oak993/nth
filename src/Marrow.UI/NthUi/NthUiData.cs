// PORT-ONLY: web surfaces W16a (DESIGN.md 7.2 components table, list, checklist, timeline; 7.4 "tables add rows with a soft
// fade"; 10 "tables expose rows and headers"; canvas index.html .utab: 13 px tabular numerals, .5 px rules, the last column
// right-aligned, the total row semibold; ED-NEW-web-w16a-1). No Swift counterpart. Rows and items are slots in a column, so
// each arrives with a soft fade and its height opening, and a row a filter hides closes smoothly while the rows under it
// slide up. Column widths are fixed by weight from the first row (the first column one and a half shares), so a later row
// never widens an earlier column and nothing placed moves. The map stand-in (W16c replaces it) reuses the table and list.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.NthUi;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi;

/// <summary>One row a table grid shows.</summary>
public sealed record NthUiGridRow(string Key, IReadOnlyList<string> Cells, bool Total = false, bool Muted = false);

/// <summary>A table's header and rows with fixed columns; rows keep their slots across refreshes.</summary>
public sealed class NthUiTableGrid
{
    private readonly NthUiContext context;
    private readonly NthUiColumn column = new() { Gap = 0 };
    private readonly NthUiColumn body = new() { Gap = 0 };
    private readonly Dictionary<string, Row> rows = new(StringComparer.Ordinal);
    private TextBlock? title;
    private Control? header;
    private string[] columns = [];
    private double[] weights = [];

    private sealed class Row
    {
        public required NthUiSlot Slot { get; init; }
        public required NthUiCells Cells { get; init; }
        public required List<TextBlock> Texts { get; init; }
        public required NthUiRule Rule { get; init; }
        public bool Total { get; set; }
        public bool Muted { get; set; }
    }

    public NthUiTableGrid(NthUiContext context, string? caption)
    {
        this.context = context;
        if (!string.IsNullOrEmpty(caption))
        {
            title = NthUiText.Block(caption, NthUiTokens.BodyStrong, NthUiTokens.Text1);
            title.Margin = new Thickness(0, 0, 0, 6);
            column.Children.Add(title);
        }
        column.Children.Add(body);
        AutomationProperties.SetControlTypeOverride(column, AutomationControlType.Table);
        if (!string.IsNullOrEmpty(caption)) AutomationProperties.SetName(column, caption);
    }

    public Control Control => column;

    public int ColumnCount => weights.Length;

    public void SetTitle(string caption)
    {
        if (title is not null) title.Text = caption;
    }

    /// <summary>Fixes the columns (once): their names for the header, or only their count.</summary>
    public void SetColumns(IReadOnlyList<string> names, int count)
    {
        if (weights.Length > 0) return;
        count = Math.Max(1, Math.Max(count, names.Count));
        weights = Enumerable.Range(0, count).Select(i => i == 0 && count > 1 ? 1.5 : 1.0).ToArray();
        columns = names.ToArray();
        if (columns.Length == 0) return;
        var cells = Cells();
        foreach (var (name, i) in columns.Select((n, i) => (n, i)))
        {
            var text = NthUiText.Block(name, NthUiTokens.Caption, NthUiTokens.Text2);
            text.TextAlignment = Align(i);
            AutomationProperties.SetIsColumnHeader(text, true);
            cells.Children.Add(text);
        }
        var rule = new NthUiRule { VerticalAlignment = VerticalAlignment.Bottom };
        header = new Panel { Children = { cells, rule } };
        AutomationProperties.SetControlTypeOverride(header, AutomationControlType.Header);
        column.Children.Insert(column.Children.IndexOf(body), header);
    }

    private TextAlignment Align(int i) => i == 0 || weights.Length == 1 ? TextAlignment.Left : TextAlignment.Right;

    private NthUiCells Cells() => new() { Weights = weights };

    /// <summary>Shows <paramref name="key"/> with <paramref name="cells"/>, adding it at <paramref name="index"/> (or the end)
    /// with a soft fade when new.</summary>
    public void Show(NthUiGridRow row, bool visible, int? index = null)
    {
        if (weights.Length == 0) SetColumns([], row.Cells.Count);
        if (!rows.TryGetValue(row.Key, out var shown))
        {
            var cells = Cells();
            var texts = new List<TextBlock>();
            for (var i = 0; i < weights.Length; i++)
            {
                var text = NthUiText.Block("", NthUiTokens.Numbers, NthUiTokens.Text1);
                text.TextAlignment = Align(i);
                texts.Add(text);
                cells.Children.Add(text);
            }
            var rule = new NthUiRule { VerticalAlignment = VerticalAlignment.Bottom };
            var panel = new Panel { Children = { cells, rule } };
            AutomationProperties.SetControlTypeOverride(panel, AutomationControlType.DataItem);
            var slot = new NthUiSlot(context.Motion) { Key = row.Key, Mode = NthUiSlotMode.Follow, Child = panel };
            shown = new Row { Slot = slot, Cells = cells, Texts = texts, Rule = rule };
            rows[row.Key] = shown;
            if (index is int at && at <= body.Children.Count) body.Children.Insert(at, slot);
            else body.Children.Add(slot);
            Fill(shown, row);
            slot.Arrive(visible, revealSize: true);
            return;
        }
        Fill(shown, row);
        shown.Slot.SetShown(visible);
    }

    /// <summary>Hides every row whose key is not in <paramref name="keep"/>.</summary>
    public void HideExcept(IReadOnlySet<string> keep)
    {
        foreach (var (key, row) in rows)
        {
            if (!keep.Contains(key)) row.Slot.SetShown(false);
        }
    }

    private void Fill(Row shown, NthUiGridRow row)
    {
        var count = shown.Texts.Count;
        for (var i = 0; i < count; i++)
        {
            var text = i < row.Cells.Count ? row.Cells[i] : "";
            if (i == count - 1 && row.Cells.Count > count) text = string.Join(" · ", row.Cells.Skip(count - 1));
            if (shown.Texts[i].Text != text) shown.Texts[i].Text = text;
        }
        if (shown.Total != row.Total || shown.Muted != row.Muted)
        {
            shown.Total = row.Total;
            shown.Muted = row.Muted;
            foreach (var text in shown.Texts)
            {
                (row.Total ? NthUiTokens.Numbers.FontWeight(SwiftFont.Weight.Semibold) : NthUiTokens.Numbers).Apply(text);
                text.Foreground = (row.Muted ? NthUiTokens.Text2 : NthUiTokens.Text1).Brush(text);
            }
            shown.Rule.IsVisible = !row.Total;
            shown.Cells.Padding = row.Total ? new Thickness(4, 9, 4, 7) : new Thickness(4, 7, 4, 7);
        }
        var names = columns.Length > 0 ? columns : null;
        var name = string.Join(", ", row.Cells.Select((c, i) => (Text: c, At: i)).Where(c => c.Text.Length > 0)
            .Select(c => names is not null && c.At < names.Length && names[c.At].Length > 0 ? $"{names[c.At]} {c.Text}" : c.Text));
        if (!string.Equals(AutomationProperties.GetName(shown.Slot.Child!), name, StringComparison.Ordinal)) AutomationProperties.SetName(shown.Slot.Child!, name);
    }
}

/// <summary>`table`: columns from `cols` (or the first row), rows from `rows=` and from row lines.</summary>
public sealed class NthUiTableView : NthUiNodeView
{
    private readonly NthUiTableGrid grid;
    private int dataRows;

    public NthUiTableView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        grid = new NthUiTableGrid(context, Runtime.Text(node));
        var cols = Runtime.Values(node, "cols").Select(v => v.Display(Runtime.Culture)).ToArray();
        if (cols.Length > 0) grid.SetColumns(cols, cols.Length);
        ShowData();
    }

    public override Control Control => grid.Control;

    public override NthUiSlotMode SlotMode => NthUiSlotMode.Exact;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind != "row" || child.Degraded) return null;
        Context.Register(child, this);
        var cells = Runtime.Cells(child).Select(v => v.Display(Runtime.Culture)).ToArray();
        grid.Show(new NthUiGridRow(child.Key, cells, child.Has("total"), child.Has("muted")), Runtime.IsVisible(child));
        return null;
    }

    private void ShowData()
    {
        if (Node.Arg("rows") is null) return;
        var table = Runtime.Table(Node);
        var data = table.Rows.Where(r => r.Key is null).ToArray();
        for (var i = 0; i < data.Length; i++) grid.Show(new NthUiGridRow(Node.Key + "#" + i, data[i].Cells), true, i);
        for (var i = data.Length; i < dataRows; i++) grid.Show(new NthUiGridRow(Node.Key + "#" + i, []), false);
        dataRows = Math.Max(dataRows, data.Length);
    }

    public override void Refresh()
    {
        grid.SetTitle(Runtime.Text(Node));
        ShowData();
        foreach (var child in Node.Children)
        {
            if (child.Kind != "row" || child.Degraded) continue;
            var visible = Runtime.IsVisible(child);
            var cells = visible ? Runtime.Cells(child).Select(v => v.Display(Runtime.Culture)).ToArray() : [];
            if (visible) grid.Show(new NthUiGridRow(child.Key, cells, child.Has("total"), child.Has("muted")), true);
            else grid.Show(new NthUiGridRow(child.Key, Runtime.Cells(child).Select(v => v.Display(Runtime.Culture)).ToArray(), child.Has("total"), child.Has("muted")), false);
        }
    }
}

/// <summary>A list's marker or a checklist's box beside its text and detail.</summary>
internal static class NthUiItems
{
    public static Grid Line(Control marker, string text, string? detail, out TextBlock textBlock, out TextBlock? detailBlock)
    {
        textBlock = NthUiText.Block(text, NthUiTokens.Body, NthUiTokens.Text1);
        var column = new StackPanel { Spacing = 1, Children = { textBlock } };
        detailBlock = null;
        if (!string.IsNullOrEmpty(detail))
        {
            detailBlock = NthUiText.Block(detail, NthUiTokens.Small, NthUiTokens.Text2);
            column.Children.Add(detailBlock);
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        marker.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(marker);
        Grid.SetColumn(column, 1);
        grid.Children.Add(column);
        return grid;
    }
}

/// <summary>`list`: bullets or numbers, from `items=` and from item lines.</summary>
public sealed class NthUiListView : NthUiNodeView
{
    private readonly NthUiColumn column = new() { Gap = 6 };
    private readonly NthUiColumn items = new() { Gap = 6 };
    private readonly Dictionary<string, (TextBlock Text, TextBlock? Detail)> lines = new(StringComparer.Ordinal);
    private readonly TextBlock? title;
    private int count;

    public NthUiListView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var caption = Runtime.Text(node);
        if (caption.Length > 0)
        {
            title = NthUiText.Block(caption, NthUiTokens.BodyStrong, NthUiTokens.Text1);
            column.Children.Add(title);
        }
        column.Children.Add(items);
        AutomationProperties.SetControlTypeOverride(column, AutomationControlType.List);
        foreach (var value in Runtime.Values(node, "items")) AddLine(Node.Key + "#" + count, value.Display(Runtime.Culture), null, true);
    }

    public override Control Control => column;

    public override NthUiSlotMode SlotMode => NthUiSlotMode.Exact;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind != "item" || child.Degraded) return null;
        Context.Register(child, this);
        AddLine(child.Key, Runtime.Text(child), child.Arg("detail") is null ? null : Runtime.Display(child, "detail"), Runtime.IsVisible(child));
        return null;
    }

    private void AddLine(string key, string text, string? detail, bool visible)
    {
        count++;
        var marker = NthUiText.Block(Node.Has("ordered") ? count.ToString(Runtime.Culture) + "." : "•", NthUiTokens.Numbers, NthUiTokens.Text3, wrap: false);
        marker.MinWidth = Node.Has("ordered") ? 22 : 14;
        var line = NthUiItems.Line(marker, text, detail, out var textBlock, out var detailBlock);
        AutomationProperties.SetControlTypeOverride(line, AutomationControlType.ListItem);
        AutomationProperties.SetName(line, text);
        lines[key] = (textBlock, detailBlock);
        var slot = new NthUiSlot(Context.Motion) { Key = key, Child = line };
        items.Children.Add(slot);
        slot.Arrive(visible, revealSize: true);
    }

    public override void Refresh()
    {
        if (title is not null) title.Text = Runtime.Text(Node);
        foreach (var child in Node.Children)
        {
            if (child.Kind != "item" || child.Degraded || !lines.TryGetValue(child.Key, out var line)) continue;
            line.Text.Text = Runtime.Text(child);
            if (line.Detail is not null) line.Detail.Text = Runtime.Display(child, "detail");
            if (line.Text.Parent?.Parent?.Parent is NthUiSlot slot) slot.SetShown(Runtime.IsVisible(child));
        }
    }
}

/// <summary>A checklist's box: a real check box (Toggle pattern), drawn monochrome with a short fade of its tick.</summary>
public sealed class NthUiCheck : ToggleButton, INthUiAnimated
{
    static NthUiCheck() => AffectsRender<NthUiCheck>(EnvironmentValues.AccessibilityHighContrastProperty, EnvironmentValues.AccessibilityContrastSurfaceProperty);
    private readonly NthUiMotion motion;
    private NthUiTween tick = NthUiTween.At(0);

    internal NthUiCheck(NthUiMotion motion)
    {
        this.motion = motion;
        Width = 18;
        Height = 18;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        IsCheckedChanged += (_, _) => Animate();
        Classes.CollectionChanged += (_, _) => InvalidateVisual();
    }

    protected override Type StyleKeyOverride => typeof(NthUiCheck);

    protected override AutomationPeer OnCreateAutomationPeer() => new ToggleButtonAutomationPeer(this);

    private void Animate()
    {
        var target = IsChecked == true ? 1 : 0;
        if (!motion.CanAnimate || NthUiCalm.For(this)) tick = NthUiTween.At(target);
        else
        {
            tick = tick.Retarget(target, NthUiMotion.Now, .16, NthEase.Out);
            motion.Start(this);
        }
        InvalidateVisual();
    }

    bool INthUiAnimated.Step(double now)
    {
        InvalidateVisual();
        return !tick.Settled(now);
    }

    protected override Size MeasureOverride(Size availableSize) => new(18, 18);

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        var contrast = EnvironmentValues.ContrastPaletteFor(this);
        var t = contrast is null ? Math.Clamp(tick.Value(NthUiMotion.Now), 0, 1) : IsChecked == true ? 1 : 0;
        var box = new Rect(1, 1, 16, 16);
        var round = new RoundedRect(box, 5);
        context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(NthUiTokens.Text3.Resolve(theme, contrast)), 1.5), round);
        if (t > 0)
        {
            var strong = NthUiTokens.Strong.Resolve(theme, contrast);
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb((byte)(strong.A * t), strong.R, strong.G, strong.B)), null, round);
            var on = NthUiTokens.OnStrong.Resolve(theme, contrast);
            var pen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(on.A * t), on.R, on.G, on.B)), 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var geometry = new PolylineGeometry([new Point(5.2, 9.3), new Point(8, 12), new Point(13, 6.4)], false);
            context.DrawGeometry(null, pen, geometry);
        }
        if (Classes.Contains(":focus-visible"))
            context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(NthUiTokens.Accent.Resolve(theme, contrast)), 2), new RoundedRect(box.Inflate(2.5), 7));
    }
}

/// <summary>`checklist`: items with check boxes; ticking one changes the checklist's state (the list of done values).</summary>
public sealed class NthUiChecklistView : NthUiNodeView
{
    private readonly NthUiColumn column = new() { Gap = 8 };
    private readonly NthUiColumn items = new() { Gap = 8 };
    private readonly Dictionary<string, (NthUiCheck Box, TextBlock Text, NthUiSlot Slot)> lines = new(StringComparer.Ordinal);
    private readonly TextBlock? title;

    public NthUiChecklistView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var caption = Runtime.Text(node);
        if (caption.Length > 0)
        {
            title = NthUiText.Block(caption, NthUiTokens.BodyStrong, NthUiTokens.Text1);
            column.Children.Add(title);
        }
        column.Children.Add(items);
        AutomationProperties.SetControlTypeOverride(column, AutomationControlType.List);
        AutomationProperties.SetName(column, caption);
    }

    public override Control Control => column;

    public override NthUiSlotMode SlotMode => NthUiSlotMode.Exact;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind != "item" || child.Degraded) return null;
        Context.Register(child, this);
        var text = Runtime.Text(child);
        var box = new NthUiCheck(Context.Motion) { Margin = new Thickness(0, 0, 10, 0) };
        box.IsChecked = Runtime.IsChecked(child);
        AutomationProperties.SetName(box, text);
        box.Click += (_, _) =>
        {
            Context.Run(child);
            box.IsChecked = Runtime.IsChecked(child);
        };
        var line = NthUiItems.Line(box, text, child.Arg("detail") is null ? null : Runtime.Display(child, "detail"), out var textBlock, out _);
        textBlock.Foreground = (box.IsChecked == true ? NthUiTokens.Text2 : NthUiTokens.Text1).Brush(textBlock);
        textBlock.Cursor = new Cursor(StandardCursorType.Hand);
        textBlock.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            Context.Run(child);
            box.IsChecked = Runtime.IsChecked(child);
        };
        var slot = new NthUiSlot(Context.Motion) { Key = child.Key, Child = line };
        lines[child.Key] = (box, textBlock, slot);
        items.Children.Add(slot);
        slot.Arrive(Runtime.IsVisible(child), revealSize: true);
        return null;
    }

    public override void Refresh()
    {
        if (title is not null) title.Text = Runtime.Text(Node);
        foreach (var child in Node.Children)
        {
            if (!lines.TryGetValue(child.Key, out var line)) continue;
            var done = Runtime.IsChecked(child);
            line.Box.IsChecked = done;
            line.Text.Text = Runtime.Text(child);
            line.Text.Foreground = (done ? NthUiTokens.Text2 : NthUiTokens.Text1).Brush(line.Text);
            line.Slot.SetShown(Runtime.IsVisible(child));
        }
    }
}

/// <summary>A timeline's rail: a dot in the event's tone (filled when done) and the line down to the next event.</summary>
public sealed class NthUiRail : Control
{
    static NthUiRail() => AffectsRender<NthUiRail>(EnvironmentValues.AccessibilityHighContrastProperty, EnvironmentValues.AccessibilityContrastSurfaceProperty);
    private string? tone;
    private bool done;
    private bool last = true;

    public NthUiRail()
    {
        Width = 18;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public void Set(string? tone, bool done)
    {
        this.tone = tone;
        this.done = done;
        InvalidateVisual();
    }

    public bool IsLast
    {
        get => last;
        set
        {
            last = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        var contrast = EnvironmentValues.ContrastPaletteFor(this);
        var color = (tone is null ? NthUiTokens.Text3 : NthUiTokens.Tone(tone)).Resolve(theme, contrast);
        var center = new Point(7, 9);
        if (!last)
            context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(NthUiTokens.Hairline.Resolve(theme, contrast)), 1.5), new Point(7, 15), new Point(7, Bounds.Height + 10));
        if (done) context.DrawEllipse(new ImmutableSolidColorBrush(color), null, center, 4.5, 4.5);
        else context.DrawEllipse(null, new ImmutablePen(new ImmutableSolidColorBrush(color), 1.5), center, 4, 4);
    }
}

/// <summary>`timeline`: events down a rail with their time and detail.</summary>
public sealed class NthUiTimelineView : NthUiNodeView
{
    private readonly NthUiColumn column = new() { Gap = 10 };
    private readonly NthUiColumn events = new() { Gap = 10 };
    private readonly Dictionary<string, (TextBlock Title, TextBlock? Time, TextBlock? Detail, NthUiRail Rail, NthUiSlot Slot)> lines = new(StringComparer.Ordinal);
    private readonly TextBlock? title;
    private NthUiRail? lastRail;

    public NthUiTimelineView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var caption = Runtime.Text(node);
        if (caption.Length > 0)
        {
            title = NthUiText.Block(caption, NthUiTokens.BodyStrong, NthUiTokens.Text1);
            column.Children.Add(title);
        }
        column.Children.Add(events);
        AutomationProperties.SetControlTypeOverride(column, AutomationControlType.List);
        AutomationProperties.SetName(column, caption);
    }

    public override Control Control => column;

    public override NthUiSlotMode SlotMode => NthUiSlotMode.Exact;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind is not ("event" or "item") || child.Degraded) return null;
        Context.Register(child, this);
        var rail = new NthUiRail();
        rail.Set(child.Arg("tone")?.Literal.AsText, child.Has("done"));
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var titleBlock = NthUiText.Block(Runtime.Text(child), NthUiTokens.BodyStrong, NthUiTokens.Text1);
        TextBlock? time = null, detail = null;
        if (child.Arg("time") is not null)
        {
            time = NthUiText.Block(Runtime.Display(child, "time"), NthUiTokens.Small.MonospacedDigit(), NthUiTokens.Text3, wrap: false);
            time.VerticalAlignment = VerticalAlignment.Center;
        }
        var text = new StackPanel { Spacing = 1, Children = { titleBlock } };
        if (time is not null) text.Children.Insert(0, time);
        if (child.Arg("detail") is not null)
        {
            detail = NthUiText.Block(Runtime.Display(child, "detail"), NthUiTokens.Small, NthUiTokens.Text2);
            text.Children.Add(detail);
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(rail);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        AutomationProperties.SetControlTypeOverride(grid, AutomationControlType.ListItem);
        AutomationProperties.SetName(grid, string.Join(", ", new[] { time?.Text, titleBlock.Text, detail?.Text }.Where(s => !string.IsNullOrEmpty(s))));
        if (lastRail is not null) lastRail.IsLast = false;
        lastRail = rail;
        var slot = new NthUiSlot(Context.Motion) { Key = child.Key, Child = grid };
        lines[child.Key] = (titleBlock, time, detail, rail, slot);
        events.Children.Add(slot);
        slot.Arrive(Runtime.IsVisible(child), revealSize: true);
        return null;
    }

    public override void Refresh()
    {
        if (title is not null) title.Text = Runtime.Text(Node);
        foreach (var child in Node.Children)
        {
            if (!lines.TryGetValue(child.Key, out var line)) continue;
            line.Title.Text = Runtime.Text(child);
            if (line.Time is not null) line.Time.Text = Runtime.Display(child, "time");
            if (line.Detail is not null) line.Detail.Text = Runtime.Display(child, "detail");
            line.Slot.SetShown(Runtime.IsVisible(child));
        }
    }
}
