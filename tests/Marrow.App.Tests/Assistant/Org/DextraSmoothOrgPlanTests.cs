using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-5")]
public sealed class DextraSmoothOrgPlanTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(500)]
    [InlineData(900)]
    public void Unchanged_node_extents_reuse_the_plan_and_lines_across_repeated_layout(double width) => headless.OnUiThread(() =>
    {
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var chart = new OrgChart(); chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB, Store), [], []));
        using var stage = new HomeStage(chart, width, 900);
        var plan = chart.Panel.Plan;
        Assert.NotNull(plan);
        for (var i = 0; i < 20; i++)
        {
            chart.Panel.InvalidateMeasure(); chart.Panel.InvalidateArrange(); stage.Layout();
            Assert.Same(plan, chart.Panel.Plan);
            Assert.Same(plan, chart.Panel.Connectors.Plan);
        }
        stage.Resize(width == 500 ? 900 : 500, 900);
        Assert.NotSame(plan, chart.Panel.Plan);
        Assert.Equal(width == 500 ? OrgLayoutMode.Chart : OrgLayoutMode.List, chart.Panel.Plan!.Mode);
        chart.Show(TeamSnapshot.Build(Team(ChannelA), [], [])); stage.Layout();
        Assert.Single(chart.Panel.Plan.Kids);
    });
}
