using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
public sealed class DesignGapsGettingReadyTests(HeadlessSession headless)
{
    [Fact]
    public void Pane_saturation_follows_hi_and_is_restored_after_the_sheet_lifts() => headless.OnUiThread(() =>
    {
        using var platform = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        using var cursor = DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            using var rig = new GettingReadyRootRig(model);
            var layer = Assert.Single(rig.Root.DetailPane.GetVisualDescendants().OfType<SaturationLayer>());
            Assert.DoesNotContain(rig.Sheet, layer.GetVisualDescendants());
            Assert.Equal(.6, layer.Amount);
            rig.AllDone();
            _ = rig.Sheet.EndAsync(new Marrow.App.Port.FirstRun.FirstRunOutcome(Marrow.App.Port.FirstRun.FirstRunOutcomeKind.Prepared));
            var elapsed = 0.0;
            for (var i = 0; i <= 20; i++)
            {
                var target = .150 + 1.100 * i / 20;
                rig.Frame(target - elapsed); elapsed = target;
                var hand = GettingReadyCurves.HandOver(i / 20.0);
                Assert.Equal(.6 + .4 * Math.Clamp(hand / .56, 0, 1), layer.Amount, 5);
            }
            rig.Frame(.050);
            Assert.Null(rig.Root.GettingReady);
            Assert.Equal(1, layer.Amount);
            Assert.True(rig.Root.DetailPane.IsHitTestVisible);
        });
    });
}
