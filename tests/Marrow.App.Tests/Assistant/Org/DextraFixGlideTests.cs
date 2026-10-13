using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Headless;
using Avalonia.Media;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-fix-10")]
public sealed class DextraFixGlideTests(HeadlessSession headless)
{
    [Fact]
    public void A_second_glide_captures_the_drawn_position_including_the_current_transform()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB), [], []));
            using var stage = new HomeStage(chart, 900, 700);
            chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB, Store), [], []));
            var node = chart.Kids[0];
            // Deterministic frame seam: the in-flight first glide currently draws 37 px right and 5 px up.
            node.RenderTransform = new TranslateTransform(37, -5);
            var drawn = node.TranslatePoint(default, chart.Panel)!.Value;
            var positions = (Dictionary<OrgNode, Point>)typeof(OrgChart).GetMethod("Positions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chart, null)!;
            Assert.Equal(drawn, positions[node]);
            Assert.NotEqual(node.Bounds.Position, positions[node]);
        });
    }
}
