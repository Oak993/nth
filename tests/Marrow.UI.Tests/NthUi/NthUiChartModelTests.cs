using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi.Charts;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.2 (web surfaces W16b; ED-NEW-web-w16b-1): a chart's data is turned into the drawing it gets: the canvas's bars
// for a small single-series bar chart, a ring for a pie, a plot for the rest; a value that is not a number is a gap; stacked
// series add up; an editable chart is a bound line, bar or area of one series; the axes take round ranges, rest on zero for
// bars and areas, and keep an edge the author fixed.
public sealed class NthUiChartModelTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_chart_type_and_the_data_pick_the_drawing()
    {
        var hosting = NthUiChartDocs.Model("chart bar x=\"Compute,Egress,Storage\" y=[101, 24, 9]\n");
        Assert.Equal((NthUiChartKind.Bar, NthUiChartLayout.BarList, 3), (hosting.Kind, hosting.Layout, hosting.Count));
        Assert.Equal(NthUiChartLayout.Plot, NthUiChartDocs.Model("chart bar y=[1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]\n").Layout);
        Assert.Equal(NthUiChartLayout.BarList, NthUiChartDocs.Model("chart bar y=[1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]\n").Layout);
        Assert.Equal(NthUiChartLayout.Plot, NthUiChartDocs.Model("chart bar series=\"A,B\" y=[[1, 2], [3, 4]]\n").Layout);
        Assert.Equal(NthUiChartLayout.Pie, NthUiChartDocs.Model(NthUiSamples.Pie).Layout);
        Assert.Equal(NthUiChartLayout.Plot, NthUiChartDocs.Model(NthUiSamples.Scatter).Layout);
        Assert.Equal(NthUiChartLayout.Plot, NthUiChartDocs.Model(NthUiSamples.Area).Layout);
        Assert.Equal(NthUiChartKind.Bar, NthUiChartDocs.Model("chart y=[1, 2]\n").Kind);
        var empty = NthUiChartDocs.Model("chart scatter title=\"Waiting\"\n");
        Assert.True(empty.IsEmpty);
        Assert.Equal(NthUiChartLayout.Plot, empty.Layout);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void An_editable_chart_is_a_bound_line_bar_or_area_of_one_series_and_never_the_bar_list()
    {
        var editable = NthUiChartDocs.Model(NthUiSamples.EditableChart);
        Assert.True(editable.Editable);
        Assert.Equal((0.0, 60.0, 20.0), (editable.YLo, editable.YHi, editable.YStep));
        Assert.Equal([0.0, 20, 40, 60], editable.YTicks);
        var bars = NthUiChartDocs.Model("card \"A\"\n  state v = [3, 5, 4]\n  chart bar x=\"a,b,c\" editable=points bind=v\n");
        Assert.True(bars.Editable);
        Assert.Equal(NthUiChartLayout.Plot, bars.Layout);
        Assert.False(NthUiChartDocs.Model("chart line y=[1, 2, 3]\n").Editable);
        Assert.False(NthUiChartDocs.Model("card \"A\"\n  state v = [3, 5, 4]\n  chart scatter editable=points bind=v\n").Editable);
        Assert.False(NthUiChartDocs.Model("card \"A\"\n  state v = [3, 5, 4]\n  chart pie editable=points bind=v\n").Editable);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_value_that_is_not_a_number_is_a_gap_and_a_chart_of_gaps_is_empty()
    {
        var gappy = NthUiChartDocs.Model("chart line y=[1, \"x\", 3]\n");
        Assert.Equal(3, gappy.Count);
        Assert.Equal([1.0, null, 3.0], gappy.Values[0]);
        Assert.Null(gappy.Value(0, 1));
        Assert.Null(gappy.Value(0, 9));
        Assert.Null(gappy.Value(4, 0));
        Assert.False(gappy.IsEmpty);
        Assert.True(NthUiChartDocs.Model("chart line y=[\"a\", \"b\"]\n").IsEmpty);
        Assert.Equal("3", gappy.Label(2));
        Assert.Equal("8", gappy.Label(7));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Stacked_series_add_up_and_the_axis_holds_the_total()
    {
        var area = NthUiChartDocs.Model(NthUiSamples.Area);
        Assert.True(area.Stacked);
        Assert.Equal((120.0, 200.0), area.Span(1, 0));
        Assert.Equal((0.0, 120.0), area.Span(0, 0));
        Assert.Equal((210.0, 380.0), area.Span(1, 3));
        Assert.Equal((0.0, 400.0, 100.0), (area.YLo, area.YHi, area.YStep));
        var apart = NthUiChartDocs.Model("chart area series=\"A,B\" y=[[1, 2], [3, 4]]\n");
        Assert.False(apart.Stacked);
        Assert.Equal((0.0, 4.0), apart.Span(1, 1));
        var negative = NthUiChartDocs.Model("chart bar series=\"A,B\" y=[[5, 5], [-2, 3]] stacked\n");
        Assert.Equal((-2.0, 0.0), negative.Span(1, 0));
        Assert.Equal((5.0, 8.0), negative.Span(1, 1));
        Assert.True(negative.YLo <= -2 && negative.YHi >= 8);
        Assert.False(NthUiChartDocs.Model("chart line series=\"A,B\" y=[[1, 2], [3, 4]] stacked\n").Stacked);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_axes_rest_on_zero_for_bars_and_areas_and_follow_the_data_for_lines_and_scatter()
    {
        Assert.Equal((100.0, 105.0, 1.0), Range(NthUiChartDocs.Model("chart line y=[100, 102, 105]\n")));
        Assert.Equal((0.0, 40.0, 10.0), Range(NthUiChartDocs.Model("chart line y=[12, 25, 35]\n")));
        Assert.Equal((0.0, 40.0, 10.0), Range(NthUiChartDocs.Model("chart bar y=[12, 25, 35, 20, 30, 33, 34, 35, 36, 37, 38, 39, 40]\n")));
        Assert.Equal((0.0, 125.0, 25.0), Range(NthUiChartDocs.Model("chart bar y=[100, 102, 105, 90, 80, 70, 60, 50, 40, 30, 20, 10, 1]\n")));
        Assert.Equal((-40.0, 0.0, 10.0), Range(NthUiChartDocs.Model("chart bar y=[-35, -20, -5, -1, -2, -3, -4, -5, -6, -7, -8, -9, -10]\n")));
        Assert.Equal((3.8, 4.8, 0.2), Range(NthUiChartDocs.Model(NthUiSamples.Scatter)));
        var scatter = NthUiChartDocs.Model(NthUiSamples.Scatter);
        Assert.Equal((0.0, 1000.0, 200.0), (scatter.XLo, scatter.XHi, scatter.XStep));
        Assert.Equal([0.0, 200, 400, 600, 800, 1000], scatter.XTicks);
        Assert.Equal((0.0, 10.0, 2.0), Range(NthUiChartDocs.Model("chart line y=[2, 4, 6] min=0 max=10\n")));
        Assert.Equal((0.0, 1.0, 0.2), Range(NthUiChartDocs.Model("chart scatter title=\"Waiting\"\n")));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_scatter_plots_y_against_the_numbers_in_x_whether_they_come_from_series_lines_or_the_chart_itself()
    {
        var series = NthUiChartDocs.Model(NthUiSamples.Scatter);
        Assert.Equal(349, series.X(0, 1));
        Assert.Equal(649, series.X(1, 2));
        var inline = NthUiChartDocs.Model("chart scatter x=[10, 20, 40] y=[1, 2, 3]\n");
        Assert.Equal([10.0, 20, 40], [inline.X(0, 0), inline.X(0, 1), inline.X(0, 2)]);
        Assert.Equal((0.0, 50.0, 10.0), (inline.XLo, inline.XHi, inline.XStep));
        var plain = NthUiChartDocs.Model("chart scatter y=[1, 2, 3]\n");
        Assert.Equal([1.0, 2, 3], [plain.X(0, 0), plain.X(0, 1), plain.X(0, 2)]);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_pie_has_the_first_series_as_its_slices()
    {
        var pie = NthUiChartDocs.Model(NthUiSamples.Pie);
        Assert.Equal([0.5, 0.3, 0.2], pie.Shares.Select(s => Math.Round(s, 9)));
        Assert.Equal(["Needs", "Wants", "Savings"], pie.Labels);
        Assert.Equal(2100, pie.Value(0, 0)!.Value);
        Assert.Equal((840.0, 2100.0), pie.Extent()!.Value);
    }

    private static (double Lo, double Hi, double Step) Range(NthUiChartModel model) => (model.YLo, model.YHi, model.YStep);
}
