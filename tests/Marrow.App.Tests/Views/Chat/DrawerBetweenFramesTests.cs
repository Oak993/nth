using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class DrawerBetweenFramesTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-NEW-motion-drawer-3")]
    public void A_reversal_before_the_first_frame_keeps_a_zero_distance_impulse_on_every_partner(bool reopening)
        => ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new DrawerMotionRig(PanelDockCorner.BottomLeading);
            rig.Toggle(true);
            if (reopening) { rig.Frame(1); rig.Toggle(false); }
            var before = rig.Margin;
            rig.AdvanceWithoutFrame(.01);
            rig.Toggle(reopening);
            Assert.Equal(before, rig.Margin);
            rig.Frame(.01);
            Assert.True(reopening ? rig.Margin < before : rig.Margin > before);
            Assert.Equal(-rig.Margin, rig.Terminal.RenderTransform!.Value.M32, 6);
            Assert.Equal(rig.ComposerBottom, rig.TerminalTop, tolerance: 1);
            Assert.Equal(rig.Gap, rig.ComposerBottom - rig.PanelBottom, tolerance: 1);
        });

    [Fact]
    [Trait("Decision", "ED-NEW-motion-drawer-3")]
    public void A_double_toggle_during_a_closing_overshoot_keeps_all_partners_speed()
        => ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new DrawerMotionRig(PanelDockCorner.BottomLeading);
            rig.Toggle(true); rig.Frame(1);
            var height = rig.Terminal.Bounds.Height;
            rig.Toggle(false);
            var seconds = Enumerable.Range(1, 100).Select(i => i / 144d)
                .First(t => Chrome.PanelSlide.Progress(t) > 1.0001 && Math.Abs(Chrome.PanelSlide.Speed(t)) > .01);
            rig.Frame(seconds);
            Assert.True(rig.Margin < -height);
            var before = rig.Margin;
            rig.Toggle(true); rig.Toggle(false);
            Assert.Equal(before, rig.Margin, 9);
            rig.Frame(.01);
            Assert.Equal(-rig.Margin, rig.Terminal.RenderTransform!.Value.M32, 6);
            Assert.Equal(rig.ComposerBottom, rig.TerminalTop, tolerance: 1);
            Assert.Equal(rig.Gap, rig.ComposerBottom - rig.PanelBottom, tolerance: 1);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-NEW-motion-drawer-3")]
    public void A_drawer_retarget_between_frames_keeps_the_last_presented_point_for_every_partner(bool reopening)
        => ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new DrawerMotionRig(PanelDockCorner.BottomLeading);
            rig.Toggle(true);
            if (reopening) { rig.Frame(1); rig.Toggle(false); }
            rig.Frame(.03);
            var pane = rig.Margin;
            var top = rig.TerminalTop;
            var panel = rig.PanelBottom;
            rig.AdvanceWithoutFrame(.01);
            rig.Toggle(reopening);
            Assert.Equal(pane, rig.Margin, 9);
            Assert.Equal(top, rig.TerminalTop, tolerance: 1);
            Assert.Equal(panel, rig.PanelBottom, tolerance: 1);
            Assert.Equal(rig.ComposerBottom, rig.TerminalTop, tolerance: 1);
            rig.Frame(.001);
            Assert.Equal(rig.ComposerBottom, rig.TerminalTop, tolerance: 1);
            Assert.Equal(rig.Gap, rig.ComposerBottom - rig.PanelBottom, tolerance: 1);
            // Another reversal before any frame also preserves the carried speed and joint position.
            var reached = rig.Margin;
            rig.Toggle(!reopening);
            rig.Toggle(reopening);
            rig.Frame(.001);
            Assert.True(reopening ? rig.Margin < reached : rig.Margin > reached);
            Assert.Equal(rig.ComposerBottom, rig.TerminalTop, tolerance: 1);
            Assert.Equal(rig.Gap, rig.ComposerBottom - rig.PanelBottom, tolerance: 1);
        });
}
