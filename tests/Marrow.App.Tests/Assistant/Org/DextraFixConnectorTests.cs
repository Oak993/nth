using Avalonia.Animation;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-fix-12")]
public sealed class DextraFixConnectorTests(HeadlessSession headless)
{
    [Fact]
    public void Connectors_fade_out_instead_of_cutting_when_nodes_move()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(TeamSnapshot.Build(Team(ChannelA), [], []));
            using var stage = new HomeStage(chart, 900, 700);
            chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB), [], []));
            stage.Layout();
            var lines = chart.Panel.Connectors;
            Assert.NotNull(lines.Transitions);
            var fade = Assert.Single(lines.Transitions.OfType<DoubleTransition>());
            Assert.Equal(Avalonia.Visual.OpacityProperty, fade.Property);
            Assert.Equal(TimeSpan.FromMilliseconds(260), fade.Duration);
            Assert.Equal(0, lines.GetBaseValue(Avalonia.Visual.OpacityProperty).Value);
            stage.Advance(OrgChart.Move + TimeSpan.FromMilliseconds(20));
            Assert.Equal(1, lines.GetBaseValue(Avalonia.Visual.OpacityProperty).Value);
        });
    }

    [Fact]
    public void The_old_connector_geometry_is_kept_until_the_nodes_land_and_the_lines_have_faded_out()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB), [], []));
            using var stage = new HomeStage(chart, 900, 700);
            var before = chart.Panel.Connectors.Plan;
            chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB, Store), [], []));
            stage.Layout();
            Assert.Same(before, chart.Panel.Connectors.Plan);
            chart.Show(TeamSnapshot.Build(Team(Store, ChannelA, ChannelB), [], []));
            stage.Layout();
            Assert.Same(before, chart.Panel.Connectors.Plan);
            stage.Advance(OrgChart.Move + TimeSpan.FromMilliseconds(20));
            Assert.NotSame(before, chart.Panel.Connectors.Plan);
            Assert.Equal(3, chart.Panel.Connectors.Plan!.Kids.Count);
        });
    }

}
