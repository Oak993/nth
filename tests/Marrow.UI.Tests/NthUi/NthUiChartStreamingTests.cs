using Avalonia.Input;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.4 "Each completed line adds or updates one node ... Charts animate their marks in once" (web surfaces W16b;
// ED-NEW-web-w16b-1): a chart whose series arrive line by line starts as an empty plot and fills in without redrawing what is
// already there; a bar chart that gains a second series becomes a plot; a series that reads a state follows it.
[Collection("Headless")]
public sealed class NthUiChartStreamingTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_scatter_streams_in_series_by_series_and_ends_as_the_whole_show_draws_it() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        var counts = new List<int>();
        rig.Stream(NthUiSamples.Scatter, 7, () =>
        {
            if (rig.View.Document.Nodes.Any(n => n.Kind == "chart")) counts.Add(rig.Chart().Model.Series.Count);
        });
        rig.View.Complete();
        rig.Settle();
        Assert.Equal(counts.Order(), counts);
        Assert.Contains(0, counts);
        Assert.Contains(1, counts);
        Assert.Equal(2, counts[^1]);
        var chart = rig.Chart();
        Assert.Equal(2, chart.Model.Series.Count);
        Assert.Equal(["Phones", "Tablets"], chart.Key.Names);
        Assert.Equal(1, chart.Plot!.Grow(1, 0), 6);
        // Each rig holds the clock while it lives, so what is read from this one is read before the other is made.
        var texts = rig.Texts();
        var summary = chart.Summary;
        var area = chart.Plot.PlotArea.Size;
        var point = chart.Plot.PointOf(1, 2);
        using var whole = new NthUiRig(NthUiView.Show(NthUiSamples.Scatter, culture: NthUiRig.English));
        Assert.Equal(whole.Texts(), texts);
        Assert.Equal(whole.Chart().Summary, summary);
        Assert.Equal(whole.Chart().Plot!.PlotArea.Size, area);
        Assert.Equal(whole.Chart().Plot!.PointOf(1, 2), point);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_series_arriving_later_draws_in_while_the_ones_already_there_stay_put() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("chart line x=\"a,b,c\" y=[1, 4, 2]\n");
        rig.Settle();
        var plot = rig.Chart().Plot!;
        Assert.Equal(1, plot.Grow(0, 2), 6);
        rig.View.Append("  series \"B\" y=[3, 1, 5]\n");
        rig.Pump();
        Assert.Same(plot, rig.Chart().Plot);
        Assert.Equal(2, plot.Model.Series.Count);
        Assert.Equal(1, plot.Grow(0, 2), 6);
        Assert.Equal(0, plot.Grow(1, 2), 3);
        Assert.Equal(4, plot.ShownValue(0, 1)!.Value, 3);
        rig.Tick();
        rig.Tick();
        Assert.InRange(plot.Grow(1, 0), .01, 1);
        rig.Settle();
        Assert.Equal(1, plot.Grow(1, 2), 6);
        Assert.Equal([1.0, 4, 2], [plot.ShownValue(0, 0)!.Value, plot.ShownValue(0, 1)!.Value, plot.ShownValue(0, 2)!.Value]);
        Assert.Equal([3.0, 1, 5], [plot.ShownValue(1, 0)!.Value, plot.ShownValue(1, 1)!.Value, plot.ShownValue(1, 2)!.Value]);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_bar_chart_that_gains_a_second_series_becomes_a_plot_and_keeps_its_button() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("chart bar x=\"a,b,c\" y=[1, 2, 3]\n");
        rig.Settle();
        var chart = rig.Chart();
        var button = chart.TableButton;
        Assert.NotNull(chart.Bars);
        Assert.Null(chart.Plot);
        rig.View.Append("  series \"B\" y=[3, 2, 1]\n");
        rig.Settle();
        Assert.Null(chart.Bars);
        Assert.NotNull(chart.Plot);
        Assert.Equal(NthUiChartLayout.Plot, chart.Model.Layout);
        Assert.Same(button, chart.TableButton);
        Assert.True(chart.Key.IsVisible);
        Assert.Equal(["Series 1", "B"], chart.Key.Names);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_series_that_reads_a_state_follows_it_and_the_chart_asks_for_no_more_frames_once_it_rests() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show("card \"S\"\n  state a = 2\n  slider a label=\"A\" min=0 max=10 step=1\n  chart line x=\"p,q\" y=[1, 2]\n    series \"Mine\" y=[a, a + 1]\n", culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        Assert.Equal([2.0, 3], [plot.ShownValue(1, 0)!.Value, plot.ShownValue(1, 1)!.Value]);
        rig.Key(rig.ViewOf<NthUiSliderView>().Slider, Key.End);
        rig.Settle();
        Assert.Equal([10.0, 11], [plot.ShownValue(1, 0)!.Value, plot.ShownValue(1, 1)!.Value]);
        Assert.Equal([1.0, 2], [plot.ShownValue(0, 0)!.Value, plot.ShownValue(0, 1)!.Value]);
        Assert.Equal((0.0, 12.5), (plot.ShownRange.Lo, plot.ShownRange.Hi));
        Assert.Equal(0, rig.Frames.Pending);
    });
}
