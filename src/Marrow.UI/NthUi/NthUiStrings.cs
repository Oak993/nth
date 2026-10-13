// PORT-ONLY: web surfaces W16a (DESIGN.md 7, 10 names and values for every control; 14 W22 moves every new string into the
// resource files; ED-NEW-web-w16a-1). No Swift counterpart. Every word a live view shows or speaks of its own, in one place.
// W22: the words are keys of Marrow.Core/Localisation/Strings/nthui-en.json, read when they are shown (never at type initialisation).
using System.Globalization;
using Marrow.Core.Localisation;

namespace Marrow.UI.NthUi;

/// <summary>The live view's own words (the model's words come from the view's text).</summary>
public static class NthUiStrings
{
    public static string LiveAnswer => UiText.Get("nthui.live_answer");
    public static string Copy => UiText.Get("nthui.action.copy");
    public static string Copied => UiText.Get("nthui.action.copied");
    public static string CopyCode => UiText.Get("nthui.action.copy_code");
    public static string Open => UiText.Get("nthui.action.open");
    public static string ExportCsv => UiText.Get("nthui.action.export_csv");
    public static string ExportPng => UiText.Get("nthui.action.export_png");
    public static string Send => UiText.Get("nthui.action.send");
    public static string Attach => UiText.Get("nthui.action.attach");
    public static string PreviousStep => UiText.Get("nthui.steps.previous");
    public static string NextStep => UiText.Get("nthui.steps.next");
    public static string ChooseDate => UiText.Get("nthui.date.choose");
    public static string NoDate => UiText.Get("nthui.date.none");
    public static string PreviousMonth => UiText.Get("nthui.date.previous_month");
    public static string NextMonth => UiText.Get("nthui.date.next_month");
    public static string Image => UiText.Get("nthui.block.image");
    public static string Chart => UiText.Get("nthui.block.chart");
    public static string Diagram => UiText.Get("nthui.block.diagram");
    public static string Map => UiText.Get("nthui.block.map");
    public static string LineArt => UiText.Get("nthui.block.line_art");
    public static string Code => UiText.Get("nthui.block.code");
    public static string FollowUps => UiText.Get("nthui.follow_ups");
    public static string On => UiText.Get("nthui.toggle.on");
    public static string Off => UiText.Get("nthui.toggle.off");
    public static string ShowAsTable => UiText.Get("nthui.chart.show_as_table");
    public static string ShowAsChart => UiText.Get("nthui.chart.show_as_chart");
    public static string NoDataYet => UiText.Get("nthui.chart.no_data_yet");
    public static string ColumnSlice => UiText.Get("nthui.chart.column_slice");
    public static string ColumnValue => UiText.Get("nthui.chart.column_value");
    public static string ColumnShare => UiText.Get("nthui.chart.column_share");
    public static string ColumnSeries => UiText.Get("nthui.chart.column_series");
    public static string ColumnItem => UiText.Get("nthui.chart.column_item");
    public static string ColumnPoint => UiText.Get("nthui.chart.column_point");
    public static string TotalCaption => UiText.Get("nthui.chart.total_caption");
    public static string PointHint => UiText.Get("nthui.chart.point_hint");

    // ---- charts (W16b) ---------------------------------------------------------------------------------------------

    public static string KindName(Charts.NthUiChartKind kind) => kind switch
    {
        Charts.NthUiChartKind.Line => UiText.Get("nthui.chart.kind_line"),
        Charts.NthUiChartKind.Area => UiText.Get("nthui.chart.kind_area"),
        Charts.NthUiChartKind.Pie => UiText.Get("nthui.chart.kind_pie"),
        Charts.NthUiChartKind.Scatter => UiText.Get("nthui.chart.kind_scatter"),
        _ => UiText.Get("nthui.chart.kind_bar"),
    };

    public static string SeriesNumber(int number) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.series_number", number);

    public static string SeriesCount(int count) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.series_count", count);

    public static string Points(int count, bool bars) =>
        bars ? UiText.PluralWith(CultureInfo.InvariantCulture, "nthui.chart.bars", count) : UiText.PluralWith(CultureInfo.InvariantCulture, "nthui.chart.points", count);

    public static string FromTo(string first, string last) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.from_to", first, last);

    public static string LowHigh(string low, string lowAt, string high, string highAt) =>
        UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.low_high", low, lowAt, high, highAt);

    public static string Trend(string first, string last, int direction) => direction switch
    {
        > 0 => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.trend_up", first, last),
        < 0 => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.trend_down", first, last),
        _ => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.trend_level", first),
    };

    public static string SeriesLine(string name, string first, string firstAt, string last, string lastAt, string high, string highAt) =>
        UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.series_line", name, first, firstAt, last, lastAt, high, highAt);

    public static string ScatterCount(int count) => UiText.PluralWith(CultureInfo.InvariantCulture, "nthui.chart.scatter_count", count);

    public static string ScatterRanges(string xLow, string xHigh, string yLow, string yHigh) =>
        UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.scatter_ranges", xLow, xHigh, yLow, yHigh);

    public static string Slice(string name, string percent, string value) =>
        UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.slice", name, percent, value);

    public static string Total(string value) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.total", value);

    public static string PointName(string label, string value) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.point_name", label, value);

    /// <summary>The chart's first sentence: its kind, and its title when it has one ("Bar chart, Sales.").</summary>
    public static string ChartHeading(string kind, string? title) =>
        title is { Length: > 0 }
            ? UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.heading_titled", kind, title)
            : UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.heading", kind);

    /// <summary>The chart's "No data yet." sentence.</summary>
    public static string NoDataSentence => Sentence(NoDataYet);

    /// <summary>Some words ended as a sentence ("Total 100" becomes "Total 100.").</summary>
    public static string Sentence(string text) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.sentence", text);

    /// <summary>A lead and its list as a sentence ("3 points: Jan 4, Feb 5, Mar 6.", "2 series: A, B.").</summary>
    public static string Listed(string lead, string list) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.listed", lead, list);

    /// <summary>A lead and the span it covers as a sentence ("40 points from Jan to Dec.").</summary>
    public static string Spanning(string lead, string span) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.chart.spanning", lead, span);

    // ---- controls ----------------------------------------------------------------------------------------------------

    /// <summary>The stepper's minus button when no field name is known.</summary>
    public static string FewerWord => UiText.Get("nthui.stepper.fewer");

    /// <summary>The stepper's plus button when no field name is known.</summary>
    public static string MoreWord => UiText.Get("nthui.stepper.more");

    public static string Fewer(string label) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.stepper.fewer_named", Lower(label));

    public static string More(string label) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.stepper.more_named", Lower(label));

    public static string StepOf(int step, int count) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.steps.of", step, count);

    public static string StepName(int step, int count, string title) =>
        UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.steps.name", step, count, title);

    public static string ImageFrom(string host) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.block.image_from", host);

    public static string Run(string tool) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.action.run", tool);

    public static string Set(string name) => UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.action.set", name);

    /// <summary>A stat's spoken name: "Revenue: 42 USD" (the unit only when it has one).</summary>
    public static string StatName(string label, string value, string? unit) =>
        unit is null
            ? UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.stat.name", label, value)
            : UiText.FormatWith(CultureInfo.InvariantCulture, "nthui.stat.name_unit", label, value, unit);

    // The label is the model's own word for the field ("Quantity"), not one of nth's words, so its first letter is lowered in place.
    private static string Lower(string label) => label.Length == 0 ? label : char.ToLower(label[0], CultureInfo.InvariantCulture) + label[1..];
}
