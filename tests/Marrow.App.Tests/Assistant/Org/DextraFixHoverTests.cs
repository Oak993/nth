using Avalonia;
using Avalonia.Headless;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-fix-11")]
public sealed class DextraFixHoverTests(HeadlessSession headless)
{
    [Fact]
    public void The_bottom_strip_keeps_hover_while_the_face_is_lifted()
    {
        headless.OnUiThread(() =>
        {
            using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            var chart = new OrgChart();
            chart.Show(TeamSnapshot.Build(Team(ChannelA), [], []));
            using var stage = new HomeStage(chart, 900, 700);
            var node = chart.Kids[0];
            var box = stage.Box(node);
            for (var i = 0; i < 8; i++)
            {
                stage.Window.MouseMove(new Point(box.Center.X + i * .1, box.Bottom - 1));
                stage.Layout();
                Assert.Equal(-2, node.LiftNow);
            }
            stage.Window.MouseMove(new Point(2, 690));
            stage.Layout();
            Assert.Equal(0, node.LiftNow);
        });
    }
}
