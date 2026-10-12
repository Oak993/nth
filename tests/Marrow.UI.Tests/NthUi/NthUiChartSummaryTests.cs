using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi.Charts;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 10 "charts carry a text summary and an 'Show as table' alternative" (web surfaces W16b; ED-NEW-web-w16b-1): the
// sentence a screen reader gets for each chart type, the readout a hovered or focused point shows, and the table of a chart's
// numbers, all worked out from the data alone.
public sealed class NthUiChartSummaryTests
{
    private static string Say(string text) => NthUiChartSummary.Describe(NthUiChartDocs.Model(text), NthUiChartDocs.English);

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_short_series_is_named_value_by_value_with_its_extremes()
    {
        Assert.Equal("Line chart. 6 points: Jan 12 units, Feb 18 units, Mar 25 units, Apr 30 units, May 28 units, Jun 35 units. Lowest 12 units at Jan, highest 35 units at Jun.",
            Say(NthUiSamples.EditableChart));
        Assert.Equal("Bar chart. 3 bars: Compute 101, Egress 24, Storage 9. Lowest 9 at Storage, highest 101 at Compute.",
            Say("chart bar x=\"Compute,Egress,Storage\" y=[101, 24, 9]\n"));
        Assert.Equal("Bar chart, Visits. 2 bars: Mon 4, Tue 7.", Say("chart bar title=\"Visits\" x=\"Mon,Tue\" y=[4, 7]\n"));
        Assert.Equal("Line chart. 1 point: 1 5.", Say("chart line y=[5]\n"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_long_series_is_described_by_its_ends_extremes_and_direction()
    {
        var up = Say("chart line title=\"Balance\" y=[3, 4, 5, 6, 7, 8, 9, 10, 11, 12]\n");
        Assert.Equal("Line chart, Balance. 10 points from 1 to 10. Lowest 3 at 1, highest 12 at 10. Up from 3 to 12.", up);
        Assert.Equal("Line chart. 9 points from 1 to 9. Lowest 1 at 9, highest 9 at 1. Down from 9 to 1.", Say("chart line y=[9, 8, 7, 6, 5, 4, 3, 2, 1]\n"));
        Assert.EndsWith("Level at 4.", Say("chart line y=[4, 4, 4, 4, 4, 4, 4, 4, 4]\n"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Several_series_get_a_line_each_and_a_scatter_gives_its_ranges_and_a_pie_its_shares()
    {
        Assert.Equal("Area chart. 2 series: Web, Mobile. Web: 120 at Q1 to 210 at Q4, highest 210 at Q4. Mobile: 80 at Q1 to 170 at Q4, highest 170 at Q4.", Say(NthUiSamples.Area));
        Assert.Equal("Scatter chart, Price and rating. 8 points. x runs from 199 to 999; y runs from 3.9 to 4.7. 2 series: Phones, Tablets.", Say(NthUiSamples.Scatter));
        Assert.Equal("Pie chart. Needs 50% (2100), Wants 30% (1260), Savings 20% (840). Total 4200.", Say(NthUiSamples.Pie));
        Assert.Equal("Pie chart. A 100% (7). Total 7.", Say("chart pie x=\"A,B\" y=[7, 0]\n"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_chart_with_no_data_says_so_and_a_unit_follows_every_number()
    {
        Assert.Equal("Bar chart. No data yet.", Say("chart bar\n"));
        Assert.Equal("Scatter chart, Waiting. No data yet.", Say("chart scatter title=\"Waiting\"\n"));
        Assert.Contains("12 kg", Say("chart line y=[12, 14, 16] unit=\"kg\"\n"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_readout_names_the_category_and_each_series_value_there()
    {
        var single = NthUiChartSummary.Readout(NthUiChartDocs.Model(NthUiSamples.EditableChart), 2, NthUiChartDocs.English);
        Assert.Equal("Mar", single.Heading);
        Assert.Equal([new NthUiReadoutRow(0, "", "25 units")], single.Rows);
        Assert.Equal("Mar: 25 units", single.Spoken);
        var many = NthUiChartSummary.Readout(NthUiChartDocs.Model(NthUiSamples.Area), 1, NthUiChartDocs.English);
        Assert.Equal("Q2", many.Heading);
        Assert.Equal([new NthUiReadoutRow(0, "Web", "140"), new NthUiReadoutRow(1, "Mobile", "95")], many.Rows);
        Assert.Equal("Q2: Web 140, Mobile 95", many.Spoken);
        var pie = NthUiChartSummary.Readout(NthUiChartDocs.Model(NthUiSamples.Pie), 1, NthUiChartDocs.English);
        Assert.Equal(("Wants", "1260 · 30%"), (pie.Heading, pie.Rows[0].Value));
        var scatter = NthUiChartSummary.ScatterReadout(NthUiChartDocs.Model(NthUiSamples.Scatter), 0, 1, NthUiChartDocs.English);
        Assert.Equal(("Phones · 349", "4.2"), (scatter.Heading, scatter.Rows[0].Value));
        Assert.Equal("", NthUiChartSummary.Readout(NthUiChartDocs.Model(NthUiSamples.Pie), 9, NthUiChartDocs.English).Heading);
        var gap = NthUiChartSummary.Readout(NthUiChartDocs.Model("chart line series=\"A,B\" y=[[1, \"x\"], [3, 4]]\n"), 1, NthUiChartDocs.English);
        Assert.Equal(["B"], gap.Rows.Select(r => r.Name));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_table_of_a_chart_has_a_row_a_point_and_a_column_a_series()
    {
        var line = NthUiChartRows.Build(NthUiChartDocs.Model(NthUiSamples.EditableChart), NthUiChartDocs.English);
        Assert.Equal(["Point", "Value"], line.Columns);
        Assert.Equal(["Jan", "12 units"], line.Rows[0]);
        Assert.Equal(6, line.Rows.Count);
        var area = NthUiChartRows.Build(NthUiChartDocs.Model(NthUiSamples.Area), NthUiChartDocs.English);
        Assert.Equal(["Point", "Web", "Mobile"], area.Columns);
        Assert.Equal(["Q4", "210", "170"], area.Rows[3]);
        var bars = NthUiChartRows.Build(NthUiChartDocs.Model("chart bar x=\"a,b\" y=[4, 7]\n"), NthUiChartDocs.English);
        Assert.Equal(["Item", "Value"], bars.Columns);
        var pie = NthUiChartRows.Build(NthUiChartDocs.Model(NthUiSamples.Pie), NthUiChartDocs.English);
        Assert.Equal(["Slice", "Value", "Share"], pie.Columns);
        Assert.Equal(["Savings", "840", "20%"], pie.Rows[2]);
        var scatter = NthUiChartRows.Build(NthUiChartDocs.Model(NthUiSamples.Scatter), NthUiChartDocs.English);
        Assert.Equal(["Series", "x", "y"], scatter.Columns);
        Assert.Equal(8, scatter.Rows.Count);
        Assert.Equal(["Tablets", "649", "4.7"], scatter.Rows[7]);
        var lone = NthUiChartRows.Build(NthUiChartDocs.Model("chart scatter x=[1, 2] y=[5, 6]\n"), NthUiChartDocs.English);
        Assert.Equal(["x", "y"], lone.Columns);
        Assert.Equal("x\ty\n1\t5\n2\t6", lone.Text);
        var gaps = NthUiChartRows.Build(NthUiChartDocs.Model("chart line y=[1, \"x\"]\n"), NthUiChartDocs.English);
        Assert.Equal(["2", "—"], gaps.Rows[1]);
    }
}
