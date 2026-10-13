using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Terminal;
using Marrow.UI.Port;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

// The owner's CU-MOTION-DRAWER order closes ED-NEW-perf-motion-11 (3): all partners carry speed, on one clock.
[Collection("Headless")]
public sealed class DrawerRetargetSpeedTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(60, false)]
    [InlineData(144, false)]
    [InlineData(60, true)]
    [InlineData(144, true)]
    [Trait("Decision", "ED-NEW-motion-drawer-1")]
    public void Repeated_drawer_reversals_carry_speed_and_keep_the_drawer_composer_and_bottom_panel_together(int hertz, bool trailing)
        => ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new DrawerMotionRig(trailing ? PanelDockCorner.BottomTrailing : PanelDockCorner.BottomLeading);
            var curve = Chrome.PanelSlide;
            var frame = TimeSpan.FromSeconds(1.0 / hertz).TotalSeconds; // the injected clock's exact ticks
            rig.Toggle(true);
            var terminal = rig.Terminal;
            var height = terminal.Bounds.Height;
            var from = -height;
            var target = 0d;
            var speed = 0d;
            var elapsed = 0d;
            // Four toggles, before each segment can land; the impulse from the preceding one is carried again.
            foreach (var visible in new[] { false, true, false, true })
            {
                var stepCount = hertz == 60 ? 3 : 7;
                for (var i = 0; i < stepCount; i++) { rig.Frame(frame); elapsed += frame; Together(rig); }
                var reached = from + (target - from) * curve.Progress(elapsed) + speed * curve.Impulse(elapsed);
                var carriedCurve = curve.WithStartSpeed(speed / (target - from));
                speed = (target - from) * carriedCurve.Speed(elapsed);
                Assert.Equal(reached, rig.Margin, 3);
                rig.Toggle(visible);
                Assert.Same(terminal, rig.Terminal);
                Assert.Equal(reached, rig.Margin, 3); // no jump at the press
                Together(rig);
                from = reached;
                target = visible ? 0 : -height;
                rig.Frame(frame);
                var carried = from + (target - from) * curve.Progress(frame) + speed * curve.Impulse(frame);
                var atRest = from + (target - from) * curve.Progress(frame);
                Assert.True(Math.Abs(carried - atRest) > 0.5, $"speed must count here: carried {carried}, from rest {atRest}");
                Assert.Equal(carried, rig.Margin, 3);
                Together(rig);
                elapsed = frame;
            }
            Land(rig, frame, target);
            rig.Toggle(false);
            rig.Frame(0.05);
            rig.Toggle(true);
            rig.Frame(0.05);
            rig.Toggle(false); // a carried closing run must also land without a step or a cleanup jump
            Land(rig, frame, -height);
            rig.Toggle(true);
            rig.Frame(1);
            rig.Toggle(false);
            Land(rig, frame, -height);
            Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(rig.View));
            Assert.Equal(rig.Resting, rig.ComposerBottom, tolerance: 1);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-NEW-motion-drawer-1")]
    public void Top_docked_panels_stay_put_while_a_drawer_reverses_at_speed(bool trailing)
        => ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new DrawerMotionRig(trailing ? PanelDockCorner.TopTrailing : PanelDockCorner.TopLeading);
            var top = rig.PanelBottom;
            rig.Toggle(true);
            rig.Frame(0.05);
            var before = rig.Margin;
            rig.Toggle(false);
            rig.Frame(0.001);
            Assert.True(rig.Margin > before, "the drawer keeps moving upwards for a moment after the close");
            for (var i = 0; i < 40; i++) { rig.Frame(1 / 60d); Assert.Equal(top, rig.PanelBottom, tolerance: 1); }
        });

    [Fact]
    [Trait("Decision", "ED-NEW-motion-drawer-1")]
    public void Reduce_Motion_stills_the_drawer_and_all_its_partners_even_after_a_moving_retarget()
        => ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new DrawerMotionRig(PanelDockCorner.BottomLeading);
            rig.Toggle(true);
            rig.Frame(0.05);
            EnvironmentValues.SetAccessibilityReduceMotion(rig.View, true);
            rig.Toggle(false);
            rig.Frame(0.001);
            Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(rig.View));
            Assert.Equal(rig.Resting, rig.ComposerBottom, tolerance: 1);
            Assert.Equal(rig.Gap, rig.ComposerBottom - rig.PanelBottom, tolerance: 1);
            rig.Toggle(true);
            rig.Frame(0.001);
            Assert.Equal(0, rig.Margin);
            Together(rig);
        });

    private static void Together(DrawerMotionRig rig)
    {
        Assert.Equal(rig.ComposerBottom, rig.TerminalTop, tolerance: 1);
        Assert.Equal(rig.Gap, rig.ComposerBottom - rig.PanelBottom, tolerance: 1);
    }

    private static void Land(DrawerMotionRig rig, double frame, double target)
    {
        var values = new List<double> { rig.Margin };
        var panels = new List<double> { rig.RawPanelBottom };
        var lastComposer = rig.ComposerBottom;
        var lastTerminal = rig.TerminalTop;
        for (var i = 0; i < 100 && rig.Margin != target; i++)
        {
            rig.Frame(frame);
            values.Add(rig.Margin);
            panels.Add(rig.RawPanelBottom);
            if (LogicalQuery.SelfAndDescendants<TerminalPanel>(rig.View).Any()) Together(rig);
            else
            {
                Assert.Equal(lastTerminal, rig.ComposerBottom, tolerance: 1);
                Assert.Equal(lastComposer, rig.ComposerBottom, tolerance: 1);
                Assert.Equal(rig.Gap, rig.ComposerBottom - rig.PanelBottom, tolerance: 1);
                values[^1] = target;
                break;
            }
            lastComposer = rig.ComposerBottom;
            lastTerminal = rig.TerminalTop;
        }
        // A closing drawer drops its row and clears the margin at rest; that layout reset has no visible travel.
        if (target < 0 && !LogicalQuery.SelfAndDescendants<TerminalPanel>(rig.View).Any()) values[^1] = target;
        Assert.Equal(target, values[^1]);
        Assert.True(values.Count >= 3);
        var last = Math.Abs(values[^1] - values[^2]);
        var previous = Math.Abs(values[^2] - values[^3]);
        Assert.True(last <= previous + 1e-6, $"last step {last:E3} exceeds previous {previous:E3}");
        var panelLast = Math.Abs(panels[^1] - panels[^2]);
        var panelPrevious = Math.Abs(panels[^2] - panels[^3]);
        Assert.True(panelLast <= panelPrevious + 1e-6, $"panel last step {panelLast:E3} exceeds previous {panelPrevious:E3}");
    }
}
