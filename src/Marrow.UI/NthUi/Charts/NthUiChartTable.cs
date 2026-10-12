// PORT-ONLY: web surfaces W16b (DESIGN.md 10 "charts carry a text summary and an 'Show as table' alternative"; canvas .utab rules;
// ED-NEW-web-w16b-1). No Swift counterpart. A chart's numbers as a table: what the "Show as table" button shows in place of
// the drawing, and what a screen reader can read row by row. The rows are worked out from the model (so they are tested without
// a window) and drawn with the same rules as a table component: tabular numerals, hairline rules, the first column on the
// start edge and the numbers on the end edge.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.NthUi;

namespace Marrow.UI.NthUi.Charts;

/// <summary>The cells of a chart's table.</summary>
public sealed record NthUiChartTableData(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>The table as text a reader can copy: tab between cells, a line a row.</summary>
    public string Text => string.Join('\n', new[] { string.Join('\t', Columns) }.Concat(Rows.Select(r => string.Join('\t', r))));
}

/// <summary>Works out the table of a chart.</summary>
public static class NthUiChartRows
{
    public static NthUiChartTableData Build(NthUiChartModel model, CultureInfo culture)
    {
        var unit = model.Data.Unit;
        string V(double? value) => NthUiNumbers.Format(value, culture, unit);
        var rows = new List<IReadOnlyList<string>>();
        if (model.Kind == NthUiChartKind.Pie)
        {
            var shares = model.Shares;
            var percent = NthUiChartMath.Percentages(shares);
            for (var i = 0; i < model.Count; i++) rows.Add([model.Label(i), V(model.Value(0, i)), percent[i].ToString(culture) + "%"]);
            return new NthUiChartTableData([NthUiStrings.ColumnSlice, NthUiStrings.ColumnValue, NthUiStrings.ColumnShare], rows);
        }
        if (model.Kind == NthUiChartKind.Scatter)
        {
            var many = model.Series.Count > 1;
            for (var s = 0; s < model.Series.Count; s++)
            {
                for (var i = 0; i < model.Series[s].Values.Count; i++)
                {
                    if (model.Value(s, i) is null) continue;
                    var cells = new List<string>();
                    if (many) cells.Add(NthUiChartSummary.SeriesName(model, s));
                    cells.Add(NthUiValue.FormatNumber(model.X(s, i), culture));
                    cells.Add(V(model.Value(s, i)));
                    rows.Add(cells);
                }
            }
            return new NthUiChartTableData(many ? [NthUiStrings.ColumnSeries, "x", "y"] : ["x", "y"], rows);
        }
        var columns = new List<string> { model.Kind == NthUiChartKind.Bar ? NthUiStrings.ColumnItem : NthUiStrings.ColumnPoint };
        if (model.Series.Count == 0) columns.Add(NthUiStrings.ColumnValue);
        for (var s = 0; s < model.Series.Count; s++)
        {
            var name = model.Series[s].Name;
            columns.Add(name.Length > 0 ? name : model.Series.Count > 1 ? NthUiStrings.SeriesNumber(s + 1) : NthUiStrings.ColumnValue);
        }
        for (var i = 0; i < model.Count; i++)
        {
            var cells = new List<string> { model.Label(i) };
            for (var s = 0; s < model.Series.Count; s++) cells.Add(V(model.Value(s, i)));
            rows.Add(cells);
        }
        return new NthUiChartTableData(columns, rows);
    }
}

/// <summary>The drawn table: a header and rows in fixed columns.</summary>
public sealed class NthUiChartTable
{
    private readonly NthUiColumn column = new() { Gap = 0 };
    private readonly List<List<TextBlock>> cells = [];
    private readonly List<Control> rowControls = [];
    private NthUiChartTableData? shown;
    private Control? header;

    public NthUiChartTable()
    {
        AutomationProperties.SetControlTypeOverride(column, AutomationControlType.Table);
    }

    public Control Control => column;

    /// <summary>The table on show.</summary>
    public NthUiChartTableData? Data => shown;

    /// <summary>The texts of the header and the rows, as drawn.</summary>
    public IEnumerable<string> Texts => column.GetVisualDescendantsText();

    /// <summary>Shows <paramref name="data"/>: the texts of a table of the same shape change in place, any other shape is built again.</summary>
    public void Show(NthUiChartTableData data)
    {
        if (shown is not null && shown.Columns.Count == data.Columns.Count && shown.Rows.Count == data.Rows.Count)
        {
            Fill(data);
            shown = data;
            return;
        }
        shown = data;
        column.Children.Clear();
        cells.Clear();
        rowControls.Clear();
        var weights = Enumerable.Range(0, data.Columns.Count).Select(i => i == 0 && data.Columns.Count > 1 ? 1.5 : 1.0).ToArray();
        var head = new NthUiCells { Weights = weights };
        for (var i = 0; i < data.Columns.Count; i++)
        {
            var text = NthUiText.Block(data.Columns[i], NthUiTokens.Caption, NthUiTokens.Text2);
            text.TextAlignment = Align(i, data.Columns.Count);
            AutomationProperties.SetIsColumnHeader(text, true);
            head.Children.Add(text);
        }
        header = new Panel { Children = { head, new NthUiRule { VerticalAlignment = VerticalAlignment.Bottom } } };
        AutomationProperties.SetControlTypeOverride(header, AutomationControlType.Header);
        column.Children.Add(header);
        foreach (var row in data.Rows)
        {
            var line = new NthUiCells { Weights = weights, Padding = new Thickness(4, 7, 4, 7) };
            var texts = new List<TextBlock>();
            for (var i = 0; i < data.Columns.Count; i++)
            {
                var text = NthUiText.Block(i < row.Count ? row[i] : "", NthUiTokens.Numbers, NthUiTokens.Text1);
                text.TextAlignment = Align(i, data.Columns.Count);
                texts.Add(text);
                line.Children.Add(text);
            }
            var panel = new Panel { Children = { line, new NthUiRule { VerticalAlignment = VerticalAlignment.Bottom } } };
            AutomationProperties.SetControlTypeOverride(panel, AutomationControlType.DataItem);
            cells.Add(texts);
            rowControls.Add(panel);
            column.Children.Add(panel);
        }
        Name(data);
    }

    private void Fill(NthUiChartTableData data)
    {
        for (var r = 0; r < data.Rows.Count; r++)
        {
            for (var i = 0; i < data.Columns.Count; i++)
            {
                var value = i < data.Rows[r].Count ? data.Rows[r][i] : "";
                if (cells[r][i].Text != value) cells[r][i].Text = value;
            }
        }
        Name(data);
    }

    private void Name(NthUiChartTableData data)
    {
        for (var r = 0; r < data.Rows.Count; r++)
        {
            var name = string.Join(", ", data.Rows[r].Select((c, i) => i == 0 ? c : data.Columns[i] + " " + c));
            if (!string.Equals(AutomationProperties.GetName(rowControls[r]), name, StringComparison.Ordinal)) AutomationProperties.SetName(rowControls[r], name);
        }
    }

    private static TextAlignment Align(int index, int count) => index == 0 || count == 1 ? TextAlignment.Left : TextAlignment.Right;
}

internal static class NthUiChartTableExtensions
{
    /// <summary>The texts under a control, in order.</summary>
    public static IEnumerable<string> GetVisualDescendantsText(this Control root) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<TextBlock>().Select(t => t.Text ?? "");
}
