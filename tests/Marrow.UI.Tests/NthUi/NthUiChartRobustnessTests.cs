using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.2 ("Unknown or invalid lines degrade to their text"), 7.5 and 10 (web surfaces W16b; ED-NEW-web-w16b-1): a chart
// draws whatever it is given without throwing and without a number that is not a number reaching the screen: one point, no
// points, gaps, negatives, enormous and tiny values, a scatter of equal points, a pie of nothing; at any width from a sliver to
// a wide pane; and it follows a theme switch without being rebuilt.
[Collection("Headless")]
public sealed class NthUiChartRobustnessTests(HeadlessSession headless)
{
    private static readonly string[] Charts =
    [
        "chart line y=[5]\n",
        "chart bar y=[]\n",
        "chart line y=[1, \"x\", 3, \"y\", 5]\n",
        "chart bar y=[-5, -3, -8]\n",
        "chart line y=[1e300, -1e300, 5]\n",
        "chart line y=[0.0000001, 0.0000002, 0.0000003]\n",
        "chart scatter x=[3, 3, 3] y=[2, 2, 2]\n",
        "chart scatter x=[1, 2] y=[5, 5]\n",
        "chart pie x=\"A,B\" y=[0, 0]\n",
        "chart pie x=\"A,B,C\" y=[-1, 5, \"x\"]\n",
        "chart pie y=[1]\n",
        "chart area series=\"A,B\" y=[[1, -2, 3], [-1, 2, -3]] stacked\n",
        "chart bar series=\"A,B,C,D,E,F\" y=[[1, 2], [2, 3], [3, 4], [4, 5], [5, 6], [6, 7]] stacked\n",
        "chart bar x=\"A very long label indeed,Another extremely long label,Short\" y=[10, 20, 30]\n",
        "chart line y=[1, 2, 3] min=5 max=5\n",
        "chart line y=[1, 2, 3] min=10 max=0\n",
        "chart bar x=\"a,b,c\" y=[1, 2, 3] unit=\"a very long unit name\"\n",
        "chart line title=\"A very long title that goes on and on and on for far too long to fit on one line of any chart\" y=[1, 2, 3]\n",
        "card \"E\"\n  state v = []\n  chart line editable=points bind=v\n",
        "card \"E\"\n  state v = [1]\n  chart bar editable=points bind=v\n",
        "chart scatter\n  series \"A\" x=[1, 2, 3] y=[1, 2]\n  series \"B\" y=[4, 5, 6]\n",
    ];

    private static void Paint(NthUiRig rig)
    {
        rig.Settle();
        var chart = rig.Chart();
        foreach (var control in new Avalonia.Controls.Control?[] { chart.Plot, chart.Pie, chart.Bars })
        {
            if (control is null) continue;
            using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)control.Bounds.Width), Math.Max(1, (int)control.Bounds.Height)));
            bitmap.Render(control);
        }
        Assert.False(string.IsNullOrEmpty(chart.Summary));
        if (chart.Plot is { } plot)
        {
            var area = plot.PlotArea;
            Assert.True(double.IsFinite(area.Width) && double.IsFinite(area.Height) && area.Width > 0 && area.Height > 0, $"plot area {area}");
            var (lo, hi) = plot.ShownRange;
            Assert.True(double.IsFinite(lo) && double.IsFinite(hi) && hi > lo, $"range {lo} {hi}");
            foreach (var tick in plot.ShownTicks) Assert.True(double.IsFinite(tick.Value));
            for (var s = 0; s < plot.Model.Series.Count; s++)
            {
                for (var i = 0; i < plot.Model.Series[s].Values.Count; i++)
                {
                    if (plot.PointOf(s, i) is { } point) Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y), $"point {s},{i} {point}");
                }
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Odd_data_is_drawn_without_throwing_and_without_a_number_that_is_not_a_number() => headless.OnUiThread(() =>
    {
        foreach (var text in Charts)
        {
            using var shown = new NthUiRig(NthUiView.Show(text, culture: NthUiRig.English));
            Paint(shown);
            using var streamed = new NthUiRig();
            streamed.Stream(text, 5);
            streamed.View.Complete();
            Paint(streamed);
            streamed.Click(streamed.Chart().TableButton);
            streamed.Settle();
            Assert.True(streamed.Chart().IsTableShown);
        }
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [InlineData(60)]
    [InlineData(140)]
    [InlineData(260)]
    [InlineData(420)]
    [InlineData(900)]
    public void A_chart_draws_at_any_width_from_a_sliver_to_a_wide_pane(int width) => headless.OnUiThread(() =>
    {
        foreach (var sample in new[] { NthUiSamples.CanvasHosting, NthUiSamples.EditableChart, NthUiSamples.Pie, NthUiSamples.Scatter, NthUiSamples.Area, NthUiSamples.Mortgage })
        {
            using var rig = new NthUiRig(NthUiView.Show(sample, culture: NthUiRig.English), width: width + 32);
            Paint(rig);
            var chart = rig.Chart();
            Assert.InRange(chart.Control.Bounds.Width, 0, width + 1);
            if (chart.Plot is { } plot) Assert.InRange(plot.Bounds.Right, 0, width + .5);
            if (chart.Pie is { } pie) Assert.True(pie.Bounds.Width <= Math.Min(NthUiPieChart.Diameter, width) + .5);
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_chart_follows_a_theme_switch_without_being_rebuilt_and_draws_in_the_new_ink() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var handle = plot.Handles[0];
        var from = plot.PointOf(0, 0)!.Value;
        var to = plot.PointOf(0, 1)!.Value;
        var ring = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        var empty = new Point(plot.PlotArea.Left + 12, plot.HeightOf(50));
        double Lightness(RenderedPixels pixels)
        {
            double Of(Point point, int dx, int dy)
            {
                var p = rig.InWindow(plot, point);
                var (r, g, b) = pixels.At((int)Math.Round(p.X) + dx, (int)Math.Round(p.Y) + dy);
                return NthUiTokens.Luminance(Avalonia.Media.Color.FromRgb((byte)r, (byte)g, (byte)b));
            }
            var ground = Of(empty, 0, 0);
            var far = ground;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var l = Of(ring, dx, dy);
                    if (Math.Abs(l - ground) > Math.Abs(far - ground)) far = l;
                }
            }
            return far;
        }
        var light = Lightness(rig.Capture());
        Assert.True(light < .2, $"the line is dark ink on the light card ({light:0.00})");
        rig.Window.RequestedThemeVariant = ThemeVariant.Dark;
        rig.Pump();
        rig.Settle();
        Assert.Same(plot, rig.Chart().Plot);
        Assert.Same(handle, plot.Handles[0]);
        var dark = Lightness(rig.Capture());
        Assert.True(dark > .5, $"the line is light ink in the dark theme, not the old dark colour ({dark:0.00})");
    });
}
