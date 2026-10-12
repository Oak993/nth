using Avalonia.Input;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.4 "Charts animate their marks in once", 2.6 and 10 (web surfaces W16b; ED-NEW-web-w16b-1): marks grow from their
// base once, left to right; a state change springs every mark, the axis range and the gridlines (the old lines fade out as
// the new fade in) instead of snapping; every motion is interruptible and starts from where it is; Reduce Motion keeps a
// short fade; a view shown whole appears at rest and asks for no frames.
[Collection("Headless")]
public sealed class NthUiChartMotionTests(HeadlessSession headless)
{
    private const string Slid = "card \"S\"\n  state a = 10\n  slider a label=\"A\" min=0 max=40 step=1\n  chart line x=\"p,q,r\" y=[a, 20, a * 2]\n";

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_bars_of_the_canvas_grow_from_nothing_once_and_the_view_then_rests() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append(NthUiSamples.CanvasHosting);
        rig.View.Complete();
        rig.Pump();
        var bars = rig.Chart().Bars!.Bars;
        Assert.Equal(3, bars.Count);
        Assert.Equal(1.0, bars[0].Target, 6);
        Assert.Equal(24 / 100.74, bars[1].Target, 4);
        Assert.True(bars[0].IsAccent);
        Assert.False(bars[1].IsAccent);
        Assert.Equal(0, bars[0].Shown, 3);
        var seen = new List<double>();
        for (var i = 0; i < 12; i++)
        {
            rig.Tick();
            seen.Add(bars[0].Shown);
        }
        Assert.Equal(seen.Order(), seen);
        Assert.InRange(seen[1], .01, .99);
        rig.Settle();
        Assert.Equal(bars[0].Target, bars[0].Shown, 6);
        Assert.Equal(bars[2].Target, bars[2].Shown, 6);
        Assert.Equal(0, rig.Frames.Pending);
        Assert.False(rig.View.IsMoving);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_plot_draws_its_marks_in_left_to_right_once_and_then_rests() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("chart bar x=\"a,b,c,d,e,f,g,h,i,j,k,l,m,n\" y=[5, 8, 6, 9, 7, 4, 8, 6, 5, 7, 9, 6, 8, 7]\n");
        rig.View.Complete();
        rig.Pump();
        var plot = rig.Chart().Plot!;
        Assert.Equal(0, plot.Grow(0, 0), 3);
        var ahead = false;
        double previous = 0;
        for (var i = 0; i < 30; i++)
        {
            rig.Tick();
            var first = plot.Grow(0, 0);
            var last = plot.Grow(0, 13);
            Assert.True(first >= previous - 1e-9);
            previous = first;
            if (first > last + .05) ahead = true;
            Assert.True(first >= last - 1e-9);
        }
        Assert.True(ahead);
        rig.Settle();
        Assert.Equal(1, plot.Grow(0, 13), 6);
        Assert.Equal(0, rig.Frames.Pending);
        rig.View.Append("");
        rig.Tick();
        Assert.Equal(1, plot.Grow(0, 0), 6);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_state_change_springs_every_mark_and_can_be_reversed_from_where_it_is() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(Slid, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var slider = rig.ViewOf<NthUiSliderView>().Slider;
        Assert.Equal([10.0, 20, 20], [plot.ShownValue(0, 0)!.Value, plot.ShownValue(0, 1)!.Value, plot.ShownValue(0, 2)!.Value]);
        rig.Key(slider, Key.End);
        Assert.Equal(40, rig.View.Runtime.Get("a").AsNumber);
        Assert.Equal(10, plot.ShownValue(0, 0)!.Value, 6);
        var seen = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            rig.Tick();
            seen.Add(plot.ShownValue(0, 0)!.Value);
        }
        Assert.Equal(seen.Order(), seen);
        Assert.InRange(seen[0], 10.01, 39.99);
        Assert.InRange(seen[5], seen[0], 39.99);
        Assert.Equal(20, plot.ShownValue(0, 1)!.Value, 6);
        var before = plot.ShownValue(0, 0)!.Value;
        rig.Key(slider, Key.Home);
        Assert.Equal(0, rig.View.Runtime.Get("a").AsNumber);
        rig.Tick();
        var after = plot.ShownValue(0, 0)!.Value;
        Assert.InRange(Math.Abs(after - before), 0, 6);
        var turned = new List<double> { after };
        for (var i = 0; i < 30; i++)
        {
            rig.Tick();
            turned.Add(plot.ShownValue(0, 0)!.Value);
        }
        Assert.True(turned[^1] < turned[0]);
        rig.Settle();
        Assert.Equal(0, plot.ShownValue(0, 0)!.Value, 6);
        Assert.Equal(0, plot.ShownValue(0, 2)!.Value, 6);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_axis_range_springs_and_the_old_gridlines_fade_out_as_the_new_ones_fade_in() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(Slid, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var slider = rig.ViewOf<NthUiSliderView>().Slider;
        Assert.Equal((0.0, 20.0), plot.ShownRange);
        Assert.Equal([0.0, 5, 10, 15, 20], plot.ShownTicks.Select(t => t.Value));
        rig.Key(slider, Key.End);
        rig.Tick();
        var (_, hi) = plot.ShownRange;
        Assert.InRange(hi, 20.01, 79.99);
        var ticks = plot.ShownTicks;
        var leaving = ticks.Single(t => t.Value == 5);
        var arriving = ticks.Single(t => t.Value == 40);
        Assert.InRange(leaving.Alpha, .01, .99);
        Assert.InRange(arriving.Alpha, .01, .99);
        Assert.Equal(1, ticks.Single(t => t.Value == 20).Alpha, 3);
        Assert.Equal(1, ticks.Single(t => t.Value == 0).Alpha, 3);
        rig.Run(4 * NthUiRig.Frame);
        Assert.True(plot.ShownTicks.Single(t => t.Value == 40).Alpha > arriving.Alpha);
        Assert.True(plot.ShownTicks.Single(t => t.Value == 5).Alpha < leaving.Alpha);
        rig.Settle();
        Assert.Equal((0.0, 80.0), plot.ShownRange);
        Assert.Equal([0.0, 20, 40, 60, 80], plot.ShownTicks.Select(t => t.Value));
        Assert.All(plot.ShownTicks, t => Assert.Equal(1, t.Alpha, 3));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_pie_swings_round_to_its_new_cut_instead_of_jumping() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show("card \"P\"\n  state a = 1\n  slider a label=\"A\" min=0 max=10 step=1\n  chart pie x=\"A,B\" y=[a, 10 - a]\n", culture: NthUiRig.English));
        rig.Settle();
        var pie = rig.Chart().Pie!;
        Assert.Equal(.1, pie.ShownShare(0), 6);
        rig.Key(rig.ViewOf<NthUiSliderView>().Slider, Key.End);
        Assert.Equal(10, rig.View.Runtime.Get("a").AsNumber);
        rig.Tick();
        var seen = new List<double> { pie.ShownShare(0) };
        for (var i = 0; i < 8; i++)
        {
            rig.Tick();
            seen.Add(pie.ShownShare(0));
        }
        Assert.Equal(seen.Order(), seen);
        Assert.InRange(seen[0], .1001, .9999);
        rig.Settle();
        Assert.Equal(1, pie.ShownShare(0), 6);
        Assert.Equal(0, pie.ShownShare(1), 6);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_ring_of_a_pie_is_swept_on_from_the_top_once() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append(NthUiSamples.Pie);
        rig.View.Complete();
        rig.Pump();
        var pie = rig.Chart().Pie!;
        Assert.Equal(0, pie.Sweep, 3);
        rig.Run(5 * NthUiRig.Frame);
        Assert.InRange(pie.Sweep, .05, .95);
        rig.Settle();
        Assert.Equal(1, pie.Sweep, 6);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Reduce_Motion_keeps_a_short_fade_and_every_value_changes_with_a_tenth_of_a_second_ease() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(reduceMotion: true);
        rig.View.Append(Slid);
        rig.View.Complete();
        rig.Pump();
        var settle = 0;
        while ((rig.View.IsMoving || rig.Frames.Pending > 0) && settle < 120)
        {
            rig.Tick();
            settle++;
        }
        Assert.InRange(settle, 1, 24);
        var plot = rig.Chart().Plot!;
        var slider = rig.ViewOf<NthUiSliderView>().Slider;
        rig.Key(slider, Key.End);
        var frames = 0;
        while ((rig.View.IsMoving || rig.Frames.Pending > 0) && frames < 120)
        {
            rig.Tick();
            frames++;
        }
        Assert.InRange(frames, 1, 14);
        Assert.Equal(40, plot.ShownValue(0, 0)!.Value, 6);
        Assert.Equal((0.0, 80.0), plot.ShownRange);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_view_shown_whole_has_every_chart_at_rest_and_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        // A text line at the end of a sample is what asks the view for a frame (its pending-line watch), not a chart.
        static string OnlyCharts(string sample) => string.Join('\n', sample.Split('\n').Where(l => !l.StartsWith("text", StringComparison.Ordinal))) + "\n";
        foreach (var sample in new[] { NthUiSamples.CanvasHosting, NthUiSamples.EditableChart, NthUiSamples.Pie, OnlyCharts(NthUiSamples.Scatter), OnlyCharts(NthUiSamples.Area) })
        {
            using var rig = new NthUiRig(NthUiView.Show(sample, culture: NthUiRig.English));
            Assert.Equal(0, rig.Frames.Pending);
            Assert.False(rig.View.IsMoving);
            var chart = rig.Chart();
            if (chart.Plot is { } plot)
            {
                Assert.Equal(1, plot.Grow(0, 0), 6);
                Assert.All(plot.ShownTicks, t => Assert.Equal(1, t.Alpha, 3));
            }
            if (chart.Pie is { } pie) Assert.Equal(1, pie.Sweep, 6);
            if (chart.Bars is { } bars) Assert.All(bars.Bars, b => Assert.Equal(b.Target, b.Shown, 6));
        }
    });
}
