using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 9.4; the owner's right-to-left readers; ED-NEW-dextra-as17-1): in a right-to-left window the whole tree is mirrored, so the chart reads from the right (the
// first Astur on the right, the list's trunk and indent on the right), with Dextra still centred, nothing overlapping and nothing outside the window.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-1")]
public sealed class OrgChartRtlTests(HeadlessSession headless)
{
    private static OrgChart Chart(params AssistantProfile[] asturs)
    {
        var chart = new OrgChart();
        chart.Show(TeamSnapshot.Build(Team(asturs), [], []));
        return chart;
    }

    // where a control really is on the screen (a mirrored visual's own origin is its right edge, so both corners are read)
    private static Rect Screen(HomeStage stage, Control control)
    {
        var a = control.TranslatePoint(default, stage.Window)!.Value;
        var b = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), stage.Window)!.Value;
        return new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }

    [Fact]
    public void In_a_right_to_left_window_the_first_Astur_is_on_the_right_and_Dextra_is_still_centred()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(ChannelA, ChannelB, Store);
            using var stage = new HomeStage(chart, 900, 700, FlowDirection.RightToLeft);

            var boxes = chart.Kids.Select(k => Screen(stage, k)).ToList();
            Assert.True(boxes[0].X > boxes[1].X && boxes[1].X > boxes[2].X, string.Join(" | ", boxes));
            Assert.Equal(450, Screen(stage, chart.Root!).Center.X, 1);
            Assert.All(chart.All, n => Assert.InRange(Screen(stage, n).X, -.1, 900.1 - Screen(stage, n).Width));
        });
    }

    [Fact]
    public void In_a_left_to_right_window_the_first_Astur_is_on_the_left()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(ChannelA, ChannelB, Store);
            using var stage = new HomeStage(chart, 900, 700);

            var boxes = chart.Kids.Select(k => Screen(stage, k)).ToList();
            Assert.True(boxes[0].X < boxes[1].X && boxes[1].X < boxes[2].X);
        });
    }

    [Fact]
    public void In_the_narrow_list_the_indent_is_on_the_right_in_a_right_to_left_window()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(ChannelA, ChannelB);
            using var stage = new HomeStage(chart, 375, 700, FlowDirection.RightToLeft);

            var kid = Screen(stage, chart.Kids[0]);
            Assert.Equal(345, kid.Width, 1);
            Assert.Equal(0, kid.X, 1);                                                             // the 30 px of trunk and indent are on the right
            Assert.Equal(375, Screen(stage, chart.Root!).Width, 1);
        });
    }

    [Fact]
    public void The_connectors_are_mirrored_with_the_nodes_so_they_still_join_them()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(ChannelA, ChannelB, Store);
            using var stage = new HomeStage(chart, 900, 700, FlowDirection.RightToLeft);

            // the lines are drawn in the chart's own coordinates, which the window mirrors as it mirrors the nodes: a line's end at a node's top centre is that node's top centre on screen
            var plan = chart.Panel.Plan!;
            var end = plan.Connectors.Select(c => c.Points[^1]).First(p => Math.Abs(p.X - plan.Kids[0].CenterX) < .1);
            var screenEnd = chart.Panel.Connectors.TranslatePoint(new Point(end.X, end.Y), stage.Window)!.Value;
            var node = Screen(stage, chart.Kids[0]);
            Assert.Equal(node.Center.X, screenEnd.X, 1);
            Assert.Equal(node.Y, screenEnd.Y, 1);
        });
    }
}
