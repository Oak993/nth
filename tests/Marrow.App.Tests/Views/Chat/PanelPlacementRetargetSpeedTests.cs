using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Chrome = Marrow.UI.Chrome.Chrome;

// Traceability uses the single-word motiondrawer alias for ledger ED-NEW-motion-drawer-N.
namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class PanelPlacementRetargetSpeedTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-motiondrawer-1")]
    public void A_panel_replaced_mid_settle_carries_position_and_size_speed_on_one_clock()
        => ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new DrawerMotionRig(PanelDockCorner.BottomLeading);
            using var runtime = new TestRuntime();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var placement = new PlacedPanel(new PanelDragState(), new Point(10, 20), new Size(100, 200), new Border());
            var holder = new Panel { Children = { placement } };
            rig.Window.Content = holder;
            var part = (PanelPlacementView)placement.Child!;
            placement.UpdatePlacement(new Point(110, 220), new Size(300, 400));
            runtime.AdvanceMilliseconds(50); frames.Advance(TimeSpan.Zero);
            var p = Chrome.PanelSlide.Progress(0.05);
            var v = Chrome.PanelSlide.Speed(0.05);
            var x = 10 + 100 * p;
            var y = 20 + 200 * p;
            var width = 100 + 200 * p;
            var height = 200 + 200 * p;
            placement.UpdatePlacement(new Point(10, 20), new Size(100, 200));
            Assert.Equal(x, part.RenderTransform!.Value.M31, 6);
            Assert.Equal(y, part.RenderTransform.Value.M32, 6);
            runtime.AdvanceMilliseconds(1); frames.Advance(TimeSpan.Zero);
            var progress = Chrome.PanelSlide.Progress(0.001);
            var impulse = Chrome.PanelSlide.Impulse(0.001);
            Assert.Equal(x + (10 - x) * progress + 100 * v * impulse, part.RenderTransform!.Value.M31, 6);
            Assert.Equal(y + (20 - y) * progress + 200 * v * impulse, part.RenderTransform.Value.M32, 6);
            Assert.Equal(width + (100 - width) * progress + 200 * v * impulse, part.Width, 6);
            Assert.Equal(height + (200 - height) * progress + 200 * v * impulse, part.Height, 6);
            runtime.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.Zero);
            Assert.Equal(new Point(10, 20), new Point(part.RenderTransform.Value.M31, part.RenderTransform.Value.M32));
            Assert.Equal(100, part.Width);
            Assert.Equal(200, part.Height);
        });
}
