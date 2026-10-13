// PORT-ONLY: web surfaces W16b (DESIGN.md 10 "charts carry a text summary and an 'Show as table' alternative"; ED-NEW-web-w16b-1).
// No Swift counterpart. A chart in words: the sentence a screen reader gets as the chart's description (and the table view
// shows above its rows), and the lines of the readout that a hovered or focused point shows. Built from the model alone, so
// the words are tested without a window; every fixed word is in NthUiStrings.
using System.Globalization;
using System.Text;

namespace Marrow.UI.NthUi.Charts;

/// <summary>One line of a readout: a series (or slice) name and its value.</summary>
public sealed record NthUiReadoutRow(int Series, string Name, string Value);

/// <summary>What a hovered, pressed or focused point shows: the category and each series' value there.</summary>
public sealed record NthUiReadout(string Heading, IReadOnlyList<NthUiReadoutRow> Rows)
{
    /// <summary>The readout as one line of speech: "Mar: Web 120, Mobile 80".</summary>
    public string Spoken => Heading + (Rows.Count == 0 ? "" : ": " + string.Join(", ", Rows.Select(r => r.Name.Length == 0 ? r.Value : r.Name + " " + r.Value)));
}

/// <summary>The words of a chart.</summary>
public static class NthUiChartSummary
{
    /// <summary>A chart's most this many values are named one by one; more are described by their extremes.</summary>
    public const int NamedValues = 8;

    /// <summary>The chart in a few sentences.</summary>
    public static string Describe(NthUiChartModel model, CultureInfo culture)
    {
        var text = new StringBuilder(NthUiStrings.ChartHeading(NthUiStrings.KindName(model.Kind), model.Data.Title));
        if (model.IsEmpty)
        {
            text.Append(' ').Append(NthUiStrings.NoDataSentence);
            return text.ToString();
        }
        switch (model.Kind)
        {
            case NthUiChartKind.Pie:
                Pie(model, culture, text);
                break;
            case NthUiChartKind.Scatter:
                Scatter(model, culture, text);
                break;
            default:
                Cartesian(model, culture, text);
                break;
        }
        return text.ToString();
    }

    /// <summary>What a pointer or a key on category <paramref name="index"/> reads out (the slice, for a pie).</summary>
    public static NthUiReadout Readout(NthUiChartModel model, int index, CultureInfo culture)
    {
        var unit = model.Data.Unit;
        var rows = new List<NthUiReadoutRow>();
        if (model.Kind == NthUiChartKind.Pie)
        {
            var shares = model.Shares;
            var percent = NthUiChartMath.Percentages(shares);
            if (index < 0 || index >= shares.Count) return new NthUiReadout("", rows);
            rows.Add(new NthUiReadoutRow(0, "", NthUiNumbers.Format(model.Value(0, index), culture, unit) + " · " + percent[index].ToString(culture) + "%"));
            return new NthUiReadout(model.Label(index), rows);
        }
        for (var s = 0; s < model.Series.Count; s++)
        {
            if (model.Value(s, index) is not double value) continue;
            rows.Add(new NthUiReadoutRow(s, SeriesName(model, s), NthUiNumbers.Format(value, culture, unit)));
        }
        return new NthUiReadout(model.Label(index), rows);
    }

    /// <summary>What a scatter point reads out: its series and both of its numbers.</summary>
    public static NthUiReadout ScatterReadout(NthUiChartModel model, int series, int index, CultureInfo culture)
    {
        var unit = model.Data.Unit;
        var x = NthUiValueText(model.X(series, index), culture);
        var rows = new List<NthUiReadoutRow> { new(series, "", NthUiNumbers.Format(model.Value(series, index), culture, unit)) };
        var heading = SeriesName(model, series);
        return new NthUiReadout(heading.Length > 0 ? heading + " · " + x : x, rows);
    }

    /// <summary>A series' name, empty for the lone unnamed series.</summary>
    public static string SeriesName(NthUiChartModel model, int series) =>
        model.Series[series].Name.Length > 0 ? model.Series[series].Name : model.Series.Count > 1 ? NthUiStrings.SeriesNumber(series + 1) : "";

    private static string NthUiValueText(double value, CultureInfo culture) => Marrow.Core.NthUi.NthUiValue.FormatNumber(value, culture);

    private static void Cartesian(NthUiChartModel model, CultureInfo culture, StringBuilder text)
    {
        var unit = model.Data.Unit;
        string V(double value) => NthUiNumbers.Format(value, culture, unit);
        if (model.Series.Count == 1)
        {
            var points = Present(model, 0);
            if (points.Count <= NamedValues)
            {
                text.Append(' ').Append(NthUiStrings.Listed(NthUiStrings.Points(points.Count, model.Kind == NthUiChartKind.Bar), string.Join(", ", points.Select(p => model.Label(p.Index) + " " + V(p.Value)))));
                if (points.Count > 2) text.Append(' ').Append(Extremes(model, points, culture));
                return;
            }
            text.Append(' ').Append(NthUiStrings.Spanning(NthUiStrings.Points(points.Count, model.Kind == NthUiChartKind.Bar), NthUiStrings.FromTo(model.Label(points[0].Index), model.Label(points[^1].Index))));
            text.Append(' ').Append(Extremes(model, points, culture));
            text.Append(' ').Append(NthUiStrings.Trend(V(points[0].Value), V(points[^1].Value), Math.Sign(points[^1].Value - points[0].Value)));
            return;
        }
        text.Append(' ').Append(NthUiStrings.Listed(NthUiStrings.SeriesCount(model.Series.Count), string.Join(", ", Enumerable.Range(0, model.Series.Count).Select(s => SeriesName(model, s)))));
        for (var s = 0; s < model.Series.Count; s++)
        {
            var points = Present(model, s);
            if (points.Count == 0) continue;
            var high = points.MaxBy(p => p.Value);
            text.Append(' ').Append(NthUiStrings.SeriesLine(SeriesName(model, s), V(points[0].Value), model.Label(points[0].Index), V(points[^1].Value), model.Label(points[^1].Index), V(high.Value), model.Label(high.Index)));
        }
    }

    private static void Scatter(NthUiChartModel model, CultureInfo culture, StringBuilder text)
    {
        var unit = model.Data.Unit;
        var count = 0;
        double? xlo = null, xhi = null, ylo = null, yhi = null;
        for (var s = 0; s < model.Series.Count; s++)
        {
            for (var i = 0; i < model.Series[s].Values.Count; i++)
            {
                if (model.Value(s, i) is not double y) continue;
                var x = model.X(s, i);
                count++;
                xlo = xlo is null ? x : Math.Min(xlo.Value, x);
                xhi = xhi is null ? x : Math.Max(xhi.Value, x);
                ylo = ylo is null ? y : Math.Min(ylo.Value, y);
                yhi = yhi is null ? y : Math.Max(yhi.Value, y);
            }
        }
        text.Append(' ').Append(NthUiStrings.ScatterCount(count)).Append(' ')
            .Append(NthUiStrings.ScatterRanges(NthUiValueText(xlo ?? 0, culture), NthUiValueText(xhi ?? 0, culture), NthUiNumbers.Format(ylo, culture, unit), NthUiNumbers.Format(yhi, culture, unit)));
        if (model.Series.Count > 1)
            text.Append(' ').Append(NthUiStrings.Listed(NthUiStrings.SeriesCount(model.Series.Count), string.Join(", ", Enumerable.Range(0, model.Series.Count).Select(s => SeriesName(model, s)))));
    }

    private static void Pie(NthUiChartModel model, CultureInfo culture, StringBuilder text)
    {
        var shares = model.Shares;
        var percent = NthUiChartMath.Percentages(shares);
        var unit = model.Data.Unit;
        var parts = new List<string>();
        for (var i = 0; i < shares.Count; i++)
        {
            if (model.Value(0, i) is not double value || shares[i] <= 0) continue;
            parts.Add(NthUiStrings.Slice(model.Label(i), percent[i].ToString(culture), NthUiNumbers.Format(value, culture, unit)));
        }
        text.Append(' ').Append(NthUiStrings.Sentence(string.Join(", ", parts)));
        var total = Enumerable.Range(0, shares.Count).Sum(i => model.Value(0, i) is double v && v > 0 ? v : 0);
        text.Append(' ').Append(NthUiStrings.Sentence(NthUiStrings.Total(NthUiNumbers.Format(total, culture, unit))));
    }

    private static List<(int Index, double Value)> Present(NthUiChartModel model, int series)
    {
        var points = new List<(int, double)>();
        for (var i = 0; i < model.Series[series].Values.Count; i++)
        {
            if (model.Value(series, i) is double v) points.Add((i, v));
        }
        return points;
    }

    private static string Extremes(NthUiChartModel model, List<(int Index, double Value)> points, CultureInfo culture)
    {
        var unit = model.Data.Unit;
        var low = points.MinBy(p => p.Value);
        var high = points.MaxBy(p => p.Value);
        return NthUiStrings.LowHigh(NthUiNumbers.Format(low.Value, culture, unit), model.Label(low.Index), NthUiNumbers.Format(high.Value, culture, unit), model.Label(high.Index));
    }
}
