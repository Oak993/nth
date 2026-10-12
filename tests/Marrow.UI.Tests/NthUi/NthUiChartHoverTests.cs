using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.4 and 7.5 (web surfaces W16b; ED-NEW-web-w16b-1): the reader's attention is one moving focus. Hovering a column
// or a point shows a readout that fades in where it is and glides to the next column rather than jumping; a key walks a chart
// that cannot be edited with Left, Right, Up, Down, Home and End and the readout (and the item status a screen reader reads)
// follows; a pie lifts the slice under the pointer, and its key and the ring light up together.
[Collection("Headless")]
public sealed class NthUiChartHoverTests(HeadlessSession headless)
{
    private const string Visits = "chart line title=\"Visits\" x=\"Mon,Tue,Wed,Thu,Fri\" y=[4, 7, 5, 9, 6] unit=\"k\"\n";

    private static void Hover(NthUiRig rig, NthUiChartPlot plot, Point at) => rig.Window.MouseMove(rig.InWindow(plot, at), RawInputModifiers.None);

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Hovering_a_column_fades_in_a_readout_that_then_glides_to_the_next_column_and_leaving_fades_it_out() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(Visits, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        Assert.Null(plot.Readout);
        Hover(rig, plot, plot.PointOf(0, 1)!.Value);
        rig.Pump();
        Assert.Equal((0, 1), plot.Active);
        Assert.Equal("Tue: 7 k", plot.Readout!.Spoken);
        Assert.Equal("Tue: 7 k", AutomationProperties.GetItemStatus(plot));
        rig.Settle();
        var first = plot.ReadoutBounds!.Value;
        Assert.InRange(first.Center.X, plot.PointOf(0, 1)!.Value.X - 1, plot.PointOf(0, 1)!.Value.X + 1);
        Assert.True(first.Bottom < plot.PointOf(0, 1)!.Value.Y);

        Hover(rig, plot, plot.PointOf(0, 4)!.Value);
        rig.Pump();
        Assert.Equal("Fri: 6 k", plot.Readout!.Spoken);
        var xs = new List<double>();
        for (var i = 0; i < 40; i++)
        {
            rig.Tick();
            xs.Add(plot.ReadoutBounds!.Value.X);
        }
        Assert.InRange(xs[0], first.X + .1, plot.ReadoutBounds!.Value.X - .5);
        Assert.Equal(xs.Order(), xs);
        Assert.All(xs.Zip(xs.Skip(1)), pair => Assert.InRange(pair.Second - pair.First, 0, 40));
        var arrived = Math.Clamp(plot.PointOf(0, 4)!.Value.X - plot.ReadoutBounds!.Value.Width / 2, 0, plot.Bounds.Width - plot.ReadoutBounds!.Value.Width);
        Assert.Equal(arrived, plot.ReadoutBounds!.Value.X, 1);

        rig.Window.MouseMove(new Point(2000, 2000), RawInputModifiers.None);
        rig.Pump();
        Assert.Null(plot.Active);
        rig.Settle();
        Assert.Null(plot.ReadoutBounds);
        Assert.Null(plot.Readout);
        Assert.Equal("", AutomationProperties.GetItemStatus(plot));
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_readout_changing_its_mind_mid_glide_carries_on_from_where_it_is() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(Visits, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        Hover(rig, plot, plot.PointOf(0, 0)!.Value);
        rig.Settle();
        Hover(rig, plot, plot.PointOf(0, 4)!.Value);
        rig.Run(6 * NthUiRig.Frame);
        var mid = plot.ReadoutBounds!.Value.X;
        Assert.InRange(mid, plot.PointOf(0, 0)!.Value.X - 20, plot.PointOf(0, 4)!.Value.X);
        Hover(rig, plot, plot.PointOf(0, 0)!.Value);
        rig.Tick();
        Assert.InRange(Math.Abs(plot.ReadoutBounds!.Value.X - mid), 0, 25);
        rig.Settle();
        Assert.Equal(plot.PointOf(0, 0)!.Value.X - plot.ReadoutBounds!.Value.Width / 2, plot.ReadoutBounds!.Value.X, 1.5);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_chart_that_cannot_be_edited_is_one_tab_stop_that_walks_its_points_with_the_arrow_keys() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Area, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        Assert.True(plot.Focusable);
        Assert.Empty(plot.Handles);
        plot.Focus(NavigationMethod.Tab);
        rig.Pump();
        Assert.Equal((0, 0), plot.Active);
        Assert.Equal("Q1: Web 120, Mobile 80", plot.Readout!.Spoken);
        rig.Key(plot, Key.Right);
        Assert.Equal((0, 1), plot.Active);
        rig.Key(plot, Key.End);
        Assert.Equal((0, 3), plot.Active);
        rig.Key(plot, Key.Right);
        Assert.Equal((0, 3), plot.Active);
        rig.Key(plot, Key.Down);
        Assert.Equal((1, 3), plot.Active);
        rig.Key(plot, Key.Down);
        Assert.Equal((1, 3), plot.Active);
        rig.Key(plot, Key.Up);
        rig.Key(plot, Key.Home);
        Assert.Equal((0, 0), plot.Active);
        Assert.Equal(plot.Readout!.Spoken, AutomationProperties.GetItemStatus(plot));
        rig.Key(plot, Key.Escape);
        Assert.Null(plot.Active);
        rig.Settle();
        Assert.Null(plot.Readout);
        rig.Key(plot, Key.Right);
        Assert.Equal((0, 0), plot.Active);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_pointer_picks_the_nearest_series_in_a_column_and_the_nearest_point_of_a_scatter() => headless.OnUiThread(() =>
    {
        using var lines = new NthUiRig(NthUiView.Show("chart line series=\"A,B\" x=\"a,b,c\" y=[[10, 20, 30], [40, 35, 30]]\n", culture: NthUiRig.English));
        lines.Settle();
        var plot = lines.Chart().Plot!;
        var a = plot.PointOf(0, 0)!.Value;
        var b = plot.PointOf(1, 0)!.Value;
        Assert.Equal((0, 0), plot.Pick(new Point(a.X + 3, a.Y + 6)));
        Assert.Equal((1, 0), plot.Pick(new Point(b.X - 3, b.Y - 6)));
        Assert.Null(plot.Pick(new Point(1, 1)));
        Assert.Null(plot.Pick(new Point(plot.Bounds.Width + 30, 40)));
        using var scatter = new NthUiRig(NthUiView.Show(NthUiSamples.Scatter, culture: NthUiRig.English));
        scatter.Settle();
        var dots = scatter.Chart().Plot!;
        var tablet = dots.PointOf(1, 1)!.Value;
        Assert.Equal((1, 1), dots.Pick(new Point(tablet.X + 9, tablet.Y - 9)));
        Assert.Null(dots.Pick(new Point(tablet.X + 60, tablet.Y + 60)));
        Hover(scatter, dots, tablet);
        scatter.Pump();
        Assert.Equal("Tablets · 449: 4.3", dots.Readout!.Spoken);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_bar_under_the_pointer_takes_the_accent_and_the_others_stay_monochrome(string name) => headless.OnUiThread(() =>
    {
        var theme = name == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        using var rig = new NthUiRig(NthUiView.Show("chart bar x=\"a,b,c,d,e,f,g,h,i,j,k,l,m\" y=[5, 8, 6, 9, 7, 4, 8, 6, 5, 7, 9, 6, 8]\n", culture: NthUiRig.English), theme: theme);
        rig.Settle();
        var plot = rig.Chart().Plot!;
        Assert.Equal(NthUiChartLayout.Plot, plot.Model.Layout);
        var accent = NthUiTokens.Accent.Resolve(theme);
        var before = rig.Capture();
        Color At(RenderedPixels pixels, Point windowPoint)
        {
            var (r, g, b) = pixels.At((int)Math.Round(windowPoint.X), (int)Math.Round(windowPoint.Y));
            return Color.FromRgb((byte)r, (byte)g, (byte)b);
        }
        var top = plot.PointOf(0, 3)!.Value;
        var inside = rig.InWindow(plot, new Point(top.X, top.Y + 12));
        var rest = At(before, inside);
        Assert.True(Math.Abs(rest.R - accent.R) + Math.Abs(rest.B - accent.B) > 60, $"a bar at rest is monochrome, drew {rest}");
        Hover(rig, plot, top);
        rig.Settle();
        var lit = At(rig.Capture(), inside);
        Assert.True(Math.Abs(lit.R - accent.R) < 24 && Math.Abs(lit.G - accent.G) < 24 && Math.Abs(lit.B - accent.B) < 24, $"the hovered bar takes the accent {accent}, drew {lit}");
        var other = plot.PointOf(0, 6)!.Value;
        var neighbour = At(rig.Capture(), rig.InWindow(plot, new Point(other.X, other.Y + 12)));
        Assert.True(Math.Abs(neighbour.R - accent.R) + Math.Abs(neighbour.B - accent.B) > 60, $"another bar stays monochrome, drew {neighbour}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_pie_lifts_the_slice_under_the_pointer_and_its_key_row_lights_up_with_it() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Pie, culture: NthUiRig.English));
        rig.Settle();
        var chart = rig.Chart();
        var pie = chart.Pie!;
        var key = chart.PieKey!;
        var (center, outer, inner) = pie.Geometry;
        var middle = (outer + inner) / 2;
        Point On(double turn) => NthUiPieGeometry.At(center, middle, turn * 2 * Math.PI);
        Assert.Equal(0, pie.SliceAt(On(.25)));
        Assert.Equal(1, pie.SliceAt(On(.65)));
        Assert.Equal(2, pie.SliceAt(On(.9)));
        Assert.Null(pie.SliceAt(center));
        Assert.Null(pie.SliceAt(new Point(center.X + outer + 30, center.Y)));
        rig.Window.MouseMove(rig.InWindow(pie, On(.65)), RawInputModifiers.None);
        rig.Pump();
        Assert.Equal(1, pie.Active);
        Assert.Equal(0, pie.Pop(1), 3);
        rig.Settle();
        Assert.Equal(1, pie.Pop(1), 3);
        Assert.Equal(0, pie.Pop(0), 3);
        var row = key.Children[1];
        Assert.NotEqual(Avalonia.Media.Brushes.Transparent, ((Border)row).Background);
        Assert.Equal(Avalonia.Media.Brushes.Transparent, ((Border)key.Children[0]).Background);
        Assert.Equal("Wants: 1260 · 30%", AutomationProperties.GetItemStatus(pie));
        rig.Window.MouseMove(new Point(2000, 2000), RawInputModifiers.None);
        rig.Settle();
        Assert.Null(pie.Active);
        Assert.Equal(0, pie.Pop(1), 3);
        Assert.Equal("", AutomationProperties.GetItemStatus(pie));

        var third = (Border)key.Children[2];
        rig.Window.MouseMove(rig.InWindow(third, new Point(10, 10)), RawInputModifiers.None);
        rig.Settle();
        Assert.Equal(2, pie.Active);
        Assert.Equal(1, pie.Pop(2), 3);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_pie_is_one_tab_stop_that_walks_its_slices() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Pie, culture: NthUiRig.English));
        rig.Settle();
        var pie = rig.Chart().Pie!;
        pie.Focus(NavigationMethod.Tab);
        rig.Pump();
        Assert.Equal(0, pie.Active);
        rig.Key(pie, Key.Right);
        Assert.Equal(1, pie.Active);
        Assert.Equal("Wants: 1260 · 30%", AutomationProperties.GetItemStatus(pie));
        rig.Key(pie, Key.End);
        Assert.Equal(2, pie.Active);
        rig.Key(pie, Key.Down);
        Assert.Equal(2, pie.Active);
        rig.Key(pie, Key.Home);
        Assert.Equal(0, pie.Active);
        rig.Key(pie, Key.Up);
        Assert.Equal(0, pie.Active);
        rig.Key(pie, Key.Escape);
        Assert.Null(pie.Active);
    });
}
