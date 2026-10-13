// PORT-ONLY: web surfaces W16b (DESIGN.md 7.5 monochrome line art, 10 contrast and keyboard; ED-NEW-web-w16b-1). No Swift
// counterpart. The keys of a chart: for several series, a swatch (the line's dash and marker, or the bar's fill) and the name;
// for a pie, a row a slice with its swatch, name, value and share, which lights up with the slice the reader is on and lights
// the slice when the pointer is on the row. A pie and its key sit side by side when there is room and stack when there is not.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Marrow.UI.NthUi.Charts;

/// <summary>A swatch in a key: the look of one series (or slice).</summary>
public sealed class NthUiLegendSwatch : Control
{
    private readonly int series;
    private readonly int count;
    private readonly bool line;
    private readonly bool slice;
    private double? tone;

    /// <summary>A swatch for series <paramref name="series"/> of <paramref name="count"/>: a short line with its marker when
    /// <paramref name="line"/>, else a filled square. A <paramref name="slice"/> swatch is filled with its <see cref="Tone"/>.</summary>
    public NthUiLegendSwatch(int series, int count, bool line, bool slice = false)
    {
        this.series = series;
        this.count = count;
        this.line = line;
        this.slice = slice;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The strength a slice swatch is filled at; null draws the pale swatch of a slice with no share.</summary>
    public double? Tone
    {
        get => tone;
        set
        {
            if (tone == value) return;
            tone = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(line ? 22 : 12, 12);

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        var strong = NthUiTokens.Strong.Resolve(theme);
        var middle = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (!line)
        {
            var box = new RoundedRect(new Rect(1, 1, Bounds.Width - 2, Bounds.Height - 2), 3);
            if (slice && tone is null)
            {
                context.DrawRectangle(new ImmutableSolidColorBrush(NthUiTokens.Track.Resolve(theme)), null, box);
                return;
            }
            var fill = tone ?? NthUiChartInk.FillTone(series, count);
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(255 * fill), strong.R, strong.G, strong.B)), null, box);
            return;
        }
        var style = NthUiChartInk.Series(series);
        var color = Color.FromArgb((byte)Math.Round(255 * style.Tone), strong.R, strong.G, strong.B);
        context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(color), 2, style.Dash is null ? null : new ImmutableDashStyle(style.Dash, 0), PenLineCap.Round), new Point(1, middle.Y), new Point(Bounds.Width - 1, middle.Y));
        var face = NthUiChartInk.Face.Resolve(theme);
        var rim = new ImmutablePen(new ImmutableSolidColorBrush(color), 1.5);
        var fillBrush = new ImmutableSolidColorBrush(face);
        switch (style.Marker)
        {
            case NthUiMarker.Square:
                context.DrawRectangle(fillBrush, rim, new Rect(middle.X - 3, middle.Y - 3, 6, 6));
                break;
            case NthUiMarker.Diamond:
                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(middle.X, middle.Y - 4.2), true);
                    g.LineTo(new Point(middle.X + 4.2, middle.Y));
                    g.LineTo(new Point(middle.X, middle.Y + 4.2));
                    g.LineTo(new Point(middle.X - 4.2, middle.Y));
                    g.EndFigure(true);
                }
                context.DrawGeometry(fillBrush, rim, diamond);
                break;
            case NthUiMarker.Triangle:
                var triangle = new StreamGeometry();
                using (var g = triangle.Open())
                {
                    g.BeginFigure(new Point(middle.X, middle.Y - 4.2), true);
                    g.LineTo(new Point(middle.X + 4, middle.Y + 3));
                    g.LineTo(new Point(middle.X - 4, middle.Y + 3));
                    g.EndFigure(true);
                }
                context.DrawGeometry(fillBrush, rim, triangle);
                break;
            default:
                context.DrawEllipse(fillBrush, rim, middle, 3.25, 3.25);
                break;
        }
    }
}

/// <summary>The key under a chart of several series.</summary>
public sealed class NthUiSeriesKey : WrapPanel
{
    public NthUiSeriesKey()
    {
        Orientation = Orientation.Horizontal;
        ItemSpacing = 14;
        LineSpacing = 4;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.List);
    }

    /// <summary>The names on show.</summary>
    public IReadOnlyList<string> Names { get; private set; } = [];

    /// <summary>Shows one item a series.</summary>
    public void Show(NthUiChartModel model)
    {
        var names = Enumerable.Range(0, model.Series.Count).Select(s => NthUiChartSummary.SeriesName(model, s)).ToArray();
        var line = model.Kind is NthUiChartKind.Line or NthUiChartKind.Scatter;
        if (names.SequenceEqual(Names) && Children.Count == names.Length) return;
        Names = names;
        Children.Clear();
        for (var s = 0; s < names.Length; s++)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new NthUiLegendSwatch(s, names.Length, line || model.Kind == NthUiChartKind.Scatter) { VerticalAlignment = VerticalAlignment.Center });
            var label = NthUiText.Block(names[s], NthUiChartInk.LabelFont, NthUiTokens.Text2, wrap: false);
            label.VerticalAlignment = VerticalAlignment.Center;
            item.Children.Add(label);
            AutomationProperties.SetName(item, names[s]);
            AutomationProperties.SetControlTypeOverride(item, AutomationControlType.ListItem);
            Children.Add(item);
        }
    }
}

/// <summary>The key of a pie: a row a slice.</summary>
public sealed class NthUiPieKey : StackPanel
{
    private readonly NthUiPieChart pie;
    private readonly CultureInfo culture;
    private readonly List<Row> rows = [];

    private sealed class Row(Border border, NthUiLegendSwatch swatch, TextBlock name, TextBlock value, TextBlock share)
    {
        public Border Border { get; } = border;

        public NthUiLegendSwatch Swatch { get; } = swatch;

        public TextBlock Name { get; } = name;

        public TextBlock Value { get; } = value;

        public TextBlock Share { get; } = share;
    }

    public NthUiPieKey(NthUiPieChart pie, CultureInfo culture)
    {
        this.pie = pie;
        this.culture = culture;
        Spacing = 2;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.List);
        pie.ActiveChanged += _ => Light();
        Show(pie.Model);
    }

    /// <summary>The rows' texts, as drawn: name, value and share.</summary>
    public IReadOnlyList<(string Name, string Value, string Share)> Shown => rows.Select(r => (r.Name.Text ?? "", r.Value.Text ?? "", r.Share.Text ?? "")).ToArray();

    /// <summary>Shows one row a slice.</summary>
    public void Show(NthUiChartModel model)
    {
        var shares = model.Shares;
        var percent = NthUiChartMath.Percentages(shares);
        if (rows.Count != shares.Count) Rebuild(shares.Count);
        var live = shares.Count(s => s > 0);
        var shown = 0;
        for (var i = 0; i < shares.Count; i++)
        {
            var row = rows[i];
            row.Swatch.Tone = shares[i] > 0 ? NthUiChartInk.FillTone(shown++, live) : null;
            row.Name.Text = model.Label(i);
            row.Value.Text = NthUiNumbers.Format(model.Value(0, i), culture, model.Data.Unit);
            row.Share.Text = percent[i].ToString(culture) + "%";
            AutomationProperties.SetName(row.Border, NthUiStrings.Slice(model.Label(i), percent[i].ToString(culture), row.Value.Text));
        }
        Light();
    }

    private void Rebuild(int count)
    {
        Children.Clear();
        rows.Clear();
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var swatch = new NthUiLegendSwatch(i, count, false, slice: true) { VerticalAlignment = VerticalAlignment.Center };
            var name = NthUiText.Block("", NthUiChartInk.LabelFont, NthUiTokens.Text1, wrap: false);
            var value = NthUiText.Block("", NthUiTokens.Mono, NthUiTokens.Text1, wrap: false);
            var share = NthUiText.Block("", NthUiChartInk.LabelFont, NthUiTokens.Text2, wrap: false);
            value.TextAlignment = Avalonia.Media.TextAlignment.Right;
            share.TextAlignment = Avalonia.Media.TextAlignment.Right;
            share.MinWidth = 38;
            foreach (var text in new[] { name, value, share }) text.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
            grid.Children.Add(swatch);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(value, 2);
            Grid.SetColumn(share, 3);
            grid.Children.Add(name);
            grid.Children.Add(value);
            grid.Children.Add(share);
            var border = new Border { Child = grid, Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            AutomationProperties.SetControlTypeOverride(border, AutomationControlType.ListItem);
            border.PointerEntered += (_, _) => pie.Emphasise(index);
            border.PointerExited += (_, _) => pie.Emphasise(null);
            rows.Add(new Row(border, swatch, name, value, share));
            Children.Add(border);
        }
    }

    private void Light()
    {
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Border.Background = pie.Active == i ? NthUiTokens.ControlFill.Brush(rows[i].Border) : Brushes.Transparent;
        }
    }
}

/// <summary>A pie and its key: side by side from 340 DIP wide, else one over the other.</summary>
public sealed class NthUiPieLayout : Panel
{
    private const double SideBySide = 340;
    private const double Gap = 18;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 420;
        if (Children.Count < 2) return default;
        var pie = Children[0];
        var key = Children[1];
        pie.Measure(new Size(width, NthUiPieChart.Diameter));
        if (width >= SideBySide)
        {
            key.Measure(new Size(Math.Max(0, width - pie.DesiredSize.Width - Gap), double.PositiveInfinity));
            return new Size(width, Math.Max(pie.DesiredSize.Height, key.DesiredSize.Height));
        }
        key.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, pie.DesiredSize.Height + 6 + key.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2) return finalSize;
        var pie = Children[0];
        var key = Children[1];
        if (finalSize.Width >= SideBySide)
        {
            var height = Math.Max(pie.DesiredSize.Height, key.DesiredSize.Height);
            pie.Arrange(new Rect(0, (height - pie.DesiredSize.Height) / 2, pie.DesiredSize.Width, pie.DesiredSize.Height));
            var x = pie.DesiredSize.Width + Gap;
            key.Arrange(new Rect(x, (height - key.DesiredSize.Height) / 2, Math.Max(0, finalSize.Width - x), key.DesiredSize.Height));
        }
        else
        {
            pie.Arrange(new Rect((finalSize.Width - pie.DesiredSize.Width) / 2, 0, pie.DesiredSize.Width, pie.DesiredSize.Height));
            key.Arrange(new Rect(0, pie.DesiredSize.Height + 6, finalSize.Width, key.DesiredSize.Height));
        }
        return finalSize;
    }
}
