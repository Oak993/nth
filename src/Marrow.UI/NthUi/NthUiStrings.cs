// PORT-ONLY: web surfaces W16a (DESIGN.md 7, 10 names and values for every control; 14 W22 moves every new string into the
// resource files; ED-NEW-web-w16a-1). No Swift counterpart. Every word a live view shows or speaks of its own, in one place.
using System.Globalization;

namespace Marrow.UI.NthUi;

/// <summary>The live view's own words (the model's words come from the view's text).</summary>
public static class NthUiStrings
{
    public const string LiveAnswer = "Live answer";
    public const string Copy = "Copy";
    public const string Copied = "Copied";
    public const string CopyCode = "Copy code";
    public const string Open = "Open";
    public const string ExportCsv = "Download CSV";
    public const string ExportPng = "Save as image";
    public const string Send = "Send";
    public const string Attach = "Attach";
    public const string PreviousStep = "Previous step";
    public const string NextStep = "Next step";
    public const string ChooseDate = "Choose a date";
    public const string NoDate = "No date";
    public const string PreviousMonth = "Previous month";
    public const string NextMonth = "Next month";
    public const string Image = "Image";
    public const string Chart = "Chart";
    public const string Diagram = "Diagram";
    public const string Map = "Map";
    public const string LineArt = "Line art";
    public const string Code = "Code";
    public const string FollowUps = "Follow-up questions";
    public const string On = "On";
    public const string Off = "Off";
    public const string ShowAsTable = "Show as table";
    public const string ShowAsChart = "Show as chart";
    public const string NoDataYet = "No data yet";
    public const string ColumnSlice = "Slice";
    public const string ColumnValue = "Value";
    public const string ColumnShare = "Share";
    public const string ColumnSeries = "Series";
    public const string ColumnItem = "Item";
    public const string ColumnPoint = "Point";
    public const string TotalCaption = "Total";
    public const string PointHint = "Up and Down change the value. Left and Right move to another point.";

    // ---- charts (W16b) ---------------------------------------------------------------------------------------------

    public static string KindName(Charts.NthUiChartKind kind) => kind switch
    {
        Charts.NthUiChartKind.Line => "Line chart",
        Charts.NthUiChartKind.Area => "Area chart",
        Charts.NthUiChartKind.Pie => "Pie chart",
        Charts.NthUiChartKind.Scatter => "Scatter chart",
        _ => "Bar chart",
    };

    public static string SeriesNumber(int number) => string.Format(CultureInfo.InvariantCulture, "Series {0}", number);

    public static string SeriesCount(int count) => string.Format(CultureInfo.InvariantCulture, "{0} series", count);

    public static string Points(int count, bool bars) =>
        string.Format(CultureInfo.InvariantCulture, count == 1 ? (bars ? "{0} bar" : "{0} point") : (bars ? "{0} bars" : "{0} points"), count);

    public static string FromTo(string first, string last) => string.Format(CultureInfo.InvariantCulture, "from {0} to {1}", first, last);

    public static string LowHigh(string low, string lowAt, string high, string highAt) =>
        string.Format(CultureInfo.InvariantCulture, "Lowest {0} at {1}, highest {2} at {3}.", low, lowAt, high, highAt);

    public static string Trend(string first, string last, int direction) => direction switch
    {
        > 0 => string.Format(CultureInfo.InvariantCulture, "Up from {0} to {1}.", first, last),
        < 0 => string.Format(CultureInfo.InvariantCulture, "Down from {0} to {1}.", first, last),
        _ => string.Format(CultureInfo.InvariantCulture, "Level at {0}.", first),
    };

    public static string SeriesLine(string name, string first, string firstAt, string last, string lastAt, string high, string highAt) =>
        string.Format(CultureInfo.InvariantCulture, "{0}: {1} at {2} to {3} at {4}, highest {5} at {6}.", name, first, firstAt, last, lastAt, high, highAt);

    public static string ScatterCount(int count) =>
        string.Format(CultureInfo.InvariantCulture, count == 1 ? "{0} point." : "{0} points.", count);

    public static string ScatterRanges(string xLow, string xHigh, string yLow, string yHigh) =>
        string.Format(CultureInfo.InvariantCulture, "x runs from {0} to {1}; y runs from {2} to {3}.", xLow, xHigh, yLow, yHigh);

    public static string Slice(string name, string percent, string value) =>
        string.Format(CultureInfo.InvariantCulture, "{0} {1}% ({2})", name, percent, value);

    public static string Total(string value) => string.Format(CultureInfo.InvariantCulture, "Total {0}", value);

    public static string PointName(string label, string value) => string.Format(CultureInfo.InvariantCulture, "{0}: {1}", label, value);

    public static string Fewer(string label) => string.Format(CultureInfo.InvariantCulture, "Fewer {0}", Lower(label));

    public static string More(string label) => string.Format(CultureInfo.InvariantCulture, "More {0}", Lower(label));

    public static string StepOf(int step, int count) => string.Format(CultureInfo.InvariantCulture, "{0} of {1}", step, count);

    public static string StepName(int step, int count, string title) =>
        string.Format(CultureInfo.InvariantCulture, "Step {0} of {1}: {2}", step, count, title);

    public static string ImageFrom(string host) => string.Format(CultureInfo.InvariantCulture, "Image from {0}", host);

    public static string Run(string tool) => string.Format(CultureInfo.InvariantCulture, "Run {0}", tool);

    public static string Set(string name) => string.Format(CultureInfo.InvariantCulture, "Set {0}", name);

    private static string Lower(string label) => label.Length == 0 ? label : char.ToLower(label[0], CultureInfo.InvariantCulture) + label[1..];
}
