// PORT-ONLY: web surfaces W16b (DESIGN.md 7.2 "chart (line, bar, area, pie, scatter)" and "Editable charts ... the drag sets
// the bound state list"; ED-NEW-web-w16b-1). No Swift counterpart. What a chart draws, worked out from the runtime's chart
// data: the kind, the clean numbers (a value that is not a finite number is a gap), the stacked totals, the Y and X
// domains with their round ticks, and which of the three drawings it gets (a plot, a pie or a bar list). Pure data, so the
// rules are tested without a window.
using Marrow.Core.NthUi;

namespace Marrow.UI.NthUi.Charts;

/// <summary>The five chart types.</summary>
public enum NthUiChartKind
{
    Bar,
    Line,
    Area,
    Pie,
    Scatter,
}

/// <summary>Which drawing a chart gets.</summary>
public enum NthUiChartLayout
{
    /// <summary>Axes, gridlines and marks: line, area, scatter and vertical bars.</summary>
    Plot,

    /// <summary>A ring cut into slices, with a key.</summary>
    Pie,

    /// <summary>The approved canvas's bars: a label, a bar and the value on each row.</summary>
    BarList,
}

/// <summary>A chart's data prepared for drawing.</summary>
public sealed class NthUiChartModel
{
    /// <summary>The longest bar list: more rows than this draw as vertical bars.</summary>
    public const int MaxBarListRows = 12;

    private NthUiChartModel(NthUiChartData data)
    {
        Data = data;
        Kind = data.Type switch
        {
            "line" => NthUiChartKind.Line,
            "area" => NthUiChartKind.Area,
            "pie" => NthUiChartKind.Pie,
            "scatter" => NthUiChartKind.Scatter,
            _ => NthUiChartKind.Bar,
        };
        Count = data.Series.Count == 0 ? 0 : data.Series.Max(s => s.Values.Count);
        Values = data.Series.Select(s => s.Values.Select(v => v is double d && double.IsFinite(d) ? v : null).ToArray()).ToArray();
        var cartesian = Kind is NthUiChartKind.Bar or NthUiChartKind.Line or NthUiChartKind.Area;
        Stacked = data.Stacked && Kind is NthUiChartKind.Bar or NthUiChartKind.Area && data.Series.Count > 1;
        Editable = data.Editable && cartesian && data.Series.Count == 1;
        Layout = Kind == NthUiChartKind.Pie ? NthUiChartLayout.Pie
            : Kind == NthUiChartKind.Bar && data.Series.Count == 1 && !Editable && Count is > 0 and <= MaxBarListRows ? NthUiChartLayout.BarList
            : NthUiChartLayout.Plot;
        (YLo, YHi, YStep) = Kind == NthUiChartKind.Pie ? (0, 1, 1) : YDomain();
        YTicks = Kind == NthUiChartKind.Pie ? Array.Empty<double>() : NthUiChartMath.TicksAt(YLo, YHi, YStep);
        if (Kind == NthUiChartKind.Scatter)
        {
            (XLo, XHi, XStep) = XDomain();
            XTicks = NthUiChartMath.TicksAt(XLo, XHi, XStep);
        }
        else
        {
            (XLo, XHi, XStep) = (0, 1, 1);
            XTicks = Array.Empty<double>();
        }
    }

    /// <summary>The model of <paramref name="data"/>.</summary>
    public static NthUiChartModel From(NthUiChartData data) => new(data);

    public NthUiChartData Data { get; }

    public NthUiChartKind Kind { get; }

    public NthUiChartLayout Layout { get; }

    /// <summary>The points of the longest series (the categories of a bar, line or area chart).</summary>
    public int Count { get; }

    /// <summary>Each series' values, a gap where there is no finite number.</summary>
    public IReadOnlyList<IReadOnlyList<double?>> Values { get; }

    public IReadOnlyList<NthUiSeries> Series => Data.Series;

    /// <summary>The names of the categories (or the points' numbers).</summary>
    public IReadOnlyList<string> Labels => Data.Labels;

    /// <summary>Whether the series add up on top of each other.</summary>
    public bool Stacked { get; }

    /// <summary>Whether the reader can drag the points (a bound line, bar or area chart of one series).</summary>
    public bool Editable { get; }

    public double YLo { get; }

    public double YHi { get; }

    public double YStep { get; }

    public IReadOnlyList<double> YTicks { get; }

    public double XLo { get; }

    public double XHi { get; }

    public double XStep { get; }

    public IReadOnlyList<double> XTicks { get; }

    /// <summary>Whether there is nothing to draw yet (a chart whose series have not arrived).</summary>
    public bool IsEmpty => Count == 0 || Values.All(v => v.All(x => x is null));

    /// <summary>The label of category <paramref name="index"/>.</summary>
    public string Label(int index) => index >= 0 && index < Labels.Count ? Labels[index] : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The value of point <paramref name="index"/> of series <paramref name="series"/>, or null for a gap.</summary>
    public double? Value(int series, int index) => series < Values.Count && index >= 0 && index < Values[series].Count ? Values[series][index] : null;

    /// <summary>The x of a scatter point: its own, else its number.</summary>
    public double X(int series, int index) =>
        Series[series].X is { } xs && index < xs.Count && double.IsFinite(xs[index]) ? xs[index] : index + 1;

    /// <summary>The bottom and top of series <paramref name="series"/> at <paramref name="index"/> in a stack (the value itself when nothing stacks).</summary>
    public (double Bottom, double Top) Span(int series, int index)
    {
        var value = Value(series, index) ?? 0;
        if (!Stacked) return (0, value);
        double below = 0, above = 0;
        for (var s = 0; s < series; s++)
        {
            var v = Value(s, index) ?? 0;
            if (v >= 0) above += v;
            else below += v;
        }
        return value >= 0 ? (above, above + value) : (below + value, below);
    }

    /// <summary>The slices of a pie: the first series' values, each with its share of the whole.</summary>
    public IReadOnlyList<double> Shares => NthUiChartMath.Shares(Values.Count == 0 ? [] : Values[0]);

    /// <summary>The lowest and highest value of every series.</summary>
    public (double Lo, double Hi)? Extent()
    {
        double? lo = null, hi = null;
        for (var s = 0; s < Values.Count; s++)
        {
            for (var i = 0; i < Values[s].Count; i++)
            {
                if (Values[s][i] is not double v) continue;
                lo = lo is null ? v : Math.Min(lo.Value, v);
                hi = hi is null ? v : Math.Max(hi.Value, v);
            }
        }
        return lo is null ? null : (lo.Value, hi!.Value);
    }

    private (double Lo, double Hi, double Step) YDomain()
    {
        double vmin = 0, vmax = 1;
        var seen = false;
        void See(double v)
        {
            vmin = seen ? Math.Min(vmin, v) : v;
            vmax = seen ? Math.Max(vmax, v) : v;
            seen = true;
        }
        if (Stacked)
        {
            for (var i = 0; i < Count; i++)
            {
                double above = 0, below = 0;
                for (var s = 0; s < Values.Count; s++)
                {
                    var v = Value(s, i) ?? 0;
                    if (v >= 0) above += v;
                    else below += v;
                }
                See(above);
                See(below);
            }
        }
        else
        {
            for (var s = 0; s < Values.Count; s++)
            {
                for (var i = 0; i < Values[s].Count; i++)
                {
                    if (Values[s][i] is double v) See(v);
                }
            }
        }
        // Bars and areas rest on zero; a line or scatter reaches zero when the values are not far above it; an editable chart
        // always does, and leaves a quarter of its height above the data (and below, under zero) for the reader to drag into.
        var rest = Kind is NthUiChartKind.Bar or NthUiChartKind.Area || Editable || (vmin >= 0 && vmin <= .5 * vmax);
        var lo = Data.Min ?? (rest ? Math.Min(0, vmin) : vmin);
        var hi = Data.Max ?? (rest ? Math.Max(0, vmax) : vmax);
        if (Editable)
        {
            var span = Math.Max(hi - lo, 1);
            if (Data.Max is null) hi += .25 * span;
            if (Data.Min is null && lo < 0) lo -= .25 * span;
        }
        return NthUiChartMath.Domain(lo, hi, Data.Min is not null, Data.Max is not null, breathe: Kind == NthUiChartKind.Scatter && seen);
    }

    private (double Lo, double Hi, double Step) XDomain()
    {
        double? lo = null, hi = null;
        for (var s = 0; s < Series.Count; s++)
        {
            for (var i = 0; i < Series[s].Values.Count; i++)
            {
                if (Value(s, i) is null) continue;
                var x = X(s, i);
                lo = lo is null ? x : Math.Min(lo.Value, x);
                hi = hi is null ? x : Math.Max(hi.Value, x);
            }
        }
        return NthUiChartMath.Domain(lo ?? 0, hi ?? 1, breathe: lo is not null);
    }
}
