using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 10 "charts carry a text summary and an 'Show as table' alternative" (web surfaces W16b; ED-NEW-web-w16b-1): a
// quiet button under every chart swaps the drawing for a table of its numbers (the height follows by spring and the table
// fades in) and back; the table keeps up with the data while it is shown; a chart is a named group whose description is the
// text summary, and its drawing, key and table are named too.
[Collection("Headless")]
public sealed class NthUiChartTableTests(HeadlessSession headless)
{
    private static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Show_as_table_swaps_the_drawing_for_the_numbers_and_back_keeping_the_same_drawing() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        rig.Settle();
        var chart = rig.Chart();
        var plot = chart.Plot!;
        var button = chart.TableButton;
        Assert.Equal("Show as table", AutomationProperties.GetName(button));
        Assert.Contains("Show as table", rig.Texts());
        Assert.False(chart.IsTableShown);
        Assert.True(plot.IsVisible);
        rig.Click(button);
        rig.Settle();
        Assert.True(chart.IsTableShown);
        Assert.False(plot.IsVisible);
        Assert.True(chart.Table.Control.IsVisible);
        Assert.Equal("Show as chart", AutomationProperties.GetName(button));
        Assert.Contains("Show as chart", rig.Texts());
        Assert.DoesNotContain("Show as table", rig.Texts());
        Assert.Equal(["Point", "Value", "Jan", "12 units", "Feb", "18 units", "Mar", "25 units", "Apr", "30 units", "May", "28 units", "Jun", "35 units"], chart.Table.Texts);
        Assert.Equal(NthUiChartRows.Build(chart.Model, NthUiRig.English).Text, chart.Table.Data!.Text);
        rig.Click(button);
        rig.Settle();
        Assert.False(chart.IsTableShown);
        Assert.True(plot.IsVisible);
        Assert.Same(plot, rig.Chart().Plot);
        Assert.False(chart.Table.Control.IsVisible);
        Assert.Equal("Show as table", AutomationProperties.GetName(button));
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_height_follows_by_spring_and_the_table_fades_in_where_it_sits() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Pie, culture: NthUiRig.English));
        rig.Settle();
        var chart = rig.Chart();
        var slot = rig.View.SlotFor(rig.Node("chart").Key)!;
        var start = slot.Bounds.Height;
        rig.Click(chart.TableButton);
        rig.Tick();
        var end = slot.ContentHeight;
        Assert.True(Math.Abs(end - start) > 20, $"the table and the pie differ in height ({start} and {end})");
        Assert.InRange(chart.Table.Control.Opacity, 0, .99);
        var heights = new List<double> { slot.Bounds.Height };
        var fades = new List<double> { chart.Table.Control.Opacity };
        for (var i = 0; i < 8; i++)
        {
            rig.Tick();
            heights.Add(slot.Bounds.Height);
            fades.Add(chart.Table.Control.Opacity);
        }
        Assert.InRange(heights[0], Math.Min(start, end), Math.Max(start, end));
        Assert.Equal(end < start ? heights.OrderByDescending(h => h) : heights.Order(), heights);
        Assert.Contains(heights, h => h > Math.Min(start, end) + 1 && h < Math.Max(start, end) - 1);
        Assert.All(heights.Zip(heights.Skip(1)), pair => Assert.InRange(Math.Abs(pair.Second - pair.First), 0, 12));
        Assert.Equal(fades.Order(), fades);
        Assert.True(fades[^1] > fades[0]);
        rig.Settle();
        Assert.Equal(end, slot.Bounds.Height, 1);
        Assert.Equal(1, chart.Table.Control.Opacity, 3);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_table_keeps_up_with_the_data_while_it_is_shown_and_reuses_its_cells() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Pie, culture: NthUiRig.English));
        rig.Settle();
        var chart = rig.Chart();
        rig.Click(chart.TableButton);
        rig.Settle();
        Assert.Equal(["Slice", "Value", "Share", "Needs", "2100", "50%", "Wants", "1260", "30%", "Savings", "840", "20%"], chart.Table.Texts);
        var cell = chart.Table.Control.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "2100");
        rig.ViewOf<NthUiStepperView>().Stepper.Press(1);
        rig.Settle();
        Assert.Equal(["Slice", "Value", "Share", "Needs", "2150", "50%", "Wants", "1290", "30%", "Savings", "860", "20%"], chart.Table.Texts);
        Assert.Equal("2150", cell.Text);
        Assert.Equal("Needs, Value 2150, Share 50%", AutomationProperties.GetName(chart.Table.Control.GetVisualDescendants().OfType<Control>().First(c => AutomationProperties.GetControlTypeOverride(c) == AutomationControlType.DataItem)));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_table_stays_shown_when_the_chart_is_redrawn_as_another_kind_of_drawing() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("chart bar x=\"a,b,c\" y=[1, 2, 3]\n");
        rig.Settle();
        var chart = rig.Chart();
        Assert.NotNull(chart.Bars);
        var button = chart.TableButton;
        rig.Click(button);
        rig.Settle();
        Assert.True(chart.IsTableShown);
        rig.View.Append("  series \"B\" y=[3, 2, 1]\n");
        rig.Settle();
        Assert.Null(chart.Bars);
        var plot = Assert.IsType<NthUiChartPlot>(chart.Plot);
        Assert.True(chart.IsTableShown);
        Assert.False(plot.IsVisible);
        Assert.Same(button, rig.Chart().TableButton);
        Assert.Equal(["Item", "Series 1", "B", "a", "1", "3", "b", "2", "2", "c", "3", "1"], chart.Table.Texts);
        rig.Click(button);
        rig.Settle();
        Assert.True(plot.IsVisible);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_chart_is_a_named_group_described_by_its_summary_and_its_parts_are_named() => headless.OnUiThread(() =>
    {
        foreach (var sample in new[] { NthUiSamples.EditableChart, NthUiSamples.Pie, NthUiSamples.Scatter, NthUiSamples.Area, NthUiSamples.CanvasHosting })
        {
            using var rig = new NthUiRig(NthUiView.Show(sample, culture: NthUiRig.English));
            var chart = rig.Chart();
            var title = chart.Model.Data.Title is { Length: > 0 } t ? t : null;
            var group = Peer(chart.Control);
            Assert.Equal(AutomationControlType.Group, group.GetAutomationControlType());
            Assert.Equal(title ?? "Chart", group.GetName());
            Assert.Equal(chart.Summary, group.GetHelpText());
            var kind = chart.Model.Kind switch
            {
                NthUiChartKind.Line => "Line chart",
                NthUiChartKind.Area => "Area chart",
                NthUiChartKind.Pie => "Pie chart",
                NthUiChartKind.Scatter => "Scatter chart",
                _ => "Bar chart",
            };
            Assert.StartsWith(kind, chart.Summary, StringComparison.Ordinal);
            if (chart.Plot is { } plot)
            {
                Assert.Equal(chart.Summary, Peer(plot).GetHelpText());
                Assert.Equal(title ?? "Chart", Peer(plot).GetName());
                Assert.Equal(chart.Model.Editable ? AutomationControlType.Group : AutomationControlType.Image, Peer(plot).GetAutomationControlType());
            }
            if (chart.Pie is { } pie)
            {
                Assert.Equal(chart.Summary, Peer(pie).GetHelpText());
                Assert.Equal(title ?? "Pie chart", Peer(pie).GetName());
                Assert.Equal(AutomationControlType.Image, Peer(pie).GetAutomationControlType());
            }
            if (chart.Bars is { } bars)
            {
                Assert.Equal(chart.Summary, Peer(bars).GetHelpText());
                Assert.Equal(title ?? "Bar chart", Peer(bars).GetName());
            }
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(Peer(chart.TableButton));
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            Assert.Equal("Show as table", Peer(chart.TableButton).GetName());
            Assert.Equal(AutomationControlType.Button, Peer(chart.TableButton).GetAutomationControlType());
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_key_names_each_series_and_each_slice_and_the_table_exposes_headers_and_rows() => headless.OnUiThread(() =>
    {
        using var area = new NthUiRig(NthUiView.Show(NthUiSamples.Area, culture: NthUiRig.English));
        var key = area.Chart().Key;
        Assert.True(key.IsVisible);
        Assert.Equal(["Web", "Mobile"], key.Names);
        Assert.Equal(AutomationControlType.List, AutomationProperties.GetControlTypeOverride(key));
        Assert.Equal(["Web", "Mobile"], key.Children.Select(AutomationProperties.GetName));
        using var pie = new NthUiRig(NthUiView.Show(NthUiSamples.Pie, culture: NthUiRig.English));
        var rows = pie.Chart().PieKey!;
        Assert.Equal([("Needs", "2100", "50%"), ("Wants", "1260", "30%"), ("Savings", "840", "20%")], rows.Shown);
        Assert.Equal(["Needs 50% (2100)", "Wants 30% (1260)", "Savings 20% (840)"], rows.Children.Select(AutomationProperties.GetName));
        Assert.All(rows.Children, row => Assert.Equal(AutomationControlType.ListItem, AutomationProperties.GetControlTypeOverride(row)));
        var chart = pie.Chart();
        pie.Click(chart.TableButton);
        pie.Settle();
        var table = chart.Table.Control;
        Assert.Equal(AutomationControlType.Table, AutomationProperties.GetControlTypeOverride(table));
        Assert.Equal(["Slice", "Value", "Share"], table.GetVisualDescendants().OfType<TextBlock>().Where(AutomationProperties.GetIsColumnHeader).Select(t => t.Text));
        Assert.Equal(3, table.GetVisualDescendants().OfType<Control>().Count(c => AutomationProperties.GetControlTypeOverride(c) == AutomationControlType.DataItem));
        using var single = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        Assert.False(single.Chart().Key.IsVisible);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Every_control_a_chart_can_focus_has_a_name() => headless.OnUiThread(() =>
    {
        foreach (var sample in new[] { NthUiSamples.EditableChart, NthUiSamples.Pie, NthUiSamples.Scatter, NthUiSamples.Area, NthUiSamples.CanvasHosting })
        {
            using var rig = new NthUiRig(NthUiView.Show(sample, culture: NthUiRig.English));
            var chart = rig.Chart();
            var focusable = chart.Control.GetVisualDescendants().OfType<Control>().Where(c => c.Focusable && c.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(focusable);
            Assert.All(focusable, c => Assert.False(string.IsNullOrWhiteSpace(Peer(c).GetName()), $"{c.GetType().Name} has no name"));
        }
    });
}
