// PORT-ONLY: web surfaces W16a (DESIGN.md 7.2 layout components stack, row, grid; 7.4 "Earlier nodes never move except by
// growth below them"; ED-NEW-web-w16a-1). No Swift counterpart. The four layouts a live view uses, each built so a child
// added at the end never moves a child placed before it: a column (children top to bottom, the gap above a child scaled
// by its opening), a flow (children side by side from the start edge, wrapping; a centred, end or spread row holds its
// children until the row is complete, see NthUiRowView), fixed columns (a grid's cells fill equal columns in order), and
// a table row's cells (column widths fixed by weights, so a later row never widens an earlier column).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.NthUi;

/// <summary>Children top to bottom with a gap; a slot's opening scales the gap above it, so an arriving node grows in.</summary>
public sealed class NthUiColumn : Panel
{
    public double Gap { get; set; } = NthUiTokens.CardGap;

    private static double Open(Control child) => child is NthUiSlot slot ? slot.Openness : child.IsVisible ? 1 : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        double y = 0, width = 0, before = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var o = Open(child);
            if (before > 0) y += Gap * o * Math.Min(1, before);
            y += child.DesiredSize.Height;
            before += o;
            width = Math.Max(width, child.DesiredSize.Width);
        }
        return new Size(double.IsFinite(availableSize.Width) ? Math.Max(width, 0) : width, y);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0, before = 0;
        foreach (var child in Children)
        {
            var o = Open(child);
            if (before > 0) y += Gap * o * Math.Min(1, before);
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
            before += o;
        }
        return finalSize;
    }
}

/// <summary>How a flow places a line.</summary>
public enum NthUiAlign
{
    Start,
    Center,
    End,
    Between,
}

/// <summary>Children side by side from the start edge, wrapping onto new lines; each line's children top-aligned.</summary>
public sealed class NthUiFlow : Panel
{
    private readonly List<(int First, int Count, double Width, double Height)> lines = [];

    public double Gap { get; set; } = NthUiTokens.RowGap;

    public double LineGap { get; set; } = NthUiTokens.RowGap;

    public NthUiAlign Align { get; set; } = NthUiAlign.Start;

    /// <summary>Whether the panel takes its whole width (a centred, end or spread row).</summary>
    private bool Fills => Align != NthUiAlign.Start;

    protected override Size MeasureOverride(Size availableSize)
    {
        var limit = double.IsFinite(availableSize.Width) ? availableSize.Width : double.PositiveInfinity;
        lines.Clear();
        int first = 0, count = 0;
        double x = 0, lineHeight = 0, widest = 0, y = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(limit, double.PositiveInfinity));
            var size = child.DesiredSize;
            var gap = count > 0 && size.Width > .01 ? Gap : 0;
            if (count > 0 && x + gap + size.Width > limit + .01)
            {
                lines.Add((first, count, x, lineHeight));
                widest = Math.Max(widest, x);
                y += lineHeight + LineGap;
                first = i;
                count = 0;
                x = 0;
                lineHeight = 0;
                gap = 0;
            }
            x += gap + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            count++;
        }
        if (count > 0)
        {
            lines.Add((first, count, x, lineHeight));
            widest = Math.Max(widest, x);
            y += lineHeight;
        }
        return new Size(Fills && double.IsFinite(limit) ? limit : widest, y);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var (first, count, width, height) in lines)
        {
            var extra = Math.Max(0, finalSize.Width - width);
            var x = Align switch
            {
                NthUiAlign.End => extra,
                NthUiAlign.Center => extra / 2,
                _ => 0,
            };
            var spread = Align == NthUiAlign.Between && count > 1 ? extra / (count - 1) : 0;
            for (var i = first; i < first + count; i++)
            {
                var child = Children[i];
                var size = child.DesiredSize;
                if (i > first && size.Width > .01) x += Gap + spread;
                child.Arrange(new Rect(x, y, size.Width, size.Height));
                x += size.Width;
            }
            y += height + LineGap;
        }
        return finalSize;
    }
}

/// <summary>Cells in equal columns, filled in order, each row as tall as its tallest cell.</summary>
public sealed class NthUiGrid : Panel
{
    public int Columns { get; set; } = 2;

    public double Gap { get; set; } = NthUiTokens.CardGap;

    private double CellWidth(double width) => Math.Max(0, (width - Gap * (Columns - 1)) / Columns);

    private IEnumerable<Control> Shown => Children.Where(c => c.IsVisible && (c is not NthUiSlot slot || slot.IsShown || slot.Openness > 0));

    protected override Size MeasureOverride(Size availableSize)
    {
        var cell = double.IsFinite(availableSize.Width) ? CellWidth(availableSize.Width) : double.PositiveInfinity;
        double y = 0, rowHeight = 0, widest = 0;
        var column = 0;
        var rows = 0;
        foreach (var child in Shown)
        {
            child.Measure(new Size(cell, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            widest = Math.Max(widest, child.DesiredSize.Width);
            if (++column == Columns)
            {
                y += (rows++ > 0 ? Gap : 0) + rowHeight;
                rowHeight = 0;
                column = 0;
            }
        }
        if (column > 0) y += (rows > 0 ? Gap : 0) + rowHeight;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : widest * Columns + Gap * (Columns - 1);
        return new Size(width, y);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cell = CellWidth(finalSize.Width);
        var shown = Shown.ToList();
        foreach (var hidden in Children.Except(shown)) hidden.Arrange(default);
        double y = 0;
        for (var start = 0; start < shown.Count; start += Columns)
        {
            var row = shown.Skip(start).Take(Columns).ToList();
            var height = row.Max(c => c.DesiredSize.Height);
            for (var i = 0; i < row.Count; i++) row[i].Arrange(new Rect(i * (cell + Gap), y, cell, row[i].DesiredSize.Height));
            y += height + Gap;
        }
        return finalSize;
    }
}

/// <summary>A table row's cells: column widths fixed by weight, so a later row never widens an earlier column.</summary>
public sealed class NthUiCells : Panel
{
    public IReadOnlyList<double> Weights { get; set; } = [1];

    public double Gap { get; set; } = 10;

    public Thickness Padding { get; set; } = new(4, 7, 4, 7);

    private double[] Widths(double width)
    {
        var count = Math.Max(1, Weights.Count);
        var room = Math.Max(0, width - Padding.Left - Padding.Right - Gap * (count - 1));
        var total = Weights.Sum();
        return Weights.Select(w => total > 0 ? room * w / total : room / count).ToArray();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 480;
        var widths = Widths(width);
        double height = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var w = i < widths.Length ? widths[i] : 0;
            Children[i].Measure(new Size(w, double.PositiveInfinity));
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }
        return new Size(width, height + Padding.Top + Padding.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widths = Widths(finalSize.Width);
        var x = Padding.Left;
        for (var i = 0; i < Children.Count; i++)
        {
            var w = i < widths.Length ? widths[i] : 0;
            Children[i].Arrange(new Rect(x, Padding.Top, w, Math.Max(0, finalSize.Height - Padding.Top - Padding.Bottom)));
            x += w + Gap;
        }
        return finalSize;
    }
}

/// <summary>A hairline under a table row or between items (canvas .utab td border-bottom).</summary>
public sealed class NthUiRule : Control
{
    public NthUiRule()
    {
        Height = 1;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Avalonia.Automation.AutomationProperties.SetAccessibilityView(this, Avalonia.Automation.AccessibilityView.Raw);
    }

    public override void Render(DrawingContext context) =>
        context.FillRectangle(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(NthUiTokens.Hairline.Resolve(ActualThemeVariant)), new Rect(0, 0, Bounds.Width, Math.Min(1, Bounds.Height)));
}
